namespace DBSync.Desktop.Models;

public sealed record AppSettings
{
    public int RowCountWarningThreshold { get; init; } = 100_000;
}
