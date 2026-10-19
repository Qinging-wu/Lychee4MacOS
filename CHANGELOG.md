# Changelog

## Portable changes synced from Lychee-lite v2.0.0-beta.3

| Change | Detail |
|---|---|
| 🎨 Floating ball styles | Choose a built-in style or import a PNG, JPEG, BMP, or GIF image |
| ↔️ Resizable panel | Drag the outer edge or corner of the expanded panel; the floating ball stays in place |
| 🖌️ Custom panel appearance | Set background opacity, background and text RGB colors, and a system font, with preview and reset |

## v1.0.0

| Change | Detail |
|---|---|
| 🍎 Initial macOS release | Floating ball + info panel ported from the Windows [Lychee](https://github.com/Qinging-wu/Lychee) to .NET 8 + Avalonia UI 11 |
| 📊 Six modules | Date & time, network speed, public IP / location, CPU (mach `host_statistics`), memory (`sysctl` + `host_statistics`), network latency |
| 🖱️ Ball interactions | Hover expand/collapse, drag across monitors, double-click pin, edge snapping, panel flip by screen position |
| ⚙️ Settings window | Per-module toggles, always-show panel, snap to edge, alert options |
| 🔔 Alerts | In-app toast + system notification (AppleScript) for IP change, query failure, and recovery |
| 🍔 Menu bar icon | Show/Hide, Settings, Quit via Avalonia TrayIcon (NSStatusItem) |
