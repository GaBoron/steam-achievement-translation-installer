from __future__ import annotations

import json
import os
import urllib.error
import urllib.parse
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from satl.catalog import USER_AGENT

STORE_DETAILS_URL = "https://store.steampowered.com/api/appdetails"
STEAMSPY_DETAILS_URL = "https://steamspy.com/api.php"
MAX_RESPONSE_BYTES = 2 * 1024 * 1024
MAX_WORKERS = 4


@dataclass(frozen=True, slots=True)
class GameNameResolution:
    names: dict[str, str]
    attempted: int
    error: str = ""


class SteamGameNameResolver:
    def __init__(
        self,
        data_dir: Path,
        *,
        opener: Callable[..., object] = urllib.request.urlopen,
    ) -> None:
        self.cache_path = Path(data_dir) / "cache" / "steam-game-names.json"
        self._opener = opener

    def resolve_many(
        self,
        app_ids: list[str],
        progress: Callable[[int, int, str], None] | None = None,
    ) -> GameNameResolution:
        requested = list(dict.fromkeys(app_ids))
        cached = self._load_cache()
        names = {app_id: cached[app_id] for app_id in requested if app_id in cached}
        pending = [app_id for app_id in requested if app_id not in names]
        if not pending:
            return GameNameResolution(names, attempted=0)

        lookup_total = len(pending)
        cache_changed = False
        completed = 0
        first_id = pending.pop(0)
        try:
            first_name = self._fetch(first_id)
        except (OSError, ValueError, urllib.error.URLError) as exc:
            return GameNameResolution(names, attempted=1, error=str(exc))
        completed += 1
        if first_name:
            names[first_id] = first_name
            cached[first_id] = first_name
            cache_changed = True
        if progress:
            progress(completed, lookup_total, first_id)

        error = ""
        if pending:
            executor = ThreadPoolExecutor(max_workers=min(MAX_WORKERS, len(pending)))
            futures = {executor.submit(self._fetch, app_id): app_id for app_id in pending}
            try:
                for future in as_completed(futures):
                    app_id = futures[future]
                    completed += 1
                    try:
                        name = future.result()
                    except (OSError, ValueError, urllib.error.URLError) as exc:
                        error = str(exc)
                        for remaining in futures:
                            remaining.cancel()
                        break
                    if name:
                        names[app_id] = name
                        cached[app_id] = name
                        cache_changed = True
                    if progress:
                        progress(completed, lookup_total, app_id)
            finally:
                executor.shutdown(wait=True, cancel_futures=True)

        if cache_changed:
            try:
                self._save_cache(cached)
            except OSError as exc:
                error = error or f"无法缓存联网查询到的游戏名称：{exc}"
        return GameNameResolution(names, attempted=completed, error=error)

    def _fetch(self, app_id: str) -> str:
        store_error: Exception | None = None
        try:
            name = self._fetch_steam_store(app_id)
            if name:
                return name
        except (OSError, ValueError, urllib.error.URLError) as exc:
            store_error = exc

        try:
            return self._fetch_steamspy(app_id)
        except (OSError, ValueError, urllib.error.URLError) as exc:
            if store_error is not None:
                raise urllib.error.URLError(
                    f"Steam 商店与 SteamSpy 均查询失败：{store_error}；{exc}"
                ) from exc
            raise

    def _fetch_steam_store(self, app_id: str) -> str:
        query = urllib.parse.urlencode({"appids": app_id, "l": "schinese", "cc": "CN"})
        payload = self._read_json(f"{STORE_DETAILS_URL}?{query}", app_id, "Steam 商店")
        try:
            raw = json.loads(payload)
            item = raw.get(app_id, {})
            data = item.get("data", {}) if item.get("success") is True else {}
            name = data.get("name", "")
        except (json.JSONDecodeError, AttributeError) as exc:
            raise ValueError(f"Steam 商店返回了无效的 App ID {app_id} 名称响应") from exc
        return name.strip() if isinstance(name, str) else ""

    def _fetch_steamspy(self, app_id: str) -> str:
        query = urllib.parse.urlencode({"request": "appdetails", "appid": app_id})
        payload = self._read_json(f"{STEAMSPY_DETAILS_URL}?{query}", app_id, "SteamSpy")
        try:
            raw = json.loads(payload)
            name = raw.get("name", "")
        except (json.JSONDecodeError, AttributeError) as exc:
            raise ValueError(f"SteamSpy 返回了无效的 App ID {app_id} 名称响应") from exc
        return name.strip() if isinstance(name, str) else ""

    def _read_json(self, url: str, app_id: str, provider: str) -> str:
        request = urllib.request.Request(
            url,
            headers={"Accept": "application/json", "User-Agent": USER_AGENT},
        )
        try:
            with self._opener(request, timeout=8) as response:  # type: ignore[attr-defined]
                payload = response.read(MAX_RESPONSE_BYTES + 1)
        except urllib.error.HTTPError as exc:
            if exc.code in {400, 404}:
                return "{}"
            raise
        if len(payload) > MAX_RESPONSE_BYTES:
            raise ValueError(f"{provider} 返回的 App ID {app_id} 名称响应过大")
        try:
            return payload.decode("utf-8")
        except UnicodeError as exc:
            raise ValueError(f"{provider} 返回了无效的 App ID {app_id} 名称编码") from exc

    def _load_cache(self) -> dict[str, str]:
        try:
            raw = json.loads(self.cache_path.read_text(encoding="utf-8"))
            names = raw.get("names", {}) if raw.get("version") == 1 else {}
            if not isinstance(names, dict):
                return {}
            return {
                str(app_id): name.strip()
                for app_id, name in names.items()
                if str(app_id).isdigit() and isinstance(name, str) and name.strip()
            }
        except (OSError, UnicodeError, json.JSONDecodeError, AttributeError):
            return {}

    def _save_cache(self, names: dict[str, str]) -> None:
        self.cache_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.cache_path.with_name(f".{self.cache_path.name}.{uuid.uuid4().hex}.tmp")
        try:
            payload = json.dumps(
                {"version": 1, "names": dict(sorted(names.items(), key=lambda item: int(item[0])))},
                ensure_ascii=False,
                indent=2,
            )
            with temporary.open("x", encoding="utf-8", newline="\n") as handle:
                handle.write(payload)
                handle.write("\n")
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, self.cache_path)
        finally:
            temporary.unlink(missing_ok=True)
