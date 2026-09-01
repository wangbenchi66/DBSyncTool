using System.Text.Json;
using DBSync.Core.Models;
using DBSync.Desktop.Services;
using DBSync.Desktop.ViewModels;
using WBC66.Autofac.Core;

namespace DBSync.Desktop.Storage;

public sealed class LocalConnectionStore(IConnectionEncryption encryption) : IConnectionStore, IDependency
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DBSyncTool");
    private static readonly string FilePath = Path.Combine(Folder, "connections.dat");

    public IReadOnlyList<ConnectionItemViewModel> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        var protectedBytes = File.ReadAllBytes(FilePath);
        var json = encryption.Unprotect(protectedBytes);
        var items = JsonSerializer.Deserialize<List<ConnectionDto>>(json, JsonOptions) ?? [];

        return items.Select(i => new ConnectionItemViewModel(i.Name, i.DbType, i.ServerAddress)).ToList();
    }

    public void Save(IReadOnlyList<ConnectionItemViewModel> connections)
    {
        Directory.CreateDirectory(Folder);
        var items = connections.Select(c => new ConnectionDto(c.Name, c.DbType, c.ServerAddress)).ToList();
        var json = JsonSerializer.SerializeToUtf8Bytes(items, JsonOptions);
        var protectedBytes = encryption.Protect(json);
        File.WriteAllBytes(FilePath, protectedBytes);
    }

    private sealed record ConnectionDto(string Name, DatabaseType DbType, string ServerAddress);
}
