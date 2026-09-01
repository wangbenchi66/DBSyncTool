using System.Data;
using System.Runtime.CompilerServices;
using DBSync.Core.Models;
using Easy.SqlSugar.Core.Common;
using Microsoft.Data.SqlClient;

namespace DBSync.Core.Data;

/// <summary>
/// SQL Server 行哈希指纹计算器。
///</summary>
public sealed class SqlServerDataFingerprinter
{
    private const string HashColumnName = "__DBSYNC_HASH";

    /// <summary>
    /// 流式读取指定表的行哈希指纹。
    /// </summary>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="table">表元数据</param>
    /// <param name="whereClause">可选 WHERE 子句，不含 WHERE 关键字</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>行哈希指纹异步序列</returns>
    public async IAsyncEnumerable<RowHash> ReadRowHashesAsync(
        DatabaseConnection connection,
        TableModel table,
        string? whereClause = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (connection.DbType != DatabaseType.SqlServer || table.PrimaryKeyColumns.Count == 0)
            yield break;

        var sql = BuildFingerprintSql(table, whereClause);
        await using var sqlConnection = new SqlConnection(connection.ConnectionString.CheckTrustServerCertificate().CheckEncrypt());
        await sqlConnection.OpenAsync(cancellationToken);

        await using var command = sqlConnection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var primaryKeyValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var columnName in table.PrimaryKeyColumns)
            {
                var value = reader[columnName];
                primaryKeyValues[columnName] = value == DBNull.Value ? null : Convert.ToString(value);
            }

            yield return new RowHash
            {
                PrimaryKeyValues = primaryKeyValues,
                Hash = reader.GetString(reader.GetOrdinal(HashColumnName))
            };
        }
    }

    /// <summary>
    /// 清理 WHERE 子句。
    /// </summary>
    /// <param name="whereClause">原始 WHERE 子句</param>
    /// <returns>清理后的 WHERE 子句</returns>
    /// <exception cref="ArgumentException">WHERE 子句包含语句分隔符时抛出</exception>
    public static string? SanitizeWhereClause(string? whereClause)
    {
        var trimmed = whereClause?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        trimmed = trimmed.TrimEnd();
        if (trimmed.EndsWith(';'))
            trimmed = trimmed[..^1].TrimEnd();

        if (trimmed.Contains(';'))
            throw new ArgumentException("WHERE 子句不能包含语句分隔符。", nameof(whereClause));

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// 构建行指纹查询 SQL。
    /// </summary>
    /// <param name="table">表元数据</param>
    /// <param name="whereClause">可选 WHERE 子句</param>
    /// <returns>行指纹查询 SQL</returns>
    public static string BuildFingerprintSql(TableModel table, string? whereClause = null)
    {
        if (table.PrimaryKeyColumns.Count == 0)
            return string.Empty;

        var cleanedWhere = SanitizeWhereClause(whereClause);
        var primaryKeyColumns = table.PrimaryKeyColumns.Select(QuoteIdentifier).ToList();
        var hashParts = table.Columns
            .OrderBy(c => c.OrdinalPosition)
            .Select(FormatHashExpression)
            .ToList();
        var selectColumns = primaryKeyColumns
            .Concat([$"CONVERT(VARCHAR(32), HASHBYTES('MD5', CONCAT_WS(N'|', {string.Join(", ", hashParts)})), 2) AS {QuoteIdentifier(HashColumnName)}"]);
        var sql = $"""
SELECT {string.Join(", ", selectColumns)}
FROM {QuoteName(table)}
""";

        if (!string.IsNullOrWhiteSpace(cleanedWhere))
            sql += $"{Environment.NewLine}WHERE {cleanedWhere}";

        sql += $"{Environment.NewLine}ORDER BY {string.Join(", ", primaryKeyColumns)}";
        return sql;
    }

    /// <summary>
    /// 格式化单列哈希表达式。
    /// </summary>
    /// <param name="column">列模型</param>
    /// <returns>哈希表达式 SQL 片段</returns>
    private static string FormatHashExpression(ColumnModel column)
    {
        var columnName = QuoteIdentifier(column.Name);
        var valueExpression = column.ColumnType switch
        {
            DbColumnType.Binary => $"CONVERT(VARCHAR(MAX), {columnName}, 2)",
            DbColumnType.DateTime => FormatDateTimeExpression(column),
            DbColumnType.Float => $"LTRIM(STR({columnName}, 25, 15))",
            DbColumnType.Boolean => $"CONVERT(CHAR(1), {columnName})",
            DbColumnType.Xml => $"CAST({columnName} AS NVARCHAR(MAX))",
            _ => $"CAST({columnName} AS NVARCHAR(MAX))"
        };

        return $"COALESCE({valueExpression}, N'NULL')";
    }

    /// <summary>
    /// 格式化日期时间列哈希表达式。
    /// </summary>
    /// <param name="column">列模型</param>
    /// <returns>日期时间哈希表达式 SQL 片段</returns>
    private static string FormatDateTimeExpression(ColumnModel column)
    {
        var columnName = QuoteIdentifier(column.Name);
        return column.DbTypeName.Equals("datetimeoffset", StringComparison.OrdinalIgnoreCase)
            ? $"CONVERT(VARCHAR(23), CAST(SWITCHOFFSET({columnName}, '+00:00') AS datetime2), 121)"
            : $"CONVERT(VARCHAR(23), {columnName}, 121)";
    }

    /// <summary>
    /// 引用表名。
    /// </summary>
    /// <param name="table">表模型</param>
    /// <returns>带方括号的表名</returns>
    private static string QuoteName(TableModel table)
    {
        return string.IsNullOrWhiteSpace(table.Schema)
            ? QuoteIdentifier(table.Name)
            : $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";
    }

    /// <summary>
    /// 引用标识符。
    /// </summary>
    /// <param name="name">标识符名称</param>
    /// <returns>带方括号的标识符</returns>
    private static string QuoteIdentifier(string name)
    {
        return $"[{name.Replace("]", "]]")}]";
    }
}
