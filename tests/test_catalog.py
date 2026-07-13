from __future__ import annotations

import hashlib
import json
from pathlib import Path
from urllib.error import URLError

import pytest

from satl import __version__
from satl.catalog import USER_AGENT, CatalogRepository, parse_catalog, verify_schema_file
from satl.errors import CatalogError, IntegrityError


def test_user_agent_uses_package_version() -> None:
    assert USER_AGENT.startswith(f"satl/{__version__} ")


def legacy_entry(data: bytes = b"default") -> dict[str, object]:
    return {
        "game_id": "123",
        "game_name": "Legacy Game",
        "status": "current",
        "schema_file": "files/123/UserGameStatsSchema_123.bin",
        "sha256": hashlib.sha256(data).hexdigest(),
        "file_size_bytes": len(data),
        "achievement_count": 2,
    }


def multi_entry(default: bytes = b"default", beta: bytes = b"beta") -> dict[str, object]:
    return {
        "game_id": "123",
        "game_name": "Multi Game",
        "status": "current",
        "schema_file": "files/123/UserGameStatsSchema_123.bin",
        "sha256": hashlib.sha256(default).hexdigest(),
        "file_size_bytes": len(default),
        "achievement_count": 2,
        "schema_files": [
            {
                "variant_id": "default",
                "primary": True,
                "schema_file": "files/123/UserGameStatsSchema_123.bin",
                "sha256": hashlib.sha256(default).hexdigest(),
                "file_size_bytes": len(default),
                "achievement_count": 2,
            },
            {
                "variant_id": "beta",
                "primary": False,
                "schema_file": "files/123/beta/UserGameStatsSchema_123.bin",
                "sha256": hashlib.sha256(beta).hexdigest(),
                "file_size_bytes": len(beta),
                "achievement_count": 3,
            },
        ],
    }


class Response:
    def __init__(self, data: bytes) -> None:
        self.data = data
        self.offset = 0

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False

    def read(self, size: int = -1) -> bytes:
        if size < 0:
            size = len(self.data) - self.offset
        result = self.data[self.offset : self.offset + size]
        self.offset += len(result)
        return result


def test_parse_legacy_entry() -> None:
    catalog = parse_catalog({"version": 1, "entries": [legacy_entry()]})
    entry = catalog.entries["123"]
    assert entry.primary_variant().variant_id == "default"
    assert entry.primary_variant().schema_file == "files/123/UserGameStatsSchema_123.bin"


def test_parse_multi_version_uses_each_variants_metadata() -> None:
    default = b"default"
    beta = b"beta-is-different"
    catalog = parse_catalog({"version": 1, "entries": [multi_entry(default, beta)]})
    entry = catalog.entries["123"]
    assert [item.variant_id for item in entry.variants] == ["default", "beta"]
    assert entry.variant("beta").sha256 == hashlib.sha256(beta).hexdigest()
    assert entry.variant("beta").sha256 != entry.primary_variant().sha256


@pytest.mark.parametrize(
    "path",
    [
        "../UserGameStatsSchema_123.bin",
        "files/124/UserGameStatsSchema_123.bin",
        "files/123/beta/../../evil.bin",
        r"files\123\UserGameStatsSchema_123.bin",
    ],
)
def test_catalog_rejects_paths_outside_exact_app_layout(path: str) -> None:
    entry = legacy_entry()
    entry["schema_file"] = path
    with pytest.raises(CatalogError):
        parse_catalog({"version": 1, "entries": [entry]})


def test_download_falls_back_and_verifies_variant_hash(tmp_path: Path) -> None:
    default = b"default"
    beta = b"beta-is-different"
    catalog = parse_catalog({"version": 1, "entries": [multi_entry(default, beta)]})
    calls: list[str] = []

    def opener(request, timeout):
        calls.append(request.full_url)
        if request.full_url.startswith("https://first.invalid"):
            raise URLError("first source down")
        return Response(beta)

    repository = CatalogRepository(
        tmp_path,
        opener=opener,
        roots=("https://first.invalid", "https://second.invalid"),
    )
    path = repository.download_schema(catalog.entries["123"].variant("beta"))
    assert path.read_bytes() == beta
    assert len(calls) == 2


def test_wrong_variant_checksum_is_a_hard_failure(tmp_path: Path) -> None:
    default = b"default"
    beta = b"beta-is-different"
    catalog = parse_catalog({"version": 1, "entries": [multi_entry(default, beta)]})

    def opener(_request, timeout):
        return Response(default)

    repository = CatalogRepository(tmp_path, opener=opener, roots=("https://only.invalid",))
    with pytest.raises(IntegrityError):
        repository.download_schema(catalog.entries["123"].variant("beta"))
    assert not repository.schema_cache_path(catalog.entries["123"].variant("beta")).exists()


def test_invalid_cached_schema_is_not_used_offline(tmp_path: Path) -> None:
    catalog = parse_catalog({"version": 1, "entries": [legacy_entry()]})
    variant = catalog.entries["123"].primary_variant()
    repository = CatalogRepository(tmp_path)
    path = repository.schema_cache_path(variant)
    path.parent.mkdir(parents=True)
    path.write_bytes(b"wrong")
    with pytest.raises(CatalogError):
        repository.download_schema(variant, offline=True)
    assert not path.exists()


def test_catalog_uses_valid_cache_when_network_is_unavailable(tmp_path: Path) -> None:
    payload = json.dumps({"version": 1, "entries": [legacy_entry()]}).encode()
    cache = tmp_path / "cache" / "index.json"
    cache.parent.mkdir(parents=True)
    cache.write_bytes(payload)

    def opener(_request, timeout):
        raise URLError("offline")

    repository = CatalogRepository(tmp_path, opener=opener, catalog_urls=("https://offline.invalid",))
    catalog = repository.load()
    assert catalog.from_cache is True
    assert "123" in catalog.entries


def test_catalog_retries_direct_when_configured_proxy_is_unavailable(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    payload = json.dumps({"version": 1, "entries": [legacy_entry()]}).encode()
    calls: list[str] = []

    def proxied_opener(request, timeout):
        calls.append(f"proxy:{request.full_url}")
        raise URLError("proxy refused connection")

    def direct_opener(request, timeout):
        calls.append(f"direct:{request.full_url}")
        return Response(payload)

    monkeypatch.setattr("satl.catalog.urllib.request.urlopen", proxied_opener)
    monkeypatch.setattr(
        "satl.catalog.urllib.request.getproxies",
        lambda: {"https": "http://127.0.0.1:9"},
    )
    repository = CatalogRepository(
        tmp_path,
        direct_opener=direct_opener,
        catalog_urls=("https://catalog.invalid",),
    )

    catalog = repository.refresh()

    assert "123" in catalog.entries
    assert calls == [
        "proxy:https://catalog.invalid",
        "direct:https://catalog.invalid",
    ]


def test_catalog_falls_back_after_invalid_first_source(tmp_path: Path) -> None:
    payload = json.dumps({"version": 1, "entries": [legacy_entry()]}).encode()
    responses = iter((b"not-json", payload))

    def opener(_request, timeout):
        return Response(next(responses))

    repository = CatalogRepository(
        tmp_path,
        opener=opener,
        catalog_urls=("https://first.invalid", "https://second.invalid"),
    )
    catalog = repository.refresh()
    assert catalog.source == "https://second.invalid"
    assert "123" in catalog.entries


def test_ephemeral_catalog_fetch_does_not_write_cache(tmp_path: Path) -> None:
    payload = json.dumps({"version": 1, "entries": [legacy_entry()]}).encode()

    def opener(_request, timeout):
        return Response(payload)

    repository = CatalogRepository(
        tmp_path,
        opener=opener,
        catalog_urls=("https://catalog.invalid",),
    )
    catalog = repository.load(persist=False)
    assert "123" in catalog.entries
    assert not repository.catalog_cache.exists()


def test_verify_schema_checks_size_and_hash(tmp_path: Path) -> None:
    catalog = parse_catalog({"version": 1, "entries": [legacy_entry()]})
    variant = catalog.entries["123"].primary_variant()
    path = tmp_path / "schema.bin"
    path.write_bytes(b"default")
    verify_schema_file(path, variant)
    path.write_bytes(b"tampered")
    with pytest.raises(IntegrityError):
        verify_schema_file(path, variant)
