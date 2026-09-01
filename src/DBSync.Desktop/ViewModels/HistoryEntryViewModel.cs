namespace DBSync.Desktop.ViewModels;

public sealed record HistoryEntryViewModel(
    string Kind,
    string Title,
    string Path,
    string? ConnectionName,
    DateTimeOffset CreatedAt)
{
    public string DisplayText => $"{CreatedAt:MM-dd HH:mm} {Kind} {Title}";

    public string PathText => string.IsNullOrWhiteSpace(Path) ? string.Empty : Path;
}
