using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 差异分类枚举：两端不同 / 仅快照有 / 仅目标库有 / 完全相同
///</summary>
public enum DiffCategory
{
    /// <summary>两端都有但结构不同</summary>
    Different,
    /// <summary>仅快照中存在（基线有、目标库无）</summary>
    OnlySource,
    /// <summary>仅目标库中存在（目标库有、快照无）</summary>
    OnlyTarget,
    /// <summary>完全相同</summary>
    Identical,
    /// <summary>仅数据差异（结构相同）</summary>
    DataDiff
}

/// <summary>
/// 结构差异预览树节点的视图模型
///</summary>
public sealed partial class CompareSchemaNodeViewModel : ObservableObject
{
    /// <summary>
    /// 差异分类
    ///</summary>
    [ObservableProperty]
    private DiffCategory category;
    /// <summary>
    /// 节点标题（表名或列名）
    ///</summary>
    [ObservableProperty]
    private string title = string.Empty;

    /// <summary>
    /// 差异状态描述文本（如"新增表"、"列修改"）
    ///</summary>
    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>
    /// 是否选中（用于控制是否纳入脚本生成）
    ///</summary>
    [ObservableProperty]
    private bool isSelected = true;

    /// <summary>
    /// 是否有警告（如循环外键依赖）
    ///</summary>
    [ObservableProperty]
    private bool hasWarning;

    /// <summary>
    /// 状态文本的颜色画刷
    ///</summary>
    [ObservableProperty]
    private IBrush statusBrush = Brushes.Gray;

    /// <summary>
    /// 是否展开显示子差异
    ///</summary>
    [ObservableProperty]
    private bool isExpanded;

    /// <summary>
    /// 是否存在数据差异
    ///</summary>
    [ObservableProperty]
    private bool hasDataDiff;

    /// <summary>
    /// 数据新增行数
    ///</summary>
    [ObservableProperty]
    private int dataInsertCount;

    /// <summary>
    /// 数据删除行数
    ///</summary>
    [ObservableProperty]
    private int dataDeleteCount;

    /// <summary>
    /// 数据变更行数
    ///</summary>
    [ObservableProperty]
    private int dataChangeCount;

    /// <summary>
    /// 是否生成结构变更语句（ALTER/CREATE/DROP）
    ///</summary>
    [ObservableProperty]
    private bool generateSchema = true;

    /// <summary>
    /// 是否生成 INSERT 语句
    ///</summary>
    [ObservableProperty]
    private bool generateInsert;

    /// <summary>
    /// 是否生成 UPDATE 语句
    ///</summary>
    [ObservableProperty]
    private bool generateUpdate;

    /// <summary>
    /// 是否生成 DELETE 语句
    ///</summary>
    [ObservableProperty]
    private bool generateDelete;

    /// <summary>
    /// 节点状态变更后的刷新回调
    ///</summary>
    public Action<CompareSchemaNodeViewModel>? RefreshRequested { get; set; }

    partial void OnIsSelectedChanged(bool value) => RefreshRequested?.Invoke(this);
    partial void OnGenerateSchemaChanged(bool value) => RefreshRequested?.Invoke(this);
    partial void OnGenerateInsertChanged(bool value) => RefreshRequested?.Invoke(this);
    partial void OnGenerateUpdateChanged(bool value) => RefreshRequested?.Invoke(this);
    partial void OnGenerateDeleteChanged(bool value) => RefreshRequested?.Invoke(this);

    /// <summary>
    /// 子节点集合（列级差异、索引差异等）
    ///</summary>
    public ObservableCollection<CompareSchemaNodeViewModel> Children { get; } = new();
}

/// <summary>
/// 数据差异摘要的视图模型
///</summary>
public sealed partial class CompareDataSummaryViewModel : ObservableObject
{
    /// <summary>
    /// 差异分类
    ///</summary>
    [ObservableProperty]
    private DiffCategory category;

    /// <summary>
    /// 表全名
    ///</summary>
    [ObservableProperty]
    private string tableName = string.Empty;

    /// <summary>
    /// 差异摘要文本（如"新增 5 行，删除 2 行"）
    ///</summary>
    [ObservableProperty]
    private string summaryText = string.Empty;

    /// <summary>
    /// 是否已跳过数据比对（无主键时为 true）
    ///</summary>
    [ObservableProperty]
    private bool isSkipped;

    /// <summary>
    /// 新增行数
    ///</summary>
    [ObservableProperty]
    private int rowsToInsert;

    /// <summary>
    /// 删除行数
    ///</summary>
    [ObservableProperty]
    private int deletedRows;

    /// <summary>
    /// 变更行数
    ///</summary>
    [ObservableProperty]
    private int changedRows;

    /// <summary>
    /// 摘要文本的颜色画刷
    ///</summary>
    [ObservableProperty]
    private IBrush summaryBrush = Brushes.Gray;

    /// <summary>
    /// 该表将生成的数据差异 SQL
    ///</summary>
    [ObservableProperty]
    private string sqlPreviewText = string.Empty;

    /// <summary>
    /// 是否存在可预览的 SQL
    ///</summary>
    [ObservableProperty]
    private bool hasSqlPreview;

    /// <summary>
    /// 是否展开 SQL 预览
    ///</summary>
    [ObservableProperty]
    private bool isSqlExpanded;
}

/// <summary>
/// 比对表选择项的视图模型
///</summary>
public sealed partial class CompareTableSelectionViewModel : ObservableObject
{
    /// <summary>
    /// 是否参与比对
    ///</summary>
    [ObservableProperty]
    private bool isSelected = true;

    /// <summary>
    /// 选中状态变更时的回调（用于快照→数据库的联动同步）
    ///</summary>
    public Action<CompareTableSelectionViewModel>? IsSelectedChangedCallback { get; init; }

    /// <summary>
    /// 选中状态变更后触发回调
    ///</summary>
    partial void OnIsSelectedChanged(bool value)
    {
        IsSelectedChangedCallback?.Invoke(this);
    }

    /// <summary>
    /// 是否比对数据（false 时仅比对结构）
    ///</summary>
    [ObservableProperty]
    private bool compareData;

    /// <summary>
    /// 模式显示文本
    ///</summary>
    public string CompareModText => CompareData ? "结构+数据" : "仅结构";

    /// <summary>
    /// 模式徽章颜色
    ///</summary>
    public Avalonia.Media.IBrush CompareModeBrush => CompareData
        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2549E0"))
        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5B6678"));

    /// <summary>
    /// 切换比对模式
    ///</summary>
    [RelayCommand]
    private void ToggleCompareData()
    {
        CompareData = !CompareData;
    }

    partial void OnCompareDataChanged(bool value)
    {
        OnPropertyChanged(nameof(CompareModText));
        OnPropertyChanged(nameof(CompareModeBrush));
    }

    /// <summary>
    /// 表结构模型
    ///</summary>
    public required DBSync.Core.Models.TableModel Table { get; init; }

    /// <summary>
    /// 表完整名称
    ///</summary>
    public string FullName => Table.FullName;

    /// <summary>
    /// 表注释
    ///</summary>
    public string Comment => string.IsNullOrWhiteSpace(Table.Comment) ? string.Empty : Table.Comment;
}
