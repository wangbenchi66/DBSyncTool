using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Core.Comparers;
using DBSync.Core.Data;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Core.Snapshot;
using DBSync.Core.SqlGenerators;
using DBSync.Desktop.Helpers;
using DBSync.Desktop.Services;
using DBSync.Desktop.Storage;
using DBSync.Desktop.Models;
using DBSync.Desktop.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 比对功能的视图模型，从 MainWindowViewModel 中提取，
/// 负责快照加载、结构/数据比对、脚本生成和报告导出
///</summary>
public partial class CompareViewModel : ObservableObject, IPageViewModel
{
    /// <summary>
    /// 连接存储服务，用于读取已保存的数据库连接
    ///</summary>
    private readonly IConnectionStore _connectionStore;

    /// <summary>
    /// 应用设置存储服务，用于读写全局配置
    ///</summary>
    private readonly IAppSettingsStore _appSettingsStore;

    /// <summary>
    /// 数据库结构读取器，用于读取当前库的表结构
    ///</summary>
    private readonly ISchemaReader _schemaReader;

    /// <summary>
    /// 快照加载器，用于解密并加载 .dbsync 快照文件
    ///</summary>
    private readonly ISnapshotLoader _snapshotLoader;

    /// <summary>
    /// SQL 生成器，用于根据差异生成升级脚本
    ///</summary>
    private readonly ISqlGenerator _sqlGenerator;

    /// <summary>
    /// 差异报告导出器，用于生成 Markdown 或 HTML 格式的比对报告
    ///</summary>
    private readonly DiffReportExporter _reportExporter;

    /// <summary>
    /// 数据指纹生成器，用于逐行读取当前库的行哈希
    ///</summary>
    private readonly IDataFingerprinter _fingerprinter;

    /// <summary>
    /// 窗口提供者，用于获取主窗口以访问文件选择器和模态对话框
    ///</summary>
    private readonly IWindowProvider _windowProvider;

    /// <summary>
    /// 已加载的快照实例，加载成功后保存在此处供后续比对使用
    ///</summary>
    private Snapshot? _loadedSnapshot;

    /// <summary>
    /// 已加载的结构差异结果，比对完成后保存在此处
    ///</summary>
    private SchemaDiff? _loadedSchemaDiff;

    /// <summary>
    /// 已加载的数据差异结果，表名到 DataDiff 的映射
    ///</summary>
    private readonly Dictionary<string, DataDiff> _loadedDataDiffs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 当前应用设置
    ///</summary>
    private AppSettings _settings;

    /// <summary>
    /// 底部状态栏显示的文本
    ///</summary>
    [ObservableProperty]
    private string statusText = "就绪";

    /// <summary>
    /// 操作日志摘要文本
    ///</summary>
    [ObservableProperty]
    private string logSummary = "";

    /// <summary>
    /// 快照文件的本地路径
    ///</summary>
    [ObservableProperty]
    private string compareSnapshotPath = string.Empty;

    /// <summary>
    /// 用户输入的快照解密密码
    ///</summary>
    [ObservableProperty]
    private string comparePassword = string.Empty;

    /// <summary>
    /// 快照文件中存储的明文密码提示
    ///</summary>
    [ObservableProperty]
    private string comparePasswordHint = string.Empty;

    /// <summary>
    /// 快照元数据描述文本（导出时间、数据库类型、表数量等）
    ///</summary>
    [ObservableProperty]
    private string compareSnapshotMetaText = "尚未加载快照";

    /// <summary>
    /// 比对进度描述文本
    ///</summary>
    [ObservableProperty]
    private string compareProgressText = "未开始";

    /// <summary>
    /// 比对进度百分比（0-100）
    ///</summary>
    [ObservableProperty]
    private int compareProgress;

    /// <summary>
    /// 比对结果摘要文本
    ///</summary>
    [ObservableProperty]
    private string compareSummaryText = "尚未比对";

    /// <summary>
    /// 当前选中的比对目标数据库连接
    ///</summary>
    [ObservableProperty]
    private ConnectionItemViewModel? selectedCompareConnection;

    /// <summary>
    /// 是否有未保存的待处理操作
    ///</summary>
    [ObservableProperty]
    private bool hasPendingOperation;

    /// <summary>
    /// 与快照数据库类型匹配的可用连接列表
    ///</summary>
    public ObservableCollection<ConnectionItemViewModel> CompareConnections { get; } = new();

    /// <summary>
    /// 结构差异预览树节点集合
    ///</summary>
    public ObservableCollection<CompareSchemaNodeViewModel> CompareSchemaNodes { get; } = new();

    /// <summary>
    /// 数据差异摘要集合
    ///</summary>
    public ObservableCollection<CompareDataSummaryViewModel> CompareDataSummaries { get; } = new();

    /// <summary>
    /// 初始化比对视图模型，注入所有依赖并加载初始配置
    ///</summary>
    /// <param name="connectionStore">连接存储服务</param>
    /// <param name="appSettingsStore">应用设置存储服务</param>
    /// <param name="schemaReader">数据库结构读取器</param>
    /// <param name="snapshotLoader">快照加载器</param>
    /// <param name="sqlGenerator">SQL 生成器</param>
    /// <param name="reportExporter">差异报告导出器</param>
    /// <param name="fingerprinter">数据指纹生成器</param>
    /// <param name="windowProvider">窗口提供者</param>
    public CompareViewModel(
        IConnectionStore connectionStore,
        IAppSettingsStore appSettingsStore,
        ISchemaReader schemaReader,
        ISnapshotLoader snapshotLoader,
        ISqlGenerator sqlGenerator,
        DiffReportExporter reportExporter,
        IDataFingerprinter fingerprinter,
        IWindowProvider windowProvider)
    {
        _connectionStore = connectionStore;
        _appSettingsStore = appSettingsStore;
        _schemaReader = schemaReader;
        _snapshotLoader = snapshotLoader;
        _sqlGenerator = sqlGenerator;
        _reportExporter = reportExporter;
        _fingerprinter = fingerprinter;
        _windowProvider = windowProvider;
        _settings = _appSettingsStore.Load();
        CompareSnapshotPath = _settings.LastSnapshotPath ?? string.Empty;
    }

    /// <summary>
    /// 打开文件选择器，选择 .dbsync 快照文件并读取密码提示
    ///</summary>
    [RelayCommand]
    private async Task BrowseSnapshotAsync()
    {
        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

    /// <summary>
    /// 加载快照文件，验证路径和密码后解密并读取快照内容，
    /// 成功后更新元数据文本并刷新可用连接列表
    ///</summary>
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
            SaveRecentHistory("快照", Path.GetFileName(CompareSnapshotPath), CompareSnapshotPath);
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

    /// <summary>
    /// 执行结构和数据比对，读取当前数据库的表结构后与快照逐表对比，
    /// 生成结构差异树和数据差异摘要
    ///</summary>
    [RelayCommand]
    private async Task RunCompareAsync()
    {
        if (_loadedSnapshot is null)
        {
            StatusText = "请先加载快照";
            return;
        }

        var connection = SelectedCompareConnection?.ToDatabaseConnection();
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
            SaveRecentHistory("比对", connection.Name, CompareSnapshotPath, connection.Name);
        }
        catch (Exception ex)
        {
            StatusText = "比对失败";
            CompareProgressText = "比对失败";
            LogSummary = ex.Message;
        }
    }

    /// <summary>
    /// 根据比对结果生成升级 SQL 脚本，并保存到用户选择的文件路径
    ///</summary>
    [RelayCommand]
    private async Task GenerateUpgradeScriptAsync()
    {
        if (!EnsureCompareReady())
            return;

        var path = await PickSaveFileAsync(
            "保存升级脚本",
            $"Upgrade_{DateTime.Now:yyyyMMdd_HHmmss}.sql",
            new FilePickerFileType("SQL 脚本") { Patterns = ["*.sql"] });
        if (path is null)
            return;

        try
        {
            StatusText = "正在生成脚本...";
            var dbType = SelectedCompareConnection?.ToDatabaseConnection()?.DbType ?? _loadedSnapshot!.Manifest.DbType;
            var script = _sqlGenerator.GenerateUpgradeScript(dbType, _loadedSchemaDiff!, _loadedDataDiffs, _loadedSnapshot!.FullData);
            var lines = script.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            var ddlCount = lines.Count(line =>
                line.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("ALTER ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("DROP ", StringComparison.OrdinalIgnoreCase));
            var insertCount = lines.Count(line => line.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase));
            var estimatedRows = _loadedDataDiffs.Values.Sum(diff => diff.RowsToInsert.Count + diff.DeletedRows.Count + diff.ChangedRows.Count);

            await File.WriteAllTextAsync(path, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SaveRecentHistory("脚本", Path.GetFileName(path), path, SelectedCompareConnection?.ToDatabaseConnection()?.Name);
            var fileSize = new FileInfo(path).Length;
            StatusText = "脚本已生成";
            LogSummary = $"脚本已保存：{path}；DDL {ddlCount} 条，INSERT {insertCount} 条，估计行数 {estimatedRows}，大小 {FormatFileSize(fileSize)}";
        }
        catch (Exception ex)
        {
            StatusText = "脚本生成失败";
            LogSummary = ex.Message;
        }
    }

    /// <summary>
    /// 导出 Markdown 格式的差异报告
    ///</summary>
    [RelayCommand]
    private async Task ExportMarkdownReportAsync()
    {
        await ExportReportAsync(isHtml: false);
    }

    /// <summary>
    /// 导出 HTML 格式的差异报告
    ///</summary>
    [RelayCommand]
    private async Task ExportHtmlReportAsync()
    {
        await ExportReportAsync(isHtml: true);
    }

    /// <summary>
    /// 根据已加载快照的数据库类型，从全部连接列表中过滤出匹配的连接，
    /// 并更新 CompareConnections 集合
    ///</summary>
    public void RefreshCompareConnections()
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

        var allConnections = _connectionStore.Load();
        var filtered = allConnections
            .Where(c => c.DbType == targetDbType)
            .Select(ConnectionItemViewModel.FromDatabaseConnection);

        foreach (var connection in filtered)
            CompareConnections.Add(connection);

        SelectedCompareConnection = CompareConnections.FirstOrDefault(c =>
            string.Equals(c.Name, previousSelection, StringComparison.OrdinalIgnoreCase))
            ?? CompareConnections.FirstOrDefault();
    }

    /// <summary>
    /// 根据结构差异构建预览树节点，包括新增表、删除表、修改表和循环依赖
    ///</summary>
    /// <param name="schemaDiff">结构差异结果</param>
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

    /// <summary>
    /// 根据数据差异和表定义构建数据差异预览摘要
    ///</summary>
    /// <param name="dataDiffs">表名到数据差异的映射</param>
    /// <param name="tables">表结构定义集合</param>
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

    /// <summary>
    /// 创建结构差异预览树的节点
    ///</summary>
    /// <param name="title">节点标题</param>
    /// <param name="statusText">状态描述文本</param>
    /// <param name="isSelected">是否默认选中</param>
    /// <param name="hasWarning">是否有警告标识</param>
    /// <param name="children">子节点列表</param>
    /// <returns>结构差异预览节点</returns>
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

    /// <summary>
    /// 创建结构差异预览树的叶子节点（列、索引、主键等）
    ///</summary>
    /// <param name="title">节点标题</param>
    /// <param name="statusText">状态描述文本</param>
    /// <returns>叶子节点</returns>
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

    /// <summary>
    /// 根据状态文本和警告标识解析结构节点的颜色画刷
    ///</summary>
    /// <param name="statusText">状态描述文本</param>
    /// <param name="hasWarning">是否有警告</param>
    /// <returns>对应状态的颜色画刷</returns>
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

    /// <summary>
    /// 根据数据差异结果解析数据摘要行的颜色画刷
    ///</summary>
    /// <param name="diff">数据差异结果</param>
    /// <returns>对应状态的颜色画刷</returns>
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

    /// <summary>
    /// 根据结构差异和数据差异构建比对结果的摘要文本
    ///</summary>
    /// <param name="schemaDiff">结构差异结果</param>
    /// <param name="dataDiffs">表名到数据差异的映射</param>
    /// <returns>格式化的摘要文本</returns>
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

    /// <summary>
    /// 校验快照和比对结果是否已加载，未加载时更新状态提示
    ///</summary>
    /// <returns>如果快照和比对结果均已加载则返回 true</returns>
    private bool EnsureCompareReady()
    {
        if (_loadedSnapshot is null || _loadedSchemaDiff is null)
        {
            StatusText = "请先加载快照并完成比对";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 共享的报告导出逻辑，根据参数生成 Markdown 或 HTML 格式的差异报告
    ///</summary>
    /// <param name="isHtml">true 导出 HTML 格式，false 导出 Markdown 格式</param>
    private async Task ExportReportAsync(bool isHtml)
    {
        if (!EnsureCompareReady())
            return;

        var path = await PickSaveFileAsync(
            isHtml ? "保存 HTML 报告" : "保存 Markdown 报告",
            $"DiffReport_{DateTime.Now:yyyyMMdd_HHmmss}.{(isHtml ? "html" : "md")}",
            new FilePickerFileType(isHtml ? "HTML 报告" : "Markdown 报告")
            {
                Patterns = isHtml ? ["*.html"] : ["*.md"]
            });
        if (path is null)
            return;

        try
        {
            StatusText = isHtml ? "正在导出 HTML 报告..." : "正在导出 Markdown 报告...";
            var content = isHtml
                ? _reportExporter.BuildHtmlReport(_loadedSnapshot!, _loadedSchemaDiff!, _loadedDataDiffs, SelectedCompareConnection?.ToDatabaseConnection()?.Name)
                : _reportExporter.BuildMarkdownReport(_loadedSnapshot!, _loadedSchemaDiff!, _loadedDataDiffs, SelectedCompareConnection?.ToDatabaseConnection()?.Name);

            await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SaveRecentHistory(isHtml ? "报告" : "报告", Path.GetFileName(path), path, SelectedCompareConnection?.ToDatabaseConnection()?.Name);
            StatusText = isHtml ? "HTML 报告已导出" : "Markdown 报告已导出";
            LogSummary = $"已保存：{path}";
        }
        catch (Exception ex)
        {
            StatusText = isHtml ? "HTML 报告导出失败" : "Markdown 报告导出失败";
            LogSummary = ex.Message;
        }
    }

    /// <summary>
    /// 打开系统文件保存对话框，让用户选择保存路径
    ///</summary>
    /// <param name="title">对话框标题</param>
    /// <param name="defaultFileName">默认文件名</param>
    /// <param name="fileType">文件类型过滤器</param>
    /// <returns>用户选择的本地文件路径，取消时返回 null</returns>
    private async Task<string?> PickSaveFileAsync(string title, string defaultFileName, FilePickerFileType fileType)
    {
        var window = _windowProvider.GetMainWindow();
        if (window is null)
            return null;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = [fileType]
        });

        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// 使用系统默认程序打开指定路径的文件
    ///</summary>
    /// <param name="path">文件的绝对路径</param>
    /// <returns>异步任务</returns>
    private static Task OpenFileAsync(string path)
    {
        ViewModelHelpers.OpenFile(path);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 将字节数格式化为可读的文件大小字符串（KB 或 MB）
    ///</summary>
    /// <param name="bytes">文件字节数</param>
    /// <returns>格式化后的文件大小字符串</returns>
    private static string FormatFileSize(long bytes)
    {
        return ViewModelHelpers.FormatFileSize(bytes);
    }

    /// <summary>
    /// 保存比对历史到应用设置，记录连接名称和快照路径
    ///</summary>
    /// <param name="connectionName">比对使用的连接名称</param>
    private void SaveCompareHistory(string connectionName)
    {
        _settings = _settings with
        {
            LastConnectionName = connectionName,
            LastSnapshotPath = CompareSnapshotPath
        };
        _appSettingsStore.Save(_settings);
    }

    /// <summary>
    /// 保存最近操作历史记录到应用设置
    ///</summary>
    /// <param name="kind">操作类型（如"快照"、"比对"、"脚本"、"报告"）</param>
    /// <param name="title">操作标题</param>
    /// <param name="path">相关文件路径</param>
    /// <param name="connectionName">关联的连接名称，可选</param>
    private void SaveRecentHistory(string kind, string title, string path, string? connectionName = null)
    {
        _settings = ViewModelHelpers.SaveRecentHistory(_appSettingsStore, _settings, kind, title, path, connectionName);
    }
}
