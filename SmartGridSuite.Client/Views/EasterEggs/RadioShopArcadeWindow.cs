using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartGridSuite.Client.Views.EasterEggs
{
    /// <summary>
    /// Self-contained toy city: all towers, outages and scores are fictional.
    /// No application data or API services are read or modified.
    /// </summary>
    public sealed class RadioShopArcadeWindow : Window
    {
        private const int Columns = 17;
        private const int Rows = 12;
        private const double Cell = 33;
        private const double BoardLeft = 25;
        private const double BoardTop = 122;
        private const double BoardWidth = Columns * Cell;
        private const double BoardHeight = Rows * Cell;

        private static RadioShopArcadeWindow? _active;

        private static readonly Brush BackgroundBrush = Brush("#080f23");
        private static readonly Brush RoadBrush = Brush("#243248");
        private static readonly Brush BuildingBrush = Brush("#142442");
        private static readonly Brush BuildingAccentBrush = Brush("#23446b");
        private static readonly Brush CyanBrush = Brush("#57edff");
        private static readonly Brush GreenBrush = Brush("#64f9bc");
        private static readonly Brush RedBrush = Brush("#ff6385");
        private static readonly Brush GoldBrush = Brush("#ffe08a");
        private static readonly Brush WhiteBrush = Brush("#f3f8ff");
        private static readonly Brush DimBrush = Brush("#94a7c9");
        private static readonly Brush PanelBrush = Brush("#101d38");

        private readonly Window _host;
        private readonly ArcadeCanvas _canvas;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _gameClock = new();
        private readonly Random _random = new();
        private readonly List<RadioSite> _sites = new();

        private int _truckX = 1;
        private int _truckY = 1;
        private int _direction = 2;
        private int _wave;
        private int _score;
        private int _highScore;
        private int _repairs;
        private double _secondsRemaining;
        private double _lastTickSeconds;
        private double _nextWaveAt = -1;
        private DateTime _lastMoveUtc = DateTime.MinValue;
        private string _radioMessage = "DISPATCH: Three sites are down.";
        private bool _paused;
        private bool _gameOver;

        private RadioShopArcadeWindow(Window host)
        {
            _host = host;
            Title = "Radio Shop Arcade — Smart Grid Suite";
            Width = 940;
            Height = 650;
            MinWidth = Width;
            MinHeight = Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = host;
            ShowInTaskbar = false;
            Background = BackgroundBrush;
            FontFamily = new FontFamily("Consolas");
            Focusable = true;

            _canvas = new ArcadeCanvas(this);
            Content = _canvas;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _timer.Tick += Tick;

            for (int y = 2; y <= 11; y += 3)
            {
                for (int x = 2; x <= 14; x += 4)
                {
                    _sites.Add(new RadioSite(x, y,
                        "TWR-" + (_sites.Count + 1).ToString("00", CultureInfo.InvariantCulture)));
                }
            }

            _highScore = ReadHighScore();

            PreviewKeyDown += HandleKey;
            _host.Closed += HostClosed;
            _host.IsVisibleChanged += HostVisibilityChanged;
            _host.StateChanged += HostStateChanged;
            Closed += WindowClosed;
            Loaded += (_, _) =>
            {
                NewGame();
                Activate();
                Keyboard.Focus(this);
            };
            Deactivated += (_, _) =>
            {
                if (!_gameOver && !_paused)
                    TogglePause();
            };
        }

        public static bool IsRunning => _active != null;

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
                var game = new RadioShopArcadeWindow(host);
                _active = game;
                game.Show();
                game.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Radio Shop Arcade could not open: " + ex);
                _active = null;
            }
        }

        private void NewGame()
        {
            _timer.Stop();
            _truckX = 1;
            _truckY = 1;
            _direction = 2;
            _wave = 0;
            _score = 0;
            _repairs = 0;
            _secondsRemaining = 105;
            _lastTickSeconds = 0;
            _nextWaveAt = -1;
            _paused = false;
            _gameOver = false;
            _radioMessage = "DISPATCH: Three sites are down.";
            _lastMoveUtc = DateTime.MinValue;
            _gameClock.Restart();
            BeginWave();
            _timer.Start();
            _canvas.InvalidateVisual();
        }

        private void BeginWave()
        {
            _wave++;
            foreach (var site in _sites)
                site.IsOffline = false;

            int targetCount = Math.Min(_sites.Count, 2 + _wave);
            foreach (var site in _sites.OrderBy(_ => _random.Next()).Take(targetCount))
                site.IsOffline = true;

            _nextWaveAt = -1;
            _radioMessage = _wave == 1
                ? "DISPATCH: Three sites are down."
                : _wave < 4
                    ? "DISPATCH: More offline radios. Sorry."
                    : "DISPATCH: They ALL went down again.";
        }

        private void Tick(object? sender, EventArgs e)
        {
            if (_paused || _gameOver)
                return;

            double elapsed = _gameClock.Elapsed.TotalSeconds;
            double delta = Math.Max(0, elapsed - _lastTickSeconds);
            _lastTickSeconds = elapsed;
            _secondsRemaining = Math.Max(0, _secondsRemaining - delta);

            if (_secondsRemaining <= 0)
            {
                EndGame();
                return;
            }

            if (_nextWaveAt >= 0 && elapsed >= _nextWaveAt)
            {
                _secondsRemaining = Math.Min(130, _secondsRemaining + 18);
                BeginWave();
            }

            _canvas.InvalidateVisual();
        }

        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }

            if (e.Key == Key.R && _gameOver)
            {
                e.Handled = true;
                NewGame();
                return;
            }

            if (e.Key == Key.P && !_gameOver)
            {
                e.Handled = true;
                TogglePause();
                return;
            }

            if (_paused || _gameOver)
                return;

            int dx = 0, dy = 0;
            switch (e.Key)
            {
                case Key.Left:
                case Key.A:
                    dx = -1;
                    _direction = 3;
                    break;
                case Key.Right:
                case Key.D:
                    dx = 1;
                    _direction = 1;
                    break;
                case Key.Up:
                case Key.W:
                    dy = -1;
                    _direction = 0;
                    break;
                case Key.Down:
                case Key.S:
                    dy = 1;
                    _direction = 2;
                    break;
                case Key.Space:
                    e.Handled = true;
                    RepairNearest();
                    return;
                default:
                    return;
            }

            e.Handled = true;
            var now = DateTime.UtcNow;
            if ((now - _lastMoveUtc).TotalMilliseconds < 85)
                return;
            _lastMoveUtc = now;

            int nextX = _truckX + dx;
            int nextY = _truckY + dy;
            if (IsRoad(nextX, nextY))
            {
                _truckX = nextX;
                _truckY = nextY;
                _canvas.InvalidateVisual();
            }
        }

        private void RepairNearest()
        {
            var site = _sites.FirstOrDefault(s =>
                s.IsOffline &&
                Math.Abs(s.X - _truckX) + Math.Abs(s.Y - _truckY) == 1);

            if (site == null)
            {
                _radioMessage = "TECH: Gotta park next to a RED tower!";
                _canvas.InvalidateVisual();
                return;
            }

            site.IsOffline = false;
            _repairs++;
            _score += 100 + Math.Min(_wave - 1, 10) * 20;
            _radioMessage = _random.Next(4) switch
            {
                0 => "DISPATCH: Good copy. Next site!",
                1 => "TECH: Rebooted it. Works now.",
                2 => "DISPATCH: Can you take one more?",
                _ => "TECH: Put it on the work order."
            };

            if (_sites.All(s => !s.IsOffline))
            {
                _score += 200;
                _radioMessage = "ALL LINKS RESTORED! +200 BONUS";
                _nextWaveAt = _gameClock.Elapsed.TotalSeconds + 1.5;
            }

            _canvas.InvalidateVisual();
        }

        private void TogglePause()
        {
            _paused = !_paused;
            if (_paused)
                _gameClock.Stop();
            else
                _gameClock.Start();

            _canvas.InvalidateVisual();
        }

        private void EndGame()
        {
            _timer.Stop();
            _gameClock.Stop();
            _gameOver = true;
            _radioMessage = "END OF SHIFT. DISPATCH GOES HOME.";
            if (_score > _highScore)
            {
                _highScore = _score;
                SaveHighScore(_score);
            }
            _canvas.InvalidateVisual();
        }

        private static bool IsRoad(int x, int y)
        {
            return x >= 0 && x < Columns && y >= 0 && y < Rows &&
                (x % 4 == 1 || y % 3 == 1);
        }

        private void Render(DrawingContext dc, double pixelsPerDip)
        {
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, 940, 650));
            dc.DrawRectangle(Brush("#0e1b36"), new Pen(Brush("#335c85"), 2),
                new Rect(14, 14, 892, 578));

            Text(dc, "RADIO SHOP ARCADE", 27, 24, 31, CyanBrush, pixelsPerDip, true);
            Text(dc, "A TOTALLY UNAUTHORIZED TRAINING SIMULATOR", 28, 69,
                12, DimBrush, pixelsPerDip);

            DrawCity(dc, pixelsPerDip);
            DrawSidebar(dc, pixelsPerDip);

            dc.DrawRectangle(PanelBrush, new Pen(Brush("#335c85"), 1),
                new Rect(25, 537, 859, 43));
            Text(dc, "WASD / ARROWS  DRIVE", 39, 549, 13, WhiteBrush, pixelsPerDip, true);
            Text(dc, "SPACE  FIX RADIO", 268, 549, 13, GreenBrush, pixelsPerDip, true);
            Text(dc, "P  PAUSE", 487, 549, 13, GoldBrush, pixelsPerDip, true);
            Text(dc, "ESC  QUIT", 663, 549, 13, DimBrush, pixelsPerDip, true);

            if (_paused || _gameOver)
                DrawEndOverlay(dc, pixelsPerDip);
        }

        private void DrawCity(DrawingContext dc, double pixelsPerDip)
        {
            dc.DrawRectangle(Brush("#0a142a"), new Pen(CyanBrush, 2),
                new Rect(BoardLeft - 2, BoardTop - 2, BoardWidth + 4, BoardHeight + 4));

            foreach (var y in Enumerable.Range(0, Rows))
            foreach (var x in Enumerable.Range(0, Columns))
            {
                double left = BoardLeft + x * Cell;
                double top = BoardTop + y * Cell;
                bool road = IsRoad(x, y);

                dc.DrawRectangle(road ? RoadBrush : Brush("#0d1a32"),
                    new Pen(Brush("#172947"), .7),
                    new Rect(left, top, Cell, Cell));

                if (road)
                {
                    if (x % 4 == 1 && y % 3 != 1)
                        dc.DrawRectangle(Brush("#536e86"), null,
                            new Rect(left + 16, top + 9, 2, 14));
                    else if (y % 3 == 1 && x % 4 != 1)
                        dc.DrawRectangle(Brush("#536e86"), null,
                            new Rect(left + 9, top + 16, 14, 2));
                }
                else
                {
                    dc.DrawRectangle(BuildingBrush, new Pen(BuildingAccentBrush, 1),
                        new Rect(left + 3, top + 3, Cell - 6, Cell - 6));
                    dc.DrawRectangle(BuildingAccentBrush, null,
                        new Rect(left + 8, top + 8, 6, 5));
                    dc.DrawRectangle(BuildingAccentBrush, null,
                        new Rect(left + 20, top + 8, 5, 5));
                    dc.DrawRectangle(BuildingAccentBrush, null,
                        new Rect(left + 8, top + 21, 6, 4));
                }
            }

            foreach (var site in _sites)
                DrawTower(dc, site, pixelsPerDip);

            DrawTruck(dc);
        }

        private void DrawTower(DrawingContext dc, RadioSite site, double pixelsPerDip)
        {
            var x = BoardLeft + site.X * Cell;
            var y = BoardTop + site.Y * Cell;
            var glow = site.IsOffline ? RedBrush : GreenBrush;
            var pen = new Pen(glow, 2.2);
            var cx = x + Cell / 2;

            dc.DrawRectangle(Brush("#071123"), new Pen(glow, site.IsOffline ? 2 : 1),
                new Rect(x + 2, y + 2, Cell - 4, Cell - 4));
            dc.DrawLine(pen, new Point(cx, y + 5), new Point(x + 8, y + 27));
            dc.DrawLine(pen, new Point(cx, y + 5), new Point(x + 25, y + 27));
            dc.DrawLine(pen, new Point(x + 8, y + 27), new Point(x + 25, y + 27));
            dc.DrawLine(pen, new Point(x + 12, y + 19), new Point(x + 21, y + 19));
            dc.DrawEllipse(glow, null, new Point(cx, y + 6), 3, 3);

            if (site.IsOffline)
            {
                // Pulsing outage dot; uses only this game's clock.
                double radius = 3 + Math.Abs(Math.Sin(_gameClock.Elapsed.TotalSeconds * 5)) * 3;
                dc.DrawEllipse(RedBrush, null,
                    new Point(x + 28, y + 5), radius, radius);
            }
        }

        private void DrawTruck(DrawingContext dc)
        {
            double cx = BoardLeft + _truckX * Cell + Cell / 2;
            double cy = BoardTop + _truckY * Cell + Cell / 2;

            dc.PushTransform(new RotateTransform(_direction * 90, cx, cy));

            dc.DrawRectangle(Brush("#030812"), null,
                new Rect(cx - 13, cy - 15, 26, 30));
            dc.DrawRectangle(GoldBrush, new Pen(WhiteBrush, 1),
                new Rect(cx - 10, cy - 14, 20, 26));
            dc.DrawRectangle(CyanBrush, null,
                new Rect(cx - 8, cy - 11, 16, 8));
            dc.DrawRectangle(Brush("#22324b"), null,
                new Rect(cx - 8, cy + 1, 16, 8));
            dc.DrawRectangle(WhiteBrush, null,
                new Rect(cx - 12, cy - 10, 3, 6));
            dc.DrawRectangle(WhiteBrush, null,
                new Rect(cx + 9, cy - 10, 3, 6));

            dc.Pop();
        }

        private void DrawSidebar(DrawingContext dc, double pixelsPerDip)
        {
            const double x = 607;
            dc.DrawRectangle(PanelBrush, new Pen(Brush("#335c85"), 1),
                new Rect(x, BoardTop, 277, BoardHeight));

            Text(dc, "SHIFT STATUS", x + 15, BoardTop + 12,
                18, CyanBrush, pixelsPerDip, true);
            Text(dc, "TIME LEFT", x + 15, 174, 12, DimBrush, pixelsPerDip, true);
            Text(dc, ((int)Math.Ceiling(_secondsRemaining)).ToString("000", CultureInfo.InvariantCulture) + " SEC",
                x + 15, 192, 27,
                _secondsRemaining < 20 ? RedBrush : GoldBrush, pixelsPerDip, true);
            Text(dc, "SCORE", x + 154, 174, 12, DimBrush, pixelsPerDip, true);
            Text(dc, _score.ToString("000000", CultureInfo.InvariantCulture),
                x + 154, 198, 21, WhiteBrush, pixelsPerDip, true);

            Text(dc, "WAVE " + _wave, x + 15, 251, 16, GreenBrush, pixelsPerDip, true);
            Text(dc, "BEST " + _highScore, x + 154, 253, 12, DimBrush, pixelsPerDip);

            dc.DrawLine(new Pen(Brush("#335c85"), 1),
                new Point(x + 14, 283), new Point(x + 262, 283));

            Text(dc, "OFFLINE SITES", x + 15, 301, 15, RedBrush, pixelsPerDip, true);
            var offline = _sites.Where(s => s.IsOffline).ToList();
            for (int i = 0; i < Math.Min(5, offline.Count); i++)
            {
                var site = offline[i];
                Text(dc, "■  " + site.Code, x + 18, 328 + i * 21,
                    14, RedBrush, pixelsPerDip, true);
            }

            if (offline.Count > 5)
                Text(dc, "+ " + (offline.Count - 5) + " more", x + 18,
                    434, 12, DimBrush, pixelsPerDip);

            dc.DrawLine(new Pen(Brush("#335c85"), 1),
                new Point(x + 14, 453), new Point(x + 262, 453));

            Text(dc, "DISPATCH RADIO", x + 15, 465, 14, GoldBrush, pixelsPerDip, true);
            Text(dc, _radioMessage, x + 15, 488, 12,
                WhiteBrush, pixelsPerDip, false, 246);
        }

        private void DrawEndOverlay(DrawingContext dc, double pixelsPerDip)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, 5, 11, 27)),
                new Pen(CyanBrush, 3), new Rect(133, 184, 660, 262));
            Text(dc, _gameOver ? "END OF SHIFT" : "RADIO SILENCE — PAUSED",
                165, 222, 31, _gameOver ? GoldBrush : CyanBrush, pixelsPerDip, true);

            if (_gameOver)
            {
                Text(dc, "RADIOS FIXED: " + _repairs + "    SCORE: " + _score,
                    166, 289, 19, WhiteBrush, pixelsPerDip, true);
                Text(dc, _score >= _highScore && _score > 0
                        ? "NEW PERSONAL HIGH SCORE!"
                        : "DISPATCH: SEE YOU TOMORROW.",
                    166, 329, 17, GreenBrush, pixelsPerDip);
                Text(dc, "R TO TRY AGAIN     ESC TO QUIT",
                    166, 387, 16, DimBrush, pixelsPerDip, true);
            }
            else
            {
                Text(dc, "P TO RESUME     ESC TO QUIT",
                    167, 322, 18, WhiteBrush, pixelsPerDip, true);
            }
        }

        private static void Text(
            DrawingContext dc, string value, double x, double y,
            double size, Brush color, double pixelsPerDip,
            bool bold = false, double maxWidth = 0)
        {
            var formatted = new FormattedText(
                value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Consolas"), FontStyles.Normal,
                    bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal),
                size, color, pixelsPerDip);

            if (maxWidth > 0)
                formatted.MaxTextWidth = maxWidth;

            dc.DrawText(formatted, new Point(x, y));
        }

        private static Brush Brush(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }

        private static string ScoreFile =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartGridSuite", "Arcade", "highscore.txt");

        private static int ReadHighScore()
        {
            try
            {
                return File.Exists(ScoreFile) &&
                    int.TryParse(File.ReadAllText(ScoreFile),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out var score)
                    ? Math.Max(0, score)
                    : 0;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private static void SaveHighScore(int score)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScoreFile)!);
                File.WriteAllText(ScoreFile,
                    score.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException) { /* Score is optional. */ }
            catch (UnauthorizedAccessException) { /* Score is optional. */ }
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
            _timer.Stop();
            _gameClock.Stop();
            _timer.Tick -= Tick;
            PreviewKeyDown -= HandleKey;
            _host.Closed -= HostClosed;
            _host.IsVisibleChanged -= HostVisibilityChanged;
            _host.StateChanged -= HostStateChanged;
            Closed -= WindowClosed;

            if (ReferenceEquals(_active, this))
                _active = null;
        }

        private sealed class RadioSite
        {
            public int X { get; }
            public int Y { get; }
            public string Code { get; }
            public bool IsOffline { get; set; }

            public RadioSite(int x, int y, string code)
            {
                X = x;
                Y = y;
                Code = code;
            }
        }

        private sealed class ArcadeCanvas : FrameworkElement
        {
            private readonly RadioShopArcadeWindow _game;

            public ArcadeCanvas(RadioShopArcadeWindow game)
            {
                _game = game;
                Focusable = false;
            }

            // Custom FrameworkElement has no children, so explicitly request
            // the arcade surface's design size during WPF measure/layout.
            protected override Size MeasureOverride(Size availableSize)
            {
                return new Size(940, 600);
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);
                _game.Render(drawingContext, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            }
        }
    }
}
