using DBSync.Core.Models;

namespace DBSync.Core.Execution;

/// <summary>
/// SQL 脚本执行器接口
///</summary>
public interface IScriptExecutor
{
    /// <summary>
    /// Dry Run：解析脚本，返回操作摘要但不执行
    ///</summary>
    /// <param name="script">SQL 脚本文本</param>
    /// <returns>操作摘要</returns>
    ScriptExecutionPlan DryRun(string script);

    /// <summary>
    /// 执行脚本到目标数据库
    ///</summary>
    /// <param name="connection">目标数据库连接</param>
    /// <param name="script">SQL 脚本文本</param>
    /// <param name="progress">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<ScriptExecutionResult> ExecuteAsync(
        DatabaseConnection connection,
        string script,
        IProgress<ScriptExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
