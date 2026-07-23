from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Any

from satl.catalog import CatalogRepository
from satl.cli_protocol import (
    emit_jsonl,
    game_record,
    print_catalog_cache_notice,
    print_json,
)
from satl.errors import UsageError
from satl.game_names import SteamGameNameResolver
from satl.native_languages import detect_achievement_languages
from satl.steam import discover_local_games, find_steam_dir, schema_target
from satl.transaction import TransactionManager


def command_scan(args: argparse.Namespace) -> int:
    if args.json and args.jsonl:
        raise UsageError("--json 与 --jsonl 不能同时使用")
    catalog = CatalogRepository(Path(args.data_dir)).load(offline=args.offline)
    print_catalog_cache_notice(catalog, operation="scan", jsonl=args.jsonl)
    steam_dir = None if args.scope == "cloud" else find_steam_dir(args.steam_dir)
    discovered = {} if steam_dir is None else discover_local_games(steam_dir, args.account)
    manager = TransactionManager(Path(args.data_dir))
    if args.scope == "cloud":
        app_ids = sorted(catalog.entries, key=int)
    elif args.scope == "local":
        app_ids = sorted(discovered, key=int)
    else:
        app_ids = sorted(discovered.keys() & catalog.entries.keys(), key=int)

    if args.jsonl:
        emit_jsonl(
            "scan",
            "plan",
            {
                "steam_dir": str(steam_dir) if steam_dir is not None else "",
                "scope": args.scope,
                "count": len(app_ids),
            },
        )

    resolved_names: dict[str, str] = {}
    missing_names = [
        app_id
        for app_id in app_ids
        if app_id not in catalog.entries and not discovered[app_id].game_name
    ]
    if missing_names and not args.offline:

        def report_name_progress(current: int, total: int, app_id: str) -> None:
            if args.jsonl:
                emit_jsonl(
                    "scan",
                    "progress",
                    {
                        "phase": "name-lookup",
                        "current": current,
                        "total": total,
                        "message": f"正在联网查询游戏名称 {current}/{total}（App ID {app_id}）",
                    },
                )

        resolution = SteamGameNameResolver(Path(args.data_dir)).resolve_many(
            missing_names,
            report_name_progress,
        )
        resolved_names = resolution.names
        if resolution.error:
            message = f"部分本地游戏名称联网查询失败，将显示 App ID：{resolution.error}"
            if args.jsonl:
                emit_jsonl("scan", "warning", {"message": message})
            else:
                print(f"警告：{message}", file=sys.stderr)

    if args.jsonl and app_ids:
        emit_jsonl(
            "scan",
            "progress",
            {
                "phase": "game-loading",
                "current": 0,
                "total": len(app_ids),
                "message": f"正在加载游戏 0/{len(app_ids)}",
            },
        )

    records: list[dict[str, Any]] = []
    for position, app_id in enumerate(app_ids, 1):
        entry = catalog.entries.get(app_id)
        discovery = discovered.get(app_id)
        sources = sorted(discovery.discovery) if discovery else []
        installed_state = manager.status(app_id)
        native_languages = (
            detect_achievement_languages(schema_target(steam_dir, app_id))
            if steam_dir is not None and installed_state in {"unmanaged", "restored"}
            else ()
        )
        if entry is not None:
            record = game_record(
                entry,
                sources,
                installed_state,
                "available",
                manager.installed_variant_id(app_id),
            )
            record["native_languages"] = list(native_languages)
            records.append(record)
        else:
            game_name = discovery.game_name if discovery else ""
            records.append(
                {
                    "app_id": app_id,
                    "game_name": game_name or resolved_names.get(app_id) or f"Steam 游戏 {app_id}",
                    "discovery": sources,
                    "catalog_status": "unknown",
                    "variants": [],
                    "installed_state": installed_state,
                    "installed_variant_id": manager.installed_variant_id(app_id),
                    "native_languages": list(native_languages),
                    "action": "unavailable",
                    "error": None,
                }
            )
        if args.jsonl:
            emit_jsonl("scan", "item-succeeded", records[-1] | {"position": position})
    if args.jsonl:
        emit_jsonl("scan", "completed", {"count": len(records), "exit_code": 0})
    elif args.json:
        print_json(records)
    elif records:
        if steam_dir is not None:
            print(f"Steam：{steam_dir}")
        for record in records:
            sources = ",".join(record["discovery"])
            variants = ",".join(item["variant_id"] for item in record["variants"])
            print(
                f"{record['app_id']:>10}  {record['catalog_status']:<16} "
                f"{record['installed_state']:<10} [{sources}] [{variants}] {record['game_name']}"
            )
        labels = {"manageable": "可管理游戏", "local": "本地游戏", "cloud": "云端条目"}
        print(f"找到 {len(records)} 个{labels[args.scope]}。")
    else:
        print("没有在本地 Steam 数据中匹配到翻译库条目。")
    return 0
