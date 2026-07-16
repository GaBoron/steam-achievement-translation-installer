using System.Text.Json;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void ParseEventReadsVersionedPayload()
    {
        var parsed = SatlCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"completed\",\"payload\":{\"count\":4}}"
        );

        Assert.Equal(1, parsed.ProtocolVersion);
        Assert.Equal("scan", parsed.Operation);
        Assert.Equal(4, parsed.Payload.GetProperty("count").GetInt32());
    }

    [Fact]
    public void ParseEventRejectsUnknownProtocol()
    {
        Assert.Throws<InvalidDataException>(() => SatlCliService.ParseEvent(
            "{\"protocol_version\":2,\"operation\":\"scan\",\"event\":\"completed\",\"payload\":{}}"
        ));
    }

    [Fact]
    public void ParseEventPreservesCjkText()
    {
        var parsed = SatlCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"item-succeeded\",\"payload\":{\"game_name\":\"以撒的结合：重生\",\"note_zh\":\"原版\"}}"
        );

        Assert.Equal("以撒的结合：重生", parsed.Payload.GetProperty("game_name").GetString());
        Assert.Equal("原版", parsed.Payload.GetProperty("note_zh").GetString());
    }

    [Fact]
    public void GameItemMapsVariantsAndState()
    {
        using var document = JsonDocument.Parse(
            "{\"app_id\":\"123\",\"game_name\":\"Game\",\"catalog_status\":\"current\",\"installed_state\":\"modified\",\"installed_variant_id\":\"with-unlock-conditions\",\"discovery\":[\"installed\"],\"variants\":[{\"variant_id\":\"default\",\"primary\":true,\"note_zh\":\"原版\"},{\"variant_id\":\"with-unlock-conditions\",\"primary\":false,\"note_zh\":\"含解锁条件\"}]}"
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.Equal("123", item.AppId);
        Assert.True(item.IsModified);
        Assert.Equal("with-unlock-conditions", item.SelectedVariant?.VariantId);
        Assert.Contains("含解锁条件", item.SelectedVariant?.DisplayName);
        Assert.Contains("with-unlock-conditions · 含解锁条件", item.InstalledVersionText);
        Assert.Equal("with-unlock-conditions", item.SelectedVariantId);

        item.SelectedVariantId = "default";

        Assert.Equal("default", item.SelectedVariant?.VariantId);
        item.SelectedVariantId = string.Empty;
        Assert.Equal("default", item.SelectedVariantId);
    }

    [Fact]
    public void ReplacementPreviewScansLanguagesAndDefaultsToSimplifiedChinese()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "app_id":"105600",
              "game_name":"Terraria",
              "variant_id":"default",
              "action":"replace",
              "achievement_count":1,
              "languages":["schinese","english","japanese"],
              "rows":[{
                "index":1,
                "api_name":"TIMBER",
                "translations":{
                  "schinese":{"name":"木材！！","description":"砍倒第一棵树。"},
                  "english":{"name":"Timber!!","description":"Chop down your first tree."},
                  "token":{"name":"TOKEN_NAME","description":"TOKEN_DESC"}
                }
              }]
            }
            """);

        var preview = ReplacementPreview.FromPayload(document.RootElement, "fallback");

        Assert.Equal("schinese", preview.DefaultLanguage);
        Assert.Equal(["schinese", "english", "japanese"], preview.Languages);
        Assert.Equal("木材！！", preview.Rows[0].TranslationFor("schinese").Name);
        Assert.DoesNotContain("token", preview.Rows[0].Translations.Keys);
    }

    [Fact]
    public async Task SettingsRoundTripUsesRequestedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-gui-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings
            {
                Offline = true,
                Theme = "dark",
                SteamDirectory = "C:\\Steam",
                LoggingEnabled = false,
                LogLevel = "detailed",
                LogRetentionDays = 90,
                LogWordWrap = false,
                CheckForUpdatesOnStartup = true,
            });

            var loaded = await service.LoadAsync();

            Assert.True(loaded.Offline);
            Assert.Equal("dark", loaded.Theme);
            Assert.Equal("C:\\Steam", loaded.SteamDirectory);
            Assert.False(loaded.LoggingEnabled);
            Assert.Equal("detailed", loaded.LogLevel);
            Assert.Equal(90, loaded.LogRetentionDays);
            Assert.False(loaded.LogWordWrap);
            Assert.True(loaded.CheckForUpdatesOnStartup);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SettingsNeverPersistDebugModeAcrossRestarts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-gui-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings { LogLevel = "debug" });

            Assert.Equal("detailed", (await service.LoadAsync()).LogLevel);
            Assert.DoesNotContain("debug", await File.ReadAllTextAsync(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SettingsEnableLogWordWrapByDefault()
    {
        Assert.True(new GuiSettings().LogWordWrap);
    }

    [Fact]
    public void SettingsEnableUpdateChecksByDefault()
    {
        Assert.True(new GuiSettings().CheckForUpdatesOnStartup);
    }

    [Fact]
    public async Task LogServiceWritesFiltersAndClearsLogs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-log-test-{Guid.NewGuid():N}");
        try
        {
            var service = new LogService(root);
            service.Configure(enabled: true, level: "detailed", retentionDays: 30);
            await service.WriteAsync("信息", "测试", "标准记录");
            await service.WriteAsync("详细", "测试", "详细记录", detailed: true);
            await service.WriteAsync("调试", "测试", "调试记录", debug: true);

            var content = await service.ReadRecentAsync();

            Assert.Contains("标准记录", content);
            Assert.Contains("详细记录", content);
            Assert.DoesNotContain("调试记录", content);
            service.Configure(enabled: true, level: "debug", retentionDays: 30);
            Assert.True(service.IsDebugEnabled);
            await service.WriteAsync("调试", "测试", "调试记录", debug: true);
            Assert.Contains("调试记录", await service.ReadRecentAsync());
            service.Configure(enabled: false, level: "detailed", retentionDays: 30);
            Assert.False(service.IsDebugEnabled);
            await service.WriteAsync("信息", "测试", "不应写入");
            Assert.DoesNotContain("不应写入", await service.ReadRecentAsync());

            await service.ClearAsync();
            Assert.Equal(string.Empty, await service.ReadRecentAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LogServiceAppliesAllThreeVerbosityThresholds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-log-test-{Guid.NewGuid():N}");
        try
        {
            var service = new LogService(root);
            service.Configure(enabled: true, level: "standard", retentionDays: 30);
            await service.WriteAsync("信息", "测试", "普通可见");
            await service.WriteAsync("详细", "测试", "详尽隐藏", detailed: true);
            await service.WriteAsync("调试", "测试", "调试隐藏", debug: true);
            var standard = await service.ReadRecentAsync();
            Assert.Contains("普通可见", standard);
            Assert.DoesNotContain("详尽隐藏", standard);
            Assert.DoesNotContain("调试隐藏", standard);

            service.Configure(enabled: true, level: "detailed", retentionDays: 30);
            await service.WriteAsync("详细", "测试", "详尽可见", detailed: true);
            await service.WriteAsync("调试", "测试", "调试仍隐藏", debug: true);
            var detailed = await service.ReadRecentAsync();
            Assert.Contains("详尽可见", detailed);
            Assert.DoesNotContain("调试仍隐藏", detailed);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LogServiceReadsOnlyTheLatestLogFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-log-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllLinesAsync(
                Path.Combine(root, "satl-gui-2026-07-15.log"),
                ["旧文件第一行", "旧文件第二行"]);
            await File.WriteAllLinesAsync(
                Path.Combine(root, "satl-gui-2026-07-16.log"),
                ["最新文件第一行", "最新文件第二行", "最新文件第三行"]);

            var content = await new LogService(root).ReadRecentAsync(maximumLines: 2);

            Assert.DoesNotContain("旧文件", content);
            Assert.DoesNotContain("最新文件第一行", content);
            Assert.Equal($"最新文件第二行{Environment.NewLine}最新文件第三行", content);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WindowPlacementDefaultsTo1280By720AndCenters()
    {
        var placement = WindowPlacementService.CenterDefault(100, 50, 1920, 1080);

        Assert.Equal(new WindowPlacement(420, 230, 1280, 720), placement);
    }

    [Fact]
    public void WindowPlacementFitsRestoredBoundsToCurrentWorkArea()
    {
        var placement = WindowPlacementService.FitToWorkArea(
            new WindowPlacement(3000, -800, 2400, 200),
            workX: 0,
            workY: 0,
            workWidth: 1920,
            workHeight: 1040);

        Assert.Equal(new WindowPlacement(0, 0, 1920, 480), placement);
    }

    [Fact]
    public void WindowPlacementRoundTripsThroughItsOwnSettingsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-window-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "window-placement.json");
        try
        {
            var service = new WindowPlacementService(path);
            var expected = new WindowPlacement(140, 90, 1440, 900);

            service.Save(expected);

            Assert.Equal(expected, service.Load());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateServiceMapsLatestReleaseRedirectWithoutGithubApi()
    {
        using var client = new HttpClient(new StubHttpHandler(
            new Uri("https://github.com/GaBoron/steam-achievement-translation-installer/releases/tag/v0.3.0")));
        var service = new UpdateService(client, new Version(0, 2, 0), new Uri("https://example.invalid/latest"));

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.3.0", result.LatestVersion);
        Assert.Equal(
            "https://github.com/GaBoron/steam-achievement-translation-installer/releases/download/v0.3.0/SATLInstaller-Setup-v0.3.0.exe",
            result.InstallerDownload?.AbsoluteUri);
        Assert.Equal(
            "https://github.com/GaBoron/steam-achievement-translation-installer/releases/download/v0.3.0/SATLInstaller-Portable-v0.3.0.zip",
            result.PortableDownload?.AbsoluteUri);
        Assert.Contains("发现新版本", result.Message);
    }

    [Fact]
    public async Task UpdateServiceReportsCurrentVersion()
    {
        using var client = new HttpClient(new StubHttpHandler(
            new Uri("https://github.com/GaBoron/steam-achievement-translation-installer/releases/tag/v0.2.0")));
        var service = new UpdateService(client, new Version(0, 2, 0), new Uri("https://example.invalid/latest"));

        var result = await service.CheckAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.Equal("0.2.0", result.CurrentVersion);
        Assert.Contains("最新版本", result.Message);
    }

    [Fact]
    public async Task UpdateServiceReadsReleaseNotesAndDownloadsVerifiedInstaller()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-update-test-{Guid.NewGuid():N}");
        var installerBytes = "verified-installer"u8.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(installerBytes))
            .ToLowerInvariant();
        var releaseJson = """
            {
              "tag_name": "v0.3.0",
              "html_url": "https://github.com/GaBoron/steam-achievement-translation-installer/releases/tag/v0.3.0",
              "body": "## 修复\n- 修复刷新问题",
              "assets": [
                {"name":"SATLInstaller-Setup-v0.3.0.exe","browser_download_url":"https://example.invalid/SATLInstaller-Setup-v0.3.0.exe"},
                {"name":"SATLInstaller-Portable-v0.3.0.zip","browser_download_url":"https://example.invalid/SATLInstaller-Portable-v0.3.0.zip"},
                {"name":"SHA256SUMS.txt","browser_download_url":"https://example.invalid/SHA256SUMS.txt"}
              ]
            }
            """;
        using var client = new HttpClient(new RoutingHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent($"{hash}  SATLInstaller-Setup-v0.3.0.exe\n"),
                };
            }
            if (path.EndsWith(".exe", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(installerBytes),
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson),
            };
        }));
        try
        {
            var service = new UpdateService(
                client,
                new Version(0, 2, 0),
                new Uri("https://example.invalid/latest"),
                root);

            var update = await service.CheckAsync();
            var progress = new List<double>();
            var installer = await service.DownloadInstallerAsync(
                update,
                new Progress<double>(progress.Add));

            Assert.Contains("修复刷新问题", update.ReleaseNotes);
            Assert.Equal(installerBytes, await File.ReadAllBytesAsync(installer));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateServiceFallsBackFromRateLimitedPageToAtomFeed()
    {
        var latest = new Uri("https://example.invalid/releases/latest");
        var feed = new Uri("https://example.invalid/releases.atom");
        var api = new Uri("https://api.example.invalid/releases/latest");
        var atom = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <link rel="alternate" href="https://github.com/GaBoron/steam-achievement-translation-installer/releases/tag/v0.4.0" />
                <content type="html">&lt;h2&gt;修复&lt;/h2&gt;&lt;ul&gt;&lt;li&gt;改进更新检查回退&lt;/li&gt;&lt;/ul&gt;</content>
              </entry>
            </feed>
            """;
        using var client = new HttpClient(new RoutingHttpHandler(request =>
        {
            if (request.RequestUri == latest)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
            }
            if (request.RequestUri == feed)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(atom),
                };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        }));
        var service = new UpdateService(
            client,
            new Version(0, 3, 0),
            latest,
            feedEndpoint: feed,
            apiEndpoint: api);

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.4.0", result.LatestVersion);
        Assert.Contains("改进更新检查回退", result.ReleaseNotes);
    }

    [Fact]
    public async Task UpdateServiceMapsForbiddenResponseToReadableMessage()
    {
        using var client = new HttpClient(new StubHttpHandler(
            new Uri("https://example.invalid/latest"),
            System.Net.HttpStatusCode.Forbidden));
        var service = new UpdateService(client, new Version(0, 2, 0), new Uri("https://example.invalid/latest"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckAsync());

        Assert.Contains("请稍后重试", exception.Message);
    }

    private sealed class StubHttpHandler(
        Uri responseUri,
        System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = statusCode,
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, responseUri),
            Content = new StringContent(string.Empty),
        });
    }

    private sealed class RoutingHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = route(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
