using System.Reflection;

namespace DBSync.Core.Versioning;

/// <summary>
/// 应用版本号值对象：负责解析 git tag / 程序集版本文本并比较新旧。
/// 桌面端检查更新与 CLI 版本显示共用，避免引用第三方语义化版本库。
///</summary>
/// <param name="Major">主版本号</param>
/// <param name="Minor">次版本号</param>
/// <param name="Patch">修订号</param>
/// <param name="Prerelease">预发布标识（如 beta.1），正式版为 null</param>
public readonly record struct AppVersion(int Major, int Minor, int Patch, string? Prerelease = null)
{
    /// <summary>
    /// 从任意宽松的版本文本解析：容忍前导 v、+ 元数据（如 git 提交号）、
    /// 预发布后缀与缺段补 0（如 v3.0 视为 3.0.0）。
    ///</summary>
    /// <param name="text">版本文本，可为 null 或空白</param>
    /// <param name="version">解析成功后的版本号</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();

        // 剥离 + 元数据（SDK 会生成形如 1.0.0+g<提交号> 的 InformationalVersion）
        var plus = value.IndexOf('+');
        if (plus >= 0)
            value = value[..plus];

        // 容忍前导 v（Git tag 常为 v1.2.3）
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        // 分离预发布标识
        string? prerelease = null;
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = value[(dash + 1)..];
            value = value[..dash];
        }

        if (string.IsNullOrEmpty(value))
            return false;

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 3)
            return false;

        Span<int> numbers = stackalloc int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
                return false;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    /// <summary>
    /// 是否比另一个版本更新：先比数值三元组；数值相同时正式版优先于预发布；
    /// 两个预发布按字符串序比较（非严格语义化版本预发布排序，正式 tag 场景够用）。
    ///</summary>
    /// <param name="other">待比较的旧版本</param>
    /// <returns>当前版本是否比 other 更新</returns>
    public bool IsNewerThan(in AppVersion other)
    {
        if (Major != other.Major)
            return Major > other.Major;
        if (Minor != other.Minor)
            return Minor > other.Minor;
        if (Patch != other.Patch)
            return Patch > other.Patch;

        // 数值相同：正式版 > 预发布
        if (Prerelease is null && other.Prerelease is not null)
            return true;
        if (Prerelease is not null && other.Prerelease is null)
            return false;

        // 两个预发布按字符串序比较
        return Prerelease is not null &&
               string.CompareOrdinal(Prerelease, other.Prerelease) > 0;
    }

    /// <summary>
    /// 当前运行程序集的版本号：读取入口程序集的 InformationalVersion，
    /// 剥离 + 元数据后解析，失败时回落 0.0.0。
    ///</summary>
    public static AppVersion Current
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly();
            var informational = assembly?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return TryParse(informational, out var version) ? version : new AppVersion(0, 0, 0);
        }
    }

    /// <summary>
    /// 输出不含 v 前缀与 + 元数据的裸版本号（如 1.2.3 或 1.2.3-beta.1）。
    ///</summary>
    /// <returns>裸版本号文本</returns>
    public override string ToString() =>
        Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
