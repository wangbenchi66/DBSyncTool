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
    /// 缓存的源库表结构
    ///</summary>
    private Dictionary<string, TableModel> _sourceTableMap = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 应用设置
    ///</summary>
    private AppSettings _settings;

    /// <summary>
    /// 抑制节点刷新的标志
    ///</summary>
    private bool _suppressNodeRefresh;

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
    /// 是否正在加载源库数据库列表
    ///</summary>
    [ObservableProperty]
    private bool isLoadingSourceDatabases;

    /// <summary>
    /// 是否正在加载目标库数据库列表
    ///</summary>
    [ObservableProperty]
    private bool isLoadingTargetDatabases;

    /// <summary>
    /// 源库选中的数据库名称
    ///</summary>
    [ObservableProperty]
    private string? selectedSourceDatabaseName;

    /// <summary>
    /// 目标库选中的数据库名称
    ///</summary>
    [ObservableProperty]
    private string? selectedTargetDatabaseName;

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
    /// INSERT 语句是否排除自增列
    ///</summary>
    [ObservableProperty]
    private bool excludeIdentityColumns;

    /// <summary>
    /// 源库表名过滤关键字
    ///</summary>
    [ObservableProperty]
    private string sourceTableFilter = string.Empty;

    /// <summary>
    /// 目标库表名过滤关键字
    ///</summary>
    [ObservableProperty]
    private string targetTableFilter = string.Empty;

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
    /// 当前差异分类 Tab 索引
    ///</summary>
    [ObservableProperty]
    private int selectedDiffTabIndex;

    /// <summary>
    /// 当前 Tab 对应的差异节点集合
    ///</summary>
    [ObservableProperty]
    private ObservableCollection<CompareSchemaNodeViewModel> currentDiffNodes = new();

    /// <summary>
    /// 全局开关：是否生成结构变更语句
    ///</summary>
    [ObservableProperty]
    private bool globalGenerateSchema = true;

    /// <summary>
    /// 全局开关：是否生成 INSERT 语句
    ///</summary>
    [ObservableProperty]
    private bool globalGenerateInsert;

    /// <summary>
    /// 全局开关：是否生成 DELETE 语句
    ///</summary>
    [ObservableProperty]
    private bool globalGenerateDelete;

    /// <summary>
    /// 全局开关：是否生成 UPDATE 语句
    ///</summary>
    [ObservableProperty]
    private bool globalGenerateUpdate;

    /// <summary>
    /// 可用连接列表
    ///</summary>
    public ObservableCollection<ConnectionItemViewModel> Connections { get; } = new();

    /// <summary>
    /// 源库所有可用数据库名称
    ///</summary>
    public ObservableCollection<string> AllSourceDatabases { get; } = new();

    /// <summary>
    /// 目标库所有可用数据库名称
    ///</summary>
    public ObservableCollection<string> AllTargetDatabases { get; } = new();

    /// <summary>
    /// 源库表选择列表
    ///</summary>
    public ObservableCollection<CompareTableSelectionViewModel> SourceTables { get; } = new();

    /// <summary>
    /// 目标库表选择列表
    ///</summary>
    public ObservableCollection<CompareTableSelectionViewModel> TargetTables { get; } = new();

    /// <summary>
    /// 源库过滤后的表列表
    ///</summary>
    public ObservableCollection<CompareTableSelectionViewModel> FilteredSourceTables { get; } = new();

    /// <summary>
    /// 目标库过滤后的表列表
    ///</summary>
    public ObservableCollection<CompareTableSelectionViewModel> FilteredTargetTables { get; } = new();

    /// <summary>
    /// 全部差异节点
    ///</summary>
    public ObservableCollection<CompareSchemaNodeViewModel> AllDiffSchemaNodes { get; } = new();

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
    /// 数据差异节点
    ///</summary>
    public ObservableCollection<CompareSchemaNodeViewModel> DataDiffNodes { get; } = new();

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

    partial void OnSelectedSourceConnectionChanged(ConnectionItemViewModel? value)
    {
        _ = LoadDatabasesAsync(value, AllSourceDatabases, isSource: true);
    }

    partial void OnSelectedTargetConnectionChanged(ConnectionItemViewModel? value)
    {
        _ = LoadDatabasesAsync(value, AllTargetDatabases, isSource: false);
    }

    private async Task LoadDatabasesAsync(
        ConnectionItemViewModel? connVm,
        ObservableCollection<string> allDatabases,
        bool isSource)
    {
        allDatabases.Clear();
        if (isSource) SelectedSourceDatabaseName = null;
        else SelectedTargetDatabaseName = null;

        var conn = connVm?.ToDatabaseConnection();
        if (conn is null || conn.DbType == DatabaseType.Sqlite)
            return;

        try
        {
            if (isSource) IsLoadingSourceDatabases = true;
            else IsLoadingTargetDatabases = true;

            var databases = await _schemaReader.ListDatabasesAsync(conn);
            foreach (var db in databases)
                allDatabases.Add(db);

            var defaultDb = allDatabases.FirstOrDefault(
                db => string.Equals(db, connVm?.Database, StringComparison.OrdinalIgnoreCase))
                ?? allDatabases.FirstOrDefault();
            if (isSource) SelectedSourceDatabaseName = defaultDb;
            else SelectedTargetDatabaseName = defaultDb;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "获取数据库列表失败");
        }
        finally
        {
            if (isSource) IsLoadingSourceDatabases = false;
            else IsLoadingTargetDatabases = false;
        }
    }

    partial void OnSelectedSourceDatabaseNameChanged(string? value)
    {
        if (SelectedSourceConnection is not null && !string.IsNullOrEmpty(value))
            SelectedSourceConnection.Database = value;
    }

    partial void OnSelectedTargetDatabaseNameChanged(string? value)
    {
        if (SelectedTargetConnection is not null && !string.IsNullOrEmpty(value))
            SelectedTargetConnection.Database = value;
    }

    partial void OnSourceTableFilterChanged(string value) => ApplyTableFilter(SourceTables, FilteredSourceTables, value);
    partial void OnTargetTableFilterChanged(string value) => ApplyTableFilter(TargetTables, FilteredTargetTables, value);

    /// <summary>
    /// 加载源库和目标库的表列表
    ///</summary>
    [RelayCommand]
    private async Task LoadTablesAsync()
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
            StatusText = "正在加载表...";
            SourceTables.Clear();
            TargetTables.Clear();

            var sourceTables = await _schemaReader.ReadAllTablesAsync(sourceConn);
            foreach (var t in sourceTables.OrderBy(t => t.FullName))
                SourceTables.Add(new CompareTableSelectionViewModel
                {
                    Table = t,
                    IsSelected = false,
                    IsSelectedChangedCallback = OnSourceTableSelectionChanged
                });
            ApplyTableFilter(SourceTables, FilteredSourceTables, SourceTableFilter);

            var targetTables = await _schemaReader.ReadAllTablesAsync(targetConn);
            foreach (var t in targetTables.OrderBy(t => t.FullName))
                TargetTables.Add(new CompareTableSelectionViewModel { Table = t, IsSelected = false });
            ApplyTableFilter(TargetTables, FilteredTargetTables, TargetTableFilter);

            StatusText = $"已加载源库 {SourceTables.Count} 张表，目标库 {TargetTables.Count} 张表";
        }
        catch (Exception ex)
        {
            StatusText = $"加载表失败：{ex.Message}";
            Log.Error(ex, "加载表失败");
        }
    }

    [RelayCommand]
    private void SelectAllSourceTables()
    {
        foreach (var t in FilteredSourceTables) t.IsSelected = true;
    }

    [RelayCommand]
    private void InvertSourceTables()
    {
        foreach (var t in FilteredSourceTables) t.IsSelected = !t.IsSelected;
    }

    [RelayCommand]
    private void SelectAllTargetTables()
    {
        foreach (var t in FilteredTargetTables) t.IsSelected = true;
    }

    [RelayCommand]
    private void InvertTargetTables()
    {
        foreach (var t in FilteredTargetTables) t.IsSelected = !t.IsSelected;
    }

    /// <summary>
    /// 源库表全部设为结构+数据
    ///</summary>
    [RelayCommand]
    private void SetSourceAllData()
    {
        foreach (var t in FilteredSourceTables) t.CompareData = true;
    }

    /// <summary>
    /// 源库表全部设为仅结构
    ///</summary>
    [RelayCommand]
    private void SetSourceSchemaOnly()
    {
        foreach (var t in FilteredSourceTables) t.CompareData = false;
    }

    /// <summary>
    /// 源库表勾选变更时联动目标库同名表
    ///</summary>
    private void OnSourceTableSelectionChanged(CompareTableSelectionViewModel source)
    {
        var target = TargetTables.FirstOrDefault(t =>
            string.Equals(t.FullName, source.FullName, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
            target.IsSelected = source.IsSelected;
    }

    private static void ApplyTableFilter(
        ObservableCollection<CompareTableSelectionViewModel> source,
        ObservableCollection<CompareTableSelectionViewModel> filtered,
        string filter)
    {
        filtered.Clear();
        var items = string.IsNullOrWhiteSpace(filter)
            ? source
            : source.Where(t => t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        foreach (var item in items)
            filtered.Add(item);
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

            IReadOnlyList<TableModel> sourceTables;
            IReadOnlyList<TableModel> targetTables;

            // 如果已加载表且有勾选，则只比对勾选的表
            if (SourceTables.Count > 0 && TargetTables.Count > 0)
            {
                var selectedSourceNames = new HashSet<string>(
                    SourceTables.Where(t => t.IsSelected).Select(t => t.FullName), StringComparer.OrdinalIgnoreCase);
                var selectedTargetNames = new HashSet<string>(
                    TargetTables.Where(t => t.IsSelected).Select(t => t.FullName), StringComparer.OrdinalIgnoreCase);
                sourceTables = SourceTables.Where(t => selectedSourceNames.Contains(t.FullName)).Select(t => t.Table).ToList();
                targetTables = TargetTables.Where(t => selectedTargetNames.Contains(t.FullName)).Select(t => t.Table).ToList();
            }
            else
            {
                sourceTables = await _schemaReader.ReadAllTablesAsync(sourceConn);
                CompareProgressText = "正在读取目标库结构";
                targetTables = await _schemaReader.ReadAllTablesAsync(targetConn);
            }

            _sourceTableMap = sourceTables.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);
            var targetTableMap = targetTables.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);

            CompareProgressText = "正在比对结构";
            _schemaDiff = SchemaComparer.Compare(targetTables, sourceTables);

            var commonTables = _sourceTableMap.Keys
                .Intersect(targetTableMap.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            // 确定哪些表需要比对数据
            var dataCompareNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (SourceTables.Count > 0)
            {
                foreach (var t in SourceTables.Where(t => t.IsSelected && t.CompareData))
                    dataCompareNames.Add(t.FullName);
            }

            var tablesToCompareData = commonTables.Where(t => dataCompareNames.Count == 0 || dataCompareNames.Contains(t)).ToList();

            for (var i = 0; i < tablesToCompareData.Count; i++)
            {
                var tableName = tablesToCompareData[i];
                CompareProgress = tablesToCompareData.Count == 0 ? 0 : (i + 1) * 100 / tablesToCompareData.Count;
                CompareProgressText = $"正在比对数据 {i + 1}/{tablesToCompareData.Count}：{tableName}";

                var sourceTable = _sourceTableMap[tableName];
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

            BuildSchemaPreview(_schemaDiff);
            MergeAllDiffNodes();
            RefreshCurrentDiffNodes();

            CompareProgress = 100;
            CompareProgressText = "比对完成";
            var added = _schemaDiff.AddedTables.Count;
            var removed = _schemaDiff.RemovedTables.Count;
            var modified = _schemaDiff.ModifiedTables.Count;
            var dataOnlyCount = DataDiffNodes.Count;
            CompareSummaryText = $"结构：新增 {added}，删除 {removed}，变更 {modified}；数据差异 {dataOnlyCount} 表";
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
            var script = BuildUpgradeScriptFromNodes(dbType);
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
    /// 差异分类 Tab 切换时刷新列表
    ///</summary>
    partial void OnSelectedDiffTabIndexChanged(int value)
    {
        RefreshCurrentDiffNodes();
        BuildCategoryDiffSql();
    }

    /// <summary>
    /// 选中差异项变化时生成 SQL 预览
    ///</summary>
    partial void OnSelectedDiffItemChanged(CompareSchemaNodeViewModel? value)
    {
        if (value is null || _schemaDiff is null)
        {
            BuildCategoryDiffSql();
            return;
        }

        try
        {
            var dbType = SelectedSourceConnection?.ToDatabaseConnection()?.DbType ?? DatabaseType.MySql;
            var tableName = value.Title.Split('（')[0].Trim();
            var sb = new StringBuilder();

            if (UseTransaction)
            {
                sb.AppendLine(GetTransactionBegin(dbType));
                sb.AppendLine();
            }

            if (value.GenerateSchema)
            {
                var mod = _schemaDiff.ModifiedTables.FirstOrDefault(t => t.SourceTable.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (mod is not null)
                {
                    sb.AppendLine($"-- {tableName} 结构变更");
                    sb.AppendLine(string.Join(Environment.NewLine, _sqlGenerator.GenerateAlterTable(dbType, mod)));
                    sb.AppendLine();
                }

                var added = _schemaDiff.AddedTables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (added is not null)
                {
                    sb.AppendLine($"-- {tableName} 新增表");
                    sb.AppendLine(_sqlGenerator.GenerateCreateTable(dbType, added));
                    sb.AppendLine();
                }

                var removed = _schemaDiff.RemovedTables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (removed is not null)
                {
                    sb.AppendLine($"-- {tableName} 删除表");
                    sb.AppendLine(_sqlGenerator.GenerateDropTable(dbType, removed));
                    sb.AppendLine();
                }
            }

            AppendDataDml(sb, dbType, tableName, value);

            if (UseTransaction)
                sb.AppendLine(GetTransactionEnd(dbType));

            SelectedDiffSqlText = sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SelectedDiffSqlText = $"-- 生成 SQL 时出错: {ex.Message}";
        }
    }

    partial void OnUseTransactionChanged(bool value) => RefreshDiffSql();
    partial void OnExcludeIdentityColumnsChanged(bool value) => RefreshDiffSql();
    partial void OnGlobalGenerateSchemaChanged(bool value) { SyncGlobalToNodes(n => n.GenerateSchema = value); BuildCategoryDiffSql(); }
    partial void OnGlobalGenerateInsertChanged(bool value) { SyncGlobalToNodes(n => n.GenerateInsert = value); BuildCategoryDiffSql(); }
    partial void OnGlobalGenerateDeleteChanged(bool value) { SyncGlobalToNodes(n => n.GenerateDelete = value); BuildCategoryDiffSql(); }
    partial void OnGlobalGenerateUpdateChanged(bool value) { SyncGlobalToNodes(n => n.GenerateUpdate = value); BuildCategoryDiffSql(); }

    /// <summary>
    /// 强制刷新 SQL 预览
    ///</summary>
    public void RefreshDiffSql()
    {
        if (_suppressNodeRefresh)
            return;

        if (SelectedDiffItem is not null && CurrentDiffNodes.Contains(SelectedDiffItem))
            OnSelectedDiffItemChanged(SelectedDiffItem);
        else
            BuildCategoryDiffSql();
    }

    /// <summary>
    /// 点击分类 Tab 时调用：清除单行选中，重新渲染当前分类所有勾选差异的 SQL
    ///</summary>
    public void ShowCategoryDiffSql()
    {
        SelectedDiffItem = null;
        BuildCategoryDiffSql();
    }

    /// <summary>
    /// 构建当前分类全部选中节点的 SQL 预览
    ///</summary>
    private void BuildCategoryDiffSql()
    {
        if (_schemaDiff is null)
        {
            SelectedDiffSqlText = string.Empty;
            return;
        }

        try
        {
            var dbType = SelectedSourceConnection?.ToDatabaseConnection()?.DbType ?? DatabaseType.MySql;
            var sb = new StringBuilder();

            if (UseTransaction)
            {
                sb.AppendLine(GetTransactionBegin(dbType));
                sb.AppendLine();
            }

            var processedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in CurrentDiffNodes.Where(n => n.IsSelected))
            {
                var tableName = node.Title.Split('（')[0].Trim();
                if (!processedTables.Add(tableName)) continue;

                if (node.GenerateSchema)
                {
                    var mod = _schemaDiff.ModifiedTables.FirstOrDefault(t => t.SourceTable.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                    if (mod is not null)
                    {
                        sb.AppendLine($"-- {tableName} 结构变更");
                        sb.AppendLine(string.Join(Environment.NewLine, _sqlGenerator.GenerateAlterTable(dbType, mod)));
                        sb.AppendLine();
                    }

                    var added = _schemaDiff.AddedTables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                    if (added is not null)
                    {
                        sb.AppendLine($"-- {tableName} 新增表");
                        sb.AppendLine(_sqlGenerator.GenerateCreateTable(dbType, added));
                        sb.AppendLine();
                    }

                    var removed = _schemaDiff.RemovedTables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                    if (removed is not null)
                    {
                        sb.AppendLine($"-- {tableName} 删除表");
                        sb.AppendLine(_sqlGenerator.GenerateDropTable(dbType, removed));
                        sb.AppendLine();
                    }
                }

                AppendDataDml(sb, dbType, tableName, node);
            }

            if (UseTransaction)
                sb.AppendLine(GetTransactionEnd(dbType));

            SelectedDiffSqlText = sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SelectedDiffSqlText = $"-- 生成 SQL 时出错: {ex.Message}";
        }
    }

    /// <summary>
    /// 根据 UI 节点的勾选状态组装完整的升级脚本
    ///</summary>
    private string BuildUpgradeScriptFromNodes(DatabaseType dbType)
    {
        var sb = new StringBuilder();
        var selectedNodes = AllDiffSchemaNodes.Where(n => n.IsSelected).ToList();

        sb.AppendLine("-- DBSyncTool Upgrade.sql");
        sb.AppendLine($"-- 生成时间: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"-- 选中表数量: {selectedNodes.Count}");
        sb.AppendLine();

        if (UseTransaction)
        {
            sb.AppendLine(GetTransactionBegin(dbType));
            sb.AppendLine();
        }

        foreach (var node in selectedNodes)
        {
            var tableName = node.Title.Split('（')[0].Trim();

            if (node.GenerateSchema)
            {
                var added = _schemaDiff!.AddedTables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (added is not null)
                {
                    sb.AppendLine($"-- {tableName} 新增表");
                    sb.AppendLine(_sqlGenerator.GenerateCreateTable(dbType, added));
                    sb.AppendLine();
                }

                var mod = _schemaDiff.ModifiedTables.FirstOrDefault(t => t.SourceTable.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (mod is not null)
                {
                    sb.AppendLine($"-- {tableName} 结构变更");
                    foreach (var sql in _sqlGenerator.GenerateAlterTable(dbType, mod))
                    {
                        sb.AppendLine(sql);
                        sb.AppendLine();
                    }
                }

                var removed = _schemaDiff.RemovedTables.FirstOrDefault(t => t.FullName.Equals(tableName, StringComparison.OrdinalIgnoreCase));
                if (removed is not null)
                {
                    sb.AppendLine($"-- {tableName} 删除表");
                    sb.AppendLine(_sqlGenerator.GenerateDropTable(dbType, removed));
                    sb.AppendLine();
                }
            }

            AppendDataDml(sb, dbType, tableName, node);
        }

        if (UseTransaction)
            sb.AppendLine(GetTransactionEnd(dbType));

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 追加数据 DML（INSERT/DELETE/UPDATE）
    ///</summary>
    private void AppendDataDml(StringBuilder sb, DatabaseType dbType, string tableName, CompareSchemaNodeViewModel node)
    {
        if (!_dataDiffs.TryGetValue(tableName, out var dataDiff) || dataDiff.Skipped)
            return;

        if (!_sourceTableMap.TryGetValue(tableName, out var table))
            return;

        if (node.GenerateInsert && dataDiff.RowsToInsert.Count > 0)
        {
            var rows = dataDiff.RowsToInsert.Select(r => r.PrimaryKeyValues).ToList();
            sb.AppendLine($"-- {tableName} 数据新增 {dataDiff.RowsToInsert.Count} 行");
            foreach (var sql in _sqlGenerator.GenerateInsertStatements(dbType, table, rows, ExcludeIdentityColumns))
            {
                sb.AppendLine(sql);
                sb.AppendLine();
            }
        }

        if (node.GenerateDelete && dataDiff.DeletedRows.Count > 0)
        {
            var pkValues = dataDiff.DeletedRows.Select(r => r.PrimaryKeyValues).ToList();
            sb.AppendLine($"-- {tableName} 数据删除 {dataDiff.DeletedRows.Count} 行");
            foreach (var sql in _sqlGenerator.GenerateDeleteStatements(dbType, table, pkValues))
            {
                sb.AppendLine(sql);
                sb.AppendLine();
            }
        }

        if (node.GenerateUpdate && dataDiff.ChangedRows.Count > 0)
        {
            var changedRowData = dataDiff.ChangedRows.Select(r => r.PrimaryKeyValues).ToList();
            sb.AppendLine($"-- {tableName} 数据变更 {dataDiff.ChangedRows.Count} 行");
            foreach (var sql in _sqlGenerator.GenerateUpdateStatements(dbType, table, changedRowData))
            {
                sb.AppendLine(sql);
                sb.AppendLine();
            }
        }
    }

    /// <summary>
    /// 清空比对结果
    ///</summary>
    private void ClearResults()
    {
        AllDiffSchemaNodes.Clear();
        DifferentNodes.Clear();
        OnlySourceNodes.Clear();
        OnlyTargetNodes.Clear();
        DataDiffNodes.Clear();
        _schemaDiff = null;
        _dataDiffs.Clear();
        _sourceTableMap.Clear();
        SelectedDiffItem = null;
        SelectedDiffSqlText = "";
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
                Title = FormatTableTitle(table),
                StatusText = "新增表",
                IsSelected = true,
                Category = DiffCategory.OnlySource,
                StatusBrush = Brushes.DarkGreen,
                GenerateSchema = true
            };
            node.RefreshRequested = _ => RefreshDiffSql();
            OnlySourceNodes.Add(node);
        }

        foreach (var table in diff.RemovedTables.OrderBy(t => t.FullName))
        {
            var node = new CompareSchemaNodeViewModel
            {
                Title = FormatTableTitle(table),
                StatusText = "删除表",
                IsSelected = false,
                Category = DiffCategory.OnlyTarget,
                StatusBrush = Brushes.Firebrick,
                GenerateSchema = true
            };
            node.RefreshRequested = _ => RefreshDiffSql();
            OnlyTargetNodes.Add(node);
        }

        foreach (var mod in diff.ModifiedTables.OrderBy(t => t.SourceTable.FullName))
        {
            var tableName = mod.SourceTable.FullName;
            var hasData = _dataDiffs.TryGetValue(tableName, out var dataDiff) && !dataDiff.Skipped;
            var node = new CompareSchemaNodeViewModel
            {
                Title = FormatTableTitle(mod.SourceTable),
                StatusText = $"结构变更（{mod.ColumnDiffs.Count} 列，{mod.IndexDiffs.Count} 索引）",
                IsSelected = true,
                Category = DiffCategory.Different,
                StatusBrush = Brushes.DarkGoldenrod,
                GenerateSchema = true,
                HasDataDiff = hasData,
                DataInsertCount = hasData ? dataDiff!.RowsToInsert.Count : 0,
                DataDeleteCount = hasData ? dataDiff!.DeletedRows.Count : 0,
                DataChangeCount = hasData ? dataDiff!.ChangedRows.Count : 0
            };
            node.RefreshRequested = _ => RefreshDiffSql();
            DifferentNodes.Add(node);
        }

        // 纯数据差异的表（结构相同）
        var schemaTableNames = new HashSet<string>(
            diff.AddedTables.Select(t => t.FullName)
                .Concat(diff.RemovedTables.Select(t => t.FullName))
                .Concat(diff.ModifiedTables.Select(t => t.SourceTable.FullName)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (tableName, dataDiff) in _dataDiffs.OrderBy(kv => kv.Key))
        {
            if (schemaTableNames.Contains(tableName) || dataDiff.Skipped)
                continue;
            if (dataDiff.RowsToInsert.Count == 0 && dataDiff.DeletedRows.Count == 0 && dataDiff.ChangedRows.Count == 0)
                continue;

            var table = _sourceTableMap.GetValueOrDefault(tableName);
            var node = new CompareSchemaNodeViewModel
            {
                Title = table is not null ? FormatTableTitle(table) : tableName,
                StatusText = $"数据差异（新增 {dataDiff.RowsToInsert.Count}，删除 {dataDiff.DeletedRows.Count}，变更 {dataDiff.ChangedRows.Count}）",
                IsSelected = true,
                Category = DiffCategory.DataDiff,
                StatusBrush = Brushes.DarkGoldenrod,
                HasDataDiff = true,
                DataInsertCount = dataDiff.RowsToInsert.Count,
                DataDeleteCount = dataDiff.DeletedRows.Count,
                DataChangeCount = dataDiff.ChangedRows.Count
            };
            node.RefreshRequested = _ => RefreshDiffSql();
            DataDiffNodes.Add(node);
        }
    }

    /// <summary>
    /// 合并所有分类到 AllDiffSchemaNodes
    ///</summary>
    private void MergeAllDiffNodes()
    {
        AllDiffSchemaNodes.Clear();
        foreach (var n in DifferentNodes) AllDiffSchemaNodes.Add(n);
        foreach (var n in OnlySourceNodes) AllDiffSchemaNodes.Add(n);
        foreach (var n in OnlyTargetNodes) AllDiffSchemaNodes.Add(n);
        foreach (var n in DataDiffNodes) AllDiffSchemaNodes.Add(n);
    }

    /// <summary>
    /// 根据 SelectedDiffTabIndex 切换 CurrentDiffNodes
    ///</summary>
    private void RefreshCurrentDiffNodes()
    {
        CurrentDiffNodes = SelectedDiffTabIndex switch
        {
            1 => DifferentNodes,
            2 => OnlySourceNodes,
            3 => OnlyTargetNodes,
            4 => DataDiffNodes,
            _ => AllDiffSchemaNodes
        };
    }

    /// <summary>
    /// 同步全局开关到所有节点
    ///</summary>
    private void SyncGlobalToNodes(Action<CompareSchemaNodeViewModel> setter)
    {
        _suppressNodeRefresh = true;
        try
        {
            foreach (var node in AllDiffSchemaNodes)
                setter(node);
        }
        finally
        {
            _suppressNodeRefresh = false;
        }
    }

    private static string GetTransactionBegin(DatabaseType dbType) => dbType switch
    {
        DatabaseType.SqlServer => "SET XACT_ABORT ON;\nBEGIN TRANSACTION;",
        DatabaseType.MySql => "START TRANSACTION;",
        _ => "BEGIN;"
    };

    private static string GetTransactionEnd(DatabaseType dbType) => dbType switch
    {
        DatabaseType.SqlServer => "COMMIT TRANSACTION;\nGO",
        _ => "COMMIT;"
    };

    private static string FormatTableTitle(TableModel table)
    {
        return string.IsNullOrWhiteSpace(table.Comment)
            ? table.FullName
            : $"{table.FullName}（{table.Comment}）";
    }
}
