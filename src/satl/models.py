from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(frozen=True, slots=True)
class SchemaVariant:
    variant_id: str
    primary: bool
    schema_file: str
    sha256: str
    file_size_bytes: int
    note_zh: str = ""
    note_en: str = ""
    achievement_count: int | None = None


@dataclass(frozen=True, slots=True)
class CatalogEntry:
    app_id: str
    game_name: str
    status: str
    variants: tuple[SchemaVariant, ...]

    def primary_variant(self) -> SchemaVariant:
        return next(variant for variant in self.variants if variant.primary)

    def variant(self, variant_id: str) -> SchemaVariant:
        for variant in self.variants:
            if variant.variant_id == variant_id:
                return variant
        raise KeyError(variant_id)


@dataclass(frozen=True, slots=True)
class Catalog:
    version: int
    entries: dict[str, CatalogEntry]
    source: str = ""
    from_cache: bool = False


@dataclass(frozen=True, slots=True)
class SteamAccount:
    steam_id: str
    account_name: str
    persona_name: str
    most_recent: bool = False


@dataclass(slots=True)
class DiscoveryRecord:
    app_id: str
    game_name: str = ""
    discovery: set[str] = field(default_factory=set)
    accounts: set[str] = field(default_factory=set)
