using System.Runtime.InteropServices;

namespace Lychee.Platform;

/// <summary>
/// Sets the app to the macOS "accessory" activation policy so no Dock icon
/// appears. Pure runtime NSApplication call — works from a bare executable,
/// no .app bundle required.
/// </summary>
public static class MacAppActivation
{
    private const int NSApplicationActivationPolicyAccessory = 1;

    public static void SetAccessory()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var cls = objc_getClass("NSApplication");
            if (cls == IntPtr.Zero) return;
            var app = objc_msgSend(cls, sel_registerName("sharedApplication"));
            if (app == IntPtr.Zero) return;
            objc_msgSend(app, sel_registerName("setActivationPolicy:"), new IntPtr(NSApplicationActivationPolicyAccessory));
        }
        catch
        {
            // Cosmetic only — never block startup.
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr argument);
}
