using DBSync.Core.Models;

namespace DBSync.Core;

internal static class DbDialectSupport
{
    internal static DbColumnType MapColumnType(string dbTypeName, string? rawTypeName = null)
    {
        var type = dbTypeName.Trim().ToLowerInvariant();
        var raw = rawTypeName?.Trim().ToLowerInvariant();

        if (type is "char" or "nchar" or "varchar" or "nvarchar" or "text" or "tinytext" or "mediumtext" or "longtext" or "citext" or "character varying" or "character")
            return DbColumnType.Text;

        if (type is "tinyint" or "smallint" or "int" or "integer" or "bigint" or "serial" or "bigserial" or "smallserial" or "mediumint")
            return DbColumnType.Integer;

        if (type is "decimal" or "numeric" or "money" or "smallmoney")
            return DbColumnType.Decimal;

        if (type is "float" or "real" or "double" or "double precision")
            return DbColumnType.Float;

        if (type is "bit" or "bool" or "boolean")
            return DbColumnType.Boolean;

        if (type.Contains("date", StringComparison.Ordinal) ||
            type.Contains("time", StringComparison.Ordinal) ||
            type is "timestamp" or "datetime" or "datetime2" or "smalldatetime" or "timestamptz" or "timestamp with time zone" or "timestamp without time zone")
            return DbColumnType.DateTime;

        if (type is "binary" or "varbinary" or "blob" or "bytea" or "image" or "raw" or "longblob" or "mediumblob")
            return DbColumnType.Binary;

        if (type is "json" or "jsonb")
            return DbColumnType.Json;

        if (type is "xml")
            return DbColumnType.Xml;

        if (raw is not null && raw.Contains("tinyint(1)", StringComparison.OrdinalIgnoreCase))
            return DbColumnType.Boolean;

        return DbColumnType.Other;
    }

    internal static string QuoteSqlServerIdentifier(string name) => $"[{name.Replace("]", "]]")}]";

    internal static string QuoteMySqlIdentifier(string name) => $"`{name.Replace("`", "``")}`";

    internal static string QuotePostgresIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    internal static string QuoteSqliteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

    internal static string QuoteIdentifier(DatabaseType dbType, string name)
    {
        return dbType switch
        {
            DatabaseType.SqlServer => QuoteSqlServerIdentifier(name),
            DatabaseType.MySql => QuoteMySqlIdentifier(name),
            DatabaseType.PostgreSql => QuotePostgresIdentifier(name),
            DatabaseType.Sqlite => QuoteSqliteIdentifier(name),
            _ => throw new NotSupportedException($"不支持的数据库类型：{dbType}")
        };
    }

    internal static string QuoteTableName(DatabaseType dbType, string schema, string tableName)
    {
        return string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(dbType, tableName)
            : $"{QuoteIdentifier(dbType, schema)}.{QuoteIdentifier(dbType, tableName)}";
    }

    internal static string QuoteTableName(string schema, string tableName, Func<string, string> quoteIdentifier)
    {
        return string.IsNullOrWhiteSpace(schema)
            ? quoteIdentifier(tableName)
            : $"{quoteIdentifier(schema)}.{quoteIdentifier(tableName)}";
    }

    internal static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

    internal static string EscapeCsv(string value)
    {
        return value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
