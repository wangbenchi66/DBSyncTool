using DBSync.Core.Data;
using DBSync.Core.Models;
using DBSync.Tests.Helpers;

namespace DBSync.Tests.Data;

public class SqlServerDataFingerprinterTests
{
    [Fact]
    public void SanitizeWhereClause_NullOrBlank_ReturnsNull()
    {
        Assert.Null(SqlServerDataFingerprinter.SanitizeWhereClause(null));
        Assert.Null(SqlServerDataFingerprinter.SanitizeWhereClause("   "));
    }

    [Fact]
    public void SanitizeWhereClause_TrailingSemicolon_RemovesSemicolon()
    {
        var result = SqlServerDataFingerprinter.SanitizeWhereClause("CreatedAt >= '2026-01-01';");

        Assert.Equal("CreatedAt >= '2026-01-01'", result);
    }

    [Fact]
    public void SanitizeWhereClause_InternalSemicolon_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SqlServerDataFingerprinter.SanitizeWhereClause("Id > 1; DROP TABLE Users"));

        Assert.Equal("whereClause", ex.ParamName);
    }

    [Fact]
    public void BuildFingerprintSql_NoPrimaryKey_ReturnsEmptyString()
    {
        var table = TableModelFactory.NoPrimaryKey("Logs");

        var sql = SqlServerDataFingerprinter.BuildFingerprintSql(table);

        Assert.Empty(sql);
    }

    [Fact]
    public void BuildFingerprintSql_TableWithTypedColumns_GeneratesHashQuery()
    {
        var table = TableModelFactory.WithColumns(
            "Orders",
            [
                TableModelFactory.IdColumn(),
                TableModelFactory.Col("Payload", DbColumnType.Binary),
                TableModelFactory.Col("CreatedAt", DbColumnType.DateTime) with { DbTypeName = "datetime2" },
                TableModelFactory.Col("Amount", DbColumnType.Float),
                TableModelFactory.Col("IsPaid", DbColumnType.Boolean)
            ]);

        var sql = SqlServerDataFingerprinter.BuildFingerprintSql(table, "Id > 10;");

        Assert.Contains("SELECT [Id], CONVERT(VARCHAR(32), HASHBYTES('MD5'", sql);
        Assert.Contains("COALESCE(CONVERT(VARCHAR(MAX), [Payload], 2), N'NULL')", sql);
        Assert.Contains("COALESCE(CONVERT(VARCHAR(23), [CreatedAt], 121), N'NULL')", sql);
        Assert.Contains("COALESCE(LTRIM(STR([Amount], 25, 15)), N'NULL')", sql);
        Assert.Contains("COALESCE(CONVERT(CHAR(1), [IsPaid]), N'NULL')", sql);
        Assert.Contains("FROM [dbo].[Orders]", sql);
        Assert.Contains("WHERE Id > 10", sql);
        Assert.Contains("ORDER BY [Id]", sql);
    }
}
