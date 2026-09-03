namespace DBSync.Core.Execution;

/// <summary>
/// Dry Run 结果：脚本解析后的操作摘要
///</summary>
public sealed record ScriptExecutionPlan
{
    /// <summary>
    /// DDL 语句数量（CREATE/ALTER/DROP）
    ///</summary>
    public int DdlCount { get; init; }

    /// <summary>
    /// DML 语句数量（INSERT/UPDATE/DELETE）
    ///</summary>
    public int DmlCount { get; init; }

    /// <summary>
    /// 总语句数
    ///</summary>
    public int TotalStatements { get; init; }

    /// <summary>
    /// 是否包含事务
    ///</summary>
    public bool HasTransaction { get; init; }

    /// <summary>
    /// DDL 语句明细
    ///</summary>
    public IReadOnlyList<string> DdlStatements { get; init; } = [];

    /// <summary>
    /// 错误信息（解析失败时）
    ///</summary>
    public string? Error { get; init; }
}
