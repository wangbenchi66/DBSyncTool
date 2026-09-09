using System.Text.Json.Serialization;
using DBSync.Core.Versioning;

namespace DBSync.Desktop.Models;

/// <summary>
/// GitHub Releases API 的响应模型（仅保留检查更新所需字段）。
///</summary>
public sealed class GitHubRelease
{
    /// <summary>
    /// 发布标签名（如 v1.2.3）
    ///</summary>
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    /// <summary>
    /// Release 页面地址（供浏览器打开下载）
    ///</summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    /// <summary>
    /// 是否预发布
    ///</summary>
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    /// <summary>
    /// 该 Release 是否比当前运行版本更新（仅 UI 使用）
    ///</summary>
    [JsonIgnore]
    public bool IsNewerThanCurrent =>
        AppVersion.TryParse(TagName, out var latest) && latest.IsNewerThan(AppVersion.Current);
}

/// <summary>
/// GitHubRelease 的源生成 JSON 序列化上下文（规避裁剪下反射序列化风险）。
///</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class ReleaseJsonContext : JsonSerializerContext;
