using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.ViewModels;

namespace Satl_Gui.Pages;

public sealed partial class GamesPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public GamesPage()
    {
        InitializeComponent();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanAsync();

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.Games.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ViewModel.ShowInfo("请先选择至少一个游戏。", InfoBarSeverity.Warning);
            return;
        }

        var preview = await ViewModel.PreviewInstallAsync(selected);
        if (preview is null)
        {
            return;
        }
        var outdated = selected.Count(item => !item.IsCurrent);
        var details = string.Join("\n", selected.Select(item => $"{item.GameName} · {item.SelectedVariant?.VariantId ?? "default"}"));
        if (outdated > 0)
        {
            details += $"\n\n其中 {outdated} 个条目不是 current 状态，请确认仍要继续。";
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"安装 {selected.Count} 个翻译？",
            Content = details,
            PrimaryButtonText = "安装",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.InstallAsync(selected);
        }
    }
}
