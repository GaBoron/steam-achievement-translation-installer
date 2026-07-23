using System.Text.Json.Serialization;

namespace Satl_Gui.Models;

public sealed class NetworkSettings
{
    public string DnsMode { get; set; } = "system";
    public string DnsServers { get; set; } = "1.1.1.1; 8.8.8.8";
    public string ProxyMode { get; set; } = "system";
    public string ProxyAddress { get; set; } = string.Empty;
    public string ProxyUsername { get; set; } = string.Empty;
    [JsonIgnore]
    public string ProxyPassword { get; set; } = string.Empty;
    public string ProtectedProxyPassword { get; set; } = string.Empty;
}
