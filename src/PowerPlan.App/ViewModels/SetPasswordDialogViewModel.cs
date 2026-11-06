using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerPlan.Core;

namespace PowerPlan.App.ViewModels;

/// <summary>Backs the "choose a new password" prompt, used both to turn parental mode on and to change its password.</summary>
public sealed partial class SetPasswordDialogViewModel(string title, string message) : ObservableObject
{
    public string Title => title;
    public string Message => message;

    [ObservableProperty] string password = "";
    [ObservableProperty] string confirmPassword = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] string? error;

    public bool HasError => Error is not null;

    public event Action<string>? Confirmed;
    public event Action? Cancelled;

    [RelayCommand]
    void Confirm()
    {
        if (Password.Length < ParentalMode.MinPasswordLength)
        {
            Error = $"Password must be at least {ParentalMode.MinPasswordLength} characters.";
            return;
        }
        if (Password != ConfirmPassword)
        {
            Error = "Passwords don't match.";
            return;
        }
        Confirmed?.Invoke(Password);
    }

    [RelayCommand]
    void Cancel() => Cancelled?.Invoke();
}
