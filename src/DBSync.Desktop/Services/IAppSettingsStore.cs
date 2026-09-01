using DBSync.Desktop.Models;

namespace DBSync.Desktop.Services;

public interface IAppSettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
