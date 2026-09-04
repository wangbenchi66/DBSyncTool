using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Core.Models;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 导出表项的视图模型，包装 TableModel 并提供导出配置
///</summary>
public sealed partial class ExportTableItemViewModel : ObservableObject
{
    /// <summary>
    /// 被包装的表模型
    ///</summary>
    private readonly TableModel _table;

    /// <summary>
    /// 是否选中导出
    ///</summary>
    [ObservableProperty]
    private bool isSelected;

    /// <summary>
    /// 是否同步数据（需要有主键）
    ///</summary>
    [ObservableProperty]
    private bool syncData;

    /// <summary>
    /// WHERE 过滤条件
    ///</summary>
    [ObservableProperty]
    private string whereClause = string.Empty;

    /// <summary>
    /// 表的完整名称（含 Schema）
    ///</summary>
    public string FullName => _table.FullName;

    /// <summary>
    /// 原始表模型引用
    ///</summary>
    public TableModel Table => _table;

    /// <summary>
    /// 表注释
    ///</summary>
    public string Comment => string.IsNullOrWhiteSpace(_table.Comment) ? string.Empty : _table.Comment;

    /// <summary>
    /// 表是否有主键
    ///</summary>
    public bool HasPrimaryKey => _table.HasPrimaryKey;

    /// <summary>
    /// 同步模式显示文本
    ///</summary>
    public string SyncModeText => HasPrimaryKey
        ? SyncData ? "结构+数据" : "仅结构"
        : "无主键";

    /// <summary>
    /// 同步模式标签的背景色画刷
    ///</summary>
    public IBrush SyncModeBrush => HasPrimaryKey
        ? SyncData ? Brushes.RoyalBlue : Brushes.Gray
        : Brushes.OrangeRed;

    /// <summary>
    /// 切换同步数据模式（仅结构 ↔ 结构+数据）
    ///</summary>
    [RelayCommand]
    private void ToggleSyncData()
    {
        if (HasPrimaryKey)
            SyncData = !SyncData;
    }

    /// <summary>
    /// 创建导出表项视图模型
    ///</summary>
    /// <param name="table">表模型</param>
    public ExportTableItemViewModel(TableModel table)
    {
        _table = table;
        EstimatedRowCountText = table.EstimatedRowCount?.ToString() ?? "未知";
        DataSizeText = table.EstimatedDataSizeMb is null ? "未知" : $"{table.EstimatedDataSizeMb:0.##} MB";
    }

    /// <summary>
    /// 预估行数显示文本
    ///</summary>
    public string EstimatedRowCountText { get; }

    /// <summary>
    /// 数据大小显示文本
    ///</summary>
    public string DataSizeText { get; }

    /// <summary>
    /// 行数警告阈值
    ///</summary>
    public long RowCountWarningThreshold { get; init; } = 100_000;

    /// <summary>
    /// 大表导出确认回调
    ///</summary>
    public Func<ExportTableItemViewModel, Task<bool>>? ConfirmLargeExportAsync { get; init; }

    /// <summary>
    /// 防止 SyncData 回退时的递归标志
    ///</summary>
    private bool _isRevertingSyncData;

    /// <summary>
    /// SyncData 属性变更时的回调
    ///</summary>
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
        OnPropertyChanged(nameof(SyncModeBrush));
    }

    /// <summary>
    /// 异步确认大表导出，用户拒绝时回退 SyncData
    ///</summary>
    private async Task ConfirmLargeExportIfNeededAsync()
    {
        var confirmed = await ConfirmLargeExportAsync!(this);
        if (confirmed)
        {
            OnPropertyChanged(nameof(SyncModeText));
            OnPropertyChanged(nameof(SyncModeBrush));
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
            OnPropertyChanged(nameof(SyncModeBrush));
        }
    }
}
