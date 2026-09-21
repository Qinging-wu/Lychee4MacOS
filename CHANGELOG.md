# Changelog

## v1.0.0

| Change | Detail |
|---|---|
| 🍎 Initial macOS release | Floating ball + info panel ported from the Windows [Lychee](https://github.com/Qinging-wu/Lychee) to .NET 8 + Avalonia UI 11 |
| 📊 Six modules | Date & time, network speed, public IP / location, CPU (mach `host_statistics`), memory (`sysctl` + `host_statistics`), network latency |
| 🖱️ Ball interactions | Hover expand/collapse, drag across monitors, double-click pin, edge snapping, panel flip by screen position |
| ⚙️ Settings window | Per-module toggles, always-show panel, snap to edge, alert options |
| 🔔 Alerts | In-app toast + system notification (AppleScript) for IP change, query failure, and recovery |
| 🍔 Menu bar icon | Show/Hide, Settings, Quit via Avalonia TrayIcon (NSStatusItem) |
