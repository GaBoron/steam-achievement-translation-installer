from __future__ import annotations

from pathlib import Path

from satl.errors import PreflightError


def tokenize_vdf(text: str) -> list[str]:
    tokens: list[str] = []
    index = 0
    length = len(text)
    while index < length:
        char = text[index]
        if char.isspace():
            index += 1
            continue
        if char == "/" and index + 1 < length and text[index + 1] == "/":
            index += 2
            while index < length and text[index] not in "\r\n":
                index += 1
            continue
        if char in "{}":
            tokens.append(char)
            index += 1
            continue
        if char == '"':
            index += 1
            value: list[str] = []
            while index < length:
                char = text[index]
                if char == '"':
                    index += 1
                    break
                if char == "\\" and index + 1 < length:
                    following = text[index + 1]
                    if following in {'"', "\\"}:
                        value.append(following)
                        index += 2
                        continue
                value.append(char)
                index += 1
            else:
                raise ValueError("VDF 包含未闭合的字符串")
            tokens.append("".join(value))
            continue

        start = index
        while index < length and not text[index].isspace() and text[index] not in '{}"':
            index += 1
        tokens.append(text[start:index])
    return tokens


def parse_vdf(text: str) -> dict[str, object]:
    tokens = tokenize_vdf(text)
    position = 0

    def parse_object(expect_closing: bool = False) -> dict[str, object]:
        nonlocal position
        result: dict[str, object] = {}
        while position < len(tokens):
            token = tokens[position]
            if token == "}":
                if not expect_closing:
                    raise ValueError("VDF 包含多余的右括号")
                position += 1
                return result
            if token == "{":
                raise ValueError("VDF 对象缺少键")
            key = token
            position += 1
            if position >= len(tokens):
                raise ValueError(f"VDF 键 {key!r} 缺少值")
            token = tokens[position]
            position += 1
            if token == "{":
                value: object = parse_object(expect_closing=True)
            elif token == "}":
                raise ValueError(f"VDF 键 {key!r} 缺少值")
            else:
                value = token
            result[key] = value
        if expect_closing:
            raise ValueError("VDF 包含未闭合的对象")
        return result

    parsed = parse_object()
    if position != len(tokens):
        raise ValueError("VDF 尾部包含无法解析的数据")
    return parsed


def load_vdf(path: Path) -> dict[str, object]:
    try:
        return parse_vdf(path.read_text(encoding="utf-8-sig", errors="strict"))
    except (OSError, UnicodeError, ValueError) as exc:
        raise PreflightError(f"无法解析 VDF：{path}：{exc}") from exc


def get_casefold(mapping: object, key: str, default: object = None) -> object:
    if not isinstance(mapping, dict):
        return default
    wanted = key.casefold()
    for current, value in mapping.items():
        if str(current).casefold() == wanted:
            return value
    return default
