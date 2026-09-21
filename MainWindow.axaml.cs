using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lychee.Core;
using Lychee.Modules;
using Lychee.Platform;

namespace Lychee;

public partial class MainWindow : Window
{
    private const double BallSize = 48;
    private const double BallMargin = 4;
    private const double BallSlot = 56;          // total window size occupied by the ball (incl. margins)
    private const double PanelWidth = 260;
    private const double PanelMargin = 4;
    private const double DragThreshold = 5;
    private const double MenuBarInset = 25;      // fallback when macOS WorkingArea includes the menu bar

    private readonly SettingsService _settings;
    private readonly ModuleManager _moduleManager;
    private readonly TrayIconService _tray;
    private readonly DispatcherTimer _hoverTimer;
    private readonly ScaleTransform _ballScale;
    private DispatcherTimer? _slideTimer;
    private bool _isExpanded = false;
    private bool _isHoveringBall = false;
    private Views.SettingsWindow? _settingsWindow;
    private Window? _alertToast;
    private bool _shutdownStarted;

    private bool _isRightSide = true;
    private bool _isBottomSide = true;
    private bool _isDragging;
    private bool _isMouseDown;
    private bool _dragStarted;
    private bool _restorePinnedAfterDrag;
    private Point _dragStartCursor;              // screen space (same units as Window.Position)

    public MainWindow()
    {
        InitializeComponent();

        _ballScale = (ScaleTransform)(BallEllipse.RenderTransform ??= new ScaleTransform());

        _settings = new SettingsService();
        _moduleManager = new ModuleManager(_settings);
        _moduleManager.RegisterModule(new DateTimeModule());
        _moduleManager.RegisterModule(new NetworkSpeedModule());
        _moduleManager.RegisterModule(new PublicIpModule());
        _moduleManager.RegisterModule(new CpuModule());
        _moduleManager.RegisterModule(new MemoryModule());
        _moduleManager.RegisterModule(new LatencyModule());

        if (_moduleManager.Get("public-ip") is PublicIpModule ipModule)
        {
            ipModule.IpChanged += OnIpChanged;
            ipModule.QueryFailed += OnIpQueryFailed;
            ipModule.QueryRecovered += OnIpQueryRecovered;
        }

        _moduleManager.ValueChanged += (s, e) => Dispatcher.UIThread.Post(RefreshModuleList);
        _settings.Changed += (s, e) => Dispatcher.UIThread.Post(ApplySettings);

        _tray = new TrayIconService();
        _tray.ShowHideRequested += (s, e) => Dispatcher.UIThread.Post(ToggleVisibility);
        _tray.SettingsRequested += (s, e) => Dispatcher.UIThread.Post(OpenSettings);
        _tray.ExitRequested += (s, e) => Dispatcher.UIThread.Post(ShutdownApp);

        Width = BallSlot;
        Height = BallSlot;

        _hoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _hoverTimer.Tick += (s, e) => HoverTimer_Tick();
        _hoverTimer.Start();

        _ballScale.Transitions = new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(120) },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(120) },
        };
        BallGlow.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) },
        };
        PanelBorder.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) },
        };

        RefreshModuleList();
        ApplyBallStyle();
    }

    public void StartModules() => _moduleManager.StartAll();

    // ---------- coordinate helpers ----------

    // All window/screen/cursor math happens in SCREEN SPACE: the same units as
    // Window.Position, Screen.Bounds/WorkingArea and the global cursor (device
    // pixels on Windows, Cocoa points on macOS). DIPs are used only for layout
    // (Width/Height, Canvas positions).
    //
    // The single platform assumption lives in DipToScreen: on Windows a DIP is
    // RenderScaling screen pixels; on macOS a DIP is one Cocoa point, so the
    // conversion is the identity. If a future Avalonia version changes the
    // macOS unit convention, this is the only function to touch.

    private double DipToScreen(double dip) =>
        OperatingSystem.IsWindows() && RenderScaling > 0 ? dip * RenderScaling : dip;

    private void SetWindowPosPx(double left, double top)
        => Position = new PixelPoint(
            (int)Math.Round(left),
            (int)Math.Round(top));

    /// <summary>Ball's top-left in screen space.</summary>
    private PixelPoint BallScreenPos() => new PixelPoint(
        (int)Math.Round(Position.X + DipToScreen(Canvas.GetLeft(BallEllipse))),
        (int)Math.Round(Position.Y + DipToScreen(Canvas.GetTop(BallEllipse))));

    private PixelPoint WindowTopLeftPx() => Position;

    private PixelPoint WindowBottomRightPx() => new PixelPoint(
        (int)Math.Round(Position.X + DipToScreen(Bounds.Width)),
        (int)Math.Round(Position.Y + DipToScreen(Bounds.Height)));

    /// <summary>
    /// Global cursor position in screen space. macOS CGEventGetLocation returns
    /// points (matching PixelPoint there); Windows GetCursorPos returns device
    /// pixels (matching PixelPoint there). Falls back to the window's own
    /// position when the platform call fails.
    /// </summary>
    private Point RawCursor()
    {
        if (GlobalCursor.TryGetPosition(out var x, out var y))
            return new Point(x, y);
        return new Point(Position.X, Position.Y);
    }

    private PixelRect GetWorkAreaPx(Screen screen)
    {
        var wa = screen.WorkingArea;
        if (OperatingSystem.IsMacOS() && wa.Height == screen.Bounds.Height)
        {
            // Some Avalonia macOS versions report the full frame as WorkingArea;
            // reserve room for the menu bar so the ball never sits behind it.
            var inset = (int)Math.Round(DipToScreen(MenuBarInset));
            wa = new PixelRect(wa.X, wa.Y + inset, wa.Width, Math.Max(0, wa.Height - inset));
        }
        return wa;
    }

    private Screen? ScreenFromWindowOrFallback()
    {
        try
        {
            return Screens.ScreenFromWindow(this) ?? Screens.All.FirstOrDefault();
        }
        catch
        {
            return Screens.All.FirstOrDefault();
        }
    }

    /// <summary>Union of all monitor bounds, in screen space.</summary>
    private PixelRect GetVirtualScreenPx()
    {
        var left = int.MaxValue;
        var top = int.MaxValue;
        var right = int.MinValue;
        var bottom = int.MinValue;
        foreach (var s in Screens.All)
        {
            var b = s.Bounds;
            left = Math.Min(left, b.X);
            top = Math.Min(top, b.Y);
            right = Math.Max(right, b.Right);
            bottom = Math.Max(bottom, b.Bottom);
        }
        if (left == int.MaxValue) return default;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private void SyncBallGlow()
    {
        Canvas.SetLeft(BallGlow, Canvas.GetLeft(BallEllipse) - BallMargin);
        Canvas.SetTop(BallGlow, Canvas.GetTop(BallEllipse) - BallMargin);
    }

    // ---------- lifecycle ----------

    private void Window_Opened(object? sender, EventArgs e)
    {
        MacAppActivation.SetAccessory();
        PositionToRightEdge();
        StartModules();
        ApplySettings();
    }

    private void PositionToRightEdge()
    {
        var screen = ScreenFromWindowOrFallback();
        if (screen == null) return;
        var wa = GetWorkAreaPx(screen);

        Width = BallSlot;
        Height = BallSlot;
        Canvas.SetLeft(BallEllipse, BallMargin);
        Canvas.SetTop(BallEllipse, BallMargin);
        SyncBallGlow();
        Canvas.SetTop(PanelBorder, PanelMargin);

        var slot = DipToScreen(BallSlot);
        SetWindowPosPx(wa.Right - slot, wa.Y + (wa.Height - slot) / 2);

        _isRightSide = true;
        _isBottomSide = Position.Y + slot / 2 >= wa.Y + wa.Height / 2.0;
    }

    // ---------- module list ----------

    private void RefreshModuleList()
    {
        var visibleModules = _moduleManager.Modules.Where(m => m.IsEnabled).ToList();
        var current = ModuleList.ItemsSource as List<IInfoModule>;
        if (current != null &&
            current.Count == visibleModules.Count &&
            !current.Except(visibleModules).Any())
        {
            return;
        }
        ModuleList.ItemsSource = visibleModules;
    }

    private double CalcExpandedHeight(int moduleCount)
    {
        var approxItemHeight = 70;
        var headerHeight = 50;
        var padding = 16;
        var target = headerHeight + padding + moduleCount * approxItemHeight;
        if (target > 540) target = 540;
        return target;
    }

    // ---------- settings ----------

    private void ApplySettings()
    {
        var s = _settings.Current;

        if (s.AlwaysShowPanel)
        {
            ExpandPanel(immediate: true);
        }
        else if (!_isHoveringBall)
        {
            CollapsePanel(immediate: true);
        }

        if (s.SnapToEdge)
        {
            SnapToNearestEdge();
        }
    }

    private void ApplyBallStyle()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Lychee/Assets/icon.png"));
            var bitmap = new Bitmap(stream);
            BallEllipse.Fill = new ImageBrush
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill
            };
        }
        catch (Exception ex)
        {
            AppLog.Error("ball-style", ex);
            BallEllipse.Fill = Brushes.Gray;
        }
    }

    // ---------- expand / collapse ----------

    private void ExpandPanel(bool immediate = false)
    {
        if (_isExpanded) return;
        _isExpanded = true;

        var ballScreen = BallScreenPos();

        var visibleModules = _moduleManager.Modules.Count(m => m.IsEnabled);
        var targetHeight = CalcExpandedHeight(visibleModules);
        var targetWidth = PanelWidth + BallSlot + PanelMargin;

        Width = targetWidth;
        Height = targetHeight;

        PanelBorder.Height = targetHeight - PanelMargin * 2;
        PanelBorder.IsVisible = true;

        if (_isRightSide)
        {
            Canvas.SetLeft(PanelBorder, PanelMargin);
            Canvas.SetLeft(BallEllipse, PanelWidth + PanelMargin);
            SetWindowPosPx(ballScreen.X - DipToScreen(PanelWidth + PanelMargin), Position.Y);
        }
        else
        {
            Canvas.SetLeft(BallEllipse, BallMargin);
            Canvas.SetLeft(PanelBorder, BallSlot);
            SetWindowPosPx(ballScreen.X - DipToScreen(BallMargin), Position.Y);
        }

        if (_isBottomSide)
        {
            // expand upward when docked to bottom
            SetWindowPosPx(Position.X, ballScreen.Y - DipToScreen(targetHeight - BallSlot + BallMargin));
            Canvas.SetTop(BallEllipse, targetHeight - BallSlot + BallMargin);
            Canvas.SetTop(PanelBorder, PanelMargin);
        }
        else
        {
            SetWindowPosPx(Position.X, ballScreen.Y - DipToScreen(BallMargin));
            Canvas.SetTop(BallEllipse, BallMargin);
            Canvas.SetTop(PanelBorder, PanelMargin);
        }

        SyncBallGlow();

        // fade the panel in (transition attached in the constructor)
        PanelBorder.Opacity = 0;
        Dispatcher.UIThread.Post(() => PanelBorder.Opacity = 1);
    }

    private void CollapsePanel(bool immediate = false, bool force = false)
    {
        if (!_isExpanded) return;
        _isExpanded = false;

        if (_settings.Current.AlwaysShowPanel && !force) return;

        var ballScreen = BallScreenPos();

        Width = BallSlot;
        Height = BallSlot;
        Canvas.SetLeft(BallEllipse, BallMargin);
        Canvas.SetTop(BallEllipse, BallMargin);
        SyncBallGlow();
        SetWindowPosPx(ballScreen.X - DipToScreen(BallMargin), ballScreen.Y - DipToScreen(BallMargin));

        PanelBorder.IsVisible = false;
    }

    // ---------- hover ----------

    private void HoverTimer_Tick()
    {
        if (!IsVisible || _isDragging || _isMouseDown) return;

        var cursor = RawCursor();
        var tl = WindowTopLeftPx();
        var br = WindowBottomRightPx();

        bool inWindow = cursor.X >= tl.X && cursor.X <= br.X
                     && cursor.Y >= tl.Y && cursor.Y <= br.Y;

        if (inWindow && !_isHoveringBall)
        {
            _isHoveringBall = true;
            _ballScale.ScaleX = 1.1;
            _ballScale.ScaleY = 1.1;
            BallGlow.Opacity = 0.9;
            if (!_settings.Current.AlwaysShowPanel)
            {
                ExpandPanel();
            }
        }
        else if (!inWindow && _isHoveringBall)
        {
            _isHoveringBall = false;
            _ballScale.ScaleX = 1.0;
            _ballScale.ScaleY = 1.0;
            BallGlow.Opacity = 0.6;
            if (!_settings.Current.AlwaysShowPanel)
            {
                CollapsePanel();
            }
        }
    }

    private bool IsPointerOverPanel() => PanelBorder.IsPointerOver;

    // ---------- ball interaction ----------

    private void Ball_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(BallEllipse);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (e.ClickCount >= 2)
        {
            TogglePin();
            return;
        }

        _isMouseDown = true;
        _dragStartCursor = RawCursor();

        if (_isExpanded)
        {
            _restorePinnedAfterDrag = _settings.Current.AlwaysShowPanel;
            CollapsePanel(immediate: true, force: _settings.Current.AlwaysShowPanel);
        }

        _isDragging = false;
        _dragStarted = false;
        e.Pointer.Capture(BallEllipse);
    }

    private void Ball_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isMouseDown) return;
        var point = e.GetCurrentPoint(BallEllipse);
        if (!point.Properties.IsLeftButtonPressed) return;

        var current = RawCursor();
        var dx = current.X - _dragStartCursor.X;
        var dy = current.Y - _dragStartCursor.Y;

        if (!_dragStarted && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
        {
            _dragStarted = true;
            _isDragging = true;
        }

        if (_isDragging)
        {
            var newLeft = Position.X + dx;
            var newTop = Position.Y + dy;

            var vs = GetVirtualScreenPx();
            var slot = DipToScreen(BallSlot);
            if (newLeft < vs.X) newLeft = vs.X;
            if (newLeft + slot > vs.Right) newLeft = vs.Right - slot;
            if (newTop < vs.Y) newTop = vs.Y;
            if (newTop + slot > vs.Bottom) newTop = vs.Bottom - slot;

            SetWindowPosPx(newLeft, newTop);
        }
    }

    private void Ball_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        _isMouseDown = false;
        e.Pointer.Capture(null);

        if (_isDragging)
        {
            _isDragging = false;
            _dragStarted = false;
            RefreshBallSideFromPosition();
            if (_settings.Current.SnapToEdge)
            {
                SnapToNearestEdge(RestorePinnedPanelAfterDrag);
            }
            else
            {
                RestorePinnedPanelAfterDrag();
            }
        }
    }

    private void RestorePinnedPanelAfterDrag()
    {
        if (!_restorePinnedAfterDrag) return;
        _restorePinnedAfterDrag = false;
        if (_settings.Current.AlwaysShowPanel)
        {
            ExpandPanel();
        }
    }

    private void RefreshBallSideFromPosition()
    {
        Screen? screen;
        try
        {
            screen = Screens.ScreenFromPoint(Position) ?? Screens.All.FirstOrDefault();
        }
        catch
        {
            screen = Screens.All.FirstOrDefault();
        }
        if (screen == null) return;

        var wa = GetWorkAreaPx(screen);
        var ballSize = DipToScreen(BallSize);
        var ballScreen = BallScreenPos();
        var ballCenterX = ballScreen.X + ballSize / 2;
        var ballCenterY = ballScreen.Y + ballSize / 2;
        _isRightSide = ballCenterX >= wa.X + wa.Width / 2.0;
        _isBottomSide = ballCenterY >= wa.Y + wa.Height / 2.0;
    }

    private void SnapToNearestEdge(Action? onCompleted = null)
    {
        var screen = ScreenFromWindowOrFallback();
        if (screen == null)
        {
            onCompleted?.Invoke();
            return;
        }
        var wa = GetWorkAreaPx(screen);

        // Ball's actual screen position and its window-internal offset
        // (works whether the panel is collapsed or expanded).
        var ballScreen = BallScreenPos();
        var ballOffsetX = ballScreen.X - Position.X;
        var ballOffsetY = ballScreen.Y - Position.Y;
        var ballSize = DipToScreen(BallSize);
        var ballCenterX = ballScreen.X + ballSize / 2;
        var ballCenterY = ballScreen.Y + ballSize / 2;

        var dLeft = Math.Abs(ballCenterX - wa.X);
        var dRight = Math.Abs(wa.Right - ballCenterX);
        var dTop = Math.Abs(ballCenterY - wa.Y);
        var dBottom = Math.Abs(wa.Bottom - ballCenterY);

        var min = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));

        var targetLeft = (double)Position.X;
        var targetTop = (double)Position.Y;
        if (min == dLeft) targetLeft = wa.X - ballOffsetX;
        else if (min == dRight) targetLeft = wa.Right - ballOffsetX - ballSize;
        else if (min == dTop) targetTop = wa.Y - ballOffsetY;
        else targetTop = wa.Bottom - ballOffsetY - ballSize;

        _isRightSide = ballCenterX >= wa.X + wa.Width / 2.0;
        _isBottomSide = ballCenterY >= wa.Y + wa.Height / 2.0;

        if (Math.Abs(targetLeft - Position.X) < 0.5 && Math.Abs(targetTop - Position.Y) < 0.5)
        {
            onCompleted?.Invoke();
            return;
        }

        SlideWindowTo(targetLeft, targetTop, 120, onCompleted);
    }

    private void SlideWindowTo(double targetLeft, double targetTop, int durationMs, Action? onCompleted = null)
    {
        _slideTimer?.Stop();
        _slideTimer = null;

        var startLeft = (double)Position.X;
        var startTop = (double)Position.Y;
        var sw = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _slideTimer = timer;
        timer.Tick += (s, e) =>
        {
            var t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / durationMs);
            var k = 1 - Math.Pow(1 - t, 3); // easeOutCubic
            SetWindowPosPx(
                startLeft + (targetLeft - startLeft) * k,
                startTop + (targetTop - startTop) * k);
            if (t >= 1)
            {
                timer.Stop();
                if (_slideTimer == timer) _slideTimer = null;
                onCompleted?.Invoke();
            }
        };
        timer.Start();
    }

    // ---------- commands ----------

    private void Pin_Click(object? sender, RoutedEventArgs e) => TogglePin();

    private void TogglePin()
    {
        _settings.Update(s => s.AlwaysShowPanel = !s.AlwaysShowPanel);
        if (_settings.Current.AlwaysShowPanel)
        {
            ExpandPanel();
        }
        else if (!_isHoveringBall)
        {
            CollapsePanel();
        }
    }

    private void Hide_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current.AlwaysShowPanel)
        {
            _settings.Update(s => s.AlwaysShowPanel = false);
        }
        CollapsePanel();
    }

    private void Quit_Click(object? sender, RoutedEventArgs e) => ShutdownApp();

    private void Settings_Click(object? sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new Views.SettingsWindow(_settings, _moduleManager);
        _settingsWindow.AttachOwner(this);
        _settingsWindow.Closed += (s, e) =>
        {
            if (ReferenceEquals(_settingsWindow, s)) _settingsWindow = null;
        };
        _settingsWindow.Show();
    }

    private void ToggleVisibility()
    {
        IsVisible = !IsVisible;
    }

    private async void ShutdownApp()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        _hoverTimer.Stop();
        _slideTimer?.Stop();
        await _moduleManager.DisposeAsync();
        _tray.Dispose();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }

    // ---------- alerts ----------

    private void OnIpChanged(object? sender, IpChangedEventArgs e)
    {
        if (!_settings.Current.AlertOnIpChange) return;

        Dispatcher.UIThread.Post(() =>
        {
            MacNotifier.Notify("Public IP Changed", $"Old: {e.OldIp}  New: {e.NewIp}");
            ShowAlertToast("Public IP Changed", null, $"Old: {e.OldIp}", $"New: {e.NewIp}");
        });
    }

    private void OnIpQueryFailed(object? sender, QueryFailedEventArgs e)
    {
        if (!_settings.Current.AlertOnQueryFailure) return;

        Dispatcher.UIThread.Post(() =>
        {
            MacNotifier.Notify("Public IP Query Failed", $"{e.Message} Network may be down or VPN dropped.");
            ShowAlertToast(
                "Public IP Query Failed",
                null,
                e.Message,
                "Network may be down or VPN dropped.");
        });
    }

    private void OnIpQueryRecovered(object? sender, QueryRecoveredEventArgs e)
    {
        if (!_settings.Current.AlertOnQueryFailure) return;

        Dispatcher.UIThread.Post(() =>
        {
            MacNotifier.Notify("Network Recovered", $"Public IP: {e.Ip}");
            var displayLines = e.Display.Split('\n');
            displayLines[0] = $"IP: {displayLines[0]}";
            ShowAlertToast(
                "Network Recovered",
                Color.FromArgb(0xF2, 0x2E, 0xCC, 0x71),
                displayLines);
        });
    }

    private void ShowAlertToast(string title, Color? background, params string[] lines)
    {
        _alertToast?.Close();
        _alertToast = null;

        var toast = new Window
        {
            Width = 320,
            Height = 120,
            SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false
        };

        var border = new Border
        {
            Background = new SolidColorBrush(
                background ?? Color.FromArgb(0xF2, 0xE8, 0x4D, 0x3D)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(6)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 6)
        });
        foreach (var line in lines)
        {
            stack.Children.Add(new TextBlock
            {
                Text = line,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }
        border.Child = stack;

        border.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(400) }
        };
        toast.Content = border;

        var screen = ScreenFromWindowOrFallback();
        if (screen != null)
        {
            var wa = GetWorkAreaPx(screen);
            toast.Position = new PixelPoint(
                (int)(wa.Right - DipToScreen(toast.Width) - DipToScreen(16)),
                (int)(wa.Bottom - DipToScreen(toast.Height) - DipToScreen(16)));
        }

        _alertToast = toast;
        toast.Show();

        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        closeTimer.Tick += (s, e) =>
        {
            closeTimer.Stop();
            border.Opacity = 0;
            var fadeDone = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            fadeDone.Tick += (s2, e2) =>
            {
                fadeDone.Stop();
                toast.Close();
                if (_alertToast == toast) _alertToast = null;
            };
            fadeDone.Start();
        };
        closeTimer.Start();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_settings.Current.AlwaysShowPanel && !_isHoveringBall && !IsPointerOverPanel())
        {
            CollapsePanel();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_shutdownStarted)
        {
            e.Cancel = true;
            ShutdownApp();
            return;
        }
        base.OnClosing(e);
    }
}
