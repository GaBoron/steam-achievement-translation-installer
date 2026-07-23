from __future__ import annotations

import argparse
import os
from pathlib import Path

from satl import __version__
from satl.cache_command import command_cache_refresh
from satl.install_command import command_install
from satl.petition_command import command_petition_export
from satl.restore_command import command_restore
from satl.scan_command import command_scan
from satl.status_command import command_status


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
