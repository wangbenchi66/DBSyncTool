using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Core.Comparers;
using DBSync.Core.Data;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Core.SqlGenerators;
using DBSync.Desktop.Helpers;
using DBSync.Desktop.Services;
using DBSync.Desktop.Storage;
using DBSync.Desktop.Models;
using System.Collections.ObjectModel;
using System.Text;
using Serilog;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 库对库直连比对的 ViewModel，不经过快照，直接连接两端数据库进行结构和数据比对
///</summary>
public partial class DirectCompareViewModel : ObservableObject, IPageViewModel
{
    /// <summary>
    /// 连接存储服务
    ///</summary>
    private readonly IConnectionStore _connectionStore;

    /// <summary>
    /// 应用设置存储
    ///</summary>
    private readonly IAppSettingsStore _appSettingsStore;

    /// <summary>
    /// 结构读取器
    ///</summary>
    private readonly ISchemaReader _schemaReader;

    /// <summary>
    /// SQL 生成器
    ///</summary>
    private readonly ISqlGenerator _sqlGenerator;

    /// <summary>
    /// 数据指纹生成器
    ///</summary>
    private readonly IDataFingerprinter _fingerprinter;

    /// <summary>
    /// 窗口提供者
    ///</summary>
    private readonly IWindowProvider _windowProvider;

    /// <summary>
    /// 缓存的结构差异
    ///</summary>
    private SchemaDiff? _schemaDiff;

    /// <summary>
    /// 缓存的数据差异
    ///</summary>
    private readonly Dictionary<string, DataDiff> _dataDiffs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 应用设置
    ///</summary>
    private AppSettings _settings;

    /// <summary>
    /// 页面状态文本
    ///</summary>
    [ObservableProperty]
    private string statusText = "就绪";

    /// <summary>
    /// 日志摘要
    ///</summary>
    [ObservableProperty]
    private string logSummary = "";

    /// <summary>
    /// 是否有未完成操作
    ///</summary>
    [ObservableProperty]
    private bool hasPendingOperation;

    /// <summary>
    /// 源库连接
    ///</summary>
    [ObservableProperty]
    private ConnectionItemViewModel? selectedSourceConnection;

    /// <summary>
    /// 目标库连接
    ///</summary>
    [ObservableProperty]
    private ConnectionItemViewModel? selectedTargetConnection;

    /// <summary>
    /// 比对进度百分比
    ///</summary>
    [ObservableProperty]
    private int compareProgress;

    /// <summary>
    /// 比对进度文本
    ///</summary>
    [ObservableProperty]
    private string compareProgressText = "未开始";

    /// <summary>
    /// 比对摘要文本
    ///</summary>
    [ObservableProperty]
    private string compareSummaryText = "请选择源库和目标库后开始比对";

    /// <summary>
    /// 启用事务
    ///</summary>
    [ObservableProperty]
    private bool useTransaction = true;

    /// <summary>
    /// 当前选中的差异项
    ///</summary>
    [ObservableProperty]
    private CompareSchemaNodeViewModel? selectedDiffItem;

    /// <summary>
    /// 选中项的 SQL 差异文本
    ///</summary>
    [ObservableProperty]
    private string selectedDiffSqlText = "";

    /// <summary>
    /// 可用连接列表
    ///</summary>
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = new();

    /// <summary>
    /// 两端不同
    ///</summary>
    public ObservableCollection<CompareSchemaNodeViewModel> DifferentNodes { get; } = new();

    /// <summary>
    /// 仅源库有
    ///</summary>
    public ObservableCollection<CompareSchemaNodeViewModel> OnlySourceNodes { get; } = new();

    /// <summary>
    /// 仅目标库有
    ///</summary>
    public ObservableCollection<CompareSchemaNodeViewModel> OnlyTargetNodes { get; } = new();

    /// <summary>
    /// 全量结构节点（脚本生成用）
    ///</summary>
    public ObservableCollection<CompareSchemaNodeViewModel> AllSchemaNodes { get; } = new();

    /// <summary>
    /// 创建直连比对 ViewModel
    ///</summary>
    public DirectCompareViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore appSettingsStore,
        ISchemaReader schemaReader,
        ISqlGenerator sqlGenerator,
        IDataFingerprinter fingerprinter,
        IWindowProvider windowProvider)
    {
        _connectionStore = connectionStore;
        _appSettingsStore = appSettingsStore;
        _schemaReader = schemaReader;
        _sqlGenerator = sqlGenerator;
        _fingerprinter = fingerprinter;
        _windowProvider = windowProvider;
        _settings = _appSettingsStore.Load();
    }

    /// <summary>
    /// 刷新连接列表
    ///</summary>
    public void RefreshConnections()
    {
        Connections.Clear();
        foreach (var conn in _connectionStore.Load())
            Connections.Add(ConnectionItemViewModel.FromDatabaseConnection(conn));
    }

    /// <summary>
    /// 交换源库和目标库
    ///</summary>
    [RelayCommand]
    private void SwapConnections()
    {
        (SelectedSourceConnection, SelectedTargetConnection) = (SelectedTargetConnection, SelectedSourceConnection);
    }

    /// <summary>
    /// 执行直连比对
    ///</summary>
    [RelayCommand]
    private async Task RunCompareAsync()
    {
        var sourceConn = SelectedSourceConnection?.ToDatabaseConnection();
        var targetConn = SelectedTargetConnection?.ToDatabaseConnection();
        if (sourceConn is null || targetConn is null)
        {
            StatusText = "请先选择源库和目标库连接";
            return;
        }

        try
        {
            StatusText = "正在比对...";
            CompareProgress = 0;
            CompareProgressText = "正在读取源库结构";
            ClearResults();

            var sourceTables = await _schemaReader.ReadAllTablesAsync(sourceConn);
            CompareProgressText = "正在读取目标库结构";
            var targetTables = await _schemaReader.ReadAllTablesAsync(targetConn);

            CompareProgressText = "正在比对结构";
            _schemaDiff = SchemaComparer.Compare(targetTables, sourceTables);
            BuildSchemaPreview(_schemaDiff);

            var sourceTableMap = sourceTables.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);
            var targetTableMap = targetTables.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);
            var commonTables = sourceTableMap.Keys
                .Intersect(targetTableMap.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            for (var i = 0; i < commonTables.Count; i++)
            {
                var tableName = commonTables[i];
                CompareProgress = commonTables.Count == 0 ? 0 : (i + 1) * 100 / commonTables.Count;
                CompareProgressText = $"正在比对数据 {i + 1}/{commonTables.Count}：{tableName}";

                var sourceTable = sourceTableMap[tableName];
                var targetTable = targetTableMap[tableName];

                if (!sourceTable.HasPrimaryKey || !targetTable.HasPrimaryKey)
                {
                    _dataDiffs[tableName] = DataDiff.NoPrimaryKey;
                    continue;
                }

                var sourceRows = new List<RowHash>();
                await foreach (var row in _fingerprinter.ReadRowHashesAsync(sourceConn, sourceTable, cancellationToken: CancellationToken.None))
                    sourceRows.Add(row);

                var targetRows = new List<RowHash>();
                await foreach (var row in _fingerprinter.ReadRowHashesAsync(targetConn, targetTable, cancellationToken: CancellationToken.None))
                    targetRows.Add(row);

                _dataDiffs[tableName] = DataComparer.Compare(targetRows, sourceRows, false);
            }

            CompareProgress = 100;
            CompareProgressText = "比对完成";
            var added = _schemaDiff.AddedTables.Count;
            var removed = _schemaDiff.RemovedTables.Count;
            var modified = _schemaDiff.ModifiedTables.Count;
            var inserted = _dataDiffs.Values.Sum(d => d.RowsToInsert.Count);
            CompareSummaryText = $"结构：新增 {added}，删除 {removed}，变更 {modified}；数据差异行 {inserted}";
            StatusText = "直连比对完成";
            LogSummary = CompareSummaryText;
            HasPendingOperation = true;
        }
        catch (Exception ex)
        {
            StatusText = "比对失败";
            CompareProgressText = "比对失败";
            LogSummary = ex.Message;
            Log.Error(ex, "直连比对失败");
        }
    }

    /// <summary>
    /// 生成升级脚本
    ///</summary>
    [RelayCommand]
    private async Task GenerateScriptAsync()
    {
        if (_schemaDiff is null)
        {
            StatusText = "请先完成比对";
            return;
        }

        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存升级脚本",
            SuggestedFileName = $"Upgrade_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            FileTypeChoices = [new FilePickerFileType("SQL 脚本") { Patterns = ["*.sql"] }]
        });

        var path = file?.TryGetLocalPath();
        if (path is null)
            return;

        try
        {
            StatusText = "正在生成脚本...";
            var dbType = SelectedSourceConnection?.ToDatabaseConnection()?.DbType ?? DatabaseType.MySql;
            var script = _sqlGenerator.GenerateUpgradeScript(dbType, _schemaDiff, _dataDiffs, null, UseTransaction);
            await File.WriteAllTextAsync(path, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusText = "脚本已生成";
            LogSummary = $"已保存：{path}";
        }
        catch (Exception ex)
        {
            StatusText = "脚本生成失败";
            LogSummary = ex.Message;
            Log.Error(ex, "生成升级脚本失败");
        }
    }

    /// <summary>
    /// 选中差异项变化时生成 SQL 预览
    ///</summary>
    partial void OnSelectedDiffItemChanged(CompareSchemaNodeViewModel? value)
    {
        if (value is null || _schemaDiff is null)
        {
            SelectedDiffSqlText = "";
            return;
        }

        try
        {
            var dbType = SelectedSourceConnection?.ToDatabaseConnection()?.DbType ?? DatabaseType.MySql;
            var tableName = value.Title.Split('（')[0].Trim();
            var sb = new StringBuilder();

            var mod = _schemaDiff.ModifiedTables.FirstOrDefault(t => t.SourceTable.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            if (mod is not null)
            {
                sb.AppendLine($"-- {tableName} 结构变更");
                sb.AppendLine(string.Join(Environment.NewLine, _sqlGenerator.GenerateAlterTable(dbType, mod)));
            }

            var added = _schemaDiff.AddedTables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            if (added is not null)
            {
                sb.AppendLine($"-- {tableName} 新增表");
                sb.AppendLine(_sqlGenerator.GenerateCreateTable(dbType, added));
            }

            var removed = _schemaDiff.RemovedTables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            if (removed is not null)
            {
                sb.AppendLine($"-- {tableName} 删除表");
                sb.AppendLine($"DROP TABLE {tableName};");
            }

            SelectedDiffSqlText = sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SelectedDiffSqlText = $"-- 生成 SQL 时出错: {ex.Message}";
        }
    }

    /// <summary>
    /// 清空比对结果
    ///</summary>
    private void ClearResults()
    {
        AllSchemaNodes.Clear();
        DifferentNodes.Clear();
        OnlySourceNodes.Clear();
        OnlyTargetNodes.Clear();
        _schemaDiff = null;
        _dataDiffs.Clear();
        SelectedDiffItem = null;
    }

    /// <summary>
    /// 构建结构差异预览
    ///</summary>
    private void BuildSchemaPreview(SchemaDiff diff)
    {
        foreach (var table in diff.AddedTables.OrderBy(t => t.FullName))
        {
            var node = new CompareSchemaNodeViewModel
            {
                Title = table.FullName,
                StatusText = "新增表",
                IsSelected = true,
                Category = DiffCategory.OnlyTarget,
                StatusBrush = Brushes.DarkGreen
            };
            AllSchemaNodes.Add(node);
            OnlyTargetNodes.Add(node);
        }

        foreach (var table in diff.RemovedTables.OrderBy(t => t.FullName))
        {
            var node = new CompareSchemaNodeViewModel
            {
                Title = table.FullName,
                StatusText = "删除表",
                IsSelected = false,
                Category = DiffCategory.OnlySource,
                StatusBrush = Brushes.Firebrick
            };
            AllSchemaNodes.Add(node);
            OnlySourceNodes.Add(node);
        }

        foreach (var mod in diff.ModifiedTables.OrderBy(t => t.SourceTable.FullName))
        {
            var node = new CompareSchemaNodeViewModel
            {
                Title = mod.SourceTable.FullName,
                StatusText = $"结构变更（{mod.ColumnDiffs.Count} 列，{mod.IndexDiffs.Count} 索引）",
                IsSelected = true,
                Category = DiffCategory.Different,
                StatusBrush = Brushes.DarkGoldenrod
            };
            AllSchemaNodes.Add(node);
            DifferentNodes.Add(node);
        }
    }
}
