using System;
using System.Runtime.InteropServices;

namespace YAHAA.Common
{
    /// <summary>
    /// A minimal system-tray (notification area) icon implemented directly on top of the Win32
    /// Shell_NotifyIcon API. The icon is loaded straight from an .ico file so it always renders,
    /// and the context menu is a native popup menu (no truncation / async-image issues).
    ///
    /// Must be created and disposed on the UI thread; the WinUI message loop pumps the messages
    /// for the hidden message-only window this creates.
    /// </summary>
    public sealed partial class TrayIcon : IDisposable
    {
        private const uint WM_TRAYCALLBACK = 0x8000 + 1; // WM_APP + 1
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_NULL = 0x0000;

        private const uint NIM_ADD = 0x0;
        private const uint NIM_MODIFY = 0x1;
        private const uint NIM_DELETE = 0x2;
        private const uint NIF_MESSAGE = 0x1;
        private const uint NIF_ICON = 0x2;
        private const uint NIF_TIP = 0x4;

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x10;

        private const uint MF_STRING = 0x0;
        private const uint MF_SEPARATOR = 0x800;
        private const uint TPM_RIGHTBUTTON = 0x2;
        private const uint TPM_NONOTIFY = 0x80;
        private const uint TPM_RETURNCMD = 0x100;

        private const int SM_CXSMICON = 49;
        private const int SM_CYSMICON = 50;

        private const uint MENU_OPEN = 1;
        private const uint MENU_EXIT = 2;

        private static readonly IntPtr HWND_MESSAGE = new(-3);

        private readonly WndProcDelegate _wndProc; // kept alive to avoid GC of the callback
        private IntPtr _hwnd;
        private IntPtr _hIcon;
        private ushort _classAtom;
        private int _uid;
        private bool _added;

        private Action? _onOpen;
        private Action? _onExit;

        public TrayIcon()
        {
            _wndProc = WndProc;
        }

        /// <summary>Creates the tray icon. <paramref name="iconPath"/> must be a real .ico file path.</summary>
        public void Create(string iconPath, string tooltip, Action onOpen, Action onExit)
        {
            _onOpen = onOpen;
            _onExit = onExit;
            _uid = 1;

            var hInstance = GetModuleHandleW(null);

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = "YAHAA_TrayWindow",
            };
            _classAtom = RegisterClassExW(ref wc); // 0 is fine if the class already exists

            _hwnd = CreateWindowExW(0, "YAHAA_TrayWindow", "YAHAA_Tray", 0,
                0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);

            var size = GetSystemMetrics(SM_CXSMICON);
            _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);

            var data = NewData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = WM_TRAYCALLBACK;
            data.hIcon = _hIcon;
            data.szTip = tooltip;

            _added = Shell_NotifyIconW(NIM_ADD, ref data);
        }

        /// <summary>Swaps the tray icon to a different .ico file at runtime.</summary>
        public void UpdateIcon(string iconPath)
        {
            if (!_added) return;

            var size = GetSystemMetrics(SM_CXSMICON);
            var newIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);
            if (newIcon == IntPtr.Zero) return;

            var data = NewData();
            data.uFlags = NIF_ICON;
            data.hIcon = newIcon;

            if (Shell_NotifyIconW(NIM_MODIFY, ref data))
            {
                if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
                _hIcon = newIcon;
            }
            else
            {
                DestroyIcon(newIcon);
            }
        }

        private NOTIFYICONDATA NewData() => new()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _uid,
        };

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_TRAYCALLBACK)
            {
                var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMessage == WM_LBUTTONUP)
                {
                    _onOpen?.Invoke();
                }
                else if (mouseMessage == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                }
                return IntPtr.Zero;
            }

            return DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private void ShowContextMenu()
        {
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;

            try
            {
                AppendMenuW(menu, MF_STRING, MENU_OPEN, "Open YAHAA");
                AppendMenuW(menu, MF_SEPARATOR, 0, null);
                AppendMenuW(menu, MF_STRING, MENU_EXIT, "Exit");

                GetCursorPos(out var pt);

                // Required so the menu dismisses correctly when the user clicks elsewhere.
                SetForegroundWindow(_hwnd);

                var command = TrackPopupMenuEx(menu,
                    TPM_RETURNCMD | TPM_NONOTIFY | TPM_RIGHTBUTTON,
                    pt.X, pt.Y, _hwnd, IntPtr.Zero);

                PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

                if (command == MENU_OPEN) _onOpen?.Invoke();
                else if (command == MENU_EXIT) _onExit?.Invoke();
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        public void Dispose()
        {
            if (_added)
            {
                var data = NewData();
                Shell_NotifyIconW(NIM_DELETE, ref data);
                _added = false;
            }

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            if (_classAtom != 0)
            {
                UnregisterClassW("YAHAA_TrayWindow", GetModuleHandleW(null));
                _classAtom = 0;
            }
        }

        // ---- Win32 interop ----

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
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
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public int uID;
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

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImageW(IntPtr hinst, string lpszName, uint uType, int cxDesired,
            int cyDesired, uint fuLoad);

        [DllImport("user32")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

        [DllImport("user32")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32")]
        private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);
    }
}
