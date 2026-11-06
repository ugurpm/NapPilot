using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_accepts_the_right_password()
    {
        var hash = PasswordHasher.Hash("correct horse");

        Assert.True(PasswordHasher.Verify("correct horse", hash));
    }

    [Fact]
    public void Verify_rejects_the_wrong_password()
    {
        var hash = PasswordHasher.Hash("correct horse");

        Assert.False(PasswordHasher.Verify("wrong password", hash));
    }

    [Fact]
    public void Same_password_hashes_differently_each_time()
    {
        // A fixed salt would let two users with the same password share a hash; that must not happen.
        Assert.NotEqual(PasswordHasher.Hash("hunter2"), PasswordHasher.Hash("hunter2"));
    }
}

public class ParentalModeTests
{
    [Fact]
    public void Enable_turns_it_on_and_hashes_the_password()
    {
        var (errors, settings) = ParentalMode.Enable(new AppSettings(), "secret1");

        Assert.Empty(errors);
        Assert.True(settings.ParentalModeEnabled);
        Assert.NotNull(settings.ParentalModePasswordHash);
        Assert.DoesNotContain("secret1", settings.ParentalModePasswordHash);
        Assert.True(ParentalMode.Verify(settings, "secret1"));
    }

    [Fact]
    public void Enable_refuses_a_too_short_password()
    {
        var (errors, settings) = ParentalMode.Enable(new AppSettings(), "abc");

        Assert.NotEmpty(errors);
        Assert.False(settings.ParentalModeEnabled);
    }

    [Fact]
    public void Enable_refuses_when_already_on()
    {
        var on = ParentalMode.Enable(new AppSettings(), "secret1").Settings;

        var (errors, settings) = ParentalMode.Enable(on, "secret2");

        Assert.NotEmpty(errors);
        Assert.True(ParentalMode.Verify(settings, "secret1"));
    }

    [Fact]
    public void Disable_needs_the_current_password()
    {
        var on = ParentalMode.Enable(new AppSettings(), "secret1").Settings;

        var (errors, settings) = ParentalMode.Disable(on, "wrong");

        Assert.NotEmpty(errors);
        Assert.True(settings.ParentalModeEnabled);
    }

    [Fact]
    public void Disable_turns_it_off_and_clears_the_hash()
    {
        var on = ParentalMode.Enable(new AppSettings(), "secret1").Settings;

        var (errors, settings) = ParentalMode.Disable(on, "secret1");

        Assert.Empty(errors);
        Assert.False(settings.ParentalModeEnabled);
        Assert.Null(settings.ParentalModePasswordHash);
    }

    [Fact]
    public void Disable_refuses_when_already_off()
    {
        var (errors, _) = ParentalMode.Disable(new AppSettings(), "anything");

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ChangePassword_needs_the_current_password()
    {
        var on = ParentalMode.Enable(new AppSettings(), "secret1").Settings;

        var (errors, settings) = ParentalMode.ChangePassword(on, "wrong", "secret2");

        Assert.NotEmpty(errors);
        Assert.True(ParentalMode.Verify(settings, "secret1"));
    }

    [Fact]
    public void ChangePassword_refuses_a_too_short_replacement()
    {
        var on = ParentalMode.Enable(new AppSettings(), "secret1").Settings;

        var (errors, settings) = ParentalMode.ChangePassword(on, "secret1", "ab");

        Assert.NotEmpty(errors);
        Assert.True(ParentalMode.Verify(settings, "secret1"));
    }

    [Fact]
    public void ChangePassword_replaces_the_hash()
    {
        var on = ParentalMode.Enable(new AppSettings(), "secret1").Settings;

        var (errors, settings) = ParentalMode.ChangePassword(on, "secret1", "secret2");

        Assert.Empty(errors);
        Assert.False(ParentalMode.Verify(settings, "secret1"));
        Assert.True(ParentalMode.Verify(settings, "secret2"));
    }

    [Fact]
    public void ChangePassword_refuses_when_not_enabled()
    {
        var (errors, _) = ParentalMode.ChangePassword(new AppSettings(), "x", "secret2");

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Verify_is_false_when_no_password_was_ever_set()
    {
        Assert.False(ParentalMode.Verify(new AppSettings(), ""));
    }
}
