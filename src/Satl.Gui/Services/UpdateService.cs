using System.Net;

namespace Satl_Gui.Services;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    Uri? ReleasePage,
    Uri? InstallerDownload,
    Uri? PortableDownload,
    string Message);

public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/GaBoron/steam-achievement-translation-installer";
    private static readonly Uri DefaultEndpoint = new(
        $"{RepositoryUrl}/releases/latest");
    private static readonly HttpClient SharedClient = CreateClient();

    private readonly HttpClient _client;
    private readonly Version _currentVersion;
    private readonly Uri _endpoint;

    public UpdateService(
        HttpClient? client = null,
        Version? currentVersion = null,
        Uri? endpoint = null)
    {
        _client = client ?? SharedClient;
        _currentVersion = currentVersion ?? CurrentVersion;
        _endpoint = endpoint ?? DefaultEndpoint;
    }

    public static Version CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 2, 0);

    public static string CurrentVersionText => FormatVersion(CurrentVersion);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        request.Headers.UserAgent.ParseAdd($"SATLInstaller/{FormatVersion(_currentVersion)}");

        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("GitHub 暂时拒绝了更新请求，请稍后重试。", null, response.StatusCode);
        }
        response.EnsureSuccessStatusCode();

        var releasePage = response.RequestMessage?.RequestUri;
        if (!TryGetReleaseVersion(releasePage, out var latestVersion, out var tag))
        {
            throw new InvalidDataException("GitHub 最新发布页没有返回有效的版本标签。");
        }

        var isAvailable = latestVersion > Normalize(_currentVersion);
        var currentText = FormatVersion(_currentVersion);
        var latestText = FormatVersion(latestVersion);
        releasePage ??= new Uri($"{RepositoryUrl}/releases/tag/{tag}");
        var installer = new Uri($"{RepositoryUrl}/releases/download/{tag}/SATLInstaller-Setup-v{latestText}.exe");
        var portable = new Uri($"{RepositoryUrl}/releases/download/{tag}/SATLInstaller-Portable-v{latestText}.zip");
        var message = isAvailable
            ? $"发现新版本 v{latestText}。请打开 GitHub Release 选择安装版或便携版。"
            : $"当前已是最新版本 v{currentText}。";
        return new UpdateCheckResult(
            isAvailable,
            currentText,
            latestText,
            releasePage,
            installer,
            portable,
            message);
    }

    private static HttpClient CreateClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static bool TryGetReleaseVersion(Uri? uri, out Version version, out string tag)
    {
        version = new Version(0, 0, 0);
        tag = string.Empty;
        if (uri is null)
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + 1 < segments.Length; index++)
        {
            if (!segments[index].Equals("tag", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            tag = Uri.UnescapeDataString(segments[index + 1]);
            return TryParseVersion(tag, out version);
        }
        return false;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var separator = normalized.IndexOfAny(['-', '+']);
        if (separator >= 0)
        {
            normalized = normalized[..separator];
        }
        if (Version.TryParse(normalized, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    private static Version Normalize(Version value) => new(
        Math.Max(value.Major, 0),
        Math.Max(value.Minor, 0),
        Math.Max(value.Build, 0));

    private static string FormatVersion(Version value)
    {
        var normalized = Normalize(value);
        return $"{normalized.Major}.{normalized.Minor}.{normalized.Build}";
    }
}
