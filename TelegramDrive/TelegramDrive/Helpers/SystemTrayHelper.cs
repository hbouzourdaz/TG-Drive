using System;
using System.Runtime.InteropServices;
using Microsoft.UI;

namespace TelegramDrive.Helpers;

public static class SystemTrayHelper
{
    private const int WM_USER = 0x0400;
    public const int WM_TRAYICON = WM_USER + 101;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    public const int NIIF_INFO = 0x00000001;
    public const int NIIF_WARNING = 0x00000002;
    public const int NIIF_ERROR = 0x00000003;

    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, [In] ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);

    // Subclassing delegate and APIs
    public delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private static NOTIFYICONDATA _nid;
    private static bool _iconAdded;
    private static SUBCLASSPROC? _subclassProc;
    private static IntPtr _windowHandle;
    private static Action? _onRestore;

    public static void Initialize(IntPtr hwnd, Action onRestore)
    {
        _windowHandle = hwnd;
        _onRestore = onRestore;

        // Subclass window to intercept tray events
        _subclassProc = new SUBCLASSPROC(WindowSubclassProc);
        SetWindowSubclass(hwnd, _subclassProc, new IntPtr(1001), IntPtr.Zero);
    }

    public static void AddTrayIcon(string tooltip)
    {
        if (_iconAdded) RemoveTrayIcon();

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
            if (System.IO.File.Exists(iconPath))
            {
                // 1 = IMAGE_ICON, 0x00000010 = LR_LOADFROMFILE, 0x00000020 = LR_DEFAULTSIZE
                hIcon = LoadImage(IntPtr.Zero, iconPath, 1, 0, 0, 0x00000010 | 0x00000020);
            }

            if (hIcon == IntPtr.Zero)
            {
                hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
            }
        }
        catch 
        {
            try { hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); } catch { }
        }

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
            hWnd = _windowHandle,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = tooltip
        };

        _iconAdded = Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    public static void RemoveTrayIcon()
    {
        if (!_iconAdded) return;
        Shell_NotifyIcon(NIM_DELETE, ref _nid);
        _iconAdded = false;
    }

    public static void ShowNotification(string title, string message, int infoFlags = NIIF_INFO)
    {
        if (!_iconAdded) return;

        _nid.uFlags |= NIF_INFO;
        _nid.szInfoTitle = title;
        _nid.szInfo = message;
        _nid.dwInfoFlags = infoFlags;

        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    public static void MinimizeToTray()
    {
        // Use SW_HIDE but keep the handle valid; FindWindow by class/title still works
        ShowWindow(_windowHandle, SW_HIDE);
    }

    public static void RestoreFromTray()
    {
        ShowWindow(_windowHandle, SW_RESTORE);
        SetForegroundWindow(_windowHandle);
        _onRestore?.Invoke();
    }

    private static IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_TRAYICON)
        {
            int mouseMsg = (int)lParam;
            if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK || mouseMsg == WM_RBUTTONUP)
            {
                RestoreFromTray();
                return IntPtr.Zero;
            }
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }
}
