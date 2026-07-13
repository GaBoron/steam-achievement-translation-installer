using System.Text.Json;
using System.Text.Json.Serialization;

namespace Satl_Gui.Models;

public sealed record SatlEvent(
    [property: JsonPropertyName("protocol_version")] int ProtocolVersion,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("event")] string Event,
    [property: JsonPropertyName("payload")] JsonElement Payload);

public sealed record CliRunResult(int ExitCode, IReadOnlyList<SatlEvent> Events, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;

    public string ErrorMessage => Events.LastOrDefault(item => item.Event is "error" or "item-failed")
        ?.Payload.TryGetProperty("message", out var message) == true
            ? message.GetString() ?? StandardError
            : StandardError;
}
