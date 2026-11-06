using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PowerPlan.App.ViewModels;

/// <summary>Backs a single-password prompt; <paramref name="verify"/> decides whether Confirm succeeds.</summary>
public sealed partial class UnlockDialogViewModel(string title, string message, Func<string, bool> verify) : ObservableObject
{
    public string Title => title;
    public string Message => message;

    [ObservableProperty] string password = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] string? error;

    public bool HasError => Error is not null;

    public event Action<string>? Confirmed;
    public event Action? Cancelled;

    [RelayCommand]
    void Confirm()
    {
        if (verify(Password))
        {
            Confirmed?.Invoke(Password);
            return;
        }
        Error = "That password is incorrect.";
        Password = "";
    }

    [RelayCommand]
    void Cancel() => Cancelled?.Invoke();
}
