using DBSync.Core.Execution;

namespace DBSync.Tests.Execution;

/// <summary>
/// ScriptExecutor Dry Run 单元测试
///</summary>
public class ScriptExecutorTests
{
    private readonly ScriptExecutor _executor = new();

    [Fact]
    public void DryRun_EmptyScript_ReturnsZeroCounts()
    {
        var plan = _executor.DryRun("");

        Assert.Equal(0, plan.TotalStatements);
        Assert.Equal(0, plan.DdlCount);
        Assert.Equal(0, plan.DmlCount);
        Assert.False(plan.HasTransaction);
    }

    [Fact]
    public void DryRun_CreateAndInsert_CountsCorrectly()
    {
        var script = """
            CREATE TABLE Users (Id INT PRIMARY KEY);
            INSERT INTO Users (Id) VALUES (1);
            INSERT INTO Users (Id) VALUES (2);
            ALTER TABLE Users ADD COLUMN Name NVARCHAR(50)
            """;

        var plan = _executor.DryRun(script);

        Assert.Equal(2, plan.DdlCount);
        Assert.Equal(2, plan.DmlCount);
        Assert.Equal(4, plan.TotalStatements);
    }

    [Fact]
    public void DryRun_WithTransaction_DetectsTransaction()
    {
        var script = """
            BEGIN TRANSACTION;
            INSERT INTO Users (Id) VALUES (1);
            COMMIT TRANSACTION
            """;

        var plan = _executor.DryRun(script);

        Assert.True(plan.HasTransaction);
    }

    [Fact]
    public void DryRun_UpdateAndDelete_CountsAsDml()
    {
        var script = """
            UPDATE Users SET Name = 'test' WHERE Id = 1;
            DELETE FROM Users WHERE Id = 2
            """;

        var plan = _executor.DryRun(script);

        Assert.Equal(0, plan.DdlCount);
        Assert.Equal(2, plan.DmlCount);
    }

    [Fact]
    public void DryRun_DropTable_CountsAsDdl()
    {
        var script = "DROP TABLE IF EXISTS Users";

        var plan = _executor.DryRun(script);

        Assert.Equal(1, plan.DdlCount);
        Assert.Equal(0, plan.DmlCount);
    }

    [Fact]
    public void DryRun_DdlStatements_CapturedInList()
    {
        var script = """
            CREATE TABLE Users (Id INT);
            ALTER TABLE Users ADD Name NVARCHAR(50);
            INSERT INTO Users (Id) VALUES (1)
            """;

        var plan = _executor.DryRun(script);

        Assert.Equal(2, plan.DdlStatements.Count);
        Assert.Contains("CREATE TABLE", plan.DdlStatements[0]);
        Assert.Contains("ALTER TABLE", plan.DdlStatements[1]);
    }

    [Fact]
    public void DryRun_LongStatement_TruncatedInDdlList()
    {
        var longDdl = "CREATE TABLE VeryLongTableName (" + string.Join(", ", Enumerable.Range(1, 50).Select(i => $"Col{i} INT")) + ")";
        var script = longDdl;

        var plan = _executor.DryRun(script);

        Assert.Single(plan.DdlStatements);
        Assert.True(plan.DdlStatements[0].Length <= 125);
        Assert.EndsWith("...", plan.DdlStatements[0]);
    }
}
