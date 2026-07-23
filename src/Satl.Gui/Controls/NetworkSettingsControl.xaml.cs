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
        DnsTimeoutBox.Value = settings.DnsTimeoutSeconds;
        ProxyModeBox.SelectedIndex = settings.ProxyMode switch
        {
            "direct" => 1,
            "manual" => 2,
            _ => 0,
        };
        ProxyAddressBox.Text = settings.ProxyAddress;
        ProxyUsernameBox.Text = settings.ProxyUsername;
        ProxyPasswordBox.Password = settings.ProxyPassword;
        ProxyBypassBox.Text = settings.ProxyBypassList;
        ProxyBypassLocalSwitch.IsOn = settings.ProxyBypassLocal;
        ConnectTimeoutBox.Value = settings.ConnectTimeoutSeconds;
        UpdateFieldAvailability();
        _isInitializing = false;
    }

    public NetworkSettings ReadSettings() => new()
    {
        DnsMode = SelectedTag(DnsModeBox, "system"),
        DnsServers = DnsServersBox.Text,
        DnsTimeoutSeconds = IntegerValue(DnsTimeoutBox, 5),
        ProxyMode = SelectedTag(ProxyModeBox, "system"),
        ProxyAddress = ProxyAddressBox.Text,
        ProxyUsername = ProxyUsernameBox.Text,
        ProxyPassword = ProxyPasswordBox.Password,
        ProxyBypassList = ProxyBypassBox.Text,
        ProxyBypassLocal = ProxyBypassLocalSwitch.IsOn,
        ConnectTimeoutSeconds = IntegerValue(ConnectTimeoutBox, 15),
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

    private void ToggleSwitch_Toggled(object sender, RoutedEventArgs e) => NotifyChanged();

    private void NumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        NotifyChanged();

    private void TestConnectionButton_Click(object sender, RoutedEventArgs e) =>
        TestConnectionRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateFieldAvailability()
    {
        var customDns = SelectedTag(DnsModeBox, "system") == "custom";
        CustomDnsPanel.IsHitTestVisible = customDns;
        CustomDnsPanel.Opacity = customDns ? 1 : 0.55;
        var manualProxy = SelectedTag(ProxyModeBox, "system") == "manual";
        ManualProxyPanel.IsHitTestVisible = manualProxy;
        ManualProxyPanel.Opacity = manualProxy ? 1 : 0.55;
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

    private static int IntegerValue(NumberBox box, int fallback) =>
        double.IsNaN(box.Value) ? fallback : (int)Math.Round(box.Value);
}
