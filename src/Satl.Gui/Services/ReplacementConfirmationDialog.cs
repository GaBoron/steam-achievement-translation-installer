using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public static class ReplacementConfirmationDialog
{
    private static readonly IReadOnlyDictionary<string, string> LanguageNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["schinese"] = "简体中文",
            ["tchinese"] = "繁体中文",
            ["english"] = "英语",
            ["japanese"] = "日语",
            ["koreana"] = "韩语",
            ["french"] = "法语",
            ["italian"] = "意大利语",
            ["german"] = "德语",
            ["spanish"] = "西班牙语",
            ["latam"] = "拉丁美洲西班牙语",
            ["russian"] = "俄语",
            ["thai"] = "泰语",
            ["portuguese"] = "葡萄牙语",
            ["brazilian"] = "巴西葡萄牙语",
            ["polish"] = "波兰语",
            ["danish"] = "丹麦语",
            ["dutch"] = "荷兰语",
            ["finnish"] = "芬兰语",
            ["norwegian"] = "挪威语",
            ["swedish"] = "瑞典语",
            ["czech"] = "捷克语",
            ["hungarian"] = "匈牙利语",
            ["romanian"] = "罗马尼亚语",
            ["turkish"] = "土耳其语",
            ["ukrainian"] = "乌克兰语",
            ["vietnamese"] = "越南语",
            ["indonesian"] = "印度尼西亚语",
            ["arabic"] = "阿拉伯语",
            ["bulgarian"] = "保加利亚语",
            ["greek"] = "希腊语",
        };

    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        IReadOnlyList<ReplacementPreview> previews,
        string title,
        string confirmText)
    {
        if (previews.Count == 0)
        {
            return false;
        }

        var page = 0;
        var selectedLanguages = new Dictionary<int, string>();
        var heading = new TextBlock
        {
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
            TextWrapping = TextWrapping.Wrap,
        };
        var explanation = new TextBlock
        {
            Text = "下表来自即将写入的 BIN 文件，并已通过 Binary KeyValues 字节级 roundtrip 校验。语言列表由该 BIN 的名称和说明字段自动扫描生成。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
        };
        var languageBox = new ComboBox
        {
            MinWidth = 220,
            DisplayMemberPath = nameof(LanguageOption.DisplayName),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(languageBox, "选择成就显示语言");
        var languageBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        languageBar.Children.Add(new TextBlock
        {
            Text = "显示语言",
            VerticalAlignment = VerticalAlignment.Center,
        });
        languageBar.Children.Add(languageBox);

        var tableHost = new Grid();
        var previous = new Button { Content = "上一页" };
        var next = new Button { Content = "下一页" };
        var pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(previous, "显示上一个游戏");
        AutomationProperties.SetName(next, "显示下一个游戏");

        void RenderTable()
        {
            var preview = previews[page];
            var language = selectedLanguages.TryGetValue(page, out var selected)
                ? selected
                : preview.DefaultLanguage;
            tableHost.Children.Clear();
            tableHost.Children.Add(BuildTable(preview, language));
        }

        void RenderPage()
        {
            var preview = previews[page];
            heading.Text = preview.DeletesTarget
                ? $"{preview.GameName} · App ID {preview.AppId} · 将删除当前文件"
                : $"{preview.GameName} · App ID {preview.AppId} · {preview.VariantId} · {preview.AchievementCount} 项成就";
            pageText.Text = $"第 {page + 1} / {previews.Count} 页";
            previous.IsEnabled = page > 0;
            next.IsEnabled = page + 1 < previews.Count;

            var options = preview.Languages
                .Select(code => new LanguageOption(code, DisplayLanguage(code)))
                .ToList();
            languageBar.Visibility = preview.DeletesTarget || options.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            languageBox.ItemsSource = options;
            var selected = selectedLanguages.TryGetValue(page, out var remembered)
                && options.Any(option => option.Code.Equals(remembered, StringComparison.OrdinalIgnoreCase))
                    ? remembered
                    : preview.DefaultLanguage;
            selectedLanguages[page] = selected;
            languageBox.SelectedItem = options.FirstOrDefault(
                option => option.Code.Equals(selected, StringComparison.OrdinalIgnoreCase));
            RenderTable();
        }

        languageBox.SelectionChanged += (_, _) =>
        {
            if (languageBox.SelectedItem is LanguageOption option)
            {
                selectedLanguages[page] = option.Code;
                RenderTable();
            }
        };
        previous.Click += (_, _) =>
        {
            if (page > 0)
            {
                page--;
                RenderPage();
            }
        };
        next.Click += (_, _) =>
        {
            if (page + 1 < previews.Count)
            {
                page++;
                RenderPage();
            }
        };

        var pager = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
        };
        pager.Children.Add(previous);
        pager.Children.Add(pageText);
        pager.Children.Add(next);

        var content = new Grid { RowSpacing = 10 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(heading);
        Grid.SetRow(explanation, 1);
        content.Children.Add(explanation);
        Grid.SetRow(languageBar, 2);
        content.Children.Add(languageBar);
        Grid.SetRow(tableHost, 3);
        content.Children.Add(tableHost);
        Grid.SetRow(pager, 4);
        content.Children.Add(pager);

        RenderPage();
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = confirmText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        void ApplyAdaptiveSize()
        {
            var dialogWidth = Math.Clamp(xamlRoot.Size.Width - 48, 420, 1280);
            var contentWidth = Math.Max(340, dialogWidth - 48);
            dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
            dialog.Resources["ContentDialogMinWidth"] = Math.Min(640, dialogWidth);
            content.Width = contentWidth;
            content.MaxWidth = contentWidth;
            tableHost.Height = Math.Clamp(xamlRoot.Size.Height - 330, 220, 600);
        }

        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyAdaptiveSize();
        ApplyAdaptiveSize();
        xamlRoot.Changed += RootChanged;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            xamlRoot.Changed -= RootChanged;
        }
    }

    private static UIElement BuildTable(ReplacementPreview preview, string language)
    {
        if (preview.DeletesTarget)
        {
            return new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = "恢复目标为空",
                Message = "安装前不存在原始 BIN 文件。确认恢复后，软件将删除 SATL 安装的当前文件。",
            };
        }

        var rows = new StackPanel { Spacing = 1 };
        rows.Children.Add(BuildRow("#", "成就 ID", "名称", "说明", isHeader: true));
        foreach (var row in preview.Rows)
        {
            var translation = row.TranslationFor(language);
            rows.Children.Add(BuildRow(
                row.Index.ToString(),
                row.ApiName,
                translation.Name,
                translation.Description,
                isHeader: false));
        }
        if (preview.Rows.Count == 0)
        {
            rows.Children.Add(new TextBlock
            {
                Text = "此 BIN 中没有识别到成就记录。",
                Padding = new Thickness(10),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        return new ScrollViewer
        {
            Content = rows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
        };
    }

    private static Grid BuildRow(
        string index,
        string apiName,
        string name,
        string description,
        bool isHeader)
    {
        var grid = new Grid
        {
            Padding = new Thickness(8, 7, 8, 7),
            ColumnSpacing = 8,
            Background = isHeader
                ? Application.Current.Resources["SubtleFillColorSecondaryBrush"] as Brush
                : null,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        var values = new[] { index, apiName, name, description };
        for (var column = 0; column < values.Length; column++)
        {
            var text = new TextBlock
            {
                Text = values[column],
                TextWrapping = TextWrapping.Wrap,
                FontWeight = isHeader
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
            };
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
        return grid;
    }

    private static string DisplayLanguage(string code) =>
        LanguageNames.TryGetValue(code, out var displayName)
            ? $"{displayName} ({code})"
            : code;

    private sealed record LanguageOption(string Code, string DisplayName);
}
