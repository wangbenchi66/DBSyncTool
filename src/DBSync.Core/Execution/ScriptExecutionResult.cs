namespace DBSync.Core.Execution;

/// <summary>
/// 脚本执行结果
///</summary>
public sealed record ScriptExecutionResult
{
    /// <summary>
    /// 是否执行成功
    ///</summary>
    public bool Success { get; init; }

    /// <summary>
    /// 已执行的语句数
    ///</summary>
    public int ExecutedStatements { get; init; }

    /// <summary>
    /// 受影响的总行数
    ///</summary>
    public long AffectedRows { get; init; }

    /// <summary>
    /// 执行耗时
    ///</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// 错误信息（失败时）
    ///</summary>
    public string? Error { get; init; }

    /// <summary>
    /// 失败时的 SQL 语句
    ///</summary>
    public string? FailedStatement { get; init; }

    /// <summary>
    /// 是否已回滚
    ///</summary>
    public bool RolledBack { get; init; }
}
