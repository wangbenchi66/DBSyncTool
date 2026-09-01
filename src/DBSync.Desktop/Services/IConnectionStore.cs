using DBSync.Desktop.ViewModels;

namespace DBSync.Desktop.Services;

public interface IConnectionStore
{
    IReadOnlyList<ConnectionItemViewModel> Load();

    void Save(IReadOnlyList<ConnectionItemViewModel> connections);
}
