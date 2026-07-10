from __future__ import annotations

import os
import shutil
import uuid
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from satl.catalog import sha256_file, verify_schema_file
from satl.errors import IntegrityError, TransactionError
from satl.models import SchemaVariant
from satl.state import StateStore


def utc_now() -> str:
    return datetime.now(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _copy_fsync(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_name(f".{destination.name}.{uuid.uuid4().hex}.tmp")
    try:
        with source.open("rb") as reader, temporary.open("xb") as writer:
            shutil.copyfileobj(reader, writer, length=1024 * 1024)
            writer.flush()
            os.fsync(writer.fileno())
        os.replace(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)


class TransactionManager:
    def __init__(self, data_dir: Path, store: StateStore | None = None) -> None:
        self.data_dir = Path(data_dir).resolve()
        self.store = store or StateStore(self.data_dir)

    def install(
        self,
        app_id: str,
        target: Path,
        source: Path,
        variant: SchemaVariant,
        *,
        dry_run: bool = False,
    ) -> dict[str, Any]:
        verify_schema_file(source, variant)
        target = Path(target).resolve()
        if target.name != f"UserGameStatsSchema_{app_id}.bin":
            raise TransactionError(f"目标文件名与 App ID 不匹配：{target}")
        if dry_run:
            return {"app_id": app_id, "action": "would-install", "target": str(target)}

        transaction_id = uuid.uuid4().hex
        backup_dir = self.data_dir / "backups" / app_id / transaction_id
        snapshot = backup_dir / "original.bin"
        stage = target.with_name(f".{target.name}.{transaction_id}.tmp")
        previous_exists = target.is_file()
        previous_sha256: str | None = None
        replaced = False
        try:
            if previous_exists:
                previous_sha256 = sha256_file(target)
                _copy_fsync(target, snapshot)
                if sha256_file(snapshot) != previous_sha256:
                    raise IntegrityError(f"安装前备份校验失败：{snapshot}")
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.open("rb") as reader, stage.open("xb") as writer:
                shutil.copyfileobj(reader, writer, length=1024 * 1024)
                writer.flush()
                os.fsync(writer.fileno())
            verify_schema_file(stage, variant)
            os.replace(stage, target)
            replaced = True
            transaction = {
                "id": transaction_id,
                "installed_at": utc_now(),
                "variant_id": variant.variant_id,
                "schema_file": variant.schema_file,
                "source_sha256": variant.sha256,
                "installed_sha256": variant.sha256,
                "target": str(target),
                "previous_exists": previous_exists,
                "previous_sha256": previous_sha256,
                "snapshot": self._relative(snapshot) if previous_exists else None,
            }
            try:
                self.store.add_transaction(app_id, transaction)
            except TransactionError as state_error:
                self._rollback_install(target, snapshot, previous_exists, variant.sha256)
                raise TransactionError(f"写入状态失败，已回滚目标文件：{state_error}") from state_error
            return transaction
        except (OSError, IntegrityError, TransactionError) as exc:
            if replaced and not isinstance(exc, TransactionError):
                try:
                    self._rollback_install(target, snapshot, previous_exists, variant.sha256)
                except TransactionError as rollback_error:
                    raise TransactionError(f"安装失败且回滚失败：{exc}；{rollback_error}") from exc
            if isinstance(exc, (IntegrityError, TransactionError)):
                raise
            raise TransactionError(f"安装 {app_id} 失败：{exc}") from exc
        finally:
            stage.unlink(missing_ok=True)
            if not replaced:
                shutil.rmtree(backup_dir, ignore_errors=True)

    def restore(
        self,
        app_id: str,
        expected_target: Path,
        *,
        force: bool = False,
        dry_run: bool = False,
    ) -> dict[str, Any]:
        transaction = self.store.active_transaction(app_id)
        if transaction is None:
            raise TransactionError(f"{app_id} 没有可恢复的安装记录")
        target = Path(str(transaction.get("target") or "")).resolve()
        if target != Path(expected_target).resolve():
            raise TransactionError(f"状态文件中的目标路径与当前 Steam 目录不一致：{target}")
        expected_hash = str(transaction.get("installed_sha256") or "")
        target_exists = target.is_file()
        current_hash = sha256_file(target) if target_exists else None
        unchanged = target_exists and current_hash == expected_hash
        if not unchanged and not force:
            state = "缺失" if not target_exists else f"已修改（当前 {current_hash}）"
            raise TransactionError(f"拒绝恢复 {app_id}：目标文件{state}；如确认继续请使用 --force")
        if dry_run:
            return {"app_id": app_id, "action": "would-restore", "target": str(target)}

        transaction_id = str(transaction.get("id") or "")
        if not transaction_id:
            raise TransactionError(f"{app_id} 的事务 ID 无效")
        backup_dir = self.data_dir / "backups" / app_id / transaction_id
        restore_id = uuid.uuid4().hex
        rollback = backup_dir / f"restore-rollback-{restore_id}.bin"
        forced_archive = backup_dir / f"forced-current-{restore_id}.bin"
        before_exists = target_exists
        cleanup_rollback = True
        try:
            if before_exists:
                _copy_fsync(target, rollback)
                if force and not unchanged:
                    _copy_fsync(target, forced_archive)

            if transaction.get("previous_exists"):
                snapshot_value = transaction.get("snapshot")
                if not isinstance(snapshot_value, str):
                    raise TransactionError(f"{app_id} 的备份路径缺失")
                snapshot = self._resolve_relative(snapshot_value)
                if not snapshot.is_file():
                    raise TransactionError(f"找不到安装前备份：{snapshot}")
                expected_previous = str(transaction.get("previous_sha256") or "")
                if sha256_file(snapshot) != expected_previous:
                    raise IntegrityError(f"安装前备份 SHA-256 不匹配：{snapshot}")
                _copy_fsync(snapshot, target)
            else:
                target.unlink(missing_ok=True)

            forced_value = self._relative(forced_archive) if forced_archive.is_file() else None
            try:
                self.store.mark_restored(app_id, transaction_id, utc_now(), forced_value)
            except TransactionError as state_error:
                try:
                    self._rollback_restore(target, rollback, before_exists)
                except TransactionError as rollback_error:
                    cleanup_rollback = False
                    raise TransactionError(
                        f"保存恢复状态失败且文件回滚失败：{state_error}；"
                        f"恢复副本保留在 {rollback}：{rollback_error}"
                    ) from state_error
                raise TransactionError(f"保存恢复状态失败，已回滚：{state_error}") from state_error
            return {
                "app_id": app_id,
                "action": "restored",
                "target": str(target),
                "forced_archive": forced_value,
            }
        except (OSError, IntegrityError, TransactionError) as exc:
            if isinstance(exc, (IntegrityError, TransactionError)):
                raise
            raise TransactionError(f"恢复 {app_id} 失败：{exc}") from exc
        finally:
            if cleanup_rollback:
                rollback.unlink(missing_ok=True)

    def status(self, app_id: str) -> str:
        transaction = self.store.active_transaction(app_id)
        if transaction is None:
            return "restored" if self.store.transactions(app_id) else "unmanaged"
        target = Path(str(transaction.get("target") or ""))
        if not target.is_file():
            return "missing"
        try:
            actual = sha256_file(target)
        except OSError:
            return "unreadable"
        return "installed" if actual == transaction.get("installed_sha256") else "modified"

    def _relative(self, path: Path) -> str:
        resolved = path.resolve()
        try:
            return resolved.relative_to(self.data_dir).as_posix()
        except ValueError as exc:
            raise TransactionError(f"备份路径越界：{resolved}") from exc

    def _resolve_relative(self, value: str) -> Path:
        candidate = (self.data_dir / Path(value)).resolve()
        try:
            candidate.relative_to(self.data_dir)
        except ValueError as exc:
            raise TransactionError(f"状态文件中的备份路径越界：{value}") from exc
        return candidate

    @staticmethod
    def _rollback_install(target: Path, snapshot: Path, previous_exists: bool, installed_hash: str) -> None:
        try:
            if previous_exists:
                if not snapshot.is_file():
                    raise TransactionError(f"回滚备份不存在：{snapshot}")
                _copy_fsync(snapshot, target)
            elif target.is_file():
                if sha256_file(target) != installed_hash:
                    raise TransactionError("目标文件已变化，不能安全删除")
                target.unlink()
        except (OSError, TransactionError) as exc:
            if isinstance(exc, TransactionError):
                raise
            raise TransactionError(f"回滚安装失败：{exc}") from exc

    @staticmethod
    def _rollback_restore(target: Path, rollback: Path, before_exists: bool) -> None:
        try:
            if before_exists:
                if not rollback.is_file():
                    raise TransactionError(f"恢复回滚文件不存在：{rollback}")
                _copy_fsync(rollback, target)
            else:
                target.unlink(missing_ok=True)
        except (OSError, TransactionError) as exc:
            if isinstance(exc, TransactionError):
                raise
            raise TransactionError(f"回滚恢复失败：{exc}") from exc
