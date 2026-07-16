from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Iterator

from satl.errors import PreflightError


@dataclass
class BinaryKeyValuesNode:
    type_id: int
    name: str
    children: list["BinaryKeyValuesNode"] = field(default_factory=list)
    value: str | None = None
    raw_value: bytes = b""


class _Reader:
    def __init__(self, data: bytes) -> None:
        self.data = data
        self.position = 0

    def read_byte(self) -> int:
        if self.position >= len(self.data):
            raise ValueError("读取类型时意外到达文件结尾")
        value = self.data[self.position]
        self.position += 1
        return value

    def read_bytes(self, count: int) -> bytes:
        if self.position + count > len(self.data):
            raise ValueError("读取值时意外到达文件结尾")
        value = self.data[self.position : self.position + count]
        self.position += count
        return value

    def read_cstring_bytes(self) -> bytes:
        end = self.data.find(b"\0", self.position)
        if end < 0:
            raise ValueError("字符串缺少 NUL 结束符")
        value = self.data[self.position : end]
        self.position = end + 1
        return value

    def read_cstring(self) -> str:
        return self.read_cstring_bytes().decode("utf-8")


def parse_binary_keyvalues(data: bytes) -> list[BinaryKeyValuesNode]:
    reader = _Reader(data)

    def parse_nodes() -> list[BinaryKeyValuesNode]:
        nodes: list[BinaryKeyValuesNode] = []
        while True:
            type_id = reader.read_byte()
            if type_id == 8:
                return nodes
            if type_id not in range(0, 8):
                raise ValueError(f"未知 Binary KeyValues 类型 {type_id}")
            name = reader.read_cstring()
            node = BinaryKeyValuesNode(type_id, name)
            if type_id == 0:
                node.children = parse_nodes()
            elif type_id == 1:
                node.raw_value = reader.read_cstring_bytes()
                node.value = node.raw_value.decode("utf-8")
            elif type_id in (2, 3, 4, 6):
                node.raw_value = reader.read_bytes(4)
            elif type_id == 7:
                node.raw_value = reader.read_bytes(8)
            elif type_id == 5:
                raise ValueError("暂不支持 WideString Binary KeyValues 节点")
            nodes.append(node)

    try:
        nodes = parse_nodes()
    except (UnicodeError, ValueError) as exc:
        raise PreflightError(f"无法解析 Binary KeyValues schema：{exc}") from exc
    if reader.position != len(data):
        raise PreflightError(
            f"Binary KeyValues 解析在偏移 {reader.position} 停止，文件大小为 {len(data)}"
        )
    return nodes


def serialize_binary_keyvalues(nodes: list[BinaryKeyValuesNode]) -> bytes:
    output = bytearray()
    for node in nodes:
        output.append(node.type_id)
        output.extend(node.name.encode("utf-8"))
        output.append(0)
        if node.type_id == 0:
            output.extend(serialize_binary_keyvalues(node.children))
        elif node.type_id == 1:
            output.extend(node.raw_value)
            output.append(0)
        elif node.type_id in (2, 3, 4, 6, 7):
            output.extend(node.raw_value)
        else:
            raise PreflightError(f"无法序列化 Binary KeyValues 类型 {node.type_id}")
    output.append(8)
    return bytes(output)


def achievement_preview(data: bytes) -> dict[str, Any]:
    nodes = parse_binary_keyvalues(data)
    if serialize_binary_keyvalues(nodes) != data:
        raise PreflightError("Binary KeyValues schema 未通过字节级 roundtrip 校验")

    rows: list[dict[str, Any]] = []
    for bits in (node for node in _walk(nodes) if node.type_id == 0 and node.name == "bits"):
        for achievement in bits.children:
            if achievement.type_id != 0:
                continue
            display_name = _nested_object(achievement, "display", "name")
            display_description = _nested_object(achievement, "display", "desc")
            api_name = _first_string(achievement, "name")
            if display_name is None or display_description is None or not api_name:
                continue
            names = _language_strings(display_name)
            descriptions = _language_strings(display_description)
            other_languages = []
            for language in sorted(set(names) | set(descriptions)):
                if language in {"schinese", "english"}:
                    continue
                name = names.get(language, "")
                description = descriptions.get(language, "")
                other_languages.append(f"{language}: {name}" + (f" — {description}" if description else ""))
            rows.append(
                {
                    "index": len(rows) + 1,
                    "api_name": api_name,
                    "schinese_name": names.get("schinese", ""),
                    "schinese_description": descriptions.get("schinese", ""),
                    "english_name": names.get("english", ""),
                    "english_description": descriptions.get("english", ""),
                    "other_languages": "\n".join(other_languages),
                }
            )
    return {
        "achievement_count": len(rows),
        "roundtrip_equal": True,
        "rows": rows,
    }


def _walk(nodes: list[BinaryKeyValuesNode]) -> Iterator[BinaryKeyValuesNode]:
    for node in nodes:
        yield node
        yield from _walk(node.children)


def _child_objects(node: BinaryKeyValuesNode, name: str) -> list[BinaryKeyValuesNode]:
    return [child for child in node.children if child.type_id == 0 and child.name == name]


def _nested_object(node: BinaryKeyValuesNode, *names: str) -> BinaryKeyValuesNode | None:
    current: BinaryKeyValuesNode | None = node
    for name in names:
        if current is None:
            return None
        matches = _child_objects(current, name)
        current = matches[0] if matches else None
    return current


def _first_string(node: BinaryKeyValuesNode, name: str) -> str:
    for child in node.children:
        if child.type_id == 1 and child.name == name:
            return child.value or ""
    return ""


def _language_strings(node: BinaryKeyValuesNode) -> dict[str, str]:
    result: dict[str, str] = {}
    for child in node.children:
        if child.type_id == 1 and child.name not in result:
            result[child.name] = child.value or ""
    return result
