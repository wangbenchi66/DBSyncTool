using System.Net;
using System.Text;
using DBSync.Core.Comparers;
using DBSync.Core.Models;

namespace DBSync.Desktop.Services;

public sealed class DiffReportExporter
{
    public string BuildMarkdownReport(
        Snapshot snapshot,
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        string? sourceConnectionName = null)
    {
        var text = new StringBuilder();
        text.AppendLine("# 差异报告");
        text.AppendLine();
        AppendMetaMarkdown(text, snapshot.Manifest, sourceConnectionName);
        AppendSchemaMarkdown(text, schemaDiff);
        AppendDataMarkdown(text, snapshot, dataDiffs);
        AppendListMarkdown(text, "循环依赖警告", schemaDiff.CyclicDependencyGroups.Select(group => string.Join("、", group)).ToList());
        AppendListMarkdown(text, "无主键表跳过列表", snapshot.Tables.Values.Where(table => !table.HasPrimaryKey).Select(table => table.FullName).ToList());
        return text.ToString().TrimEnd();
    }

    public string BuildHtmlReport(
        Snapshot snapshot,
        SchemaDiff schemaDiff,
        IReadOnlyDictionary<string, DataDiff> dataDiffs,
        string? sourceConnectionName = null)
    {
        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"zh-CN\"><head><meta charset=\"utf-8\" />");
        html.AppendLine("<title>DBSyncTool 差异报告</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#111827}h1,h2{margin:0 0 12px}table{border-collapse:collapse;width:100%;margin:8px 0 20px}th,td{border:1px solid #d1d5db;padding:8px;vertical-align:top;text-align:left}th{background:#f3f4f6}.muted{color:#6b7280}.warn{color:#b45309}.danger{color:#b91c1c}.ok{color:#166534}ul{margin:8px 0 20px}</style>");
        html.AppendLine("</head><body>");
        html.AppendLine("<h1>差异报告</h1>");
        AppendMetaHtml(html, snapshot.Manifest, sourceConnectionName);
        AppendSchemaHtml(html, schemaDiff);
        AppendDataHtml(html, snapshot, dataDiffs);
        AppendListHtml(html, "循环依赖警告", schemaDiff.CyclicDependencyGroups.Select(group => string.Join("、", group)).ToList());
        AppendListHtml(html, "无主键表跳过列表", snapshot.Tables.Values.Where(table => !table.HasPrimaryKey).Select(table => table.FullName).ToList());
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static void AppendMetaMarkdown(StringBuilder text, SnapshotManifest manifest, string? sourceConnectionName)
    {
        text.AppendLine("## 快照信息");
        text.AppendLine($"- 导出时间：{manifest.ExportedAt:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"- 数据库类型：{manifest.DbType}");
        text.AppendLine($"- 表数量：{manifest.TableNames.Count}");
        if (!string.IsNullOrWhiteSpace(sourceConnectionName))
            text.AppendLine($"- 比对连接：{sourceConnectionName}");
        text.AppendLine();
    }

    private static void AppendSchemaMarkdown(StringBuilder text, SchemaDiff schemaDiff)
    {
        text.AppendLine("## 结构差异汇总");
        text.AppendLine("| 表名 | 变更类型 | 内容 |");
        text.AppendLine("| --- | --- | --- |");

        foreach (var row in BuildSchemaRows(schemaDiff))
            text.AppendLine($"| {EscapeMarkdown(row.TableName)} | {row.ChangeType} | {EscapeMarkdown(row.Details)} |");

        text.AppendLine();
    }

    private static void AppendDataMarkdown(
        StringBuilder text,
        Snapshot snapshot,
        IReadOnlyDictionary<string, DataDiff> dataDiffs)
    {
        text.AppendLine("## 数据差异汇总");
        text.AppendLine("| 表名 | 新增 | 删除 | 变更 | 状态 |");
        text.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var table in snapshot.Tables.Values.OrderBy(table => table.FullName))
        {
            var diff = dataDiffs.TryGetValue(table.FullName, out var current) ? current : DataDiff.Empty;
            var status = diff.Skipped
                ? "跳过：无主键"
                : diff.DeletedRows.Count > 0 || diff.ChangedRows.Count > 0
                    ? "需人工审阅"
                    : "正常";

            text.AppendLine($"| {EscapeMarkdown(table.FullName)} | {diff.RowsToInsert.Count} | {diff.DeletedRows.Count} | {diff.ChangedRows.Count} | {status} |");
        }

        text.AppendLine();
    }

    private static void AppendListMarkdown(StringBuilder text, string title, IReadOnlyList<string> items)
    {
        text.AppendLine($"## {title}");
        if (items.Count == 0)
        {
            text.AppendLine("- 无");
        }
        else
        {
            foreach (var item in items)
                text.AppendLine($"- {EscapeMarkdown(item)}");
        }

        text.AppendLine();
    }

    private static void AppendMetaHtml(StringBuilder html, SnapshotManifest manifest, string? sourceConnectionName)
    {
        html.AppendLine("<h2>快照信息</h2><ul>");
        html.AppendLine($"<li>导出时间：{WebUtility.HtmlEncode(manifest.ExportedAt.ToString("yyyy-MM-dd HH:mm:ss"))}</li>");
        html.AppendLine($"<li>数据库类型：{WebUtility.HtmlEncode(manifest.DbType.ToString())}</li>");
        html.AppendLine($"<li>表数量：{manifest.TableNames.Count}</li>");
        if (!string.IsNullOrWhiteSpace(sourceConnectionName))
            html.AppendLine($"<li>比对连接：{WebUtility.HtmlEncode(sourceConnectionName)}</li>");
        html.AppendLine("</ul>");
    }

    private static void AppendSchemaHtml(StringBuilder html, SchemaDiff schemaDiff)
    {
        html.AppendLine("<h2>结构差异汇总</h2><table><thead><tr><th>表名</th><th>变更类型</th><th>内容</th></tr></thead><tbody>");
        foreach (var row in BuildSchemaRows(schemaDiff))
        {
            html.AppendLine("<tr>");
            html.AppendLine($"<td>{WebUtility.HtmlEncode(row.TableName)}</td>");
            html.AppendLine($"<td class=\"{GetHtmlClass(row.Warning, row.ChangeType)}\">{WebUtility.HtmlEncode(row.ChangeType)}</td>");
            html.AppendLine($"<td>{WebUtility.HtmlEncode(row.Details)}</td>");
            html.AppendLine("</tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    private static void AppendDataHtml(
        StringBuilder html,
        Snapshot snapshot,
        IReadOnlyDictionary<string, DataDiff> dataDiffs)
    {
        html.AppendLine("<h2>数据差异汇总</h2><table><thead><tr><th>表名</th><th>新增</th><th>删除</th><th>变更</th><th>状态</th></tr></thead><tbody>");
        foreach (var table in snapshot.Tables.Values.OrderBy(table => table.FullName))
        {
            var diff = dataDiffs.TryGetValue(table.FullName, out var current) ? current : DataDiff.Empty;
            var status = diff.Skipped
                ? "跳过：无主键"
                : diff.DeletedRows.Count > 0 || diff.ChangedRows.Count > 0
                    ? "需人工审阅"
                    : "正常";

            html.AppendLine("<tr>");
            html.AppendLine($"<td>{WebUtility.HtmlEncode(table.FullName)}</td>");
            html.AppendLine($"<td>{diff.RowsToInsert.Count}</td>");
            html.AppendLine($"<td>{diff.DeletedRows.Count}</td>");
            html.AppendLine($"<td>{diff.ChangedRows.Count}</td>");
            html.AppendLine($"<td class=\"{GetHtmlClass(diff.Skipped || status.Contains("审阅", StringComparison.Ordinal), status)}\">{WebUtility.HtmlEncode(status)}</td>");
            html.AppendLine("</tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    private static void AppendListHtml(StringBuilder html, string title, IReadOnlyList<string> items)
    {
        html.AppendLine($"<h2>{WebUtility.HtmlEncode(title)}</h2>");
        if (items.Count == 0)
        {
            html.AppendLine("<p class=\"muted\">无</p>");
            return;
        }

        html.AppendLine("<ul>");
        foreach (var item in items)
            html.AppendLine($"<li>{WebUtility.HtmlEncode(item)}</li>");
        html.AppendLine("</ul>");
    }

    private static IReadOnlyList<SchemaRow> BuildSchemaRows(SchemaDiff schemaDiff)
    {
        var rows = new List<SchemaRow>();

        foreach (var table in schemaDiff.AddedTables.OrderBy(table => table.FullName))
            rows.Add(new SchemaRow(table.FullName, "新增", $"列 {table.Columns.Count}，索引 {table.Indexes.Count}"));

        foreach (var table in schemaDiff.RemovedTables.OrderBy(table => table.FullName))
            rows.Add(new SchemaRow(table.FullName, "删除", "目标库中不存在", true));

        foreach (var diff in schemaDiff.ModifiedTables.OrderBy(diff => diff.SourceTable.FullName))
        {
            var details = new List<string>();
            details.AddRange(diff.ColumnDiffs.Select(DescribeColumnDiff));
            details.AddRange(diff.IndexDiffs.Select(DescribeIndexDiff));
            if (diff.PrimaryKeyChanged)
                details.Add("主键定义已变更");

            rows.Add(new SchemaRow(diff.SourceTable.FullName, "变更", string.Join("；", details)));
        }

        return rows;
    }

    private static string DescribeColumnDiff(ColumnDiff diff)
    {
        return diff.DiffType switch
        {
            ColumnDiffType.Added => $"列 {diff.After?.Name} 新增",
            ColumnDiffType.Removed => $"列 {diff.Before?.Name} 删除",
            _ => $"列 {diff.After?.Name ?? diff.Before?.Name} 修改"
        };
    }

    private static string DescribeIndexDiff(IndexDiff diff)
    {
        return diff.DiffType switch
        {
            IndexDiffType.Added => $"索引 {diff.After?.Name} 新增",
            IndexDiffType.Removed => $"索引 {diff.Before?.Name} 删除",
            _ => $"索引 {diff.After?.Name ?? diff.Before?.Name} 修改"
        };
    }

    private static string EscapeMarkdown(string value)
    {
        return value.Replace("|", "\\|");
    }

    private static string GetHtmlClass(bool warning, string value)
    {
        if (warning)
            return "warn";

        if (value.Contains("删除", StringComparison.Ordinal))
            return "danger";

        if (value.Contains("新增", StringComparison.Ordinal))
            return "ok";

        return "muted";
    }

    private sealed record SchemaRow(string TableName, string ChangeType, string Details, bool Warning = false);
}
