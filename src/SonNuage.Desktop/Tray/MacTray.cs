using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SonNuage.Desktop.Tray;

/// <summary>
/// Icône de la barre des menus macOS (NSStatusItem), par le runtime Objective-C. Les menus envoient leurs clics à
/// une petite classe Objective-C créée ici, qui rappelle le code .NET avec l'index de l'entrée (tag).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacTray : ITrayIcon
{
    private static MacTray? _current;
    private static IntPtr _handlerClass;

    private readonly IntPtr _statusItem;
    private readonly IntPtr _handler;
    private IReadOnlyList<TrayItem> _items = [];

    // Un clic ouvre le menu, comme pour les autres icônes de la barre des menus.
    public event Action? Activated
    {
        add { }
        remove { }
    }

    public MacTray(string iconPath, string title)
    {
        var bar = Objc.Send(Objc.Class("NSStatusBar"), "systemStatusBar");
        _statusItem = Objc.Retain(Objc.SendDouble(bar, "statusItemWithLength:", -1 /* NSVariableStatusItemLength */));
        var button = Objc.Send(_statusItem, "button");
        var image = Objc.Send(Objc.Send(Objc.Class("NSImage"), "alloc"), "initWithContentsOfFile:", Objc.String(iconPath));
        if (image != IntPtr.Zero)
        {
            ((delegate* unmanaged<IntPtr, IntPtr, double, double, void>)Objc.MsgSend)(image, Objc.Sel("setSize:"), 18, 18);
            Objc.Send(button, "setImage:", image);
        }
        else
        {
            Objc.Send(button, "setTitle:", Objc.String(title));
        }
        Objc.Send(button, "setToolTip:", Objc.String(title));
        _handler = Objc.Send(Objc.Send(HandlerClass(), "alloc"), "init");
        _current = this;
    }

    public void SetMenu(IReadOnlyList<TrayItem> items)
    {
        _items = items;
        var menu = Objc.Send(Objc.Send(Objc.Class("NSMenu"), "alloc"), "init");
        Objc.SendBool(menu, "setAutoenablesItems:", false);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            IntPtr entry;
            if (item.Label is null)
            {
                entry = Objc.Send(Objc.Class("NSMenuItem"), "separatorItem");
            }
            else
            {
                entry = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)Objc.MsgSend)(
                    Objc.Send(Objc.Class("NSMenuItem"), "alloc"), Objc.Sel("initWithTitle:action:keyEquivalent:"),
                    Objc.String(item.Label), Objc.Sel("onItem:"), Objc.String(""));
                Objc.Send(entry, "setTarget:", _handler);
                ((delegate* unmanaged<IntPtr, IntPtr, nint, void>)Objc.MsgSend)(entry, Objc.Sel("setTag:"), i);
                Objc.SendBool(entry, "setEnabled:", item.Enabled);
            }
            Objc.Send(menu, "addItem:", entry);
        }
        Objc.Send(_statusItem, "setMenu:", menu);
    }

    public void SetToolTip(string text) => Objc.Send(Objc.Send(_statusItem, "button"), "setToolTip:", Objc.String(text));

    private static IntPtr HandlerClass()
    {
        if (_handlerClass != IntPtr.Zero) return _handlerClass;
        var cls = Objc.objc_allocateClassPair(Objc.Class("NSObject"), "SonNuageTrayHandler", 0);
        Objc.class_addMethod(cls, Objc.Sel("onItem:"), (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnItem, "v@:@");
        Objc.objc_registerClassPair(cls);
        return _handlerClass = cls;
    }

    [UnmanagedCallersOnly]
    private static void OnItem(IntPtr self, IntPtr selector, IntPtr sender)
    {
        var index = (int)((delegate* unmanaged<IntPtr, IntPtr, nint>)Objc.MsgSend)(sender, Objc.Sel("tag"));
        if (_current is { } tray && index >= 0 && index < tray._items.Count) tray._items[index].OnClick?.Invoke();
    }

    public void Dispose()
    {
        Objc.Send(Objc.Send(Objc.Class("NSStatusBar"), "systemStatusBar"), "removeStatusItem:", _statusItem);
        if (_current == this) _current = null;
    }
}

/// <summary>Fenêtre Cocoa de Photino, retrouvée parmi les fenêtres de l'application par son titre.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacWindow(string title) : IWindowControl
{
    public bool IsVisible => Window() is var w && w != IntPtr.Zero && Objc.SendReturnsBool(w, "isVisible");

    public void Hide()
    {
        if (Window() is var w && w != IntPtr.Zero) Objc.Send(w, "orderOut:", IntPtr.Zero);
    }

    public void Show()
    {
        var w = Window();
        if (w == IntPtr.Zero) return;
        if (Objc.SendReturnsBool(w, "isMiniaturized")) Objc.Send(w, "deminiaturize:", IntPtr.Zero);
        Objc.Send(w, "makeKeyAndOrderFront:", IntPtr.Zero);
        Objc.SendBool(Objc.Send(Objc.Class("NSApplication"), "sharedApplication"), "activateIgnoringOtherApps:", true);
    }

    private IntPtr Window()
    {
        var windows = Objc.Send(Objc.Send(Objc.Class("NSApplication"), "sharedApplication"), "windows");
        var count = (long)Objc.Send(windows, "count");
        for (long i = 0; i < count; i++)
        {
            var w = Objc.SendLong(windows, "objectAtIndex:", i);
            if (Marshal.PtrToStringUTF8(Objc.Send(Objc.Send(w, "title"), "UTF8String")) == title) return w;
        }
        return IntPtr.Zero;
    }
}

[SupportedOSPlatform("macos")]
internal static unsafe class Objc
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    public static readonly IntPtr MsgSend = NativeLibrary.GetExport(NativeLibrary.Load(Lib), "objc_msgSend");

    public static IntPtr Class(string name) => objc_getClass(name);
    public static IntPtr Sel(string name) => sel_registerName(name);

    public static IntPtr Send(IntPtr target, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)MsgSend)(target, Sel(selector));

    public static IntPtr Send(IntPtr target, string selector, IntPtr arg) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(target, Sel(selector), arg);

    public static IntPtr SendDouble(IntPtr target, string selector, double arg) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double, IntPtr>)MsgSend)(target, Sel(selector), arg);

    public static IntPtr SendLong(IntPtr target, string selector, long arg) =>
        ((delegate* unmanaged<IntPtr, IntPtr, long, IntPtr>)MsgSend)(target, Sel(selector), arg);

    public static void SendBool(IntPtr target, string selector, bool arg) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte, void>)MsgSend)(target, Sel(selector), arg ? (byte)1 : (byte)0);

    public static bool SendReturnsBool(IntPtr target, string selector) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte>)MsgSend)(target, Sel(selector)) != 0;

    public static IntPtr Retain(IntPtr target) => Send(target, "retain");

    /// <summary>NSString (libérée avec le pool de la boucle d'événements).</summary>
    public static IntPtr String(string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return Send(Class("NSString"), "stringWithUTF8String:", utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [DllImport(Lib)] private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Lib)] public static extern IntPtr objc_allocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);
    [DllImport(Lib)] public static extern void objc_registerClassPair(IntPtr cls);
    [DllImport(Lib)] public static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);
}
