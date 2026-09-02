using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 结构差异预览树节点的视图模型
///</summary>
public sealed partial class CompareSchemaNodeViewModel : ObservableObject
{
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
