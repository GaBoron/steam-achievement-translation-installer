from __future__ import annotations

import socket
import urllib.error
import urllib.request

import pytest

from satl.network import (
    NetworkConfigurationError,
    NetworkSettings,
    NetworkTransport,
    _build_dns_query,
    describe_network_error,
)


class Response:
    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False


def test_network_settings_read_general_desktop_defaults(monkeypatch: pytest.MonkeyPatch) -> None:
    for name in (
        "SATL_DNS_MODE",
        "SATL_DNS_SERVERS",
        "SATL_PROXY_MODE",
        "SATL_PROXY_ADDRESS",
    ):
        monkeypatch.delenv(name, raising=False)

    settings = NetworkSettings.from_environment()

    assert settings.dns_mode == "system"
    assert settings.proxy_mode == "system"
    assert settings.proxy_bypass_local is True


def test_manual_proxy_requires_complete_http_address(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("SATL_PROXY_MODE", "manual")
    monkeypatch.setenv("SATL_PROXY_ADDRESS", "127.0.0.1:7890")

    with pytest.raises(NetworkConfigurationError, match="http://"):
        NetworkSettings.from_environment()


def test_manual_proxy_bypasses_local_addresses() -> None:
    calls: list[str] = []

    def direct(request, timeout):
        calls.append(f"direct:{request.host}")
        return Response()

    transport = NetworkTransport(
        NetworkSettings(
            proxy_mode="manual",
            proxy_address="http://proxy.invalid:8080",
            proxy_bypass_local=True,
        ),
        direct_opener=direct,
    )

    with transport.open(urllib.request.Request("https://localhost/status"), 15):
        pass

    assert calls == ["direct:localhost"]


def test_dns_query_supports_unicode_host_names() -> None:
    payload = _build_dns_query("例子.测试", 1, 123)

    assert payload[:2] == b"\x00{"
    assert b"xn--" in payload


@pytest.mark.parametrize(
    ("error", "message"),
    [
        (socket.gaierror(socket.EAI_NONAME, "internal"), "DNS"),
        (ConnectionRefusedError("internal"), "代理"),
        (urllib.error.HTTPError("https://example.invalid", 407, "internal", {}, None), "身份验证"),
        (TimeoutError("internal"), "超时"),
    ],
)
def test_network_errors_are_translated_without_internal_details(
    error: BaseException,
    message: str,
) -> None:
    result = describe_network_error(error)

    assert message in result
    assert "internal" not in result
