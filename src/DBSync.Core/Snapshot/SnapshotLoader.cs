using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBSync.Core.Models;
using SnapshotModel = DBSync.Core.Models.Snapshot;

namespace DBSync.Core.Snapshot;

/// <summary>
/// .dbsync 快照加载器。
///</summary>
public sealed class SnapshotLoader : ISnapshotLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 从文件头读取明文密码提示。
    /// </summary>
    /// <param name="inputStream">.dbsync 文件流</param>
    /// <returns>密码提示，未设置时返回 null</returns>
    public async Task<string?> ReadPasswordHintAsync(Stream inputStream)
    {
        var originalPosition = inputStream.CanSeek ? inputStream.Position : 0;
        var header = await SnapshotFileFormat.ReadHeaderAsync(inputStream);

        if (inputStream.CanSeek)
            inputStream.Position = originalPosition;

        return header.PasswordHint;
    }

    /// <summary>
    /// 解密并加载完整快照。
    /// </summary>
    /// <param name="inputStream">.dbsync 文件流</param>
    /// <param name="password">解密密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>快照对象</returns>
    /// <exception cref="InvalidOperationException">密码错误或文件损坏时抛出</exception>
    public async Task<SnapshotModel> LoadAsync(
        Stream inputStream,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var header = await SnapshotFileFormat.ReadHeaderAsync(inputStream);
            await using var cryptoStream = SnapshotFileFormat.CreateDecryptStream(inputStream, password, header);
            using var archive = new ZipArchive(cryptoStream, ZipArchiveMode.Read);

            var manifest = await ReadJsonEntryAsync<SnapshotManifest>(archive, "manifest.json", cancellationToken);
            var tables = new Dictionary<string, TableModel>(StringComparer.OrdinalIgnoreCase);
            var fingerprints = new Dictionary<string, IReadOnlyList<RowHash>>(StringComparer.OrdinalIgnoreCase);
            var fullData = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("schema/", StringComparison.OrdinalIgnoreCase)))
            {
                var table = await ReadJsonEntryAsync<TableModel>(entry, cancellationToken);
                tables[table.FullName] = table;
            }

            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("data_fingerprint/", StringComparison.OrdinalIgnoreCase)))
            {
                var tableName = Path.GetFileNameWithoutExtension(entry.Name);
                fingerprints[tableName] = await ReadFingerprintsAsync(entry, cancellationToken);
            }

            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("data_full/", StringComparison.OrdinalIgnoreCase)))
            {
                var tableName = entry.Name.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase)
                    ? entry.Name[..^".csv.gz".Length]
                    : Path.GetFileNameWithoutExtension(entry.Name);
                fullData[tableName] = await ReadFullDataAsync(entry, cancellationToken);
            }

            return new SnapshotModel
            {
                Manifest = manifest,
                Tables = tables,
                DataFingerprints = fingerprints,
                FullData = fullData
            };
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or CryptographicException)
        {
            throw new InvalidOperationException("密码错误或 .dbsync 文件已损坏。", ex);
        }
    }

    /// <summary>
    /// 从 ZIP 中读取 JSON 条目。
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="archive">ZIP 包</param>
    /// <param name="entryName">条目名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>反序列化对象</returns>
    private static async Task<T> ReadJsonEntryAsync<T>(ZipArchive archive, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"快照缺少 {entryName}。");
        return await ReadJsonEntryAsync<T>(entry, cancellationToken);
    }

    /// <summary>
    /// 从 ZIP 条目读取 JSON。
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="entry">ZIP 条目</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>反序列化对象</returns>
    private static async Task<T> ReadJsonEntryAsync<T>(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidOperationException($"快照条目 {entry.FullName} 内容为空。");
    }

    /// <summary>
    /// 读取 GZip 压缩的行指纹 JSON Lines。
    /// </summary>
    /// <param name="entry">ZIP 条目</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>行指纹列表</returns>
    private static async Task<IReadOnlyList<RowHash>> ReadFingerprintsAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        var rows = new List<RowHash>();
        await using var stream = entry.Open();
        await using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream);

        var content = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return rows;

        foreach (var json in ReadJsonObjects(content))
            rows.Add(JsonSerializer.Deserialize<RowHash>(json, JsonOptions)!);

        return rows;
    }

    private static IEnumerable<string> ReadJsonObjects(string content)
    {
        var depth = 0;
        var start = -1;
        var inString = false;
        var escaped = false;

        for (var i = 0; i < content.Length; i++)
        {
            var ch = content[i];
            if (inString)
            {
                escaped = ch == '\\' && !escaped;
                if (ch == '"' && !escaped)
                    inString = false;
                else if (ch != '\\')
                    escaped = false;
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                escaped = false;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return content[start..(i + 1)];
                    start = -1;
                }
            }
        }
    }

    /// <summary>
    /// 读取 GZip 压缩的完整数据 CSV。
    /// </summary>
    /// <param name="entry">ZIP 条目</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>完整行数据</returns>
    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> ReadFullDataAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        await using var stream = entry.Open();
        await using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream);
        var header = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(header))
            return rows;

        var columns = ParseCsvLine(header);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var values = ParseCsvLine(line);
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Count; i++)
            {
                var value = i < values.Count ? values[i] : null;
                row[columns[i]] = value == "\\N" ? null : value;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// 解析单行 CSV。
    /// </summary>
    /// <param name="line">CSV 行文本</param>
    /// <returns>单元格列表</returns>
    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && inQuotes && i + 1 < line.Length && line[i + 1] == '"')
            {
                value.Append('"');
                i++;
            }
            else if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                values.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(ch);
            }
        }

        values.Add(value.ToString());
        return values;
    }
}
