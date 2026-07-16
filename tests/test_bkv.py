from __future__ import annotations

from satl.bkv import achievement_preview, parse_binary_keyvalues, serialize_binary_keyvalues


def _string(name: str, value: str) -> bytes:
    return b"\x01" + name.encode() + b"\0" + value.encode("utf-8") + b"\0"


def _object(name: str, *children: bytes) -> bytes:
    return b"\x00" + name.encode() + b"\0" + b"".join(children) + b"\x08"


def schema_bytes() -> bytes:
    achievement = _object(
        "0",
        _string("name", "ACH_FIRST"),
        _object(
            "display",
            _object(
                "name",
                _string("english", "First"),
                _string("schinese", "第一个"),
                _string("japanese", "最初"),
            ),
            _object(
                "desc",
                _string("english", "Do the thing"),
                _string("schinese", "完成目标"),
                _string("japanese", "目標を達成"),
            ),
        ),
    )
    return _object("UserGameStatsSchema", _object("bits", achievement)) + b"\x08"


def test_binary_keyvalues_roundtrip_is_byte_identical() -> None:
    payload = schema_bytes()

    assert serialize_binary_keyvalues(parse_binary_keyvalues(payload)) == payload


def test_achievement_preview_includes_all_localized_content() -> None:
    preview = achievement_preview(schema_bytes())

    assert preview["roundtrip_equal"] is True
    assert preview["achievement_count"] == 1
    row = preview["rows"][0]
    assert row["api_name"] == "ACH_FIRST"
    assert row["schinese_name"] == "第一个"
    assert row["english_description"] == "Do the thing"
    assert "japanese: 最初 — 目標を達成" in row["other_languages"]
