from __future__ import annotations

import contextlib
import fnmatch
import ipaddress
import os
import random
import socket
import ssl
import struct
import threading
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any, Callable, Iterator


class NetworkConfigurationError(ValueError):
    pass


@dataclass(frozen=True)
class NetworkSettings:
    dns_mode: str = "system"
    dns_servers: tuple[tuple[str, int], ...] = ()
    dns_timeout: float = 5
    proxy_mode: str = "system"
    proxy_address: str = ""
    proxy_username: str = ""
    proxy_password: str = ""
    proxy_bypass: tuple[str, ...] = ()
    proxy_bypass_local: bool = True
    connect_timeout: float = 15

    @classmethod
    def from_environment(cls) -> NetworkSettings:
        dns_mode = _choice(os.environ.get("SATL_DNS_MODE", "system"), {"system", "custom"}, "DNS 模式")
        proxy_mode = _choice(
            os.environ.get("SATL_PROXY_MODE", "system"),
            {"system", "direct", "manual"},
            "代理模式",
        )
        dns_servers = tuple(_parse_dns_server(item) for item in _split_list(
            os.environ.get("SATL_DNS_SERVERS", "1.1.1.1; 8.8.8.8")
        ))
        if dns_mode == "custom" and not dns_servers:
            raise NetworkConfigurationError("自定义 DNS 至少需要填写一个服务器地址。")

        proxy_address = os.environ.get("SATL_PROXY_ADDRESS", "").strip()
        if proxy_mode == "manual":
            parsed = urllib.parse.urlsplit(proxy_address)
            if parsed.scheme not in {"http", "https"} or not parsed.hostname:
                raise NetworkConfigurationError(
                    "代理地址必须是完整的 http:// 或 https:// 地址，例如 http://127.0.0.1:7890。"
                )
            if parsed.username or parsed.password:
                raise NetworkConfigurationError("请单独填写代理用户名和密码，不要把凭据写进代理地址。")

        return cls(
            dns_mode=dns_mode,
            dns_servers=dns_servers,
            dns_timeout=_number("SATL_DNS_TIMEOUT", 5, 1, 30, "DNS 超时"),
            proxy_mode=proxy_mode,
            proxy_address=proxy_address,
            proxy_username=os.environ.get("SATL_PROXY_USERNAME", "").strip(),
            proxy_password=os.environ.get("SATL_PROXY_PASSWORD", ""),
            proxy_bypass=tuple(_split_list(os.environ.get("SATL_PROXY_BYPASS", ""))),
            proxy_bypass_local=os.environ.get("SATL_PROXY_BYPASS_LOCAL", "1") != "0",
            connect_timeout=_number("SATL_CONNECT_TIMEOUT", 15, 3, 120, "连接超时"),
        )


class NetworkTransport:
    def __init__(
        self,
        settings: NetworkSettings | None = None,
        *,
        system_opener: Callable[..., Any] | None = None,
        direct_opener: Callable[..., Any] | None = None,
    ) -> None:
        self.settings = settings or NetworkSettings.from_environment()
        self._system_opener = system_opener or urllib.request.urlopen
        self._direct_opener = direct_opener or urllib.request.build_opener(
            urllib.request.ProxyHandler({})
        ).open
        self._manual_opener = self._build_manual_opener()
        self._resolver = (
            CustomDnsResolver(self.settings.dns_servers, self.settings.dns_timeout)
            if self.settings.dns_mode == "custom"
            else None
        )

    def open(self, request: urllib.request.Request, timeout: float):
        effective_timeout = min(timeout, self.settings.connect_timeout)
        with _dns_override(self._resolver):
            if self.settings.proxy_mode == "direct" or self._should_bypass(request.host):
                return self._direct_opener(request, timeout=effective_timeout)
            if self.settings.proxy_mode == "manual":
                assert self._manual_opener is not None
                return self._manual_opener(request, timeout=effective_timeout)
            try:
                return self._system_opener(request, timeout=effective_timeout)
            except (OSError, urllib.error.URLError, TimeoutError) as proxy_error:
                if not urllib.request.getproxies():
                    raise
                try:
                    return self._direct_opener(request, timeout=effective_timeout)
                except (OSError, urllib.error.URLError, TimeoutError) as direct_error:
                    raise urllib.error.URLError(
                        "系统代理和无代理直连都无法建立连接"
                    ) from direct_error

    def _build_manual_opener(self):
        if self.settings.proxy_mode != "manual":
            return None
        parsed = urllib.parse.urlsplit(self.settings.proxy_address)
        if self.settings.proxy_username:
            username = urllib.parse.quote(self.settings.proxy_username, safe="")
            password = urllib.parse.quote(self.settings.proxy_password, safe="")
            credentials = f"{username}:{password}@"
            host = parsed.hostname or ""
            if ":" in host:
                host = f"[{host}]"
            port = f":{parsed.port}" if parsed.port else ""
            proxy_url = urllib.parse.urlunsplit(
                (parsed.scheme, f"{credentials}{host}{port}", parsed.path, parsed.query, parsed.fragment)
            )
        else:
            proxy_url = self.settings.proxy_address
        return urllib.request.build_opener(
            urllib.request.ProxyHandler({"http": proxy_url, "https": proxy_url})
        ).open

    def _should_bypass(self, host: str | None) -> bool:
        if not host or self.settings.proxy_mode != "manual":
            return False
        normalized = host.strip("[]").lower()
        for pattern in self.settings.proxy_bypass:
            if fnmatch.fnmatch(normalized, pattern.lower()):
                return True
        if not self.settings.proxy_bypass_local:
            return False
        if normalized == "localhost" or "." not in normalized:
            return True
        try:
            address = ipaddress.ip_address(normalized)
            return address.is_loopback or address.is_private or address.is_link_local
        except ValueError:
            return False


class CustomDnsResolver:
    def __init__(self, servers: tuple[tuple[str, int], ...], timeout: float) -> None:
        self._servers = servers
        self._timeout = timeout

    def resolve(self, host: str, family: int = socket.AF_UNSPEC) -> list[str]:
        try:
            return [str(ipaddress.ip_address(host))]
        except ValueError:
            pass
        record_types = []
        if family in (socket.AF_UNSPEC, socket.AF_INET):
            record_types.append(1)
        if family in (socket.AF_UNSPEC, socket.AF_INET6):
            record_types.append(28)
        for server in self._servers:
            addresses: list[str] = []
            for record_type in record_types:
                try:
                    addresses.extend(self._query(host, record_type, server))
                except (OSError, TimeoutError, ValueError):
                    continue
            if addresses:
                return list(dict.fromkeys(addresses))
        raise socket.gaierror(socket.EAI_NONAME, "custom DNS lookup failed")

    def _query(self, host: str, record_type: int, server: tuple[str, int]) -> list[str]:
        request_id = random.randint(0, 65535)
        query = _build_dns_query(host, record_type, request_id)
        address = ipaddress.ip_address(server[0])
        family = socket.AF_INET6 if address.version == 6 else socket.AF_INET
        target: tuple[Any, ...] = (str(address), server[1], 0, 0) if address.version == 6 else (
            str(address),
            server[1],
        )
        with socket.socket(family, socket.SOCK_DGRAM) as connection:
            connection.settimeout(self._timeout)
            connection.sendto(query, target)
            payload, _ = connection.recvfrom(4096)
        return _parse_dns_response(payload, request_id)


def describe_network_error(error: BaseException) -> str:
    current: BaseException = error
    if isinstance(current, urllib.error.HTTPError):
        return _http_error_message(current.code)
    if isinstance(current, urllib.error.URLError) and isinstance(current.reason, BaseException):
        current = current.reason
    if isinstance(current, socket.gaierror):
        return "无法解析服务器地址。请检查 DNS 设置，或改回“跟随系统”。"
    if isinstance(current, (TimeoutError, socket.timeout)):
        return "等待服务器响应超时。请检查网络、DNS 或代理设置。"
    if isinstance(current, ConnectionRefusedError):
        return "连接被拒绝。若正在使用代理，请确认代理已启动且地址和端口正确。"
    if isinstance(current, (ssl.SSLError, ssl.CertificateError)):
        return "无法建立安全连接。请检查系统时间、证书或代理的 HTTPS 设置。"
    if isinstance(current, OSError):
        return "无法建立网络连接。请检查网络、DNS 和代理设置。"
    return "暂时无法连接到在线服务。请检查网络、DNS 或代理设置后重试。"


def is_network_error(error: BaseException) -> bool:
    return isinstance(
        error,
        (OSError, urllib.error.URLError, urllib.error.HTTPError, TimeoutError),
    )


_DNS_OVERRIDE_LOCK = threading.RLock()
_ORIGINAL_GETADDRINFO = socket.getaddrinfo


@contextlib.contextmanager
def _dns_override(resolver: CustomDnsResolver | None) -> Iterator[None]:
    if resolver is None:
        yield
        return

    def custom_getaddrinfo(
        host: str | bytes | None,
        port: str | int | None,
        family: int = 0,
        socktype: int = 0,
        proto: int = 0,
        flags: int = 0,
    ):
        if host is None or isinstance(host, bytes) or flags & socket.AI_NUMERICHOST:
            return _ORIGINAL_GETADDRINFO(host, port, family, socktype, proto, flags)
        addresses = resolver.resolve(host, family)
        resolved_port = _service_port(port, socktype)
        results = []
        for value in addresses:
            address = ipaddress.ip_address(value)
            address_family = socket.AF_INET6 if address.version == 6 else socket.AF_INET
            if family not in (socket.AF_UNSPEC, 0, address_family):
                continue
            socket_address = (value, resolved_port, 0, 0) if address.version == 6 else (
                value,
                resolved_port,
            )
            results.append((address_family, socktype or socket.SOCK_STREAM, proto or 6, "", socket_address))
        if not results:
            raise socket.gaierror(socket.EAI_NONAME, "custom DNS lookup failed")
        return results

    with _DNS_OVERRIDE_LOCK:
        socket.getaddrinfo = custom_getaddrinfo
        try:
            yield
        finally:
            socket.getaddrinfo = _ORIGINAL_GETADDRINFO


def _build_dns_query(host: str, record_type: int, request_id: int) -> bytes:
    labels = host.rstrip(".").encode("idna").split(b".")
    name = bytearray()
    for label in labels:
        if not 0 < len(label) <= 63:
            raise ValueError("invalid DNS label")
        name.append(len(label))
        name.extend(label)
    name.append(0)
    return struct.pack("!HHHHHH", request_id, 0x0100, 1, 0, 0, 0) + bytes(name) + struct.pack(
        "!HH", record_type, 1
    )


def _parse_dns_response(payload: bytes, request_id: int) -> list[str]:
    if len(payload) < 12:
        raise ValueError("short DNS response")
    response_id, flags, questions, answers, _, _ = struct.unpack_from("!HHHHHH", payload)
    if response_id != request_id or flags & 0x000F:
        raise ValueError("invalid DNS response")
    offset = 12
    for _ in range(questions):
        offset = _skip_dns_name(payload, offset) + 4
        if offset > len(payload):
            raise ValueError("short DNS question")
    addresses: list[str] = []
    for _ in range(answers):
        offset = _skip_dns_name(payload, offset)
        if offset + 10 > len(payload):
            raise ValueError("short DNS answer")
        record_type, record_class, _, length = struct.unpack_from("!HHIH", payload, offset)
        offset += 10
        data = payload[offset : offset + length]
        if len(data) != length:
            raise ValueError("short DNS record")
        if record_class == 1 and record_type == 1 and length == 4:
            addresses.append(socket.inet_ntop(socket.AF_INET, data))
        elif record_class == 1 and record_type == 28 and length == 16:
            addresses.append(socket.inet_ntop(socket.AF_INET6, data))
        offset += length
    return addresses


def _skip_dns_name(payload: bytes, offset: int) -> int:
    while True:
        if offset >= len(payload):
            raise ValueError("short DNS name")
        length = payload[offset]
        offset += 1
        if length == 0:
            return offset
        if length & 0xC0 == 0xC0:
            if offset >= len(payload):
                raise ValueError("short DNS pointer")
            return offset + 1
        if length & 0xC0:
            raise ValueError("invalid DNS name")
        offset += length
        if offset > len(payload):
            raise ValueError("short DNS label")


def _parse_dns_server(value: str) -> tuple[str, int]:
    text = value.strip()
    try:
        return str(ipaddress.ip_address(text)), 53
    except ValueError:
        pass
    parsed = urllib.parse.urlsplit(f"dns://{text}")
    try:
        address = str(ipaddress.ip_address(parsed.hostname or ""))
        port = parsed.port
    except ValueError as error:
        raise NetworkConfigurationError(
            f"DNS 服务器“{text}”无效。请填写 IP 地址，可选端口格式为 1.1.1.1:53。"
        ) from error
    if port is None or not 1 <= port <= 65535:
        raise NetworkConfigurationError(f"DNS 服务器“{text}”的端口无效。")
    return address, port


def _split_list(value: str) -> list[str]:
    normalized = value.replace(",", ";").replace("\r", ";").replace("\n", ";")
    return [item.strip() for item in normalized.split(";") if item.strip()]


def _choice(value: str, choices: set[str], description: str) -> str:
    normalized = value.strip().lower()
    if normalized not in choices:
        raise NetworkConfigurationError(f"{description}无效。")
    return normalized


def _number(
    environment_name: str,
    default: float,
    minimum: float,
    maximum: float,
    description: str,
) -> float:
    raw = os.environ.get(environment_name, str(default))
    try:
        value = float(raw)
    except ValueError as error:
        raise NetworkConfigurationError(f"{description}必须是数字。") from error
    if not minimum <= value <= maximum:
        raise NetworkConfigurationError(f"{description}必须在 {minimum:g} 到 {maximum:g} 秒之间。")
    return value


def _service_port(port: str | int | None, socktype: int) -> int:
    if isinstance(port, int):
        return port
    if port is None:
        return 0
    return socket.getservbyname(port, "udp" if socktype == socket.SOCK_DGRAM else "tcp")


def _http_error_message(status: int) -> str:
    if status == 407:
        return "代理服务器需要身份验证。请检查代理用户名和密码。"
    if status in {401, 403}:
        return "服务器暂时拒绝访问，请稍后再试。"
    if status == 404:
        return "服务器上没有找到需要的内容，下载源可能已经变更。"
    if status in {408, 504}:
        return "服务器响应超时，请检查网络、DNS 或代理设置。"
    if status == 429:
        return "请求过于频繁，请稍后再试。"
    if status >= 500:
        return "在线服务暂时不可用，请稍后再试。"
    return f"服务器返回了 HTTP {status}，请稍后再试。"
