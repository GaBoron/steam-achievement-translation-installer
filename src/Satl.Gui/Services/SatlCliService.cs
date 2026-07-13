using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed class SatlCliService
{
    public async Task<CliRunResult> RunAsync(IEnumerable<string> arguments, Action<SatlEvent>? onEvent = null)
    {
        var launch = ResolveLaunch();
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.FileName,
            WorkingDirectory = launch.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var prefix in launch.PrefixArguments)
        {
            startInfo.ArgumentList.Add(prefix);
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var environment in launch.Environment)
        {
            startInfo.Environment[environment.Key] = environment.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 satl 命令行核心。");
        }

        var stderrTask = process.StandardError.ReadToEndAsync();
        var events = new List<SatlEvent>();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var parsed = ParseEvent(line);
            events.Add(parsed);
            onEvent?.Invoke(parsed);
        }

        await process.WaitForExitAsync();
        return new CliRunResult(process.ExitCode, events, (await stderrTask).Trim());
    }

    public static SatlEvent ParseEvent(string line)
    {
        SatlEvent parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SatlEvent>(line)
                ?? throw new InvalidDataException("SATL 返回了空事件。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"SATL 返回了无效事件：{line}", exception);
        }
        if (parsed.ProtocolVersion != 1)
        {
            throw new InvalidDataException($"不支持的 SATL GUI 协议版本：{parsed.ProtocolVersion}");
        }
        return parsed;
    }

    private static LaunchInfo ResolveLaunch()
    {
        var overridePath = Environment.GetEnvironmentVariable("SATL_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return new LaunchInfo(
                overridePath,
                Path.GetDirectoryName(overridePath)!,
                [],
                new Dictionary<string, string>());
        }

        var sidecar = Path.Combine(AppContext.BaseDirectory, "satl.exe");
        if (File.Exists(sidecar))
        {
            return new LaunchInfo(
                sidecar,
                AppContext.BaseDirectory,
                [],
                new Dictionary<string, string>());
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "pyproject.toml")))
            {
                continue;
            }
            var python = Path.Combine(directory.FullName, ".venv", "Scripts", "python.exe");
            if (File.Exists(python))
            {
                return new LaunchInfo(
                    python,
                    directory.FullName,
                    ["-m", "satl"],
                    new Dictionary<string, string>
                    {
                        ["PYTHONUTF8"] = "1",
                        ["PYTHONPATH"] = Path.Combine(directory.FullName, "src"),
                    });
            }
        }

        throw new FileNotFoundException("未找到 satl.exe。请重新解压完整便携包。", sidecar);
    }

    private sealed record LaunchInfo(
        string FileName,
        string WorkingDirectory,
        IReadOnlyList<string> PrefixArguments,
        IReadOnlyDictionary<string, string> Environment);
}
