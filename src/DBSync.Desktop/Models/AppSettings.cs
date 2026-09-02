namespace DBSync.Desktop.Models;

public sealed record AppSettings
{
    public int RowCountWarningThreshold { get; init; } = 100_000;

    public string? LastConnectionName { get; init; }

    public string? LastExportPath { get; init; }

    public string? LastSnapshotPath { get; init; }

    public List<RecentHistoryItem> RecentHistoryItems { get; init; } = [];

    /// <summary>
    /// 上次使用的导航页面标识
    ///</summary>
    public string? LastPageName { get; init; }
}
