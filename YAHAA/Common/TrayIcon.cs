using System;
using System.Runtime.CompilerServices;
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

            // RegisterClassEx copies the class name into its atom table, so the buffer can be freed
            // immediately afterwards.
            var classNamePtr = Marshal.StringToHGlobalUni("YAHAA_TrayWindow");
            try
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = hInstance,
                    lpszClassName = classNamePtr,
                };
                _classAtom = RegisterClassExW(ref wc); // 0 is fine if the class already exists
            }
            finally
            {
                Marshal.FreeHGlobal(classNamePtr);
            }

            _hwnd = CreateWindowExW(0, "YAHAA_TrayWindow", "YAHAA_Tray", 0,
                0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);

            var size = GetSystemMetrics(SM_CXSMICON);
            _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, size, size, LR_LOADFROMFILE);

            var data = NewData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = WM_TRAYCALLBACK;
            data.hIcon = _hIcon;
            SetTip(data.szTip, tooltip);

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

        [StructLayout(LayoutKind.Sequential)]
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
            public IntPtr lpszMenuName;
            public IntPtr lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public int uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            public Utf16Buffer128 szTip;
            public uint dwState;
            public uint dwStateMask;
            public Utf16Buffer256 szInfo;
            public uint uVersion;
            public Utf16Buffer64 szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        // Fixed-size UTF-16 buffers replacing ByValTStr strings, so the structs above stay blittable
        // (a requirement for LibraryImport source-generated marshalling). ushort elements avoid the
        // ANSI/Unicode ambiguity of char in a marshalled struct.
        [InlineArray(128)]
        private struct Utf16Buffer128 { private ushort _element0; }

        [InlineArray(256)]
        private struct Utf16Buffer256 { private ushort _element0; }

        [InlineArray(64)]
        private struct Utf16Buffer64 { private ushort _element0; }

        /// <summary>Copies a string into a fixed UTF-16 buffer, truncating and null-terminating.</summary>
        private static void SetTip(Span<ushort> buffer, string value)
        {
            buffer.Clear();
            var count = Math.Min(value.Length, buffer.Length - 1);
            MemoryMarshal.Cast<char, ushort>(value.AsSpan(0, count)).CopyTo(buffer);
        }

        [LibraryImport("kernel32", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr GetModuleHandleW(string? lpModuleName);

        [LibraryImport("user32")]
        private static partial ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnregisterClassW(string lpClassName, IntPtr hInstance);

        [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [LibraryImport("user32")]
        private static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyWindow(IntPtr hWnd);

        [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr LoadImageW(IntPtr hinst, string lpszName, uint uType, int cxDesired,
            int cyDesired, uint fuLoad);

        [LibraryImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyIcon(IntPtr hIcon);

        [LibraryImport("user32")]
        private static partial int GetSystemMetrics(int nIndex);

        [LibraryImport("user32")]
        private static partial IntPtr CreatePopupMenu();

        [LibraryImport("user32", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

        [LibraryImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DestroyMenu(IntPtr hMenu);

        [LibraryImport("user32")]
        private static partial uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

        [LibraryImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);

        [LibraryImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out POINT lpPoint);

        [LibraryImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("shell32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);
    }
}
