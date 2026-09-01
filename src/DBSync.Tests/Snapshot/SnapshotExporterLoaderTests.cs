using DBSync.Core.Data;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Core.Snapshot;
using DBSync.Tests.Helpers;

namespace DBSync.Tests.Snapshot;

public class SnapshotExporterLoaderTests
{
    [Fact]
    public async Task ExportAndLoadAsync_NoPrimaryKeyTable_RoundTripsManifestAndSchema()
    {
        var table = TableModelFactory.NoPrimaryKey("Logs");
        var exporter = new SnapshotExporter(new FakeSchemaReader([table]), new SqlServerDataFingerprinter());
        var loader = new SnapshotLoader();
        await using var stream = new MemoryStream();

        await exporter.ExportAsync(
            SqlServerConnection(),
            new ExportOptions
            {
                Password = "pwd",
                PasswordHint = "测试提示",
                Tables = [new TableExportOptions { TableName = table.FullName }]
            },
            stream);

        stream.Position = 0;
        var hint = await loader.ReadPasswordHintAsync(stream);
        stream.Position = 0;
        var snapshot = await loader.LoadAsync(stream, "pwd");

        Assert.Equal("测试提示", hint);
        Assert.Equal(DatabaseType.SqlServer, snapshot.Manifest.DbType);
        Assert.Equal([table.FullName], snapshot.Manifest.TableNames);
        Assert.True(snapshot.Tables.ContainsKey(table.FullName));
        Assert.Empty(snapshot.DataFingerprints[table.FullName]);
    }

    [Fact]
    public async Task LoadAsync_WrongPassword_ThrowsInvalidOperationException()
    {
        var table = TableModelFactory.NoPrimaryKey("Logs");
        var exporter = new SnapshotExporter(new FakeSchemaReader([table]), new SqlServerDataFingerprinter());
        var loader = new SnapshotLoader();
        await using var stream = new MemoryStream();

        await exporter.ExportAsync(
            SqlServerConnection(),
            new ExportOptions
            {
                Password = "right",
                Tables = [new TableExportOptions { TableName = table.FullName }]
            },
            stream);

        stream.Position = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync(stream, "wrong"));

        Assert.Contains("密码错误", ex.Message);
    }

    /// <summary>
    /// 创建测试用 SQL Server 连接配置。
    /// </summary>
    /// <returns>数据库连接配置</returns>
    private static DatabaseConnection SqlServerConnection()
    {
        return new DatabaseConnection
        {
            Name = "test",
            DbType = DatabaseType.SqlServer,
            ConnectionString = "Server=localhost;"
        };
    }

    private sealed class FakeSchemaReader(IReadOnlyList<TableModel> tables) : ISchemaReader
    {
        /// <summary>
        /// 读取全部测试表。
        /// </summary>
        /// <param name="connection">数据库连接配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>测试表集合</returns>
        public Task<IReadOnlyList<TableModel>> ReadAllTablesAsync(
            DatabaseConnection connection,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tables);
        }

        /// <summary>
        /// 读取指定测试表。
        /// </summary>
        /// <param name="connection">数据库连接配置</param>
        /// <param name="tableName">表名</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>测试表，不存在时返回 null</returns>
        public Task<TableModel?> ReadTableAsync(
            DatabaseConnection connection,
            string tableName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tables.FirstOrDefault(t =>
                string.Equals(t.FullName, tableName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// 返回测试连接可用。
        /// </summary>
        /// <param name="connection">数据库连接配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>固定返回 true</returns>
        public Task<bool> TestConnectionAsync(
            DatabaseConnection connection,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}
