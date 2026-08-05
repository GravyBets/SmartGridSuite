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
        Sandstone = 2,
        Graphite = 3,
        Cobalt = 4,
        Evergreen = 5,
        Ember = 6,
        Kuipers = 7,

        /*
         * Compatibility aliases for older code and saved preferences.
         */
        [Obsolete("Use Graphite instead.")]
        Slate = Graphite,

        [Obsolete("Use Cobalt instead.")]
        Midnight = Cobalt,

        [Obsolete("Use Cobalt instead.")]
        DeepBlue = Cobalt,

        [Obsolete("Use Graphite instead.")]
        FieldGraphite = Graphite,

        [Obsolete("Use Graphite instead.")]
        DeepGraphite = Graphite,

        [Obsolete("Use Cobalt instead.")]
        Dark = Cobalt,

        [Obsolete("Use Kuipers instead.")]
        NeonFlux = Kuipers
    }

    public sealed class AppThemeOption
    {
        public AppTheme Theme { get; init; }

        public string DisplayName { get; init; } = "";
    }

    public static class ThemeService
    {
        /*
         * Current theme dictionaries.
         */
        private const string LightPath =
            "Themes/LightTheme.xaml";

        private const string BrightBluePath =
            "Themes/BrightBlueTheme.xaml";

        private const string SandstonePath =
            "Themes/SandstoneTheme.xaml";

        private const string GraphitePath =
            "Themes/GraphiteTheme.xaml";

        private const string CobaltPath =
            "Themes/CobaltTheme.xaml";

        private const string EvergreenPath =
            "Themes/EvergreenTheme.xaml";

        private const string EmberPath =
            "Themes/EmberTheme.xaml";

        private const string KuipersPath =
            "Themes/KuipersTheme.xaml";

        /*
         * Retired theme paths.
         *
         * These files may be deleted. The strings remain so an old dictionary
         * can still be recognized and removed during an in-place update.
         */
        private const string LegacyDarkPath =
            "Themes/DarkTheme.xaml";

        private const string LegacyDeepBluePath =
            "Themes/DeepBlueTheme.xaml";

        private const string LegacyFieldGraphitePath =
            "Themes/FieldGraphiteTheme.xaml";

        private const string LegacyDeepGraphitePath =
            "Themes/DeepGraphiteTheme.xaml";

        private const string LegacySlatePath =
            "Themes/SlateTheme.xaml";

        private const string LegacyMidnightPath =
            "Themes/MidnightTheme.xaml";

        private const string LegacyNeonFluxPath =
            "Themes/NeonFluxTheme.xaml";

        private static readonly Uri CompanyLogoBlackTextUri =
            new(
                "pack://application:,,,/Assets/Branding/" +
                "CenterPoint%20Logo%20Color%20Transparent%20bkgrnd.png",
                UriKind.Absolute);

        private static readonly Uri CompanyLogoWhiteTextUri =
            new(
                "pack://application:,,,/Assets/Branding/" +
                "CenterPoint%20Logo%20Color%20White%20text%20Transparent%20bkgrnd.png",
                UriKind.Absolute);

        private static readonly string[] KnownThemePaths =
        {
            // Current themes
            LightPath,
            BrightBluePath,
            SandstonePath,
            GraphitePath,
            CobaltPath,
            EvergreenPath,
            EmberPath,
            KuipersPath,

            // Retired themes
            LegacyDarkPath,
            LegacyDeepBluePath,
            LegacyFieldGraphitePath,
            LegacyDeepGraphitePath,
            LegacySlatePath,
            LegacyMidnightPath,
            LegacyNeonFluxPath


        };

        private static readonly string SettingsFolder =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "SmartGridSuite");

        private static readonly string ThemeSettingsFile =
            Path.Combine(
                SettingsFolder,
                "theme.txt");

        public static IReadOnlyList<AppThemeOption> ThemeOptions { get; } =
            new List<AppThemeOption>
            {
                new()
                {
                    Theme = AppTheme.Light,
                    DisplayName = "Light of 1000 Suns"
                },
                new()
                {
                    Theme = AppTheme.BrightBlue,
                    DisplayName = "Old Pingo Vibes"
                },
                new()
                {
                    Theme = AppTheme.Sandstone,
                    DisplayName = "Sandstone"
                },
                new()
                {
                    Theme = AppTheme.Graphite,
                    DisplayName = "Graphite"
                },
                new()
                {
                    Theme = AppTheme.Cobalt,
                    DisplayName = "Cobalt Blue"
                },
                new()
                {
                    Theme = AppTheme.Evergreen,
                    DisplayName = "Broccoli"
                },
                new()
                {
                    Theme = AppTheme.Ember,
                    DisplayName = "Mesa Biome"
                },
                new()
                {
                    Theme = AppTheme.Kuipers,
                    DisplayName = "'David Kuipers'"
                }
            };

        public static event EventHandler? ThemeChanged;

        public static AppTheme Current { get; private set; } =
            AppTheme.Light;

        public static bool IsDarkTheme => NormalizeTheme(Current) 
            switch
            {
                AppTheme.Graphite => true,
                AppTheme.Cobalt => true,
                AppTheme.Evergreen => true,
                AppTheme.Ember => true,
                AppTheme.Kuipers => true,
                _ => false
            };

        public static Uri CurrentCompanyLogoUri =>
            NormalizeTheme(Current) switch
            {
                AppTheme.Light =>
                    CompanyLogoBlackTextUri,

                AppTheme.BrightBlue =>
                    CompanyLogoBlackTextUri,

                AppTheme.Sandstone =>
                    CompanyLogoBlackTextUri,

                AppTheme.Graphite =>
                    CompanyLogoWhiteTextUri,

                AppTheme.Cobalt =>
                    CompanyLogoWhiteTextUri,

                AppTheme.Evergreen =>
                    CompanyLogoWhiteTextUri,

                AppTheme.Ember =>
                    CompanyLogoWhiteTextUri,

                AppTheme.Kuipers =>
                    CompanyLogoWhiteTextUri,

                _ =>
                    CompanyLogoBlackTextUri
            };

        public static void ApplySavedTheme()
        {
            Apply(
                ReadSavedTheme(),
                saveChoice: false);
        }

        public static void Apply(AppTheme theme)
        {
            Apply(
                theme,
                saveChoice: true);
        }

        /*
         * Retains the old two-theme toggle behavior.
         *
         * Light switches to Graphite.
         * Any other theme switches back to Light.
         */
        public static void Toggle()
        {
            Apply(
                Current == AppTheme.Light
                    ? AppTheme.Graphite
                    : AppTheme.Light);
        }

        private static void Apply(AppTheme theme, bool saveChoice)
        {
            var app =
                Application.Current;

            if (app == null)
                return;

            var normalizedTheme =
                NormalizeTheme(theme);

            var nextPath =
                GetThemePath(normalizedTheme);

            var merged =
                app.Resources.MergedDictionaries;

            var existingThemeDictionaries =
                merged
                    .Where(IsKnownThemeDictionary)
                    .ToList();

            var nextDictionary =
                new ResourceDictionary
                {
                    Source = new Uri(
                        nextPath,
                        UriKind.Relative)
                };

            /*
             * Preserve the position of the first theme dictionary relative
             * to the application's other shared resource dictionaries.
             */
            if (existingThemeDictionaries.Count > 0)
            {
                var insertionIndex =
                    merged.IndexOf(existingThemeDictionaries[0]);

                foreach (var dictionary in existingThemeDictionaries)
                    merged.Remove(dictionary);

                if (insertionIndex < 0 ||
                    insertionIndex > merged.Count)
                {
                    insertionIndex = merged.Count;
                }

                merged.Insert(
                    insertionIndex,
                    nextDictionary);
            }
            else
            {
                merged.Add(nextDictionary);
            }

            var themeActuallyChanged =
                Current != normalizedTheme;

            Current =
                normalizedTheme;

            if (saveChoice)
                SaveTheme(normalizedTheme);

            if (themeActuallyChanged)
            {
                ThemeChanged?.Invoke(
                    null,
                    EventArgs.Empty);
            }
        }

        private static bool IsKnownThemeDictionary(
            ResourceDictionary dictionary)
        {
            if (dictionary.Source == null)
                return false;

            var source =
                dictionary.Source.OriginalString;

            return KnownThemePaths.Any(path =>
                source.EndsWith(
                    path,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static AppTheme NormalizeTheme(
            AppTheme theme)
        {
            /*
             * Compatibility aliases have the same numeric values as their
             * replacements, so they resolve through these canonical cases.
             */
            return theme switch
            {
                AppTheme.Light =>
                    AppTheme.Light,

                AppTheme.BrightBlue =>
                    AppTheme.BrightBlue,

                AppTheme.Sandstone =>
                    AppTheme.Sandstone,

                AppTheme.Graphite =>
                    AppTheme.Graphite,

                AppTheme.Cobalt =>
                    AppTheme.Cobalt,

                AppTheme.Evergreen =>
                    AppTheme.Evergreen,

                AppTheme.Ember =>
                    AppTheme.Ember,

                AppTheme.Kuipers =>
                    AppTheme.Kuipers,

                _ =>
                    AppTheme.Light
            };
        }

        private static string GetThemePath(
            AppTheme theme)
        {
            return NormalizeTheme(theme) switch
            {
                AppTheme.Light =>
                    LightPath,

                AppTheme.BrightBlue =>
                    BrightBluePath,

                AppTheme.Sandstone =>
                    SandstonePath,

                AppTheme.Graphite =>
                    GraphitePath,

                AppTheme.Cobalt =>
                    CobaltPath,

                AppTheme.Evergreen =>
                    EvergreenPath,

                AppTheme.Ember =>
                    EmberPath,

                AppTheme.Kuipers =>
                    KuipersPath,

                _ =>
                    LightPath
            };
        }

        private static AppTheme ReadSavedTheme()
        {
            try
            {
                if (!File.Exists(ThemeSettingsFile))
                    return AppTheme.Light;

                var raw =
                    File.ReadAllText(ThemeSettingsFile)
                        .Trim();

                if (string.IsNullOrWhiteSpace(raw))
                    return AppTheme.Light;

                /*
                 * Explicitly migrate names saved by retired versions.
                 */
                var migratedTheme =
                    raw.ToUpperInvariant() switch
                    {
                        "DARK" =>
                            AppTheme.Cobalt,

                        "DEEPBLUE" =>
                            AppTheme.Cobalt,

                        "MIDNIGHT" =>
                            AppTheme.Cobalt,

                        "FIELDGRAPHITE" =>
                            AppTheme.Graphite,

                        "DEEPGRAPHITE" =>
                            AppTheme.Graphite,

                        "SLATE" =>
                            AppTheme.Graphite,

                        "NEONFLUX" =>
                            AppTheme.Kuipers,

                        _ =>
                            (AppTheme?)null
                    };

                if (migratedTheme.HasValue)
                    return migratedTheme.Value;

                if (Enum.TryParse<AppTheme>(
                        raw,
                        ignoreCase: true,
                        out var parsedTheme))
                {
                    return NormalizeTheme(parsedTheme);
                }
            }
            catch
            {
                /*
                 * A damaged or inaccessible local preference must never
                 * prevent Smart Grid Suite from starting.
                 */
            }

            return AppTheme.Light;
        }

        private static void SaveTheme(
            AppTheme theme)
        {
            try
            {
                Directory.CreateDirectory(
                    SettingsFolder);

                File.WriteAllText(
                    ThemeSettingsFile,
                    GetThemeStorageName(theme));
            }
            catch
            {
                /*
                 * The theme remains active for the current session even when
                 * the local preference cannot be written.
                 */
            }
        }

        private static string GetThemeStorageName(
            AppTheme theme)
        {
            /*
             * Avoid Enum.ToString() because compatibility aliases share
             * numeric values with canonical themes.
             */
            return NormalizeTheme(theme) switch
            {
                AppTheme.Light =>
                    nameof(AppTheme.Light),

                AppTheme.BrightBlue =>
                    nameof(AppTheme.BrightBlue),

                AppTheme.Sandstone =>
                    nameof(AppTheme.Sandstone),

                AppTheme.Graphite =>
                    nameof(AppTheme.Graphite),

                AppTheme.Cobalt =>
                    nameof(AppTheme.Cobalt),

                AppTheme.Evergreen =>
                    nameof(AppTheme.Evergreen),

                AppTheme.Ember =>
                    nameof(AppTheme.Ember),

                AppTheme.Kuipers =>
                    nameof(AppTheme.Kuipers),

                _ =>
                    nameof(AppTheme.Light)
            };
        }
    }
}