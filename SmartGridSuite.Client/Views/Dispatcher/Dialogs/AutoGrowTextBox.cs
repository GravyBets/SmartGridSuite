using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs;

public static class AutoGrowTextBox
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(AutoGrowTextBox),
            new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        if ((bool)e.NewValue)
        {
            tb.TextChanged += (_, __) => Resize(tb);
            tb.SizeChanged += (_, __) => Resize(tb);
            tb.Loaded += (_, __) => Resize(tb);
        }
    }

    private static void Resize(TextBox tb)
    {
        if (!tb.IsLoaded) return;

        // Measure how tall the text wants to be
        var ft = new FormattedText(
            tb.Text + " ", // keep at least one char
            System.Globalization.CultureInfo.CurrentCulture,
            tb.FlowDirection,
            new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
            tb.FontSize,
            Brushes.Transparent,
            VisualTreeHelper.GetDpi(tb).PixelsPerDip);

        // subtract padding/border from width, clamp to reasonable range
        var innerWidth = Math.Max(0, tb.ActualWidth - tb.Padding.Left - tb.Padding.Right - 8);
        ft.MaxTextWidth = Math.Max(20, innerWidth);

        // + padding + a little breathing room
        var desired = ft.Height + tb.Padding.Top + tb.Padding.Bottom + 12;

        tb.Height = Math.Max(tb.MinHeight, Math.Min(tb.MaxHeight > 0 ? tb.MaxHeight : desired, desired));
    }
}