using DBSync.Core.Models;

namespace DBSync.Core.Comparers;

/// <summary>
/// 按外键依赖对表进行拓扑排序。
///</summary>
public static class FkTopologicalSorter
{
    /// <summary>
    /// 对表集合按创建顺序排序，并返回无法排序的循环依赖组。
    /// </summary>
    /// <param name="tables">待排序的表集合</param>
    /// <returns>排序后的表集合和循环依赖组</returns>
    public static (IReadOnlyList<TableModel> Sorted, IReadOnlyList<IReadOnlyList<string>> Cycles) Sort(
        IEnumerable<TableModel> tables)
    {
        var list = tables.ToList();
        var tableMap = list.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
        var incoming = list.ToDictionary(t => t.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var outgoing = list.ToDictionary(t => t.Name, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var table in list)
        {
            foreach (var fk in table.ForeignKeys)
            {
                if (!tableMap.ContainsKey(fk.ReferencedTable))
                    continue;

                incoming[table.Name]++;
                outgoing[fk.ReferencedTable].Add(table.Name);
            }
        }

        var queue = new Queue<string>(list.Where(t => incoming[t.Name] == 0).Select(t => t.Name));
        var sorted = new List<TableModel>(list.Count);

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            sorted.Add(tableMap[name]);

            foreach (var child in outgoing[name])
            {
                incoming[child]--;
                if (incoming[child] == 0)
                    queue.Enqueue(child);
            }
        }

        var cycles = FindCycles(list, tableMap, incoming);
        return (sorted, cycles);
    }

    /// <summary>
    /// 从仍有入度的表中查找循环依赖组。
    /// </summary>
    /// <param name="tables">原始表集合</param>
    /// <param name="tableMap">按表名索引的表字典</param>
    /// <param name="incoming">拓扑排序后剩余的入度字典</param>
    /// <returns>循环依赖组列表</returns>
    private static IReadOnlyList<IReadOnlyList<string>> FindCycles(
        IReadOnlyList<TableModel> tables,
        IReadOnlyDictionary<string, TableModel> tableMap,
        IReadOnlyDictionary<string, int> incoming)
    {
        var cycleNames = tables.Where(t => incoming[t.Name] > 0).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cycleNames.Count == 0)
            return [];

        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lowLinks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<IReadOnlyList<string>>();

        void StrongConnect(string name)
        {
            indexes[name] = index;
            lowLinks[name] = index;
            index++;
            stack.Push(name);
            onStack.Add(name);

            foreach (var next in tableMap[name].ForeignKeys.Select(fk => fk.ReferencedTable).Where(cycleNames.Contains))
            {
                if (!indexes.ContainsKey(next))
                {
                    StrongConnect(next);
                    lowLinks[name] = Math.Min(lowLinks[name], lowLinks[next]);
                }
                else if (onStack.Contains(next))
                {
                    lowLinks[name] = Math.Min(lowLinks[name], indexes[next]);
                }
            }

            if (lowLinks[name] != indexes[name])
                return;

            var component = new List<string>();
            string popped;
            do
            {
                popped = stack.Pop();
                onStack.Remove(popped);
                component.Add(popped);
            }
            while (!string.Equals(popped, name, StringComparison.OrdinalIgnoreCase));

            if (component.Count > 1 || HasSelfReference(component[0], tableMap))
                result.Add(component);
        }

        foreach (var name in tables.Select(t => t.Name))
        {
            if (cycleNames.Contains(name) && !indexes.ContainsKey(name))
                StrongConnect(name);
        }

        return result;
    }

    /// <summary>
    /// 判断指定表是否存在自引用外键。
    /// </summary>
    /// <param name="tableName">表名</param>
    /// <param name="tableMap">按表名索引的表字典</param>
    /// <returns>存在自引用外键时返回 true</returns>
    private static bool HasSelfReference(string tableName, IReadOnlyDictionary<string, TableModel> tableMap) =>
        tableMap[tableName].ForeignKeys.Any(fk => string.Equals(fk.ReferencedTable, tableName, StringComparison.OrdinalIgnoreCase));
}
