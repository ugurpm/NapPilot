namespace PowerPlan.Core;

/// <summary>
/// Installed copies start at sign-in by default: wake handling (the sleep catcher) and update checks only work while the
/// tray app runs. The default is applied once per install; after that the user's choice, made in NapPilot or in the OS's
/// startup settings, is never overridden.
/// </summary>
public static class StartAtSignIn
{
    public static AppSettings ApplyDefault(AppSettings settings, ILoginItem loginItem, ILog log)
    {
        if (settings.StartAtSignInDefaultApplied) return settings;

        if (!loginItem.IsEnabled)
        {
            loginItem.SetEnabled(true);
            log.Write("Start at sign-in turned on (first start of this install)");
        }
        return settings with { StartAtSignInDefaultApplied = true };
    }
}
