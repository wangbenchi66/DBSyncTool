namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 页面 ViewModel 公共接口，用于状态栏转发
///</summary>
public interface IPageViewModel
{
    /// <summary>
    /// 页面状态文本
    ///</summary>
    string StatusText { get; }

    /// <summary>
    /// 页面日志摘要
    ///</summary>
    string LogSummary { get; }
}
