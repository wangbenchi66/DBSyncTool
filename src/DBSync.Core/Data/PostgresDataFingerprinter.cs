using DBSync.Core;
using System.Data;
using System.Runtime.CompilerServices;
using DBSync.Core.Models;
using Npgsql;

namespace DBSync.Core.Data;

public sealed class PostgresDataFingerprinter : IDataFingerprinter
{
    private const string HashColumnName = "__DBSYNC_HASH";

    public async IAsyncEnumerable<RowHash> ReadRowHashesAsync(
        DatabaseConnection connection,
        TableModel table,
        string? whereClause = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (connection.DbType != DatabaseType.PostgreSql || table.PrimaryKeyColumns.Count == 0)
            yield break;

        var sql = BuildFingerprintSql(table, whereClause);
        await using var db = new NpgsqlConnection(connection.ConnectionString);
        await db.OpenAsync(cancellationToken);

        await using var command = db.CreateCommand();
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

    public static string BuildFingerprintSql(TableModel table, string? whereClause = null)
    {
        if (table.PrimaryKeyColumns.Count == 0)
            return string.Empty;

        var cleanedWhere = SqlServerDataFingerprinter.SanitizeWhereClause(whereClause);
        var primaryKeyColumns = table.PrimaryKeyColumns.Select(QuoteIdentifier).ToList();
        var hashParts = table.Columns
            .OrderBy(c => c.OrdinalPosition)
            .Select(FormatHashExpression)
            .ToList();
        var selectColumns = primaryKeyColumns.Concat([
            $"md5(concat_ws('|', {string.Join(", ", hashParts)})) AS {QuoteIdentifier(HashColumnName)}"
        ]);
        var sql = $"""
SELECT {string.Join(", ", selectColumns)}
FROM {QuoteName(table)}
""";

        if (!string.IsNullOrWhiteSpace(cleanedWhere))
            sql += $"{Environment.NewLine}WHERE {cleanedWhere}";

        sql += $"{Environment.NewLine}ORDER BY {string.Join(", ", primaryKeyColumns)}";
        return sql;
    }

    private static string FormatHashExpression(ColumnModel column)
    {
        var columnName = QuoteIdentifier(column.Name);
        var valueExpression = column.ColumnType switch
        {
            DbColumnType.Binary => $"encode({columnName}, 'hex')",
            DbColumnType.DateTime => $"to_char(({columnName} AT TIME ZONE 'UTC'), 'YYYY-MM-DD HH24:MI:SS.US')",
            DbColumnType.Float => $"trim(to_char({columnName}, 'FM999999999999990.###############'))",
            DbColumnType.Json => $"to_jsonb({columnName})::text",
            DbColumnType.Boolean => $"CASE WHEN {columnName} THEN '1' ELSE '0' END",
            _ => $"{columnName}::text"
        };

        return $"COALESCE({valueExpression}, 'NULL')";
    }

    private static string QuoteName(TableModel table)
    {
        return DbDialectSupport.QuoteTableName(DatabaseType.PostgreSql, table.Schema, table.Name);
    }

    private static string QuoteIdentifier(string name)
    {
        return DbDialectSupport.QuotePostgresIdentifier(name);
    }
}
