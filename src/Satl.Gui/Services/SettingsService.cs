using System.Text.Json;
using Satl_Gui.Models;

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
            return await JsonSerializer.DeserializeAsync<GuiSettings>(stream) ?? new GuiSettings();
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
                await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true });
                await stream.FlushAsync();
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
