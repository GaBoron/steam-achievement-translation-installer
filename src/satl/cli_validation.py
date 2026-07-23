from __future__ import annotations

import sys
from typing import Sequence

from satl.errors import UsageError


def confirm(message: str, yes: bool) -> None:
    if yes:
        return
    if not sys.stdin.isatty():
        raise UsageError("非交互终端必须使用 --yes 明确确认")
    answer = input(f"{message} [y/N] ").strip().casefold()
    if answer not in {"y", "yes"}:
        raise UsageError("用户取消操作")


def validate_app_ids(values: Sequence[str]) -> list[str]:
    unique: set[str] = set()
    for value in values:
        if not value.isdigit() or len(value) > 20 or int(value) <= 0:
            raise UsageError(f"无效的 Steam App ID：{value}")
        unique.add(value)
    return sorted(unique, key=int)
