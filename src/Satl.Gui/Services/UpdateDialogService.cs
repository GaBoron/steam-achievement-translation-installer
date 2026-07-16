using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Satl_Gui.Services;

public static class UpdateDialogService
{
    public static async Task ShowAsync(
        XamlRoot xamlRoot,
        UpdateCheckResult update,
        UpdateService updateService)
    {
        var notes = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                ? "此版本未提供发布说明。"
                : update.ReleaseNotes,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        var notesScroller = new ScrollViewer
        {
            Content = notes,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Visibility = Visibility.Collapsed,
        };
        var status = new TextBlock
        {
            Text = "下载后会校验 Release 提供的 SHA-256，随后打开安装界面并关闭当前窗口。",
            TextWrapping = TextWrapping.Wrap,
        };
        var content = new StackPanel { Spacing = 12, MinWidth = 560 };
        content.Children.Add(new TextBlock
        {
            Text = $"v{update.CurrentVersion} → v{update.LatestVersion}",
            Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
        });
        content.Children.Add(notesScroller);
        content.Children.Add(progress);
        content.Children.Add(status);

        using var cancellation = new CancellationTokenSource();
        var downloading = false;
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"发现新版本 v{update.LatestVersion}",
            Content = content,
            PrimaryButtonText = "下载并安装",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            downloading = true;
            dialog.IsPrimaryButtonEnabled = false;
            progress.Visibility = Visibility.Visible;
            progress.IsIndeterminate = false;
            status.Text = "正在下载安装程序…";
            var reporter = new Progress<double>(value =>
            {
                progress.Value = value * 100;
                status.Text = $"正在下载安装程序… {value:P0}";
            });
            try
            {
                var installer = await updateService.DownloadInstallerAsync(
                    update,
                    reporter,
                    cancellation.Token);
                status.Text = "校验完成，正在打开安装界面…";
                Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
                await App.Logs.WriteAsync("信息", "更新", $"已启动新版安装程序：{installer}");
                dialog.Hide();
                App.Window.Close();
                Application.Current.Exit();
            }
            catch (OperationCanceledException)
            {
                status.Text = "下载已取消。";
                dialog.IsPrimaryButtonEnabled = true;
            }
            catch (Exception exception)
            {
                status.Text = $"更新下载失败：{exception.Message}";
                dialog.IsPrimaryButtonEnabled = true;
                await App.Logs.WriteAsync("错误", "更新", exception.ToString());
            }
            finally
            {
                downloading = false;
                deferral.Complete();
            }
        };
        dialog.CloseButtonClick += (_, _) =>
        {
            if (downloading)
            {
                cancellation.Cancel();
            }
        };
        await dialog.ShowAsync();
    }
}
