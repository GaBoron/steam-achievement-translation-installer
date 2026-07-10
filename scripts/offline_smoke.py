from __future__ import annotations

import hashlib
import json
import tempfile
from pathlib import Path

from satl.cli import main


def run() -> int:
    with tempfile.TemporaryDirectory(prefix="satl-smoke-") as raw:
        root = Path(raw)
        steam = root / "Steam"
        (steam / "steamapps").mkdir(parents=True)
        (steam / "steam.exe").write_bytes(b"")
        (steam / "steamapps" / "appmanifest_123.acf").write_text('"AppState" {}', encoding="utf-8")
        data_dir = root / "data"
        (data_dir / "cache").mkdir(parents=True)
        sample = b"sample"
        catalog = {
            "version": 1,
            "entries": [
                {
                    "game_id": "123",
                    "game_name": "Smoke Test",
                    "status": "current",
                    "schema_file": "files/123/UserGameStatsSchema_123.bin",
                    "sha256": hashlib.sha256(sample).hexdigest(),
                    "file_size_bytes": len(sample),
                    "achievement_count": 1,
                }
            ],
        }
        (data_dir / "cache" / "index.json").write_text(
            json.dumps(catalog), encoding="utf-8"
        )
        return main(
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


if __name__ == "__main__":
    raise SystemExit(run())
