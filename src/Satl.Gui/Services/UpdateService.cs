using System.Net.Http.Headers;
using System.Text.Json;

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
        "https://api.github.com/repos/GaBoron/steam-achievement-translation-installer/releases/latest");
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
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd($"SATLInstaller/{FormatVersion(_currentVersion)}");

        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!TryParseVersion(tag, out var latestVersion))
        {
            throw new InvalidDataException($"GitHub Release 的版本标签无效：{tag}");
        }

        var releasePage = TryGetUri(root, "html_url");
        Uri? installer = null;
        Uri? portable = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameValue)
                    ? nameValue.GetString() ?? string.Empty
                    : string.Empty;
                var download = TryGetUri(asset, "browser_download_url");
                if (download is null)
                {
                    continue;
                }
                if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    installer = download;
                }
                else if (name.Contains("Portable", StringComparison.OrdinalIgnoreCase)
                         && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    portable = download;
                }
            }
        }

        var isAvailable = latestVersion > Normalize(_currentVersion);
        var currentText = FormatVersion(_currentVersion);
        var latestText = FormatVersion(latestVersion);
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

    private static Uri? TryGetUri(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
        && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri)
            ? uri
            : null;

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
