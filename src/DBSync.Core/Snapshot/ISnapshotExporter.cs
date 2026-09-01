using DBSync.Core.Models;

namespace DBSync.Core.Snapshot;

/// <summary>
/// 快照导出器接口，负责将数据库元数据和数据指纹写入 .dbsync 文件
///</summary>
public interface ISnapshotExporter
{
    /// <summary>
    /// 将指定数据库的元数据和数据指纹导出为 .dbsync 加密压缩文件
    /// </summary>
    /// <param name="connection">源数据库连接</param>
    /// <param name="options">导出选项（含密码、表选择、行数阈值等）</param>
    /// <param name="outputStream">写入 .dbsync 内容的目标流</param>
    /// <param name="progress">进度报告回调（当前表索引, 总表数, 当前表名）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ExportAsync(
        DatabaseConnection connection,
        ExportOptions options,
        Stream outputStream,
        IProgress<(int current, int total, string tableName)>? progress = null,
        CancellationToken cancellationToken = default);
}
