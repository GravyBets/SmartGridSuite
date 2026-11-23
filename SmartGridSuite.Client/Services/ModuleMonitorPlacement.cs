using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SmartGridSuite.Client.Services
{
    /// <summary>
    /// Positions module windows on the monitor containing the launcher,
    /// and returns the launcher to the monitor where the module was closed.
    /// Uses native pixel coordinates so monitors with different DPI scaling
    /// and negative desktop coordinates are handled without DIP assumptions.
    /// </summary>
    internal static class ModuleMonitorPlacement
    {
        private const uint MonitorDefaultToNearest = 2;
        private const uint NoSize = 0x0001;
        private const uint NoZOrder = 0x0004;
        private const uint NoActivate = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        internal static IntPtr FindMonitor(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            return hwnd == IntPtr.Zero
                ? IntPtr.Zero
                : MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        }

        internal static void CenterOnMonitor(Window window, IntPtr desiredMonitor)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            // If the screen was disconnected, fall back to the nearest
            // remaining display rather than positioning the window off-screen.
            var monitor = desiredMonitor != IntPtr.Zero
                ? desiredMonitor
                : MonitorFromWindow(hwnd, MonitorDefaultToNearest);

            var info = new MonitorInfo
            {
                Size = Marshal.SizeOf<MonitorInfo>()
            };

            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            {
                monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                    return;
            }

            if (!GetWindowRect(hwnd, out var bounds))
                return;

            var windowWidth = Math.Max(1, bounds.Right - bounds.Left);
            var windowHeight = Math.Max(1, bounds.Bottom - bounds.Top);
            var workWidth = Math.Max(1, info.Work.Right - info.Work.Left);
            var workHeight = Math.Max(1, info.Work.Bottom - info.Work.Top);

            var left = info.Work.Left + Math.Max(0, (workWidth - windowWidth) / 2);
            var top = info.Work.Top + Math.Max(0, (workHeight - windowHeight) / 2);

            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                left,
                top,
                0,
                0,
                NoSize | NoZOrder | NoActivate);
        }
    }
}
