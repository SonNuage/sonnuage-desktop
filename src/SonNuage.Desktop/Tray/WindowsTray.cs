using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SonNuage.Desktop.Tray;

/// <summary>
/// Zone de notification Windows (Shell_NotifyIcon). Une fenêtre cachée reçoit les clics de l'icône : créée sur le
/// thread de Photino, elle profite de sa boucle de messages.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTray : ITrayIcon
{
    private const uint CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const uint IconId = 1;

    private readonly WndProc _wndProc;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly uint _taskbarCreated;
    private IReadOnlyList<TrayItem> _items = [];
    private string _tip;

    public event Action? Activated;

    public WindowsTray(string iconPath, string title)
    {
        _tip = title;
        _wndProc = OnMessage;
        var instance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = "SonNuageTray",
        };
        RegisterClassExW(ref wc);
        // Fenêtre jamais affichée, mais de premier niveau : le menu doit pouvoir passer au premier plan pour se
        // refermer quand on clique ailleurs.
        _hwnd = CreateWindowExW(0, "SonNuageTray", title, 0x80000000 /* WS_POPUP */, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        _icon = LoadImageW(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, GetSystemMetrics(49 /* SM_CXSMICON */), GetSystemMetrics(50), 0x10 /* LR_LOADFROMFILE */);
        // Explorer redémarré : la barre des tâches est recréée et l'icône doit être rajoutée.
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        Add();
    }

    private void Add()
    {
        var data = Data(NifMessage | NifIcon | NifTip | NifShowTip);
        Shell_NotifyIconW(0 /* NIM_ADD */, ref data);
        data.uVersion = 4; // NOTIFYICON_VERSION_4
        Shell_NotifyIconW(4 /* NIM_SETVERSION */, ref data);
    }

    public void SetMenu(IReadOnlyList<TrayItem> items) => _items = items;

    public void SetToolTip(string text)
    {
        // 127 caractères au plus, zéro final compris dans les 128 du champ.
        _tip = text.Length > 127 ? text[..126] + "…" : text;
        var data = Data(NifTip | NifShowTip);
        Shell_NotifyIconW(1 /* NIM_MODIFY */, ref data);
    }

    private IntPtr OnMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case 0x0202: // WM_LBUTTONUP
                case 0x0400: // NIN_SELECT
                    Activated?.Invoke();
                    break;
                case 0x007B: // WM_CONTEXTMENU
                    ShowMenu();
                    break;
            }
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0) Add();
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = _items;
        var menu = CreatePopupMenu();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Label is null) AppendMenuW(menu, 0x800 /* MF_SEPARATOR */, UIntPtr.Zero, null);
            else AppendMenuW(menu, item.Enabled ? 0u : 1u /* MF_GRAYED */, (UIntPtr)(uint)(i + 1), item.Label);
        }
        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        var chosen = TrackPopupMenuEx(menu, 0x100 /* TPM_RETURNCMD */ | 0x2 /* TPM_RIGHTBUTTON */, point.X, point.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);
        PostMessageW(_hwnd, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
        if (chosen > 0 && chosen <= items.Count) items[chosen - 1].OnClick?.Invoke();
    }

    private NOTIFYICONDATAW Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tip,
    };

    public void Dispose()
    {
        var data = Data(0);
        Shell_NotifyIconW(2 /* NIM_DELETE */, ref data);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        DestroyWindow(_hwnd);
    }

    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifShowTip = 0x80;

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}

/// <summary>Fenêtre de Photino sous Windows, par son HWND.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsWindow(Photino.NET.PhotinoWindow window) : IWindowControl
{
    public bool IsVisible => IsWindowVisible(window.WindowHandle);

    public void Hide() => ShowWindow(window.WindowHandle, 0 /* SW_HIDE */);

    public void Show()
    {
        var hwnd = window.WindowHandle;
        ShowWindow(hwnd, IsIconic(hwnd) ? 9 /* SW_RESTORE */ : 5 /* SW_SHOW */);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
