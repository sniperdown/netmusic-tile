using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NetMusicTile
{
    /// <summary>Thin P/Invoke surface. No logic lives here on purpose.</summary>
    internal static class Win32
    {
        // ---- window styles ----
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;

        // ---- messages ----
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_HOTKEY = 0x0312;
        public const int MA_NOACTIVATE = 3;
        public const int VK_ESCAPE = 0x1B;

        // ---- SetWindowPos ----
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowW(string className, string windowName);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNOACTIVATE = 4;

        // ---- DWM cloaking ----
        // The shell frequently "hides" a window by asking DWM to cloak it rather
        // than by moving it. A cloaked window still reports IsWindowVisible=true
        // and keeps its old rect, which is exactly the false positive that kept
        // this panel on screen.
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
        private const int DWMWA_CLOAKED = 14;

        /// <summary>True when the shell has asked DWM to cloak this window.</summary>
        public static bool IsCloaked(IntPtr hWnd)
        {
            try
            {
                int cloaked;
                int hr = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
                return hr == 0 && cloaked != 0;
            }
            catch
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO mi);
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        /// <summary>
        /// True when the window's rect actually overlaps the monitor it is on.
        /// A window parked off-screen (x=-16000) fails this.
        /// </summary>
        public static bool RectOnMonitor(IntPtr hWnd, out RECT rect)
        {
            rect = new RECT();
            try
            {
                if (!GetWindowRect(hWnd, out rect)) return false;
                IntPtr mon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfoW(mon, ref mi)) return false;

                bool overlaps = rect.Right > mi.rcMonitor.Left && rect.Left < mi.rcMonitor.Right
                             && rect.Bottom > mi.rcMonitor.Top && rect.Top < mi.rcMonitor.Bottom;
                return overlaps;
            }
            catch
            {
                return false;
            }
        }
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLengthW(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        public const byte VK_MEDIA_PREV_TRACK = 0xB1;
        public const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

        // ---- WM_APPCOMMAND: the official "media control" message ----
        // Posted straight at the player's window, so it does not depend on how
        // synthetic keyboard input gets routed while the Start menu holds focus.
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessageW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public const int WM_APPCOMMAND = 0x0319;

        public const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
        public const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
        public const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;

        /// <summary>Send one media command to a specific window.</summary>
        public static bool SendAppCommand(IntPtr hWnd, int appCommand)
        {
            try
            {
                IntPtr lParam = (IntPtr)(appCommand << 16);
                return PostMessageW(hWnd, WM_APPCOMMAND, hWnd, lParam);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Send one media key press+release (fallback path).</summary>
        public static void SendMediaKey(byte vk)
        {
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, IntPtr.Zero);
            System.Threading.Thread.Sleep(40);
            keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, IntPtr.Zero);
        }

        // ---- SendInput: the modern replacement for keybd_event ----
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        /// <summary>
        /// Send a media key through SendInput.
        ///
        /// Preferred over keybd_event: SendInput goes through the same path as real
        /// hardware input and is far less likely to be ignored by an application or
        /// a system input filter.
        /// </summary>
        public static bool SendMediaKeyInput(byte vk)
        {
            try
            {
                var inputs = new INPUT[2];

                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = vk;
                inputs[0].u.ki.wScan = 0;
                inputs[0].u.ki.dwFlags = KEYEVENTF_EXTENDEDKEY;
                inputs[0].u.ki.time = 0;
                inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].u.ki.wVk = vk;
                inputs[1].u.ki.wScan = 0;
                inputs[1].u.ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;
                inputs[1].u.ki.time = 0;
                inputs[1].u.ki.dwExtraInfo = IntPtr.Zero;

                uint sent = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                return sent == 2;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        public const uint GW_CHILD = 5;
        public const uint GW_HWNDNEXT = 2;

        /// <summary>
        /// Position of <paramref name="target"/> in the top-level Z order, walked
        /// from the front (0 = frontmost). Returns -1 when not found.
        ///
        /// Index comparisons are the only reliable way to answer "who is drawn on
        /// top" without eyeballing a screenshot.
        /// </summary>
        public static int ZOrderIndex(IntPtr target)
        {
            try
            {
                IntPtr h = GetWindow(GetDesktopWindow(), GW_CHILD);
                int i = 0;
                while (h != IntPtr.Zero && i < 2000)
                {
                    if (h == target) return i;
                    h = GetWindow(h, GW_HWNDNEXT);
                    i++;
                }
            }
            catch
            {
            }
            return -1;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr param);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr param);

        /// <summary>Read a window title safely (empty string on any failure).</summary>
        public static string GetTitle(IntPtr hWnd)
        {
            try
            {
                int len = GetWindowTextLengthW(hWnd);
                if (len <= 0) return string.Empty;
                var sb = new StringBuilder(len + 2);
                GetWindowTextW(hWnd, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
