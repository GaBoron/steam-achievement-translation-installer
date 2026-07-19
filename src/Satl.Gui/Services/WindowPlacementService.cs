using System.Text.Json;
using Satl_Gui.Serialization;

namespace Satl_Gui.Services;

public sealed record WindowPlacement(int X, int Y, int Width, int Height);

public sealed class WindowPlacementService
{
    public const int DefaultWidth = 1280;
    public const int DefaultHeight = 720;
    private const int MinimumWidth = 640;
    private const int MinimumHeight = 480;
    private readonly string _path;

    public WindowPlacementService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTranslationInstaller",
            "window-placement.json");
    }

    public WindowPlacement? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(_path),
                SatlJsonSerializerContext.Default.WindowPlacement);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        var directory = Path.GetDirectoryName(_path)!;
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(
                    placement,
                    SatlJsonSerializerContext.Default.WindowPlacement));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException)
        {
            // Window placement is a convenience and must never block app shutdown.
        }
        catch (UnauthorizedAccessException)
        {
            // Window placement is a convenience and must never block app shutdown.
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // Best-effort cleanup for a failed atomic save.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup for a failed atomic save.
            }
        }
    }

    public static WindowPlacement CenterDefault(int workX, int workY, int workWidth, int workHeight)
    {
        var width = Math.Min(DefaultWidth, workWidth);
        var height = Math.Min(DefaultHeight, workHeight);
        return new WindowPlacement(
            workX + ((workWidth - width) / 2),
            workY + ((workHeight - height) / 2),
            width,
            height);
    }

    public static WindowPlacement FitToWorkArea(
        WindowPlacement placement,
        int workX,
        int workY,
        int workWidth,
        int workHeight)
    {
        var minimumWidth = Math.Min(MinimumWidth, workWidth);
        var minimumHeight = Math.Min(MinimumHeight, workHeight);
        var width = Math.Clamp(placement.Width, minimumWidth, workWidth);
        var height = Math.Clamp(placement.Height, minimumHeight, workHeight);
        var x = Math.Clamp(placement.X, workX, workX + workWidth - width);
        var y = Math.Clamp(placement.Y, workY, workY + workHeight - height);
        return new WindowPlacement(x, y, width, height);
    }
}
