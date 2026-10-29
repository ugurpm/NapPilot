# Contributing to NapPilot

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Reporting bugs and ideas

- Bugs: use the [bug report form](https://github.com/ugurpm/NapPilot/issues/new?template=bug_report.yml). The version,
  OS and the end of `nappilot.log` make most bugs easy to find.
- Ideas: use the [feature request form](https://github.com/ugurpm/NapPilot/issues/new?template=feature_request.yml).
- Security problems: **don't** open an issue; see [SECURITY.md](SECURITY.md).

For anything bigger than a small fix, please open an issue first so we can agree on the approach before you spend time
on it.

## Development setup

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (the exact feature band is pinned in
`global.json`). Then:

```bash
dotnet test
```

```bash
dotnet run --project src/PowerPlan.App
```

Debug builds start in **dry-run**: power actions are only written to the log, so the app never turns your computer off
while you work on it. Keep it that way while developing. More detail, including manual testing and how wake works on
Modern Standby laptops, is in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) and [docs/WAKE-FINDINGS.md](docs/WAKE-FINDINGS.md).

The internal project names are still `PowerPlan.*`; the product is NapPilot.

## Pull requests

1. Fork the repo and create a branch from `main` (`fix/wake-on-battery`, `feature/custom-days`, ...).
2. Keep the change focused: one fix or feature per pull request.
3. Add or update tests. Core logic goes in `src/PowerPlan.Core` with tests in `tests/PowerPlan.Core.Tests`; OS-specific
   code goes behind an interface in `Abstractions.cs` with a fake in the tests. Tests must never perform real power
   actions or touch the real Task Scheduler / launchd.
4. Run `dotnet test` and make sure the build has no warnings.
5. Open the pull request and fill in the template.

`main` is protected: every change goes through a pull request, CI must pass, and the maintainer reviews and approves
before it is merged.

## Code style

- Follow `.editorconfig`; most editors pick it up automatically.
- File-scoped namespaces, primary constructors, `var` when the type is obvious.
- Comments explain *why*, not *what*. Short is better.
- Keep platform code out of `PowerPlan.Core`.

## Commit messages

Use the imperative mood and say what changes and why: `Skip late shutdowns after the Mac wakes`, not `fixed bug`.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
