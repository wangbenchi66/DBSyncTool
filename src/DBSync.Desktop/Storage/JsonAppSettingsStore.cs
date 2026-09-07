using System.Text.Json;
using DBSync.Desktop.Models;
using DBSync.Desktop.Services;

namespace DBSync.Desktop.Storage;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    /// <summary>
    /// JSON 序列化上下文（Source Generator）
    ///</summary>
    private static readonly StorageJsonContext JsonContext = StorageJsonContext.Default;
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DBSyncTool");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
            return new AppSettings();

        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize(json, JsonContext.AppSettings) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonContext.AppSettings));
    }
}
