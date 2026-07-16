using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public static class ReplacementConfirmationDialog
{
    private static readonly GridLength IndexWidth = new(54);
    private static readonly GridLength ApiNameWidth = new(180);
    private static readonly GridLength NameWidth = new(220);
    private static readonly GridLength DescriptionWidth = new(320);
    private static readonly GridLength OtherWidth = new(360);

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
        var heading = new TextBlock
        {
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
            TextWrapping = TextWrapping.Wrap,
        };
        var explanation = new TextBlock
        {
            Text = "下表来自即将写入的 BIN 文件，并已通过 Binary KeyValues 字节级 roundtrip 校验。空白表示文件中没有该语言字段。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush,
        };
        var tableHost = new Grid { MinHeight = 360 };
        var previous = new Button { Content = "上一页" };
        var next = new Button { Content = "下一页" };
        var pageText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(previous, "显示上一个游戏");
        AutomationProperties.SetName(next, "显示下一个游戏");

        void RenderPage()
        {
            var preview = previews[page];
            heading.Text = preview.DeletesTarget
                ? $"{preview.GameName} · App ID {preview.AppId} · 将删除当前文件"
                : $"{preview.GameName} · App ID {preview.AppId} · {preview.VariantId} · {preview.AchievementCount} 项成就";
            pageText.Text = $"第 {page + 1} / {previews.Count} 页";
            previous.IsEnabled = page > 0;
            next.IsEnabled = page + 1 < previews.Count;
            tableHost.Children.Clear();
            tableHost.Children.Add(BuildTable(preview));
        }

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

        var content = new Grid { RowSpacing = 12, MinWidth = 900 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(heading);
        Grid.SetRow(explanation, 1);
        content.Children.Add(explanation);
        Grid.SetRow(tableHost, 2);
        content.Children.Add(tableHost);
        Grid.SetRow(pager, 3);
        content.Children.Add(pager);

        RenderPage();
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = confirmText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1180d;
        dialog.Resources["ContentDialogMinWidth"] = 940d;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static UIElement BuildTable(ReplacementPreview preview)
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
        rows.Children.Add(BuildRow(
            "#",
            "成就 ID",
            "简体中文名称",
            "简体中文说明",
            "英文名称",
            "英文说明",
            "其他语言",
            isHeader: true));
        foreach (var row in preview.Rows)
        {
            rows.Children.Add(BuildRow(
                row.Index.ToString(),
                row.ApiName,
                row.SimplifiedChineseName,
                row.SimplifiedChineseDescription,
                row.EnglishName,
                row.EnglishDescription,
                row.OtherLanguages,
                isHeader: false));
        }
        if (preview.Rows.Count == 0)
        {
            rows.Children.Add(new TextBlock
            {
                Text = "此 BIN 中没有识别到成就记录。",
                Padding = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        return new ScrollViewer
        {
            Content = rows,
            Height = 410,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
        };
    }

    private static Grid BuildRow(
        string index,
        string apiName,
        string chineseName,
        string chineseDescription,
        string englishName,
        string englishDescription,
        string otherLanguages,
        bool isHeader)
    {
        var grid = new Grid
        {
            MinWidth = 1660,
            Padding = new Thickness(8),
            ColumnSpacing = 12,
            Background = isHeader
                ? Application.Current.Resources["SubtleFillColorSecondaryBrush"] as Brush
                : null,
        };
        foreach (var width in new[]
                 {
                     IndexWidth,
                     ApiNameWidth,
                     NameWidth,
                     DescriptionWidth,
                     NameWidth,
                     DescriptionWidth,
                     OtherWidth,
                 })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }
        var values = new[]
        {
            index,
            apiName,
            chineseName,
            chineseDescription,
            englishName,
            englishDescription,
            otherLanguages,
        };
        for (var column = 0; column < values.Length; column++)
        {
            var text = new TextBlock
            {
                Text = values[column],
                TextWrapping = TextWrapping.Wrap,
                FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            };
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
        return grid;
    }
}
