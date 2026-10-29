# Security policy

NapPilot can shut down, restart and wake computers, and it updates itself from GitHub Releases. Security problems in it
matter, and reports are welcome.

## Supported versions

Only the latest release gets fixes. Installed copies update themselves, so a fix reaches users with the next release.

| Version | Supported |
|---|---|
| Latest release | ✅ |
| Older releases | ❌ (update to the latest) |

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report it privately through GitHub: go to the [Security tab](https://github.com/ugurpm/NapPilot/security) and click
**Report a vulnerability**. Only the maintainer can see the report. If that does not work for you, email
**ugurpm@gmail.com** with "NapPilot security" in the subject.

Please include:

- what an attacker could do, and what they need first (local user, network position, a crafted file, ...);
- steps to reproduce, the NapPilot version and OS;
- a proof of concept if you have one.

You can expect an acknowledgement within **3 working days** and an assessment within **10 working days**. Fixes are
released as soon as they are ready, and you will be credited in the release notes unless you prefer not to be.

## Scope

In scope: the NapPilot app, its scheduled tasks / LaunchAgents, the update mechanism and the release pipeline in this
repository.

Out of scope: issues that need an attacker who already has administrator rights on the machine, and problems in
Windows, macOS, .NET, Avalonia or Velopack themselves (please report those upstream).
