using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

public class StartAtSignInTests
{
    sealed class FakeLoginItem(bool enabled) : ILoginItem
    {
        public bool IsEnabled { get; private set; } = enabled;
        public int Changes { get; private set; }

        public void SetEnabled(bool value)
        {
            IsEnabled = value;
            Changes++;
        }
    }

    readonly ListLog log = new();

    [Fact]
    public void First_start_turns_it_on_and_remembers()
    {
        var item = new FakeLoginItem(enabled: false);

        var settings = StartAtSignIn.ApplyDefault(new AppSettings(), item, log);

        Assert.True(item.IsEnabled);
        Assert.True(settings.StartAtSignInDefaultApplied);
    }

    [Fact]
    public void Users_choice_is_left_alone_afterwards()
    {
        // The user switched it off (in NapPilot or in the OS) after the default was applied.
        var item = new FakeLoginItem(enabled: false);
        var applied = new AppSettings { StartAtSignInDefaultApplied = true };

        var settings = StartAtSignIn.ApplyDefault(applied, item, log);

        Assert.False(item.IsEnabled);
        Assert.Equal(0, item.Changes);
        Assert.Same(applied, settings);
    }

    [Fact]
    public void Already_on_is_not_rewritten()
    {
        var item = new FakeLoginItem(enabled: true);

        var settings = StartAtSignIn.ApplyDefault(new AppSettings(), item, log);

        Assert.Equal(0, item.Changes);
        Assert.True(settings.StartAtSignInDefaultApplied);
    }
}
