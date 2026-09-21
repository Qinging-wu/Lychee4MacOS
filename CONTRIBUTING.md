# Contributing to Lychee for macOS

Thanks for your interest in Lychee for macOS! Bug reports, feature ideas, documentation improvements, and pull requests are welcome.

## Development setup

- macOS 11+ (Apple Silicon or Intel) for running the app
- .NET SDK 8.0 or later

> Compilation can also be verified on Windows or Linux (`dotnet build`), but the macOS-specific paths (mach APIs, CGEvent, notifications) can only be exercised on a real Mac.

Build the project with:

```bash
dotnet build -c Release
```

Run from source:

```bash
dotnet run
```

## Issues

Before opening an issue, check whether it has already been reported. Include your macOS version, hardware (Apple Silicon / Intel), display arrangement (a screenshot of System Settings → Displays helps for positioning bugs), Lychee version or commit, steps to reproduce, expected behavior, actual behavior, and relevant logs from `~/Library/Application Support/Lychee/logs/lychee.log`. Please do not include private or sensitive information.

## Pull requests

1. Create a focused branch from `main`.
2. Keep changes small and explain the motivation.
3. Preserve the existing coding style and user-facing behavior unless the change is intentional.
4. Build the project successfully before submitting the PR.
5. Describe what changed, how it was tested, and any limitations.

For changes in `Platform/`, keep the macOS and Windows branches consistent with the documented API contracts (flavor constants, struct sizes, coordinate units) and add a source comment referencing the relevant header or documentation.
