using DBSync.Core.Data;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Core.SqlGenerators;
using DBSync.Desktop.Models;
using DBSync.Desktop.Services;
using DBSync.Desktop.ViewModels;
using System.Collections.ObjectModel;

namespace DBSync.Tests.ViewModels;

public sealed class DirectCompareViewModelTests
{
    [Fact]
    public void ActivateDirectCompareTab_ShouldRefreshConnections()
    {
        var connections = new[]
        {
            CreateConnection("源库", DatabaseType.MySql),
            CreateConnection("目标库", DatabaseType.MySql)
        };

        var directCompare = CreateDirectCompareViewModel(connections);
        var workflow = new SyncWorkflowViewModel(
            CreateExportViewModel(connections),
            CreateCompareViewModel(connections),
            directCompare);

        Assert.Empty(directCompare.Connections);

        workflow.ActivateDirectCompareTab();

        Assert.Equal(2, directCompare.Connections.Count);
        Assert.Equal("源库", directCompare.Connections[0].Name);
    }

    [Fact]
    public async Task RunCompare_ShouldClassifySourceAndTargetDifferently()
    {
        var sourceTables = new[]
        {
            CreateTable("shared_table"),
            CreateTable("source_only_table")
        };
        var targetTables = new[]
        {
            CreateTable("shared_table"),
            CreateTable("target_only_table")
        };

        var directCompare = CreateDirectCompareViewModel(
            [CreateConnection("源库", DatabaseType.MySql), CreateConnection("目标库", DatabaseType.MySql)],
            sourceTables,
            targetTables);

        directCompare.RefreshConnections();
        directCompare.SelectedSourceConnection = directCompare.Connections.First(c => c.Name == "源库");
        directCompare.SelectedTargetConnection = directCompare.Connections.First(c => c.Name == "目标库");

        await directCompare.RunCompareCommand.ExecuteAsync(null);

        Assert.Single(directCompare.OnlySourceNodes);
        Assert.Single(directCompare.OnlyTargetNodes);
        Assert.Contains(directCompare.OnlySourceNodes, node => node.Title.StartsWith("source_only_table", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(directCompare.OnlyTargetNodes, node => node.Title.StartsWith("target_only_table", StringComparison.OrdinalIgnoreCase));
    }

    private static DirectCompareViewModel CreateDirectCompareViewModel(
        IReadOnlyList<DatabaseConnection> connections,
        IReadOnlyList<TableModel>? sourceTables = null,
        IReadOnlyList<TableModel>? targetTables = null)
    {
        return new DirectCompareViewModel(
            new FakeConnectionStore(connections),
            new FakeAppSettingsStore(),
            new FakeSchemaReader(sourceTables, targetTables),
            new FakeSqlGenerator(),
            new FakeFingerprinter(),
            new NullWindowProvider());
    }

    private static ExportViewModel CreateExportViewModel(IReadOnlyList<DatabaseConnection> connections)
    {
        return new ExportViewModel(
            new FakeConnectionStore(connections),
            new FakeSchemaReader(),
            new FakeSnapshotExporter(),
            new FakeAppSettingsStore(),
            new NullWindowProvider());
    }

    private static CompareViewModel CreateCompareViewModel(IReadOnlyList<DatabaseConnection> connections)
    {
        return new CompareViewModel(
            new FakeConnectionStore(connections),
            new FakeAppSettingsStore(),
            new FakeSchemaReader(),
            new FakeSnapshotLoader(),
            new FakeSqlGenerator(),
            new DBSync.Desktop.Services.DiffReportExporter(),
            new FakeFingerprinter(),
            new NullWindowProvider());
    }

    private static DatabaseConnection CreateConnection(string name, DatabaseType dbType)
    {
        var connection = new DatabaseConnection
        {
            Name = name,
            DbType = dbType,
            ConnectionString = "",
            Server = "localhost"
        };

        return connection with { ConnectionString = connection.BuildConnectionString() };
    }

    private static TableModel CreateTable(string name)
    {
        return new TableModel
        {
            Name = name,
            Schema = "",
            Columns =
            [
                new ColumnModel
                {
                    Name = "id",
                    DbTypeName = "int",
                    ColumnType = DbColumnType.Integer,
                    IsNullable = false,
                    OrdinalPosition = 1
                }
            ],
            PrimaryKeyColumns = ["id"],
            ForeignKeys = [],
            Indexes = [],
            Comment = name + " 注释"
        };
    }

    private sealed class FakeConnectionStore(IReadOnlyList<DatabaseConnection> connections) : IConnectionStore
    {
        public IReadOnlyList<DatabaseConnection> Load() => connections;
        public void Save(IReadOnlyList<DatabaseConnection> connections) { }
    }

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        private AppSettings _settings = new();
        public AppSettings Load() => _settings;
        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class FakeSchemaReader(
        IReadOnlyList<TableModel>? sourceTables = null,
        IReadOnlyList<TableModel>? targetTables = null) : ISchemaReader
    {
        public Task<IReadOnlyList<TableModel>> ReadAllTablesAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
        {
            if (connection.Name == "源库")
                return Task.FromResult<IReadOnlyList<TableModel>>(sourceTables ?? []);

            if (connection.Name == "目标库")
                return Task.FromResult<IReadOnlyList<TableModel>>(targetTables ?? []);

            return Task.FromResult<IReadOnlyList<TableModel>>([]);
        }

        public Task<TableModel?> ReadTableAsync(DatabaseConnection connection, string tableName, CancellationToken cancellationToken = default) =>
            Task.FromResult<TableModel?>(null);

        public Task<IReadOnlyList<DBSync.Core.Models.DatabaseObjectModel>> ReadAllObjectsAsync(DatabaseConnection connection, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DBSync.Core.Models.DatabaseObjectModel>>([]);

        public Task<bool> TestConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeSqlGenerator : ISqlGenerator
    {
        public string GenerateUpgradeScript(DatabaseType dbType, SchemaDiff schemaDiff, IReadOnlyDictionary<string, DataDiff> dataDiffs, IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>? fullData = null, bool useTransaction = true) => "";
        public string GenerateCreateTable(DatabaseType dbType, TableModel table) => "";
        public string GenerateDropTable(DatabaseType dbType, TableModel table) => "";
        public IReadOnlyList<string> GenerateAlterTable(DatabaseType dbType, TableDiff diff) => [];
        public IReadOnlyList<string> GenerateUpdateStatements(DatabaseType dbType, TableModel table, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) => [];
        public IReadOnlyList<string> GenerateDeleteStatements(DatabaseType dbType, TableModel table, IReadOnlyList<IReadOnlyDictionary<string, string?>> primaryKeyValues) => [];
        public IReadOnlyList<string> GenerateInsertStatements(DatabaseType dbType, TableModel table, IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) => [];
    }

    private sealed class FakeFingerprinter : IDataFingerprinter
    {
        public async IAsyncEnumerable<RowHash> ReadRowHashesAsync(DatabaseConnection connection, TableModel table, string? whereClause = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeSnapshotExporter : DBSync.Core.Snapshot.ISnapshotExporter
    {
        public Task ExportAsync(DatabaseConnection connection, DBSync.Core.Models.ExportOptions options, Stream output, IProgress<(int currentTable, int totalTables, string tableName, long currentRow)>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeSnapshotLoader : DBSync.Core.Snapshot.ISnapshotLoader
    {
        public Task<DBSync.Core.Models.Snapshot> LoadAsync(Stream input, string password, CancellationToken cancellationToken = default) => Task.FromResult(new DBSync.Core.Models.Snapshot
        {
            Manifest = new DBSync.Core.Models.SnapshotManifest
            {
                Version = "1",
                DbType = DatabaseType.MySql,
                ExportedAt = DateTimeOffset.Now,
                TableNames = []
            },
            Tables = new Dictionary<string, TableModel>(),
            DataFingerprints = new Dictionary<string, IReadOnlyList<RowHash>>(),
            FullData = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>()
        });

        public Task<string?> ReadPasswordHintAsync(Stream inputStream) => Task.FromResult<string?>(null);
    }

    private sealed class NullWindowProvider : IWindowProvider
    {
        public Avalonia.Controls.Window? GetMainWindow() => null;
    }
}
