from __future__ import annotations

import json
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Callable

from satl.catalog import USER_AGENT

STORE_DETAILS_URL = "https://store.steampowered.com/api/appdetails"
STEAMCMD_DETAILS_URL = "https://api.steamcmd.net/v1/info"
MAX_RESPONSE_BYTES = 2 * 1024 * 1024
REQUEST_TIMEOUT_SECONDS = 12
MAX_ATTEMPTS = 3
RETRY_DELAY_SECONDS = 0.2


class SteamGameNameSources:
    """Look up one Steam app name without requiring a Steam Web API key."""

    def __init__(
        self,
        *,
        opener: Callable[..., object] = urllib.request.urlopen,
        sleeper: Callable[[float], None] = time.sleep,
    ) -> None:
        self._opener = opener
        self._sleeper = sleeper

    def fetch(self, app_id: str) -> str:
        failures: list[str] = []
        for provider, lookup in (
            ("Steam 商店", self._fetch_steam_store),
            ("SteamCMD API", self._fetch_steamcmd),
        ):
            try:
                name = lookup(app_id)
            except (OSError, ValueError, urllib.error.URLError) as exc:
                failures.append(f"{provider}：{self._describe_error(exc)}")
                continue
            if name:
                return name

        if failures:
            raise urllib.error.URLError(
                f"App ID {app_id} 的在线名称查询失败（{'；'.join(failures)}）"
            )
        return ""

    def _fetch_steam_store(self, app_id: str) -> str:
        query = urllib.parse.urlencode({"appids": app_id, "l": "schinese", "cc": "CN"})
        raw = self._read_json(f"{STORE_DETAILS_URL}?{query}", app_id, "Steam 商店")
        item = raw.get(app_id, {})
        if not isinstance(item, dict) or item.get("success") is not True:
            return ""
        data = item.get("data", {})
        name = data.get("name", "") if isinstance(data, dict) else ""
        return name.strip() if isinstance(name, str) else ""

    def _fetch_steamcmd(self, app_id: str) -> str:
        raw = self._read_json(f"{STEAMCMD_DETAILS_URL}/{app_id}", app_id, "SteamCMD API")
        data = raw.get("data", {})
        item = data.get(app_id, {}) if isinstance(data, dict) else {}
        common = item.get("common", {}) if isinstance(item, dict) else {}
        name = common.get("name", "") if isinstance(common, dict) else ""
        return name.strip() if isinstance(name, str) else ""

    def _read_json(self, url: str, app_id: str, provider: str) -> dict[str, object]:
        request = urllib.request.Request(
            url,
            headers={
                "Accept": "application/json",
                "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.7",
                "User-Agent": USER_AGENT,
            },
        )
        for attempt in range(MAX_ATTEMPTS):
            try:
                with self._opener(  # type: ignore[attr-defined]
                    request,
                    timeout=REQUEST_TIMEOUT_SECONDS,
                ) as response:
                    payload = response.read(MAX_RESPONSE_BYTES + 1)
                break
            except urllib.error.HTTPError as exc:
                if exc.code in {400, 404}:
                    return {}
                if exc.code not in {408, 429, 500, 502, 503, 504} or attempt + 1 >= MAX_ATTEMPTS:
                    raise
            except urllib.error.URLError:
                if attempt + 1 >= MAX_ATTEMPTS:
                    raise
            self._sleeper(RETRY_DELAY_SECONDS * (2**attempt))

        if len(payload) > MAX_RESPONSE_BYTES:
            raise ValueError(f"{provider} 返回的 App ID {app_id} 名称响应过大")
        try:
            raw = json.loads(payload.decode("utf-8"))
        except (UnicodeError, json.JSONDecodeError) as exc:
            raise ValueError(f"{provider} 返回了无效的 App ID {app_id} 名称响应") from exc
        if not isinstance(raw, dict):
            raise ValueError(f"{provider} 返回了无效的 App ID {app_id} 名称响应")
        return raw

    @staticmethod
    def _describe_error(exc: Exception) -> str:
        if isinstance(exc, urllib.error.HTTPError):
            return f"HTTP {exc.code} {exc.reason}"
        if isinstance(exc, urllib.error.URLError):
            return str(exc.reason)
        return str(exc)
