using System;
using System.Linq;
using System.Windows;

namespace SmartGridSuite.Client.Services
{
    public enum AppTheme
    {
        Light,
        Dark
    }

    public static class ThemeService
    {
        private const string LightPath = "Themes/LightTheme.xaml";
        private const string DarkPath = "Themes/DarkTheme.xaml";

        public static AppTheme Current { get; private set; } = AppTheme.Light;

        public static void Apply(AppTheme theme)
        {
            var app = Application.Current;
            if (app == null) return;

            var merged = app.Resources.MergedDictionaries;

            // Find the existing theme dictionary (Light or Dark)
            var existing = merged.FirstOrDefault(d =>
                d.Source != null &&
                (d.Source.OriginalString.EndsWith(LightPath, StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.EndsWith(DarkPath, StringComparison.OrdinalIgnoreCase)));

            var next = new ResourceDictionary
            {
                Source = new Uri(theme == AppTheme.Dark ? DarkPath : LightPath, UriKind.Relative)
            };

            if (existing != null)
            {
                var idx = merged.IndexOf(existing);
                merged.RemoveAt(idx);
                merged.Insert(idx, next);
            }
            else
            {
                merged.Add(next);
            }

            Current = theme;
        }

        public static void Toggle() =>
            Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);
    }
}