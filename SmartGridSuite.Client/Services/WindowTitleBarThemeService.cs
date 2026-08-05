using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SmartGridSuite.Client.Services
{
    public static class WindowTitleBarThemeService
    {
        /*
         * Windows 10 versions before and after the 20H1 update use
         * different attribute numbers for immersive dark title bars.
         */
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;

        /*
         * Windows 11 title-bar color attributes.
         * Unsupported Windows versions simply return a failed HRESULT,
         * which is safe to ignore.
         */
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized)
                return;

            _initialized = true;

            /*
             * Loaded is raised after the native window handle exists.
             * Registering against Window also covers RibbonWindow subclasses.
             */
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(Window_Loaded),
                handledEventsToo: true);

            ThemeService.ThemeChanged +=
                ThemeService_ThemeChanged;

            ApplyToOpenWindows();
        }

        private static void Window_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Window window)
                Apply(window);
        }

        private static void ThemeService_ThemeChanged(
            object? sender,
            EventArgs e)
        {
            var dispatcher =
                Application.Current?.Dispatcher;

            if (dispatcher == null)
                return;

            if (dispatcher.CheckAccess())
            {
                ApplyToOpenWindows();
                return;
            }

            dispatcher.BeginInvoke(
                new Action(ApplyToOpenWindows));
        }

        private static void ApplyToOpenWindows()
        {
            var application =
                Application.Current;

            if (application == null)
                return;

            foreach (Window window in application.Windows)
                Apply(window);
        }

        public static void Apply(
            Window window)
        {
            var handle =
                new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero)
                return;

            var useDarkMode =
                ThemeService.IsDarkTheme
                    ? 1
                    : 0;

            /*
             * Try the current Windows attribute first, then the older
             * Windows 10 attribute as a compatibility fallback.
             */
            var result =
                DwmSetWindowAttribute(
                    handle,
                    DwmwaUseImmersiveDarkMode,
                    ref useDarkMode,
                    sizeof(int));

            if (result != 0)
            {
                DwmSetWindowAttribute(
                    handle,
                    DwmwaUseImmersiveDarkModeBefore20H1,
                    ref useDarkMode,
                    sizeof(int));
            }

            /*
             * Match the native caption background to the current
             * Fluent ribbon background.
             */
            var captionColor =
                GetColorReference(
                    "Fluent.Ribbon.Brushes.RibbonBackgroundBrush",
                    ThemeService.IsDarkTheme
                        ? Color.FromRgb(18, 22, 28)
                        : Color.FromRgb(243, 245, 248));

            DwmSetWindowAttribute(
                handle,
                DwmwaCaptionColor,
                ref captionColor,
                sizeof(int));

            /*
             * The caption text color also controls the visual treatment
             * used by Windows for the caption-button glyphs.
             */
            var textColor =
                GetColorReference(
                    "TextPrimary",
                    ThemeService.IsDarkTheme
                        ? Colors.White
                        : Colors.Black);

            DwmSetWindowAttribute(
                handle,
                DwmwaTextColor,
                ref textColor,
                sizeof(int));

            /*
             * Force Windows to redraw the non-client title-bar area.
             */
            SetWindowPos(
                handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove |
                SwpNoSize |
                SwpNoZOrder |
                SwpNoActivate |
                SwpFrameChanged);
        }

        private static int GetColorReference(
            string resourceKey,
            Color fallbackColor)
        {
            var color =
                Application.Current?.TryFindResource(resourceKey)
                    is SolidColorBrush brush
                    ? brush.Color
                    : fallbackColor;

            /*
             * Windows COLORREF uses BGR byte ordering.
             */
            return color.R |
                   color.G << 8 |
                   color.B << 16;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}