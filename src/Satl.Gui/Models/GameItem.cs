using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Satl_Gui.Models;

public sealed class SchemaVariantOption
{
    public required string VariantId { get; init; }
    public bool Primary { get; init; }
    public string Note { get; init; } = string.Empty;
    public string DisplayName => string.IsNullOrWhiteSpace(Note) ? VariantId : $"{VariantId} · {Note}";
    public override string ToString() => DisplayName;
}

public sealed class GameItem : ObservableObject
{
    private bool _isSelected;
    private SchemaVariantOption? _selectedVariant;
    private string _installedState = "unmanaged";
    private string _installedVariantId = string.Empty;

    public required string AppId { get; init; }
    public required string GameName { get; init; }
    public string CatalogStatus { get; init; } = "unknown";
    public string DiscoveryText { get; init; } = string.Empty;
    public ObservableCollection<SchemaVariantOption> Variants { get; } = [];

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public SchemaVariantOption? SelectedVariant
    {
        get => _selectedVariant;
        set => SetProperty(ref _selectedVariant, value);
    }

    public string InstalledState
    {
        get => _installedState;
        set
        {
            if (SetProperty(ref _installedState, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(IsModified));
                OnPropertyChanged(nameof(InstalledVersionText));
            }
        }
    }

    public string InstalledVariantId
    {
        get => _installedVariantId;
        set
        {
            if (SetProperty(ref _installedVariantId, value))
            {
                OnPropertyChanged(nameof(InstalledVersionText));
            }
        }
    }

    public string StateText => InstalledState switch
    {
        "installed" => "已安装",
        "modified" => "已被修改",
        "missing" => "文件缺失",
        "restored" => "已恢复",
        "unreadable" => "无法读取",
        _ => "未管理",
    };

    public bool IsModified => InstalledState == "modified";
    public string InstalledVersionText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstalledVariantId) || InstalledState is "unmanaged" or "restored")
            {
                return "已安装版本：无";
            }
            var variant = Variants.FirstOrDefault(item => item.VariantId == InstalledVariantId);
            return $"已安装版本：{variant?.DisplayName ?? InstalledVariantId}";
        }
    }
    public bool IsCurrent => CatalogStatus == "current";
    public string CatalogText => IsCurrent ? "当前版本" : "可能已过期";
    public string Subtitle => $"App ID {AppId}" + (string.IsNullOrWhiteSpace(DiscoveryText) ? string.Empty : $" · {DiscoveryText}");

    public static GameItem FromPayload(JsonElement payload)
    {
        var item = new GameItem
        {
            AppId = GetString(payload, "app_id", "0"),
            GameName = GetString(payload, "game_name", "未知游戏"),
            CatalogStatus = GetString(payload, "catalog_status", "unknown"),
            DiscoveryText = payload.TryGetProperty("discovery", out var discovery)
                ? string.Join(" / ", discovery.EnumerateArray().Select(source => source.GetString()).Where(source => !string.IsNullOrWhiteSpace(source)))
                : string.Empty,
            InstalledState = GetString(payload, "installed_state", "unmanaged"),
            InstalledVariantId = GetString(payload, "installed_variant_id", string.Empty),
        };

        if (payload.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Array)
        {
            foreach (var variant in variants.EnumerateArray())
            {
                item.Variants.Add(new SchemaVariantOption
                {
                    VariantId = GetString(variant, "variant_id", "default"),
                    Primary = variant.TryGetProperty("primary", out var primary) && primary.GetBoolean(),
                    Note = GetString(variant, "note_zh", string.Empty),
                });
            }
        }

        item.SelectedVariant = item.Variants.FirstOrDefault(variant => variant.VariantId == item.InstalledVariantId)
            ?? item.Variants.FirstOrDefault(variant => variant.Primary)
            ?? item.Variants.FirstOrDefault();
        return item;
    }

    private static string GetString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
}
