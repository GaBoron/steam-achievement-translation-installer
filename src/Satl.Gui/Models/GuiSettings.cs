namespace Satl_Gui.Models;

public sealed class GuiSettings
{
    public string SteamDirectory { get; set; } = string.Empty;
    public string DataDirectory { get; set; } = string.Empty;
    public bool Offline { get; set; }
    public string Theme { get; set; } = "system";
}
