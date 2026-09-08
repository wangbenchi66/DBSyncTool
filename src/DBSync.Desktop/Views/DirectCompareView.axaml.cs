using Avalonia.Controls;
using Avalonia.Input;
using DBSync.Desktop.ViewModels;

namespace DBSync.Desktop.Views;

/// <summary>
/// 直连比对页面视图
/// </summary>
public partial class DirectCompareView : UserControl
{
    /// <summary>
    /// 初始化直连比对视图
    /// </summary>
    public DirectCompareView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击分类按钮时重新渲染当前分类所有勾选差异的 SQL 预览
    /// </summary>
    private void DiffCategoryList_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DirectCompareViewModel vm)
            vm.ShowCategoryDiffSql();
    }
}
