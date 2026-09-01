using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DBSync.Core.Data;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using Easy.SqlSugar.Core.Common;
using Microsoft.Data.SqlClient;

namespace DBSync.Core.Snapshot;

/// <summary>
/// .dbsync 快照导出器。
///</summary>
public sealed class SnapshotExporter(ISchemaReader schemaReader, SqlServerDataFingerprinter fingerprinter) : ISnapshotExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// 导出加密快照。
    /// </summary>
    /// <param name="connection">源数据库连接</param>
    /// <param name="options">导出选项</param>
    /// <param name="outputStream">输出流</param>
    /// <param name="progress">进度报告</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ExportAsync(
        DatabaseConnection connection,
        ExportOptions options,
        Stream outputStream,
        IProgress<(int current, int total, string tableName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var header = await SnapshotFileFormat.WriteHeaderAsync(outputStream, options.PasswordHint);
        await using var cryptoStream = SnapshotFileFormat.CreateEncryptStream(outputStream, options.Password, header);
        using var archive = new ZipArchive(cryptoStream, ZipArchiveMode.Create);

        var allTables = await schemaReader.ReadAllTablesAsync(connection, cancellationToken);
        var selectedTables = SelectTables(allTables, options.Tables);
        var manifest = new SnapshotManifest
        {
            Version = "1",
            DbType = connection.DbType,
            ExportedAt = DateTimeOffset.Now,
            TableNames = selectedTables.Select(t => t.FullName).ToList(),
            PasswordHint = options.PasswordHint
        };

        await WriteJsonEntryAsync(archive, "manifest.json", manifest, cancellationToken);

        for (var i = 0; i < selectedTables.Count; i++)
        {
            var table = selectedTables[i];
            var tableOptions = FindOptions(options.Tables, table);
            progress?.Report((i + 1, selectedTables.Count, table.FullName));

            if (tableOptions.SyncSchema)
                await WriteJsonEntryAsync(archive, $"schema/{table.FullName}.json", table, cancellationToken);

            await WriteFingerprintsAsync(archive, connection, table, tableOptions.WhereClause, cancellationToken);

            if (tableOptions.SyncData)
                await WriteFullDataAsync(archive, connection, table, tableOptions.WhereClause, cancellationToken);
        }
    }

    /// <summary>
    /// 写入 JSON 条目。
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="archive">ZIP 包</param>
    /// <param name="entryName">条目名</param>
    /// <param name="value">写入对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    /// <summary>
    /// 写入行指纹条目。
    /// </summary>
    /// <param name="archive">ZIP 包</param>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="table">表模型</param>
    /// <param name="whereClause">WHERE 子句</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task WriteFingerprintsAsync(
        ZipArchive archive,
        DatabaseConnection connection,
        TableModel table,
        string? whereClause,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry($"data_fingerprint/{table.FullName}.fp", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var gzipStream = new GZipStream(stream, CompressionMode.Compress);
        await using var writer = new StreamWriter(gzipStream);

        await foreach (var row in fingerprinter.ReadRowHashesAsync(connection, table, whereClause, cancellationToken))
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(row, JsonOptions));
        }
    }

    /// <summary>
    /// 流式写入完整行数据。
    /// </summary>
    /// <param name="archive">ZIP 包</param>
    /// <param name="connection">数据库连接配置</param>
    /// <param name="table">表模型</param>
    /// <param name="whereClause">WHERE 子句</param>
    /// <param name="cancellationToken">取消令牌</param>
    private static async Task WriteFullDataAsync(
        ZipArchive archive,
        DatabaseConnection connection,
        TableModel table,
        string? whereClause,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry($"data_full/{table.FullName}.csv.gz", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var gzipStream = new GZipStream(stream, CompressionMode.Compress);
        await using var writer = new StreamWriter(gzipStream, Encoding.UTF8);
        var columns = table.Columns.OrderBy(c => c.OrdinalPosition).ToList();

        await writer.WriteLineAsync(string.Join(",", columns.Select(c => EscapeCsv(c.Name))));

        await using var sqlConnection = new SqlConnection(connection.ConnectionString.CheckTrustServerCertificate().CheckEncrypt());
        await sqlConnection.OpenAsync(cancellationToken);
        await using var command = sqlConnection.CreateCommand();
        command.CommandText = BuildFullDataSql(table, whereClause);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new List<string>(columns.Count);
            foreach (var column in columns)
            {
                var value = reader[column.Name];
                values.Add(value == DBNull.Value ? "\\N" : EscapeCsv(Convert.ToString(value) ?? string.Empty));
            }

            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    /// <summary>
    /// 构建完整数据查询 SQL。
    /// </summary>
    /// <param name="table">表模型</param>
    /// <param name="whereClause">WHERE 子句</param>
    /// <returns>完整数据查询 SQL</returns>
    private static string BuildFullDataSql(TableModel table, string? whereClause)
    {
        var cleanedWhere = SqlServerDataFingerprinter.SanitizeWhereClause(whereClause);
        var columns = string.Join(", ", table.Columns.OrderBy(c => c.OrdinalPosition).Select(c => QuoteIdentifier(c.Name)));
        var sql = $"SELECT {columns} FROM {QuoteName(table)}";

        return string.IsNullOrWhiteSpace(cleanedWhere)
            ? sql
            : $"{sql}{Environment.NewLine}WHERE {cleanedWhere}";
    }

    /// <summary>
    /// 转义 CSV 单元格。
    /// </summary>
    /// <param name="value">单元格值</param>
    /// <returns>CSV 单元格文本</returns>
    private static string EscapeCsv(string value)
    {
        return value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    /// <summary>
    /// 根据导出选项筛选表。
    /// </summary>
    /// <param name="tables">全部表</param>
    /// <param name="options">表导出选项</param>
    /// <returns>选中的表</returns>
    private static IReadOnlyList<TableModel> SelectTables(
        IReadOnlyList<TableModel> tables,
        IReadOnlyList<TableExportOptions> options)
    {
        return tables
            .Where(table => options.Any(option => IsMatch(option.TableName, table)))
            .ToList();
    }

    /// <summary>
    /// 查找表导出选项。
    /// </summary>
    /// <param name="options">表导出选项集合</param>
    /// <param name="table">表模型</param>
    /// <returns>表导出选项</returns>
    private static TableExportOptions FindOptions(IReadOnlyList<TableExportOptions> options, TableModel table)
    {
        return options.First(option => IsMatch(option.TableName, table));
    }

    /// <summary>
    /// 判断选项表名是否匹配表模型。
    /// </summary>
    /// <param name="tableName">选项表名</param>
    /// <param name="table">表模型</param>
    /// <returns>匹配时返回 true</returns>
    private static bool IsMatch(string tableName, TableModel table)
    {
        return string.Equals(tableName, table.FullName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(tableName, table.Name, StringComparison.OrdinalIgnoreCase);
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
