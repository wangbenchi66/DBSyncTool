using DBSync.Core.Comparers;
using DBSync.Tests.Helpers;
using Xunit;

namespace DBSync.Tests.Comparers;

public class FkTopologicalSorterTests
{
    // ── 无依赖 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Sort_NoDependencies_ReturnsSameOrder()
    {
        var tables = new[]
        {
            TableModelFactory.Simple("A"),
            TableModelFactory.Simple("B"),
            TableModelFactory.Simple("C")
        };

        var (sorted, cycles) = FkTopologicalSorter.Sort(tables);

        Assert.Equal(3, sorted.Count);
        Assert.Empty(cycles);
    }

    // ── 线性依赖链 ──────────────────────────────────────────────────────────

    [Fact]
    public void Sort_LinearChain_ParentBeforeChild()
    {
        // Orders.CustomerId → Customers
        var customers = TableModelFactory.Simple("Customers");
        var orders = TableModelFactory.WithColumns(
            "Orders",
            [TableModelFactory.IdColumn(), TableModelFactory.Col("CustomerId", Core.Models.DbColumnType.Integer)],
            foreignKeys: [TableModelFactory.Fk("CustomerId", "Customers")]);

        var (sorted, cycles) = FkTopologicalSorter.Sort([orders, customers]);

        Assert.Empty(cycles);
        var names = sorted.Select(t => t.Name).ToList();
        Assert.True(names.IndexOf("Customers") < names.IndexOf("Orders"),
            "Customers（父表）应在 Orders（子表）之前");
    }

    // ── 菱形依赖 ────────────────────────────────────────────────────────────

    [Fact]
    public void Sort_DiamondDependency_AllParentsBeforeChildren()
    {
        // D → B, D → C, B → A, C → A
        var a = TableModelFactory.Simple("A");
        var b = TableModelFactory.WithColumns("B",
            [TableModelFactory.IdColumn()],
            foreignKeys: [TableModelFactory.Fk("AId", "A")]);
        var c = TableModelFactory.WithColumns("C",
            [TableModelFactory.IdColumn()],
            foreignKeys: [TableModelFactory.Fk("AId", "A")]);
        var d = TableModelFactory.WithColumns("D",
            [TableModelFactory.IdColumn()],
            foreignKeys: [TableModelFactory.Fk("BId", "B"), TableModelFactory.Fk("CId", "C")]);

        var (sorted, cycles) = FkTopologicalSorter.Sort([d, b, c, a]);

        Assert.Empty(cycles);
        var idx = sorted.Select((t, i) => (t.Name, i)).ToDictionary(x => x.Name, x => x.i);
        Assert.True(idx["A"] < idx["B"]);
        Assert.True(idx["A"] < idx["C"]);
        Assert.True(idx["B"] < idx["D"]);
        Assert.True(idx["C"] < idx["D"]);
    }

    // ── 循环依赖 ────────────────────────────────────────────────────────────

    [Fact]
    public void Sort_CyclicDependency_ReportsCycleAndExcludesFromSorted()
    {
        // A → B → A（循环）
        var a = TableModelFactory.WithColumns("A",
            [TableModelFactory.IdColumn()],
            foreignKeys: [TableModelFactory.Fk("BId", "B")]);
        var b = TableModelFactory.WithColumns("B",
            [TableModelFactory.IdColumn()],
            foreignKeys: [TableModelFactory.Fk("AId", "A")]);

        var (sorted, cycles) = FkTopologicalSorter.Sort([a, b]);

        Assert.NotEmpty(cycles);
        // 循环组中的表不应出现在正常排序结果里
        var sortedNames = sorted.Select(t => t.Name).ToHashSet();
        foreach (var cycle in cycles)
        foreach (var tableName in cycle)
            Assert.DoesNotContain(tableName, sortedNames);
    }

    // ── DROP TABLE 逆序 ─────────────────────────────────────────────────────

    [Fact]
    public void Sort_DropOrder_IsReverseOfCreateOrder()
    {
        var customers = TableModelFactory.Simple("Customers");
        var orders = TableModelFactory.WithColumns(
            "Orders",
            [TableModelFactory.IdColumn()],
            foreignKeys: [TableModelFactory.Fk("CustomerId", "Customers")]);

        var (sorted, _) = FkTopologicalSorter.Sort([orders, customers]);
        var dropOrder = sorted.Reverse().ToList();

        var dropNames = dropOrder.Select(t => t.Name).ToList();
        Assert.True(dropNames.IndexOf("Orders") < dropNames.IndexOf("Customers"),
            "DROP 时子表（Orders）应先删，父表（Customers）后删");
    }
}
