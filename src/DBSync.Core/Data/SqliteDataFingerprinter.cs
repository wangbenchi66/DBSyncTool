using DBSync.Core;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DBSync.Core.Models;
using Microsoft.Data.Sqlite;

namespace DBSync.Core.Data;

public sealed class SqliteDataFingerprinter : IDataFingerprinter
{
    public async IAsyncEnumerable<RowHash> ReadRowHashesAsync(
        DatabaseConnection connection,
        TableModel table,
        string? whereClause = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (connection.DbType != DatabaseType.Sqlite || table.PrimaryKeyColumns.Count == 0)
            yield break;

        var sql = BuildFingerprintSql(table, whereClause);
        await using var db = new SqliteConnection(connection.ConnectionString);
        await db.OpenAsync(cancellationToken);

        await using var command = db.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        var columns = table.Columns.OrderBy(c => c.OrdinalPosition).ToList();
        while (await reader.ReadAsync(cancellationToken))
        {
            var primaryKeyValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var columnName in table.PrimaryKeyColumns)
            {
                var value = reader[columnName];
                primaryKeyValues[columnName] = value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            var parts = new List<string>(columns.Count);
            foreach (var column in columns)
            {
                var value = reader[column.Name];
                parts.Add(FormatValue(column, value));
            }

            var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)));

            yield return new RowHash
            {
                PrimaryKeyValues = primaryKeyValues,
                Hash = Convert.ToHexString(hashBytes).ToLowerInvariant()
            };
        }
    }

    public static string BuildFingerprintSql(TableModel table, string? whereClause = null)
    {
        if (table.PrimaryKeyColumns.Count == 0)
            return string.Empty;

        var cleanedWhere = SqlServerDataFingerprinter.SanitizeWhereClause(whereClause);
        var columns = table.Columns
            .OrderBy(c => c.OrdinalPosition)
            .Select(c => QuoteIdentifier(c.Name));
        var sql = $"""
SELECT {string.Join(", ", columns)}
FROM {QuoteName(table)}
""";

        if (!string.IsNullOrWhiteSpace(cleanedWhere))
            sql += $"{Environment.NewLine}WHERE {cleanedWhere}";

        sql += $"{Environment.NewLine}ORDER BY {string.Join(", ", table.PrimaryKeyColumns.Select(QuoteIdentifier))}";
        return sql;
    }

    private static string FormatValue(ColumnModel column, object? value)
    {
        if (value is null || value is DBNull)
            return "NULL";

        return column.ColumnType switch
        {
            DbColumnType.Binary when value is byte[] bytes => Convert.ToHexString(bytes),
            DbColumnType.DateTime => FormatDateTime(value),
            DbColumnType.Float => FormatFloat(value),
            DbColumnType.Boolean => FormatBoolean(value),
            DbColumnType.Json => NormalizeJson(value),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string FormatDateTime(object value)
    {
        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
            string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) =>
                parsed.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string FormatFloat(object value)
    {
        return value switch
        {
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string FormatBoolean(object value)
    {
        return value switch
        {
            bool b => b ? "1" : "0",
            byte b => b == 0 ? "0" : "1",
            sbyte sb => sb == 0 ? "0" : "1",
            short s => s == 0 ? "0" : "1",
            int i => i == 0 ? "0" : "1",
            long l => l == 0 ? "0" : "1",
            string text => bool.TryParse(text, out var parsed) ? (parsed ? "1" : "0") : text,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string NormalizeJson(object value)
    {
        if (value is not string text)
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            return document.RootElement.GetRawText();
        }
        catch
        {
            return text;
        }
    }

    private static string QuoteName(TableModel table)
    {
        return DbDialectSupport.QuoteTableName(DatabaseType.Sqlite, table.Schema, table.Name);
    }

    private static string QuoteIdentifier(string name)
    {
        return DbDialectSupport.QuoteSqliteIdentifier(name);
    }
}
