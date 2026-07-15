using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Satl_Gui.Pages;

public sealed partial class LogsPage : Page
{
    private string _allLogs = string.Empty;

    public LogsPage()
    {
        InitializeComponent();
        Loaded += LogsPage_Loaded;
    }

    private async void LogsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWordWrap(App.ViewModel.Settings.LogWordWrap);
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清理全部日志？",
            Content = "这会删除本机日志目录中的 SATL GUI 日志，且无法撤销。",
            PrimaryButtonText = "清理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await App.Logs.ClearAsync();
            _allLogs = string.Empty;
            ApplyFilter();
            App.ViewModel.ShowInfo("日志已清理。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法清理日志：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ApplyFilter();
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            _allLogs = await App.Logs.ReadRecentAsync();
            ApplyFilter();
        }
        catch (Exception exception)
        {
            App.ViewModel.ShowInfo($"无法读取日志：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var content = string.IsNullOrWhiteSpace(query)
            ? _allLogs
            : string.Join(
                Environment.NewLine,
                _allLogs.Split(["\r\n", "\n"], StringSplitOptions.None)
                    .Where(line => line.Contains(query, StringComparison.CurrentCultureIgnoreCase)));
        LogTextBox.Text = string.IsNullOrWhiteSpace(content) ? "暂无日志。" : content;
        ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        LogTextBox.Select(LogTextBox.Text.Length, 0);
        LogTextBox.DispatcherQueue.TryEnqueue(() =>
        {
            LogTextBox.UpdateLayout();
            FindScrollViewer(LogTextBox)?.ChangeView(
                horizontalOffset: null,
                verticalOffset: double.MaxValue,
                zoomFactor: null,
                disableAnimation: true);
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            if (FindScrollViewer(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ApplyWordWrap(bool enabled)
    {
        LogTextBox.TextWrapping = enabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(
            LogTextBox,
            enabled ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }
}
