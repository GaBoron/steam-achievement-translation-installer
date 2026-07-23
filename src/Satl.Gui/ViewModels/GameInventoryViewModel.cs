using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.ViewModels;

public sealed class GameInventoryViewModel(GameInventoryScope scope) : ObservableObject
{
    private readonly SatlCliService _cli = new();
    private string _searchText = string.Empty;
    private string _statusMessage = "准备就绪";
    private bool _isBusy;
    private bool _initialized;

    public ObservableCollection<GameItem> Games { get; } = [];
    public ObservableCollection<GameItem> VisibleGames { get; } = [];
    public GameLoadingProgress Loading { get; } = new();

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = scope == GameInventoryScope.Local
            ? "正在扫描本地 Steam 游戏…"
            : "正在读取云端翻译索引…";
        Loading.Start(StatusMessage);
        try
        {
            var arguments = BuildArguments();
            var result = await _cli.RunAsync(
                arguments,
                HandleEvent,
                networkSettings: App.ViewModel.Settings.Network);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(result.ErrorMessage);
            }

            Games.Clear();
            foreach (var satlEvent in result.Events.Where(item => item.Event == "item-succeeded"))
            {
                Games.Add(GameItem.FromPayload(satlEvent.Payload));
            }
            ApplyFilter();
            StatusMessage = $"共 {Games.Count} 个{(scope == GameInventoryScope.Local ? "本地游戏" : "云端条目")}";
            Loading.Finish(StatusMessage);
            await App.Logs.WriteAsync("信息", "游戏清单", StatusMessage);
        }
        catch (Exception exception)
        {
            StatusMessage = "加载失败";
            Loading.Fail(StatusMessage);
            App.ViewModel.ShowInfo(exception.Message, InfoBarSeverity.Error);
            await App.Logs.WriteAsync("错误", "游戏清单", exception.ToString());
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<string> BuildArguments()
    {
        var settings = App.ViewModel.Settings;
        var arguments = new List<string>
        {
            "scan",
            "--scope",
            scope == GameInventoryScope.Local ? "local" : "cloud",
            "--jsonl",
        };
        if (!string.IsNullOrWhiteSpace(settings.DataDirectory))
        {
            arguments.AddRange(["--data-dir", settings.DataDirectory]);
        }
        if (scope == GameInventoryScope.Local && !string.IsNullOrWhiteSpace(settings.SteamDirectory))
        {
            arguments.AddRange(["--steam-dir", settings.SteamDirectory]);
        }
        if (settings.Offline)
        {
            arguments.Add("--offline");
        }
        return arguments;
    }

    private void HandleEvent(SatlEvent satlEvent)
    {
        void UpdateUi()
        {
            Loading.Handle(satlEvent);
            if (Loading.IsActive)
            {
                StatusMessage = Loading.Text;
            }
            if (satlEvent.Event == "warning"
                && satlEvent.Payload.TryGetProperty("message", out var warning))
            {
                App.ViewModel.ShowInfo(
                    warning.GetString() ?? "正在使用本地缓存。",
                    InfoBarSeverity.Warning);
            }
        }

        if (App.DispatcherQueue.HasThreadAccess)
        {
            UpdateUi();
        }
        else
        {
            App.DispatcherQueue.TryEnqueue(UpdateUi);
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
}
