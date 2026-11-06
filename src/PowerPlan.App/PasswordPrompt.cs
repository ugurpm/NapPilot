using Avalonia.Controls;
using PowerPlan.App.Views;

namespace PowerPlan.App;

/// <summary>Modal prompts for the parental mode password, owned by the main window.</summary>
public interface IPasswordPrompt
{
    /// <summary>Asks for a password, letting the user retry on a wrong one. Null if they cancel.</summary>
    Task<string?> ConfirmAsync(string title, string message, Func<string, bool> verify);

    /// <summary>Asks for a new password twice. Null if they cancel.</summary>
    Task<string?> SetAsync(string title, string message);
}

public sealed class AvaloniaPasswordPrompt(Window owner) : IPasswordPrompt
{
    public Task<string?> ConfirmAsync(string title, string message, Func<string, bool> verify) => UnlockDialog.Ask(owner, title, message, verify);

    public Task<string?> SetAsync(string title, string message) => SetPasswordDialog.Ask(owner, title, message);
}
