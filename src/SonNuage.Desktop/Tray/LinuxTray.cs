using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SonNuage.Desktop.Tray;

/// <summary>
/// Indicateur Linux (StatusNotifierItem) par libayatana-appindicator, sur la boucle GTK de Photino. KDE, Xfce,
/// Cinnamon et Ubuntu l'affichent. GNOME demande l'extension AppIndicator.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed unsafe class LinuxTray : ITrayIcon
{
    private const string Gtk = "libgtk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    private static LinuxTray? _current;

    private readonly IntPtr _indicator;
    private IntPtr _menu;
    private IReadOnlyList<TrayItem> _items = [];

    // Sans clic principal sous Linux : l'indicateur ouvre toujours le menu.
    public event Action? Activated
    {
        add { }
        remove { }
    }

    private LinuxTray(IntPtr indicator) => _indicator = indicator;

    /// <summary>null si aucune bibliothèque d'indicateur n'est installée.</summary>
    public static LinuxTray? TryCreate(string iconDirectory, string iconName, string title)
    {
        if (!AppIndicator.Load()) return null;
        var indicator = AppIndicator.New("sonnuage", iconName, 0 /* APP_INDICATOR_CATEGORY_APPLICATION_STATUS */);
        AppIndicator.SetIconThemePath(indicator, iconDirectory);
        AppIndicator.SetIcon(indicator, iconName);
        AppIndicator.SetTitle(indicator, title);
        AppIndicator.SetStatus(indicator, 1 /* APP_INDICATOR_STATUS_ACTIVE */);
        return _current = new LinuxTray(indicator);
    }

    public void SetMenu(IReadOnlyList<TrayItem> items)
    {
        _items = items;
        var menu = gtk_menu_new();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var widget = item.Label is null ? gtk_separator_menu_item_new() : gtk_menu_item_new_with_label(item.Label);
            if (item.Label is not null)
            {
                gtk_widget_set_sensitive(widget, item.Enabled);
                g_signal_connect_data(widget, "activate", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnActivate, (IntPtr)i, IntPtr.Zero, 0);
            }
            gtk_menu_shell_append(menu, widget);
        }
        gtk_widget_show_all(menu);
        AppIndicator.SetMenu(_indicator, menu);
        if (_menu != IntPtr.Zero) gtk_widget_destroy(_menu);
        _menu = menu;
    }

    // L'indicateur n'a pas de bulle : le titre reste « SonNuage ».
    public void SetToolTip(string text) { }

    [UnmanagedCallersOnly]
    private static void OnActivate(IntPtr widget, IntPtr index)
    {
        if (_current is { } tray && (int)index is var i && i >= 0 && i < tray._items.Count) tray._items[i].OnClick?.Invoke();
    }

    public void Dispose()
    {
        AppIndicator.SetStatus(_indicator, 0 /* APP_INDICATOR_STATUS_PASSIVE */);
        if (_current == this) _current = null;
    }

    [DllImport(Gtk)] private static extern IntPtr gtk_menu_new();
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] private static extern IntPtr gtk_separator_menu_item_new();
    [DllImport(Gtk)] private static extern void gtk_menu_shell_append(IntPtr menu, IntPtr item);
    [DllImport(Gtk)] private static extern void gtk_widget_set_sensitive(IntPtr widget, bool sensitive);
    [DllImport(Gtk)] private static extern void gtk_widget_show_all(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_widget_destroy(IntPtr widget);
    [DllImport(GObject)] private static extern ulong g_signal_connect_data(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, IntPtr handler, IntPtr data, IntPtr destroy, int flags);

    /// <summary>libayatana-appindicator3 (Debian, Ubuntu, Fedora), sinon l'ancienne libappindicator3.</summary>
    private static class AppIndicator
    {
        private static IntPtr _lib;

        public static bool Load() =>
            _lib != IntPtr.Zero
            || NativeLibrary.TryLoad("libayatana-appindicator3.so.1", out _lib)
            || NativeLibrary.TryLoad("libappindicator3.so.1", out _lib);

        private static IntPtr Fn(string name) => NativeLibrary.GetExport(_lib, name);

        public static IntPtr New(string id, string icon, int category)
        {
            using var idText = new Utf8(id);
            using var iconText = new Utf8(icon);
            return ((delegate* unmanaged<IntPtr, IntPtr, int, IntPtr>)Fn("app_indicator_new"))(idText, iconText, category);
        }

        public static void SetIconThemePath(IntPtr indicator, string path) => Call(indicator, "app_indicator_set_icon_theme_path", path);
        public static void SetIcon(IntPtr indicator, string icon) => Call(indicator, "app_indicator_set_icon", icon);
        public static void SetTitle(IntPtr indicator, string title) => Call(indicator, "app_indicator_set_title", title);

        public static void SetStatus(IntPtr indicator, int status) =>
            ((delegate* unmanaged<IntPtr, int, void>)Fn("app_indicator_set_status"))(indicator, status);

        public static void SetMenu(IntPtr indicator, IntPtr menu) =>
            ((delegate* unmanaged<IntPtr, IntPtr, void>)Fn("app_indicator_set_menu"))(indicator, menu);

        private static void Call(IntPtr indicator, string function, string text)
        {
            using var utf8 = new Utf8(text);
            ((delegate* unmanaged<IntPtr, IntPtr, void>)Fn(function))(indicator, utf8);
        }
    }

    private readonly struct Utf8(string text) : IDisposable
    {
        private readonly IntPtr _ptr = Marshal.StringToCoTaskMemUTF8(text);
        public static implicit operator IntPtr(Utf8 s) => s._ptr;
        public void Dispose() => Marshal.FreeCoTaskMem(_ptr);
    }
}

/// <summary>Fenêtre GTK de Photino, retrouvée parmi les fenêtres de premier niveau par son titre.</summary>
[SupportedOSPlatform("linux")]
internal sealed class GtkWindow(string title) : IWindowControl
{
    private const string Gtk = "libgtk-3.so.0";
    private IntPtr _window;

    public bool IsVisible => Window() is var w && w != IntPtr.Zero && gtk_widget_get_visible(w);

    public void Hide()
    {
        if (Window() is var w && w != IntPtr.Zero) gtk_widget_hide(w);
    }

    public void Show()
    {
        var w = Window();
        if (w == IntPtr.Zero) return;
        gtk_widget_show(w);
        gtk_window_deiconify(w);
        gtk_window_present(w);
    }

    private IntPtr Window()
    {
        if (_window != IntPtr.Zero) return _window;
        var list = gtk_window_list_toplevels();
        for (var node = list; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size))
        {
            var candidate = Marshal.ReadIntPtr(node);
            if (Marshal.PtrToStringUTF8(gtk_window_get_title(candidate)) == title)
            {
                _window = candidate;
                break;
            }
        }
        g_list_free(list);
        return _window;
    }

    [DllImport(Gtk)] private static extern IntPtr gtk_window_list_toplevels();
    [DllImport(Gtk)] private static extern IntPtr gtk_window_get_title(IntPtr window);
    [DllImport(Gtk)] private static extern bool gtk_widget_get_visible(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_widget_hide(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_widget_show(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_window_deiconify(IntPtr window);
    [DllImport(Gtk)] private static extern void gtk_window_present(IntPtr window);
    [DllImport("libglib-2.0.so.0")] private static extern void g_list_free(IntPtr list);
}
