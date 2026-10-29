using Avalonia.Controls;

namespace PowerPlan.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // The window is recreated each time it opens from the tray; stop the old view model's refresh timer.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}
