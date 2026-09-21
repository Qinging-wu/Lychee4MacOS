using System.Runtime.InteropServices;

namespace Lychee.Platform;

/// <summary>
/// Global (screen-space) cursor position, independent of window focus.
/// - macOS: CGEventGetLocation, returns coordinates in points with origin at
///   the top-left of the main display (same units as Avalonia DIPs).
/// - Windows: GetCursorPos, returns device pixels; divide by RenderScaling.
/// </summary>
public static class GlobalCursor
{
    public static bool TryGetPosition(out double x, out double y)
    {
        if (OperatingSystem.IsMacOS())
        {
            return TryGetMac(out x, out y);
        }
        if (OperatingSystem.IsWindows())
        {
            if (GetCursorPos(out var pt))
            {
                x = pt.X;
                y = pt.Y;
                return true;
            }
        }
        x = 0;
        y = 0;
        return false;
    }

    private static bool TryGetMac(out double x, out double y)
    {
        x = 0;
        y = 0;
        try
        {
            var ev = CGEventCreate(IntPtr.Zero);
            if (ev == IntPtr.Zero) return false;
            try
            {
                var p = CGEventGetLocation(ev);
                x = p.x;
                y = p.y;
                return true;
            }
            finally
            {
                CFRelease(ev);
            }
        }
        catch
        {
            return false;
        }
    }

    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphics)]
    private static extern CGPoint CGEventGetLocation(IntPtr ev);

    [DllImport(CoreGraphics)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double x;
        public double y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
