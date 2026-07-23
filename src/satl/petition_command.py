from __future__ import annotations

import argparse

from satl.cli_protocol import emit_jsonl
from satl.cli_validation import validate_app_ids
from satl.petition import export_petition_archive
from satl.steam import find_steam_dir


def command_petition_export(args: argparse.Namespace) -> int:
    app_id = validate_app_ids([args.app_id])[0]
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
        emit_jsonl("petition-export", "completed", payload)
    else:
        print(f"已导出翻译请愿 ZIP：{destination}")
    return 0
