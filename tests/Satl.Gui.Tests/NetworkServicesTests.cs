using System.Net;
using System.Net.Sockets;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class NetworkServicesTests
{
    [Fact]
    public void ValidatorAcceptsGeneralDesktopNetworkSettings()
    {
        var normalized = NetworkSettingsValidator.Normalize(new NetworkSettings
        {
            DnsMode = "custom",
            DnsServers = "1.1.1.1; [2606:4700:4700::1111]:53",
            ProxyMode = "manual",
            ProxyAddress = "http://127.0.0.1:7890",
            ProxyUsername = "user",
            ProxyPassword = "secret",
        });

        Assert.Equal("custom", normalized.DnsMode);
        Assert.Equal("manual", normalized.ProxyMode);
        Assert.Equal(2, NetworkSettingsValidator.ParseDnsServers(normalized.DnsServers).Count);
        Assert.Equal("secret", normalized.ProxyPassword);
    }

    [Fact]
    public void ValidatorRejectsProxyAddressWithoutScheme()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            NetworkSettingsValidator.Normalize(new NetworkSettings
            {
                ProxyMode = "manual",
                ProxyAddress = "127.0.0.1:7890",
            }));

        Assert.Contains("http://", exception.Message);
    }

    [Theory]
    [InlineData(SocketError.HostNotFound, "DNS")]
    [InlineData(SocketError.ConnectionRefused, "代理")]
    [InlineData(SocketError.TimedOut, "超时")]
    public void SocketErrorsUseUserFacingChinese(SocketError socketError, string expected)
    {
        var message = NetworkErrorMessage.Describe(
            new HttpRequestException("internal code error", new SocketException((int)socketError)),
            "测试连接");

        Assert.Contains(expected, message);
        Assert.DoesNotContain("internal code error", message);
    }

    [Fact]
    public void ProxyAuthenticationErrorExplainsWhatTheUserShouldCheck()
    {
        var message = NetworkErrorMessage.Describe(
            new HttpRequestException(
                "internal",
                null,
                HttpStatusCode.ProxyAuthenticationRequired),
            "下载");

        Assert.Contains("代理服务器需要身份验证", message);
        Assert.Contains("用户名和密码", message);
        Assert.DoesNotContain("internal", message);
    }

    [Fact]
    public async Task SettingsServiceEncryptsProxyPasswordAtRest()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"satl-network-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings
            {
                Network = new NetworkSettings
                {
                    ProxyMode = "manual",
                    ProxyAddress = "http://127.0.0.1:7890",
                    ProxyUsername = "user",
                    ProxyPassword = "top-secret",
                },
            });

            var serialized = await File.ReadAllTextAsync(path);
            var loaded = await service.LoadAsync();

            Assert.DoesNotContain("top-secret", serialized);
            Assert.DoesNotContain("ProxyBypass", serialized);
            Assert.DoesNotContain("ConnectTimeout", serialized);
            Assert.DoesNotContain("DnsTimeout", serialized);
            Assert.Equal("top-secret", loaded.Network.ProxyPassword);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
