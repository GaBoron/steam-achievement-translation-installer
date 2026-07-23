from __future__ import annotations

import json

import pytest

from satl.cli_protocol import emit_jsonl
from satl.cli_validation import validate_app_ids
from satl.errors import UsageError
from satl.install_command import parse_variant_overrides


def test_validate_app_ids_deduplicates_and_sorts_numerically() -> None:
    assert validate_app_ids(["20", "3", "20"]) == ["3", "20"]


@pytest.mark.parametrize("value", ["", "0", "-1", "abc", "123456789012345678901"])
def test_validate_app_ids_rejects_invalid_values(value: str) -> None:
    with pytest.raises(UsageError):
        validate_app_ids([value])


def test_parse_variant_overrides_maps_selected_variants() -> None:
    assert parse_variant_overrides(["123=default", "456=legacy"]) == {
        "123": "default",
        "456": "legacy",
    }


def test_emit_jsonl_preserves_protocol_shape_and_escapes_cjk(capsys) -> None:
    emit_jsonl("scan", "warning", {"message": "中文"})

    output = capsys.readouterr().out
    assert "中文" not in output
    event = json.loads(output)
    assert event == {
        "protocol_version": 1,
        "operation": "scan",
        "event": "warning",
        "payload": {"message": "中文"},
    }
