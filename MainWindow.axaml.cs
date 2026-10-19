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
using System.Runtime.InteropServices;
using Lychee.Core;
using Lychee.Modules;
using Lychee.Platform;
using SkiaSharp;

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
    private PixelPoint _dragOriginWindow;        // window origin when the drag gesture began
    private double _panelWidth = PanelWidth;
    private bool _resizingPanel;
    private Point _resizeStartCursor;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private DispatcherTimer? _ballGifTimer;
    private IReadOnlyList<(Bitmap Frame, TimeSpan Delay)>? _ballGifFrames;
    private Bitmap? _ballStyleBitmap;
    private int _ballGifFrameIndex;
    private string? _appliedBallStyle;
    private string? _appliedCustomBallPath;

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
        _tray.ApplyStatusIcon();
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
        if (_appliedBallStyle != s.FloatingBallStyle || _appliedCustomBallPath != s.CustomBallImagePath)
            ApplyBallStyle();
        ApplyPanelAppearance();

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
        _ballGifTimer?.Stop();
        _ballGifTimer = null;
        if (_ballGifFrames != null)
            foreach (var frame in _ballGifFrames) frame.Frame.Dispose();
        _ballGifFrames = null;
        _ballStyleBitmap?.Dispose();
        _ballStyleBitmap = null;
        _ballGifFrameIndex = 0;
        _appliedBallStyle = _settings.Current.FloatingBallStyle;
        _appliedCustomBallPath = _settings.Current.CustomBallImagePath;
        try
        {
            var style = _settings.Current.FloatingBallStyle;
            var asset = style switch
            {
                "basketball" => "basketball.png",
                "planet" => "planet.png",
                "moon" => "moon.png",
                "pixel" => "pixel-lychee.gif",
                "custom" when File.Exists(_settings.Current.CustomBallImagePath) => _settings.Current.CustomBallImagePath!,
                _ => "icon.png"
            };
            using var stream = Path.IsPathRooted(asset)
                ? File.OpenRead(asset)
                : AssetLoader.Open(new Uri($"avares://Lychee/Assets/{asset}"));
            if (string.Equals(Path.GetExtension(asset), ".gif", StringComparison.OrdinalIgnoreCase))
            {
                _ballGifFrames = DecodeGifFrames(stream);
                var first = _ballGifFrames[0];
                BallEllipse.Fill = new ImageBrush { Source = first.Frame, Stretch = Stretch.UniformToFill };
                if (_ballGifFrames.Count > 1)
                {
                    _ballGifTimer = new DispatcherTimer { Interval = first.Delay };
                    _ballGifTimer.Tick += BallGifTimer_Tick;
                    _ballGifTimer.Start();
                }
                return;
            }

            var bitmap = new Bitmap(stream);
            _ballStyleBitmap = bitmap;
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

    private static IReadOnlyList<(Bitmap Frame, TimeSpan Delay)> DecodeGifFrames(Stream stream)
    {
        using var data = SKData.Create(stream);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unable to decode GIF image.");
        var count = Math.Clamp(codec.FrameCount, 1, 120);
        var info = new SKImageInfo(48, 48, SKColorType.Bgra8888, SKAlphaType.Premul);
        var frames = new List<(Bitmap, TimeSpan)>(count);
        for (var index = 0; index < count; index++)
        {
            var pixels = new byte[info.BytesSize];
            var options = new SKCodecOptions(index);
            var address = Marshal.AllocHGlobal(info.BytesSize);
            SKCodecResult result;
            try
            {
                Marshal.Copy(new byte[info.BytesSize], 0, address, info.BytesSize);
                result = codec.GetPixels(info, address, options);
                Marshal.Copy(address, pixels, 0, pixels.Length);
            }
            finally { Marshal.FreeHGlobal(address); }
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                throw new InvalidDataException($"Unable to decode GIF frame {index}: {result}.");

            var bitmap = new WriteableBitmap(new PixelSize(48, 48), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using (var locked = bitmap.Lock())
            {
                for (var row = 0; row < 48; row++)
                    Marshal.Copy(pixels, row * 48 * 4, IntPtr.Add(locked.Address, row * locked.RowBytes), 48 * 4);
            }

            var delayMs = codec.GetFrameInfo(index, out var frameInfo) ? frameInfo.Duration : 100;
            frames.Add((bitmap, TimeSpan.FromMilliseconds(Math.Clamp(delayMs, 20, 10000))));
        }
        return frames;
    }

    private void BallGifTimer_Tick(object? sender, EventArgs e)
    {
        if (_ballGifFrames is not { Count: > 1 } frames || BallEllipse.Fill is not ImageBrush brush) return;
        _ballGifFrameIndex = (_ballGifFrameIndex + 1) % frames.Count;
        brush.Source = frames[_ballGifFrameIndex].Frame;
        _ballGifTimer!.Interval = frames[_ballGifFrameIndex].Delay;
    }

    private void ApplyPanelAppearance()
    {
        var s = _settings.Current;
        if (!s.UseCustomPanelAppearance)
        {
            PanelBorder.Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 46));
            Resources["ModuleNameBrush"] = new SolidColorBrush(Color.FromRgb(204, 204, 204));
            Resources["ModuleValueBrush"] = Brushes.White;
            Resources["ModuleDetailBrush"] = new SolidColorBrush(Color.FromRgb(143, 143, 159));
            Resources["ModuleFontFamily"] = new FontFamily("Arial");
            return;
        }
        var opacity = (byte)Math.Round(Math.Clamp(s.PanelBackgroundOpacity, 0, 100) * 2.55);
        PanelBorder.Background = new SolidColorBrush(Color.FromArgb(opacity,
            (byte)Math.Clamp(s.PanelBackgroundRed, 0, 255), (byte)Math.Clamp(s.PanelBackgroundGreen, 0, 255), (byte)Math.Clamp(s.PanelBackgroundBlue, 0, 255)));
        var text = new SolidColorBrush(Color.FromRgb((byte)Math.Clamp(s.PanelTextRed, 0, 255),
            (byte)Math.Clamp(s.PanelTextGreen, 0, 255), (byte)Math.Clamp(s.PanelTextBlue, 0, 255)));
        Resources["ModuleNameBrush"] = text;
        Resources["ModuleValueBrush"] = text;
        Resources["ModuleDetailBrush"] = text;
        try { Resources["ModuleFontFamily"] = new FontFamily(s.PanelFontFamily); }
        catch (ArgumentException) { Resources["ModuleFontFamily"] = new FontFamily("Arial"); }
    }

    // ---------- expand / collapse ----------

    private void ExpandPanel(bool immediate = false)
    {
        if (_isExpanded) return;
        _isExpanded = true;

        var ballScreen = BallScreenPos();
        var screen = ScreenFromWindowOrFallback();
        var wa = screen != null ? GetWorkAreaPx(screen) : GetVirtualScreenPx();

        var visibleModules = _moduleManager.Modules.Count(m => m.IsEnabled);
        var targetHeight = CalcExpandedHeight(visibleModules);
        var targetWidth = _panelWidth + BallSlot + PanelMargin;

        // Place the ball inside the expanded window so it stays on the same
        // screen pixel. If that window would cross the menu bar or dock,
        // clamp the window and slide the ball inside it by the same amount.
        var ballLeft = _isRightSide ? _panelWidth + PanelMargin : BallMargin;
        var ballTop = _isBottomSide ? targetHeight - BallSlot + BallMargin : BallMargin;
        var windowLeft = ballScreen.X - ballLeft;
        var windowTop = ballScreen.Y - ballTop;

        if (windowTop < wa.Y)
        {
            ballTop -= wa.Y - windowTop;
            windowTop = wa.Y;
        }
        if (windowTop + targetHeight > wa.Bottom)
        {
            var overflow = windowTop + targetHeight - wa.Bottom;
            windowTop -= overflow;
            ballTop += overflow;
            if (windowTop < wa.Y)
            {
                ballTop -= wa.Y - windowTop;
                windowTop = wa.Y;
            }
        }
        if (windowLeft < wa.X)
        {
            ballLeft -= wa.X - windowLeft;
            windowLeft = wa.X;
        }
        if (windowLeft + targetWidth > wa.Right)
        {
            var overflow = windowLeft + targetWidth - wa.Right;
            windowLeft -= overflow;
            ballLeft += overflow;
        }

        SetWindowPosPx(windowLeft, windowTop);
        Width = targetWidth;
        Height = targetHeight;

        PanelBorder.Height = targetHeight - PanelMargin * 2;
        PanelBorder.Width = _panelWidth;
        PanelBorder.IsVisible = true;
        Canvas.SetLeft(PanelBorder, _isRightSide ? PanelMargin : BallSlot);
        Canvas.SetTop(PanelBorder, PanelMargin);
        Canvas.SetLeft(BallEllipse, ballLeft);
        Canvas.SetTop(BallEllipse, ballTop);

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

    private void Panel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isExpanded || !e.GetCurrentPoint(PanelBorder).Properties.IsLeftButtonPressed) return;
        var point = e.GetPosition(PanelBorder);
        const double grip = 9;
        var onOuterHorizontalEdge = _isRightSide ? point.X <= grip : point.X >= PanelBorder.Bounds.Width - grip;
        var onOuterVerticalEdge = _isBottomSide ? point.Y <= grip : point.Y >= PanelBorder.Bounds.Height - grip;
        if (!onOuterHorizontalEdge && !onOuterVerticalEdge) return;

        _resizingPanel = true;
        _resizeStartCursor = RawCursor();
        _resizeStartWidth = _panelWidth;
        _resizeStartHeight = Height;
        e.Pointer.Capture(PanelBorder);
        e.Handled = true;
    }

    private void Panel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_resizingPanel) return;
        var cursor = RawCursor();
        var dx = cursor.X - _resizeStartCursor.X;
        var dy = cursor.Y - _resizeStartCursor.Y;
        _panelWidth = Math.Clamp(_resizeStartWidth + (_isRightSide ? -dx : dx), 160, 560);
        var height = Math.Clamp(_resizeStartHeight + (_isBottomSide ? -dy : dy), 180, 800);
        var ball = BallScreenPos();

        Width = _panelWidth + BallSlot + PanelMargin;
        Height = height;
        PanelBorder.Width = _panelWidth;
        PanelBorder.Height = height - PanelMargin * 2;
        if (_isRightSide)
        {
            Canvas.SetLeft(PanelBorder, PanelMargin);
            Canvas.SetLeft(BallEllipse, _panelWidth + PanelMargin);
            SetWindowPosPx(ball.X - DipToScreen(_panelWidth + PanelMargin), Position.Y);
        }
        else
        {
            Canvas.SetLeft(BallEllipse, BallMargin);
            Canvas.SetLeft(PanelBorder, BallSlot);
            SetWindowPosPx(ball.X - DipToScreen(BallMargin), Position.Y);
        }
        if (_isBottomSide)
        {
            Canvas.SetTop(BallEllipse, height - BallSlot + BallMargin);
            SetWindowPosPx(Position.X, ball.Y - DipToScreen(height - BallSlot + BallMargin));
        }
        else
        {
            Canvas.SetTop(BallEllipse, BallMargin);
            SetWindowPosPx(Position.X, ball.Y - DipToScreen(BallMargin));
        }
        Canvas.SetTop(PanelBorder, PanelMargin);
        SyncBallGlow();
        e.Handled = true;
    }

    private void Panel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_resizingPanel) return;
        _resizingPanel = false;
        e.Pointer.Capture(null);
        e.Handled = true;
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

        // Collapse keeps the ball put but moves the window. Record the origin
        // after that, and add the cursor delta to this fixed origin. Adding it
        // to Position each move compounds the displacement and the ball runs away.
        _dragOriginWindow = Position;
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
            var newLeft = _dragOriginWindow.X + dx;
            var newTop = _dragOriginWindow.Y + dy;

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
