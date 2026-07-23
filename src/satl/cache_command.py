from __future__ import annotations

import argparse
from pathlib import Path

from satl.catalog import CatalogRepository
from satl.cli_protocol import emit_jsonl


def command_cache_refresh(args: argparse.Namespace) -> int:
    catalog = CatalogRepository(Path(args.data_dir)).refresh()
    if args.jsonl:
        emit_jsonl(
            "cache-refresh",
            "completed",
            {"count": len(catalog.entries), "source": catalog.source, "exit_code": 0},
        )
    else:
        print(f"已刷新 catalog：{len(catalog.entries)} 个条目，来源 {catalog.source}")
    return 0
