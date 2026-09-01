using DBSync.Core.Comparers;
using DBSync.Core.Models;
using DBSync.Tests.Helpers;
using Xunit;

namespace DBSync.Tests.Comparers;

public class SchemaComparerTests
{
    // ── 无差异 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_IdenticalTableSets_ReturnsEmptyDiff()
    {
        var tables = new[] { TableModelFactory.Simple("Users"), TableModelFactory.Simple("Orders") };

        var result = SchemaComparer.Compare(tables, tables);

        Assert.Empty(result.AddedTables);
        Assert.Empty(result.RemovedTables);
        Assert.Empty(result.ModifiedTables);
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void Compare_EmptyBothSides_ReturnsEmptyDiff()
    {
        var result = SchemaComparer.Compare([], []);

        Assert.False(result.HasChanges);
    }

    // ── 新增表 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_SourceHasExtraTable_ReportsAsAdded()
    {
        var baseline = new[] { TableModelFactory.Simple("Users") };
        var source = new[] { TableModelFactory.Simple("Users"), TableModelFactory.Simple("Orders") };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.AddedTables);
        Assert.Equal("Orders", result.AddedTables[0].Name);
        Assert.Empty(result.RemovedTables);
    }

    // ── 删除表 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_BaselineHasExtraTable_ReportsAsRemoved()
    {
        var baseline = new[] { TableModelFactory.Simple("Users"), TableModelFactory.Simple("Legacy") };
        var source = new[] { TableModelFactory.Simple("Users") };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.RemovedTables);
        Assert.Equal("Legacy", result.RemovedTables[0].Name);
        Assert.Empty(result.AddedTables);
    }

    // ── 列变更 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_SourceTableHasNewColumn_ReportsColumnAdded()
    {
        var baseline = new[] { TableModelFactory.WithColumns("Users", [TableModelFactory.IdColumn()]) };
        var source = new[]
        {
            TableModelFactory.WithColumns("Users",
            [
                TableModelFactory.IdColumn(),
                TableModelFactory.Col("Email", DbColumnType.Text)
            ])
        };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.ModifiedTables);
        var tableDiff = result.ModifiedTables[0];
        Assert.Single(tableDiff.ColumnDiffs);
        Assert.Equal(ColumnDiffType.Added, tableDiff.ColumnDiffs[0].DiffType);
        Assert.Equal("Email", tableDiff.ColumnDiffs[0].After!.Name);
    }

    [Fact]
    public void Compare_SourceTableMissingColumn_ReportsColumnRemoved()
    {
        var baseline = new[]
        {
            TableModelFactory.WithColumns("Users",
            [
                TableModelFactory.IdColumn(),
                TableModelFactory.Col("OldField", DbColumnType.Text)
            ])
        };
        var source = new[] { TableModelFactory.WithColumns("Users", [TableModelFactory.IdColumn()]) };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.ModifiedTables);
        var columnDiff = result.ModifiedTables[0].ColumnDiffs[0];
        Assert.Equal(ColumnDiffType.Removed, columnDiff.DiffType);
        Assert.Equal("OldField", columnDiff.Before!.Name);
    }

    [Fact]
    public void Compare_ColumnTypeChanged_ReportsColumnModified()
    {
        var baseline = new[]
        {
            TableModelFactory.WithColumns("Users",
            [
                TableModelFactory.IdColumn(),
                TableModelFactory.Col("Age", DbColumnType.Integer)
            ])
        };
        var source = new[]
        {
            TableModelFactory.WithColumns("Users",
            [
                TableModelFactory.IdColumn(),
                TableModelFactory.Col("Age", DbColumnType.Decimal)
            ])
        };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.ModifiedTables);
        var columnDiff = result.ModifiedTables[0].ColumnDiffs[0];
        Assert.Equal(ColumnDiffType.Modified, columnDiff.DiffType);
        Assert.Equal("Age", columnDiff.Before!.Name);
    }

    [Fact]
    public void Compare_ColumnNullabilityChanged_ReportsColumnModified()
    {
        var baseline = new[]
        {
            TableModelFactory.WithColumns("Users",
            [
                TableModelFactory.IdColumn(),
                TableModelFactory.Col("Name", DbColumnType.Text, isNullable: false)
            ])
        };
        var source = new[]
        {
            TableModelFactory.WithColumns("Users",
            [
                TableModelFactory.IdColumn(),
                TableModelFactory.Col("Name", DbColumnType.Text, isNullable: true)
            ])
        };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Equal(ColumnDiffType.Modified, result.ModifiedTables[0].ColumnDiffs[0].DiffType);
    }

    // ── 约束变更 ────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_PrimaryKeyColumnsChanged_ReportsTableModified()
    {
        var baseline = new[] { TableModelFactory.WithColumns("Users", [TableModelFactory.IdColumn()], pkColumns: ["Id"]) };
        var source = new[] { TableModelFactory.WithColumns("Users", [TableModelFactory.IdColumn()], pkColumns: ["Id", "TenantId"]) };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.ModifiedTables);
        Assert.True(result.ModifiedTables[0].PrimaryKeyChanged);
    }

    [Fact]
    public void Compare_IndexPrimaryKeyFlagChanged_ReportsIndexModified()
    {
        var baseline = new[]
        {
            TableModelFactory.WithColumns("Users",
                [TableModelFactory.IdColumn()],
                indexes: [TableModelFactory.Index("PK_Users", ["Id"], isUnique: true)])
        };
        var source = new[]
        {
            TableModelFactory.WithColumns("Users",
                [TableModelFactory.IdColumn()],
                indexes: [TableModelFactory.Index("PK_Users", ["Id"], isUnique: true, isPrimaryKey: true)])
        };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.ModifiedTables);
        Assert.Single(result.ModifiedTables[0].IndexDiffs);
        Assert.Equal(IndexDiffType.Modified, result.ModifiedTables[0].IndexDiffs[0].DiffType);
    }

    // ── 表名大小写不敏感 ────────────────────────────────────────────────────

    [Fact]
    public void Compare_TableNameCaseInsensitive_TreatedAsSameTable()
    {
        var baseline = new[] { TableModelFactory.Simple("users") };
        var source = new[] { TableModelFactory.Simple("Users") };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Empty(result.AddedTables);
        Assert.Empty(result.RemovedTables);
    }

    [Fact]
    public void Compare_SameTableNameDifferentSchema_TreatedAsDifferentTable()
    {
        var baseline = new[] { TableModelFactory.Simple("Users", schema: "dbo") };
        var source = new[] { TableModelFactory.Simple("Users", schema: "audit") };

        var result = SchemaComparer.Compare(baseline, source);

        Assert.Single(result.AddedTables);
        Assert.Single(result.RemovedTables);
        Assert.Equal("audit.Users", result.AddedTables[0].FullName);
        Assert.Equal("dbo.Users", result.RemovedTables[0].FullName);
    }
}
