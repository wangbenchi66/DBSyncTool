using Avalonia;
using Avalonia.Controls;
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
    private Window? _ownerWindow;
    private CancellationTokenSource? _exportCancellation;
    private AppSettings _settings;

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
    private string rowCountWarningThresholdText = "100000";

    [ObservableProperty]
    private bool hasPendingOperation;

    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = new();

    public ObservableCollection<ExportTableItemViewModel> ExportTables { get; } = new();

    public ObservableCollection<ExportTableItemViewModel> FilteredExportTables { get; } = new();

    public MainWindowViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore appSettingsStore,
        ISchemaReader schemaReader,
        ISnapshotExporter snapshotExporter)
    {
        _connectionStore = connectionStore;
        _appSettingsStore = appSettingsStore;
        _schemaReader = schemaReader;
        _snapshotExporter = snapshotExporter;
        _settings = _appSettingsStore.Load();
        RowCountWarningThresholdText = _settings.RowCountWarningThreshold.ToString();
        ExportPath = _settings.LastExportPath ?? CreateDefaultExportPath();
        LoadConnections();
        SelectedConnection = Connections.FirstOrDefault(c =>
            string.Equals(c.Name, _settings.LastConnectionName, StringComparison.OrdinalIgnoreCase))
            ?? Connections.FirstOrDefault();
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
    }

    private void Save()
    {
        _connectionStore.Save(Connections.ToList());
        HasPendingOperation = false;
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
