from __future__ import annotations

from pathlib import Path

from satl.native_languages import detect_achievement_languages


def _string(name: str, value: str) -> bytes:
    return b"\x01" + name.encode() + b"\0" + value.encode("utf-8") + b"\0"


def _object(name: str, *children: bytes) -> bytes:
    return b"\x00" + name.encode() + b"\0" + b"".join(children) + b"\x08"


def _schema_bytes() -> bytes:
    achievement = _object(
        "0",
        _string("name", "ACH_FIRST"),
        _object(
            "display",
            _object("name", _string("english", "First"), _string("schinese", "第一个")),
            _object("desc", _string("english", "Do it"), _string("schinese", "完成目标")),
        ),
    )
    return _object("UserGameStatsSchema", _object("bits", achievement)) + b"\x08"


def test_detect_achievement_languages_reads_native_schema(tmp_path: Path) -> None:
    schema = tmp_path / "UserGameStatsSchema_123.bin"
    schema.write_bytes(_schema_bytes())

    assert detect_achievement_languages(schema) == ("schinese", "english")


def test_detect_achievement_languages_ignores_missing_or_invalid_schema(tmp_path: Path) -> None:
    missing = tmp_path / "missing.bin"
    invalid = tmp_path / "invalid.bin"
    invalid.write_bytes(b"not-binary-keyvalues")

    assert detect_achievement_languages(missing) == ()
    assert detect_achievement_languages(invalid) == ()
