using DBSync.Core.Models;
using DBSync.Core.Schema;
using SqlSugar;

namespace DBSync.Tests.Schema;

public class SqlServerSchemaReaderTests
{
    [Fact]
    public async Task TestConnectionAsync_NonSqlServerConnection_ReturnsFalse()
    {
        var reader = new SqlServerSchemaReader();
        var connection = new DatabaseConnection
        {
            Name = "mysql",
            DbType = DatabaseType.MySql,
            ConnectionString = "Server=localhost;"
        };

        var result = await reader.TestConnectionAsync(connection);

        Assert.False(result);
    }

    [Fact]
    public async Task ReadAllTablesAsync_NonSqlServerConnection_ThrowsArgumentException()
    {
        var reader = new SqlServerSchemaReader();
        var connection = new DatabaseConnection
        {
            Name = "mysql",
            DbType = DatabaseType.MySql,
            ConnectionString = "Server=localhost;"
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAllTablesAsync(connection));

        Assert.Equal("connection", ex.ParamName);
    }

    [Fact]
    public async Task TestConnectionAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var reader = new SqlServerSchemaReader();
        var connection = new DatabaseConnection
        {
            Name = "sqlserver",
            DbType = DatabaseType.SqlServer,
            ConnectionString = "Server=localhost;"
        };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => reader.TestConnectionAsync(connection, cts.Token));
    }

    [Fact(Skip = "需要可用的 SQL Server 或 LocalDB 实例；当前环境 LocalDB 无法自动创建实例。")]
    public async Task ReadAllTablesAsync_LocalDbDatabase_ReadsTablesColumnsKeysAndIndexes()
    {
        var databaseName = $"DBSyncTool_Test_{Guid.NewGuid():N}";
        var masterConnectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True;";
        var databaseConnectionString = $"{masterConnectionString}Database={databaseName};";

        using var masterDb = CreateClient(masterConnectionString);
        await masterDb.Ado.ExecuteCommandAsync($"CREATE DATABASE [{databaseName}]");

        try
        {
            using var db = CreateClient(databaseConnectionString);
            await db.Ado.ExecuteCommandAsync("""
CREATE TABLE dbo.Customers
(
    Id int IDENTITY(1,1) NOT NULL,
    Code nvarchar(32) NOT NULL,
    CreatedAt datetime2 NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Customers PRIMARY KEY (Id),
    CONSTRAINT UQ_Customers_Code UNIQUE (Code)
);

CREATE TABLE dbo.Orders
(
    OrderId int NOT NULL,
    LineNo int NOT NULL,
    CustomerId int NOT NULL,
    Amount decimal(18,2) NOT NULL,
    IsPaid bit NOT NULL DEFAULT 0,
    Payload varbinary(32) NULL,
    CONSTRAINT PK_Orders PRIMARY KEY (OrderId, LineNo),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id)
);

CREATE INDEX IX_Orders_CustomerId ON dbo.Orders(CustomerId);

CREATE TABLE dbo.NoPk
(
    Name nvarchar(20) NULL
);
""");

            var reader = new SqlServerSchemaReader();
            var connection = new DatabaseConnection
            {
                Name = "localdb",
                DbType = DatabaseType.SqlServer,
                ConnectionString = databaseConnectionString
            };

            var tables = await reader.ReadAllTablesAsync(connection);

            var customers = Assert.Single(tables, t => t.Name == "Customers");
            Assert.Equal("dbo", customers.Schema);
            Assert.Equal(["Id"], customers.PrimaryKeyColumns);
            Assert.Contains(customers.Columns, c => c.Name == "Id" && c.ColumnType == DbColumnType.Integer && c.IsIdentity);
            Assert.Contains(customers.Columns, c => c.Name == "Code" && c.ColumnType == DbColumnType.Text && c.MaxLength == 32);
            Assert.Contains(customers.Columns, c => c.Name == "CreatedAt" && c.ColumnType == DbColumnType.DateTime && c.IsNullable);
            Assert.Contains(customers.Indexes, i => i.Name == "UQ_Customers_Code" && i.IsUnique);

            var orders = Assert.Single(tables, t => t.Name == "Orders");
            Assert.Equal(["OrderId", "LineNo"], orders.PrimaryKeyColumns);
            Assert.Contains(orders.Columns, c => c.Name == "Amount" && c.ColumnType == DbColumnType.Decimal && c.Precision == 18 && c.Scale == 2);
            Assert.Contains(orders.Columns, c => c.Name == "IsPaid" && c.ColumnType == DbColumnType.Boolean);
            Assert.Contains(orders.Columns, c => c.Name == "Payload" && c.ColumnType == DbColumnType.Binary && c.IsNullable);
            Assert.Contains(orders.ForeignKeys, fk => fk.ColumnName == "CustomerId" && fk.ReferencedTable == "Customers");
            Assert.Contains(orders.Indexes, i => i.Name == "IX_Orders_CustomerId" && i.ColumnNames.SequenceEqual(["CustomerId"]));

            var noPk = Assert.Single(tables, t => t.Name == "NoPk");
            Assert.Empty(noPk.PrimaryKeyColumns);
        }
        finally
        {
            await masterDb.Ado.ExecuteCommandAsync($"""
ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [{databaseName}];
""");
        }
    }

    /// <summary>
    /// 创建测试用 SqlSugar 客户端。
    /// </summary>
    /// <param name="connectionString">连接字符串</param>
    /// <returns>SqlSugar 客户端</returns>
    private static SqlSugarClient CreateClient(string connectionString)
    {
        return new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true
        });
    }
}
