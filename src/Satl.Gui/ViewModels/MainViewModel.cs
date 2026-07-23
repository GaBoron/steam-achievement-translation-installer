using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SatlCliService _cli = new();
    private readonly SettingsService _settingsService = new();
    private readonly UpdateService _updateService = new();
    private readonly NetworkProbeService _networkProbeService = new();
    private string _searchText = string.Empty;
    private bool _isBusy;
    private string _statusMessage = "准备就绪";
    private bool _isInfoOpen;
    private string _infoMessage = string.Empty;
    private InfoBarSeverity _infoSeverity = InfoBarSeverity.Informational;
    private string _detectedSteamDirectory = string.Empty;
    private Uri? _latestReleasePage;

    public ObservableCollection<GameItem> Games { get; } = [];
    public ObservableCollection<GameItem> VisibleGames { get; } = [];
    public ObservableCollection<GameItem> ManagedGames { get; } = [];
    public GameLoadingProgress GameLoading { get; } = new();
    public GuiSettings Settings { get; private set; } = new();
    public string SettingsPath => _settingsService.SettingsPath;
    public string CurrentSteamDirectory => !string.IsNullOrWhiteSpace(Settings.SteamDirectory)
        ? Settings.SteamDirectory
        : string.IsNullOrWhiteSpace(_detectedSteamDirectory) ? "尚未检测到 Steam 目录" : _detectedSteamDirectory;
    public string CurrentDataDirectory => !string.IsNullOrWhiteSpace(Settings.DataDirectory)
        ? Settings.DataDirectory
        : Path.GetDirectoryName(SettingsPath)!;
    public Uri? LatestReleasePage
    {
        get => _latestReleasePage;
        private set => SetProperty(ref _latestReleasePage, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsInfoOpen
    {
        get => _isInfoOpen;
        set => SetProperty(ref _isInfoOpen, value);
    }

    public string InfoMessage
    {
        get => _infoMessage;
        private set => SetProperty(ref _infoMessage, value);
    }

    public InfoBarSeverity InfoSeverity
    {
        get => _infoSeverity;
        private set => SetProperty(ref _infoSeverity, value);
    }

    public async Task InitializeAsync()
    {
        Settings = await _settingsService.LoadAsync();
        _updateService.ConfigureNetwork(Settings.Network);
        OnPropertyChanged(nameof(Settings));
        App.Logs.Configure(Settings.LoggingEnabled, Settings.LogLevel, Settings.LogRetentionDays);
        await App.Logs.WriteAsync("信息", "应用", "设置已加载，开始初始化。");
        ApplyTheme();
        await ScanAsync(refreshCatalog: true);
        if (Settings.CheckForUpdatesOnStartup)
        {
            await CheckForUpdatesCoreAsync(showCurrentResult: false);
        }
    }

    public async Task ScanAsync(bool refreshCatalog = true)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        IsInfoOpen = false;
        try
        {
            var refreshed = false;
            if (refreshCatalog && !Settings.Offline)
            {
                refreshed = await RefreshCatalogCoreAsync();
            }
            await ScanCoreAsync(forceOffline: refreshed || Settings.Offline);
            await LoadManagedCoreAsync(forceOffline: refreshed || Settings.Offline);
            ShowInfo($"扫描完成，匹配到 {Games.Count} 个可用翻译。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("扫描", exception);
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public async Task<IReadOnlyList<ReplacementPreview>?> PreviewInstallAsync(IReadOnlyList<GameItem> selected)
    {
        var result = await RunPreviewAsync(
            BuildInstallArguments(selected, dryRun: true, yes: false, previewContent: true),
            "正在读取待安装文件内容…");
        return result is null ? null : TryParsePreviews(result, selected);
    }

    public async Task InstallAsync(IReadOnlyList<GameItem> selected)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        IsInfoOpen = false;
        try
        {
            var result = await RunCliAsync(
                BuildInstallArguments(selected, dryRun: false, yes: true, previewContent: false),
                "正在安装翻译…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await ScanCoreAsync(forceOffline: true);
            await LoadManagedCoreAsync(forceOffline: true);
            ShowInfo("所选翻译已安装。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("安装", exception);
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public async Task<IReadOnlyList<ReplacementPreview>?> PreviewRestoreAsync(
        IReadOnlyList<GameItem> selected,
        bool force)
    {
        var result = await RunPreviewAsync(
            BuildRestoreArguments(selected, dryRun: true, yes: false, force, previewContent: true),
            "正在读取待恢复文件内容…");
        return result is null ? null : TryParsePreviews(result, selected);
    }

    public async Task RestoreAsync(IReadOnlyList<GameItem> selected, bool force)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        IsInfoOpen = false;
        try
        {
            var result = await RunCliAsync(
                BuildRestoreArguments(selected, dryRun: false, yes: true, force, previewContent: false),
                force ? "正在强制恢复并归档当前文件…" : "正在恢复安装前文件…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await ScanCoreAsync(forceOffline: true);
            await LoadManagedCoreAsync(forceOffline: true);
            ShowInfo(force ? "已归档当前文件并完成恢复。" : "已恢复安装前文件。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("恢复", exception);
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public async Task RefreshCacheAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        IsInfoOpen = false;
        try
        {
            var arguments = new List<string> { "cache", "refresh", "--jsonl" };
            AddDataDirectory(arguments);
            var result = await RunCliAsync(arguments, "正在刷新翻译目录…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await ScanCoreAsync(forceOffline: true);
            await LoadManagedCoreAsync(forceOffline: true);
            ShowInfo("翻译目录缓存已刷新。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("刷新缓存", exception);
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public async Task ExportPetitionAsync(string appId, string outputPath)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        IsInfoOpen = false;
        try
        {
            var arguments = new List<string>
            {
                "petition",
                "export",
                appId,
                "--output",
                outputPath,
                "--overwrite",
                "--jsonl",
            };
            AddSteamDirectory(arguments);
            var result = await RunCliAsync(arguments, $"正在导出 App ID {appId} 的翻译请愿文件…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            ShowInfo($"翻译请愿 ZIP 已导出：{outputPath}", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowException("导出请愿文件", exception);
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public Task<UpdateCheckResult?> CheckForUpdatesAsync() =>
        CheckForUpdatesCoreAsync(showCurrentResult: true);

    public async Task<NetworkProbeResult> TestNetworkAsync(NetworkSettings settings)
    {
        var normalized = NetworkSettingsValidator.Normalize(settings);
        await App.Logs.WriteAsync(
            "信息",
            "网络测试",
            $"开始测试连接。DNS={normalized.DnsMode}；代理={normalized.ProxyMode}。");
        var result = await _networkProbeService.TestAsync(normalized);
        await App.Logs.WriteAsync(
            result.IsSuccess ? "信息" : "警告",
            "网络测试",
            result.Message);
        ShowInfo(
            result.Message,
            result.IsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        return result;
    }

    private async Task<UpdateCheckResult?> CheckForUpdatesCoreAsync(bool showCurrentResult)
    {
        if (IsBusy)
        {
            return null;
        }
        IsBusy = true;
        IsInfoOpen = false;
        StatusMessage = "正在检查软件更新…";
        var stopwatch = Stopwatch.StartNew();
        await App.Logs.WriteAsync(
            "调试",
            "更新",
            $"开始检查更新。手动显示当前结果={showCurrentResult}；当前版本={UpdateService.CurrentVersionText}。",
            debug: true);
        try
        {
            var result = await _updateService.CheckAsync();
            LatestReleasePage = result.ReleasePage;
            await App.Logs.WriteAsync("信息", "更新", result.Message);
            await App.Logs.WriteAsync(
                "调试",
                "更新",
                $"更新检查完成。耗时={stopwatch.ElapsedMilliseconds} ms；当前={result.CurrentVersion}；最新={result.LatestVersion}；" +
                $"有更新={result.IsUpdateAvailable}；发布页={result.ReleasePage}。",
                debug: true);
            if (result.IsUpdateAvailable)
            {
                var xamlRoot = (App.Window.Content as FrameworkElement)?.XamlRoot;
                if (xamlRoot is not null)
                {
                    await UpdateDialogService.ShowAsync(xamlRoot, result, _updateService);
                }
                else
                {
                    ShowInfo(result.Message, InfoBarSeverity.Success);
                }
            }
            else if (showCurrentResult)
            {
                ShowInfo(result.Message, InfoBarSeverity.Informational);
            }
            return result;
        }
        catch (Exception exception)
        {
            var message = NetworkErrorMessage.Describe(exception, "检查软件更新");
            await App.Logs.WriteAsync("警告", "更新", message);
            await App.Logs.WriteAsync(
                "调试",
                "更新",
                $"更新检查异常。耗时={stopwatch.ElapsedMilliseconds} ms。{exception}",
                debug: true);
            if (showCurrentResult)
            {
                ShowInfo(message, InfoBarSeverity.Warning);
            }
            return null;
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public async Task UpdateSettingsAsync(GuiSettings settings)
    {
        settings.Network = NetworkSettingsValidator.Normalize(settings.Network);
        var previous = Settings;
        var enablingDebug = settings.LoggingEnabled
            && settings.LogLevel == "debug"
            && (!previous.LoggingEnabled || previous.LogLevel != "debug");
        await App.Logs.WriteAsync(
            "调试",
            "设置",
            $"准备保存设置。原设置={DescribeSettings(previous)}；新设置={DescribeSettings(settings)}。",
            debug: true);
        await _settingsService.SaveAsync(settings);
        Settings = settings;
        _updateService.ConfigureNetwork(settings.Network);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(CurrentSteamDirectory));
        OnPropertyChanged(nameof(CurrentDataDirectory));
        App.Logs.Configure(settings.LoggingEnabled, settings.LogLevel, settings.LogRetentionDays);
        if (enablingDebug)
        {
            await WriteDebugSessionHeaderAsync();
        }
        await App.Logs.WriteAsync(
            "调试",
            "设置",
            $"设置已应用。运行时日志级别={settings.LogLevel}；持久化日志级别=" +
            $"{(settings.LogLevel == "debug" ? "detailed（重启后自动恢复）" : settings.LogLevel)}。",
            debug: true);
        ApplyTheme();
    }

    public void ShowInfo(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        InfoMessage = message;
        InfoSeverity = severity;
        IsInfoOpen = true;
        _ = App.Logs.WriteAsync("调试", "界面", $"显示 InfoBar。严重性={severity}；消息={message}", debug: true);
        if (severity is InfoBarSeverity.Error or InfoBarSeverity.Warning)
        {
            _ = App.Logs.WriteAsync(severity == InfoBarSeverity.Error ? "错误" : "警告", "界面", message);
        }
    }

    private async Task<CliRunResult?> RunPreviewAsync(IReadOnlyList<string> arguments, string status)
    {
        if (IsBusy)
        {
            return null;
        }
        IsBusy = true;
        IsInfoOpen = false;
        try
        {
            var result = await RunCliAsync(arguments, status);
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return null;
            }
            return result;
        }
        catch (Exception exception)
        {
            ShowException("预览", exception);
            return null;
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    private IReadOnlyList<ReplacementPreview>? TryParsePreviews(
        CliRunResult result,
        IReadOnlyList<GameItem> selected)
    {
        try
        {
            var selectedById = selected.ToDictionary(item => item.AppId);
            var previews = result.Events
                .Where(item => item.Event == "item-preview")
                .Select(item =>
                {
                    var appId = item.Payload.TryGetProperty("app_id", out var value)
                        ? value.GetString() ?? string.Empty
                        : string.Empty;
                    var fallbackName = selectedById.TryGetValue(appId, out var game)
                        ? game.GameName
                        : appId;
                    return ReplacementPreview.FromPayload(item.Payload, fallbackName);
                })
                .ToList();
            if (previews.Count != selected.Count)
            {
                throw new InvalidDataException(
                    $"替换预览数量不完整：请求 {selected.Count} 个，收到 {previews.Count} 个。拒绝继续。"
                );
            }
            return previews;
        }
        catch (Exception exception)
        {
            ShowException("替换预览", exception);
            return null;
        }
    }

    private async Task<bool> RefreshCatalogCoreAsync()
    {
        var arguments = new List<string> { "cache", "refresh", "--jsonl" };
        AddDataDirectory(arguments);
        var result = await RunCliAsync(arguments, "正在刷新云端翻译索引…");
        if (result.IsSuccess)
        {
            return true;
        }
        ShowInfo(
            "云端索引刷新失败，将尝试使用已验证的本地缓存。" + Environment.NewLine + ResultError(result),
            InfoBarSeverity.Warning);
        return false;
    }

    private async Task ScanCoreAsync(bool forceOffline = false)
    {
        var arguments = new List<string> { "scan", "--jsonl" };
        AddCommonArguments(
            arguments,
            includeSteamDirectory: true,
            includeOffline: true,
            forceOffline: forceOffline);
        var result = await RunCliAsync(arguments, "正在扫描本地 Steam 数据…");
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(ResultError(result));
        }

        var plan = result.Events.FirstOrDefault(item => item.Event == "plan");
        if (plan is not null
            && plan.Payload.TryGetProperty("steam_dir", out var steamDirectory)
            && steamDirectory.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            _detectedSteamDirectory = steamDirectory.GetString() ?? string.Empty;
            OnPropertyChanged(nameof(CurrentSteamDirectory));
        }

        Games.Clear();
        foreach (var satlEvent in result.Events.Where(item => item.Event == "item-succeeded"))
        {
            Games.Add(GameItem.FromPayload(satlEvent.Payload));
        }
        ApplyFilter();
    }

    private async Task LoadManagedCoreAsync(bool forceOffline = false)
    {
        var arguments = new List<string> { "status", "--jsonl" };
        AddCommonArguments(
            arguments,
            includeSteamDirectory: false,
            includeOffline: true,
            forceOffline: forceOffline);
        var result = await RunCliAsync(arguments, "正在读取安装状态…");
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(ResultError(result));
        }

        ManagedGames.Clear();
        foreach (var satlEvent in result.Events.Where(item => item.Event == "item-succeeded"))
        {
            var managed = GameItem.FromPayload(satlEvent.Payload);
            ManagedGames.Add(managed);
            var scanned = Games.FirstOrDefault(item => item.AppId == managed.AppId);
            if (scanned is not null)
            {
                scanned.InstalledState = managed.InstalledState;
                scanned.InstalledVariantId = managed.InstalledVariantId;
            }
        }
    }

    private async Task<CliRunResult> RunCliAsync(IReadOnlyList<string> arguments, string status)
    {
        StatusMessage = status;
        var operation = arguments.FirstOrDefault() ?? "unknown";
        var tracksGameLoading = operation == "scan";
        if (tracksGameLoading)
        {
            GameLoading.Start(status);
        }
        var stopwatch = Stopwatch.StartNew();
        await App.Logs.WriteAsync("信息", operation, $"开始：{status}");
        await App.Logs.WriteAsync(
            "调试",
            operation,
            $"GUI 已提交 CLI 操作。状态文本={status}；参数数量={arguments.Count}。",
            debug: true);
        var diagnosticWrites = new List<Task>();
        Action<string>? diagnostic = App.Logs.IsDebugEnabled
            ? message => diagnosticWrites.Add(App.Logs.WriteAsync("调试", operation, message, debug: true))
            : null;
        CliRunResult result;
        try
        {
            result = await _cli.RunAsync(arguments, satlEvent =>
            {
                _ = App.Logs.WriteAsync("详细", satlEvent.Operation, DescribeEvent(satlEvent), detailed: true);
                if (tracksGameLoading)
                {
                    void UpdateProgress()
                    {
                        GameLoading.Handle(satlEvent);
                        if (GameLoading.IsActive)
                        {
                            StatusMessage = GameLoading.Text;
                        }
                    }
                    if (App.DispatcherQueue.HasThreadAccess)
                    {
                        UpdateProgress();
                    }
                    else
                    {
                        App.DispatcherQueue.TryEnqueue(UpdateProgress);
                    }
                }
                if (satlEvent.Event == "item-started" && satlEvent.Payload.TryGetProperty("app_id", out var appId))
                {
                    App.DispatcherQueue.TryEnqueue(() => StatusMessage = $"正在处理 App ID {appId.GetString()}…");
                }
                else if (satlEvent.Event == "warning" && satlEvent.Payload.TryGetProperty("message", out var warning))
                {
                    App.DispatcherQueue.TryEnqueue(() => ShowInfo(warning.GetString() ?? "正在使用本地缓存。"));
                }
            }, diagnostic, Settings.Network);
        }
        catch (Exception exception)
        {
            if (tracksGameLoading)
            {
                GameLoading.Fail("游戏加载失败");
            }
            await Task.WhenAll(diagnosticWrites);
            await App.Logs.WriteAsync(
                "调试",
                operation,
                $"CLI 调用抛出异常。耗时={stopwatch.ElapsedMilliseconds} ms。{exception}",
                debug: true);
            throw;
        }
        await Task.WhenAll(diagnosticWrites);
        if (tracksGameLoading)
        {
            GameLoading.Finish(result.IsSuccess ? "游戏加载完成" : "游戏加载失败");
        }
        await App.Logs.WriteAsync(
            result.IsSuccess ? "信息" : "错误",
            operation,
            $"完成：退出码 {result.ExitCode}，事件 {result.Events.Count} 个。" +
            (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty : $" 标准错误：{result.StandardError}"));
        await App.Logs.WriteAsync(
            "调试",
            operation,
            $"CLI 操作返回 GUI。成功={result.IsSuccess}；耗时={stopwatch.ElapsedMilliseconds} ms；" +
            $"错误消息={result.ErrorMessage}。",
            debug: true);
        return result;
    }

    private List<string> BuildInstallArguments(
        IReadOnlyList<GameItem> selected,
        bool dryRun,
        bool yes,
        bool previewContent)
    {
        var arguments = new List<string> { "install" };
        arguments.AddRange(selected.Select(item => item.AppId));
        foreach (var item in selected.Where(item => item.SelectedVariant is not null))
        {
            arguments.Add("--variant");
            arguments.Add($"{item.AppId}={item.SelectedVariant!.VariantId}");
        }
        if (selected.Any(item => !item.IsCurrent))
        {
            arguments.Add("--allow-outdated");
        }
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        if (previewContent)
        {
            arguments.Add("--preview-content");
        }
        if (yes)
        {
            arguments.Add("--yes");
        }
        arguments.Add("--jsonl");
        AddCommonArguments(arguments, includeSteamDirectory: true, includeOffline: true);
        return arguments;
    }

    private List<string> BuildRestoreArguments(
        IReadOnlyList<GameItem> selected,
        bool dryRun,
        bool yes,
        bool force,
        bool previewContent)
    {
        var arguments = new List<string> { "restore" };
        arguments.AddRange(selected.Select(item => item.AppId));
        if (force)
        {
            arguments.Add("--force");
        }
        if (dryRun)
        {
            arguments.Add("--dry-run");
        }
        if (previewContent)
        {
            arguments.Add("--preview-content");
        }
        if (yes)
        {
            arguments.Add("--yes");
        }
        arguments.Add("--jsonl");
        AddCommonArguments(arguments, includeSteamDirectory: true, includeOffline: false);
        return arguments;
    }

    private void AddCommonArguments(
        List<string> arguments,
        bool includeSteamDirectory,
        bool includeOffline,
        bool forceOffline = false)
    {
        AddDataDirectory(arguments);
        if (includeSteamDirectory)
        {
            AddSteamDirectory(arguments);
        }
        if (includeOffline && (Settings.Offline || forceOffline))
        {
            arguments.Add("--offline");
        }
    }

    private void AddDataDirectory(List<string> arguments)
    {
        if (!string.IsNullOrWhiteSpace(Settings.DataDirectory))
        {
            arguments.Add("--data-dir");
            arguments.Add(Settings.DataDirectory);
        }
    }

    private void AddSteamDirectory(List<string> arguments)
    {
        if (!string.IsNullOrWhiteSpace(Settings.SteamDirectory))
        {
            arguments.Add("--steam-dir");
            arguments.Add(Settings.SteamDirectory);
        }
    }

    private void ApplyFilter()
    {
        VisibleGames.Clear();
        var query = SearchText.Trim();
        foreach (var game in Games.Where(game =>
                     string.IsNullOrWhiteSpace(query)
                     || game.GameName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                     || game.AppId.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            VisibleGames.Add(game);
        }
    }

    private void ApplyTheme()
    {
        if (App.Window.Content is not FrameworkElement root)
        {
            return;
        }
        root.RequestedTheme = Settings.Theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (App.Window is MainWindow mainWindow)
        {
            var effectiveTheme = root.RequestedTheme == ElementTheme.Default ? root.ActualTheme : root.RequestedTheme;
            mainWindow.ApplyTitleBarTheme(effectiveTheme);
            App.DispatcherQueue.TryEnqueue(() => mainWindow.ApplyTitleBarTheme(root.ActualTheme));
        }
    }

    private static string DescribeEvent(SatlEvent satlEvent)
    {
        var appId = satlEvent.Payload.TryGetProperty("app_id", out var appIdValue)
            ? $"，App ID {appIdValue.GetString()}"
            : string.Empty;
        var variant = satlEvent.Payload.TryGetProperty("variant_id", out var variantValue)
            ? $"，版本 {variantValue.GetString()}"
            : string.Empty;
        var message = satlEvent.Payload.TryGetProperty("message", out var messageValue)
            ? $"：{messageValue.GetString()}"
            : string.Empty;
        return $"事件 {satlEvent.Event}{appId}{variant}{message}";
    }

    private async Task WriteDebugSessionHeaderAsync()
    {
        await App.Logs.WriteAsync(
            "调试",
            "Debug",
            $"Debug 会话已开启。会话 ID={Guid.NewGuid():N}；进程 ID={Environment.ProcessId}；" +
            $"应用版本={UpdateService.CurrentVersionText}；OS={Environment.OSVersion}；" +
            $".NET={Environment.Version}；程序目录={AppContext.BaseDirectory}；日志目录={App.Logs.DirectoryPath}；" +
            $"当前设置={DescribeSettings(Settings)}。Debug 仅本次运行有效。",
            debug: true);
    }

    private static string DescribeSettings(GuiSettings settings) =>
        $"SteamDirectory={settings.SteamDirectory}; DataDirectory={settings.DataDirectory}; Offline={settings.Offline}; " +
        $"Theme={settings.Theme}; LoggingEnabled={settings.LoggingEnabled}; LogLevel={settings.LogLevel}; " +
        $"LogRetentionDays={settings.LogRetentionDays}; LogWordWrap={settings.LogWordWrap}; " +
        $"CheckForUpdatesOnStartup={settings.CheckForUpdatesOnStartup}; " +
        $"DnsMode={settings.Network.DnsMode}; DnsServers={settings.Network.DnsServers}; " +
        $"ProxyMode={settings.Network.ProxyMode}; ProxyAddress={settings.Network.ProxyAddress}; " +
        $"ProxyUsernameConfigured={!string.IsNullOrEmpty(settings.Network.ProxyUsername)}; " +
        $"ProxyPasswordConfigured={!string.IsNullOrEmpty(settings.Network.ProxyPassword)}; " +
        $"ConnectTimeoutSeconds={settings.Network.ConnectTimeoutSeconds}";

    private void ShowException(string operation, Exception exception)
    {
        _ = App.Logs.WriteAsync("调试", operation, exception.ToString(), debug: true);
        var message = NetworkErrorMessage.IsNetworkError(exception)
            ? NetworkErrorMessage.Describe(exception, operation)
            : exception.Message;
        ShowInfo(message, InfoBarSeverity.Error);
    }

    private void ShowResultError(CliRunResult result) => ShowInfo(ResultError(result), InfoBarSeverity.Error);

    private static string ResultError(CliRunResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage) ? $"SATL 操作失败，退出码 {result.ExitCode}。" : result.ErrorMessage;
}
