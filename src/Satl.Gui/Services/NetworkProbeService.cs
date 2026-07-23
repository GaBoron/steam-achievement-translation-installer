using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed record NetworkProbeResult(bool IsSuccess, string Message);

public sealed class NetworkProbeService
{
    private static readonly (string Name, Uri Endpoint)[] Endpoints =
    [
        (
            "翻译目录",
            new Uri(
                "https://raw.githubusercontent.com/GaBoron/steam-achievement-translation-library/main/index.json")),
        (
            "软件更新",
            new Uri(
                "https://github.com/GaBoron/steam-achievement-translation-installer/releases/latest")),
    ];

    public async Task<NetworkProbeResult> TestAsync(
        NetworkSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var client = NetworkHttpClientFactory.Create(settings);
        foreach (var (name, endpoint) in Endpoints)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.UserAgent.ParseAdd("SATLInstaller/NetworkTest");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(settings.ConnectTimeoutSeconds, 10)));
            try
            {
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                return new NetworkProbeResult(
                    false,
                    NetworkErrorMessage.Describe(exception, $"连接{name}"));
            }
        }
        return new NetworkProbeResult(true, "网络连接正常：翻译目录和软件更新服务均可访问。");
    }
}
