using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Satl_Gui.Services;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    Uri? ReleasePage,
    Uri? InstallerDownload,
    Uri? ChecksumsDownload,
    string ReleaseNotes,
    string Message);

public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/GaBoron/steam-achievement-translation-installer";
    private static readonly Uri DefaultEndpoint = new($"{RepositoryUrl}/releases/latest");
    private static readonly Uri DefaultFeedEndpoint = new($"{RepositoryUrl}/releases.atom");
    private static readonly Uri DefaultApiEndpoint = new(
        "https://api.github.com/repos/GaBoron/steam-achievement-translation-installer/releases/latest");
    private static readonly HttpClient SharedClient = CreateClient();
    private const long MaximumInstallerBytes = 1024L * 1024 * 1024;

    private readonly HttpClient _client;
    private readonly Version _currentVersion;
    private readonly Uri _endpoint;
    private readonly Uri? _feedEndpoint;
    private readonly Uri? _apiEndpoint;
    private readonly bool _fallbackEnabled;
    private readonly string _updateDirectory;

    public UpdateService(
        HttpClient? client = null,
        Version? currentVersion = null,
        Uri? endpoint = null,
        string? updateDirectory = null,
        Uri? feedEndpoint = null,
        Uri? apiEndpoint = null)
    {
        _client = client ?? SharedClient;
        _currentVersion = currentVersion ?? CurrentVersion;
        _endpoint = endpoint ?? DefaultEndpoint;
        _fallbackEnabled = endpoint is null || feedEndpoint is not null || apiEndpoint is not null;
        _feedEndpoint = _fallbackEnabled ? feedEndpoint ?? DefaultFeedEndpoint : null;
        _apiEndpoint = _fallbackEnabled ? apiEndpoint ?? DefaultApiEndpoint : null;
        _updateDirectory = updateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTranslationInstaller",
            "updates");
    }

    public static Version CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 2, 0);

    public static string CurrentVersionText => FormatVersion(CurrentVersion);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<Exception>();
        ReleaseMetadata? metadata = null;
        try
        {
            metadata = await FetchReleaseMetadataAsync(_endpoint, cancellationToken);
        }
        catch (Exception exception) when (_fallbackEnabled && exception is not OperationCanceledException)
        {
            failures.Add(exception);
        }

        if (_feedEndpoint is not null
            && (metadata is null || string.IsNullOrWhiteSpace(metadata.ReleaseNotes)))
        {
            try
            {
                var feed = await FetchReleaseMetadataAsync(_feedEndpoint, cancellationToken);
                metadata = MergeMetadata(metadata, feed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(exception);
            }
        }

        if (_apiEndpoint is not null
            && (metadata is null || string.IsNullOrWhiteSpace(metadata.ReleaseNotes)))
        {
            try
            {
                var api = await FetchReleaseMetadataAsync(_apiEndpoint, cancellationToken);
                metadata = MergeMetadata(metadata, api);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(exception);
            }
        }

        if (metadata is null)
        {
            var reason = failures.LastOrDefault()?.Message ?? "所有更新来源都没有返回有效版本信息。";
            throw new HttpRequestException(
                $"无法从 GitHub Releases 页面、发布订阅或 API 获取更新信息：{reason}");
        }

        var releasePage = metadata.ReleasePage;
        var tag = metadata.Tag;
        Version latestVersion;
        if (!TryParseVersion(tag, out latestVersion))
        {
            throw new InvalidDataException("GitHub Release 返回了无效的三段版本标签。");
        }

        var isAvailable = latestVersion > Normalize(_currentVersion);
        var currentText = FormatVersion(_currentVersion);
        var latestText = FormatVersion(latestVersion);
        releasePage ??= new Uri($"{RepositoryUrl}/releases/tag/{tag}");
        var installerName = $"SATLInstaller-Setup-v{latestText}.exe";
        var installer = metadata.Asset(installerName)
            ?? new Uri($"{RepositoryUrl}/releases/download/{tag}/{installerName}");
        var checksums = metadata.Asset("SHA256SUMS.txt")
            ?? new Uri($"{RepositoryUrl}/releases/download/{tag}/SHA256SUMS.txt");
        var message = isAvailable
            ? $"发现新版本 v{latestText}。"
            : $"当前已是最新版本 v{currentText}。";
        return new UpdateCheckResult(
            isAvailable,
            currentText,
            latestText,
            releasePage,
            installer,
            checksums,
            string.IsNullOrWhiteSpace(metadata.ReleaseNotes)
                ? "暂时无法读取此版本的发布说明，请打开发布页查看。"
                : metadata.ReleaseNotes,
            message);
    }

    private async Task<ReleaseMetadata?> FetchReleaseMetadataAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.ParseAdd($"SATLInstaller/{FormatVersion(_currentVersion)}");
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.Accept.ParseAdd("application/atom+xml");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{endpoint.Host} 响应更新检查超时。");
        }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException(
                    "GitHub 更新来源暂时拒绝了更新请求，请稍后重试。",
                    null,
                    response.StatusCode);
            }
            response.EnsureSuccessStatusCode();

            var finalUri = response.RequestMessage?.RequestUri;
            if (TryGetReleaseVersion(finalUri, out _, out var redirectedTag))
            {
                return EmptyMetadata(redirectedTag, finalUri);
            }

            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            return TryParseReleaseMetadata(payload)
                ?? TryParseAtomMetadata(payload);
        }
    }

    private static ReleaseMetadata? MergeMetadata(
        ReleaseMetadata? primary,
        ReleaseMetadata? supplement)
    {
        if (primary is null)
        {
            return supplement;
        }
        if (supplement is null
            || !primary.Tag.Equals(supplement.Tag, StringComparison.OrdinalIgnoreCase))
        {
            return primary;
        }

        var assets = new Dictionary<string, Uri>(supplement.Assets, StringComparer.OrdinalIgnoreCase);
        foreach (var asset in primary.Assets)
        {
            assets[asset.Key] = asset.Value;
        }
        return new ReleaseMetadata(
            primary.Tag,
            primary.ReleasePage ?? supplement.ReleasePage,
            string.IsNullOrWhiteSpace(primary.ReleaseNotes)
                ? supplement.ReleaseNotes
                : primary.ReleaseNotes,
            assets);
    }

    private static ReleaseMetadata? TryParseAtomMetadata(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }
        try
        {
            var document = XDocument.Parse(payload);
            XNamespace atom = "http://www.w3.org/2005/Atom";
            foreach (var entry in document.Root?.Elements(atom + "entry") ?? [])
            {
                var link = entry.Elements(atom + "link")
                    .FirstOrDefault(element =>
                        element.Attribute("rel")?.Value is null or "alternate")
                    ?.Attribute("href")?.Value;
                if (!Uri.TryCreate(link, UriKind.Absolute, out var releasePage)
                    || !TryGetReleaseVersion(releasePage, out _, out var tag))
                {
                    continue;
                }
                var content = entry.Element(atom + "content")?.Value ?? string.Empty;
                return new ReleaseMetadata(
                    tag,
                    releasePage,
                    HtmlToPlainText(content),
                    new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        return null;
    }

    private static string HtmlToPlainText(string html)
    {
        var text = Regex.Replace(html, "<\\s*br\\s*/?\\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<\\s*li(?:\\s[^>]*)?>", "- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(
            text,
            "</\\s*(?:p|li|h[1-6]|ul|ol|blockquote)\\s*>",
            "\n",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text).Replace("\u00a0", " ");
        return string.Join(
            "\n",
            text.Split(['\r', '\n'])
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));
    }

    private static ReleaseMetadata EmptyMetadata(string tag, Uri? releasePage) => new(
        tag,
        releasePage,
        string.Empty,
        new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase));

    public async Task<string> DownloadInstallerAsync(
        UpdateCheckResult update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!update.IsUpdateAvailable
            || update.InstallerDownload is null
            || update.ChecksumsDownload is null)
        {
            throw new InvalidOperationException("当前更新信息不包含可下载并校验的安装程序。");
        }

        var fileName = Path.GetFileName(update.InstallerDownload.LocalPath);
        var expectedName = $"SATLInstaller-Setup-v{update.LatestVersion}.exe";
        if (!fileName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"安装程序文件名无效：{fileName}");
        }
        var expectedHash = await ReadExpectedHashAsync(
            update.ChecksumsDownload,
            fileName,
            cancellationToken);
        Directory.CreateDirectory(_updateDirectory);
        var destination = Path.Combine(_updateDirectory, fileName);
        var partial = destination + ".part";
        try
        {
            File.Delete(partial);
            using var request = new HttpRequestMessage(HttpMethod.Get, update.InstallerDownload);
            request.Headers.UserAgent.ParseAdd($"SATLInstaller/{FormatVersion(_currentVersion)}");
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            if (total is > MaximumInstallerBytes)
            {
                throw new InvalidDataException("安装程序超过 1 GiB 安全上限。");
            }
            string actualHash;
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(
                    partial,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 1024];
                long received = 0;
                while (true)
                {
                    var count = await source.ReadAsync(buffer, cancellationToken);
                    if (count == 0)
                    {
                        break;
                    }
                    received += count;
                    if (received > MaximumInstallerBytes)
                    {
                        throw new InvalidDataException("安装程序超过 1 GiB 安全上限。");
                    }
                    hasher.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    if (total is > 0)
                    {
                        progress?.Report((double)received / total.Value);
                    }
                }
                await target.FlushAsync(cancellationToken);
                actualHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            }
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"安装程序 SHA-256 校验失败：期望 {expectedHash}，实际 {actualHash}。"
                );
            }
            File.Move(partial, destination, overwrite: true);
            progress?.Report(1);
            return destination;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    private async Task<string> ReadExpectedHashAsync(
        Uri checksumsDownload,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, checksumsDownload);
        request.Headers.UserAgent.ParseAdd($"SATLInstaller/{FormatVersion(_currentVersion)}");
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && parts[0].Length == 64
                && parts[0].All(Uri.IsHexDigit)
                && parts[^1].TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0].ToLowerInvariant();
            }
        }
        throw new InvalidDataException($"SHA256SUMS.txt 中没有 {fileName} 的校验值。");
    }

    private static HttpClient CreateClient() => new()
    {
        Timeout = TimeSpan.FromMinutes(10),
    };

    private static ReleaseMetadata? TryParseReleaseMetadata(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagValue)
                || tagValue.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var assets = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("assets", out var assetValues)
                && assetValues.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetValues.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameValue)
                        ? nameValue.GetString()
                        : null;
                    var url = asset.TryGetProperty("browser_download_url", out var urlValue)
                        ? urlValue.GetString()
                        : null;
                    if (!string.IsNullOrWhiteSpace(name)
                        && Uri.TryCreate(url, UriKind.Absolute, out var assetUri))
                    {
                        assets[name] = assetUri;
                    }
                }
            }
            var releasePage = root.TryGetProperty("html_url", out var htmlValue)
                && Uri.TryCreate(htmlValue.GetString(), UriKind.Absolute, out var page)
                ? page
                : null;
            var notes = root.TryGetProperty("body", out var bodyValue)
                && bodyValue.ValueKind == JsonValueKind.String
                ? bodyValue.GetString() ?? string.Empty
                : string.Empty;
            return new ReleaseMetadata(tagValue.GetString() ?? string.Empty, releasePage, notes, assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

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
        if (Version.TryParse(normalized, out var parsed)
            && normalized.Split('.').Length == 3)
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

    private sealed record ReleaseMetadata(
        string Tag,
        Uri? ReleasePage,
        string ReleaseNotes,
        IReadOnlyDictionary<string, Uri> Assets)
    {
        public Uri? Asset(string name) => Assets.TryGetValue(name, out var value) ? value : null;
    }
}
