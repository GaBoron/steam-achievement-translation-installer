namespace Satl_Gui.Services;

public sealed record UpdatePackageCleanupResult(
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> Failures);

public sealed class UpdatePackageCleanupService
{
    private const string InstallerPrefix = "SATLInstaller-Setup-v";
    private const string InstallerSuffix = ".exe";
    private const string PartialSuffix = ".part";

    private readonly string _updateDirectory;
    private readonly Version _currentVersion;

    public UpdatePackageCleanupService(
        Version? currentVersion = null,
        string? updateDirectory = null)
    {
        _currentVersion = Normalize(currentVersion ?? UpdateService.CurrentVersion);
        _updateDirectory = updateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteamAchievementTranslationInstaller",
            "updates");
    }

    public UpdatePackageCleanupResult Cleanup()
    {
        if (!Directory.Exists(_updateDirectory))
        {
            return new UpdatePackageCleanupResult([], []);
        }

        var deletedFiles = new List<string>();
        var failures = new List<string>();
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(_updateDirectory).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new UpdatePackageCleanupResult(
                [],
                [$"无法读取更新目录 {_updateDirectory}：{exception.Message}"]);
        }

        foreach (var path in candidates)
        {
            if (!TryGetPackageVersion(Path.GetFileName(path), out var packageVersion)
                || packageVersion > _currentVersion)
            {
                continue;
            }

            try
            {
                File.Delete(path);
                deletedFiles.Add(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add($"无法删除 {path}：{exception.Message}");
            }
        }

        return new UpdatePackageCleanupResult(deletedFiles, failures);
    }

    public async Task<UpdatePackageCleanupResult> CleanupWithRetryAsync(
        int maximumAttempts = 4,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        var deletedFiles = new List<string>();
        UpdatePackageCleanupResult result;
        for (var attempt = 1; ; attempt++)
        {
            result = Cleanup();
            deletedFiles.AddRange(result.DeletedFiles);
            if (result.Failures.Count == 0 || attempt >= maximumAttempts)
            {
                return new UpdatePackageCleanupResult(deletedFiles, result.Failures);
            }
            await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static bool TryGetPackageVersion(string fileName, out Version version)
    {
        version = new Version(0, 0, 0);
        var normalized = fileName.EndsWith(PartialSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^PartialSuffix.Length]
            : fileName;
        if (!normalized.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(InstallerSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var versionText = normalized[
            InstallerPrefix.Length..^InstallerSuffix.Length];
        if (versionText.Split('.').Length != 3
            || !Version.TryParse(versionText, out var parsed))
        {
            return false;
        }

        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version value) => new(
        Math.Max(value.Major, 0),
        Math.Max(value.Minor, 0),
        Math.Max(value.Build, 0));
}
