using System.Text.RegularExpressions;

namespace DBSync.Core.Extensions;

/// <summary>
/// SQL Server 连接字符串补全扩展
/// </summary>
/// <remarks>
/// 原由 Easy.SqlSugar.Core 提供（扩展方法 CheckTrustServerCertificate / CheckEncrypt），
/// 本次去除该元包依赖后内联到本文件。语义保持一致：仅当连接字符串中缺少对应关键字时
/// 才在末尾追加，已包含该关键字则不修改原字符串。
/// </remarks>
public static class ConnectionStringExtensions
{
    /// <summary>
    /// 校验并补全 TrustServerCertificate 关键字
    /// </summary>
    /// <remarks>
    /// 连接字符串已包含 TrustServerCertificate 时不作修改；
    /// 否则在末尾追加 TrustServerCertificate=true（配合 Encrypt 使用时允许信任自签名证书）。
    /// </remarks>
    /// <param name="connectionString">原始连接字符串</param>
    /// <returns>补全后的连接字符串</returns>
    public static string CheckTrustServerCertificate(this string connectionString)
        => connectionString.AppendKeywordIfMissing("TrustServerCertificate", "true");

    /// <summary>
    /// 校验并补全 Encrypt 关键字
    /// </summary>
    /// <remarks>
    /// 连接字符串已包含 Encrypt 时不作修改；否则在末尾追加 Encrypt=True。
    /// Microsoft.Data.SqlClient 5.x 默认已强制加密，此处显式声明并与
    /// <see cref="CheckTrustServerCertificate"/> 配对，确保自签名证书场景下仍可正常建立加密连接。
    /// </remarks>
    /// <param name="connectionString">原始连接字符串</param>
    /// <returns>补全后的连接字符串</returns>
    public static string CheckEncrypt(this string connectionString)
        => connectionString.AppendKeywordIfMissing("Encrypt", "True");

    /// <summary>
    /// 若连接字符串缺少指定关键字，则在末尾追加 <c>keyword=value</c>；已包含则原样返回。
    /// </summary>
    /// <param name="connectionString">原始连接字符串</param>
    /// <param name="keyword">要检查并追加的关键字名称</param>
    /// <param name="value">追加时的取值</param>
    /// <returns>处理后的连接字符串</returns>
    private static string AppendKeywordIfMissing(this string connectionString, string keyword, string value)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return $"{keyword}={value}";

        // 匹配以分号或串首开头的指定关键字（忽略大小写，允许关键字与等号间有空白）
        var pattern = $@"(^|;)\s*{Regex.Escape(keyword)}\s*=";
        if (Regex.IsMatch(connectionString, pattern, RegexOptions.IgnoreCase))
            return connectionString;

        // 原字符串未以分号结尾时补一个分隔符，避免关键字与末段粘连
        var separator = connectionString.TrimEnd().EndsWith(";") ? string.Empty : ";";
        return connectionString + separator + keyword + "=" + value;
    }
}
