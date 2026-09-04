using DBSync.Core.Models;
using DBSync.Desktop.ViewModels;

namespace DBSync.Tests.ViewModels;

public class ConnectionItemViewModelTests
{
    [Fact]
    public void DisplayInfo_ShouldIncludePort()
    {
        var vm = new ConnectionItemViewModel
        {
            Name = "测试",
            DbType = DatabaseType.MySql,
            Server = "192.168.21.232",
            Port = 3306,
            Database = "journal"
        };

        Assert.Equal("192.168.21.232:3306 / journal", vm.DisplayInfo);
    }

    [Fact]
    public void ToDatabaseConnection_ShouldPreserveEnvironment()
    {
        var vm = new ConnectionItemViewModel
        {
            Name = "测试",
            DbType = DatabaseType.SqlServer,
            Server = "localhost",
            Port = 1433,
            Database = "db",
            Environment = ConnectionEnvironment.Production
        };

        var connection = vm.ToDatabaseConnection();

        Assert.Equal(ConnectionEnvironment.Production, connection.Environment);
    }
}
