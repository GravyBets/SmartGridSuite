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
                 * Resolve the active SmartGridSuite colors once, then apply
                 * them to BOTH title-bar layers:
                 *
                 *   1. Native Windows/DWM chrome used by ordinary Window.
                 *   2. Fluent.Ribbon's custom RibbonWindow title bar.
                 *
                 * RibbonWindow does not automatically inherit DWM caption
                 * foreground colors, which is why its title could remain
                 * nearly black on Cobalt/Graphite/etc.
                 */
                var captionBrush =
                    ResolveThemeBrush(
                        "AppBackground",
                        ThemeService.IsDarkTheme
                            ? new SolidColorBrush(
                                Color.FromRgb(24, 24, 24))
                            : Brushes.White);

                var textBrush =
                    ResolveThemeBrush(
                        "TextPrimary",
                        ThemeService.IsDarkTheme
                            ? Brushes.White
                            : Brushes.Black);

                var borderBrush =
                    ResolveThemeBrush(
                        "CardBorder",
                        captionBrush);

                var hoverBrush =
                    ResolveThemeBrush(
                        "HoverOverlay",
                        ThemeService.IsDarkTheme
                            ? new SolidColorBrush(
                                Color.FromArgb(24, 255, 255, 255))
                            : new SolidColorBrush(
                                Color.FromArgb(20, 0, 0, 0)));

                var pressedBrush =
                    ResolveThemeBrush(
                        "PressedOverlay",
                        ThemeService.IsDarkTheme
                            ? new SolidColorBrush(
                                Color.FromArgb(40, 255, 255, 255))
                            : new SolidColorBrush(
                                Color.FromArgb(34, 0, 0, 0)));

                SetColorAttribute(
                    handle,
                    DwmwaCaptionColor,
                    captionBrush.Color);

                SetColorAttribute(
                    handle,
                    DwmwaTextColor,
                    textBrush.Color);

                SetColorAttribute(
                    handle,
                    DwmwaBorderColor,
                    borderBrush.Color);

                ApplyFluentRibbonTitleBar(
                    window,
                    captionBrush,
                    textBrush,
                    hoverBrush,
                    pressedBrush);
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

        private static SolidColorBrush ResolveThemeBrush(
            string resourceKey,
            SolidColorBrush fallback)
        {
            try
            {
                if (Application.Current
                        ?.TryFindResource(resourceKey)
                    is SolidColorBrush brush)
                {
                    return brush;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static void ApplyFluentRibbonTitleBar(
            Window window,
            SolidColorBrush captionBrush,
            SolidColorBrush textBrush,
            SolidColorBrush hoverBrush,
            SolidColorBrush pressedBrush)
        {
            /*
             * Avoid coupling this service to Fluent.Ribbon's CLR types.
             * RibbonWindow exposes TitleBackground and TitleForeground, so
             * reflection lets the same global service work for both normal
             * WPF Window instances and Fluent RibbonWindow instances.
             */
            var windowType =
                window.GetType();

            var titleBackgroundProperty =
                windowType.GetProperty(
                    "TitleBackground");

            if (titleBackgroundProperty?.CanWrite == true &&
                typeof(Brush).IsAssignableFrom(
                    titleBackgroundProperty.PropertyType))
            {
                titleBackgroundProperty.SetValue(
                    window,
                    captionBrush);
            }

            var titleForegroundProperty =
                windowType.GetProperty(
                    "TitleForeground");

            if (titleForegroundProperty?.CanWrite == true &&
                typeof(Brush).IsAssignableFrom(
                    titleForegroundProperty.PropertyType))
            {
                titleForegroundProperty.SetValue(
                    window,
                    textBrush);
            }

            /*
             * Fluent.Ribbon 11 uses these resources for the custom title bar
             * and its minimize/maximize/close controls. Window-local values
             * deliberately override Fluent's default theme resources.
             */
            window.Resources[
                "Fluent.Ribbon.Brushes.RibbonWindow.TitleBackground"] =
                captionBrush;

            window.Resources[
                "Fluent.Ribbon.Brushes.WindowCommands.CaptionButton.Foreground"] =
                textBrush;

            window.Resources[
                "Fluent.Ribbon.Brushes.WindowCommands.CaptionButton.Background"] =
                Brushes.Transparent;

            window.Resources[
                "Fluent.Ribbon.Brushes.WindowCommands.CaptionButton.MouseOver.Background"] =
                hoverBrush;

            window.Resources[
                "Fluent.Ribbon.Brushes.WindowCommands.CaptionButton.Pressed.Background"] =
                pressedBrush;

            /*
             * Keep the close button readable as well. Its hover treatment is
             * intentionally a conventional Windows red; normal state remains
             * transparent with the same theme-aware caption glyph color.
             */
            window.Resources[
                "Fluent.Ribbon.Brushes.WindowCommands.CloseButton.MouseOver.Background"] =
                new SolidColorBrush(
                    Color.FromRgb(196, 43, 28));

            window.Resources[
                "Fluent.Ribbon.Brushes.WindowCommands.CloseButton.Pressed.Background"] =
                new SolidColorBrush(
                    Color.FromRgb(153, 33, 22));

            /*
             * Some Fluent.Ribbon templates bind the title foreground back to
             * RibbonWindow.Foreground. Setting it only on RibbonWindow types
             * gives those templates the same theme-aware value without
             * changing ordinary WPF windows.
             */
            if (titleForegroundProperty is not null)
            {
                window.Foreground =
                    textBrush;
            }
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
