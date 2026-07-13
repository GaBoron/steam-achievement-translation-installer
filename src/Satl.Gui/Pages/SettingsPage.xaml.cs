using System.Diagnostics;
using Microsoft.Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.ViewModels;

namespace Satl_Gui.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private bool _isInitializing;
    private string _steamDirectory = string.Empty;
    private string _dataDirectory = string.Empty;

    public MainViewModel ViewModel => App.ViewModel;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        _steamDirectory = ViewModel.Settings.SteamDirectory;
        _dataDirectory = ViewModel.Settings.DataDirectory;
        OfflineSwitch.IsOn = ViewModel.Settings.Offline;
        ThemeBox.SelectedIndex = ViewModel.Settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        RefreshDirectoryLabels();
        _isInitializing = false;
    }

    private async Task ApplySettingsAsync()
    {
        await _settingsGate.WaitAsync();
        try
        {
            var theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
            await ViewModel.UpdateSettingsAsync(new GuiSettings
            {
                SteamDirectory = _steamDirectory,
                DataDirectory = _dataDirectory,
                Offline = OfflineSwitch.IsOn,
                Theme = theme,
            });
            RefreshDirectoryLabels();
        }
        catch (Exception exception)
        {
            ViewModel.ShowInfo($"无法应用设置：{exception.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private async void RefreshCache_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshCacheAsync();

    private async void BrowseSteamDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickDirectoryAsync("SteamDirectoryPicker");
        if (path is null)
        {
            return;
        }
        _steamDirectory = path;
        await ApplySettingsAsync();
    }

    private async void BrowseDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickDirectoryAsync("DataDirectoryPicker");
        if (path is null)
        {
            return;
        }
        _dataDirectory = path;
        await ApplySettingsAsync();
    }

    private async void ResetSteamDirectory_Click(object sender, RoutedEventArgs e)
    {
        _steamDirectory = string.Empty;
        await ApplySettingsAsync();
    }

    private async void ResetDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        _dataDirectory = string.Empty;
        await ApplySettingsAsync();
    }

    private async void OfflineSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing)
        {
            await ApplySettingsAsync();
        }
    }

    private async void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && IsLoaded)
        {
            await ApplySettingsAsync();
        }
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.CurrentDataDirectory;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private async Task<string?> PickDirectoryAsync(string settingsIdentifier)
    {
        try
        {
            var picker = new FolderPicker(App.Window.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                SettingsIdentifier = settingsIdentifier,
                CommitButtonText = "选择此文件夹",
            };
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception exception)
        {
            ViewModel.ShowInfo($"无法打开文件夹选择器：{exception.Message}", InfoBarSeverity.Error);
            return null;
        }
    }

    private void RefreshDirectoryLabels()
    {
        SteamDirectoryText.Text = ViewModel.CurrentSteamDirectory;
        DataDirectoryText.Text = ViewModel.CurrentDataDirectory;
        ToolTipService.SetToolTip(SteamDirectoryText, ViewModel.CurrentSteamDirectory);
        ToolTipService.SetToolTip(DataDirectoryText, ViewModel.CurrentDataDirectory);
    }
}
