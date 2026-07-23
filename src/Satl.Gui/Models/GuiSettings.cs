namespace Satl_Gui.Models;

public sealed class GuiSettings
{
    public string SteamDirectory { get; set; } = string.Empty;
    public string DataDirectory { get; set; } = string.Empty;
    public bool Offline { get; set; }
    public string Theme { get; set; } = "system";
    public bool LoggingEnabled { get; set; } = true;
    public string LogLevel { get; set; } = "standard";
    public int LogRetentionDays { get; set; } = 30;
    public bool LogWordWrap { get; set; } = true;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public NetworkSettings Network { get; set; } = new();
}
