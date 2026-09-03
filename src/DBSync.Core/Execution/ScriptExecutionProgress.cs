namespace DBSync.Core.Execution;

/// <summary>
/// 脚本执行进度信息
///</summary>
public sealed record ScriptExecutionProgress
{
    /// <summary>
    /// 当前已执行的语句数
    ///</summary>
    public int Current { get; init; }

    /// <summary>
    /// 总语句数
    ///</summary>
    public int Total { get; init; }

    /// <summary>
    /// 当前正在执行的语句摘要
    ///</summary>
    public string CurrentStatement { get; init; } = "";
}
