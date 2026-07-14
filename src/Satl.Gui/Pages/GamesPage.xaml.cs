using System.Diagnostics;
using System.Globalization;
using Microsoft.Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.ViewModels;

namespace Satl_Gui.Pages;

public sealed partial class GamesPage : Page
{
    private const string PetitionUrl =
        "https://github.com/GaBoron/steam-achievement-translation-library/issues/new?template=translation_petition_zh.yml";

    public MainViewModel ViewModel => App.ViewModel;

    public GamesPage()
    {
        InitializeComponent();
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
            Text = "先输入 ID 并导出 ZIP。软件会自动查找 Steam 生成的原始成就文件，并按翻译库要求打包。提交请愿时，请填写游戏名、商店地址和目标语言，再把导出的 ZIP 拖到“需要翻译的成就 schema ZIP”字段。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(appIdBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "翻译请愿",
            Content = content,
            PrimaryButtonText = "导出",
            SecondaryButtonText = "提交请愿",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        appIdBox.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = IsValidAppId(appIdBox.Text);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            try
            {
                Process.Start(new ProcessStartInfo(PetitionUrl) { UseShellExecute = true });
            }
            catch (Exception exception)
            {
                ViewModel.ShowInfo($"无法打开翻译请愿页面：{exception.Message}", InfoBarSeverity.Error);
            }
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
