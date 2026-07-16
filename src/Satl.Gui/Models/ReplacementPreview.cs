using System.Text.Json;

namespace Satl_Gui.Models;

public sealed record AchievementTranslation(string Name, string Description)
{
    public static AchievementTranslation Empty { get; } = new(string.Empty, string.Empty);
}

public sealed record AchievementPreviewRow(
    int Index,
    string ApiName,
    IReadOnlyDictionary<string, AchievementTranslation> Translations)
{
    public AchievementTranslation TranslationFor(string language) =>
        Translations.TryGetValue(language, out var translation)
            ? translation
            : AchievementTranslation.Empty;
}

public sealed record ReplacementPreview(
    string AppId,
    string GameName,
    string VariantId,
    string Action,
    int AchievementCount,
    IReadOnlyList<string> Languages,
    IReadOnlyList<AchievementPreviewRow> Rows)
{
    public bool DeletesTarget => Action == "delete";

    public string DefaultLanguage =>
        Languages.FirstOrDefault(language => language.Equals("schinese", StringComparison.OrdinalIgnoreCase))
        ?? Languages.FirstOrDefault()
        ?? "schinese";

    public static ReplacementPreview FromPayload(JsonElement payload, string fallbackGameName)
    {
        var languages = new List<string>();
        if (payload.TryGetProperty("languages", out var languagePayloads)
            && languagePayloads.ValueKind == JsonValueKind.Array)
        {
            foreach (var language in languagePayloads.EnumerateArray())
            {
                var code = language.GetString();
                if (!string.IsNullOrWhiteSpace(code)
                    && !code.Equals("token", StringComparison.OrdinalIgnoreCase)
                    && !code.Equals("tokens", StringComparison.OrdinalIgnoreCase))
                {
                    languages.Add(code);
                }
            }
        }

        var rows = new List<AchievementPreviewRow>();
        if (payload.TryGetProperty("rows", out var rowPayloads)
            && rowPayloads.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowPayloads.EnumerateArray())
            {
                var translations = new Dictionary<string, AchievementTranslation>(
                    StringComparer.OrdinalIgnoreCase);
                if (row.TryGetProperty("translations", out var translationPayloads)
                    && translationPayloads.ValueKind == JsonValueKind.Object)
                {
                    foreach (var translation in translationPayloads.EnumerateObject())
                    {
                        if (translation.Name.Equals("token", StringComparison.OrdinalIgnoreCase)
                            || translation.Name.Equals("tokens", StringComparison.OrdinalIgnoreCase)
                            || translation.Value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }
                        translations[translation.Name] = new AchievementTranslation(
                            GetString(translation.Value, "name"),
                            GetString(translation.Value, "description"));
                    }
                }
                rows.Add(new AchievementPreviewRow(
                    GetInt(row, "index"),
                    GetString(row, "api_name"),
                    translations));
            }
        }
        return new ReplacementPreview(
            GetString(payload, "app_id"),
            GetString(payload, "game_name", fallbackGameName),
            GetString(payload, "variant_id", "default"),
            GetString(payload, "action", "replace"),
            GetInt(payload, "achievement_count", rows.Count),
            languages,
            rows);
    }

    private static string GetString(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int GetInt(JsonElement element, string name, int fallback = 0) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;
}
