using System.Net;
using System.Net.Sockets;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public static class NetworkHttpClientFactory
{
    public static HttpClient Create(NetworkSettings? rawSettings = null)
    {
        var settings = NetworkSettingsValidator.Normalize(rawSettings);
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseProxy = settings.ProxyMode != "direct",
        };

        if (settings.ProxyMode == "manual")
        {
            var proxy = new WebProxy(new Uri(settings.ProxyAddress))
            {
                BypassProxyOnLocal = false,
            };
            if (!string.IsNullOrEmpty(settings.ProxyUsername))
            {
                proxy.Credentials = new NetworkCredential(settings.ProxyUsername, settings.ProxyPassword);
            }
            handler.Proxy = proxy;
        }

        if (settings.DnsMode == "custom")
        {
            var resolver = new CustomDnsResolver(
                NetworkSettingsValidator.ParseDnsServers(settings.DnsServers),
                TimeSpan.FromSeconds(5));
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await resolver.ResolveAsync(context.DnsEndPoint.Host, cancellationToken);
                Exception? lastError = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true,
                    };
                    try
                    {
                        await socket.ConnectAsync(
                            new IPEndPoint(address, context.DnsEndPoint.Port),
                            cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception exception) when (
                        exception is SocketException or OperationCanceledException)
                    {
                        lastError = exception;
                        socket.Dispose();
                        if (exception is OperationCanceledException)
                        {
                            throw;
                        }
                    }
                }
                throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
            };
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
    }

}
