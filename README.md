# <img src="Assets/icon.ico" alt="Lychee for macOS" width="56" height="56" style="vertical-align: middle; margin-top: 8px"> Lychee for macOS

English | [简体中文](./README.zh-CN.md)

A tiny always-on-top floating ball for macOS that shows date & time, network speed, public IP, CPU, memory, and latency at a glance. Hover to expand the info panel; move away to collapse. Built on .NET 8 + Avalonia UI. No installer, no background services.

## 🔗 Relationship with Lychee (Windows)

Lychee for macOS is the macOS sibling of [Lychee](https://github.com/Qinging-wu/Lychee), the Windows version of this floating ball:

- **Same product, ported interactions** — the floating ball, info panel, settings, and module toggles mirror the Windows version's behavior.
- **Shared architecture, separate codebase** — `Core/` (module interface, manager, settings) keeps the same shape as the Windows repo, while the UI layer is rewritten on Avalonia because WPF/WinForms are Windows-only.
- **Independently versioned** — this repo has its own releases and changelog starting at v1.0.0; version numbers do not track the Windows version.
- **No Frame Performance (FPS) here** — the Windows version's frame monitoring (DWM desktop output / PresentMon per-app capture) relies on Windows-only APIs and is intentionally not ported to this repo. All other core modules are available.

## ✨ Features

- **📅 Date & time** — refreshes every second
- **🌐 Network speed** — auto-selects the active adapter, samples up/down rate every second
- **📍 Public IP / location** — queries ip-api.com every 20s, shows a toast + system notification when the IP changes (optional)
- **💻 CPU usage** — whole-system utilization via the Mach `host_statistics(HOST_CPU_LOAD_INFO)` counters, refreshes every second
- **🧠 Memory** — total via `sysctl hw.memsize`, usage via `host_statistics(HOST_VM_INFO)` page counters (free + inactive pages)
- **⏱️ Network latency** — pings 223.5.5.5 / 1.1.1.1 / 8.8.8.8 every 5s, reports the best RTT

Each module can be toggled on/off individually in Settings.

### Differences from the Windows version

| | Windows ([Lychee](https://github.com/Qinging-wu/Lychee)) | macOS (this repo) |
|---|---|---|
| UI framework | WPF + WinForms | Avalonia UI 11 |
| Frame Performance (FPS) | ✅ DWM / PresentMon | ❌ Not ported (Windows-only APIs) |
| Bouncy ball / custom ball image | ✅ / ✅ | ❌ Not ported |
| IP change alert | In-app toast + tray balloon | In-app toast + system notification (AppleScript) |
| Memory detail line | Free + page file | Free only (macOS has no page file) |

## 🖱️ Usage

- **👆 Hover** the ball to expand the panel; move away to collapse
- **✋ Drag** the ball anywhere across monitors — the panel flips direction based on screen position
- **👆👆 Double-click** the ball to pin/unpin the panel
- **📌 Pin** — keep the panel open even when the mouse leaves
- **⚙️ Settings** — per-module toggles, always-show panel, snap to edge, alert options
- **◀️ Collapse** — force-close the panel
- **❌ Quit** — exit Lychee
- **MenuBar icon** — right-click for Show/Hide, Settings, Quit

When the public IP changes (possible VPN drop or network switch), a red toast pops up in the bottom-right corner and a system notification shows the old and new IPs.

## 🚀 Quick start

Build from source (packaged releases and a `.app` bundle may come later):

```bash
git clone https://github.com/Qinging-wu/Lychee4MacOS.git
cd Lychee4MacOS
dotnet run
```

Requirements: **macOS 11+** (Apple Silicon or Intel) with **.NET SDK 8.0+**.

The floating ball appears on the right edge of the screen and a Lychee icon is added to the menu bar. The Dock icon is hidden automatically.

To close, click **✕** on the panel or use the menu-bar icon → Quit Lychee.

## 🔧 Build

```bash
dotnet build -c Release
```

Publish a self-contained raw executable (no `.app` bundle yet):

```bash
dotnet publish -c Release -r osx-arm64   # Apple Silicon
dotnet publish -c Release -r osx-x64     # Intel
```

Output: `bin/Release/net8.0/osx-arm64/publish/Lychee` — run it directly from a terminal.

## 🏗️ Architecture

```
Lychee4MacOS/
├── App.axaml(.cs)               # App entry; hides the Dock icon on macOS
├── Program.cs
├── MainWindow.axaml(.cs)        # Floating ball + info panel + interactions
├── Core/                        # Core abstractions (shared shape with the Windows version)
│   ├── IInfoModule.cs           # Module interface + InfoModuleBase
│   ├── ModuleManager.cs         # Module registration / lifecycle
│   ├── SettingsService.cs       # JSON settings persistence
│   ├── TrayIconService.cs       # Menu bar / tray icon
│   ├── AppLog.cs                # Rotating file logger
│   └── IpChangedEventArgs.cs / QueryFailedEventArgs.cs / QueryRecoveredEventArgs.cs
├── Modules/                     # Built-in modules
│   ├── DateTimeModule.cs
│   ├── NetworkSpeedModule.cs
│   ├── PublicIpModule.cs
│   ├── CpuModule.cs             # mach host_statistics(HOST_CPU_LOAD_INFO)
│   ├── MemoryModule.cs          # sysctl hw.memsize + host_statistics(HOST_VM_INFO)
│   └── LatencyModule.cs
├── Platform/                    # Per-OS data sources (macOS first, Windows fallback)
│   ├── SystemCpuTimes.cs        # mach host_statistics / GetSystemTimes
│   ├── SystemMemory.cs          # sysctl + host_statistics / GlobalMemoryStatusEx
│   ├── GlobalCursor.cs          # CGEventGetLocation / GetCursorPos
│   ├── MacAppActivation.cs      # NSApplicationActivationPolicy (no Dock icon)
│   └── MacNotifier.cs           # AppleScript display notification
└── Views/
    └── SettingsWindow.axaml(.cs)
```

### 🔌 Module interface

```csharp
public interface IInfoModule : IDisposable
{
    string Id { get; }              // unique id
    string DisplayName { get; }     // list label
    string Icon { get; }            // emoji glyph
    bool IsEnabled { get; set; }    // user toggle
    string CurrentValue { get; }    // current value (data-bound)
    string? Detail { get; }         // optional secondary line
    event EventHandler? ValueChanged;
    void Start();
    void Stop();
}
```

### 📦 Adding a module

1. Inherit `InfoModuleBase`, implement `Start()` / `Stop()`, and set `CurrentValue` when data updates (same pattern as the Windows version).
2. Register it in `MainWindow.axaml.cs`:

```csharp
_moduleManager.RegisterModule(new WeatherModule());
```

3. Rebuild. The module appears in the info panel and settings toggle list automatically — no UI changes needed.

## ⚙️ Settings

`~/Library/Application Support/Lychee/settings.json`

Same schema as the Windows version — you can copy a Windows `settings.json` over and the module toggles carry over.

```json
{
  "AlwaysShowPanel": false,
  "AlertOnIpChange": true,
  "AlertOnQueryFailure": true,
  "SnapToEdge": false,
  "ModuleEnabled": {
    "datetime": true,
    "network-speed": true,
    "public-ip": true,
    "cpu": true,
    "memory": true,
    "latency": true
  }
}
```

Logs: `~/Library/Application Support/Lychee/logs/lychee.log`

## ⚠️ Known limitations

- Frame Performance (FPS) is not available — DWM and PresentMon are Windows-only
- RAM is an estimate: used = total − (free + inactive pages). It moves in the same direction as Activity Monitor but the exact figures differ by design
- System notifications are posted via AppleScript (`display notification`); macOS may ask you to allow notifications from "Script Editor" the first time
- Some exclusive fullscreen apps may still cover the ball despite topmost
- Retina and multi-monitor coordinate handling is implemented but not yet broadly verified on real hardware — if the ball, snapping, or toasts look misplaced, please report with your display arrangement (screenshot of System Settings → Displays)
- No `.app` bundle yet: run via `dotnet run` or the published executable from a terminal

## 🤝 Community

- [Contributing guide](./CONTRIBUTING.md)
- [Code of Conduct](./CODE_OF_CONDUCT.md)
- [Security policy](./SECURITY.md)

## 📄 License

Lychee for macOS is licensed under the [MIT License](./LICENSE).
