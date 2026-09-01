using DBSync.Core.Models;
using DBSync.Core.SqlGenerators;
using DBSync.Tests.Helpers;

namespace DBSync.Tests.SqlGenerators;

public class SqlServerSqlGeneratorTests
{
    [Fact]
    public void GenerateCreateTable_TableWithIdentityAndForeignKey_GeneratesCreateTable()
    {
        var generator = new SqlServerSqlGenerator();
        var table = TableModelFactory.WithColumns(
            "Orders",
            [
                TableModelFactory.IdColumn(),
                TableModelFactory.Col("CustomerId", DbColumnType.Integer),
                TableModelFactory.Col("Status", DbColumnType.Text, maxLength: 20) with
                {
                    DbTypeName = "nvarchar",
                    DefaultValue = "N'Pending'"
                }
            ],
            foreignKeys: [TableModelFactory.Fk("CustomerId", "Customers")],
            indexes: [TableModelFactory.Index("IX_Orders_CustomerId", ["CustomerId"])]);

        var sql = generator.GenerateCreateTable(table);

        Assert.Contains("CREATE TABLE [dbo].[Orders]", sql);
        Assert.Contains("[Id] integer IDENTITY(1,1) NOT NULL", sql);
        Assert.Contains("[Status] nvarchar(20) NOT NULL DEFAULT N'Pending'", sql);
        Assert.Contains("CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])", sql);
        Assert.Contains("CONSTRAINT [FK_CustomerId_Customers] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customers] ([Id])", sql);
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Orders_CustomerId] ON [dbo].[Orders] ([CustomerId]);", sql);
    }

    [Fact]
    public void GenerateDropTable_GeneratesDropIfExists()
    {
        var generator = new SqlServerSqlGenerator();

        var sql = generator.GenerateDropTable(TableModelFactory.Simple("Users"));

        Assert.Equal("DROP TABLE IF EXISTS [dbo].[Users];", sql);
    }

    [Fact]
    public void GenerateAlterTable_ModifiedColumn_GeneratesAlterColumn()
    {
        var generator = new SqlServerSqlGenerator();
        var baseline = TableModelFactory.WithColumns("Users", [TableModelFactory.Col("Amount", DbColumnType.Integer)]);
        var source = TableModelFactory.WithColumns("Users", [TableModelFactory.Col("Amount", DbColumnType.Decimal)]);
        var diff = new TableDiff
        {
            BaselineTable = baseline,
            SourceTable = source,
            ColumnDiffs =
            [
                new ColumnDiff
                {
                    Before = baseline.Columns[0],
                    After = source.Columns[0],
                    DiffType = ColumnDiffType.Modified
                }
            ],
            IndexDiffs = []
        };

        var sql = generator.GenerateAlterTable(diff);

        Assert.Equal("ALTER TABLE [dbo].[Users] ALTER COLUMN [Amount] decimal(18,2) NOT NULL;", Assert.Single(sql));
    }

    [Fact]
    public void GenerateUpgradeScript_AddedTables_OrdersParentsBeforeChildren()
    {
        var generator = new SqlServerSqlGenerator();
        var customers = TableModelFactory.Simple("Customers");
        var orders = TableModelFactory.WithColumns(
            "Orders",
            [TableModelFactory.IdColumn(), TableModelFactory.Col("CustomerId", DbColumnType.Integer)],
            foreignKeys: [TableModelFactory.Fk("CustomerId", "Customers")]);
        var diff = new SchemaDiff
        {
            AddedTables = [orders, customers],
            RemovedTables = [],
            ModifiedTables = [],
            CyclicDependencyGroups = []
        };

        var sql = generator.GenerateUpgradeScript(diff, new Dictionary<string, DataDiff>());

        Assert.True(sql.IndexOf("CREATE TABLE [dbo].[Customers]", StringComparison.Ordinal) <
                    sql.IndexOf("CREATE TABLE [dbo].[Orders]", StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateUpgradeScript_CyclicDependencyGroups_GeneratesWarningComment()
    {
        var generator = new SqlServerSqlGenerator();
        var diff = new SchemaDiff
        {
            AddedTables = [],
            RemovedTables = [],
            ModifiedTables = [],
            CyclicDependencyGroups = [["A", "B"]]
        };

        var sql = generator.GenerateUpgradeScript(diff, new Dictionary<string, DataDiff>());

        Assert.Contains("-- 检测到循环外键依赖，需要手动处理: A, B", sql);
    }

    [Fact]
    public void GenerateDdlScript_RemovedTables_DropsChildrenBeforeParents()
    {
        var generator = new SqlServerSqlGenerator();
        var customers = TableModelFactory.Simple("Customers");
        var orders = TableModelFactory.WithColumns(
            "Orders",
            [TableModelFactory.IdColumn(), TableModelFactory.Col("CustomerId", DbColumnType.Integer)],
            foreignKeys: [TableModelFactory.Fk("CustomerId", "Customers")]);
        var diff = new SchemaDiff
        {
            AddedTables = [],
            RemovedTables = [customers, orders],
            ModifiedTables = [],
            CyclicDependencyGroups = []
        };

        var sql = generator.GenerateDdlScript(diff).ToList();

        Assert.True(sql.IndexOf("DROP TABLE IF EXISTS [dbo].[Orders];") <
                    sql.IndexOf("DROP TABLE IF EXISTS [dbo].[Customers];"));
    }
}
