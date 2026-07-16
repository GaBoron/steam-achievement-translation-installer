from __future__ import annotations

import hashlib
import json
import os
import re
import urllib.error
import urllib.parse
import urllib.request
import uuid
from dataclasses import replace
from pathlib import Path, PurePosixPath
from typing import Any, Callable

from satl import __version__
from satl.errors import CatalogError, IntegrityError
from satl.models import Catalog, CatalogEntry, SchemaVariant

REPOSITORY = "GaBoron/steam-achievement-translation-library"
RAW_ROOT = f"https://raw.githubusercontent.com/{REPOSITORY}/main"
JSDELIVR_ROOT = f"https://cdn.jsdelivr.net/gh/{REPOSITORY}@main"
CATALOG_URLS = (f"{RAW_ROOT}/index.json", f"{JSDELIVR_ROOT}/index.json")
APP_ID_RE = re.compile(r"^[0-9]+$")
VARIANT_ID_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,63}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
MAX_CATALOG_BYTES = 8 * 1024 * 1024
USER_AGENT = (
    f"satl/{__version__} "
    "(+https://github.com/GaBoron/steam-achievement-translation-installer)"
)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_schema_file(path: Path, variant: SchemaVariant) -> None:
    try:
        size = path.stat().st_size
    except OSError as exc:
        raise IntegrityError(f"无法读取下载文件：{path}：{exc}") from exc
    if size != variant.file_size_bytes:
        raise IntegrityError(
            f"文件大小不匹配：{variant.schema_file}，期望 {variant.file_size_bytes}，实际 {size}"
        )
    actual = sha256_file(path)
    if actual != variant.sha256:
        raise IntegrityError(
            f"SHA-256 不匹配：{variant.schema_file}，期望 {variant.sha256}，实际 {actual}"
        )


def verify_schema_bytes(payload: bytes, variant: SchemaVariant) -> None:
    if len(payload) != variant.file_size_bytes:
        raise IntegrityError(
            f"文件大小不匹配：{variant.schema_file}，期望 {variant.file_size_bytes}，实际 {len(payload)}"
        )
    actual = hashlib.sha256(payload).hexdigest()
    if actual != variant.sha256:
        raise IntegrityError(
            f"SHA-256 不匹配：{variant.schema_file}，期望 {variant.sha256}，实际 {actual}"
        )


def _require_string(raw: dict[str, Any], key: str, context: str) -> str:
    value = raw.get(key)
    if not isinstance(value, str) or not value.strip():
        raise CatalogError(f"{context} 的 {key} 无效")
    return value.strip()


def _variant_path(app_id: str, variant_id: str, primary: bool) -> str:
    filename = f"UserGameStatsSchema_{app_id}.bin"
    if primary:
        return f"files/{app_id}/{filename}"
    return f"files/{app_id}/{variant_id}/{filename}"


def _parse_variant(app_id: str, raw: dict[str, Any], *, explicit: bool) -> SchemaVariant:
    if explicit:
        variant_id = _require_string(raw, "variant_id", f"{app_id} 版本").lower()
        primary = raw.get("primary") is True
    else:
        variant_id = "default"
        primary = True

    if not VARIANT_ID_RE.fullmatch(variant_id):
        raise CatalogError(f"{app_id} 的 variant_id 无效：{variant_id}")
    if primary != (variant_id == "default"):
        raise CatalogError(f"{app_id}/{variant_id} 的 primary 标记无效")

    schema_file = _require_string(raw, "schema_file", f"{app_id}/{variant_id}")
    path = PurePosixPath(schema_file)
    if path.is_absolute() or ".." in path.parts or "\\" in schema_file:
        raise CatalogError(f"{app_id}/{variant_id} 的 schema_file 越界：{schema_file}")
    expected = _variant_path(app_id, variant_id, primary)
    if schema_file != expected:
        raise CatalogError(
            f"{app_id}/{variant_id} 的 schema_file 必须是 {expected}，实际为 {schema_file}"
        )

    sha256 = _require_string(raw, "sha256", f"{app_id}/{variant_id}").lower()
    if not SHA256_RE.fullmatch(sha256):
        raise CatalogError(f"{app_id}/{variant_id} 的 SHA-256 无效")
    size = raw.get("file_size_bytes")
    if not isinstance(size, int) or isinstance(size, bool) or size <= 0 or size > 32 * 1024 * 1024:
        raise CatalogError(f"{app_id}/{variant_id} 的 file_size_bytes 无效")
    count = raw.get("achievement_count")
    if count is not None and (not isinstance(count, int) or isinstance(count, bool) or count < 0):
        raise CatalogError(f"{app_id}/{variant_id} 的 achievement_count 无效")
    return SchemaVariant(
        variant_id=variant_id,
        primary=primary,
        schema_file=schema_file,
        sha256=sha256,
        file_size_bytes=size,
        note_zh=str(raw.get("note_zh") or ""),
        note_en=str(raw.get("note_en") or ""),
        achievement_count=count,
    )


def parse_catalog(payload: bytes | str | dict[str, Any], *, source: str = "") -> Catalog:
    if isinstance(payload, bytes):
        try:
            raw = json.loads(payload.decode("utf-8"))
        except (UnicodeError, json.JSONDecodeError) as exc:
            raise CatalogError(f"index.json 不是有效的 UTF-8 JSON：{exc}") from exc
    elif isinstance(payload, str):
        try:
            raw = json.loads(payload)
        except json.JSONDecodeError as exc:
            raise CatalogError(f"index.json 不是有效 JSON：{exc}") from exc
    else:
        raw = payload
    if not isinstance(raw, dict) or raw.get("version") != 1:
        raise CatalogError("仅支持 index.json version=1")
    raw_entries = raw.get("entries")
    if not isinstance(raw_entries, list):
        raise CatalogError("index.json entries 必须是数组")

    entries: dict[str, CatalogEntry] = {}
    for position, item in enumerate(raw_entries, 1):
        if not isinstance(item, dict):
            raise CatalogError(f"entries[{position}] 必须是对象")
        app_id = _require_string(item, "game_id", f"entries[{position}]")
        if not APP_ID_RE.fullmatch(app_id):
            raise CatalogError(f"无效的 Steam App ID：{app_id}")
        if app_id in entries:
            raise CatalogError(f"重复的 Steam App ID：{app_id}")
        game_name = _require_string(item, "game_name", app_id)
        status = _require_string(item, "status", app_id)
        raw_variants = item.get("schema_files")
        if raw_variants is None:
            variants = (_parse_variant(app_id, item, explicit=False),)
        else:
            if not isinstance(raw_variants, list) or not raw_variants:
                raise CatalogError(f"{app_id} 的 schema_files 必须是非空数组")
            parsed: list[SchemaVariant] = []
            seen: set[str] = set()
            for raw_variant in raw_variants:
                if not isinstance(raw_variant, dict):
                    raise CatalogError(f"{app_id} 的版本记录必须是对象")
                variant = _parse_variant(app_id, raw_variant, explicit=True)
                if variant.variant_id in seen:
                    raise CatalogError(f"{app_id} 包含重复版本：{variant.variant_id}")
                seen.add(variant.variant_id)
                parsed.append(variant)
            if sum(variant.primary for variant in parsed) != 1:
                raise CatalogError(f"{app_id} 必须且只能包含一个 default 主版本")
            variants = tuple(sorted(parsed, key=lambda value: (not value.primary, value.variant_id)))
        entries[app_id] = CatalogEntry(app_id, game_name, status, variants)
    return Catalog(version=1, entries=entries, source=source)


class CatalogRepository:
    def __init__(
        self,
        data_dir: Path,
        *,
        opener: Callable[..., Any] | None = None,
        direct_opener: Callable[..., Any] | None = None,
        catalog_urls: tuple[str, ...] = CATALOG_URLS,
        roots: tuple[str, ...] = (RAW_ROOT, JSDELIVR_ROOT),
    ) -> None:
        self.data_dir = Path(data_dir)
        self.catalog_urls = catalog_urls
        self.roots = roots
        self._opener = opener
        self._direct_opener = direct_opener or urllib.request.build_opener(
            urllib.request.ProxyHandler({})
        ).open

    @property
    def catalog_cache(self) -> Path:
        return self.data_dir / "cache" / "index.json"

    def _open(self, url: str, timeout: float):
        request = urllib.request.Request(
            url,
            headers={
                "User-Agent": USER_AGENT,
                "Cache-Control": "no-cache",
                "Pragma": "no-cache",
            },
        )
        if self._opener is not None:
            return self._opener(request, timeout=timeout)
        try:
            return urllib.request.urlopen(request, timeout=timeout)
        except (OSError, urllib.error.URLError, TimeoutError) as proxy_error:
            if not urllib.request.getproxies():
                raise
            try:
                return self._direct_opener(request, timeout=timeout)
            except (OSError, urllib.error.URLError, TimeoutError) as direct_error:
                raise urllib.error.URLError(
                    f"代理连接失败：{proxy_error}；无代理直连失败：{direct_error}"
                ) from direct_error

    def _fetch_catalog(self, *, persist: bool = True) -> Catalog:
        errors: list[str] = []
        for url in self.catalog_urls:
            try:
                separator = "&" if "?" in url else "?"
                request_url = f"{url}{separator}satl_refresh={uuid.uuid4().hex}"
                with self._open(request_url, 15) as response:
                    payload = response.read(MAX_CATALOG_BYTES + 1)
                if len(payload) > MAX_CATALOG_BYTES:
                    raise CatalogError("index.json 超过 8 MiB 安全上限")
                catalog = parse_catalog(payload, source=url)
                if persist:
                    self._atomic_write(self.catalog_cache, payload)
                return catalog
            except CatalogError as exc:
                errors.append(f"{url}: {exc}")
            except (OSError, urllib.error.URLError, TimeoutError) as exc:
                errors.append(f"{url}: {exc}")
        raise CatalogError("无法下载 index.json：" + "；".join(errors))

    def refresh(self) -> Catalog:
        return self._fetch_catalog(persist=True)

    def load(
        self,
        *,
        offline: bool = False,
        refresh: bool = True,
        persist: bool = True,
    ) -> Catalog:
        network_error: CatalogError | None = None
        if not offline and refresh:
            try:
                return self._fetch_catalog(persist=persist)
            except CatalogError as exc:
                network_error = exc
        if self.catalog_cache.is_file():
            try:
                catalog = parse_catalog(self.catalog_cache.read_bytes(), source=str(self.catalog_cache))
                return replace(catalog, from_cache=True)
            except (OSError, CatalogError) as exc:
                if offline:
                    raise CatalogError(f"缓存的 index.json 无效：{exc}") from exc
        if network_error is not None:
            raise network_error
        raise CatalogError("离线模式下没有可用的 index.json 缓存")

    def schema_cache_path(self, variant: SchemaVariant) -> Path:
        return self.data_dir / "cache" / "schemas" / f"{variant.sha256}.bin"

    def download_schema(self, variant: SchemaVariant, *, offline: bool = False) -> Path:
        destination = self.schema_cache_path(variant)
        if destination.is_file():
            try:
                verify_schema_file(destination, variant)
                return destination
            except IntegrityError:
                try:
                    destination.unlink()
                except OSError as exc:
                    raise IntegrityError(f"无法移除损坏的缓存文件：{destination}：{exc}") from exc
        if offline:
            raise CatalogError(f"离线缓存中没有 {variant.schema_file}")

        failures: list[SatlDownloadFailure] = []
        quoted = urllib.parse.quote(variant.schema_file, safe="/")
        for root in self.roots:
            url = f"{root}/{quoted}"
            try:
                self._download_and_verify(url, destination, variant)
                return destination
            except IntegrityError as exc:
                failures.append(SatlDownloadFailure(url, exc, True))
            except (OSError, urllib.error.URLError, TimeoutError) as exc:
                failures.append(SatlDownloadFailure(url, exc, False))
        integrity = next((failure for failure in failures if failure.integrity), None)
        details = "；".join(f"{failure.url}: {failure.error}" for failure in failures)
        if integrity is not None:
            raise IntegrityError("所有来源均未提供通过校验的文件：" + details)
        raise CatalogError("无法下载 schema：" + details)

    def read_schema_bytes(self, variant: SchemaVariant, *, offline: bool = False) -> bytes:
        """Read a verified schema for preview without mutating the schema cache."""
        cached = self.schema_cache_path(variant)
        if cached.is_file():
            try:
                payload = cached.read_bytes()
                verify_schema_bytes(payload, variant)
                return payload
            except (OSError, IntegrityError):
                if offline:
                    raise CatalogError(f"离线缓存中的 schema 无效：{variant.schema_file}")
        if offline:
            raise CatalogError(f"离线缓存中没有 {variant.schema_file}")

        failures: list[SatlDownloadFailure] = []
        quoted = urllib.parse.quote(variant.schema_file, safe="/")
        for root in self.roots:
            url = f"{root}/{quoted}"
            try:
                with self._open(url, 30) as response:
                    payload = response.read(variant.file_size_bytes + 1)
                verify_schema_bytes(payload, variant)
                return payload
            except IntegrityError as exc:
                failures.append(SatlDownloadFailure(url, exc, True))
            except (OSError, urllib.error.URLError, TimeoutError) as exc:
                failures.append(SatlDownloadFailure(url, exc, False))
        integrity = next((failure for failure in failures if failure.integrity), None)
        details = "；".join(f"{failure.url}: {failure.error}" for failure in failures)
        if integrity is not None:
            raise IntegrityError("所有来源均未提供通过校验的文件：" + details)
        raise CatalogError("无法读取 schema 预览：" + details)

    def _download_and_verify(self, url: str, destination: Path, variant: SchemaVariant) -> None:
        destination.parent.mkdir(parents=True, exist_ok=True)
        partial = destination.with_name(f".{destination.name}.{uuid.uuid4().hex}.part")
        try:
            with self._open(url, 30) as response, partial.open("xb") as handle:
                total = 0
                while True:
                    chunk = response.read(1024 * 1024)
                    if not chunk:
                        break
                    total += len(chunk)
                    if total > variant.file_size_bytes:
                        raise IntegrityError(f"下载内容大于声明大小：{variant.schema_file}")
                    handle.write(chunk)
                handle.flush()
                os.fsync(handle.fileno())
            verify_schema_file(partial, variant)
            os.replace(partial, destination)
        finally:
            partial.unlink(missing_ok=True)

    @staticmethod
    def _atomic_write(path: Path, payload: bytes) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
        try:
            with temporary.open("xb") as handle:
                handle.write(payload)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, path)
        except OSError as exc:
            raise CatalogError(f"无法写入缓存：{path}：{exc}") from exc
        finally:
            temporary.unlink(missing_ok=True)


class SatlDownloadFailure:
    def __init__(self, url: str, error: BaseException, integrity: bool) -> None:
        self.url = url
        self.error = error
        self.integrity = integrity
