namespace DBSync.Desktop.Models;

public sealed record RecentHistoryItem
{
    public required string Kind { get; init; }

    public required string Title { get; init; }

    public required string Path { get; init; }

    public string? ConnectionName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
