using System.Text.RegularExpressions;

namespace DBSync.Core.Models;

/// <summary>
/// 比对过滤规则，控制哪些表参与比对以及忽略哪些差异
///</summary>
public sealed record FilterOptions
{
    /// <summary>
    /// 包含规则（正则表达式），匹配的表名才参与比对
    ///</summary>
    public List<string> IncludePatterns { get; init; } = [];

    /// <summary>
    /// 排除规则（正则表达式），匹配的表名排除在外
    ///</summary>
    public List<string> ExcludePatterns { get; init; } = [];

    /// <summary>
    /// 是否忽略表注释差异
    ///</summary>
    public bool IgnoreTableComments { get; init; }

    /// <summary>
    /// 是否忽略列顺序差异
    ///</summary>
    public bool IgnoreColumnOrder { get; init; }

    /// <summary>
    /// 是否忽略索引名称差异
    ///</summary>
    public bool IgnoreIndexNames { get; init; }

    /// <summary>
    /// 判断指定表名是否通过过滤规则
    ///</summary>
    /// <param name="tableName">表全名</param>
    /// <returns>通过过滤返回 true</returns>
    public bool IsTableIncluded(string tableName)
    {
        if (ExcludePatterns.Count > 0 &&
            ExcludePatterns.Any(p => Regex.IsMatch(tableName, p, RegexOptions.IgnoreCase)))
            return false;

        if (IncludePatterns.Count > 0)
            return IncludePatterns.Any(p => Regex.IsMatch(tableName, p, RegexOptions.IgnoreCase));

        return true;
    }
}
