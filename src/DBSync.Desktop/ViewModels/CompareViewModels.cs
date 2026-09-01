using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using System.Collections.ObjectModel;

namespace DBSync.Desktop.ViewModels;

public sealed partial class CompareSchemaNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private bool isSelected = true;

    [ObservableProperty]
    private bool hasWarning;

    [ObservableProperty]
    private IBrush statusBrush = Brushes.Gray;

    public ObservableCollection<CompareSchemaNodeViewModel> Children { get; } = new();
}

public sealed partial class CompareDataSummaryViewModel : ObservableObject
{
    [ObservableProperty]
    private string tableName = string.Empty;

    [ObservableProperty]
    private string summaryText = string.Empty;

    [ObservableProperty]
    private bool isSkipped;

    [ObservableProperty]
    private int rowsToInsert;

    [ObservableProperty]
    private int deletedRows;

    [ObservableProperty]
    private int changedRows;

    [ObservableProperty]
    private IBrush summaryBrush = Brushes.Gray;
}
