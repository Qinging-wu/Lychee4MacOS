# Lychee for macOS

[English](./README.md) | [简体中文](./README.zh-CN.md)

Lychee is a small floating system monitor for macOS. Its always-on-top ball opens an information panel when you hover over it. The app is built with .NET 8 and Avalonia UI.

> **Attention!** This project is currently still in testing. There may some issues with its use. Welcome PR.

## Features

- Date and time, refreshed every second
- Network upload and download speeds
- Public IP and approximate location, with alerts for IP changes, failed lookups, and recovery
- CPU usage and memory usage
- Network latency to several public endpoints
- Built-in floating ball designs, plus custom PNG, JPEG, BMP, and animated GIF images
- Resizable information panel
- Panel appearance settings for background opacity, background and text colors, and system font, with preview and reset; the precise color picker offers an HSV spectrum, RGB sliders, and Hex input
- Optional per-module toggles, pinning, and edge snapping
- Menu bar controls for showing the panel, opening settings, and quitting

## Use

- Hover over the ball to open the panel; move away to collapse it.
- Drag the ball to move it between screen locations. The panel flips to stay beside the ball.
- Double-click the ball or use the pin button to keep the panel open.
- Drag the expanded panel's outer edge or corner to resize it.
- Open **Settings** from the panel or menu bar to change modules, alerts, ball design, and panel appearance.
- Quit from the panel or menu bar.

## Requirements and quick start

- macOS 11 or later, Apple Silicon or Intel
- .NET SDK 8.0 or later

```bash
git clone https://github.com/Qinging-wu/Lychee4MacOS.git
cd Lychee4MacOS
dotnet run
```

The ball appears near the right edge of the screen. Lychee adds a menu bar icon and hides its Dock icon. There is currently no `.app` bundle or downloadable installer.

### Build and publish

Build:

```bash
dotnet build -c Release
```

Publish a self-contained executable for Apple Silicon or Intel:

```bash
dotnet publish -c Release -r osx-arm64 --self-contained true
dotnet publish -c Release -r osx-x64 --self-contained true
```

The published executable can be run from a terminal. These commands do not create a macOS `.app` bundle.

## Platform differences and limitations

- Frame Performance (FPS), the bouncing ball, and audio level/spectrum monitoring from the Windows version are not included.
- Memory usage is estimated from macOS memory counters; its figures may differ from Activity Monitor, and swap is not shown separately.
- System notifications use AppleScript. macOS may ask you to allow notifications from Script Editor.
- Some exclusive full-screen apps may cover the floating ball.
- Retina and multi-display positioning still needs broader testing on physical Macs. Please include your display arrangement when reporting positioning issues.

## Settings and logs

Settings and logs are stored under:

```text
~/Library/Application Support/Lychee/
```

Settings are saved in `settings.json`; the rotating application log is in `logs/lychee.log`.

## Acknowledgments

Special thanks to [Icyjerry](https://github.com/Icyjerry) for [PR #1](https://github.com/Qinging-wu/Lychee4MacOS/pull/1), which improved ball dragging, panel placement, and the menu bar icon.

## Contributing

Bug reports and pull requests are welcome. Please read the [contributing guide](./CONTRIBUTING.md), [Code of Conduct](./CODE_OF_CONDUCT.md), and [security policy](./SECURITY.md).

## License

MIT. See [LICENSE](./LICENSE).
