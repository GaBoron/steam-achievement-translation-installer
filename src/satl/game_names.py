from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
import uuid
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from satl.game_name_sources import SteamGameNameSources

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
        self._sources = SteamGameNameSources(opener=opener)

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
        errors: list[str] = []
        executor = ThreadPoolExecutor(max_workers=min(MAX_WORKERS, len(pending)))
        futures = {executor.submit(self._sources.fetch, app_id): app_id for app_id in pending}
        try:
            for future in as_completed(futures):
                app_id = futures[future]
                completed += 1
                try:
                    name = future.result()
                except (OSError, ValueError, urllib.error.URLError) as exc:
                    errors.append(str(exc))
                else:
                    if name:
                        names[app_id] = name
                        cached[app_id] = name
                        cache_changed = True
                if progress:
                    progress(completed, lookup_total, app_id)
        finally:
            executor.shutdown(wait=True, cancel_futures=False)

        error = ""
        if errors:
            error = errors[0]
            if len(errors) > 1:
                error += f"；另有 {len(errors) - 1} 个名称查询失败"
        if cache_changed:
            try:
                self._save_cache(cached)
            except OSError as exc:
                error = error or f"无法缓存联网查询到的游戏名称：{exc}"
        return GameNameResolution(names, attempted=completed, error=error)

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
