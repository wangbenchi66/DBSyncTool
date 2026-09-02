using DBSync.Core.Models;

namespace DBSync.Core.Data;

public interface IDataFingerprinter
{
    IAsyncEnumerable<RowHash> ReadRowHashesAsync(
        DatabaseConnection connection,
        TableModel table,
        string? whereClause = null,
        CancellationToken cancellationToken = default);
}
