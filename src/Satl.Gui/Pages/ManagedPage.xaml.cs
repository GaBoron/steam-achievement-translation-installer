using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Satl_Gui.ViewModels;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Satl_Gui.Pages;

public sealed partial class ManagedPage : Page
{
    private int? _selectionAnchorIndex;
    public MainViewModel ViewModel => App.ViewModel;
    public ManagedPage()
    {
        InitializeComponent();
        AddShortcut(VirtualKey.A, VirtualKeyModifiers.Control, SelectAll_Invoked);
        AddShortcut(
            VirtualKey.A,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            ClearSelection_Invoked);
        AddShortcut(VirtualKey.F5, VirtualKeyModifiers.None, Refresh_Invoked);
    }

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
        var previews = await ViewModel.PreviewRestoreAsync(selected, force);
        if (previews is null)
        {
            return;
        }
        if (await ReplacementConfirmationDialog.ShowAsync(
                XamlRoot,
                previews,
                force
                    ? $"确认强制恢复 {selected.Count} 个游戏并归档当前文件"
                    : $"确认恢复 {selected.Count} 个游戏",
                force ? "确认归档并恢复" : "确认恢复"))
        {
            await ViewModel.RestoreAsync(selected, force);
        }
    }

    private void GameSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not GameItem item)
        {
            return;
        }
        var index = ViewModel.ManagedGames.IndexOf(item);
        if (index < 0)
        {
            return;
        }
        var shiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (shiftPressed
            && _selectionAnchorIndex is int anchor
            && anchor >= 0
            && anchor < ViewModel.ManagedGames.Count)
        {
            var selected = checkBox.IsChecked == true;
            for (var position = Math.Min(anchor, index); position <= Math.Max(anchor, index); position++)
            {
                ViewModel.ManagedGames[position].IsSelected = selected;
            }
        }
        _selectionAnchorIndex = index;
    }

    private void AddShortcut(
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += handler;
        KeyboardAccelerators.Add(accelerator);
    }

    private void SelectAll_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        foreach (var item in ViewModel.ManagedGames)
        {
            item.IsSelected = true;
        }
        args.Handled = true;
    }

    private void ClearSelection_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        foreach (var item in ViewModel.ManagedGames)
        {
            item.IsSelected = false;
        }
        _selectionAnchorIndex = null;
        args.Handled = true;
    }

    private async void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.ScanAsync();
    }
}
