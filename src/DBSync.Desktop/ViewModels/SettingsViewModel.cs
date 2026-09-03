using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBSync.Desktop.Services;

namespace DBSync.Desktop.ViewModels;

/// <summary>
/// 设置页面的 ViewModel
///</summary>
public sealed partial class SettingsViewModel : ObservableObject, IPageViewModel
{
    /// <summary>
    /// 应用设置存储
    ///</summary>
    private readonly IAppSettingsStore _appSettingsStore;

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
    /// 行数警告阈值
    ///</summary>
    [ObservableProperty]
    private string rowCountWarningThresholdText = "100000";

    /// <summary>
    /// 默认导出目录
    ///</summary>
    [ObservableProperty]
    private string defaultExportDirectory = "";

    /// <summary>
    /// 默认启用加密
    ///</summary>
    [ObservableProperty]
    private bool defaultEncrypt = true;

    /// <summary>
    /// 默认启用事务
    ///</summary>
    [ObservableProperty]
    private bool defaultUseTransaction = true;

    /// <summary>
    /// 创建设置页面 ViewModel
    ///</summary>
    /// <param name="appSettingsStore">应用设置存储</param>
    public SettingsViewModel(IAppSettingsStore appSettingsStore)
    {
        _appSettingsStore = appSettingsStore;
        LoadSettings();
    }

    /// <summary>
    /// 从持久化存储加载设置
    ///</summary>
    private void LoadSettings()
    {
        var settings = _appSettingsStore.Load();
        RowCountWarningThresholdText = settings.RowCountWarningThreshold.ToString();
        DefaultExportDirectory = settings.DefaultExportDirectory ?? "";
        DefaultEncrypt = settings.DefaultEncrypt;
        DefaultUseTransaction = settings.DefaultUseTransaction;
    }

    /// <summary>
    /// 保存所有设置
    ///</summary>
    [RelayCommand]
    private void SaveSettings()
    {
        if (!int.TryParse(RowCountWarningThresholdText, out var threshold) || threshold <= 0)
        {
            StatusText = "行数阈值必须是大于 0 的整数";
            return;
        }

        var settings = _appSettingsStore.Load();
        settings = settings with
        {
            RowCountWarningThreshold = threshold,
            DefaultExportDirectory = string.IsNullOrWhiteSpace(DefaultExportDirectory) ? null : DefaultExportDirectory,
            DefaultEncrypt = DefaultEncrypt,
            DefaultUseTransaction = DefaultUseTransaction,
        };
        _appSettingsStore.Save(settings);
        StatusText = "设置已保存";
    }

    /// <summary>
    /// 重置为默认值
    ///</summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        RowCountWarningThresholdText = "100000";
        DefaultExportDirectory = "";
        DefaultEncrypt = true;
        DefaultUseTransaction = true;
        StatusText = "已重置为默认值，点击保存生效";
    }
}
