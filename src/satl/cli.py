from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path
from typing import Any, Sequence

from satl import __version__
from satl.bkv import achievement_preview
from satl.catalog import CatalogRepository
from satl.errors import CatalogError, PreflightError, SatlError, TransactionError, UsageError
from satl.game_names import SteamGameNameResolver
from satl.models import Catalog, CatalogEntry, SchemaVariant
from satl.petition import export_petition_archive
from satl.state import StateStore
from satl.steam import discover_local_games, find_steam_dir, is_steam_running, schema_target
from satl.transaction import TransactionManager


def default_data_dir() -> Path:
    base = os.environ.get("LOCALAPPDATA")
    if base:
        return Path(base) / "SteamAchievementTranslationInstaller"
    return Path.home() / "AppData" / "Local" / "SteamAchievementTranslationInstaller"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="satl",
        description="安全安装和恢复 Steam 成就翻译库中的本地化文件。",
    )
    parser.add_argument("--version", action="version", version=f"satl {__version__}")
    subparsers = parser.add_subparsers(dest="command", required=True)

    scan = subparsers.add_parser("scan", help="扫描本机游戏并匹配可用翻译")
    _add_data_dir(scan)
    _add_steam_dir(scan)
    _add_offline(scan)
    scan.add_argument("--account", help="仅使用指定的本地 SteamID64 账号缓存")
    scan.add_argument(
        "--scope",
        choices=("manageable", "local", "cloud"),
        default="manageable",
        help="列出可管理、本地或云端游戏（默认：manageable）",
    )
    scan.add_argument("--json", action="store_true", help="输出稳定的 JSON 记录")
    scan.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    scan.set_defaults(handler=command_scan)

    install = subparsers.add_parser("install", help="安装一个或多个翻译")
    _add_data_dir(install)
    _add_steam_dir(install)
    _add_offline(install)
    install.add_argument("app_ids", nargs="*", metavar="APP_ID")
    install.add_argument("--matched", action="store_true", help="安装扫描到的所有可用翻译")
    install.add_argument("--account", help="与 --matched 一起使用的 SteamID64")
    install.add_argument(
        "--variant",
        action="append",
        default=[],
        metavar="APP_ID=VARIANT",
        help="选择非默认版本，可重复指定",
    )
    install.add_argument("--allow-outdated", action="store_true", help="允许安装非 current 条目")
    install.add_argument("--yes", action="store_true", help="跳过交互确认")
    install.add_argument("--dry-run", action="store_true", help="仅显示计划，不下载或写入")
    install.add_argument(
        "--preview-content",
        action="store_true",
        help="在 JSONL dry-run 中读取并输出待安装 schema 的成就内容",
    )
    install.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    install.set_defaults(handler=command_install)

    status = subparsers.add_parser("status", help="检查 SATL 管理的安装状态")
    _add_data_dir(status)
    _add_offline(status)
    status.add_argument("app_ids", nargs="*", metavar="APP_ID")
    status.add_argument("--json", action="store_true", help="输出稳定的 JSON 记录")
    status.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    status.set_defaults(handler=command_status)

    restore = subparsers.add_parser("restore", help="恢复安装前的 schema")
    _add_data_dir(restore)
    _add_steam_dir(restore)
    restore.add_argument("app_ids", nargs="*", metavar="APP_ID")
    restore.add_argument("--all", action="store_true", help="恢复所有尚未恢复的安装")
    restore.add_argument("--force", action="store_true", help="归档已变化的目标后强制恢复")
    restore.add_argument("--yes", action="store_true", help="跳过交互确认")
    restore.add_argument("--dry-run", action="store_true", help="仅显示计划，不写入")
    restore.add_argument(
        "--preview-content",
        action="store_true",
        help="在 JSONL dry-run 中读取并输出待恢复 schema 的成就内容",
    )
    restore.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    restore.set_defaults(handler=command_restore)

    cache = subparsers.add_parser("cache", help="管理本地 catalog/schema 缓存")
    cache_subparsers = cache.add_subparsers(dest="cache_command", required=True)
    refresh = cache_subparsers.add_parser("refresh", help="刷新 index.json 缓存")
    _add_data_dir(refresh)
    refresh.add_argument("--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件")
    refresh.set_defaults(handler=command_cache_refresh)

    petition = subparsers.add_parser("petition", help="导出并提交翻译请愿")
    petition_subparsers = petition.add_subparsers(dest="petition_command", required=True)
    petition_export = petition_subparsers.add_parser(
        "export", help="按翻译请愿模板导出原始 schema ZIP"
    )
    _add_steam_dir(petition_export)
    petition_export.add_argument("app_id", metavar="APP_ID")
    petition_export.add_argument("--output", type=Path, required=True, help="ZIP 保存路径")
    petition_export.add_argument("--overwrite", action="store_true", help="覆盖已确认的目标文件")
    petition_export.add_argument(
        "--jsonl", action="store_true", help="输出供桌面应用使用的 JSON Lines 事件"
    )
    petition_export.set_defaults(handler=command_petition_export)
    return parser


def _add_data_dir(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--data-dir", type=Path, default=default_data_dir(), help="覆盖 SATL 数据目录")


def _add_steam_dir(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--steam-dir", type=Path, help="覆盖自动检测的 Steam 目录")


def _add_offline(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--offline", action="store_true", help="仅使用已验证的本地缓存")


def _repository(args: argparse.Namespace) -> CatalogRepository:
    return CatalogRepository(Path(args.data_dir))


def _manager(args: argparse.Namespace) -> TransactionManager:
    return TransactionManager(Path(args.data_dir))


def _variant_json(variant: SchemaVariant) -> dict[str, Any]:
    return {
        "variant_id": variant.variant_id,
        "primary": variant.primary,
        "note_zh": variant.note_zh,
        "sha256": variant.sha256,
        "file_size_bytes": variant.file_size_bytes,
    }


def _record(
    entry: CatalogEntry,
    discovery: Sequence[str],
    state: str,
    action: str,
    installed_variant_id: str | None = None,
) -> dict[str, Any]:
    return {
        "app_id": entry.app_id,
        "game_name": entry.game_name,
        "discovery": list(discovery),
        "catalog_status": entry.status,
        "variants": [_variant_json(variant) for variant in entry.variants],
        "installed_state": state,
        "installed_variant_id": installed_variant_id,
        "action": action,
        "error": None,
    }


def _print_json(records: Sequence[dict[str, Any]]) -> None:
    print(json.dumps(list(records), ensure_ascii=False, indent=2, sort_keys=True))


def _emit_jsonl(operation: str, event: str, payload: dict[str, Any]) -> None:
    print(
        json.dumps(
            {
                "protocol_version": 1,
                "operation": operation,
                "event": event,
                "payload": payload,
            },
            # JSONL is a machine protocol that may cross a Windows console-codepage
            # boundary in the frozen executable. ASCII escapes keep the byte stream
            # lossless; JSON consumers reconstruct the original Unicode text.
            ensure_ascii=True,
            separators=(",", ":"),
            sort_keys=True,
        ),
        flush=True,
    )


def _print_catalog_cache_notice(catalog: Catalog, *, operation: str, jsonl: bool) -> None:
    if catalog.from_cache:
        message = f"网络刷新失败或已禁用，正在使用缓存 catalog：{catalog.source}"
        if jsonl:
            _emit_jsonl(operation, "warning", {"message": message, "source": catalog.source})
        else:
            print(f"警告：{message}", file=sys.stderr)


def command_scan(args: argparse.Namespace) -> int:
    if args.json and args.jsonl:
        raise UsageError("--json 与 --jsonl 不能同时使用")
    catalog = _repository(args).load(offline=args.offline)
    _print_catalog_cache_notice(catalog, operation="scan", jsonl=args.jsonl)
    steam_dir = None if args.scope == "cloud" else find_steam_dir(args.steam_dir)
    discovered = {} if steam_dir is None else discover_local_games(steam_dir, args.account)
    manager = _manager(args)
    if args.scope == "cloud":
        app_ids = sorted(catalog.entries, key=int)
    elif args.scope == "local":
        app_ids = sorted(discovered, key=int)
    else:
        app_ids = sorted(discovered.keys() & catalog.entries.keys(), key=int)

    if args.jsonl:
        _emit_jsonl(
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
                _emit_jsonl(
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
                _emit_jsonl("scan", "warning", {"message": message})
            else:
                print(f"警告：{message}", file=sys.stderr)

    if args.jsonl and app_ids:
        _emit_jsonl(
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
        if entry is not None:
            records.append(
                _record(
                    entry,
                    sources,
                    manager.status(app_id),
                    "available",
                    manager.installed_variant_id(app_id),
                )
            )
        else:
            game_name = discovery.game_name if discovery else ""
            records.append(
                {
                    "app_id": app_id,
                    "game_name": game_name or resolved_names.get(app_id) or f"Steam 游戏 {app_id}",
                    "discovery": sources,
                    "catalog_status": "unknown",
                    "variants": [],
                    "installed_state": manager.status(app_id),
                    "installed_variant_id": manager.installed_variant_id(app_id),
                    "action": "unavailable",
                    "error": None,
                }
            )
        if args.jsonl:
            _emit_jsonl("scan", "item-succeeded", records[-1] | {"position": position})
    if args.jsonl:
        _emit_jsonl("scan", "completed", {"count": len(records), "exit_code": 0})
    elif args.json:
        _print_json(records)
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


def _parse_variant_overrides(values: Sequence[str]) -> dict[str, str]:
    result: dict[str, str] = {}
    for value in values:
        app_id, separator, variant_id = value.partition("=")
        if not separator or not app_id.isdigit() or not variant_id:
            raise UsageError(f"--variant 必须使用 APP_ID=VARIANT：{value}")
        if app_id in result:
            raise UsageError(f"重复指定版本：{app_id}")
        result[app_id] = variant_id
    return result


def _select_install_entries(args: argparse.Namespace, catalog: Catalog, steam_dir: Path) -> list[CatalogEntry]:
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


def _confirm(message: str, yes: bool) -> None:
    if yes:
        return
    if not sys.stdin.isatty():
        raise UsageError("非交互终端必须使用 --yes 明确确认")
    answer = input(f"{message} [y/N] ").strip().casefold()
    if answer not in {"y", "yes"}:
        raise UsageError("用户取消操作")


def command_install(args: argparse.Namespace) -> int:
    if args.preview_content and (not args.dry_run or not args.jsonl):
        raise UsageError("--preview-content 必须与 --dry-run --jsonl 一起使用")
    repository = _repository(args)
    catalog = repository.load(offline=args.offline, persist=not args.dry_run)
    _print_catalog_cache_notice(catalog, operation="install", jsonl=args.jsonl)
    steam_dir = find_steam_dir(args.steam_dir)
    entries = _select_install_entries(args, catalog, steam_dir)
    if not entries:
        if args.jsonl:
            _emit_jsonl("install", "plan", {"items": [], "count": 0})
            _emit_jsonl("install", "completed", {"succeeded": 0, "failed": 0, "exit_code": 0})
        else:
            print("没有符合条件的 current 条目。")
        return 0
    overrides = _parse_variant_overrides(args.variant)
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
        _emit_jsonl(
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
                _emit_jsonl(
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
            _emit_jsonl(
                "install",
                "completed",
                {"succeeded": 0, "failed": 0, "dry_run": True, "exit_code": 0},
            )
        else:
            print("dry-run：未下载、未创建目录、未写入文件。")
        return 0
    _confirm(f"确认安装以上 {len(plan)} 个翻译？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")

    manager = _manager(args)
    successes = 0
    failures: list[SatlError] = []
    for entry, variant in plan:
        if args.jsonl:
            _emit_jsonl(
                "install",
                "item-started",
                {"app_id": entry.app_id, "game_name": entry.game_name, "variant_id": variant.variant_id},
            )
        try:
            source = repository.download_schema(variant, offline=args.offline)
            manager.install(entry.app_id, schema_target(steam_dir, entry.app_id), source, variant)
            if args.jsonl:
                _emit_jsonl(
                    "install",
                    "item-succeeded",
                    {"app_id": entry.app_id, "game_name": entry.game_name, "variant_id": variant.variant_id},
                )
            else:
                print(f"已安装：{entry.app_id} / {variant.variant_id}")
            successes += 1
        except SatlError as exc:
            if args.jsonl:
                _emit_jsonl(
                    "install",
                    "item-failed",
                    {"app_id": entry.app_id, "game_name": entry.game_name, "message": str(exc), "exit_code": exc.exit_code},
                )
            else:
                print(f"失败：{entry.app_id}：{exc}", file=sys.stderr)
            failures.append(exc)
    exit_code = 0 if not failures else (7 if successes else failures[0].exit_code)
    if args.jsonl:
        _emit_jsonl(
            "install",
            "completed",
            {"succeeded": successes, "failed": len(failures), "exit_code": exit_code},
        )
    return exit_code


def command_status(args: argparse.Namespace) -> int:
    if args.json and args.jsonl:
        raise UsageError("--json 与 --jsonl 不能同时使用")
    store = StateStore(Path(args.data_dir))
    manager = _manager(args)
    app_ids = _validated_app_ids(args.app_ids or list(store.managed_app_ids()))
    catalog: Catalog | None
    try:
        catalog = _repository(args).load(offline=args.offline)
    except CatalogError:
        catalog = None
    records: list[dict[str, Any]] = []
    for app_id in app_ids:
        entry = catalog.entries.get(app_id) if catalog else None
        if entry:
            record = _record(
                entry,
                [],
                manager.status(app_id),
                "none",
                manager.installed_variant_id(app_id),
            )
        else:
            record = {
                "app_id": app_id,
                "game_name": app_id,
                "discovery": [],
                "catalog_status": "unknown",
                "variants": [],
                "installed_state": manager.status(app_id),
                "installed_variant_id": manager.installed_variant_id(app_id),
                "action": "none",
                "error": None,
            }
        records.append(record)
    if args.jsonl:
        _emit_jsonl("status", "plan", {"count": len(records)})
        for record in records:
            _emit_jsonl("status", "item-succeeded", record)
        _emit_jsonl("status", "completed", {"count": len(records), "exit_code": 0})
    elif args.json:
        _print_json(records)
    elif records:
        for record in records:
            print(f"{record['app_id']:>10}  {record['installed_state']:<10} {record['game_name']}")
    else:
        print("没有 SATL 管理的安装记录。")
    return 0


def command_restore(args: argparse.Namespace) -> int:
    if args.preview_content and (not args.dry_run or not args.jsonl):
        raise UsageError("--preview-content 必须与 --dry-run --jsonl 一起使用")
    if args.all and args.app_ids:
        raise UsageError("APP_ID 与 --all 不能同时使用")
    if not args.all and not args.app_ids:
        raise UsageError("请指定 APP_ID，或使用 --all")
    manager = _manager(args)
    if args.all:
        app_ids = [
            app_id
            for app_id in manager.store.managed_app_ids()
            if manager.store.active_transaction(app_id) is not None
        ]
    else:
        app_ids = _validated_app_ids(args.app_ids)
    if not app_ids:
        if args.jsonl:
            _emit_jsonl("restore", "plan", {"items": [], "count": 0})
            _emit_jsonl("restore", "completed", {"succeeded": 0, "failed": 0, "exit_code": 0})
        else:
            print("没有可恢复的安装记录。")
        return 0
    steam_dir = find_steam_dir(args.steam_dir)
    if args.jsonl:
        _emit_jsonl(
            "restore",
            "plan",
            {"count": len(app_ids), "items": [{"app_id": app_id, "force": args.force} for app_id in app_ids]},
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
                _emit_jsonl(
                    "restore",
                    "item-preview",
                    {
                        "app_id": app_id,
                        "action": "replace" if source is not None else "delete",
                        **preview,
                    },
                )
        if args.jsonl:
            _emit_jsonl(
                "restore",
                "completed",
                {"succeeded": 0, "failed": 0, "dry_run": True, "exit_code": 0},
            )
        else:
            print("dry-run：未写入文件或状态。")
        return 0
    _confirm(f"确认恢复以上 {len(app_ids)} 个翻译？", args.yes)
    if is_steam_running():
        raise PreflightError("Steam 正在运行。请从系统托盘正常退出 Steam 后重试。")

    successes = 0
    failures: list[SatlError] = []
    for app_id in app_ids:
        if args.jsonl:
            _emit_jsonl("restore", "item-started", {"app_id": app_id, "force": args.force})
        try:
            manager.restore(
                app_id,
                schema_target(steam_dir, app_id),
                force=args.force,
            )
            if args.jsonl:
                _emit_jsonl("restore", "item-succeeded", {"app_id": app_id, "force": args.force})
            else:
                print(f"已恢复：{app_id}")
            successes += 1
        except SatlError as exc:
            if args.jsonl:
                _emit_jsonl(
                    "restore",
                    "item-failed",
                    {"app_id": app_id, "message": str(exc), "force": args.force, "exit_code": exc.exit_code},
                )
            else:
                print(f"失败：{app_id}：{exc}", file=sys.stderr)
            failures.append(exc)
    exit_code = 0 if not failures else (7 if successes else failures[0].exit_code)
    if args.jsonl:
        _emit_jsonl(
            "restore",
            "completed",
            {"succeeded": successes, "failed": len(failures), "exit_code": exit_code},
        )
    return exit_code


def command_cache_refresh(args: argparse.Namespace) -> int:
    catalog = _repository(args).refresh()
    if args.jsonl:
        _emit_jsonl(
            "cache-refresh",
            "completed",
            {"count": len(catalog.entries), "source": catalog.source, "exit_code": 0},
        )
    else:
        print(f"已刷新 catalog：{len(catalog.entries)} 个条目，来源 {catalog.source}")
    return 0


def command_petition_export(args: argparse.Namespace) -> int:
    app_id = _validated_app_ids([args.app_id])[0]
    steam_dir = find_steam_dir(args.steam_dir)
    source, destination, byte_count = export_petition_archive(
        steam_dir,
        app_id,
        args.output,
        overwrite=args.overwrite,
    )
    payload = {
        "app_id": app_id,
        "source": str(source),
        "output": str(destination),
        "file_size_bytes": byte_count,
        "exit_code": 0,
    }
    if args.jsonl:
        _emit_jsonl("petition-export", "completed", payload)
    else:
        print(f"已导出翻译请愿 ZIP：{destination}")
    return 0


def _validated_app_ids(values: Sequence[str]) -> list[str]:
    unique: set[str] = set()
    for value in values:
        if not value.isdigit() or len(value) > 20 or int(value) <= 0:
            raise UsageError(f"无效的 Steam App ID：{value}")
        unique.add(value)
    return sorted(unique, key=int)


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args: argparse.Namespace | None = None
    try:
        args = parser.parse_args(argv)
        return int(args.handler(args))
    except SatlError as exc:
        if args is not None and getattr(args, "jsonl", False):
            operation = {
                "cache": "cache-refresh",
                "petition": "petition-export",
            }.get(getattr(args, "command", None), str(args.command))
            _emit_jsonl(operation, "error", {"message": str(exc), "exit_code": exc.exit_code})
        else:
            print(f"错误：{exc}", file=sys.stderr)
        return exc.exit_code
    except KeyboardInterrupt:
        print("已取消。", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
