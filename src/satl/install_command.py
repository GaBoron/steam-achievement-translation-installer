from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Sequence

from satl.bkv import achievement_preview
from satl.catalog import CatalogRepository
from satl.cli_protocol import emit_jsonl, print_catalog_cache_notice
from satl.cli_validation import confirm
from satl.errors import PreflightError, SatlError, UsageError
from satl.models import Catalog, CatalogEntry, SchemaVariant
from satl.steam import discover_local_games, find_steam_dir, is_steam_running, schema_target
from satl.transaction import TransactionManager


def parse_variant_overrides(values: Sequence[str]) -> dict[str, str]:
    result: dict[str, str] = {}
    for value in values:
        app_id, separator, variant_id = value.partition("=")
        if not separator or not app_id.isdigit() or not variant_id:
            raise UsageError(f"--variant 必须使用 APP_ID=VARIANT：{value}")
        if app_id in result:
            raise UsageError(f"重复指定版本：{app_id}")
        result[app_id] = variant_id
    return result


def select_install_entries(
    args: argparse.Namespace,
    catalog: Catalog,
    steam_dir: Path,
) -> list[CatalogEntry]:
    if args.matched and args.app_ids:
        raise UsageError("APP_ID 与 --matched 不能同时使用")
    if not args.matched and not args.app_ids:
        raise UsageError("请指定 APP_ID，或使用 --matched")
    if args.matched:
        discovered = discover_local_games(steam_dir, args.account)
        app_ids = [app_id for app_id in discovered if app_id in catalog.entries]
    else:
        app_ids = args.app_ids
    unique: list[str] = []
    seen: set[str] = set()
    for app_id in app_ids:
        if not app_id.isdigit():
            raise UsageError(f"无效的 Steam App ID：{app_id}")
        if app_id not in catalog.entries:
            raise UsageError(f"翻译库中没有 App ID：{app_id}")
        if app_id not in seen:
            seen.add(app_id)
            unique.append(app_id)
    entries = [catalog.entries[app_id] for app_id in sorted(unique, key=int)]
    if not args.allow_outdated:
        non_current = [entry.app_id for entry in entries if entry.status != "current"]
        if non_current and not args.matched:
            raise UsageError(
                "以下条目不是 current，需显式使用 --allow-outdated：" + ", ".join(non_current)
            )
        entries = [entry for entry in entries if entry.status == "current"]
    return entries


def command_install(args: argparse.Namespace) -> int:
    if args.preview_content and (not args.dry_run or not args.jsonl):
        raise UsageError("--preview-content 必须与 --dry-run --jsonl 一起使用")
    repository = CatalogRepository(Path(args.data_dir))
    catalog = repository.load(offline=args.offline, persist=not args.dry_run)
    print_catalog_cache_notice(catalog, operation="install", jsonl=args.jsonl)
    steam_dir = find_steam_dir(args.steam_dir)
    entries = select_install_entries(args, catalog, steam_dir)
    if not entries:
        if args.jsonl:
            emit_jsonl("install", "plan", {"items": [], "count": 0})
            emit_jsonl("install", "completed", {"succeeded": 0, "failed": 0, "exit_code": 0})
        else:
            print("没有符合条件的 current 条目。")
        return 0
    overrides = parse_variant_overrides(args.variant)
    selected_ids = {entry.app_id for entry in entries}
    unknown_overrides = set(overrides) - selected_ids
    if unknown_overrides:
        raise UsageError("--variant 指向未选择的 App ID：" + ", ".join(sorted(unknown_overrides)))

    plan: list[tuple[CatalogEntry, SchemaVariant]] = []
    if not args.jsonl:
        print("安装计划：")
    for entry in entries:
        variant_id = overrides.get(entry.app_id)
        try:
            variant = entry.variant(variant_id) if variant_id else entry.primary_variant()
        except KeyError as exc:
            raise UsageError(f"{entry.app_id} 没有版本：{variant_id}") from exc
        plan.append((entry, variant))
        warning = " [非 current]" if entry.status != "current" else ""
        if not args.jsonl:
            print(f"  {entry.app_id}  {variant.variant_id}  {entry.game_name}{warning}")
    if args.jsonl:
        emit_jsonl(
            "install",
            "plan",
            {
                "count": len(plan),
                "items": [
                    {
                        "app_id": entry.app_id,
                        "game_name": entry.game_name,
                        "catalog_status": entry.status,
                        "variant_id": variant.variant_id,
                    }
                    for entry, variant in plan
                ],
            },
        )
    if args.dry_run:
        if args.preview_content:
            for entry, variant in plan:
                preview = achievement_preview(
                    repository.read_schema_bytes(variant, offline=args.offline)
                )
                emit_jsonl(
                    "install",
                    "item-preview",
                    {
                        "app_id": entry.app_id,
                        "game_name": entry.game_name,
                        "variant_id": variant.variant_id,
                        "action": "replace",
                        **preview,
                    },
                )
        if args.jsonl:
            emit_jsonl(
                "install",
                "completed",
                {"succeeded": 0, "failed": 0, "dry_run": True, "exit_code": 0},
            )
        else:
            print("dry-run：未下载、未创建目录、未写入文件。")
        return 0
    confirm(f"确认安装以上 {len(plan)} 个翻译？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")

    manager = TransactionManager(Path(args.data_dir))
    successes = 0
    failures: list[SatlError] = []
    for entry, variant in plan:
        if args.jsonl:
            emit_jsonl(
                "install",
                "item-started",
                {
                    "app_id": entry.app_id,
                    "game_name": entry.game_name,
                    "variant_id": variant.variant_id,
                },
            )
        try:
            source = repository.download_schema(variant, offline=args.offline)
            manager.install(entry.app_id, schema_target(steam_dir, entry.app_id), source, variant)
            if args.jsonl:
                emit_jsonl(
                    "install",
                    "item-succeeded",
                    {
                        "app_id": entry.app_id,
                        "game_name": entry.game_name,
                        "variant_id": variant.variant_id,
                    },
                )
            else:
                print(f"已安装：{entry.app_id} / {variant.variant_id}")
            successes += 1
        except SatlError as exc:
            if args.jsonl:
                emit_jsonl(
                    "install",
                    "item-failed",
                    {
                        "app_id": entry.app_id,
                        "game_name": entry.game_name,
                        "message": str(exc),
                        "exit_code": exc.exit_code,
                    },
                )
            else:
                print(f"失败：{entry.app_id}：{exc}", file=sys.stderr)
            failures.append(exc)
    exit_code = 0 if not failures else (7 if successes else failures[0].exit_code)
    if args.jsonl:
        emit_jsonl(
            "install",
            "completed",
            {"succeeded": successes, "failed": len(failures), "exit_code": exit_code},
        )
    return exit_code
