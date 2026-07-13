using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;
using Satl_Gui.ViewModels;

namespace Satl_Gui.Pages;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        SteamDirectoryBox.Text = ViewModel.Settings.SteamDirectory;
        DataDirectoryBox.Text = ViewModel.Settings.DataDirectory;
        OfflineSwitch.IsOn = ViewModel.Settings.Offline;
        ThemeBox.SelectedIndex = ViewModel.Settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var theme = (ThemeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "system";
        await ViewModel.SaveSettingsAsync(new GuiSettings
        {
            SteamDirectory = SteamDirectoryBox.Text.Trim(),
            DataDirectory = DataDirectoryBox.Text.Trim(),
            Offline = OfflineSwitch.IsOn,
            Theme = theme,
        });
    }

    private async void RefreshCache_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshCacheAsync();

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var path = string.IsNullOrWhiteSpace(DataDirectoryBox.Text)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamAchievementTranslationInstaller")
            : DataDirectoryBox.Text.Trim();
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }
}
