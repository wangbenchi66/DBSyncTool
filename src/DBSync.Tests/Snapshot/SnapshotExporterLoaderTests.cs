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

    [Fact]
    public async Task ExportAndLoadAsync_EmptyPassword_RoundTripsSnapshot()
    {
        var table = TableModelFactory.NoPrimaryKey("Logs");
        var exporter = new SnapshotExporter(new FakeSchemaReader([table]), new SqlServerDataFingerprinter());
        var loader = new SnapshotLoader();
        await using var stream = new MemoryStream();

        await exporter.ExportAsync(
            SqlServerConnection(),
            new ExportOptions
            {
                Password = "",
                Tables = [new TableExportOptions { TableName = table.FullName }]
            },
            stream);

        stream.Position = 0;
        var snapshot = await loader.LoadAsync(stream, "");

        Assert.Equal([table.FullName], snapshot.Manifest.TableNames);
        Assert.True(snapshot.Tables.ContainsKey(table.FullName));
    }

    [Fact]
    public async Task ExportAsync_OneSelectedTable_WritesOnlyThatTable()
    {
        var selected = TableModelFactory.NoPrimaryKey("Selected");
        var ignored = TableModelFactory.NoPrimaryKey("Ignored");
        var exporter = new SnapshotExporter(new FakeSchemaReader([selected, ignored]), new SqlServerDataFingerprinter());
        var loader = new SnapshotLoader();
        await using var stream = new MemoryStream();

        await exporter.ExportAsync(
            SqlServerConnection(),
            new ExportOptions
            {
                Password = "",
                Tables = [new TableExportOptions { TableName = selected.FullName }]
            },
            stream);

        stream.Position = 0;
        var snapshot = await loader.LoadAsync(stream, "");

        Assert.Equal([selected.FullName], snapshot.Manifest.TableNames);
        Assert.Equal([selected.FullName], snapshot.Tables.Keys);
    }

    [Fact]
    public async Task ExportAndLoadAsync_RowFingerprints_RoundTripsSnapshot()
    {
        var table = TableModelFactory.Simple("Logs");
        var row = new RowHash
        {
            PrimaryKeyValues = new Dictionary<string, string?> { ["Id"] = "1" },
            Hash = "abc123"
        };
        var exporter = new SnapshotExporter(new FakeSchemaReader([table]), new FakeFingerprinter(row));
        var loader = new SnapshotLoader();
        await using var stream = new MemoryStream();

        await exporter.ExportAsync(
            SqlServerConnection(),
            new ExportOptions
            {
                Password = "",
                Tables = [new TableExportOptions { TableName = table.FullName }]
            },
            stream);

        stream.Position = 0;
        var snapshot = await loader.LoadAsync(stream, "");

        Assert.Single(snapshot.DataFingerprints[table.FullName]);
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

    private sealed class FakeFingerprinter(RowHash row) : IDataFingerprinter
    {
        public async IAsyncEnumerable<RowHash> ReadRowHashesAsync(
            DatabaseConnection connection,
            TableModel table,
            string? whereClause = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return row;
        }
    }
}
