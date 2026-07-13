using System.Diagnostics;
using System.Text;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "_satl_runtime");
var pythonExecutable = Path.Combine(runtimeDirectory, "python.exe");
var applicationArchive = Path.Combine(runtimeDirectory, "satl.pyz");

if (!File.Exists(pythonExecutable) || !File.Exists(applicationArchive))
{
    Console.Error.WriteLine("SATL 运行文件不完整，请重新安装或完整解压便携包。");
    return 2;
}

try
{
    var startInfo = new ProcessStartInfo
    {
        FileName = pythonExecutable,
        WorkingDirectory = Environment.CurrentDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    startInfo.ArgumentList.Add(applicationArchive);
    foreach (var argument in args)
    {
        startInfo.ArgumentList.Add(argument);
    }
    startInfo.Environment["PYTHONUTF8"] = "1";
    startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine("无法启动 SATL 命令行核心。");
        return 2;
    }

    var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
    var stderrTask = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
    await process.WaitForExitAsync();
    await Task.WhenAll(stdoutTask, stderrTask);
    return process.ExitCode;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"无法启动 SATL 命令行核心：{exception.Message}");
    return 2;
}
