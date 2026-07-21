from __future__ import annotations

import json
import urllib.error
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
        assert timeout == 12
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


def test_resolver_falls_back_to_steamcmd_for_hidden_store_app(tmp_path: Path) -> None:
    calls: list[str] = []

    def opener(request: object, *, timeout: int) -> FakeResponse:
        url = request.full_url  # type: ignore[attr-defined]
        calls.append(url)
        if "store.steampowered.com" in url:
            return FakeResponse({"123": {"success": False}})
        return FakeResponse(
            {
                "data": {"123": {"common": {"name": "Fallback Game"}}},
                "status": "success",
            }
        )

    result = SteamGameNameResolver(tmp_path, opener=opener).resolve_many(["123"])

    assert result.names == {"123": "Fallback Game"}
    assert len(calls) == 2
    assert "api.steamcmd.net/v1/info/123" in calls[1]


def test_one_http_error_does_not_cancel_other_name_queries(tmp_path: Path) -> None:
    def opener(request: object, *, timeout: int) -> FakeResponse:
        url = request.full_url  # type: ignore[attr-defined]
        if "store.steampowered.com" in url:
            app_id = "1" if "appids=1" in url else "2"
            return FakeResponse({app_id: {"success": False}})
        if url.endswith("/1"):
            raise urllib.error.HTTPError(url, 403, "Forbidden", {}, None)
        return FakeResponse(
            {"data": {"2": {"common": {"name": "Second Game"}}}, "status": "success"}
        )

    progress: list[tuple[int, int, str]] = []
    result = SteamGameNameResolver(tmp_path, opener=opener).resolve_many(
        ["1", "2"],
        lambda current, total, app_id: progress.append((current, total, app_id)),
    )

    assert result.names == {"2": "Second Game"}
    assert result.attempted == 2
    assert "App ID 1" in result.error
    assert "SteamCMD API：HTTP 403 Forbidden" in result.error
    assert [item[:2] for item in progress] == [(1, 2), (2, 2)]
    assert {item[2] for item in progress} == {"1", "2"}
