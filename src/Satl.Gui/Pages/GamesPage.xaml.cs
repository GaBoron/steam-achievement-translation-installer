using System.Diagnostics;
using System.Globalization;
using Microsoft.Windows.Storage.Pickers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Satl_Gui.ViewModels;
using Windows.System;
using Windows.UI.Core;
using Windows.Foundation;

namespace Satl_Gui.Pages;

public sealed partial class GamesPage : Page
{
    private const string PetitionUrl =
        "https://github.com/GaBoron/steam-achievement-translation-library/issues/new?template=translation_petition_zh.yml";
    private const string ContributionUrl =
        "https://github.com/GaBoron/steam-achievement-translation-library/issues/new?template=translation_contribution_zh.yml";

    private int? _selectionAnchorIndex;

    public MainViewModel ViewModel => App.ViewModel;

    public GamesPage()
    {
        InitializeComponent();
        AddShortcut(VirtualKey.A, VirtualKeyModifiers.Control, SelectAll_Invoked);
        AddShortcut(
            VirtualKey.A,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            ClearSelection_Invoked);
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control, FocusSearch_Invoked);
        AddShortcut(VirtualKey.F5, VirtualKeyModifiers.None, Refresh_Invoked);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanAsync();

    private async void Petition_Click(object sender, RoutedEventArgs e)
    {
        var appIdBox = new TextBox
        {
            Header = "Steam App ID",
            PlaceholderText = "例如：123456",
            MaxLength = 20,
        };
        AutomationProperties.SetName(appIdBox, "Steam App ID");
        var content = new StackPanel { Spacing = 12, MaxWidth = 500 };
        content.Children.Add(new TextBlock
        {
            Text = "在 Steam 商店打开游戏，地址中 /app/ 后面的数字就是游戏 ID。也可以在 Steam 的游戏属性页面查看 App ID。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "只有原始文件：输入 ID 并导出请愿 ZIP，再通过“提交翻译请愿”告诉社区你需要哪些语言。请愿不会把原始文件直接收录为翻译。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "已经完成翻译：选择“贡献翻译”。投稿 ZIP 必须命名为 UserGameStatsSchema_<app_id>.zip，并完整包含所声明语言的成就名称和说明。提交前请先在翻译库索引中搜索 App ID；已收录游戏应使用“更新已有翻译”模板。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(appIdBox);
        var petitionLink = new HyperlinkButton { Content = "提交翻译请愿" };
        petitionLink.Click += (_, _) => OpenExternalUrl(PetitionUrl, "翻译请愿");
        content.Children.Add(petitionLink);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "翻译请愿",
            Content = content,
            PrimaryButtonText = "导出请愿 ZIP",
            SecondaryButtonText = "贡献翻译",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        appIdBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = IsValidAppId(appIdBox.Text);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            OpenExternalUrl(ContributionUrl, "翻译贡献");
            return;
        }
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var appId = appIdBox.Text.Trim();
        var output = await PickPetitionDestinationAsync(appId);
        if (output is not null)
        {
            await ViewModel.ExportPetitionAsync(appId, output);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.Games.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ViewModel.ShowInfo("请先选择至少一个游戏。", InfoBarSeverity.Warning);
            return;
        }

        var previews = await ViewModel.PreviewInstallAsync(selected);
        if (previews is null)
        {
            return;
        }
        var outdated = selected.Count(item => !item.IsCurrent);
        var title = outdated == 0
            ? $"确认安装 {selected.Count} 个翻译"
            : $"确认安装 {selected.Count} 个翻译（{outdated} 个可能已过期）";
        if (await ReplacementConfirmationDialog.ShowAsync(
                XamlRoot,
                previews,
                title,
                "确认安装"))
        {
            await ViewModel.InstallAsync(selected);
        }
    }

    private void GameSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not GameItem item)
        {
            return;
        }
        var index = ViewModel.VisibleGames.IndexOf(item);
        if (index < 0)
        {
            return;
        }
        var shiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);
        if (shiftPressed
            && _selectionAnchorIndex is int anchor
            && anchor >= 0
            && anchor < ViewModel.VisibleGames.Count)
        {
            var selected = checkBox.IsChecked == true;
            for (var position = Math.Min(anchor, index); position <= Math.Max(anchor, index); position++)
            {
                ViewModel.VisibleGames[position].IsSelected = selected;
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
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or AutoSuggestBox)
        {
            return;
        }
        foreach (var item in ViewModel.VisibleGames)
        {
            item.IsSelected = true;
        }
        args.Handled = true;
    }

    private void ClearSelection_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        foreach (var item in ViewModel.Games)
        {
            item.IsSelected = false;
        }
        _selectionAnchorIndex = null;
        args.Handled = true;
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private async void Refresh_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.ScanAsync();
    }

    private void OpenExternalUrl(string url, string description)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ViewModel.ShowInfo($"无法打开{description}页面：{exception.Message}", InfoBarSeverity.Error);
        }
    }

    private static bool IsValidAppId(string value) =>
        value.Length <= 20
        && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0;

    private async Task<string?> PickPetitionDestinationAsync(string appId)
    {
        try
        {
            var picker = new FileSavePicker(App.Window.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.Downloads,
                SuggestedFileName = $"UserGameStatsSchema_{appId}",
                DefaultFileExtension = ".zip",
                CommitButtonText = "导出",
                SettingsIdentifier = "PetitionSchemaExportPicker",
                ShowOverwritePrompt = true,
                FileTypeChoices =
                {
                    { "ZIP 压缩文件", new List<string> { ".zip" } },
                },
            };
            return (await picker.PickSaveFileAsync())?.Path;
        }
        catch (Exception exception)
        {
            ViewModel.ShowInfo($"无法打开保存位置选择器：{exception.Message}", InfoBarSeverity.Error);
            return null;
        }
    }
}
