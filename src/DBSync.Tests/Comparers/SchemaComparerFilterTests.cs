using DBSync.Core.Comparers;
using DBSync.Core.Models;
using DBSync.Tests.Helpers;

namespace DBSync.Tests.Comparers;

/// <summary>
/// SchemaComparer 配合 FilterOptions 的单元测试
///</summary>
public class SchemaComparerFilterTests
{
    [Fact]
    public void Compare_IgnoreTableComments_CommentDiffNotReported()
    {
        var baseline = new[] { TableModelFactory.Simple("Users") with { Comment = "旧注释" } };
        var source = new[] { TableModelFactory.Simple("Users") with { Comment = "新注释" } };
        var filter = new FilterOptions { IgnoreTableComments = true };

        var result = SchemaComparer.Compare(baseline, source, filter);

        Assert.Empty(result.ModifiedTables);
    }

    [Fact]
    public void Compare_WithoutIgnoreComments_CommentDiffReported()
    {
        var baseline = new[] { TableModelFactory.Simple("Users") with { Comment = "旧注释" } };
        var source = new[] { TableModelFactory.Simple("Users") with { Comment = "新注释" } };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.ModifiedTables);
        Assert.True(result.ModifiedTables[0].CommentChanged);
    }

    [Fact]
    public void Compare_IgnoreIndexNames_IndexDiffsNotReported()
    {
        var baseline = new[]
        {
            TableModelFactory.WithColumns("Users", [TableModelFactory.IdColumn()],
                indexes: [TableModelFactory.Index("IX_Old", ["Id"], isUnique: true)])
        };
        var source = new[]
        {
            TableModelFactory.WithColumns("Users", [TableModelFactory.IdColumn()],
                indexes: [TableModelFactory.Index("IX_New", ["Id"], isUnique: true)])
        };
        var filter = new FilterOptions { IgnoreIndexNames = true };

        var result = SchemaComparer.Compare(baseline, source, filter);

        Assert.Empty(result.ModifiedTables);
    }

    [Fact]
    public void Compare_WithoutIgnoreIndexNames_IndexDiffReported()
    {
        var baseline = new[]
        {
            TableModelFactory.WithColumns("Users", [TableModelFactory.IdColumn()],
                indexes: [TableModelFactory.Index("IX_Old", ["Id"], isUnique: true)])
        };
        var source = new[]
        {
            TableModelFactory.WithColumns("Users", [TableModelFactory.IdColumn()],
                indexes: [TableModelFactory.Index("IX_New", ["Id"], isUnique: true)])
        };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.ModifiedTables);
        Assert.Equal(2, result.ModifiedTables[0].IndexDiffs.Count);
    }

    [Fact]
    public void Compare_IgnoreColumnComments_ColumnCommentDiffNotReported()
    {
        var baseline = new[]
        {
            TableModelFactory.WithColumns("Users",
                [TableModelFactory.IdColumn(), TableModelFactory.Col("Name", DbColumnType.Text) with { Comment = "旧列注释" }])
        };
        var source = new[]
        {
            TableModelFactory.WithColumns("Users",
                [TableModelFactory.IdColumn(), TableModelFactory.Col("Name", DbColumnType.Text) with { Comment = "新列注释" }])
        };
        var filter = new FilterOptions { IgnoreTableComments = true };

        var result = SchemaComparer.Compare(baseline, source, filter);

        Assert.Empty(result.ModifiedTables);
    }

    [Fact]
    public void Compare_NullFilter_BehavesLikeNoFilter()
    {
        var baseline = new[] { TableModelFactory.Simple("Users") };
        var source = new[] { TableModelFactory.Simple("Users"), TableModelFactory.Simple("Orders") };

        var result = SchemaComparer.Compare(baseline, source, null);

        Assert.Single(result.AddedTables);
    }
}
