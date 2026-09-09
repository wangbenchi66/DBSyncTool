using System.Net;
using System.Text.Json;
using DBSync.Core.Versioning;
using DBSync.Desktop.Models;

namespace DBSync.Desktop.Services;

/// <summary>
/// 检查 GitHub Releases 最新版本的服务。
/// 所有失败均静默返回 null（无网 / 超时 / 无 Release / 仓库私有等），不打扰用户。
/// 注意：ReleaseApiUrl 与仓库地址绑定，换仓库需同步修改。
///</summary>
public sealed class UpdateChecker
{
    /// <summary>
    /// GitHub Releases API：返回最新正式（非预发布、非草稿）Release
    ///</summary>
    private const string ReleaseApiUrl = "https://api.github.com/repos/wangbenchi66/DBSyncTool/releases/latest";

    /// <summary>
    /// 供请求 GitHub API 的 HttpClient（GitHub 要求携带 User-Agent）
    ///</summary>
    private static readonly HttpClient Client = CreateClient();

    /// <summary>
    /// 创建配置好请求头与超时的 HttpClient
    ///</summary>
    /// <returns>HttpClient 实例</returns>
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DBSyncTool/" + AppVersion.Current);
        return client;
    }

    /// <summary>
    /// 查询仓库最新 Release；失败或无 Release 时返回 null
    ///</summary>
    /// <returns>最新 Release，或 null</returns>
    public async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        try
        {
            using var response = await Client.GetAsync(ReleaseApiUrl);
            // 仓库尚无任何正式 Release
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize(json, ReleaseJsonContext.Default.GitHubRelease);
        }
        catch (HttpRequestException)
        {
            // 网络异常静默
            return null;
        }
        catch (TaskCanceledException)
        {
            // 超时静默
            return null;
        }
        catch (JsonException)
        {
            // 响应格式异常静默
            return null;
        }
    }
}
