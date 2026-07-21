namespace Satl_Gui.Models;

public static class CatalogStatusPresentation
{
    public static string Label(string status) => Normalize(status) switch
    {
        "current" => "可用",
        "possibly-outdated" or "outdated" or "stale" => "可能过期",
        "possibly-ineffective" or "possibly-not-working" or "may-not-work" => "可能不生效",
        "ineffective" or "not-working" or "broken" or "invalid" => "已失效",
        "pending" or "pending-review" or "under-review" or "reviewing" => "审核中",
        "unavailable" or "disabled" => "不可用",
        "deprecated" or "retired" => "已停用",
        "unknown" or "unlisted" or "missing" => "未收录",
        _ => "未知状态",
    };

    public static string Warning(string status) => Normalize(status) switch
    {
        "possibly-outdated" or "outdated" or "stale" =>
            "该翻译可能已经过期，安装前请确认它仍适用于当前游戏版本。",
        "possibly-ineffective" or "possibly-not-working" or "may-not-work" =>
            "该翻译可能无法在当前游戏版本中生效，建议等待确认或更新。",
        "ineffective" or "not-working" or "broken" or "invalid" =>
            "该翻译已标记为失效，不建议安装。",
        "pending" or "pending-review" or "under-review" or "reviewing" =>
            "该翻译仍在审核中，当前不建议安装。",
        "unavailable" or "disabled" =>
            "该翻译当前不可用。",
        "deprecated" or "retired" =>
            "该翻译已经停用，不建议继续安装。",
        "unknown" or "unlisted" or "missing" =>
            "云端索引未收录，当前没有可安装的社区翻译。",
        _ => "云端索引返回了未识别的状态，当前不建议安装。",
    };

    private static string Normalize(string status) =>
        status.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
}
