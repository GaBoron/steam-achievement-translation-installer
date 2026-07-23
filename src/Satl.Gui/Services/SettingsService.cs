using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Satl_Gui.Models;
using Satl_Gui.Serialization;

namespace Satl_Gui.Services;

public sealed class SettingsService
{
    private static readonly byte[] PasswordEntropy =
        Encoding.UTF8.GetBytes("SATLInstaller.ProxyPassword.v1");
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
            settings.Network = LoadNetworkSettings(settings.Network);
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
                var network = NetworkSettingsValidator.Normalize(settings.Network);
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
                    Network = new NetworkSettings
                    {
                        DnsMode = network.DnsMode,
                        DnsServers = network.DnsServers,
                        ProxyMode = network.ProxyMode,
                        ProxyAddress = network.ProxyAddress,
                        ProxyUsername = network.ProxyUsername,
                        ProtectedProxyPassword = ProtectPassword(network.ProxyPassword),
                    },
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

    private static NetworkSettings LoadNetworkSettings(NetworkSettings? stored)
    {
        stored ??= new NetworkSettings();
        try
        {
            stored.ProxyPassword = UnprotectPassword(stored.ProtectedProxyPassword);
            return NetworkSettingsValidator.Normalize(stored);
        }
        catch (ArgumentException)
        {
            return new NetworkSettings();
        }
    }

    private static string ProtectPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password),
            PasswordEntropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string UnprotectPassword(string protectedPassword)
    {
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return string.Empty;
        }
        try
        {
            var clearBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedPassword),
                PasswordEntropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch (Exception exception) when (
            exception is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }
}
