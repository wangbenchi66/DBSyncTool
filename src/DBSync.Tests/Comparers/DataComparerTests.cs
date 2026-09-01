using Bogus;
using DBSync.Core.Comparers;
using DBSync.Core.Models;

namespace DBSync.Tests.Comparers;

public class DataComparerTests
{
    private static readonly Faker Faker = new("zh_CN");

    [Fact]
    public void Compare_IdenticalRows_ReturnsEmptyDiff()
    {
        var rows = new[] { Row("1"), Row("2") };

        var result = DataComparer.Compare(rows, rows);

        Assert.Empty(result.RowsToInsert);
        Assert.Empty(result.DeletedRows);
        Assert.Empty(result.ChangedRows);
        Assert.False(result.Skipped);
    }

    [Fact]
    public void Compare_SourceOnlyRows_ReportsRowsToInsert()
    {
        var source = new[] { Row("1"), Row("2") };

        var result = DataComparer.Compare([], source);

        Assert.Equal(["1", "2"], result.RowsToInsert.Select(Id));
        Assert.Empty(result.DeletedRows);
        Assert.Empty(result.ChangedRows);
    }

    [Fact]
    public void Compare_PartialUpdatedRows_ReportsChangedRows()
    {
        var baseline = new[] { Row("1", "aaa"), Row("2", "bbb") };
        var source = new[] { Row("1", "aaa"), Row("2", "ccc") };

        var result = DataComparer.Compare(baseline, source);

        var row = Assert.Single(result.ChangedRows);
        Assert.Equal("2", Id(row));
        Assert.Equal("ccc", row.Hash);
    }

    [Fact]
    public void Compare_MixedRows_ReportsAllDiffTypes()
    {
        var baseline = new[] { Row("1", "aaa"), Row("2", "bbb"), Row("3", "ccc") };
        var source = new[] { Row("1", "aaa"), Row("2", "changed"), Row("4", "ddd") };

        var result = DataComparer.Compare(baseline, source);

        Assert.Equal(["4"], result.RowsToInsert.Select(Id));
        Assert.Equal(["3"], result.DeletedRows.Select(Id));
        Assert.Equal(["2"], result.ChangedRows.Select(Id));
    }

    [Fact]
    public void Compare_EmptyBothSides_ReturnsEmptyDiff()
    {
        var result = DataComparer.Compare([], []);

        Assert.Empty(result.RowsToInsert);
        Assert.Empty(result.DeletedRows);
        Assert.Empty(result.ChangedRows);
    }

    [Fact]
    public void Compare_NoPrimaryKey_ReturnsSkippedDiff()
    {
        var result = DataComparer.Compare([Row("1")], [Row("2")], noPrimaryKey: true);

        Assert.True(result.Skipped);
        Assert.Empty(result.RowsToInsert);
        Assert.Empty(result.DeletedRows);
        Assert.Empty(result.ChangedRows);
    }

    private static RowHash Row(string id, string? hash = null) => new()
    {
        PrimaryKeyValues = new Dictionary<string, string?> { ["Id"] = id },
        Hash = hash ?? Faker.Random.Hash()
    };

    private static string? Id(RowHash row) => row.PrimaryKeyValues["Id"];
}
