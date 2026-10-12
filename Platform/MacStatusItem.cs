using System.Runtime.InteropServices;

namespace Lychee.Platform;

/// <summary>
/// Menu-bar status item. Avalonia's tray icon calls the deprecated
/// NSStatusItem.image setter, which does not paint on current macOS, so the
/// slot exists but stays blank. This sets NSStatusItem.button.image instead.
/// </summary>
public sealed class MacStatusItem : IDisposable
{
    private const double SquareLength = -2; // NSSquareStatusItemLength

    private static readonly MenuCallback ShowHideImp = OnShowHide;
    private static readonly MenuCallback SettingsImp = OnSettings;
    private static readonly MenuCallback QuitImp = OnQuit;
    private static MacStatusItem? _current;
    private static bool _classReady;

    private readonly byte[] _png;
    private IntPtr _item;
    private IntPtr _image;
    private bool _disposed;

    public event EventHandler? ShowHideRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public MacStatusItem(byte[] png)
    {
        _png = png;
        _current = this;
        EnsureClass();

        var statusBar = Msg(Cls("NSStatusBar"), Sel("systemStatusBar"));
        _item = MsgD(statusBar, Sel("statusItemWithLength:"), SquareLength);
        if (_item == IntPtr.Zero) return;

        var menu = Msg(Msg(Cls("NSMenu"), Sel("alloc")), Sel("init"));
        var target = Msg(Msg(Cls("LycheeStatusTarget"), Sel("alloc")), Sel("init"));
        AddItem(menu, target, "Show / Hide", "showHide:");
        AddItem(menu, target, "Settings...", "openSettings:");
        Msg(menu, Sel("addItem:"), Msg(Cls("NSMenuItem"), Sel("separatorItem")));
        AddItem(menu, target, "Quit Lychee", "quitApp:");
        // Assigning the menu recreates the status button and drops anything
        // already drawn on it. Paint the icon after the menu is in place.
        Msg(_item, Sel("setMenu:"), menu);
        Apply();
    }

    public void Apply()
    {
        if (_disposed || _item == IntPtr.Zero) return;
        var button = Msg(_item, Sel("button"));
        if (button == IntPtr.Zero) return;
        if (_image == IntPtr.Zero)
            _image = CreateImage(_png);
        if (_image == IntPtr.Zero) return;

        MsgD2(_image, Sel("setSize:"), 18, 18);
        MsgBool(_image, Sel("setTemplate:"), false);
        MsgNint(button, Sel("setImagePosition:"), 1); // NSImageOnly
        Msg(button, Sel("setImage:"), _image);
        Msg(button, Sel("setToolTip:"), NsString("Lychee"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_item != IntPtr.Zero)
        {
            var bar = Msg(_item, Sel("statusBar"));
            if (bar != IntPtr.Zero)
                Msg(bar, Sel("removeStatusItem:"), _item);
            _item = IntPtr.Zero;
        }
        if (_current == this) _current = null;
    }

    private static void AddItem(IntPtr menu, IntPtr target, string title, string action)
    {
        var item = Msg(Cls("NSMenuItem"), Sel("alloc"));
        item = Msg3(item, Sel("initWithTitle:action:keyEquivalent:"),
            NsString(title), Sel(action), NsString(""));
        Msg(item, Sel("setTarget:"), target);
        Msg(menu, Sel("addItem:"), item);
    }

    private static IntPtr CreateImage(byte[] png)
    {
        if (png.Length == 0) return IntPtr.Zero;
        var ptr = Marshal.AllocHGlobal(png.Length);
        try
        {
            Marshal.Copy(png, 0, ptr, png.Length);
            var data = MsgPtrNint(Cls("NSData"), Sel("dataWithBytes:length:"), ptr, png.Length);
            if (data == IntPtr.Zero) return IntPtr.Zero;
            return Msg(Msg(Cls("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void EnsureClass()
    {
        if (_classReady) return;
        var cls = Cls("LycheeStatusTarget");
        if (cls == IntPtr.Zero)
        {
            cls = objc_allocateClassPair(Cls("NSObject"), "LycheeStatusTarget", 0);
            if (cls == IntPtr.Zero) return;
            AddMethod(cls, "showHide:", ShowHideImp);
            AddMethod(cls, "openSettings:", SettingsImp);
            AddMethod(cls, "quitApp:", QuitImp);
            objc_registerClassPair(cls);
        }
        _classReady = true;
    }

    private static void AddMethod(IntPtr cls, string name, MenuCallback callback)
    {
        class_addMethod(cls, Sel(name), Marshal.GetFunctionPointerForDelegate(callback), "v@:@");
    }

    private static void OnShowHide(IntPtr self, IntPtr cmd, IntPtr sender)
        => _current?.ShowHideRequested?.Invoke(_current, EventArgs.Empty);

    private static void OnSettings(IntPtr self, IntPtr cmd, IntPtr sender)
        => _current?.SettingsRequested?.Invoke(_current, EventArgs.Empty);

    private static void OnQuit(IntPtr self, IntPtr cmd, IntPtr sender)
        => _current?.ExitRequested?.Invoke(_current, EventArgs.Empty);

    private static IntPtr NsString(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return Msg(Cls("NSString"), Sel("stringWithUTF8String:"), ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MenuCallback(IntPtr self, IntPtr cmd, IntPtr sender);

    private const string ObjC = "/usr/lib/libobjc.dylib";

    private static IntPtr Cls(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);

    private static IntPtr Msg(IntPtr recv, IntPtr sel) => objc_msgSend(recv, sel);
    private static IntPtr Msg(IntPtr recv, IntPtr sel, IntPtr a) => objc_msgSend_1(recv, sel, a);

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);

    [DllImport(ObjC)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjC)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr recv, IntPtr sel);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_1(IntPtr recv, IntPtr sel, IntPtr a);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Msg3(IntPtr recv, IntPtr sel, IntPtr a, IntPtr b, IntPtr c);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgD(IntPtr recv, IntPtr sel, double arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgD2(IntPtr recv, IntPtr sel, double a, double b);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgNint(IntPtr recv, IntPtr sel, nint arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgBool(IntPtr recv, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgPtrNint(IntPtr recv, IntPtr sel, IntPtr ptr, nint length);
}
