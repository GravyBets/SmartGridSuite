using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SmartGridSuite.Client.Views.EasterEggs
{
    /// <summary>
    /// Purely visual Easter egg. The real WPF controls, tickets and API are never modified.
    /// Triggered from App's global Window PreviewKeyDown handler.
    /// </summary>
    public sealed class ShootingStarsWindow : Window
    {
        private const double BuildSeconds = 8;
        private const double DropSeconds = 15;
        private const double FinaleSeconds = 30;
        private const double EndSeconds = 35;

        private static ShootingStarsWindow? _active;

        private readonly Window _host;
        private readonly double _width;
        private readonly double _height;
        private readonly Stopwatch _clock = new();
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _audioWatchdog;
        private readonly MediaPlayer _music = new();
        private readonly Random _random = new(1019);

        private readonly Canvas _stars = new() { IsHitTestVisible = false };
        private readonly Canvas _debris = new() { IsHitTestVisible = false };
        private readonly Canvas _flyers = new() { IsHitTestVisible = false };
        private readonly Image _originalScreen = new() { Stretch = Stretch.Fill };
        private readonly RotateTransform _screenRotation = new();
        private readonly TranslateTransform _screenShift = new();
        private readonly TextBlock _message = new();
        private readonly List<Star> _starItems = new();
        private readonly List<Fragment> _fragments = new();
        private readonly List<Flyer> _floatingObjects = new();
        private readonly Image _finalLogo = new() { Stretch = Stretch.Uniform, Opacity = 0 };
        private readonly ScaleTransform _logoScale = new(1, 1);

        private BitmapSource? _screenshot;
        private bool _dropped;

        private ShootingStarsWindow(Window host)
        {
            _host = host;
            var content = host.Content as FrameworkElement;
            _width = Math.Max(320, content?.ActualWidth ?? host.ActualWidth);
            _height = Math.Max(260, content?.ActualHeight ?? host.ActualHeight);

            // PointToScreen is in device pixels, while Window.Left/Top use WPF DIPs.
            var devicePosition = host.PointToScreen(new Point(0, 0));
            var fromDevice = PresentationSource.FromVisual(host)?
                .CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var windowPosition = fromDevice.Transform(devicePosition);

            Owner = host;
            Left = windowPosition.X;
            Top = windowPosition.Y;
            Width = _width;
            Height = _height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false;
            ShowActivated = true;
            Background = Brushes.Black;
            Focusable = true;

            // Capture before the overlay becomes visible; no live application UI moves.
            _screenshot = CaptureHost(content);
            BuildScene();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _timer.Tick += OnFrame;

            _audioWatchdog = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _audioWatchdog.Tick += (_, _) =>
            {
                _audioWatchdog.Stop();
                StartTimeline();
            };

            _host.Closed += HostClosed;
            _host.LocationChanged += HostMoved;
            _host.SizeChanged += HostResized;
            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        public static bool IsRunning => _active != null;

        public static void TryStart(Window host)
        {
            if (_active != null || !host.IsVisible ||
                host.WindowState == WindowState.Minimized)
                return;

            try
            {
                var window = new ShootingStarsWindow(host);
                _active = window;
                window.Show();
                window.Activate();
                window.Focus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shooting Stars Easter egg could not start: {ex}");
                _active = null;
            }
        }

        public static void Stop()
        {
            _active?.Close();
        }

        private static BitmapSource? CaptureHost(FrameworkElement? content)
        {
            if (content == null || content.ActualWidth <= 0 || content.ActualHeight <= 0)
                return null;

            try
            {
                var dpi = VisualTreeHelper.GetDpi(content);
                var pixelsWide = Math.Max(1, (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX));
                var pixelsHigh = Math.Max(1, (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY));

                // Limit oversized or unusually scaled surfaces on field laptops.
                if ((long)pixelsWide * pixelsHigh > 9_000_000)
                    return null;

                var bitmap = new RenderTargetBitmap(
                    pixelsWide, pixelsHigh, dpi.PixelsPerInchX,
                    dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                bitmap.Render(content);
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shooting Stars screenshot unavailable: {ex}");
                return null;
            }
        }

        private void BuildScene()
        {
            var root = new Grid
            {
                Width = _width,
                Height = _height,
                ClipToBounds = true,
                Background = new LinearGradientBrush(
                    Color.FromRgb(1, 4, 18), Color.FromRgb(14, 26, 70), 34)
            };
            Content = root;

            _stars.Width = _debris.Width = _flyers.Width = _width;
            _stars.Height = _debris.Height = _flyers.Height = _height;

            CreateStars();
            _stars.Opacity = 0;
            root.Children.Add(_stars);

            _originalScreen.Width = _width;
            _originalScreen.Height = _height;
            _originalScreen.Source = _screenshot;
            _originalScreen.RenderTransformOrigin = new Point(.5, .5);
            var screenTransforms = new TransformGroup();
            screenTransforms.Children.Add(_screenRotation);
            screenTransforms.Children.Add(_screenShift);
            _originalScreen.RenderTransform = screenTransforms;
            root.Children.Add(_originalScreen);

            _debris.Visibility = Visibility.Collapsed;
            _flyers.Visibility = Visibility.Collapsed;
            root.Children.Add(_debris);
            root.Children.Add(_flyers);

            if (_screenshot == null)
            {
                _originalScreen.Source = null;
                var fallback = MakeCard("SMART GRID SUITE", "RADIO NETWORK ONLINE");
                fallback.Width = Math.Min(440, _width * .8);
                fallback.Height = 130;
                var fallbackLayer = new Canvas
                {
                    Width = _width, Height = _height, IsHitTestVisible = false
                };
                Canvas.SetLeft(fallback, (_width - fallback.Width) / 2);
                Canvas.SetTop(fallback, (_height - fallback.Height) / 2);
                fallbackLayer.Children.Add(fallback);
                root.Children.Add(fallbackLayer);
                // Keep the fallback card moving with the screenshot.
                fallbackLayer.RenderTransform = screenTransforms;
                // Avoid sharing the same mutable transform across multiple visuals.
                _originalScreen.RenderTransform = new TransformGroup
                {
                    Children = new TransformCollection
                    {
                        new RotateTransform(), new TranslateTransform()
                    }
                };
            }

            try
            {
                _finalLogo.Source = new BitmapImage(
                    new Uri("pack://application:,,,/Assets/Branding/SmartGridSuite_Logo.png"));
            }
            catch
            {
                // No external asset is required for the animation.
            }

            _finalLogo.Width = Math.Min(220, _width * .32);
            _finalLogo.Height = _finalLogo.Width;
            _finalLogo.HorizontalAlignment = HorizontalAlignment.Center;
            _finalLogo.VerticalAlignment = VerticalAlignment.Center;
            _finalLogo.RenderTransformOrigin = new Point(.5, .5);
            _finalLogo.RenderTransform = _logoScale;
            root.Children.Add(_finalLogo);

            _message.Text = " ";
            _message.FontSize = Math.Clamp(_width / 35, 19, 38);
            _message.FontWeight = FontWeights.ExtraBold;
            _message.Foreground = Brushes.White;
            _message.TextAlignment = TextAlignment.Center;
            _message.TextWrapping = TextWrapping.Wrap;
            _message.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.DeepSkyBlue, BlurRadius = 26, ShadowDepth = 0
            };
            _message.Margin = new Thickness(28, 26, 28, 0);
            _message.VerticalAlignment = VerticalAlignment.Top;
            _message.IsHitTestVisible = false;
            root.Children.Add(_message);

            var escapeHint = new TextBlock
            {
                Text = "ESC — return to Earth",
                Foreground = new SolidColorBrush(Color.FromArgb(210, 172, 198, 255)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 20, 14),
                IsHitTestVisible = false
            };
            root.Children.Add(escapeHint);
        }

        private void CreateStars()
        {
            for (var i = 0; i < 95; i++)
            {
                var radius = 1 + _random.NextDouble() * 2.4;
                var star = new Ellipse
                {
                    Width = radius,
                    Height = radius,
                    Fill = i % 6 == 0 ? Brushes.LightSkyBlue : Brushes.White,
                    Opacity = .4 + _random.NextDouble() * .6
                };
                var item = new Star(star,
                    _random.NextDouble() * _width,
                    _random.NextDouble() * _height,
                    35 + _random.NextDouble() * 160);
                _starItems.Add(item);
                _stars.Children.Add(star);
                Canvas.SetLeft(star, item.X);
                Canvas.SetTop(star, item.Y);
            }
        }

        private void BeginDrop()
        {
            if (_dropped)
                return;
            _dropped = true;
            _originalScreen.Visibility = Visibility.Collapsed;
            _debris.Visibility = Visibility.Visible;
            _flyers.Visibility = Visibility.Visible;

            if (_screenshot != null)
            {
                const int columns = 5, rows = 3;
                for (var row = 0; row < rows; row++)
                for (var col = 0; col < columns; col++)
                {
                    var left = col * _screenshot.PixelWidth / columns;
                    var top = row * _screenshot.PixelHeight / rows;
                    var right = (col + 1) * _screenshot.PixelWidth / columns;
                    var bottom = (row + 1) * _screenshot.PixelHeight / rows;
                    var crop = new CroppedBitmap(_screenshot,
                        new Int32Rect(left, top, right - left, bottom - top));
                    crop.Freeze();

                    var piece = new Image
                    {
                        Source = crop, Stretch = Stretch.Fill,
                        Width = _width / columns,
                        Height = _height / rows,
                        RenderTransformOrigin = new Point(.5, .5)
                    };
                    var move = new TranslateTransform();
                    var rotate = new RotateTransform();
                    var transforms = new TransformGroup();
                    transforms.Children.Add(rotate);
                    transforms.Children.Add(move);
                    piece.RenderTransform = transforms;

                    Canvas.SetLeft(piece, col * _width / columns);
                    Canvas.SetTop(piece, row * _height / rows);
                    _debris.Children.Add(piece);
                    _fragments.Add(new Fragment(piece, move, rotate,
                        (col - 2) * 1.1 + (_random.NextDouble() - .5) * 2.2,
                        (row - 1) * 1.5 + (_random.NextDouble() - .5) * 1.9,
                        (_random.NextDouble() - .5) * 48));
                }
            }

            AddCard("TRUCK 07", "NOW LEAVING THE ATMOSPHERE", .07, .31, -1);
            AddCard("TICKET 1405xxxxx", "STATUS: IN ORBIT", .56, .12, 1);
            AddCard("SNMP / PING", "TIMEOUT: ∞ LIGHT YEARS", .40, .65, -1.5);
            AddCard("RADIO NETWORK", "SIGNAL STRENGTH: COSMIC", .10, .75, 1.3);
            AddCard("DISPATCH", "ASSIGNMENT: MOON", .69, .72, -.8);
            AddTower();
        }

        private static Border MakeCard(string heading, string detail)
        {
            return new Border
            {
                Width = 260,
                Height = 93,
                Background = new SolidColorBrush(Color.FromArgb(243, 12, 25, 56)),
                BorderBrush = Brushes.DeepSkyBlue,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(13),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = heading,
                            Foreground = Brushes.White,
                            FontWeight = FontWeights.Bold,
                            FontSize = 18
                        },
                        new TextBlock
                        {
                            Text = detail,
                            Foreground = Brushes.LightSkyBlue,
                            FontSize = 12,
                            Margin = new Thickness(0, 9, 0, 0),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
        }

        private void AddCard(string header, string detail, double x, double y, double speed)
        {
            var card = MakeCard(header, detail);
            AddFlyer(card, x * Math.Max(1, _width - 260),
                y * Math.Max(1, _height - 93), speed);
        }

        private void AddTower()
        {
            var tower = new Canvas { Width = 130, Height = 190 };
            var segments = new (double x1, double y1, double x2, double y2)[]
            {
                (65,5,19,174), (65,5,111,174), (20,174,111,174),
                (43,88,87,88), (28,140,101,140), (52,49,78,49),
                (43,88,78,49), (87,88,52,49), (28,140,87,88), (101,140,43,88),
                (65,5,65,175)
            };
            foreach (var s in segments)
                tower.Children.Add(new Line
                {
                    X1 = s.x1, Y1 = s.y1, X2 = s.x2, Y2 = s.y2,
                    Stroke = Brushes.Cyan, StrokeThickness = 4
                });
            tower.Children.Add(new Ellipse
            {
                Width = 18, Height = 18,
                Fill = Brushes.White,
                Margin = new Thickness(56, 0, 0, 0)
            });
            AddFlyer(tower, _width * .70, _height * .32, 1.8);
        }

        private void AddFlyer(UIElement visual, double x, double y, double speed)
        {
            var move = new TranslateTransform();
            var rotate = new RotateTransform();
            var group = new TransformGroup();
            group.Children.Add(rotate);
            group.Children.Add(move);
            visual.RenderTransformOrigin = new Point(.5, .5);
            visual.RenderTransform = group;
            Canvas.SetLeft(visual, x);
            Canvas.SetTop(visual, y);
            _flyers.Children.Add(visual);
            _floatingObjects.Add(new Flyer(visual, move, rotate, speed,
                _random.NextDouble() * 6.28));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var track = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartGridSuite", "EasterEgg", "ShootingStars.mp3");

            if (!File.Exists(track))
            {
                StartTimeline();
                return;
            }

            _music.MediaOpened += (_, _) =>
            {
                if (_clock.IsRunning)
                    return;
                _audioWatchdog.Stop();
                _music.Volume = .72; // App volume only; never changes Windows volume.
                _music.Play();
                StartTimeline();
            };
            _music.MediaFailed += (_, _) =>
            {
                _audioWatchdog.Stop();
                StartTimeline();
            };

            try
            {
                _audioWatchdog.Start();
                _music.Open(new Uri(track, UriKind.Absolute));
            }
            catch
            {
                _audioWatchdog.Stop();
                StartTimeline();
            }
        }

        private void StartTimeline()
        {
            if (_clock.IsRunning)
                return;
            _clock.Restart();
            _timer.Start();
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            if (seconds >= EndSeconds)
            {
                Close();
                return;
            }

            if (seconds < BuildSeconds)
            {
                _message.Text = "";
                return;
            }

            if (seconds < DropSeconds)
            {
                var intensity = (seconds - BuildSeconds) / (DropSeconds - BuildSeconds);
                _screenRotation.Angle = Math.Sin(seconds * 8) * intensity * 7;
                _screenShift.X = Math.Sin(seconds * 19) * intensity * 18;
                _screenShift.Y = Math.Cos(seconds * 21) * intensity * 11;
                _message.Text = seconds < 11
                    ? "⚠ RADIO NETWORK LOSING GRAVITY"
                    : "CRITICAL: ALL TRUCKS NOW OFF-WORLD";
                _message.Opacity = .65 + Math.Sin(seconds * 11) * .30;
                return;
            }

            BeginDrop();
            _stars.Opacity = Math.Min(1, (seconds - DropSeconds) * 2);
            var flight = seconds - DropSeconds;
            _message.Opacity = 1;
            _message.Text = seconds < 25
                ? "SMART GRID SUITE: OUT OF COVERAGE"
                : seconds < FinaleSeconds
                    ? "DISPATCH, WE HAVE A PROBLEM"
                    : "RETURNING TO EARTH...";

            foreach (var star in _starItems)
            {
                var newX = (star.X - flight * star.Speed) % _width;
                if (newX < 0) newX += _width;
                Canvas.SetLeft(star.Shape, newX);
                Canvas.SetTop(star.Shape,
                    star.Y + Math.Sin(flight * .5 + star.X) * 3);
            }

            foreach (var fragment in _fragments)
            {
                fragment.Move.X = fragment.VelocityX * flight * 31;
                fragment.Move.Y = fragment.VelocityY * flight * 29 +
                                  flight * flight * 1.5;
                fragment.Rotation.Angle = fragment.Spin * flight;
                fragment.Image.Opacity = Math.Max(0, 1 - flight / 10);
            }

            foreach (var flyer in _floatingObjects)
            {
                flyer.Move.X = Math.Sin(flight * (.5 + Math.Abs(flyer.Speed) * .1)
                    + flyer.Phase) * _width * .18 + flight * flyer.Speed * 15;
                flyer.Move.Y = Math.Cos(flight * .65 + flyer.Phase) * _height * .22
                    - flight * flyer.Speed * 8;
                flyer.Rotation.Angle =
                    Math.Sin(flight * .8 + flyer.Phase) * 30 + flight * flyer.Speed * 10;
                flyer.Element.Opacity = seconds >= FinaleSeconds
                    ? Math.Max(0, 1 - (seconds - FinaleSeconds) / 3)
                    : 1;
            }

            if (seconds >= FinaleSeconds)
            {
                var progress = (seconds - FinaleSeconds) /
                               (EndSeconds - FinaleSeconds);
                _finalLogo.Opacity = Math.Min(1, (seconds - FinaleSeconds) * 2)
                    * Math.Max(0, 1 - Math.Max(0, progress - .8) * 5);
                _logoScale.ScaleX = _logoScale.ScaleY = 1 + progress * 7;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            Close();
        }

        private void HostClosed(object? sender, EventArgs e) => Close();
        private void HostMoved(object? sender, EventArgs e) => Close();
        private void HostResized(object sender, SizeChangedEventArgs e) => Close();

        private void OnClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _audioWatchdog.Stop();
            _clock.Stop();
            _music.Stop();
            _music.Close();

            _host.Closed -= HostClosed;
            _host.LocationChanged -= HostMoved;
            _host.SizeChanged -= HostResized;
            PreviewKeyDown -= OnPreviewKeyDown;
            Loaded -= OnLoaded;
            Closed -= OnClosed;

            if (ReferenceEquals(_active, this))
                _active = null;
        }

        private sealed record Star(Ellipse Shape, double X, double Y, double Speed);
        private sealed record Fragment(Image Image, TranslateTransform Move,
            RotateTransform Rotation, double VelocityX, double VelocityY, double Spin);
        private sealed record Flyer(UIElement Element, TranslateTransform Move,
            RotateTransform Rotation, double Speed, double Phase);
    }
}
