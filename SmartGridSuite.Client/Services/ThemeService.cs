using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace SmartGridSuite.Client.Services
{
    public enum AppTheme
    {
        Light = 0,
        BrightBlue = 1,
        DeepBlue = 2,
        FieldGraphite = 3,
        DeepGraphite = 4,

        // Keeps older references from breaking while we migrate away from Light/Dark.
        Dark = DeepBlue
    }

    public sealed class AppThemeOption
    {
        public AppTheme Theme { get; init; }

        public string DisplayName { get; init; } = "";
    }

    public static class ThemeService
    {
        private const string LightPath = "Themes/LightTheme.xaml";
        private const string BrightBluePath = "Themes/BrightBlueTheme.xaml";
        private const string DeepBluePath = "Themes/DeepBlueTheme.xaml";
        private const string FieldGraphitePath = "Themes/FieldGraphiteTheme.xaml";
        private const string DeepGraphitePath = "Themes/DeepGraphiteTheme.xaml";

        // Old file path, only here so Apply() can remove it if it is already loaded.
        private const string LegacyDarkPath = "Themes/DarkTheme.xaml";

        private static readonly string[] KnownThemePaths =
        {
            LightPath,
            BrightBluePath,
            DeepBluePath,
            FieldGraphitePath,
            DeepGraphitePath,
            LegacyDarkPath
        };

        private static readonly string SettingsFolder =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartGridSuite");

        private static readonly string ThemeSettingsFile =
            Path.Combine(SettingsFolder, "theme.txt");

        public static IReadOnlyList<AppThemeOption> ThemeOptions { get; } =
            new List<AppThemeOption>
            {
                new() { Theme = AppTheme.Light, DisplayName = "Light" },
                new() { Theme = AppTheme.BrightBlue, DisplayName = "Bright Blue" },
                new() { Theme = AppTheme.DeepBlue, DisplayName = "Deep Blue" },
                new() { Theme = AppTheme.FieldGraphite, DisplayName = "Graphite" },
                new() { Theme = AppTheme.DeepGraphite, DisplayName = "Deep Graphite" }
            };

        public static AppTheme Current { get; private set; } = AppTheme.Light;

        public static void ApplySavedTheme()
        {
            Apply(ReadSavedTheme(), saveChoice: false);
        }

        public static void Apply(AppTheme theme)
        {
            Apply(theme, saveChoice: true);
        }

        public static void Toggle()
        {
            Apply(Current == AppTheme.Light
                ? AppTheme.DeepBlue
                : AppTheme.Light);
        }

        private static void Apply(AppTheme theme, bool saveChoice)
        {
            var app = Application.Current;
            if (app == null)
                return;

            var normalizedTheme = NormalizeTheme(theme);
            var nextPath = GetThemePath(normalizedTheme);

            var merged = app.Resources.MergedDictionaries;

            var existing = merged.FirstOrDefault(d =>
                d.Source != null &&
                KnownThemePaths.Any(path =>
                    d.Source.OriginalString.EndsWith(
                        path,
                        StringComparison.OrdinalIgnoreCase)));

            var next = new ResourceDictionary
            {
                Source = new Uri(nextPath, UriKind.Relative)
            };

            if (existing != null)
            {
                var index = merged.IndexOf(existing);
                merged.RemoveAt(index);
                merged.Insert(index, next);
            }
            else
            {
                merged.Add(next);
            }

            Current = normalizedTheme;

            if (saveChoice)
                SaveTheme(normalizedTheme);
        }

        private static AppTheme NormalizeTheme(AppTheme theme)
        {
            return theme switch
            {
                AppTheme.Light => AppTheme.Light,
                AppTheme.BrightBlue => AppTheme.BrightBlue,
                AppTheme.DeepBlue => AppTheme.DeepBlue,
                AppTheme.FieldGraphite => AppTheme.FieldGraphite,
                AppTheme.DeepGraphite => AppTheme.DeepGraphite,
                _ => AppTheme.Light
            };
        }

        private static string GetThemePath(AppTheme theme)
        {
            return theme switch
            {
                AppTheme.Light => LightPath,
                AppTheme.BrightBlue => BrightBluePath,
                AppTheme.DeepBlue => DeepBluePath,
                AppTheme.FieldGraphite => FieldGraphitePath,
                AppTheme.DeepGraphite => DeepGraphitePath,
                _ => LightPath
            };
        }

        private static AppTheme ReadSavedTheme()
        {
            try
            {
                if (!File.Exists(ThemeSettingsFile))
                    return AppTheme.Light;

                var raw = File.ReadAllText(ThemeSettingsFile).Trim();

                if (Enum.TryParse<AppTheme>(raw, ignoreCase: true, out var theme))
                    return NormalizeTheme(theme);
            }
            catch
            {
                // Use default theme if local preference cannot be read.
            }

            return AppTheme.Light;
        }

        private static void SaveTheme(AppTheme theme)
        {
            try
            {
                Directory.CreateDirectory(SettingsFolder);
                File.WriteAllText(ThemeSettingsFile, NormalizeTheme(theme).ToString());
            }
            catch
            {
                // Theme still applies even if local preference cannot be saved.
            }
        }
    }
}