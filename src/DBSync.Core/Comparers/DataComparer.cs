using DBSync.Core.Models;

namespace DBSync.Core.Comparers;

/// <summary>
/// 纯函数数据比较器，对比两组行指纹产生数据差异。
///</summary>
public static class DataComparer
{
    /// <summary>
    /// 比较基线和源库的行指纹。
    /// </summary>
    /// <param name="baseline">基线行指纹集合</param>
    /// <param name="source">源库行指纹集合</param>
    /// <param name="noPrimaryKey">表是否无主键；无主键时跳过数据比对</param>
    /// <returns>数据差异汇总</returns>
    public static DataDiff Compare(
        IEnumerable<RowHash> baseline,
        IEnumerable<RowHash> source,
        bool noPrimaryKey = false)
    {
        if (noPrimaryKey)
            return DataDiff.NoPrimaryKey;

        var baselineMap = baseline.ToDictionary(r => r.PrimaryKeyString, StringComparer.OrdinalIgnoreCase);
        var sourceMap = source.ToDictionary(r => r.PrimaryKeyString, StringComparer.OrdinalIgnoreCase);

        var rowsToInsert = sourceMap.Keys.Except(baselineMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(key => sourceMap[key])
            .ToList();
        var deletedRows = baselineMap.Keys.Except(sourceMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(key => baselineMap[key])
            .ToList();
        var changedRows = baselineMap.Keys.Intersect(sourceMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key => !string.Equals(baselineMap[key].Hash, sourceMap[key].Hash, StringComparison.OrdinalIgnoreCase))
            .Select(key => sourceMap[key])
            .ToList();

        return new DataDiff
        {
            RowsToInsert = rowsToInsert,
            DeletedRows = deletedRows,
            ChangedRows = changedRows
        };
    }
}
