from __future__ import annotations

from pathlib import Path

from satl.bkv import achievement_preview
from satl.errors import PreflightError


def detect_achievement_languages(schema_path: Path) -> tuple[str, ...]:
    """Return normalized achievement languages from a readable local Steam schema."""
    try:
        preview = achievement_preview(Path(schema_path).read_bytes())
    except (OSError, PreflightError):
        return ()

    languages = preview.get("languages", ())
    return tuple(
        language.casefold()
        for language in languages
        if isinstance(language, str) and language.strip()
    )
