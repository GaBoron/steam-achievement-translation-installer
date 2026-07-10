from __future__ import annotations

import json
import os
import uuid
from pathlib import Path
from typing import Any

from satl.errors import TransactionError


class StateStore:
    VERSION = 1

    def __init__(self, data_dir: Path) -> None:
        self.data_dir = Path(data_dir)
        self.path = self.data_dir / "state.json"

    @staticmethod
    def empty() -> dict[str, Any]:
        return {"version": StateStore.VERSION, "apps": {}}

    def load(self) -> dict[str, Any]:
        if not self.path.is_file():
            return self.empty()
        try:
            raw = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise TransactionError(f"无法读取状态文件：{self.path}：{exc}") from exc
        if not isinstance(raw, dict) or raw.get("version") != self.VERSION:
            raise TransactionError(f"不支持的状态文件版本：{self.path}")
        if not isinstance(raw.get("apps"), dict):
            raise TransactionError(f"状态文件 apps 字段无效：{self.path}")
        return raw

    def save(self, state: dict[str, Any]) -> None:
        payload = (json.dumps(state, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")
        self.data_dir.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_name(f".{self.path.name}.{uuid.uuid4().hex}.tmp")
        try:
            with temporary.open("xb") as handle:
                handle.write(payload)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, self.path)
        except OSError as exc:
            raise TransactionError(f"无法保存状态文件：{self.path}：{exc}") from exc
        finally:
            temporary.unlink(missing_ok=True)

    def add_transaction(self, app_id: str, transaction: dict[str, Any]) -> None:
        state = self.load()
        apps = state["apps"]
        app = apps.setdefault(app_id, {"transactions": []})
        transactions = app.get("transactions")
        if not isinstance(transactions, list):
            raise TransactionError(f"{app_id} 的事务记录无效")
        transactions.append(transaction)
        self.save(state)

    def mark_restored(
        self,
        app_id: str,
        transaction_id: str,
        restored_at: str,
        forced_archive: str | None,
    ) -> None:
        state = self.load()
        transactions = self._transactions_from(state, app_id)
        for transaction in transactions:
            if transaction.get("id") == transaction_id:
                transaction["restored_at"] = restored_at
                if forced_archive:
                    transaction["forced_archive"] = forced_archive
                self.save(state)
                return
        raise TransactionError(f"找不到事务：{app_id}/{transaction_id}")

    def transactions(self, app_id: str) -> list[dict[str, Any]]:
        return list(self._transactions_from(self.load(), app_id))

    def active_transaction(self, app_id: str) -> dict[str, Any] | None:
        for transaction in reversed(self.transactions(app_id)):
            if not transaction.get("restored_at"):
                return transaction
        return None

    def managed_app_ids(self) -> tuple[str, ...]:
        state = self.load()
        return tuple(sorted((str(key) for key in state["apps"]), key=lambda value: int(value)))

    @staticmethod
    def _transactions_from(state: dict[str, Any], app_id: str) -> list[dict[str, Any]]:
        app = state["apps"].get(app_id)
        if app is None:
            return []
        if not isinstance(app, dict) or not isinstance(app.get("transactions"), list):
            raise TransactionError(f"{app_id} 的状态记录无效")
        transactions = app["transactions"]
        if not all(isinstance(item, dict) for item in transactions):
            raise TransactionError(f"{app_id} 的事务记录无效")
        return transactions
