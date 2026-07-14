from __future__ import annotations

import os
import uuid
import zipfile
from pathlib import Path

from satl.errors import PreflightError, TransactionError, UsageError
from satl.steam import schema_target


def export_petition_archive(
    steam_dir: Path,
    app_id: str,
    output: Path,
    *,
    overwrite: bool = False,
) -> tuple[Path, Path, int]:
    """Export the original Steam schema in the exact petition ZIP layout."""
    if not app_id.isdigit() or int(app_id) <= 0:
        raise UsageError(f"无效的 Steam App ID：{app_id}")

    source = schema_target(steam_dir, app_id)
    archive_name = f"UserGameStatsSchema_{app_id}.zip"
    member_name = f"UserGameStatsSchema_{app_id}.bin"
    destination = Path(output).expanduser().resolve()
    if destination.suffix.lower() != ".zip":
        raise UsageError(f"请将导出文件保存为 ZIP：{destination}")
    if destination.name != archive_name:
        raise UsageError(f"翻译请愿 ZIP 文件名必须为 {archive_name}")
    if destination.exists() and not overwrite:
        raise UsageError(f"导出文件已存在；确认覆盖后请使用 --overwrite：{destination}")
    if not source.is_file():
        raise PreflightError(
            f"未找到 App ID {app_id} 的原始成就文件：{source}。"
            "请先启动该游戏并解锁或读取一次成就，让 Steam 生成此文件。"
        )

    try:
        source_before = source.stat()
    except OSError as exc:
        raise PreflightError(f"无法读取原始成就文件：{source}：{exc}") from exc
    if source_before.st_size <= 0:
        raise PreflightError(f"原始成就文件为空，无法提交：{source}")

    parent = destination.parent
    try:
        parent.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        raise TransactionError(f"无法创建导出目录：{parent}：{exc}") from exc

    temporary = parent / f".{archive_name}.{uuid.uuid4().hex}.part"
    try:
        with zipfile.ZipFile(
            temporary,
            mode="x",
            compression=zipfile.ZIP_DEFLATED,
            compresslevel=9,
        ) as archive:
            archive.write(source, arcname=member_name)

        source_after = source.stat()
        if (
            source_before.st_size != source_after.st_size
            or source_before.st_mtime_ns != source_after.st_mtime_ns
        ):
            raise TransactionError("导出期间 Steam 成就文件发生变化，请稍后重试。")

        with zipfile.ZipFile(temporary, mode="r") as archive:
            members = archive.infolist()
            if len(members) != 1 or members[0].filename != member_name:
                raise TransactionError("导出的 ZIP 内容不符合翻译请愿要求。")
            if members[0].file_size != source_before.st_size or archive.testzip() is not None:
                raise TransactionError("导出的 ZIP 完整性校验失败。")

        if overwrite:
            os.replace(temporary, destination)
        else:
            try:
                os.rename(temporary, destination)
            except FileExistsError as exc:
                raise UsageError(
                    f"导出文件已存在；确认覆盖后请使用 --overwrite：{destination}"
                ) from exc
    except (OSError, zipfile.BadZipFile, zipfile.LargeZipFile) as exc:
        raise TransactionError(f"无法导出翻译请愿 ZIP：{exc}") from exc
    finally:
        try:
            temporary.unlink(missing_ok=True)
        except OSError:
            pass

    return source, destination, source_before.st_size
