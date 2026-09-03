using DBSync.Core.Models;
using SqlSugar;

namespace DBSync.Core.Execution;

/// <summary>
/// SQL 脚本执行器实现，基于 SqlSugar 执行分段 SQL
///</summary>
public sealed class ScriptExecutor : IScriptExecutor
{
    /// <summary>
    /// SQL 语句分隔符列表
    ///</summary>
    private static readonly string[] StatementSeparators = ["\nGO\n", "\nGO\r\n", "\n;\n"];

    /// <summary>
    /// Dry Run：解析脚本，统计 DDL/DML 数量
    ///</summary>
    public ScriptExecutionPlan DryRun(string script)
    {
        var statements = SplitStatements(script);
        var ddl = new List<string>();
        var dmlCount = 0;
        var hasTransaction = false;

        foreach (var stmt in statements)
        {
            var trimmed = stmt.TrimStart();
            if (trimmed.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("ALTER ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("DROP ", StringComparison.OrdinalIgnoreCase))
            {
                ddl.Add(stmt.Length > 120 ? stmt[..120] + "..." : stmt);
            }
            else if (trimmed.StartsWith("INSERT ", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase))
            {
                dmlCount++;
            }
            else if (trimmed.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                hasTransaction = true;
            }
        }

        return new ScriptExecutionPlan
        {
            DdlCount = ddl.Count,
            DmlCount = dmlCount,
            TotalStatements = statements.Count,
            HasTransaction = hasTransaction,
            DdlStatements = ddl,
        };
    }

    /// <summary>
    /// 执行脚本到目标数据库
    ///</summary>
    public async Task<ScriptExecutionResult> ExecuteAsync(
        DatabaseConnection connection,
        string script,
        IProgress<ScriptExecutionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var statements = SplitStatements(script);
        var startTime = DateTime.UtcNow;
        long totalAffected = 0;
        var executed = 0;

        var dbType = connection.DbType switch
        {
            DatabaseType.SqlServer => DbType.SqlServer,
            DatabaseType.MySql => DbType.MySql,
            DatabaseType.PostgreSql => DbType.PostgreSQL,
            DatabaseType.Sqlite => DbType.Sqlite,
            _ => DbType.MySql
        };

        using var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = connection.ConnectionString,
            DbType = dbType,
            IsAutoCloseConnection = false,
        });

        try
        {
            await Task.Run(async () =>
            {
                db.Open();

                foreach (var stmt in statements)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var trimmed = stmt.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    // 跳过纯注释行
                    if (trimmed.StartsWith("--", StringComparison.Ordinal) && !trimmed.Contains('\n'))
                        continue;

                    progress?.Report(new ScriptExecutionProgress
                    {
                        Current = executed + 1,
                        Total = statements.Count,
                        CurrentStatement = trimmed.Length > 80 ? trimmed[..80] + "..." : trimmed
                    });

                    var affected = await db.Ado.ExecuteCommandAsync(trimmed);
                    totalAffected += Math.Max(0, affected);
                    executed++;
                }
            }, cancellationToken);

            return new ScriptExecutionResult
            {
                Success = true,
                ExecutedStatements = executed,
                AffectedRows = totalAffected,
                Duration = DateTime.UtcNow - startTime,
            };
        }
        catch (OperationCanceledException)
        {
            return new ScriptExecutionResult
            {
                Success = false,
                ExecutedStatements = executed,
                AffectedRows = totalAffected,
                Duration = DateTime.UtcNow - startTime,
                Error = "用户取消执行",
            };
        }
        catch (Exception ex)
        {
            return new ScriptExecutionResult
            {
                Success = false,
                ExecutedStatements = executed,
                AffectedRows = totalAffected,
                Duration = DateTime.UtcNow - startTime,
                Error = ex.Message,
                FailedStatement = statements.Count > executed ? statements[executed] : null,
                RolledBack = script.Contains("BEGIN", StringComparison.OrdinalIgnoreCase),
            };
        }
    }

    /// <summary>
    /// 将脚本拆分为单条语句
    ///</summary>
    private static List<string> SplitStatements(string script)
    {
        // 按分号分割，保留非空语句
        var parts = script.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }
}
