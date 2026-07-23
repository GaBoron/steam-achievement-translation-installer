from __future__ import annotations

import argparse
import sys
from pathlib import Path

from satl.bkv import achievement_preview
from satl.cli_protocol import emit_jsonl
from satl.cli_validation import confirm, validate_app_ids
from satl.errors import PreflightError, SatlError, UsageError
from satl.steam import find_steam_dir, is_steam_running, schema_target
from satl.transaction import TransactionManager


def command_restore(args: argparse.Namespace) -> int:
    if args.preview_content and (not args.dry_run or not args.jsonl):
        raise UsageError("--preview-content 必须与 --dry-run --jsonl 一起使用")
    if args.all and args.app_ids:
        raise UsageError("APP_ID 与 --all 不能同时使用")
    if not args.all and not args.app_ids:
        raise UsageError("请指定 APP_ID，或使用 --all")
    manager = TransactionManager(Path(args.data_dir))
    if args.all:
        app_ids = [
            app_id
            for app_id in manager.store.managed_app_ids()
            if manager.store.active_transaction(app_id) is not None
        ]
    else:
        app_ids = validate_app_ids(args.app_ids)
    if not app_ids:
        if args.jsonl:
            emit_jsonl("restore", "plan", {"items": [], "count": 0})
            emit_jsonl("restore", "completed", {"succeeded": 0, "failed": 0, "exit_code": 0})
        else:
            print("没有可恢复的安装记录。")
        return 0
    steam_dir = find_steam_dir(args.steam_dir)
    if args.jsonl:
        emit_jsonl(
            "restore",
            "plan",
            {
                "count": len(app_ids),
                "items": [{"app_id": app_id, "force": args.force} for app_id in app_ids],
            },
        )
    else:
        print("恢复计划：" + ", ".join(app_ids))
    if args.dry_run:
        if args.preview_content:
            for app_id in app_ids:
                source = manager.restore_preview_source(
                    app_id,
                    schema_target(steam_dir, app_id),
                )
                preview = (
                    achievement_preview(source.read_bytes())
                    if source is not None
                    else {"achievement_count": 0, "roundtrip_equal": True, "rows": []}
                )
                emit_jsonl(
                    "restore",
                    "item-preview",
                    {
                        "app_id": app_id,
                        "action": "replace" if source is not None else "delete",
                        **preview,
                    },
                )
        if args.jsonl:
            emit_jsonl(
                "restore",
                "completed",
                {"succeeded": 0, "failed": 0, "dry_run": True, "exit_code": 0},
            )
        else:
            print("dry-run：未写入文件或状态。")
        return 0
    confirm(f"确认恢复以上 {len(app_ids)} 个翻译？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")

    successes = 0
    failures: list[SatlError] = []
    for app_id in app_ids:
        if args.jsonl:
            emit_jsonl("restore", "item-started", {"app_id": app_id, "force": args.force})
        try:
            manager.restore(
                app_id,
                schema_target(steam_dir, app_id),
                force=args.force,
            )
            if args.jsonl:
                emit_jsonl("restore", "item-succeeded", {"app_id": app_id, "force": args.force})
            else:
                print(f"已恢复：{app_id}")
            successes += 1
        except SatlError as exc:
            if args.jsonl:
                emit_jsonl(
                    "restore",
                    "item-failed",
                    {
                        "app_id": app_id,
                        "message": str(exc),
                        "force": args.force,
                        "exit_code": exc.exit_code,
                    },
                )
            else:
                print(f"失败：{app_id}：{exc}", file=sys.stderr)
            failures.append(exc)
    exit_code = 0 if not failures else (7 if successes else failures[0].exit_code)
    if args.jsonl:
        emit_jsonl(
            "restore",
            "completed",
            {"succeeded": successes, "failed": len(failures), "exit_code": exit_code},
        )
    return exit_code
