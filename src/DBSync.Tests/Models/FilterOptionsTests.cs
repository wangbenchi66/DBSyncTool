using DBSync.Core.Models;

namespace DBSync.Tests.Models;

/// <summary>
/// FilterOptions 过滤规则单元测试
///</summary>
public class FilterOptionsTests
{
    [Fact]
    public void IsTableIncluded_NoRules_ReturnsTrue()
    {
        var filter = new FilterOptions();

        Assert.True(filter.IsTableIncluded("dbo.Users"));
        Assert.True(filter.IsTableIncluded("journal.event_store"));
    }

    [Fact]
    public void IsTableIncluded_IncludePattern_OnlyMatchingTablesPass()
    {
        var filter = new FilterOptions
        {
            IncludePatterns = ["journal\\..*"]
        };

        Assert.True(filter.IsTableIncluded("journal.Users"));
        Assert.True(filter.IsTableIncluded("journal.Orders"));
        Assert.False(filter.IsTableIncluded("dbo.Users"));
    }

    [Fact]
    public void IsTableIncluded_ExcludePattern_MatchingTablesExcluded()
    {
        var filter = new FilterOptions
        {
            ExcludePatterns = [".*_migration.*"]
        };

        Assert.False(filter.IsTableIncluded("journal._migration_history"));
        Assert.True(filter.IsTableIncluded("journal.Users"));
    }

    [Fact]
    public void IsTableIncluded_ExcludeOverridesInclude()
    {
        var filter = new FilterOptions
        {
            IncludePatterns = ["journal\\..*"],
            ExcludePatterns = [".*_log$"]
        };

        Assert.True(filter.IsTableIncluded("journal.Users"));
        Assert.False(filter.IsTableIncluded("journal.audit_log"));
    }

    [Fact]
    public void IsTableIncluded_CaseInsensitive()
    {
        var filter = new FilterOptions
        {
            IncludePatterns = ["users"]
        };

        Assert.True(filter.IsTableIncluded("Users"));
        Assert.True(filter.IsTableIncluded("USERS"));
        Assert.True(filter.IsTableIncluded("users"));
    }

    [Fact]
    public void IsTableIncluded_MultiplePatterns_AnyMatchSuffices()
    {
        var filter = new FilterOptions
        {
            IncludePatterns = ["^dbo\\..*", "^journal\\..*"]
        };

        Assert.True(filter.IsTableIncluded("dbo.Users"));
        Assert.True(filter.IsTableIncluded("journal.Orders"));
        Assert.False(filter.IsTableIncluded("audit.Logs"));
    }
}
