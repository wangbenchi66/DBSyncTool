using Avalonia.Controls;
using Avalonia.Input;
using DBSync.Desktop.ViewModels;

namespace DBSync.Desktop.Views;

/// <summary>
/// 加载快照并比对页面视图
/// </summary>
public partial class CompareView : UserControl
{
    /// <summary>
    /// 初始化比对视图
    /// </summary>
    public CompareView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击分类按钮时强制刷新 SQL 预览（包括点击已选中的分类）
    /// </summary>
    private void DiffCategoryList_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CompareViewModel vm)
            vm.RefreshDiffSql();
    }
}
