using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class UpdatePackageCleanupServiceTests
{
    [Fact]
    public void CleanupDeletesCurrentAndEarlierPackagesButPreservesFutureAndUnrelatedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-update-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var earlier = Path.Combine(root, "SATLInstaller-Setup-v0.6.0.exe");
        var current = Path.Combine(root, "SATLInstaller-Setup-v0.7.1.exe");
        var partial = Path.Combine(root, "SATLInstaller-Setup-v0.7.0.exe.part");
        var future = Path.Combine(root, "SATLInstaller-Setup-v0.8.0.exe");
        var unrelated = Path.Combine(root, "notes.txt");
        foreach (var path in new[] { earlier, current, partial, future, unrelated })
        {
            File.WriteAllText(path, "fixture");
        }

        try
        {
            var result = new UpdatePackageCleanupService(new Version(0, 7, 1), root).Cleanup();

            Assert.Equal(3, result.DeletedFiles.Count);
            Assert.Empty(result.Failures);
            Assert.False(File.Exists(earlier));
            Assert.False(File.Exists(current));
            Assert.False(File.Exists(partial));
            Assert.True(File.Exists(future));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CleanupIsSafeWhenUpdateDirectoryDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-update-cleanup-{Guid.NewGuid():N}");

        var result = new UpdatePackageCleanupService(new Version(0, 7, 1), root).Cleanup();

        Assert.Empty(result.DeletedFiles);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task CleanupWithRetryReturnsImmediatelyWhenNoRetryIsNeeded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-update-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var current = Path.Combine(root, "SATLInstaller-Setup-v0.7.1.exe");
        File.WriteAllText(current, "fixture");

        try
        {
            var result = await new UpdatePackageCleanupService(
                new Version(0, 7, 1),
                root).CleanupWithRetryAsync(retryDelay: TimeSpan.Zero);

            Assert.Single(result.DeletedFiles);
            Assert.Empty(result.Failures);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
