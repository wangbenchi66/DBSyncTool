using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using DBSync.Core.Comparers;
using DBSync.Core.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Core.Snapshot;
using DBSync.Desktop.Services;
using DBSync.Desktop.Storage;
using DBSync.Desktop.Models;
using DBSync.Desktop.Views;
using System.Collections.ObjectModel;

namespace DBSync.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IConnectionStore _connectionStore;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly ISchemaReader _schemaReader;
    private readonly ISnapshotExporter _snapshotExporter;
    private readonly ISnapshotLoader _snapshotLoader;
    private readonly SqlServerDataFingerprinter _fingerprinter;
    private Window? _ownerWindow;
    private CancellationTokenSource? _exportCancellation;
    private AppSettings _settings;
    private Snapshot? _loadedSnapshot;
    private SchemaDiff? _loadedSchemaDiff;
    private readonly Dictionary<string, DataDiff> _loadedDataDiffs = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string statusText = "就绪";

    [ObservableProperty]
    private string logSummary = "未记录操作";

    [ObservableProperty]
    private ConnectionItemViewModel? selectedConnection;

    [ObservableProperty]
    private string tableFilter = string.Empty;

    [ObservableProperty]
    private string exportPassword = string.Empty;

    [ObservableProperty]
    private string exportPasswordConfirm = string.Empty;

    [ObservableProperty]
    private string passwordHint = string.Empty;

    [ObservableProperty]
    private string exportPath = string.Empty;

    [ObservableProperty]
    private int exportProgress;

    [ObservableProperty]
    private string exportProgressText = "未开始";

    [ObservableProperty]
    private string exportSummary = "尚未导出";

    [ObservableProperty]
    private bool isExporting;

    [ObservableProperty]
    private string compareSnapshotPath = string.Empty;

    [ObservableProperty]
    private string comparePassword = string.Empty;

    [ObservableProperty]
    private string comparePasswordHint = string.Empty;

    [ObservableProperty]
    private string compareSnapshotMetaText = "尚未加载快照";

    [ObservableProperty]
    private string compareProgressText = "未开始";

    [ObservableProperty]
    private int compareProgress;

    [ObservableProperty]
    private string compareSummaryText = "尚未比对";

    [ObservableProperty]
    private ConnectionItemViewModel? selectedCompareConnection;

    [ObservableProperty]
    private string rowCountWarningThresholdText = "100000";

    [ObservableProperty]
    private bool hasPendingOperation;

    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = new();

    public ObservableCollection<ExportTableItemViewModel> ExportTables { get; } = new();

    public ObservableCollection<ExportTableItemViewModel> FilteredExportTables { get; } = new();

    public ObservableCollection<ConnectionItemViewModel> CompareConnections { get; } = new();

    public ObservableCollection<CompareSchemaNodeViewModel> CompareSchemaNodes { get; } = new();

    public ObservableCollection<CompareDataSummaryViewModel> CompareDataSummaries { get; } = new();

    public MainWindowViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore appSettingsStore,
        ISchemaReader schemaReader,
        ISnapshotExporter snapshotExporter,
        ISnapshotLoader snapshotLoader,
        SqlServerDataFingerprinter fingerprinter)
    {
        _connectionStore = connectionStore;
        _appSettingsStore = appSettingsStore;
        _schemaReader = schemaReader;
        _snapshotExporter = snapshotExporter;
        _snapshotLoader = snapshotLoader;
        _fingerprinter = fingerprinter;
        _settings = _appSettingsStore.Load();
        RowCountWarningThresholdText = _settings.RowCountWarningThreshold.ToString();
        ExportPath = _settings.LastExportPath ?? CreateDefaultExportPath();
        CompareSnapshotPath = _settings.LastSnapshotPath ?? string.Empty;
        LoadConnections();
        SelectedConnection = Connections.FirstOrDefault(c =>
            string.Equals(c.Name, _settings.LastConnectionName, StringComparison.OrdinalIgnoreCase))
            ?? Connections.FirstOrDefault();
        RefreshCompareConnections();
        CompareSnapshotMetaText = "尚未加载快照";
    }

    [RelayCommand]
    private void OpenExport()
    {
        StatusText = "准备导出快照";
        LogSummary = "请选择连接并加载表。";
        HasPendingOperation = true;
    }

    [RelayCommand]
    private void OpenCompare()
    {
        StatusText = "准备加载快照并比对";
        LogSummary = "已进入比对入口";
        HasPendingOperation = true;
    }

    [RelayCommand]
    private void AddConnection()
    {
        var item = new ConnectionItemViewModel("新连接", DatabaseType.SqlServer, "localhost");
        Connections.Add(item);
        SelectedConnection = item;
        HasPendingOperation = true;
        Save();
    }

    [RelayCommand]
    private void EditConnection()
    {
        var target = SelectedConnection ?? Connections.FirstOrDefault();
        if (target is null)
            return;

        target.Name = $"{target.Name}*";
        HasPendingOperation = true;

        Save();
    }

    [RelayCommand]
    private void DeleteConnection()
    {
        var target = SelectedConnection ?? Connections.FirstOrDefault();
        if (target is null)
            return;

        Connections.Remove(target);
        SelectedConnection = Connections.FirstOrDefault();
        HasPendingOperation = true;

        Save();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (int.TryParse(RowCountWarningThresholdText, out var threshold) && threshold > 0)
        {
            _settings = _settings with { RowCountWarningThreshold = threshold };
            _appSettingsStore.Save(_settings);
            HasPendingOperation = false;
            StatusText = "设置已保存";
        }
        else
        {
            StatusText = "行数阈值必须是大于 0 的整数";
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var target = SelectedConnection ?? Connections.FirstOrDefault();
        if (target is null)
        {
            StatusText = "没有可测试的连接";
            return;
        }

        var connection = new DatabaseConnection
        {
            Name = target.Name,
            DbType = target.DbType,
            ConnectionString = target.ConnectionString
        };

        var ok = await _schemaReader.TestConnectionAsync(connection);
        StatusText = ok ? "连接测试成功" : "连接测试失败";
        LogSummary = StatusText;
    }

    [RelayCommand]
    private async Task LoadTablesAsync()
    {
        var connection = CreateSelectedConnection();
        if (connection is null)
        {
            StatusText = "请先选择连接";
            return;
        }

        try
        {
            StatusText = "正在读取表结构...";
            ExportTables.Clear();
            FilteredExportTables.Clear();

            var tables = await _schemaReader.ReadAllTablesAsync(connection);
            foreach (var table in tables.OrderBy(t => t.FullName))
            {
                ExportTables.Add(new ExportTableItemViewModel(table)
                {
                    RowCountWarningThreshold = ParseRowCountWarningThreshold(),
                    ConfirmLargeExportAsync = ConfirmLargeExportAsync
                });
            }

            ApplyTableFilter();
            StatusText = $"已加载 {ExportTables.Count} 张表";
            LogSummary = "请选择需要写入快照的表。";
            HasPendingOperation = true;
        }
        catch (Exception ex)
        {
            StatusText = "读取表结构失败";
            LogSummary = ex.Message;
        }
    }

    [RelayCommand]
    private void SelectAllTables()
    {
        foreach (var table in FilteredExportTables)
            table.IsSelected = true;
    }

    [RelayCommand]
    private void InvertTableSelection()
    {
        foreach (var table in FilteredExportTables)
            table.IsSelected = !table.IsSelected;
    }

    [RelayCommand]
    private void UseDefaultExportPath()
    {
        ExportPath = CreateDefaultExportPath();
    }

    [RelayCommand]
    private async Task BrowseSnapshotAsync()
    {
        if (_ownerWindow is null)
            return;

        var files = await _ownerWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择快照文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("DBSync 快照") { Patterns = ["*.dbsync"] }]
        });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
            CompareSnapshotPath = localPath;

        await using var stream = await file.OpenReadAsync();
        ComparePasswordHint = await _snapshotLoader.ReadPasswordHintAsync(stream) ?? string.Empty;
        CompareSnapshotMetaText = "已选择快照，请输入密码并加载。";
        StatusText = "已选择快照文件";
    }

    [RelayCommand]
    private async Task LoadSnapshotAsync()
    {
        if (string.IsNullOrWhiteSpace(CompareSnapshotPath))
        {
            StatusText = "请先选择快照文件";
            return;
        }

        if (!File.Exists(CompareSnapshotPath))
        {
            StatusText = "快照文件不存在";
            return;
        }

        if (string.IsNullOrWhiteSpace(ComparePassword))
        {
            StatusText = "请输入快照密码";
            return;
        }

        try
        {
            StatusText = "正在加载快照...";
            CompareProgress = 0;
            CompareProgressText = "正在解密快照";

            await using var stream = File.OpenRead(CompareSnapshotPath);
            ComparePasswordHint = await _snapshotLoader.ReadPasswordHintAsync(stream) ?? string.Empty;
            stream.Position = 0;
            _loadedSnapshot = await _snapshotLoader.LoadAsync(stream, ComparePassword);

            CompareSnapshotMetaText = $"导出时间：{_loadedSnapshot.Manifest.ExportedAt:yyyy-MM-dd HH:mm:ss}；" +
                                      $"数据库：{_loadedSnapshot.Manifest.DbType}；" +
                                      $"表数量：{_loadedSnapshot.Manifest.TableNames.Count}";
            CompareSummaryText = "快照已加载，等待选择连接并开始比对。";
            StatusText = "快照加载成功";
            LogSummary = CompareSnapshotMetaText;
            HasPendingOperation = true;
            _settings = _settings with { LastSnapshotPath = CompareSnapshotPath };
            _appSettingsStore.Save(_settings);
            RefreshCompareConnections();
        }
        catch (Exception ex)
        {
            _loadedSnapshot = null;
            _loadedSchemaDiff = null;
            _loadedDataDiffs.Clear();
            CompareSchemaNodes.Clear();
            CompareDataSummaries.Clear();
            RefreshCompareConnections();
            CompareSnapshotMetaText = "快照加载失败";
            StatusText = "快照加载失败";
            LogSummary = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RunCompareAsync()
    {
        if (_loadedSnapshot is null)
        {
            StatusText = "请先加载快照";
            return;
        }

        var connection = CreateSelectedCompareConnection();
        if (connection is null)
        {
            StatusText = "请先选择匹配的数据库连接";
            return;
        }

        try
        {
            StatusText = "正在比对结构和数据...";
            CompareProgress = 0;
            CompareProgressText = "正在读取当前库结构";
            CompareSchemaNodes.Clear();
            CompareDataSummaries.Clear();
            _loadedDataDiffs.Clear();

            var currentTables = await _schemaReader.ReadAllTablesAsync(connection);
            _loadedSchemaDiff = SchemaComparer.Compare(_loadedSnapshot.Tables.Values, currentTables);
            BuildSchemaPreview(_loadedSchemaDiff);

            var currentTableMap = currentTables.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);
            var baselineTables = _loadedSnapshot.Tables.Values.OrderBy(t => t.FullName).ToList();

            for (var i = 0; i < baselineTables.Count; i++)
            {
                var table = baselineTables[i];
                CompareProgress = baselineTables.Count == 0 ? 0 : (i + 1) * 100 / baselineTables.Count;
                CompareProgressText = $"正在比对 {i + 1}/{baselineTables.Count}：{table.FullName}";

                var baselineRows = _loadedSnapshot.DataFingerprints.TryGetValue(table.FullName, out var rows)
                    ? rows
                    : [];

                if (!currentTableMap.TryGetValue(table.FullName, out var currentTable))
                {
                    _loadedDataDiffs[table.FullName] = table.HasPrimaryKey
                        ? DataComparer.Compare(baselineRows, [], false)
                        : DataDiff.NoPrimaryKey;
                    continue;
                }

                if (!table.HasPrimaryKey || !currentTable.HasPrimaryKey)
                {
                    _loadedDataDiffs[table.FullName] = DataDiff.NoPrimaryKey;
                    continue;
                }

                var currentRows = new List<RowHash>();
                var currentRow = 0L;
                await foreach (var row in _fingerprinter.ReadRowHashesAsync(connection, currentTable, cancellationToken: CancellationToken.None))
                {
                    currentRows.Add(row);
                    currentRow++;
                    CompareProgressText = $"正在比对 {i + 1}/{baselineTables.Count}：{table.FullName}，当前行 {currentRow}";
                }

                _loadedDataDiffs[table.FullName] = DataComparer.Compare(baselineRows, currentRows, false);
            }

            BuildDataPreview(_loadedDataDiffs, _loadedSnapshot.Tables.Values);
            CompareProgress = 100;
            CompareProgressText = "比对完成";
            CompareSummaryText = BuildCompareSummary(_loadedSchemaDiff, _loadedDataDiffs);
            StatusText = "比对完成";
            LogSummary = CompareSummaryText;
            HasPendingOperation = true;
            SaveCompareHistory(connection.Name);
        }
        catch (Exception ex)
        {
            StatusText = "比对失败";
            CompareProgressText = "比对失败";
            LogSummary = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportSnapshotAsync()
    {
        var connection = CreateSelectedConnection();
        var selectedTables = ExportTables.Where(t => t.IsSelected).ToList();
        if (connection is null)
        {
            StatusText = "请先选择连接";
            return;
        }

        if (selectedTables.Count == 0)
        {
            StatusText = "请至少选择一张表";
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportPassword))
        {
            StatusText = "请输入快照加密密码";
            return;
        }

        if (!string.Equals(ExportPassword, ExportPasswordConfirm, StringComparison.Ordinal))
        {
            StatusText = "两次输入的密码不一致";
            return;
        }

        if (string.IsNullOrWhiteSpace(ExportPath))
        {
            StatusText = "请输入导出路径";
            return;
        }

        var directory = Path.GetDirectoryName(ExportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _exportCancellation = new CancellationTokenSource();
        IsExporting = true;
        ExportProgress = 0;
        ExportProgressText = "正在导出...";
        StatusText = "正在导出快照...";
        HasPendingOperation = true;

        try
        {
            var options = new ExportOptions
            {
                Password = ExportPassword,
                PasswordHint = string.IsNullOrWhiteSpace(PasswordHint) ? null : PasswordHint,
                RowCountWarningThreshold = ParseRowCountWarningThreshold(),
                Tables = selectedTables.Select(t => new TableExportOptions
                {
                    TableName = t.FullName,
                    SyncSchema = true,
                    SyncData = t.SyncData,
                    WhereClause = string.IsNullOrWhiteSpace(t.WhereClause) ? null : t.WhereClause
                }).ToList()
            };

            var progress = new Progress<(int currentTable, int totalTables, string tableName, long currentRow)>(p =>
            {
                ExportProgress = p.totalTables == 0 ? 0 : p.currentTable * 100 / p.totalTables;
                ExportProgressText = $"正在导出 {p.currentTable}/{p.totalTables}：{p.tableName}，当前行 {p.currentRow}";
            });

            await using var stream = File.Create(ExportPath);
            await _snapshotExporter.ExportAsync(connection, options, stream, progress, _exportCancellation.Token);

            var fileSize = new FileInfo(ExportPath).Length;
            ExportProgress = 100;
            ExportProgressText = "导出完成";
            ExportSummary = $"文件：{ExportPath}；大小：{FormatFileSize(fileSize)}；表数量：{selectedTables.Count}";
            StatusText = "导出完成";
            LogSummary = ExportSummary;
            SaveExportHistory(connection.Name, ExportPath);
            HasPendingOperation = false;
        }
        catch (OperationCanceledException)
        {
            StatusText = "导出已取消";
            ExportProgressText = "已取消";
            LogSummary = "未完成快照导出。";
        }
        catch (Exception ex)
        {
            StatusText = "导出失败";
            ExportProgressText = "导出失败";
            LogSummary = ex.Message;
        }
        finally
        {
            IsExporting = false;
            _exportCancellation.Dispose();
            _exportCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelExport()
    {
        _exportCancellation?.Cancel();
    }

    private void LoadConnections()
    {
        foreach (var item in _connectionStore.Load())
            Connections.Add(item);

        RefreshCompareConnections();
    }

    private void Save()
    {
        _connectionStore.Save(Connections.ToList());
        HasPendingOperation = false;
        RefreshCompareConnections();
    }

    partial void OnTableFilterChanged(string value)
    {
        ApplyTableFilter();
    }

    private void ApplyTableFilter()
    {
        FilteredExportTables.Clear();
        var filtered = string.IsNullOrWhiteSpace(TableFilter)
            ? ExportTables
            : ExportTables.Where(t => t.FullName.Contains(TableFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var table in filtered)
            FilteredExportTables.Add(table);
    }

    private void RefreshCompareConnections()
    {
        if (_loadedSnapshot is null)
        {
            CompareConnections.Clear();
            SelectedCompareConnection = null;
            return;
        }

        var targetDbType = _loadedSnapshot?.Manifest.DbType;
        var previousSelection = SelectedCompareConnection?.Name;

        CompareConnections.Clear();

        var filtered = Connections.Where(c => c.DbType == targetDbType);

        foreach (var connection in filtered)
            CompareConnections.Add(connection);

        SelectedCompareConnection = CompareConnections.FirstOrDefault(c =>
            string.Equals(c.Name, previousSelection, StringComparison.OrdinalIgnoreCase))
            ?? CompareConnections.FirstOrDefault();
    }

    private DatabaseConnection? CreateSelectedCompareConnection()
    {
        var target = SelectedCompareConnection ?? CompareConnections.FirstOrDefault();
        return target is null
            ? null
            : new DatabaseConnection
            {
                Name = target.Name,
                DbType = target.DbType,
                ConnectionString = target.ConnectionString
            };
    }

    private void BuildSchemaPreview(SchemaDiff schemaDiff)
    {
        CompareSchemaNodes.Clear();

        var cyclicTables = schemaDiff.CyclicDependencyGroups
            .SelectMany(group => group)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var table in schemaDiff.AddedTables.OrderBy(t => t.FullName))
            CompareSchemaNodes.Add(CreateSchemaNode(table.FullName, "新增表", true, false, table.Columns.Select(c => CreateLeafNode($"列 {c.Name}", "将随表一起创建")).ToList()));

        foreach (var table in schemaDiff.RemovedTables.OrderBy(t => t.FullName))
            CompareSchemaNodes.Add(CreateSchemaNode(table.FullName, "删除表，默认未勾选", false, true));

        foreach (var diff in schemaDiff.ModifiedTables.OrderBy(t => t.SourceTable.FullName))
        {
            var node = CreateSchemaNode(diff.SourceTable.FullName, "结构变更", true, cyclicTables.Contains(diff.SourceTable.FullName));

            foreach (var columnDiff in diff.ColumnDiffs)
            {
                var status = columnDiff.DiffType switch
                {
                    ColumnDiffType.Added => "列新增",
                    ColumnDiffType.Removed => "列删除",
                    _ => "列修改"
                };
                var title = columnDiff.After?.Name ?? columnDiff.Before?.Name ?? "未命名列";
                node.Children.Add(CreateLeafNode(title, status));
            }

            foreach (var indexDiff in diff.IndexDiffs)
            {
                var status = indexDiff.DiffType switch
                {
                    IndexDiffType.Added => "索引新增",
                    IndexDiffType.Removed => "索引删除",
                    _ => "索引修改"
                };
                var title = indexDiff.After?.Name ?? indexDiff.Before?.Name ?? "未命名索引";
                node.Children.Add(CreateLeafNode(title, status));
            }

            if (diff.PrimaryKeyChanged)
                node.Children.Add(CreateLeafNode("主键", "主键定义已变更"));

            CompareSchemaNodes.Add(node);
        }

        foreach (var cycle in schemaDiff.CyclicDependencyGroups)
        {
            var title = $"循环外键依赖：{string.Join("、", cycle)}";
            CompareSchemaNodes.Add(CreateSchemaNode(title, "需手动处理", false, true));
        }
    }

    private void BuildDataPreview(
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        IEnumerable<TableModel> tables)
    {
        CompareDataSummaries.Clear();

        foreach (var table in tables.OrderBy(t => t.FullName))
        {
            var diff = dataDiffs.TryGetValue(table.FullName, out var current)
                ? current
                : DataDiff.Empty;

            var summary = diff.Skipped
                ? "⚠ 已跳过数据比对"
                : $"新增 {diff.RowsToInsert.Count} 行，删除 {diff.DeletedRows.Count} 行，变更 {diff.ChangedRows.Count} 行";

            CompareDataSummaries.Add(new CompareDataSummaryViewModel
            {
                TableName = table.FullName,
                SummaryText = summary,
                IsSkipped = diff.Skipped,
                RowsToInsert = diff.RowsToInsert.Count,
                DeletedRows = diff.DeletedRows.Count,
                ChangedRows = diff.ChangedRows.Count,
                SummaryBrush = ResolveDataBrush(diff)
            });
        }
    }

    private static CompareSchemaNodeViewModel CreateSchemaNode(
        string title,
        string statusText,
        bool isSelected,
        bool hasWarning,
        IReadOnlyList<CompareSchemaNodeViewModel>? children = null)
    {
        var node = new CompareSchemaNodeViewModel
        {
            Title = title,
            StatusText = hasWarning ? $"⚠ {statusText}" : statusText,
            IsSelected = isSelected,
            HasWarning = hasWarning,
            StatusBrush = ResolveSchemaBrush(statusText, hasWarning)
        };

        if (children is not null)
        {
            foreach (var child in children)
                node.Children.Add(child);
        }

        return node;
    }

    private static CompareSchemaNodeViewModel CreateLeafNode(string title, string statusText)
    {
        return new CompareSchemaNodeViewModel
        {
            Title = title,
            StatusText = statusText,
            IsSelected = true,
            StatusBrush = ResolveSchemaBrush(statusText, false)
        };
    }

    private static IBrush ResolveSchemaBrush(string statusText, bool hasWarning)
    {
        if (hasWarning)
            return Brushes.OrangeRed;

        if (statusText.Contains("新增", StringComparison.Ordinal))
            return Brushes.DarkGreen;

        if (statusText.Contains("删除", StringComparison.Ordinal))
            return Brushes.Firebrick;

        if (statusText.Contains("变更", StringComparison.Ordinal) || statusText.Contains("修改", StringComparison.Ordinal))
            return Brushes.DarkGoldenrod;

        return Brushes.Gray;
    }

    private static IBrush ResolveDataBrush(DataDiff diff)
    {
        if (diff.Skipped)
            return Brushes.Gray;

        if (diff.RowsToInsert.Count > 0)
            return Brushes.DarkGreen;

        if (diff.DeletedRows.Count > 0 || diff.ChangedRows.Count > 0)
            return Brushes.Gray;

        return Brushes.Gray;
    }

    private string BuildCompareSummary(SchemaDiff? schemaDiff, IReadOnlyDictionary<string, DataDiff> dataDiffs)
    {
        var added = schemaDiff?.AddedTables.Count ?? 0;
        var removed = schemaDiff?.RemovedTables.Count ?? 0;
        var modified = schemaDiff?.ModifiedTables.Count ?? 0;
        var inserted = dataDiffs.Values.Sum(d => d.RowsToInsert.Count);
        var deleted = dataDiffs.Values.Sum(d => d.DeletedRows.Count);
        var changed = dataDiffs.Values.Sum(d => d.ChangedRows.Count);
        return $"结构：新增 {added}，删除 {removed}，变更 {modified}；数据：新增 {inserted} 行，删除 {deleted} 行，变更 {changed} 行";
    }

    private void SaveCompareHistory(string connectionName)
    {
        _settings = _settings with
        {
            LastConnectionName = connectionName,
            LastSnapshotPath = CompareSnapshotPath
        };
        _appSettingsStore.Save(_settings);
    }

    private DatabaseConnection? CreateSelectedConnection()
    {
        var target = SelectedConnection ?? Connections.FirstOrDefault();
        return target is null
            ? null
            : new DatabaseConnection
            {
                Name = target.Name,
                DbType = target.DbType,
                ConnectionString = target.ConnectionString
            };
    }

    private int ParseRowCountWarningThreshold()
    {
        return int.TryParse(RowCountWarningThresholdText, out var threshold) && threshold > 0
            ? threshold
            : 100_000;
    }

    private void SaveExportHistory(string connectionName, string path)
    {
        _settings = _settings with
        {
            RowCountWarningThreshold = ParseRowCountWarningThreshold(),
            LastConnectionName = connectionName,
            LastExportPath = path
        };
        _appSettingsStore.Save(_settings);
    }

    private static string CreateDefaultExportPath()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(folder))
            folder = Environment.CurrentDirectory;

        return Path.Combine(folder, $"baseline-{DateTime.Now:yyyyMMdd-HHmmss}.dbsync");
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes < 1024 * 1024
            ? $"{bytes / 1024.0:F1} KB"
            : $"{bytes / 1024.0 / 1024.0:F1} MB";
    }

    private async Task<bool> ConfirmLargeExportAsync(ExportTableItemViewModel table)
    {
        if (_ownerWindow is null)
            return true;

        var dialog = new ConfirmLargeExportWindow(table);
        return await dialog.ShowDialog<bool>(_ownerWindow);
    }

    public void AttachOwnerWindow(Window ownerWindow)
    {
        _ownerWindow = ownerWindow;
    }
}

public sealed partial class ConnectionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private DatabaseType dbType;

    [ObservableProperty]
    private string serverAddress;

    public string ConnectionString => $"Server={ServerAddress};";

    public ConnectionItemViewModel(string name, DatabaseType dbType, string serverAddress)
    {
        this.name = name;
        this.dbType = dbType;
        this.serverAddress = serverAddress;
    }
}

public sealed partial class ExportTableItemViewModel : ObservableObject
{
    private readonly TableModel _table;

    [ObservableProperty]
    private bool isSelected = true;

    [ObservableProperty]
    private bool syncData;

    [ObservableProperty]
    private string whereClause = string.Empty;

    public string FullName => _table.FullName;

    public TableModel Table => _table;

    public bool HasPrimaryKey => _table.HasPrimaryKey;

    public string SyncModeText => HasPrimaryKey
        ? SyncData ? "结构+数据" : "结构"
        : "⚠ 无主键，仅结构";

    public ExportTableItemViewModel(TableModel table)
    {
        _table = table;
        EstimatedRowCountText = table.EstimatedRowCount?.ToString() ?? "未知";
        DataSizeText = table.EstimatedDataSizeMb is null ? "未知" : $"{table.EstimatedDataSizeMb:0.##} MB";
    }

    public string EstimatedRowCountText { get; }

    public string DataSizeText { get; }

    public long RowCountWarningThreshold { get; init; } = 100_000;

    public Func<ExportTableItemViewModel, Task<bool>>? ConfirmLargeExportAsync { get; init; }

    private bool _isRevertingSyncData;

    partial void OnSyncDataChanged(bool value)
    {
        if (_isRevertingSyncData)
            return;

        if (value && !HasPrimaryKey)
        {
            SyncData = false;
            return;
        }

        if (value && HasPrimaryKey && (Table.EstimatedRowCount ?? 0) > RowCountWarningThreshold && ConfirmLargeExportAsync is not null)
        {
            _ = ConfirmLargeExportIfNeededAsync();
        }

        OnPropertyChanged(nameof(SyncModeText));
    }

    private async Task ConfirmLargeExportIfNeededAsync()
    {
        var confirmed = await ConfirmLargeExportAsync!(this);
        if (confirmed)
        {
            OnPropertyChanged(nameof(SyncModeText));
            return;
        }

        try
        {
            _isRevertingSyncData = true;
            SyncData = false;
        }
        finally
        {
            _isRevertingSyncData = false;
            OnPropertyChanged(nameof(SyncModeText));
        }
    }
}
