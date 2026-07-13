using System.Collections.ObjectModel;
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
        OnPropertyChanged(nameof(Settings));
        App.Logs.Configure(Settings.LoggingEnabled, Settings.LogLevel, Settings.LogRetentionDays);
        await App.Logs.WriteAsync("信息", "应用", "设置已加载，开始初始化。");
        ApplyTheme();
        await ScanAsync();
        if (Settings.CheckForUpdatesOnStartup)
        {
            await CheckForUpdatesCoreAsync(showCurrentResult: false);
        }
    }

    public async Task ScanAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        IsInfoOpen = false;
        try
        {
            await ScanCoreAsync();
            await LoadManagedCoreAsync();
            ShowInfo($"扫描完成，匹配到 {Games.Count} 个可用翻译。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowInfo(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public async Task<CliRunResult?> PreviewInstallAsync(IReadOnlyList<GameItem> selected)
    {
        return await RunPreviewAsync(BuildInstallArguments(selected, dryRun: true, yes: false), "正在检查安装计划…");
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
            var result = await RunCliAsync(BuildInstallArguments(selected, dryRun: false, yes: true), "正在安装翻译…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await ScanCoreAsync();
            await LoadManagedCoreAsync();
            ShowInfo("所选翻译已安装。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowInfo(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public async Task<CliRunResult?> PreviewRestoreAsync(IReadOnlyList<GameItem> selected, bool force)
    {
        return await RunPreviewAsync(BuildRestoreArguments(selected, dryRun: true, yes: false, force), "正在检查恢复计划…");
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
                BuildRestoreArguments(selected, dryRun: false, yes: true, force),
                force ? "正在强制恢复并归档当前文件…" : "正在恢复安装前文件…");
            if (!result.IsSuccess)
            {
                ShowResultError(result);
                return;
            }
            await ScanCoreAsync();
            await LoadManagedCoreAsync();
            ShowInfo(force ? "已归档当前文件并完成恢复。" : "已恢复安装前文件。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowInfo(exception.Message, InfoBarSeverity.Error);
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
            await ScanCoreAsync();
            await LoadManagedCoreAsync();
            ShowInfo("翻译目录缓存已刷新。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowInfo(exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    public Task<UpdateCheckResult?> CheckForUpdatesAsync() =>
        CheckForUpdatesCoreAsync(showCurrentResult: true);

    private async Task<UpdateCheckResult?> CheckForUpdatesCoreAsync(bool showCurrentResult)
    {
        if (IsBusy)
        {
            return null;
        }
        IsBusy = true;
        IsInfoOpen = false;
        StatusMessage = "正在检查软件更新…";
        try
        {
            var result = await _updateService.CheckAsync();
            LatestReleasePage = result.ReleasePage;
            await App.Logs.WriteAsync("信息", "更新", result.Message);
            if (result.IsUpdateAvailable || showCurrentResult)
            {
                ShowInfo(
                    result.Message,
                    result.IsUpdateAvailable ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
            }
            return result;
        }
        catch (Exception exception)
        {
            var message = $"无法检查软件更新：{exception.Message}";
            await App.Logs.WriteAsync("警告", "更新", message);
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
        Settings = settings;
        await _settingsService.SaveAsync(settings);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(CurrentSteamDirectory));
        OnPropertyChanged(nameof(CurrentDataDirectory));
        App.Logs.Configure(settings.LoggingEnabled, settings.LogLevel, settings.LogRetentionDays);
        ApplyTheme();
    }

    public void ShowInfo(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        InfoMessage = message;
        InfoSeverity = severity;
        IsInfoOpen = true;
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
            ShowInfo(exception.Message, InfoBarSeverity.Error);
            return null;
        }
        finally
        {
            StatusMessage = "准备就绪";
            IsBusy = false;
        }
    }

    private async Task ScanCoreAsync()
    {
        var arguments = new List<string> { "scan", "--jsonl" };
        AddCommonArguments(arguments, includeSteamDirectory: true, includeOffline: true);
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

    private async Task LoadManagedCoreAsync()
    {
        var arguments = new List<string> { "status", "--jsonl" };
        AddCommonArguments(arguments, includeSteamDirectory: false, includeOffline: true);
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
        await App.Logs.WriteAsync("信息", operation, $"开始：{status}");
        var result = await _cli.RunAsync(arguments, satlEvent =>
        {
            _ = App.Logs.WriteAsync("详细", satlEvent.Operation, DescribeEvent(satlEvent), detailed: true);
            if (satlEvent.Event == "item-started" && satlEvent.Payload.TryGetProperty("app_id", out var appId))
            {
                App.DispatcherQueue.TryEnqueue(() => StatusMessage = $"正在处理 App ID {appId.GetString()}…");
            }
            else if (satlEvent.Event == "warning" && satlEvent.Payload.TryGetProperty("message", out var warning))
            {
                App.DispatcherQueue.TryEnqueue(() => ShowInfo(warning.GetString() ?? "正在使用本地缓存。"));
            }
        });
        await App.Logs.WriteAsync(
            result.IsSuccess ? "信息" : "错误",
            operation,
            $"完成：退出码 {result.ExitCode}，事件 {result.Events.Count} 个。" +
            (string.IsNullOrWhiteSpace(result.StandardError) ? string.Empty : $" 标准错误：{result.StandardError}"));
        return result;
    }

    private List<string> BuildInstallArguments(IReadOnlyList<GameItem> selected, bool dryRun, bool yes)
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
        if (yes)
        {
            arguments.Add("--yes");
        }
        arguments.Add("--jsonl");
        AddCommonArguments(arguments, includeSteamDirectory: true, includeOffline: true);
        return arguments;
    }

    private List<string> BuildRestoreArguments(IReadOnlyList<GameItem> selected, bool dryRun, bool yes, bool force)
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
        if (yes)
        {
            arguments.Add("--yes");
        }
        arguments.Add("--jsonl");
        AddCommonArguments(arguments, includeSteamDirectory: true, includeOffline: false);
        return arguments;
    }

    private void AddCommonArguments(List<string> arguments, bool includeSteamDirectory, bool includeOffline)
    {
        AddDataDirectory(arguments);
        if (includeSteamDirectory && !string.IsNullOrWhiteSpace(Settings.SteamDirectory))
        {
            arguments.Add("--steam-dir");
            arguments.Add(Settings.SteamDirectory);
        }
        if (includeOffline && Settings.Offline)
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

    private void ShowResultError(CliRunResult result) => ShowInfo(ResultError(result), InfoBarSeverity.Error);

    private static string ResultError(CliRunResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage) ? $"SATL 操作失败，退出码 {result.ExitCode}。" : result.ErrorMessage;
}
