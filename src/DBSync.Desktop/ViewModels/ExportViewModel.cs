using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Core.Snapshot;
using DBSync.Desktop.Helpers;
using DBSync.Desktop.Models;
using DBSync.Desktop.Services;
using DBSync.Desktop.Views;
using System.Collections.ObjectModel;
using Serilog;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 导出快照功能的视图模型，从 MainWindowViewModel 提取而来，
/// 负责表结构读取、表筛选、快照导出及进度跟踪
///</summary>
public partial class ExportViewModel : ObservableObject, IPageViewModel
{
    /// <summary>
    /// 连接存储服务，用于加载和保存数据库连接列表
    ///</summary>
    private readonly IConnectionStore _connectionStore;

    /// <summary>
    /// 应用设置存储服务，用于持久化行数阈值、导出路径等配置
    ///</summary>
    private readonly IAppSettingsStore _appSettingsStore;

    /// <summary>
    /// 数据库结构读取器，用于读取表结构元数据
    ///</summary>
    private readonly ISchemaReader _schemaReader;

    /// <summary>
    /// 快照导出器，用于将表结构和数据指纹写入 .dbsync 文件
    ///</summary>
    private readonly ISnapshotExporter _snapshotExporter;

    /// <summary>
    /// 窗口提供器，用于获取主窗口以显示对话框
    ///</summary>
    private readonly IWindowProvider _windowProvider;

    /// <summary>
    /// 导出取消令牌源，用于支持用户取消导出操作
    ///</summary>
    private CancellationTokenSource? _exportCancellation;

    /// <summary>
    /// 当前应用设置快照，变更后重新赋值并持久化
    ///</summary>
    private AppSettings _settings;

    private bool _suppressDefaultFileNameUpdate;

    /// <summary>
    /// 当前状态栏文本
    ///</summary>
    [ObservableProperty]
    private string statusText = "就绪";

    /// <summary>
    /// 操作日志摘要，用于在界面上显示最近一次操作的结果描述
    ///</summary>
    [ObservableProperty]
    private string logSummary = "";

    /// <summary>
    /// 当前选中的数据库连接
    ///</summary>
    [ObservableProperty]
    private ConnectionItemViewModel? selectedConnection;

    /// <summary>
    /// 表名过滤关键字，输入后自动触发筛选
    ///</summary>
    [ObservableProperty]
    private string tableFilter = string.Empty;

    /// <summary>
    /// 快照加密密码
    ///</summary>
    [ObservableProperty]
    private string exportPassword = string.Empty;

    /// <summary>
    /// 快照加密密码确认（须与 ExportPassword 一致）
    ///</summary>
    [ObservableProperty]
    private string exportPasswordConfirm = string.Empty;

    /// <summary>
    /// 密码提示文本，会明文写入快照 manifest.json
    ///</summary>
    [ObservableProperty]
    private string passwordHint = string.Empty;

    /// <summary>
    /// 导出目录，通过系统目录选择器设置
    ///</summary>
    [ObservableProperty]
    private string exportDirectory = string.Empty;

    /// <summary>
    /// 导出文件名
    ///</summary>
    [ObservableProperty]
    private string exportFileName = string.Empty;

    /// <summary>
    /// 导出文件的完整路径
    ///</summary>
    [ObservableProperty]
    private string exportPath = string.Empty;

    /// <summary>
    /// 导出进度百分比（0-100）
    ///</summary>
    [ObservableProperty]
    private int exportProgress;

    /// <summary>
    /// 导出进度描述文本，例如"正在导出 3/10：Users，当前行 5000"
    ///</summary>
    [ObservableProperty]
    private string exportProgressText = "未开始";

    /// <summary>
    /// 导出完成后的摘要信息，包含文件路径、大小和表数量
    ///</summary>
    [ObservableProperty]
    private string exportSummary = "尚未导出";

    /// <summary>
    /// 是否正在执行导出操作
    ///</summary>
    [ObservableProperty]
    private bool isExporting;

    /// <summary>
    /// 是否有待处理的操作（加载表、导出等）
    ///</summary>
    [ObservableProperty]
    private bool hasPendingOperation;

    /// <summary>
    /// 行数警告阈值文本，用于界面双向绑定
    ///</summary>
    [ObservableProperty]
    private string rowCountWarningThresholdText = "100000";

    /// <summary>
    /// 是否全选当前筛选列表中的表
    ///</summary>
    private bool? areAllFilteredTablesSelected;

    /// <summary>
    /// 可用的数据库连接列表
    ///</summary>
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = new();

    /// <summary>
    /// 从数据库读取到的全部表列表（未筛选）
    ///</summary>
    public ObservableCollection<ExportTableItemViewModel> ExportTables { get; } = new();

    /// <summary>
    /// 按 TableFilter 筛选后的表列表，绑定到界面展示
    ///</summary>
    public ObservableCollection<ExportTableItemViewModel> FilteredExportTables { get; } = new();

    /// <summary>
    /// 当前筛选列表是否全选。true 表示全选，false 表示全不选，null 表示部分选中。
    ///</summary>
    public bool? AreAllFilteredTablesSelected
    {
        get => areAllFilteredTablesSelected;
        set
        {
            if (SetProperty(ref areAllFilteredTablesSelected, value))
            {
                if (value is bool selected)
                {
                    foreach (var table in FilteredExportTables)
                        table.IsSelected = selected;
                }
            }
        }
    }

    /// <summary>
    /// 初始化导出视图模型，加载设置和连接列表
    ///</summary>
    /// <param name="connectionStore">连接存储服务</param>
    /// <param name="schemaReader">数据库结构读取器</param>
    /// <param name="snapshotExporter">快照导出器</param>
    /// <param name="appSettingsStore">应用设置存储服务</param>
    /// <param name="windowProvider">窗口提供器</param>
    public ExportViewModel(
        IConnectionStore connectionStore,
        ISchemaReader schemaReader,
        ISnapshotExporter snapshotExporter,
        IAppSettingsStore appSettingsStore,
        IWindowProvider windowProvider)
    {
        _connectionStore = connectionStore;
        _schemaReader = schemaReader;
        _snapshotExporter = snapshotExporter;
        _appSettingsStore = appSettingsStore;
        _windowProvider = windowProvider;

        _settings = _appSettingsStore.Load();
        RowCountWarningThresholdText = _settings.RowCountWarningThreshold.ToString();
        _suppressDefaultFileNameUpdate = true;
        try
        {
            RefreshConnections();
            SetExportPath(_settings.LastExportPath ?? CreateDefaultExportPath(SelectedConnection?.Name));
        }
        finally
        {
            _suppressDefaultFileNameUpdate = false;
        }
    }

    /// <summary>
    /// 读取选中数据库连接的所有表结构，填充到导出表列表
    ///</summary>
    [RelayCommand]
    private async Task LoadTablesAsync()
    {
        var connection = SelectedConnection?.ToDatabaseConnection();
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
                var item = new ExportTableItemViewModel(table)
                {
                    RowCountWarningThreshold = ParseRowCountWarningThreshold(),
                    ConfirmLargeExportAsync = ConfirmLargeExportAsync
                };
                item.PropertyChanged += OnExportTableItemPropertyChanged;
                ExportTables.Add(item);
            }

            ApplyTableFilter();
            RefreshSelectAllState();
            StatusText = $"已加载 {ExportTables.Count} 张表";
            LogSummary = "请选择需要写入快照的表。";
            HasPendingOperation = true;
        }
        catch (Exception ex)
        {
            StatusText = "读取表结构失败";
            LogSummary = ex.Message;
            Log.Error(ex, "加载表失败");
        }
    }

    /// <summary>
    /// 全选当前筛选列表中的所有表
    ///</summary>
    [RelayCommand]
    private void SelectAllTables()
    {
        foreach (var table in FilteredExportTables)
            table.IsSelected = true;

        RefreshSelectAllState();
    }

    /// <summary>
    /// 反选当前筛选列表中所有表的选中状态
    ///</summary>
    [RelayCommand]
    private void InvertTableSelection()
    {
        foreach (var table in FilteredExportTables)
            table.IsSelected = !table.IsSelected;

        RefreshSelectAllState();
    }

    /// <summary>
    /// 当前筛选列表中的有主键表全部设置为结构+数据
    ///</summary>
    [RelayCommand]
    private void SelectAllDataModes()
    {
        foreach (var table in FilteredExportTables.Where(t => t.HasPrimaryKey))
            table.SyncData = true;
    }

    /// <summary>
    /// 当前筛选列表中的所有表全部设置为仅结构
    ///</summary>
    [RelayCommand]
    private void SelectSchemaOnlyModes()
    {
        foreach (var table in FilteredExportTables)
            table.SyncData = false;
    }

    /// <summary>
    /// 选择快照导出目录
    ///</summary>
    [RelayCommand]
    private async Task BrowseExportDirectoryAsync()
    {
        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择导出目录",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            ExportDirectory = path;
    }

    /// <summary>
    /// 执行快照导出，包含密码验证、文件创建、进度跟踪和历史保存
    ///</summary>
    [RelayCommand]
    private async Task ExportSnapshotAsync()
    {
        var connection = SelectedConnection?.ToDatabaseConnection();
        var exportSource = string.IsNullOrWhiteSpace(TableFilter) ? ExportTables : FilteredExportTables;
        var selectedTables = exportSource.Where(t => t.IsSelected).ToList();

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

        if (!await ShowExportOptionsAsync())
            return;

        if (!string.Equals(ExportPassword, ExportPasswordConfirm, StringComparison.Ordinal))
        {
            StatusText = "两次输入的密码不一致";
            return;
        }

        var exportPath = BuildExportPath();
        if (exportPath is null)
        {
            StatusText = "请选择导出目录并填写文件名";
            return;
        }

        ExportPath = exportPath;

        var directory = Path.GetDirectoryName(exportPath);
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
                Password = ExportPassword ?? string.Empty,
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

            await using var stream = File.Create(exportPath);
            await _snapshotExporter.ExportAsync(connection, options, stream, progress, _exportCancellation.Token);

            var fileSize = new FileInfo(exportPath).Length;
            ExportProgress = 100;
            ExportProgressText = "导出完成";
            ExportSummary = $"文件：{exportPath}；大小：{FormatFileSize(fileSize)}；表数量：{selectedTables.Count}";
            StatusText = "导出完成";
            LogSummary = ExportSummary;
            SaveExportHistory(connection.Name, exportPath);
            SaveRecentHistory("导出快照", Path.GetFileName(exportPath), exportPath, connection.Name);
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
            Log.Error(ex, "导出快照失败");
        }
        finally
        {
            IsExporting = false;
            _exportCancellation.Dispose();
            _exportCancellation = null;
        }
    }

    /// <summary>
    /// 取消正在进行的导出操作
    ///</summary>
    [RelayCommand]
    private void CancelExport()
    {
        _exportCancellation?.Cancel();
    }

    /// <summary>
    /// 保存行数警告阈值设置到持久化存储
    ///</summary>
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

    /// <summary>
    /// 按 TableFilter 关键字过滤 ExportTables，结果写入 FilteredExportTables
    ///</summary>
    private void ApplyTableFilter()
    {
        FilteredExportTables.Clear();
        var filtered = string.IsNullOrWhiteSpace(TableFilter)
            ? ExportTables
            : ExportTables.Where(t =>
                t.FullName.Contains(TableFilter, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(t.Comment) && t.Comment.Contains(TableFilter, StringComparison.OrdinalIgnoreCase)));

        foreach (var table in filtered)
            FilteredExportTables.Add(table);

        RefreshSelectAllState();
    }

    /// <summary>
    /// TableFilter 属性变更时由源生成器自动调用，触发表筛选
    ///</summary>
    /// <param name="value">新的筛选关键字</param>
    partial void OnTableFilterChanged(string value)
    {
        ApplyTableFilter();
    }

    partial void OnSelectedConnectionChanged(ConnectionItemViewModel? value)
    {
        if (_suppressDefaultFileNameUpdate)
            return;

        ExportFileName = CreateDefaultFileName(value?.Name);
    }

    partial void OnExportDirectoryChanged(string value)
    {
        UpdateExportPath();
    }

    partial void OnExportFileNameChanged(string value)
    {
        UpdateExportPath();
    }

    /// <summary>
    /// 解析行数警告阈值文本为整数，解析失败时返回默认值 100000
    ///</summary>
    /// <returns>解析后的阈值整数</returns>
    private int ParseRowCountWarningThreshold()
    {
        return int.TryParse(RowCountWarningThresholdText, out var threshold) && threshold > 0
            ? threshold
            : 100_000;
    }

    /// <summary>
    /// 生成默认的导出文件路径（桌面目录 + 时间戳文件名）
    ///</summary>
    /// <returns>默认导出文件的完整路径</returns>
    private static string CreateDefaultExportPath(string? connectionName)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(folder))
            folder = Environment.CurrentDirectory;

        return Path.Combine(folder, CreateDefaultFileName(connectionName));
    }

    private static string CreateDefaultFileName(string? connectionName)
    {
        var prefix = string.IsNullOrWhiteSpace(connectionName) ? "snapshot" : SanitizeFileName(connectionName);
        return $"{prefix}-{DateTime.Now:yyyyMMddHHmmssfff}.dbsync";
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", value.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    public void SetExportPath(string path)
    {
        ExportPath = path;
        ExportDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        ExportFileName = Path.GetFileName(path);
    }

    private string? BuildExportPath()
    {
        if (string.IsNullOrWhiteSpace(ExportDirectory) || string.IsNullOrWhiteSpace(ExportFileName))
            return null;

        var fileName = Path.GetExtension(ExportFileName).Equals(".dbsync", StringComparison.OrdinalIgnoreCase)
            ? ExportFileName
            : $"{ExportFileName}.dbsync";

        return Path.Combine(ExportDirectory, fileName);
    }

    private void UpdateExportPath()
    {
        var path = BuildExportPath();
        if (path is not null)
            ExportPath = path;
    }

    /// <summary>
    /// 表项勾选状态变化时刷新表头全选状态
    ///</summary>
    /// <param name="sender">表项对象</param>
    /// <param name="e">属性变化参数</param>
    private void OnExportTableItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExportTableItemViewModel.IsSelected))
            RefreshSelectAllState();
    }

    /// <summary>
    /// 刷新表头全选状态
    ///</summary>
    private void RefreshSelectAllState()
    {
        if (FilteredExportTables.Count == 0)
        {
            areAllFilteredTablesSelected = false;
            OnPropertyChanged(nameof(AreAllFilteredTablesSelected));
            return;
        }

        var selectedCount = FilteredExportTables.Count(t => t.IsSelected);
        areAllFilteredTablesSelected = selectedCount switch
        {
            0 => false,
            var count when count == FilteredExportTables.Count => true,
            _ => null
        };
        OnPropertyChanged(nameof(AreAllFilteredTablesSelected));
    }

    /// <summary>
    /// 将字节数格式化为可读的文件大小字符串（KB 或 MB）
    ///</summary>
    /// <param name="bytes">文件字节数</param>
    /// <returns>格式化后的大小字符串</returns>
    private static string FormatFileSize(long bytes)
    {
        return ViewModelHelpers.FormatFileSize(bytes);
    }

    /// <summary>
    /// 当表的行数超过警告阈值时，通过模态对话框让用户确认是否继续导出
    ///</summary>
    /// <param name="table">触发警告的导出表视图模型</param>
    /// <returns>用户确认返回 true，取消返回 false</returns>
    private async Task<bool> ConfirmLargeExportAsync(ExportTableItemViewModel table)
    {
        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return true;

        var dialog = new ConfirmLargeExportWindow(table);
        return await dialog.ShowDialog<bool>(window);
    }

    /// <summary>
    /// 打开导出输出设置弹窗
    ///</summary>
    /// <returns>用户确认则返回 true，取消返回 false</returns>
    private async Task<bool> ShowExportOptionsAsync()
    {
        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return true;

        var dialog = new ExportOptionsWindow(this);
        return await dialog.ShowDialog<bool>(window);
    }

    /// <summary>
    /// 保存导出历史（连接名、路径、阈值）到应用设置
    ///</summary>
    /// <param name="connectionName">本次导出使用的连接名称</param>
    /// <param name="path">导出文件路径</param>
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

    /// <summary>
    /// 保存一条最近操作历史记录，保留最新 20 条并去重
    ///</summary>
    /// <param name="kind">操作类型（如"导出快照"）</param>
    /// <param name="title">显示标题</param>
    /// <param name="path">关联文件路径</param>
    /// <param name="connectionName">关联的连接名称（可选）</param>
    private void SaveRecentHistory(string kind, string title, string path, string? connectionName = null)
    {
        _settings = ViewModelHelpers.SaveRecentHistory(_appSettingsStore, _settings, kind, title, path, connectionName);
    }

    /// <summary>
    /// 从 IConnectionStore 重新加载连接列表，页面切换时调用以保持数据同步
    ///</summary>
    public void RefreshConnections()
    {
        var previousSelection = SelectedConnection?.Name;
        Connections.Clear();

        foreach (var conn in _connectionStore.Load())
            Connections.Add(ConnectionItemViewModel.FromDatabaseConnection(conn));

        SelectedConnection = Connections.FirstOrDefault(c =>
            string.Equals(c.Name, previousSelection, StringComparison.OrdinalIgnoreCase))
            ?? Connections.FirstOrDefault();
    }
}
