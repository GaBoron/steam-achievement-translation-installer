using System.Text;

namespace Satl_Gui.Services;

public sealed class LogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _enabled;
    private string _level = "standard";
    private int _retentionDays = 30;

    public LogService(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTranslationInstaller",
            "logs");
    }

    public string DirectoryPath { get; }

    public void Configure(bool enabled, string level, int retentionDays)
    {
        _enabled = enabled;
        _level = level == "detailed" ? "detailed" : "standard";
        _retentionDays = retentionDays is 7 or 30 or 90 ? retentionDays : 30;
        _ = PruneAsync();
    }

    public async Task WriteAsync(
        string level,
        string category,
        string message,
        bool detailed = false)
    {
        if (!_enabled || (detailed && _level != "detailed"))
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{Clean(level)}] [{Clean(category)}] {Clean(message)}{Environment.NewLine}";
            var path = Path.Combine(DirectoryPath, $"satl-gui-{DateTimeOffset.Now:yyyy-MM-dd}.log");
            await File.AppendAllTextAsync(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (IOException)
        {
            // Logging must never interrupt installation or recovery operations.
        }
        catch (UnauthorizedAccessException)
        {
            // The UI remains usable even when the configured log directory is unavailable.
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> ReadRecentAsync(int maximumLines = 1000)
    {
        await _gate.WaitAsync();
        try
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return string.Empty;
            }

            var files = Directory.EnumerateFiles(DirectoryPath, "satl-gui-*.log")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var output = new List<string>();
            foreach (var file in files)
            {
                var lines = await File.ReadAllLinesAsync(file, Encoding.UTF8);
                var remaining = maximumLines - output.Count;
                if (remaining <= 0)
                {
                    break;
                }
                output.AddRange(lines.TakeLast(remaining));
            }
            return string.Join(Environment.NewLine, output);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return;
            }
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "satl-gui-*.log"))
            {
                File.Delete(file);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PruneAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return;
            }
            var cutoff = DateTime.Now.Date.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "satl-gui-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
            // Retention cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Retention cleanup is best effort.
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Clean(string value) => value
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();
}
