using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.ViewModels;

namespace Satl_Gui.Pages;

public sealed partial class ManagedPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;
    public ManagedPage() => InitializeComponent();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanAsync();
    private async void Restore_Click(object sender, RoutedEventArgs e) => await ConfirmRestoreAsync(force: false);
    private async void ForceRestore_Click(object sender, RoutedEventArgs e) => await ConfirmRestoreAsync(force: true);

    private async Task ConfirmRestoreAsync(bool force)
    {
        var selected = ViewModel.ManagedGames.Where(item => item.IsSelected && (!force || item.IsModified)).ToList();
        if (selected.Count == 0)
        {
            ViewModel.ShowInfo(force ? "强制恢复仅适用于状态为“已被修改”的所选条目。" : "请先选择至少一个已管理游戏。", InfoBarSeverity.Warning);
            return;
        }
        if (await ViewModel.PreviewRestoreAsync(selected, force) is null)
        {
            return;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = force ? "强制恢复并归档当前文件？" : $"恢复 {selected.Count} 个游戏？",
            Content = force
                ? "当前 schema 已被修改。SATL 会先归档当前文件，再恢复安装前快照。此操作不会结束 Steam。"
                : string.Join("\n", selected.Select(item => item.GameName)),
            PrimaryButtonText = force ? "归档并恢复" : "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RestoreAsync(selected, force);
        }
    }
}
