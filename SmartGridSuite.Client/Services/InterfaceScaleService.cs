using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace SmartGridSuite.Client.Services
{
    public enum InterfaceScaleMode
    {
        Auto,
        Percent100,
        Percent110,
        Percent125,
        Percent150
    }

    public static class InterfaceScaleService
    {
        private sealed class LocalDisplaySettings
        {
            public InterfaceScaleMode InterfaceScaleMode { get; set; }
                = InterfaceScaleMode.Percent100;
        }

        private sealed class WindowScaleState
        {
            public FrameworkElement? RootElement { get; set; }

            public ScaleTransform ScaleTransform { get; }
                = new ScaleTransform(1.0, 1.0);
        }

        private static readonly ConditionalWeakTable<Window, WindowScaleState>
            WindowStates = new();

        private static readonly string SettingsDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "SmartGridSuite");

        private static readonly string SettingsPath =
            Path.Combine(
                SettingsDirectory,
                "display-settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        private static bool _initialized;

        public static InterfaceScaleMode CurrentMode { get; private set; }
            = InterfaceScaleMode.Percent100;

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(InterfaceScaleService),
                new FrameworkPropertyMetadata(true));

        public static void SetIsEnabled(DependencyObject element, bool value)
        {
            element.SetValue(
                IsEnabledProperty,
                value);
        }

        public static bool GetIsEnabled(DependencyObject element)
        {
            return (bool)element.GetValue(
                IsEnabledProperty);
        }

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            LoadSettings();

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(
                    OnWindowLoaded));

            EventManager.RegisterClassHandler(
                typeof(Window),
                Window.DpiChangedEvent,
                new DpiChangedEventHandler(
                    OnWindowDpiChanged));
        }

        public static void SetMode(InterfaceScaleMode mode)
        {
            Initialize();

            if (CurrentMode == mode)
            {
                return;
            }

            CurrentMode = mode;

            SaveSettings();
            RefreshOpenWindows();
        }

        public static double GetInterfaceScale(DpiScale dpi)
        {
            Initialize();

            return CurrentMode switch
            {
                InterfaceScaleMode.Percent100 => 1.00,
                InterfaceScaleMode.Percent110 => 1.10,
                InterfaceScaleMode.Percent125 => 1.25,
                InterfaceScaleMode.Percent150 => 1.50,

                _ => GetAutomaticScale(
                    dpi.DpiScaleX)
            };
        }

        private static double GetAutomaticScale(double windowsScale)
        {
            // External monitor configured at 100%.
            if (windowsScale < 1.15)
            {
                return 1.25;
            }

            // Monitor configured at approximately 125%.
            if (windowsScale < 1.40)
            {
                return 1.10;
            }

            // Laptop at 150% or above already looks correct.
            return 1.00;
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window)
            {
                return;
            }

            // Prevent handling a child control's Loaded event
            // as though the entire Window just loaded.
            if (!ReferenceEquals(
                    e.OriginalSource,
                    window))
            {
                return;
            }

            ApplyScale(
                window,
                VisualTreeHelper.GetDpi(window));
        }

        private static void OnWindowDpiChanged(object sender, DpiChangedEventArgs e)
        {
            if (sender is not Window window)
            {
                return;
            }

            ApplyScale(
                window,
                e.NewDpi);
        }

        private static void ApplyScale(Window window, DpiScale dpi)
        {
            WindowScaleState state =
                WindowStates.GetValue(
                    window,
                    static _ => new WindowScaleState());

            if (!GetIsEnabled(window))
            {
                state.ScaleTransform.ScaleX = 1.0;
                state.ScaleTransform.ScaleY = 1.0;

                return;
            }

            if (window.Content is not FrameworkElement root)
            {
                return;
            }

            if (!ReferenceEquals(
                    state.RootElement,
                    root))
            {
                AttachScaleTransform(
                    root,
                    state);

                state.RootElement = root;
            }

            double scale =
                GetInterfaceScale(dpi);

            state.ScaleTransform.ScaleX = scale;
            state.ScaleTransform.ScaleY = scale;

            root.UseLayoutRounding = true;
            root.SnapsToDevicePixels = true;

            root.InvalidateMeasure();
            root.InvalidateArrange();
        }

        private static void AttachScaleTransform(FrameworkElement root, WindowScaleState state)
        {
            Transform? existingTransform =
                root.LayoutTransform;

            if (existingTransform is null ||
                existingTransform.Value.IsIdentity)
            {
                root.LayoutTransform =
                    state.ScaleTransform;

                return;
            }

            var transformGroup =
                new TransformGroup();

            transformGroup.Children.Add(
                existingTransform.CloneCurrentValue());

            transformGroup.Children.Add(
                state.ScaleTransform);

            root.LayoutTransform =
                transformGroup;
        }

        private static void RefreshOpenWindows()
        {
            Application? application =
                Application.Current;

            if (application is null)
            {
                return;
            }

            void Refresh()
            {
                foreach (Window window
                         in application.Windows)
                {
                    if (!window.IsLoaded)
                    {
                        continue;
                    }

                    ApplyScale(
                        window,
                        VisualTreeHelper.GetDpi(window));
                }
            }

            if (application.Dispatcher.CheckAccess())
            {
                Refresh();
            }
            else
            {
                application.Dispatcher.BeginInvoke(
                    new Action(Refresh));
            }
        }

        private static void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    CurrentMode =
                        InterfaceScaleMode.Percent100;

                    return;
                }

                string json =
                    File.ReadAllText(SettingsPath);

                LocalDisplaySettings? settings =
                    JsonSerializer.Deserialize<LocalDisplaySettings>(
                        json,
                        JsonOptions);

                CurrentMode =
                    settings?.InterfaceScaleMode
                    ?? InterfaceScaleMode.Percent100;
            }
            catch
            {
                // A corrupt local display setting must never
                // prevent SmartGridSuite from opening.
                CurrentMode =
                    InterfaceScaleMode.Percent100;
            }
        }

        private static void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(
                    SettingsDirectory);

                var settings =
                    new LocalDisplaySettings
                    {
                        InterfaceScaleMode =
                            CurrentMode
                    };

                string json =
                    JsonSerializer.Serialize(
                        settings,
                        JsonOptions);

                string temporaryPath =
                    SettingsPath + ".tmp";

                File.WriteAllText(
                    temporaryPath,
                    json);

                File.Move(
                    temporaryPath,
                    SettingsPath,
                    overwrite: true);
            }
            catch
            {
                // Display-setting persistence must never crash
                // the application.
            }
        }
    }
}