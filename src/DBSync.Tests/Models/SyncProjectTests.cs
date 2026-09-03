using System.Text.Json;
using DBSync.Core.Models;

namespace DBSync.Tests.Models;

/// <summary>
/// SyncProject 序列化/反序列化单元测试
///</summary>
public class SyncProjectTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void SerializeDeserialize_RoundTrip()
    {
        var project = new SyncProject
        {
            Name = "测试项目",
            SourceConnectionName = "生产主库",
            TargetConnectionName = "预发环境",
            SnapshotPath = @"E:\snapshot.dbsync",
            UseTransaction = true,
            ExportDirectory = @"E:\exports",
            Filters = new FilterOptions
            {
                IncludePatterns = ["journal\\..*"],
                ExcludePatterns = [".*_migration.*"],
                IgnoreTableComments = true,
                IgnoreIndexNames = false
            }
        };

        var json = JsonSerializer.Serialize(project, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SyncProject>(json, JsonOptions)!;

        Assert.Equal(project.Name, deserialized.Name);
        Assert.Equal(project.SourceConnectionName, deserialized.SourceConnectionName);
        Assert.Equal(project.TargetConnectionName, deserialized.TargetConnectionName);
        Assert.Equal(project.SnapshotPath, deserialized.SnapshotPath);
        Assert.Equal(project.UseTransaction, deserialized.UseTransaction);
        Assert.Equal(project.ExportDirectory, deserialized.ExportDirectory);
        Assert.Equal(project.Filters.IncludePatterns, deserialized.Filters.IncludePatterns);
        Assert.Equal(project.Filters.ExcludePatterns, deserialized.Filters.ExcludePatterns);
        Assert.True(deserialized.Filters.IgnoreTableComments);
        Assert.False(deserialized.Filters.IgnoreIndexNames);
    }

    [Fact]
    public void Defaults_AreReasonable()
    {
        var project = new SyncProject();

        Assert.Equal("", project.Name);
        Assert.Null(project.SourceConnectionName);
        Assert.True(project.UseTransaction);
        Assert.Empty(project.Filters.IncludePatterns);
        Assert.Empty(project.Filters.ExcludePatterns);
        Assert.False(project.Filters.IgnoreTableComments);
    }
}
