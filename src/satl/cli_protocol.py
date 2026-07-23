from __future__ import annotations

import json
import sys
from typing import Any, Sequence

from satl.models import Catalog, CatalogEntry, SchemaVariant


def variant_record(variant: SchemaVariant) -> dict[str, Any]:
    return {
        "variant_id": variant.variant_id,
        "primary": variant.primary,
        "note_zh": variant.note_zh,
        "sha256": variant.sha256,
        "file_size_bytes": variant.file_size_bytes,
    }


def game_record(
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
        "variants": [variant_record(variant) for variant in entry.variants],
        "installed_state": state,
        "installed_variant_id": installed_variant_id,
        "action": action,
        "error": None,
    }


def print_json(records: Sequence[dict[str, Any]]) -> None:
    print(json.dumps(list(records), ensure_ascii=False, indent=2, sort_keys=True))


def emit_jsonl(operation: str, event: str, payload: dict[str, Any]) -> None:
    print(
        json.dumps(
            {
                "protocol_version": 1,
                "operation": operation,
                "event": event,
                "payload": payload,
            },
            # JSONL may cross a Windows console-codepage boundary in the frozen
            # executable. ASCII escapes keep the byte stream lossless.
            ensure_ascii=True,
            separators=(",", ":"),
            sort_keys=True,
        ),
        flush=True,
    )


def print_catalog_cache_notice(catalog: Catalog, *, operation: str, jsonl: bool) -> None:
    if not catalog.from_cache:
        return
    message = f"网络刷新失败或已禁用，正在使用缓存 catalog：{catalog.source}"
    if jsonl:
        emit_jsonl(operation, "warning", {"message": message, "source": catalog.source})
    else:
        print(f"警告：{message}", file=sys.stderr)
