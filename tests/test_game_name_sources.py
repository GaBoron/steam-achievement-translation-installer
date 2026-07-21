from __future__ import annotations

import json
import urllib.error

from satl.game_name_sources import SteamGameNameSources


class FakeResponse:
    def __init__(self, payload: dict[str, object]) -> None:
        self.payload = json.dumps(payload).encode("utf-8")

    def __enter__(self) -> FakeResponse:
        return self

    def __exit__(self, *args: object) -> None:
        return None

    def read(self, maximum: int) -> bytes:
        return self.payload[:maximum]


def test_transient_source_error_is_retried_with_backoff() -> None:
    steamcmd_attempts = 0
    delays: list[float] = []

    def opener(request: object, *, timeout: int) -> FakeResponse:
        nonlocal steamcmd_attempts
        url = request.full_url  # type: ignore[attr-defined]
        if "store.steampowered.com" in url:
            return FakeResponse({"123": {"success": False}})
        steamcmd_attempts += 1
        if steamcmd_attempts == 1:
            raise urllib.error.URLError("temporary TLS failure")
        return FakeResponse(
            {"data": {"123": {"common": {"name": "Recovered Game"}}}}
        )

    name = SteamGameNameSources(opener=opener, sleeper=delays.append).fetch("123")

    assert name == "Recovered Game"
    assert steamcmd_attempts == 2
    assert delays == [0.2]
