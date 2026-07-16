using System.Text.Json;

namespace Satl_Gui.Models;

public sealed record AchievementPreviewRow(
    int Index,
    string ApiName,
    string SimplifiedChineseName,
    string SimplifiedChineseDescription,
    string EnglishName,
    string EnglishDescription,
    string OtherLanguages);

public sealed record ReplacementPreview(
    string AppId,
    string GameName,
    string VariantId,
    string Action,
    int AchievementCount,
    IReadOnlyList<AchievementPreviewRow> Rows)
{
    public bool DeletesTarget => Action == "delete";

    public static ReplacementPreview FromPayload(JsonElement payload, string fallbackGameName)
    {
        var rows = new List<AchievementPreviewRow>();
        if (payload.TryGetProperty("rows", out var rowPayloads)
            && rowPayloads.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowPayloads.EnumerateArray())
            {
                rows.Add(new AchievementPreviewRow(
                    GetInt(row, "index"),
                    GetString(row, "api_name"),
                    GetString(row, "schinese_name"),
                    GetString(row, "schinese_description"),
                    GetString(row, "english_name"),
                    GetString(row, "english_description"),
                    GetString(row, "other_languages")));
            }
        }
        return new ReplacementPreview(
            GetString(payload, "app_id"),
            GetString(payload, "game_name", fallbackGameName),
            GetString(payload, "variant_id", "default"),
            GetString(payload, "action", "replace"),
            GetInt(payload, "achievement_count", rows.Count),
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
