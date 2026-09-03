using System.Text;
using System.Text.Json;
using DBSync.Core.Comparers;
using DBSync.Core.Data;
using DBSync.Core.Execution;
using DBSync.Core.Extensions;
using DBSync.Core.Models;
using DBSync.Core.Schema;
using DBSync.Core.Snapshot;
using DBSync.Core.SqlGenerators;
using Microsoft.Extensions.DependencyInjection;

namespace DBSync.CLI;

/// <summary>
/// DBSyncTool CLI 入口，支持 export / compare / script / execute 四个子命令
///</summary>
public static class Program
{
    /// <summary>
    /// JSON 序列化选项
    ///</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// CLI 主入口
    ///</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var services = new ServiceCollection();
        services.AddDbSyncCore();
        var sp = services.BuildServiceProvider();

        var command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "export" => await RunExportAsync(args, sp),
                "compare" => await RunCompareAsync(args, sp),
                "script" => await RunScriptAsync(args, sp),
                "execute" => await RunExecuteAsync(args, sp),
                "help" or "--help" or "-h" => PrintUsage(),
                _ => PrintError($"未知命令：{command}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// 导出快照
    ///</summary>
    private static async Task<int> RunExportAsync(string[] args, IServiceProvider sp)
    {
        var connStr = GetArg(args, "--connection");
        var dbTypeStr = GetArg(args, "--db-type");
        var output = GetArg(args, "--output");
        var password = GetArg(args, "--password") ?? "";

        if (connStr is null || dbTypeStr is null || output is null)
        {
            Console.Error.WriteLine("用法：dbsync export --connection <连接字符串> --db-type <mysql|sqlserver|postgresql|sqlite> --output <路径> [--password <密码>]");
            return 2;
        }

        var conn = BuildConnection(connStr, dbTypeStr);
        var exporter = sp.GetRequiredService<ISnapshotExporter>();
        var schemaReader = sp.GetRequiredService<ISchemaReader>();

        Console.WriteLine("正在读取表结构...");
        var tables = await schemaReader.ReadAllTablesAsync(conn);
        Console.WriteLine($"发现 {tables.Count} 张表");

        var options = new ExportOptions
        {
            Password = password,
            Tables = tables.Select(t => new TableExportOptions
            {
                TableName = t.FullName,
                SyncSchema = true,
                SyncData = t.HasPrimaryKey
            }).ToList()
        };

        await using var stream = File.Create(output);
        var progress = new Progress<(int current, int total, string tableName, long currentRow)>(p =>
            Console.Write($"\r导出中 {p.current}/{p.total}：{p.tableName} ({p.currentRow} 行)    "));

        await exporter.ExportAsync(conn, options, stream, progress);
        Console.WriteLine($"\n快照已保存：{output}");
        return 0;
    }

    /// <summary>
    /// 比对（快照或直连）
    ///</summary>
    private static async Task<int> RunCompareAsync(string[] args, IServiceProvider sp)
    {
        var snapshotPath = GetArg(args, "--snapshot");
        var snapshotPassword = GetArg(args, "--snapshot-password") ?? "";
        var connStr = GetArg(args, "--connection");
        var dbTypeStr = GetArg(args, "--db-type");
        var outputFormat = GetArg(args, "--output-format") ?? "text";
        var outputPath = GetArg(args, "--output");

        if (snapshotPath is null || connStr is null || dbTypeStr is null)
        {
            Console.Error.WriteLine("用法：dbsync compare --snapshot <快照路径> --connection <连接字符串> --db-type <类型> [--snapshot-password <密码>] [--output-format json|text] [--output <路径>]");
            return 2;
        }

        var conn = BuildConnection(connStr, dbTypeStr);
        var schemaReader = sp.GetRequiredService<ISchemaReader>();
        var snapshotLoader = sp.GetRequiredService<ISnapshotLoader>();
        var fingerprinter = sp.GetRequiredService<IDataFingerprinter>();

        Console.WriteLine("正在加载快照...");
        await using var stream = File.OpenRead(snapshotPath);
        var snapshot = await snapshotLoader.LoadAsync(stream, snapshotPassword);

        Console.WriteLine("正在读取目标库结构...");
        var targetTables = await schemaReader.ReadAllTablesAsync(conn);

        Console.WriteLine("正在比对结构...");
        var schemaDiff = SchemaComparer.Compare(targetTables, snapshot.Tables.Values);

        Console.WriteLine("正在比对数据...");
        var dataDiffs = new Dictionary<string, DataDiff>(StringComparer.OrdinalIgnoreCase);
        var targetMap = targetTables.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var table in snapshot.Tables.Values)
        {
            var snapshotRows = snapshot.DataFingerprints.TryGetValue(table.FullName, out var rows) ? rows : [];
            if (!targetMap.TryGetValue(table.FullName, out var targetTable) || !table.HasPrimaryKey || !targetTable.HasPrimaryKey)
            {
                dataDiffs[table.FullName] = !table.HasPrimaryKey ? DataDiff.NoPrimaryKey : DataComparer.Compare([], snapshotRows, false);
                continue;
            }

            var targetRows = new List<RowHash>();
            await foreach (var row in fingerprinter.ReadRowHashesAsync(conn, targetTable))
                targetRows.Add(row);

            dataDiffs[table.FullName] = DataComparer.Compare(targetRows, snapshotRows, false);
        }

        var result = new
        {
            Timestamp = DateTimeOffset.Now,
            Source = new { Type = "snapshot", Path = snapshotPath },
            Target = new { Type = dbTypeStr, Connection = "***" },
            Schema = new
            {
                Added = schemaDiff.AddedTables.Select(t => t.FullName).ToList(),
                Removed = schemaDiff.RemovedTables.Select(t => t.FullName).ToList(),
                Modified = schemaDiff.ModifiedTables.Select(t => t.SourceTable.FullName).ToList(),
            },
            Data = dataDiffs.Where(kv => !kv.Value.Skipped).Select(kv => new
            {
                Name = kv.Key,
                Inserted = kv.Value.RowsToInsert.Count,
                Deleted = kv.Value.DeletedRows.Count,
                Changed = kv.Value.ChangedRows.Count
            }).Where(d => d.Inserted > 0 || d.Deleted > 0 || d.Changed > 0).ToList(),
            Summary = new
            {
                HasChanges = schemaDiff.HasChanges || dataDiffs.Values.Any(d => d.RowsToInsert.Count > 0 || d.DeletedRows.Count > 0 || d.ChangedRows.Count > 0),
                DdlCount = schemaDiff.AddedTables.Count + schemaDiff.RemovedTables.Count + schemaDiff.ModifiedTables.Count,
                DmlCount = dataDiffs.Values.Sum(d => d.RowsToInsert.Count)
            }
        };

        var output = outputFormat == "json"
            ? JsonSerializer.Serialize(result, JsonOptions)
            : FormatTextResult(result.Summary.HasChanges, schemaDiff, dataDiffs);

        if (outputPath is not null)
        {
            await File.WriteAllTextAsync(outputPath, output);
            Console.WriteLine($"结果已保存：{outputPath}");
        }
        else
        {
            Console.WriteLine(output);
        }

        return result.Summary.HasChanges ? 1 : 0;
    }

    /// <summary>
    /// 生成升级脚本
    ///</summary>
    private static async Task<int> RunScriptAsync(string[] args, IServiceProvider sp)
    {
        var snapshotPath = GetArg(args, "--snapshot");
        var snapshotPassword = GetArg(args, "--snapshot-password") ?? "";
        var connStr = GetArg(args, "--connection");
        var dbTypeStr = GetArg(args, "--db-type");
        var output = GetArg(args, "--output");
        var useTransaction = !HasFlag(args, "--no-transaction");

        if (snapshotPath is null || connStr is null || dbTypeStr is null || output is null)
        {
            Console.Error.WriteLine("用法：dbsync script --snapshot <快照路径> --connection <连接字符串> --db-type <类型> --output <路径> [--snapshot-password <密码>] [--no-transaction]");
            return 2;
        }

        var conn = BuildConnection(connStr, dbTypeStr);
        var schemaReader = sp.GetRequiredService<ISchemaReader>();
        var snapshotLoader = sp.GetRequiredService<ISnapshotLoader>();
        var sqlGenerator = sp.GetRequiredService<ISqlGenerator>();
        var fingerprinter = sp.GetRequiredService<IDataFingerprinter>();

        await using var stream = File.OpenRead(snapshotPath);
        var snapshot = await snapshotLoader.LoadAsync(stream, snapshotPassword);
        var targetTables = await schemaReader.ReadAllTablesAsync(conn);
        var schemaDiff = SchemaComparer.Compare(targetTables, snapshot.Tables.Values);

        var dataDiffs = new Dictionary<string, DataDiff>(StringComparer.OrdinalIgnoreCase);
        var targetMap = targetTables.ToDictionary(t => t.FullName, t => t, StringComparer.OrdinalIgnoreCase);
        foreach (var table in snapshot.Tables.Values)
        {
            var snapshotRows = snapshot.DataFingerprints.TryGetValue(table.FullName, out var rows) ? rows : [];
            if (!targetMap.TryGetValue(table.FullName, out var targetTable) || !table.HasPrimaryKey || !targetTable.HasPrimaryKey)
            {
                dataDiffs[table.FullName] = !table.HasPrimaryKey ? DataDiff.NoPrimaryKey : DataComparer.Compare([], snapshotRows, false);
                continue;
            }
            var targetRows = new List<RowHash>();
            await foreach (var row in fingerprinter.ReadRowHashesAsync(conn, targetTable))
                targetRows.Add(row);
            dataDiffs[table.FullName] = DataComparer.Compare(targetRows, snapshotRows, false);
        }

        var script = sqlGenerator.GenerateUpgradeScript(conn.DbType, schemaDiff, dataDiffs, snapshot.FullData, useTransaction);
        await File.WriteAllTextAsync(output, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Console.WriteLine($"脚本已保存：{output}");
        return 0;
    }

    /// <summary>
    /// 执行脚本
    ///</summary>
    private static async Task<int> RunExecuteAsync(string[] args, IServiceProvider sp)
    {
        var connStr = GetArg(args, "--connection");
        var dbTypeStr = GetArg(args, "--db-type");
        var scriptPath = GetArg(args, "--script");
        var dryRun = HasFlag(args, "--dry-run");

        if (connStr is null || dbTypeStr is null || scriptPath is null)
        {
            Console.Error.WriteLine("用法：dbsync execute --connection <连接字符串> --db-type <类型> --script <脚本路径> [--dry-run]");
            return 2;
        }

        var executor = sp.GetRequiredService<IScriptExecutor>();
        var script = await File.ReadAllTextAsync(scriptPath);

        if (dryRun)
        {
            var plan = executor.DryRun(script);
            Console.WriteLine($"Dry Run 结果：");
            Console.WriteLine($"  总语句数：{plan.TotalStatements}");
            Console.WriteLine($"  DDL：{plan.DdlCount} 条");
            Console.WriteLine($"  DML：{plan.DmlCount} 条");
            Console.WriteLine($"  事务：{(plan.HasTransaction ? "是" : "否")}");
            foreach (var ddl in plan.DdlStatements)
                Console.WriteLine($"    {ddl}");
            return 0;
        }

        var conn = BuildConnection(connStr, dbTypeStr);
        var progress = new Progress<ScriptExecutionProgress>(p =>
            Console.Write($"\r执行中 {p.Current}/{p.Total}：{p.CurrentStatement}    "));

        var result = await executor.ExecuteAsync(conn, script, progress);
        Console.WriteLine();

        if (result.Success)
        {
            Console.WriteLine($"执行成功：{result.ExecutedStatements} 条语句，影响 {result.AffectedRows} 行，耗时 {result.Duration.TotalSeconds:F1}s");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"执行失败：{result.Error}");
            if (result.FailedStatement is not null)
                Console.Error.WriteLine($"失败语句：{result.FailedStatement}");
            return 2;
        }
    }

    /// <summary>
    /// 构建数据库连接对象
    ///</summary>
    private static DatabaseConnection BuildConnection(string connectionString, string dbType)
    {
        var type = dbType.ToLowerInvariant() switch
        {
            "mysql" => DatabaseType.MySql,
            "sqlserver" or "mssql" => DatabaseType.SqlServer,
            "postgresql" or "postgres" => DatabaseType.PostgreSql,
            "sqlite" => DatabaseType.Sqlite,
            _ => throw new ArgumentException($"不支持的数据库类型：{dbType}")
        };

        return new DatabaseConnection
        {
            Name = "CLI",
            DbType = type,
            ConnectionString = connectionString
        };
    }

    /// <summary>
    /// 获取命令行参数值
    ///</summary>
    private static string? GetArg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// 检查是否存在指定标志
    ///</summary>
    private static bool HasFlag(string[] args, string flag) => args.Contains(flag);

    /// <summary>
    /// 格式化文本结果
    ///</summary>
    private static string FormatTextResult(bool hasChanges, SchemaDiff schema, Dictionary<string, DataDiff> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine(hasChanges ? "比对结果：存在差异" : "比对结果：完全相同");
        sb.AppendLine($"结构：新增 {schema.AddedTables.Count}，删除 {schema.RemovedTables.Count}，变更 {schema.ModifiedTables.Count}");
        sb.AppendLine($"数据：新增 {data.Values.Sum(d => d.RowsToInsert.Count)} 行，删除 {data.Values.Sum(d => d.DeletedRows.Count)} 行，变更 {data.Values.Sum(d => d.ChangedRows.Count)} 行");
        return sb.ToString();
    }

    /// <summary>
    /// 打印用法说明
    ///</summary>
    private static int PrintUsage()
    {
        Console.WriteLine("""
            DBSyncTool CLI v3.0

            用法：dbsync <命令> [选项]

            命令：
              export   导出数据库快照
              compare  比对快照与目标库
              script   生成升级脚本
              execute  执行脚本到目标库

            退出码：
              0  成功/无差异
              1  有差异
              2  错误

            详细用法：dbsync <命令> --help
            """);
        return 0;
    }

    /// <summary>
    /// 打印错误信息
    ///</summary>
    private static int PrintError(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }
}
