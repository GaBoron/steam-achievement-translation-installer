using System.Text.Json;
using Satl_Gui.Models;
using Satl_Gui.Services;
using Xunit;

namespace Satl_Gui.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void ParseEventReadsVersionedPayload()
    {
        var parsed = SatlCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"completed\",\"payload\":{\"count\":4}}"
        );

        Assert.Equal(1, parsed.ProtocolVersion);
        Assert.Equal("scan", parsed.Operation);
        Assert.Equal(4, parsed.Payload.GetProperty("count").GetInt32());
    }

    [Fact]
    public void ParseEventRejectsUnknownProtocol()
    {
        Assert.Throws<InvalidDataException>(() => SatlCliService.ParseEvent(
            "{\"protocol_version\":2,\"operation\":\"scan\",\"event\":\"completed\",\"payload\":{}}"
        ));
    }

    [Fact]
    public void ParseEventPreservesCjkText()
    {
        var parsed = SatlCliService.ParseEvent(
            "{\"protocol_version\":1,\"operation\":\"scan\",\"event\":\"item-succeeded\",\"payload\":{\"game_name\":\"以撒的结合：重生\",\"note_zh\":\"原版\"}}"
        );

        Assert.Equal("以撒的结合：重生", parsed.Payload.GetProperty("game_name").GetString());
        Assert.Equal("原版", parsed.Payload.GetProperty("note_zh").GetString());
    }

    [Fact]
    public void GameItemMapsVariantsAndState()
    {
        using var document = JsonDocument.Parse(
            "{\"app_id\":\"123\",\"game_name\":\"Game\",\"catalog_status\":\"current\",\"installed_state\":\"modified\",\"discovery\":[\"installed\"],\"variants\":[{\"variant_id\":\"default\",\"primary\":true,\"note_zh\":\"原版\"}]}"
        );

        var item = GameItem.FromPayload(document.RootElement);

        Assert.Equal("123", item.AppId);
        Assert.True(item.IsModified);
        Assert.Equal("default", item.SelectedVariant?.VariantId);
        Assert.Contains("原版", item.SelectedVariant?.DisplayName);
    }

    [Fact]
    public async Task SettingsRoundTripUsesRequestedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"satl-gui-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "settings.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new GuiSettings { Offline = true, Theme = "dark", SteamDirectory = "C:\\Steam" });

            var loaded = await service.LoadAsync();

            Assert.True(loaded.Offline);
            Assert.Equal("dark", loaded.Theme);
            Assert.Equal("C:\\Steam", loaded.SteamDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
