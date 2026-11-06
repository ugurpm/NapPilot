using System.Security.Cryptography;

namespace PowerPlan.Core;

/// <summary>Salted PBKDF2 hashing for the parental mode password; nothing is ever stored in plain text.</summary>
public static class PasswordHasher
{
    const int Iterations = 210_000;
    const int SaltSize = 16;
    const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>
/// Turning parental mode on or off, and changing its password, all live here so the rule "you need the current
/// password to change it" is enforced in one place instead of in every caller.
/// </summary>
public static class ParentalMode
{
    public const int MinPasswordLength = 4;

    public static (IReadOnlyList<string> Errors, AppSettings Settings) Enable(AppSettings settings, string password)
    {
        if (settings.ParentalModeEnabled) return (["Parental mode is already on."], settings);
        if (password.Length < MinPasswordLength) return ([TooShort], settings);

        return ([], settings with { ParentalModeEnabled = true, ParentalModePasswordHash = PasswordHasher.Hash(password) });
    }

    public static (IReadOnlyList<string> Errors, AppSettings Settings) Disable(AppSettings settings, string currentPassword)
    {
        if (!settings.ParentalModeEnabled) return (["Parental mode is already off."], settings);
        if (!Verify(settings, currentPassword)) return ([WrongPassword], settings);

        return ([], settings with { ParentalModeEnabled = false, ParentalModePasswordHash = null });
    }

    public static (IReadOnlyList<string> Errors, AppSettings Settings) ChangePassword(AppSettings settings, string currentPassword, string newPassword)
    {
        if (!settings.ParentalModeEnabled) return (["Turn on parental mode first."], settings);
        if (!Verify(settings, currentPassword)) return ([WrongPassword], settings);
        if (newPassword.Length < MinPasswordLength) return ([TooShort], settings);

        return ([], settings with { ParentalModePasswordHash = PasswordHasher.Hash(newPassword) });
    }

    public static bool Verify(AppSettings settings, string password) =>
        settings.ParentalModePasswordHash is { } hash && PasswordHasher.Verify(password, hash);

    const string WrongPassword = "That password is incorrect.";
    static readonly string TooShort = $"Password must be at least {MinPasswordLength} characters.";
}
