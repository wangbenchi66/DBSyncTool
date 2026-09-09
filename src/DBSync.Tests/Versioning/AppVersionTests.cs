using DBSync.Core.Versioning;

namespace DBSync.Tests.Versioning;

/// <summary>
/// AppVersion 解析、比较与格式化的单元测试。
/// 注意：不测 AppVersion.Current —— 它读取入口程序集，测试宿主下会读到测试程序集版本。
///</summary>
public class AppVersionTests
{
    [Fact]
    public void TryParse_WithLeadingV_ReturnsVersion()
    {
        Assert.True(AppVersion.TryParse("v1.2.3", out var version));
        Assert.Equal(new AppVersion(1, 2, 3), version);
    }

    [Fact]
    public void TryParse_WithMetadataSuffix_IgnoresMetadata()
    {
        Assert.True(AppVersion.TryParse("1.0.0+gabcdef123", out var version));
        Assert.Equal(new AppVersion(1, 0, 0), version);
    }

    [Fact]
    public void TryParse_MissingSegments_FillsZero()
    {
        Assert.True(AppVersion.TryParse("v3.0", out var version));
        Assert.Equal(new AppVersion(3, 0, 0), version);
    }

    [Fact]
    public void TryParse_WithPrerelease_ParsesPrerelease()
    {
        Assert.True(AppVersion.TryParse("v1.0.0-beta.1", out var version));
        Assert.Equal("beta.1", version.Prerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    public void TryParse_InvalidInput_ReturnsFalse(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Fact]
    public void IsNewerThan_HigherNumeric_ReturnsTrue()
    {
        Assert.True(AppVersion.TryParse("v1.2.0", out var newer));
        Assert.True(AppVersion.TryParse("v1.1.9", out var older));
        Assert.True(newer.IsNewerThan(older));
        Assert.False(older.IsNewerThan(newer));
    }

    [Fact]
    public void IsNewerThan_SameMajorHigherMinor_ReturnsTrue()
    {
        Assert.True(AppVersion.TryParse("v2.0", out var newer));
        Assert.True(AppVersion.TryParse("v1.9.9", out var older));
        Assert.True(newer.IsNewerThan(older));
    }

    [Fact]
    public void IsNewerThan_ReleaseBeatsPrerelease()
    {
        Assert.True(AppVersion.TryParse("1.0.0", out var release));
        Assert.True(AppVersion.TryParse("1.0.0-beta.1", out var prerelease));
        Assert.True(release.IsNewerThan(prerelease));
        Assert.False(prerelease.IsNewerThan(release));
    }

    [Fact]
    public void IsNewerThan_PrereleaseComparedByText()
    {
        Assert.True(AppVersion.TryParse("1.0.0-beta.2", out var later));
        Assert.True(AppVersion.TryParse("1.0.0-beta.1", out var earlier));
        Assert.True(later.IsNewerThan(earlier));
        Assert.False(earlier.IsNewerThan(later));
    }

    [Fact]
    public void IsNewerThan_EqualVersion_ReturnsFalse()
    {
        Assert.True(AppVersion.TryParse("v1.0.0", out var a));
        Assert.True(AppVersion.TryParse("1.0.0", out var b));
        Assert.False(a.IsNewerThan(b));
    }

    [Fact]
    public void ToString_ReturnsBareVersionWithoutPrefixOrMetadata()
    {
        Assert.True(AppVersion.TryParse("v1.2.3+gabcdef", out var version));
        Assert.Equal("1.2.3", version.ToString());
    }

    [Fact]
    public void ToString_WithPrerelease_AppendsSuffix()
    {
        Assert.True(AppVersion.TryParse("v1.2.3-rc.1", out var version));
        Assert.Equal("1.2.3-rc.1", version.ToString());
    }
}
