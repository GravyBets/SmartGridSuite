using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace SmartGridSuite.Client.Services
{
    public static class UiScaleService
    {
        private const double DefaultScale = 0.80;
        private const double MinScale = 0.65;
        private const double MaxScale = 1.10;

        private static readonly string SettingsFolder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartGridSuite");

        private static readonly string SettingsPath =
            Path.Combine(SettingsFolder, "ui-settings.json");

        private static double _currentScale = DefaultScale;

        public static event EventHandler? ScaleChanged;

        public static double CurrentScale
        {
            get => _currentScale;
            private set
            {
                var clean = Math.Clamp(value, MinScale, MaxScale);

                if (Math.Abs(_currentScale - clean) < 0.001)
                    return;

                _currentScale = clean;
                ScaleChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    CurrentScale = DefaultScale;
                    return;
                }

                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<UiScaleSettings>(json);

                CurrentScale = settings?.Scale ?? DefaultScale;
            }
            catch
            {
                CurrentScale = DefaultScale;
            }
        }

        public static void SaveScale(double scale)
        {
            CurrentScale = scale;

            try
            {
                Directory.CreateDirectory(SettingsFolder);

                var settings = new UiScaleSettings
                {
                    Scale = CurrentScale
                };

                var json = JsonSerializer.Serialize(
                    settings,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Do not crash the app over a local preference save failure.
            }
        }

        public static void ApplyToWindow(Window window)
        {
            if (window is null)
                return;

            ApplyScaleToWindowContent(window);

            ScaleChanged -= WindowScaleChanged;
            ScaleChanged += WindowScaleChanged;

            window.Closed += (_, _) =>
            {
                ScaleChanged -= WindowScaleChanged;
            };

            void WindowScaleChanged(object? sender, EventArgs e)
            {
                ApplyScaleToWindowContent(window);
            }
        }

        private static void ApplyScaleToWindowContent(Window window)
        {
            if (window.Content is not FrameworkElement root)
                return;

            window.UseLayoutRounding = true;
            window.SnapsToDevicePixels = true;

            root.UseLayoutRounding = true;
            root.SnapsToDevicePixels = true;

            TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(root, TextRenderingMode.ClearType);
            RenderOptions.SetClearTypeHint(root, ClearTypeHint.Enabled);

            root.LayoutTransform = new ScaleTransform(CurrentScale, CurrentScale);
        }

        private sealed class UiScaleSettings
        {
            public double Scale { get; set; } = DefaultScale;
        }
    }
}