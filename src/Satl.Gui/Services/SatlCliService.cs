using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Satl_Gui.Models;

namespace Satl_Gui.Services;

public sealed class SatlCliService
{
    public async Task<CliRunResult> RunAsync(
        IEnumerable<string> arguments,
        Action<SatlEvent>? onEvent = null,
        Action<string>? onDiagnostic = null)
    {
        var argumentList = arguments.ToList();
        onDiagnostic?.Invoke($"步骤 1：解析 CLI 启动目标。请求参数={FormatArguments(argumentList)}");
        var launch = ResolveLaunch();
        onDiagnostic?.Invoke(
            $"步骤 2：启动目标已解析。可执行文件={launch.FileName}；工作目录={launch.WorkingDirectory}；" +
            $"前置参数={FormatArguments(launch.PrefixArguments)}；附加环境变量={string.Join(",", launch.Environment.Keys)}");
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
        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }
        foreach (var environment in launch.Environment)
        {
            startInfo.Environment[environment.Key] = environment.Value;
        }
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        onDiagnostic?.Invoke(
            $"步骤 3：进程启动信息已组装。完整参数={FormatArguments(startInfo.ArgumentList)}；" +
            "标准输出/标准错误=UTF-8 重定向；隐藏控制台窗口=True。");

        using var process = new Process { StartInfo = startInfo };
        onDiagnostic?.Invoke("步骤 4：正在启动 satl 命令行核心。");
        if (!process.Start())
        {
            throw new InvalidOperationException("无法启动 satl 命令行核心。");
        }
        onDiagnostic?.Invoke($"步骤 5：satl 进程已启动。PID={process.Id}。");

        var stderrTask = process.StandardError.ReadToEndAsync();
        var events = new List<SatlEvent>();
        var outputLine = 0;
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            outputLine++;
            onDiagnostic?.Invoke($"步骤 6.{outputLine}：收到 stdout 原始行：{line}");
            if (string.IsNullOrWhiteSpace(line))
            {
                onDiagnostic?.Invoke($"步骤 6.{outputLine}：该行为空，已跳过。");
                continue;
            }
            var parsed = ParseEvent(line);
            events.Add(parsed);
            onDiagnostic?.Invoke(
                $"步骤 6.{outputLine}：事件解析完成。operation={parsed.Operation}；event={parsed.Event}；payload={parsed.Payload.GetRawText()}");
            onEvent?.Invoke(parsed);
        }

        onDiagnostic?.Invoke("步骤 7：stdout 已关闭，等待 CLI 进程退出。");
        await process.WaitForExitAsync();
        var standardError = (await stderrTask).Trim();
        onDiagnostic?.Invoke(
            $"步骤 8：CLI 进程已退出。PID={process.Id}；退出码={process.ExitCode}；事件数={events.Count}；" +
            $"stderr={(string.IsNullOrEmpty(standardError) ? "<空>" : standardError)}");
        return new CliRunResult(process.ExitCode, events, standardError);
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
                        ["PYTHONPATH"] = Path.Combine(directory.FullName, "src"),
                    });
            }
        }

        throw new FileNotFoundException("未找到 satl.exe。请重新解压完整便携包。", sidecar);
    }

    private static string FormatArguments(IEnumerable<string> arguments) =>
        string.Join(" ", arguments.Select(argument => JsonSerializer.Serialize(argument)));

    private sealed record LaunchInfo(
        string FileName,
        string WorkingDirectory,
        IReadOnlyList<string> PrefixArguments,
        IReadOnlyDictionary<string, string> Environment);
}
