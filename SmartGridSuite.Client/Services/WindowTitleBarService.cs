using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SmartGridSuite.Client.Services
{
    /// <summary>
    /// Keeps the native Windows caption/title bar aligned with the
    /// active SmartGridSuite theme.
    ///
    /// WPF theme dictionaries do not style the non-client title bar,
    /// so without this bridge dark themes can end up with unreadable
    /// caption text against the Windows-selected title background.
    /// </summary>
    public static class WindowTitleBarService
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

        // Windows 11 color attributes. Unsupported Windows versions simply
        // return a non-zero HRESULT; those failures are intentionally ignored.
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    OnWindowLoaded));

            ThemeService.ThemeChanged +=
                ThemeService_ThemeChanged;
        }

        public static void Apply(Window? window)
        {
            if (window is null ||
                !RuntimeInformation.IsOSPlatform(
                    OSPlatform.Windows))
            {
                return;
            }

            try
            {
                var handle =
                    new WindowInteropHelper(window)
                        .Handle;

                if (handle == IntPtr.Zero)
                    return;

                var darkMode =
                    ThemeService.IsDarkTheme
                        ? 1
                        : 0;

                var darkModeResult =
                    DwmSetWindowAttribute(
                        handle,
                        DwmwaUseImmersiveDarkMode,
                        ref darkMode,
                        sizeof(int));

                if (darkModeResult != 0)
                {
                    DwmSetWindowAttribute(
                        handle,
                        DwmwaUseImmersiveDarkModeBefore20H1,
                        ref darkMode,
                        sizeof(int));
                }

                /*
                 * Windows 11 lets us use the exact SmartGridSuite theme
                 * colors instead of merely asking Windows for dark/light.
                 */
                var captionColor =
                    ResolveThemeColor(
                        "AppBackground",
                        ThemeService.IsDarkTheme
                            ? Color.FromRgb(24, 24, 24)
                            : Colors.White);

                var textColor =
                    ResolveThemeColor(
                        "TextPrimary",
                        ThemeService.IsDarkTheme
                            ? Colors.White
                            : Colors.Black);

                var borderColor =
                    ResolveThemeColor(
                        "CardBorder",
                        captionColor);

                SetColorAttribute(
                    handle,
                    DwmwaCaptionColor,
                    captionColor);

                SetColorAttribute(
                    handle,
                    DwmwaTextColor,
                    textColor);

                SetColorAttribute(
                    handle,
                    DwmwaBorderColor,
                    borderColor);
            }
            catch
            {
                /*
                 * Title-bar polish must never prevent an app window from
                 * opening. Unsupported DWM attributes are safe to ignore.
                 */
            }
        }

        private static void OnWindowLoaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Window window ||
                !ReferenceEquals(
                    e.OriginalSource,
                    window))
            {
                return;
            }

            Apply(window);
        }

        private static void ThemeService_ThemeChanged(
            object? sender,
            EventArgs e)
        {
            var application =
                Application.Current;

            if (application is null)
                return;

            void RefreshWindows()
            {
                foreach (Window window
                         in application.Windows)
                {
                    if (window.IsLoaded)
                        Apply(window);
                }
            }

            if (application.Dispatcher.CheckAccess())
            {
                RefreshWindows();
            }
            else
            {
                application.Dispatcher.BeginInvoke(
                    new Action(
                        RefreshWindows));
            }
        }

        private static Color ResolveThemeColor(
            string resourceKey,
            Color fallback)
        {
            try
            {
                if (Application.Current
                        ?.TryFindResource(resourceKey)
                    is SolidColorBrush brush)
                {
                    return brush.Color;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static void SetColorAttribute(
            IntPtr handle,
            int attribute,
            Color color)
        {
            var colorRef =
                color.R |
                (color.G << 8) |
                (color.B << 16);

            DwmSetWindowAttribute(
                handle,
                attribute,
                ref colorRef,
                sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);
    }
}
