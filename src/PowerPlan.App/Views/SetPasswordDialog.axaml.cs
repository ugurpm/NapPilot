using Avalonia.Controls;
using PowerPlan.App.ViewModels;

namespace PowerPlan.App.Views;

public partial class SetPasswordDialog : Window
{
    public SetPasswordDialog() => InitializeComponent();

    public static Task<string?> Ask(Window owner, string title, string message)
    {
        var vm = new SetPasswordDialogViewModel(title, message);
        var dialog = new SetPasswordDialog { DataContext = vm };
        var result = new TaskCompletionSource<string?>();

        vm.Confirmed += password => { result.TrySetResult(password); dialog.Close(); };
        vm.Cancelled += () => { result.TrySetResult(null); dialog.Close(); };
        dialog.Closed += (_, _) => result.TrySetResult(null);

        dialog.Opened += (_, _) => dialog.FindControl<TextBox>("PasswordBox")?.Focus();
        _ = dialog.ShowDialog(owner);
        return result.Task;
    }
}
