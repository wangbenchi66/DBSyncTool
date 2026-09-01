using Avalonia.Controls;
using Avalonia.Interactivity;
using DBSync.Desktop.ViewModels;

namespace DBSync.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.AttachOwnerWindow(this);
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
            return;

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.HasPendingOperation)
            return;

        e.Cancel = true;
        var confirmed = await ConfirmCloseWindow.ShowAsync(this);
        if (!confirmed)
            return;

        _allowClose = true;
        Close();
    }
}
