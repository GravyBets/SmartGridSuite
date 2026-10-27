using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.EasterEggs
{
    /// <summary>
    /// Secret launcher for self-contained arcade games. Owned by the active
    /// SmartGridSuite module and closes if that module closes or hides.
    /// </summary>
    public sealed class ArcadeMenuWindow : Window
    {
        private static ArcadeMenuWindow? _active;
        private readonly Window _host;

        private static readonly Brush Back = ColorBrush("#081326");
        private static readonly Brush Surface = ColorBrush("#142945");
        private static readonly Brush Edge = ColorBrush("#355979");
        private static readonly Brush Cyan = ColorBrush("#63E5FF");
        private static readonly Brush Gold = ColorBrush("#FFE089");
        private static readonly Brush White = ColorBrush("#F2F7FF");
        private static readonly Brush Muted = ColorBrush("#A6BAD4");

        private ArcadeMenuWindow(Window host)
        {
            _host = host;
            Owner = host;
            Title = "SmartGridSuite Arcade";
            // Keep the launcher comfortable at laptop resolutions, while
            // allowing resizing if Windows text scaling makes controls taller.
            Width = Math.Min(840, Math.Max(760, SystemParameters.WorkArea.Width - 48));
            Height = Math.Min(620, Math.Max(550, SystemParameters.WorkArea.Height - 48));
            MinWidth = 760;
            MinHeight = 550;
            ResizeMode = ResizeMode.CanResize;
            UseLayoutRounding = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Back;
            FontFamily = new FontFamily("Consolas");

            var layout = new Grid { Margin = new Thickness(26, 22, 26, 22) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 17) };
            heading.Children.Add(Label("SMARTGRID SUITE PRESENTS", 12, Cyan));
            heading.Children.Add(Label("RADIO SHOP ARCADE", 31, Gold, true));
            heading.Children.Add(Label("Two ways to avoid AMS/IG Tickets", 13, Muted));
            Grid.SetRow(heading, 0);
            layout.Children.Add(heading);

            var gameGrid = new Grid();
            Grid.SetIsSharedSizeScope(gameGrid, true);
            gameGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            gameGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            var bucket = GameCard(
                "01   BUCKET TRUCK",
                "RADIO SHOP ARCADE",
                "Drive a bucket truck, fix radio outages, earn cash, and spend it on bigger trucks.",
                "WASD / ARROWS • SPACE",
                Cyan,
                "PLAY BUCKET TRUCK",
                LaunchBucketTruck);
            bucket.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(bucket, 0);
            gameGrid.Children.Add(bucket);

            var defense = GameCard(
                "02   GRID UNDER ATTACK",
                "TOWER DEFENSE",
                "Defend the tower through escalating waves. Upgrade defenses and permanently unlock speed, wave skips and special weapons.",
                "SPACE prepare/waves • P pause • H help\nL shop scores • W research • ESC back\nTAB categories • 1–8 standard buys\nSpecial: 1–6 buys • S choose wave skip",
                Gold,
                "PLAY TOWER DEFENSE",
                LaunchTowerDefense);
            defense.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(defense, 1);
            gameGrid.Children.Add(defense);

            Grid.SetRow(gameGrid, 1);
            layout.Children.Add(gameGrid);

            var footer = new StackPanel { Margin = new Thickness(0, 17, 0, 0) };
            footer.Children.Add(Label(
                "ESC TO CLOSE    •    FICTIONAL RADIOS AND TOWERS ONLY    •    NO WORK ORDERS WERE HARMED",
                11, Muted));
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);

            Content = layout;
            PreviewKeyDown += KeyPressed;
            _host.Closed += HostClosed;
            _host.IsVisibleChanged += HostVisibilityChanged;
            _host.StateChanged += HostStateChanged;
            Closed += WindowClosed;
        }

        public static void Toggle(Window host)
        {
            if (_active != null)
            {
                _active.Close();
                return;
            }

            if (!host.IsVisible || host.WindowState == WindowState.Minimized)
                return;

            try
            {
                var menu = new ArcadeMenuWindow(host);
                _active = menu;
                menu.Show();
                menu.Activate();
            }
            catch (Exception ex)
            {
                _active = null;
                Debug.WriteLine("Arcade menu failed to open: " + ex);
            }
        }

        private static TextBlock Label(
            string text, double size, Brush color, bool bold = false,
            double bottomMargin = 9) =>
            new()
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = color,
                Margin = new Thickness(0, 0, 0, bottomMargin)
            };

        private static Border GameCard(
            string id, string title, string description, string controls,
            Brush highlight, string buttonText, RoutedEventHandler launch)
        {
            // Shared row heights line up the two cards, and the star spacer
            // leaves room for the controls and both Play buttons at the same
            // baseline even when one game has a longer description.
            var content = new Grid
            {
                Margin = new Thickness(20, 22, 20, 21)
            };
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto, SharedSizeGroup = "GameId"
            });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto, SharedSizeGroup = "GameTitle"
            });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto, SharedSizeGroup = "GameDescription"
            });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto, SharedSizeGroup = "GameControlsHeading"
            });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto, SharedSizeGroup = "GameControls"
            });
            content.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto, SharedSizeGroup = "GamePlay"
            });

            void AddRow(UIElement element, int row)
            {
                Grid.SetRow(element, row);
                content.Children.Add(element);
            }

            AddRow(Label(id, 13, highlight, true, 7), 0);
            AddRow(Label(title, 21, White, true, 7), 1);
            AddRow(Label(description, 15, Muted, bottomMargin: 9), 2);
            AddRow(Label("CONTROLS", 11, highlight, true, 7), 4);
            AddRow(Label(controls, 12, White, bottomMargin: 0), 5);

            var play = new Button
            {
                Content = buttonText,
                Cursor = Cursors.Hand,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Height = 47,
                Margin = new Thickness(0, 18, 0, 0),
                Background = highlight,
                Foreground = Back,
                BorderThickness = new Thickness(0)
            };
            play.Click += launch;
            AddRow(play, 6);

            return new Border
            {
                Background = Surface,
                BorderBrush = Edge,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(9),
                Child = content
            };
        }

        private void LaunchBucketTruck(object sender, RoutedEventArgs args)
        {
            if (RadioShopArcadeWindow.IsRunning || TowerDefenseWindow.IsRunning)
                return;

            RadioShopArcadeWindow.Toggle(this);
        }

        private void LaunchTowerDefense(object sender, RoutedEventArgs args)
        {
            if (RadioShopArcadeWindow.IsRunning || TowerDefenseWindow.IsRunning)
                return;

            TowerDefenseWindow.Toggle(this);
        }

        private void KeyPressed(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;

            e.Handled = true;
            Close();
        }

        private void HostClosed(object? sender, EventArgs e) => Close();

        private void HostVisibilityChanged(
            object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!_host.IsVisible)
                Close();
        }

        private void HostStateChanged(object? sender, EventArgs e)
        {
            if (_host.WindowState == WindowState.Minimized)
                Close();
        }

        private void WindowClosed(object? sender, EventArgs e)
        {
            PreviewKeyDown -= KeyPressed;
            _host.Closed -= HostClosed;
            _host.IsVisibleChanged -= HostVisibilityChanged;
            _host.StateChanged -= HostStateChanged;
            Closed -= WindowClosed;
            if (ReferenceEquals(_active, this))
                _active = null;
        }

        private static Brush ColorBrush(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }
    }
}
