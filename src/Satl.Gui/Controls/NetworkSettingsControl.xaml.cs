using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satl_Gui.Models;

namespace Satl_Gui.Controls;

public sealed partial class NetworkSettingsControl : UserControl
{
    private bool _isInitializing;

    public event EventHandler? SettingsChanged;
    public event EventHandler? TestConnectionRequested;

    public NetworkSettingsControl()
    {
        InitializeComponent();
    }

    public void LoadSettings(NetworkSettings settings)
    {
        _isInitializing = true;
        DnsModeBox.SelectedIndex = settings.DnsMode == "custom" ? 1 : 0;
        DnsServersBox.Text = settings.DnsServers;
        ProxyModeBox.SelectedIndex = settings.ProxyMode switch
        {
            "direct" => 1,
            "manual" => 2,
            _ => 0,
        };
        ProxyAddressBox.Text = settings.ProxyAddress;
        ProxyUsernameBox.Text = settings.ProxyUsername;
        ProxyPasswordBox.Password = settings.ProxyPassword;
        UpdateFieldAvailability();
        _isInitializing = false;
    }

    public NetworkSettings ReadSettings() => new()
    {
        DnsMode = SelectedTag(DnsModeBox, "system"),
        DnsServers = DnsServersBox.Text,
        ProxyMode = SelectedTag(ProxyModeBox, "system"),
        ProxyAddress = ProxyAddressBox.Text,
        ProxyUsername = ProxyUsernameBox.Text,
        ProxyPassword = ProxyPasswordBox.Password,
    };

    public void SetTestState(bool isRunning, string message)
    {
        TestConnectionButton.IsEnabled = !isRunning;
        TestConnectionButton.Content = isRunning ? "正在测试…" : "测试网络连接";
        TestStatusText.Text = message;
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFieldAvailability();
        NotifyChanged();
    }

    private void Field_LostFocus(object sender, RoutedEventArgs e) => NotifyChanged();

    private void TestConnectionButton_Click(object sender, RoutedEventArgs e) =>
        TestConnectionRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateFieldAvailability()
    {
        var customDns = SelectedTag(DnsModeBox, "system") == "custom";
        CustomDnsPanel.Visibility = customDns ? Visibility.Visible : Visibility.Collapsed;
        var proxyMode = SelectedTag(ProxyModeBox, "system");
        ManualProxyPanel.Visibility = proxyMode == "manual"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void NotifyChanged()
    {
        if (!_isInitializing)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string SelectedTag(ComboBox box, string fallback) =>
        (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

}
