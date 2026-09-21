using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Lychee.Core;

/// <summary>
/// Menu bar / system tray icon built on Avalonia's cross-platform TrayIcon
/// (NSStatusItem on macOS, NotifyIcon equivalent on Windows).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TrayIcon _icon;
    private bool _disposed;

    public event EventHandler? ShowHideRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        WindowIcon? icon = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Lychee/Assets/icon.png"));
            icon = new WindowIcon(stream);
        }
        catch
        {
            // A missing icon must not prevent startup.
        }

        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show / Hide");
        showItem.Click += (s, e) => ShowHideRequested?.Invoke(this, EventArgs.Empty);

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var quitItem = new NativeMenuItem("Quit Lychee");
        quitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(showItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);

        _icon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Lychee",
            IsVisible = true,
            Menu = menu
        };
        _icon.Clicked += (s, e) => ShowHideRequested?.Invoke(this, EventArgs.Empty);

        var icons = new TrayIcons { _icon };
        TrayIcon.SetIcons(Application.Current!, icons);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.IsVisible = false;
        try
        {
            var icons = TrayIcon.GetIcons(Application.Current!);
            icons?.Remove(_icon);
        }
        catch { }
        _icon.Dispose();
    }
}
