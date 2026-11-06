using Avalonia.Controls;
using PowerPlan.App.ViewModels;

namespace PowerPlan.App.Views;

public partial class UnlockDialog : Window
{
    public UnlockDialog() => InitializeComponent();

    public static Task<string?> Ask(Window owner, string title, string message, Func<string, bool> verify)
    {
        var vm = new UnlockDialogViewModel(title, message, verify);
        var dialog = new UnlockDialog { DataContext = vm };
        var result = new TaskCompletionSource<string?>();

        vm.Confirmed += password => { result.TrySetResult(password); dialog.Close(); };
        vm.Cancelled += () => { result.TrySetResult(null); dialog.Close(); };
        dialog.Closed += (_, _) => result.TrySetResult(null);

        dialog.Opened += (_, _) => dialog.FindControl<TextBox>("PasswordBox")?.Focus();
        _ = dialog.ShowDialog(owner);
        return result.Task;
    }
}
