using System.Text.Json.Serialization;
using Satl_Gui.Models;
using Satl_Gui.Services;

namespace Satl_Gui.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GuiSettings))]
[JsonSerializable(typeof(SatlEvent))]
[JsonSerializable(typeof(WindowPlacement))]
[JsonSerializable(typeof(string))]
internal sealed partial class SatlJsonSerializerContext : JsonSerializerContext;
