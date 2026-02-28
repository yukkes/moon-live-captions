using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MoonLiveCaptions.Helpers
{
    /// <summary>
    /// Windows AppBar API manager for docking a window at the top or bottom of the screen.
    /// When docked, the system reserves desktop space (other windows are pushed away).
    /// </summary>
    public class AppBarManager : IDisposable
    {
        // ── Win32 Interop ────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const uint ABM_NEW = 0x00;
        private const uint ABM_REMOVE = 0x01;
        private const uint ABM_QUERYPOS = 0x02;
        private const uint ABM_SETPOS = 0x03;

        private const int ABN_STATECHANGE = 0x00;
        private const int ABN_POSCHANGED = 0x01;
        private const int ABN_FULLSCREENAPP = 0x02;

        public const uint ABE_TOP = 1;
        public const uint ABE_BOTTOM = 3;

        // ── State ────────────────────────────────────────────────

        private Window _window;
        private IntPtr _hwnd;
        private bool _isRegistered;
        private uint _callbackMessage;
        private uint _currentEdge;
        private double _desiredHeightDips;
        private HwndSource _hwndSource;

        public bool IsRegistered => _isRegistered;

        public AppBarManager(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        /// <summary>
        /// Dock the window at the specified screen edge.
        /// </summary>
        /// <param name="edge">ABE_TOP or ABE_BOTTOM</param>
        /// <param name="heightDips">Desired height in device-independent pixels</param>
        public void Dock(uint edge, double heightDips)
        {
            if (_isRegistered)
                Undock();

            _hwnd = new WindowInteropHelper(_window).Handle;
            if (_hwnd == IntPtr.Zero) return;

            _hwndSource = HwndSource.FromHwnd(_hwnd);
            if (_hwndSource == null) return;

            _currentEdge = edge;
            _desiredHeightDips = heightDips;

            // Register callback message
            _callbackMessage = RegisterWindowMessage("MoonLiveCaptions_AppBar_" + _hwnd.ToInt64());

            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = _hwnd;
            abd.uCallbackMessage = _callbackMessage;

            SHAppBarMessage(ABM_NEW, ref abd);
            _isRegistered = true;

            // Hook WndProc
            _hwndSource.AddHook(WndProc);

            // Position the appbar
            PositionAppBar();
        }

        /// <summary>
        /// Undock the window and free the reserved screen space.
        /// </summary>
        public void Undock()
        {
            if (!_isRegistered) return;

            _hwndSource?.RemoveHook(WndProc);

            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = _hwnd;

            SHAppBarMessage(ABM_REMOVE, ref abd);
            _isRegistered = false;
        }

        private void PositionAppBar()
        {
            if (!_isRegistered || _hwnd == IntPtr.Zero) return;

            // Get DPI scaling
            var source = PresentationSource.FromVisual(_window);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int heightPx = (int)(_desiredHeightDips * dpiY);

            // Get monitor info for the monitor containing this window
            IntPtr hMonitor = MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            GetMonitorInfo(hMonitor, ref mi);

            var abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = _hwnd;
            abd.uEdge = _currentEdge;

            if (_currentEdge == ABE_TOP)
            {
                abd.rc.Left = mi.rcMonitor.Left;
                abd.rc.Top = mi.rcMonitor.Top;
                abd.rc.Right = mi.rcMonitor.Right;
                abd.rc.Bottom = mi.rcMonitor.Top + heightPx;
            }
            else // ABE_BOTTOM
            {
                abd.rc.Left = mi.rcMonitor.Left;
                abd.rc.Top = mi.rcMonitor.Bottom - heightPx;
                abd.rc.Right = mi.rcMonitor.Right;
                abd.rc.Bottom = mi.rcMonitor.Bottom;
            }

            SHAppBarMessage(ABM_QUERYPOS, ref abd);

            // Adjust the rect after query
            if (_currentEdge == ABE_TOP)
                abd.rc.Bottom = abd.rc.Top + heightPx;
            else
                abd.rc.Top = abd.rc.Bottom - heightPx;

            SHAppBarMessage(ABM_SETPOS, ref abd);

            // Set window position (convert physical pixels to DIPs)
            _window.Left = abd.rc.Left / dpiX;
            _window.Top = abd.rc.Top / dpiY;
            _window.Width = (abd.rc.Right - abd.rc.Left) / dpiX;
            _window.Height = (abd.rc.Bottom - abd.rc.Top) / dpiY;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_isRegistered && msg == (int)_callbackMessage)
            {
                switch (wParam.ToInt32())
                {
                    case ABN_POSCHANGED:
                        PositionAppBar();
                        handled = true;
                        break;

                    case ABN_FULLSCREENAPP:
                        // When a full-screen app activates/deactivates,
                        // we may want to adjust z-order
                        break;
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Undock();
        }
    }
}
