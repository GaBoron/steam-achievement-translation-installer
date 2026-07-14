from __future__ import annotations

import hashlib
import json
import zipfile
from pathlib import Path

import pytest

from satl.cli import main


def make_fixture(
    root: Path, *, status: str = "current", game_name: str = "CLI Game"
) -> tuple[Path, Path]:
    steam = root / "Steam"
    (steam / "steamapps").mkdir(parents=True)
    (steam / "steam.exe").write_bytes(b"")
    (steam / "steamapps" / "appmanifest_123.acf").write_text('"AppState" {}', encoding="utf-8")
    data_dir = root / "data"
    (data_dir / "cache").mkdir(parents=True)
    payload = b"translated"
    catalog = {
        "version": 1,
        "entries": [
            {
                "game_id": "123",
                "game_name": game_name,
                "status": status,
                "schema_file": "files/123/UserGameStatsSchema_123.bin",
                "sha256": hashlib.sha256(payload).hexdigest(),
                "file_size_bytes": len(payload),
                "achievement_count": 1,
            }
        ],
    }
    (data_dir / "cache" / "index.json").write_text(json.dumps(catalog), encoding="utf-8")
    schema = data_dir / "cache" / "schemas" / f"{hashlib.sha256(payload).hexdigest()}.bin"
    schema.parent.mkdir(parents=True)
    schema.write_bytes(payload)
    return steam, data_dir


def jsonl_events(output: str) -> list[dict[str, object]]:
    return [json.loads(line) for line in output.splitlines() if line.strip()]


def test_scan_json_has_stable_fields(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    steam, data_dir = make_fixture(tmp_path)
    result = main(
        [
            "scan",
            "--offline",
            "--json",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 0
    output = json.loads(capsys.readouterr().out)
    assert len(output) == 1
    assert set(output[0]) == {
        "app_id",
        "game_name",
        "discovery",
        "catalog_status",
        "variants",
        "installed_state",
        "installed_variant_id",
        "action",
        "error",
    }
    assert output[0]["discovery"] == ["installed"]


def test_scan_jsonl_has_versioned_event_sequence(tmp_path: Path, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    result = main(
        [
            "scan",
            "--offline",
            "--jsonl",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 0
    events = jsonl_events(capsys.readouterr().out)
    assert [event["event"] for event in events] == [
        "warning",
        "plan",
        "item-succeeded",
        "completed",
    ]
    assert all(event["protocol_version"] == 1 for event in events)
    assert events[2]["payload"]["app_id"] == "123"
    assert events[-1]["payload"]["exit_code"] == 0


def test_jsonl_escapes_cjk_for_codepage_safe_transport(tmp_path: Path, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path, game_name="以撒的结合：重生")

    result = main(
        [
            "scan",
            "--offline",
            "--jsonl",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )

    assert result == 0
    output = capsys.readouterr().out
    assert "以撒的结合" not in output
    assert "\\u4ee5\\u6492\\u7684\\u7ed3\\u5408" in output
    events = jsonl_events(output)
    assert events[2]["payload"]["game_name"] == "以撒的结合：重生"


def test_json_and_jsonl_are_mutually_exclusive(tmp_path: Path, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    result = main(
        [
            "scan",
            "--offline",
            "--json",
            "--jsonl",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 2
    events = jsonl_events(capsys.readouterr().out)
    assert events[-1]["event"] == "error"
    assert events[-1]["payload"]["exit_code"] == 2


def test_install_dry_run_creates_no_target_or_state(tmp_path: Path, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    result = main(
        [
            "install",
            "123",
            "--offline",
            "--dry-run",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 0
    assert "dry-run" in capsys.readouterr().out
    assert not (steam / "appcache").exists()
    assert not (data_dir / "state.json").exists()


def test_install_jsonl_dry_run_emits_plan_without_writes(tmp_path: Path, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    result = main(
        [
            "install",
            "123",
            "--offline",
            "--dry-run",
            "--jsonl",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 0
    events = jsonl_events(capsys.readouterr().out)
    plan = next(event for event in events if event["event"] == "plan")
    assert plan["payload"]["items"][0]["variant_id"] == "default"
    assert events[-1]["payload"]["dry_run"] is True
    assert not (steam / "appcache").exists()
    assert not (data_dir / "state.json").exists()


def test_noninteractive_install_requires_yes(tmp_path: Path, monkeypatch, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    monkeypatch.setattr("satl.cli.sys.stdin.isatty", lambda: False)
    result = main(
        [
            "install",
            "123",
            "--offline",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 2
    assert "--yes" in capsys.readouterr().err
    assert not (steam / "appcache").exists()


def test_install_and_status_offline(tmp_path: Path, monkeypatch, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    monkeypatch.setattr("satl.cli.is_steam_running", lambda: False)
    result = main(
        [
            "install",
            "123",
            "--offline",
            "--yes",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 0
    capsys.readouterr()
    target = steam / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    assert target.read_bytes() == b"translated"

    result = main(["status", "123", "--offline", "--json", "--data-dir", str(data_dir)])
    assert result == 0
    output = json.loads(capsys.readouterr().out)
    assert output[0]["installed_state"] == "installed"
    assert output[0]["installed_variant_id"] == "default"


def test_install_and_restore_jsonl_emit_item_results(tmp_path: Path, monkeypatch, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    monkeypatch.setattr("satl.cli.is_steam_running", lambda: False)
    install_result = main(
        [
            "install",
            "123",
            "--offline",
            "--yes",
            "--jsonl",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert install_result == 0
    install_events = jsonl_events(capsys.readouterr().out)
    assert "item-started" in [event["event"] for event in install_events]
    assert "item-succeeded" in [event["event"] for event in install_events]

    restore_result = main(
        [
            "restore",
            "123",
            "--yes",
            "--jsonl",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert restore_result == 0
    restore_events = jsonl_events(capsys.readouterr().out)
    assert [event["event"] for event in restore_events] == [
        "plan",
        "item-started",
        "item-succeeded",
        "completed",
    ]
    assert restore_events[-1]["payload"]["succeeded"] == 1


def test_install_jsonl_reports_partial_failure(tmp_path: Path, monkeypatch, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    payload = b"missing"
    (steam / "steamapps" / "appmanifest_456.acf").write_text('"AppState" {}', encoding="utf-8")
    catalog_path = data_dir / "cache" / "index.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    catalog["entries"].append(
        {
            "game_id": "456",
            "game_name": "Missing Schema",
            "status": "current",
            "schema_file": "files/456/UserGameStatsSchema_456.bin",
            "sha256": hashlib.sha256(payload).hexdigest(),
            "file_size_bytes": len(payload),
            "achievement_count": 1,
        }
    )
    catalog_path.write_text(json.dumps(catalog), encoding="utf-8")
    monkeypatch.setattr("satl.cli.is_steam_running", lambda: False)

    result = main(
        [
            "install",
            "123",
            "456",
            "--offline",
            "--yes",
            "--jsonl",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 7
    events = jsonl_events(capsys.readouterr().out)
    assert any(event["event"] == "item-succeeded" for event in events)
    assert any(event["event"] == "item-failed" for event in events)
    assert events[-1]["payload"] == {"exit_code": 7, "failed": 1, "succeeded": 1}


def test_non_current_entry_requires_explicit_override(tmp_path: Path, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path, status="possibly-outdated")
    result = main(
        [
            "install",
            "123",
            "--offline",
            "--dry-run",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 2
    assert "--allow-outdated" in capsys.readouterr().err

    result = main(
        [
            "install",
            "123",
            "--offline",
            "--allow-outdated",
            "--dry-run",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 0


def test_restore_dry_run_does_not_change_installed_file(tmp_path: Path, monkeypatch, capsys) -> None:
    steam, data_dir = make_fixture(tmp_path)
    monkeypatch.setattr("satl.cli.is_steam_running", lambda: False)
    assert main(
        [
            "install",
            "123",
            "--offline",
            "--yes",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    ) == 0
    target = steam / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    before = target.read_bytes()
    state_before = (data_dir / "state.json").read_bytes()
    capsys.readouterr()

    result = main(
        [
            "restore",
            "123",
            "--dry-run",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    )
    assert result == 0
    assert target.read_bytes() == before
    assert (data_dir / "state.json").read_bytes() == state_before


def test_invalid_status_and_restore_ids_return_usage_error(tmp_path: Path, capsys) -> None:
    data_dir = tmp_path / "data"
    assert main(["status", "not-an-id", "--offline", "--data-dir", str(data_dir)]) == 2
    assert "无效" in capsys.readouterr().err

    steam, _ = make_fixture(tmp_path / "second")
    assert main(
        [
            "restore",
            "not-an-id",
            "--dry-run",
            "--steam-dir",
            str(steam),
            "--data-dir",
            str(data_dir),
        ]
    ) == 2
    assert "无效" in capsys.readouterr().err


def test_petition_export_creates_exact_template_zip(tmp_path: Path, capsys) -> None:
    steam, _ = make_fixture(tmp_path)
    schema = steam / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    schema.parent.mkdir(parents=True)
    schema.write_bytes(b"original-steam-schema")
    output = tmp_path / "exports" / "UserGameStatsSchema_123.zip"

    result = main(
        [
            "petition",
            "export",
            "123",
            "--steam-dir",
            str(steam),
            "--output",
            str(output),
            "--jsonl",
        ]
    )

    assert result == 0
    events = jsonl_events(capsys.readouterr().out)
    assert [event["event"] for event in events] == ["completed"]
    assert events[0]["operation"] == "petition-export"
    assert events[0]["payload"]["output"] == str(output.resolve())
    with zipfile.ZipFile(output) as archive:
        assert archive.namelist() == ["UserGameStatsSchema_123.bin"]
        assert archive.read("UserGameStatsSchema_123.bin") == b"original-steam-schema"


def test_petition_export_refuses_missing_source_and_existing_output(tmp_path: Path, capsys) -> None:
    steam, _ = make_fixture(tmp_path)
    output = tmp_path / "UserGameStatsSchema_123.zip"

    missing_result = main(
        [
            "petition",
            "export",
            "123",
            "--steam-dir",
            str(steam),
            "--output",
            str(output),
            "--jsonl",
        ]
    )
    assert missing_result == 3
    missing_events = jsonl_events(capsys.readouterr().out)
    assert missing_events[-1]["operation"] == "petition-export"
    assert "未找到" in missing_events[-1]["payload"]["message"]
    assert not output.exists()

    schema = steam / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    schema.parent.mkdir(parents=True)
    schema.write_bytes(b"original")
    output.write_bytes(b"keep-me")
    existing_result = main(
        [
            "petition",
            "export",
            "123",
            "--steam-dir",
            str(steam),
            "--output",
            str(output),
        ]
    )
    assert existing_result == 2
    assert "--overwrite" in capsys.readouterr().err
    assert output.read_bytes() == b"keep-me"


def test_petition_export_rejects_invalid_id_and_filename(tmp_path: Path, capsys) -> None:
    steam, _ = make_fixture(tmp_path)
    schema = steam / "appcache" / "stats" / "UserGameStatsSchema_123.bin"
    schema.parent.mkdir(parents=True)
    schema.write_bytes(b"original")

    assert main(
        [
            "petition",
            "export",
            "0",
            "--steam-dir",
            str(steam),
            "--output",
            str(tmp_path / "UserGameStatsSchema_0.zip"),
        ]
    ) == 2
    assert "无效" in capsys.readouterr().err

    assert main(
        [
            "petition",
            "export",
            "123",
            "--steam-dir",
            str(steam),
            "--output",
            str(tmp_path / "renamed.zip"),
        ]
    ) == 2
    assert "文件名必须" in capsys.readouterr().err
    assert not (tmp_path / "renamed.zip").exists()
