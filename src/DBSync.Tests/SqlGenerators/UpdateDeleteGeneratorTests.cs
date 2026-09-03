using DBSync.Core.Models;
using DBSync.Core.SqlGenerators;
using DBSync.Tests.Helpers;

namespace DBSync.Tests.SqlGenerators;

/// <summary>
/// UPDATE 和 DELETE SQL 生成器单元测试（覆盖 4 种方言）
///</summary>
public class UpdateDeleteGeneratorTests
{
    private static TableModel TestTable => TableModelFactory.WithColumns(
        "Users",
        [
            TableModelFactory.IdColumn(),
            TableModelFactory.Col("Name", DbColumnType.Text),
            TableModelFactory.Col("Age", DbColumnType.Integer)
        ]);

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> UpdateRows =>
    [
        new Dictionary<string, string?> { ["Id"] = "1", ["Name"] = "张三", ["Age"] = "30" },
        new Dictionary<string, string?> { ["Id"] = "2", ["Name"] = "李四", ["Age"] = "25" }
    ];

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> DeleteRows =>
    [
        new Dictionary<string, string?> { ["Id"] = "3" },
        new Dictionary<string, string?> { ["Id"] = "4" }
    ];

    // ── SQL Server ──

    [Fact]
    public void SqlServer_GenerateUpdate_CorrectSyntax()
    {
        var gen = new SqlServerSqlGenerator();
        var sql = gen.GenerateUpdateStatements(TestTable, UpdateRows);

        Assert.Equal(2, sql.Count);
        Assert.Contains("UPDATE [dbo].[Users] SET", sql[0]);
        Assert.Contains("[Name] =", sql[0]);
        Assert.Contains("WHERE [Id] =", sql[0]);
    }

    [Fact]
    public void SqlServer_GenerateDelete_CorrectSyntax()
    {
        var gen = new SqlServerSqlGenerator();
        var sql = gen.GenerateDeleteStatements(TestTable, DeleteRows);

        Assert.Equal(2, sql.Count);
        Assert.Contains("DELETE FROM [dbo].[Users] WHERE [Id] =", sql[0]);
    }

    [Fact]
    public void SqlServer_Update_ExcludesPrimaryKeyFromSetClause()
    {
        var gen = new SqlServerSqlGenerator();
        var sql = gen.GenerateUpdateStatements(TestTable, UpdateRows);

        Assert.DoesNotContain("SET [Id]", sql[0]);
    }

    // ── MySQL ──

    [Fact]
    public void MySql_GenerateUpdate_CorrectSyntax()
    {
        var gen = new MySqlSqlGenerator();
        var sql = gen.GenerateUpdateStatements(TestTable, UpdateRows);

        Assert.Equal(2, sql.Count);
        Assert.Contains("UPDATE", sql[0]);
        Assert.Contains("`Name`", sql[0]);
        Assert.Contains("WHERE", sql[0]);
    }

    [Fact]
    public void MySql_GenerateDelete_CorrectSyntax()
    {
        var gen = new MySqlSqlGenerator();
        var sql = gen.GenerateDeleteStatements(TestTable, DeleteRows);

        Assert.Equal(2, sql.Count);
        Assert.Contains("DELETE FROM", sql[0]);
    }

    // ── PostgreSQL ──

    [Fact]
    public void PostgreSql_GenerateUpdate_CorrectSyntax()
    {
        var gen = new PostgresSqlGenerator();
        var sql = gen.GenerateUpdateStatements(TestTable, UpdateRows);

        Assert.Equal(2, sql.Count);
        Assert.Contains("UPDATE", sql[0]);
        Assert.Contains("SET", sql[0]);
        Assert.Contains("WHERE", sql[0]);
    }

    [Fact]
    public void PostgreSql_GenerateDelete_CorrectSyntax()
    {
        var gen = new PostgresSqlGenerator();
        var sql = gen.GenerateDeleteStatements(TestTable, DeleteRows);

        Assert.Equal(2, sql.Count);
        Assert.Contains("DELETE FROM", sql[0]);
    }

    // ── SQLite ──

    [Fact]
    public void Sqlite_GenerateUpdate_CorrectSyntax()
    {
        var gen = new SqliteSqlGenerator();
        var sql = gen.GenerateUpdateStatements(TestTable, UpdateRows);

        Assert.Equal(2, sql.Count);
        Assert.Contains("UPDATE", sql[0]);
        Assert.Contains("SET", sql[0]);
        Assert.Contains("WHERE", sql[0]);
    }

    [Fact]
    public void Sqlite_GenerateDelete_CorrectSyntax()
    {
        var gen = new SqliteSqlGenerator();
        var sql = gen.GenerateDeleteStatements(TestTable, DeleteRows);

        Assert.Equal(2, sql.Count);
        Assert.Contains("DELETE FROM", sql[0]);
    }

    // ── NULL 值处理 ──

    [Fact]
    public void SqlServer_Update_NullValue_GeneratesNull()
    {
        var gen = new SqlServerSqlGenerator();
        var rows = new List<IReadOnlyDictionary<string, string?>>
        {
            new Dictionary<string, string?> { ["Id"] = "1", ["Name"] = null, ["Age"] = "30" }
        };

        var sql = gen.GenerateUpdateStatements(TestTable, rows);

        Assert.Contains("NULL", sql[0]);
    }

    // ── 空列表 ──

    [Fact]
    public void SqlServer_EmptyRows_ReturnsEmptyList()
    {
        var gen = new SqlServerSqlGenerator();

        Assert.Empty(gen.GenerateUpdateStatements(TestTable, []));
        Assert.Empty(gen.GenerateDeleteStatements(TestTable, []));
    }

    // ── 接口方法（带 dbType 参数）──

    [Fact]
    public void SqlServer_InterfaceOverload_DelegatesToInternal()
    {
        var gen = new SqlServerSqlGenerator();

        var sql1 = gen.GenerateUpdateStatements(TestTable, UpdateRows);
        var sql2 = gen.GenerateUpdateStatements(DatabaseType.SqlServer, TestTable, UpdateRows);

        Assert.Equal(sql1.Count, sql2.Count);
        Assert.Equal(sql1[0], sql2[0]);
    }
}
