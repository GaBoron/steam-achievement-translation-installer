using System.Text.Json;
using Satl_Gui.Models;
using Satl_Gui.Serialization;

namespace Satl_Gui.Services;

public sealed class SettingsService
{
    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTranslationInstaller",
            "gui-settings.json");
    }

    public string SettingsPath => _path;

    public async Task<GuiSettings> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return new GuiSettings();
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var settings = await JsonSerializer.DeserializeAsync(
                stream,
                SatlJsonSerializerContext.Default.GuiSettings) ?? new GuiSettings();
            settings.LogLevel = PersistentLogLevel(settings.LogLevel);
            return settings;
        }
        catch (JsonException)
        {
            return new GuiSettings();
        }
        catch (IOException)
        {
            return new GuiSettings();
        }
    }

    public async Task SaveAsync(GuiSettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var persistentSettings = new GuiSettings
                {
                    SteamDirectory = settings.SteamDirectory,
                    DataDirectory = settings.DataDirectory,
                    Offline = settings.Offline,
                    Theme = settings.Theme,
                    LoggingEnabled = settings.LoggingEnabled,
                    LogLevel = PersistentLogLevel(settings.LogLevel),
                    LogRetentionDays = settings.LogRetentionDays,
                    LogWordWrap = settings.LogWordWrap,
                    CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup,
                };
                await JsonSerializer.SerializeAsync(
                    stream,
                    persistentSettings,
                    SatlJsonSerializerContext.Default.GuiSettings);
                await stream.FlushAsync();
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string PersistentLogLevel(string level) => level switch
    {
        "detailed" => "detailed",
        "debug" => "detailed",
        _ => "standard",
    };
}
