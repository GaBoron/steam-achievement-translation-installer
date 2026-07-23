using System.Globalization;
using System.Net;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed record DnsServerEndpoint(IPAddress Address, int Port);

public static class NetworkSettingsValidator
{
    private static readonly char[] ListSeparators = [';', ',', '\r', '\n'];

    public static NetworkSettings Normalize(NetworkSettings? settings)
    {
        settings ??= new NetworkSettings();
        var dnsMode = NormalizeChoice(settings.DnsMode, "system", "custom", "DNS 模式");
        var proxyMode = NormalizeChoice(settings.ProxyMode, "system", "direct", "manual", "代理模式");
        var dnsServers = settings.DnsServers.Trim();
        if (dnsMode == "custom")
        {
            _ = ParseDnsServers(dnsServers);
        }

        var proxyAddress = settings.ProxyAddress.Trim();
        if (proxyMode == "manual")
        {
            if (!Uri.TryCreate(proxyAddress, UriKind.Absolute, out var proxyUri)
                || proxyUri.Host.Length == 0
                || proxyUri.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException("代理地址必须是完整的 http:// 或 https:// 地址，例如 http://127.0.0.1:7890。");
            }
            if (!string.IsNullOrEmpty(proxyUri.UserInfo))
            {
                throw new ArgumentException("请在用户名和密码输入框中填写代理凭据，不要把凭据写进代理地址。");
            }
        }

        return new NetworkSettings
        {
            DnsMode = dnsMode,
            DnsServers = dnsServers,
            DnsTimeoutSeconds = RequireRange(settings.DnsTimeoutSeconds, 1, 30, "DNS 超时"),
            ProxyMode = proxyMode,
            ProxyAddress = proxyAddress,
            ProxyUsername = settings.ProxyUsername.Trim(),
            ProxyPassword = settings.ProxyPassword,
            ProtectedProxyPassword = settings.ProtectedProxyPassword,
            ProxyBypassList = NormalizeList(settings.ProxyBypassList),
            ProxyBypassLocal = settings.ProxyBypassLocal,
            ConnectTimeoutSeconds = RequireRange(settings.ConnectTimeoutSeconds, 3, 120, "连接超时"),
        };
    }

    public static IReadOnlyList<DnsServerEndpoint> ParseDnsServers(string value)
    {
        var endpoints = new List<DnsServerEndpoint>();
        foreach (var item in value.Split(ListSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            endpoints.Add(ParseDnsServer(item));
        }
        if (endpoints.Count == 0)
        {
            throw new ArgumentException("自定义 DNS 至少需要填写一个服务器地址。");
        }
        return endpoints;
    }

    public static IReadOnlyList<string> ParseBypassList(string value) =>
        value.Split(ListSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static DnsServerEndpoint ParseDnsServer(string value)
    {
        if (IPAddress.TryParse(value, out var address))
        {
            return new DnsServerEndpoint(address, 53);
        }

        if (Uri.TryCreate($"dns://{value}", UriKind.Absolute, out var endpoint)
            && endpoint.Host.Length > 0
            && IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out address)
            && endpoint.Port is >= 1 and <= 65535)
        {
            return new DnsServerEndpoint(address, endpoint.Port);
        }
        throw new ArgumentException($"DNS 服务器“{value}”无效。请填写 IP 地址，可选端口格式为 1.1.1.1:53 或 [2606:4700:4700::1111]:53。");
    }

    private static string NormalizeChoice(string? value, string first, string second, string description)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized == first || normalized == second)
        {
            return normalized;
        }
        throw new ArgumentException($"{description}无效。");
    }

    private static string NormalizeChoice(string? value, string first, string second, string third, string description)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized == first || normalized == second || normalized == third)
        {
            return normalized;
        }
        throw new ArgumentException($"{description}无效。");
    }

    private static int RequireRange(int value, int minimum, int maximum, string description)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                description,
                string.Create(CultureInfo.CurrentCulture, $"{description}必须在 {minimum} 到 {maximum} 秒之间。"));
        }
        return value;
    }

    private static string NormalizeList(string value) =>
        string.Join("; ", ParseBypassList(value));
}
