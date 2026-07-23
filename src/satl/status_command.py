from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any

from satl.catalog import CatalogRepository
from satl.cli_protocol import emit_jsonl, game_record, print_json
from satl.cli_validation import validate_app_ids
from satl.errors import CatalogError, UsageError
from satl.models import Catalog
from satl.state import StateStore
from satl.transaction import TransactionManager


def command_status(args: argparse.Namespace) -> int:
    if args.json and args.jsonl:
        raise UsageError("--json 与 --jsonl 不能同时使用")
    data_dir = Path(args.data_dir)
    store = StateStore(data_dir)
    manager = TransactionManager(data_dir)
    app_ids = validate_app_ids(args.app_ids or list(store.managed_app_ids()))
    catalog: Catalog | None
    try:
        catalog = CatalogRepository(data_dir).load(offline=args.offline)
    except CatalogError:
        catalog = None
    records: list[dict[str, Any]] = []
    for app_id in app_ids:
        entry = catalog.entries.get(app_id) if catalog else None
        if entry:
            record = game_record(
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
        emit_jsonl("status", "plan", {"count": len(records)})
        for record in records:
            emit_jsonl("status", "item-succeeded", record)
        emit_jsonl("status", "completed", {"count": len(records), "exit_code": 0})
    elif args.json:
        print_json(records)
    elif records:
        for record in records:
            print(f"{record['app_id']:>10}  {record['installed_state']:<10} {record['game_name']}")
    else:
        print("没有 SATL 管理的安装记录。")
    return 0
