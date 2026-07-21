from __future__ import annotations

import json
from pathlib import Path

from satl.game_names import SteamGameNameResolver


class FakeResponse:
    def __init__(self, payload: dict[str, object]) -> None:
        self.payload = json.dumps(payload).encode("utf-8")

    def __enter__(self) -> FakeResponse:
        return self

    def __exit__(self, *args: object) -> None:
        return None

    def read(self, maximum: int) -> bytes:
        return self.payload[:maximum]


def test_resolver_uses_steam_store_and_caches_success(tmp_path: Path) -> None:
    calls: list[str] = []

    def opener(request: object, *, timeout: int) -> FakeResponse:
        calls.append(request.full_url)  # type: ignore[attr-defined]
        assert timeout == 8
        return FakeResponse(
            {"105600": {"success": True, "data": {"name": "Terraria"}}}
        )

    progress: list[tuple[int, int, str]] = []
    result = SteamGameNameResolver(tmp_path, opener=opener).resolve_many(
        ["105600"],
        lambda current, total, app_id: progress.append((current, total, app_id)),
    )

    assert result.names == {"105600": "Terraria"}
    assert progress == [(1, 1, "105600")]
    assert "store.steampowered.com" in calls[0]

    def reject_network(*args: object, **kwargs: object) -> object:
        raise AssertionError("cached names must not be queried again")

    cached = SteamGameNameResolver(tmp_path, opener=reject_network).resolve_many(["105600"])
    assert cached.names == {"105600": "Terraria"}
    assert cached.attempted == 0


def test_resolver_falls_back_to_steamspy(tmp_path: Path) -> None:
    calls: list[str] = []

    def opener(request: object, *, timeout: int) -> FakeResponse:
        url = request.full_url  # type: ignore[attr-defined]
        calls.append(url)
        if "store.steampowered.com" in url:
            return FakeResponse({"123": {"success": False}})
        return FakeResponse({"appid": 123, "name": "Fallback Game"})

    result = SteamGameNameResolver(tmp_path, opener=opener).resolve_many(["123"])

    assert result.names == {"123": "Fallback Game"}
    assert len(calls) == 2
    assert "steamspy.com" in calls[1]
