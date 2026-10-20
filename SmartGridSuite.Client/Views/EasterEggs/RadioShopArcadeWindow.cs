using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Arcade;
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

        // Two distinct roadside service locations at the center of the city.
        // Both are valid road cells (row 7 is a horizontal road).
        private const int ShopX = 9;
        private const int ShopY = 7;
        private const int GarageX = 10;
        private const int GarageY = 7;

        private const int BaseTruckCapacity = 4;
        private const int CapacityPerUpgrade = 2;
        private const int MaximumTruckCapacity = 12;
        private const int FirstTruckUpgradePrice = 350;
        private const int UpgradePriceStep = 250;

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
        private readonly MediaPlayer _chopChopPlayer = new();
        private bool _chopChopAvailable;
        private bool _chopChopPlayedAt60;
        private bool _chopChopPlayedAt10;
        private readonly Random _random = new();
        private readonly List<RadioSite> _sites = new();

        private int _truckX = 1;
        private int _truckY = 1;
        private int _direction = 2;
        private int _wave;
        private int _cash;
        private int _bestCash;
        private int _repairs;
        private int _truckCapacity = BaseTruckCapacity;
        private int _upgradeLevel;
        private int _spareRadios = BaseTruckCapacity;
        private int _missedPriorities;
        private bool _started;
        private double _secondsRemaining;
        private bool _leaderboardVisible;
        private bool _leaderboardAutoPaused;
        private List<ArcadeLeaderboardEntryDto> _leaderboard = new();
        private string _leaderboardStatus = "Loading shop leaderboard...";
        private string _scoreUploadStatus = "Your score is saved after the shift.";
        private string _playerName = "Guest (local only)";
        private string? _employeeId;
        private readonly ApiClient _arcadeApi = ClientAppSettings.CreateApiClient();
        private double _lastTickSeconds;
        private double _nextWaveAt = -1;
        private DateTime _lastMoveUtc = DateTime.MinValue;
        private string _radioMessage = "DISPATCH: Press SPACE to begin your shift.";

        private int? NextUpgradePrice => _truckCapacity >= MaximumTruckCapacity
            ? null
            : FirstTruckUpgradePrice + _upgradeLevel * UpgradePriceStep;
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
            InitializeCountdownAudio();

            for (int y = 2; y <= 11; y += 3)
            {
                for (int x = 2; x <= 14; x += 4)
                {
                    _sites.Add(new RadioSite(x, y,
                        "TWR-" + (_sites.Count + 1).ToString("00", CultureInfo.InvariantCulture)));
                }
            }

            _bestCash = ReadHighScore();

            PreviewKeyDown += HandleKey;
            _host.Closed += HostClosed;
            _host.IsVisibleChanged += HostVisibilityChanged;
            _host.StateChanged += HostStateChanged;
            Closed += WindowClosed;
            Loaded += (_, _) =>
            {
                NewGame();
                // The scoreboard is public: it must load even if resolving the
                // local Windows account to a technician fails or times out.
                _ = LoadPlayerAsync();
                _ = RefreshLeaderboardAsync();
                Activate();
                Keyboard.Focus(this);
            };
            Deactivated += (_, _) =>
            {
                if (_started && !_gameOver && !_paused)
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
            _truckX = ShopX;
            _truckY = ShopY;
            _direction = 2;
            _wave = 0;
            _cash = 0;
            _repairs = 0;
            _truckCapacity = BaseTruckCapacity;
            _upgradeLevel = 0;
            _spareRadios = _truckCapacity;
            _started = false;
            _missedPriorities = 0;
            _secondsRemaining = 105;
            _lastTickSeconds = 0;
            _nextWaveAt = -1;
            _paused = false;
            _gameOver = false;
            _leaderboardVisible = false;
            _leaderboardAutoPaused = false;
            _radioMessage = "DISPATCH: Press SPACE to begin your shift.";
            _scoreUploadStatus = "Your score is saved after the shift.";
            _lastMoveUtc = DateTime.MinValue;
            _gameClock.Reset();
            _chopChopPlayedAt60 = false;
            _chopChopPlayedAt10 = false;
            _chopChopPlayer.Stop();
            BeginWave();
            _radioMessage = "PRESS SPACE TO START!";
            // Timer and stopwatch stay stopped until the player explicitly starts.
            _canvas.InvalidateVisual();
        }

        private void BeginWave()
        {
            _wave++;
            foreach (var site in _sites)
            {
                site.IsOffline = false;
                site.PriorityDeadline = null;
            }

            int targetCount = Math.Min(_sites.Count, 2 + _wave);
            var outages = _sites.OrderBy(_ => _random.Next()).Take(targetCount).ToList();
            foreach (var site in outages)
                site.IsOffline = true;

            // From wave 2 onwards, one site has an urgent repair deadline.
            // Miss the deadline and dispatch costs you 8 seconds, once only.
            if (_wave >= 2 && outages.Count > 0)
                outages[0].PriorityDeadline = _gameClock.Elapsed.TotalSeconds +
                    Math.Max(18, 38 - _wave * 1.5);

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
            double previousSecondsRemaining = _secondsRemaining;
            _secondsRemaining = Math.Max(0, _secondsRemaining - delta);

            if (_secondsRemaining <= 0)
            {
                CheckCountdownSound(previousSecondsRemaining);
                EndGame();
                return;
            }

            foreach (var site in _sites)
            {
                if (!site.IsOffline || !site.PriorityDeadline.HasValue ||
                    elapsed < site.PriorityDeadline.Value)
                    continue;

                site.PriorityDeadline = null;
                _missedPriorities++;
                _secondsRemaining = Math.Max(0, _secondsRemaining - 8);
                _radioMessage = "DISPATCH: PRIORITY MISSED. -8 SEC!";
            }

            // Includes both normal elapsed time and priority-miss penalties.
            // Check before a wave-clear time bonus can raise the clock again.
            CheckCountdownSound(previousSecondsRemaining);

            if (_secondsRemaining <= 0)
            {
                EndGame();
                return;
            }

            if (_nextWaveAt >= 0 && elapsed >= _nextWaveAt)
            {
                // Old +18 seconds enabled virtually endless play; +5 is scarce.
                _secondsRemaining = Math.Min(105, _secondsRemaining + 5);
                BeginWave();
            }

            _canvas.InvalidateVisual();
        }

        private void InitializeCountdownAudio()
        {
            // This WAV lives in the repo and is copied alongside the published client.
            var filePath = Path.Combine(
                AppContext.BaseDirectory, "Assets", "Sounds", "ChopChop.wav");

            if (!File.Exists(filePath))
            {
                Debug.WriteLine("Arcade countdown sound missing: " + filePath);
                return;
            }

            try
            {
                _chopChopPlayer.Volume = 0.85; // Only app playback volume.
                _chopChopPlayer.MediaFailed += (_, e) =>
                {
                    _chopChopAvailable = false;
                    Debug.WriteLine("Arcade countdown WAV failed: " + e.ErrorException);
                };
                _chopChopPlayer.Open(new Uri(filePath, UriKind.Absolute));
                _chopChopAvailable = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Arcade countdown WAV unavailable: " + ex);
                _chopChopAvailable = false;
            }
        }

        private void CheckCountdownSound(double previousSecondsRemaining)
        {
            // Flags are intentionally per GAME, not per wave. Re-crossing a
            // threshold after a +5-second bonus must never play it twice.
            if (!_chopChopPlayedAt60 &&
                previousSecondsRemaining > 60 && _secondsRemaining <= 60)
            {
                _chopChopPlayedAt60 = true;
                PlayChopChop();
            }

            if (!_chopChopPlayedAt10 &&
                previousSecondsRemaining > 10 && _secondsRemaining <= 10)
            {
                _chopChopPlayedAt10 = true;
                PlayChopChop();
            }
        }

        private void PlayChopChop()
        {
            if (!_chopChopAvailable)
                return;

            try
            {
                // Rewind the same recording for the second warning at 10 sec.
                _chopChopPlayer.Stop();
                _chopChopPlayer.Position = TimeSpan.Zero;
                _chopChopPlayer.Play();
            }
            catch (Exception ex)
            {
                _chopChopAvailable = false;
                Debug.WriteLine("Arcade countdown playback failed: " + ex);
            }
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

            if (e.Key == Key.L)
            {
                e.Handled = true;
                _leaderboardVisible = !_leaderboardVisible;
                if (_leaderboardVisible)
                {
                    _leaderboardAutoPaused = _started && !_paused && !_gameOver;
                    if (_leaderboardAutoPaused)
                        TogglePause();

                    _ = RefreshLeaderboardAsync();
                }
                else if (_leaderboardAutoPaused && !_gameOver)
                {
                    _leaderboardAutoPaused = false;
                    TogglePause();
                }

                _canvas.InvalidateVisual();
                return;
            }

            if (_leaderboardVisible)
            {
                e.Handled = true;
                return;
            }

            if (!_started && !_gameOver)
            {
                if (e.Key == Key.Space)
                {
                    e.Handled = true;
                    _started = true;
                    _radioMessage = "DISPATCH: First round of outages. Good luck!";
                    _gameClock.Start();
                    _timer.Start();
                    _canvas.InvalidateVisual();
                }
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
                    // One purchase per physical key press; holding Space must
                    // never accidentally buy multiple expensive upgrades.
                    if (!e.IsRepeat)
                        Interact();
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

        private void Interact()
        {
            if (_truckX == ShopX && _truckY == ShopY)
            {
                _spareRadios = _truckCapacity;
                _radioMessage = "SHOP: Restocked for free. Now get back to work!";
                _canvas.InvalidateVisual();
                return;
            }

            if (_truckX == GarageX && _truckY == GarageY)
            {
                BuyTruckUpgrade();
                return;
            }

            if (_spareRadios <= 0)
            {
                _radioMessage = "NO SPARES! RETURN TO THE SHOP AT START!";
                _canvas.InvalidateVisual();
                return;
            }

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
            site.PriorityDeadline = null;
            _spareRadios--;
            _repairs++;
            _cash += 100 + Math.Min(_wave - 1, 10) * 20;
            _radioMessage = _random.Next(4) switch
            {
                0 => "DISPATCH: Good copy. Next site!",
                1 => "TECH: Rebooted it. Works now.",
                2 => "DISPATCH: Can you take one more?",
                _ => "TECH: Put it on the work order."
            };

            if (_sites.All(s => !s.IsOffline))
            {
                _cash += 200;
                _radioMessage = "ALL LINKS RESTORED! +$200 / +5 SEC";
                _nextWaveAt = _gameClock.Elapsed.TotalSeconds + 1.5;
            }

            _canvas.InvalidateVisual();
        }

        private void BuyTruckUpgrade()
        {
            var price = NextUpgradePrice;
            if (!price.HasValue)
            {
                _radioMessage = "GARAGE: Truck is fully upgraded (12 radios)!";
            }
            else if (_cash < price.Value)
            {
                _radioMessage = $"GARAGE: Need ${price.Value:N0}. You have ${_cash:N0}.";
            }
            else
            {
                // Purchases directly reduce the player's cash/leaderboard score.
                _cash -= price.Value;
                _upgradeLevel++;
                _truckCapacity += CapacityPerUpgrade;
                _spareRadios = Math.Min(_truckCapacity, _spareRadios + CapacityPerUpgrade);
                _radioMessage = $"GARAGE: -${price.Value:N0}! Truck holds {_truckCapacity} radios.";
            }

            _canvas.InvalidateVisual();
        }

        private async Task LoadPlayerAsync()
        {
            try
            {
                var user = await CurrentUserService.LoadCurrentTechnicianAsync();
                if (!IsLoaded)
                    return;

                if (user != null && user.IsActive)
                {
                    _employeeId = user.EmployeeId;
                    _playerName = user.Name;
                }
                else
                {
                    _employeeId = null;
                    _playerName = "Guest (local only)";
                    _scoreUploadStatus = "No matching active technician; local scores only.";
                }
            }
            catch (Exception ex)
            {
                // Identity lookup failures must NOT stop public leaderboard GET.
                Debug.WriteLine("Arcade player lookup failed: " + ex);
                if (!IsLoaded)
                    return;

                _employeeId = null;
                _playerName = "Guest (local only)";
                _scoreUploadStatus = "Technician lookup failed; local scores only.";
            }

            _canvas.InvalidateVisual();
        }

        private static string GetLeaderboardErrorMessage(Exception ex) => ex switch
        {
            ApiClient.ApiException { StatusCode: 404 } =>
                "Leaderboard API missing (404). Deploy the arcade API.",
            ApiClient.ApiException { StatusCode: 500 } =>
                "Leaderboard server error (500). Check API logs / DB table.",
            ApiClient.ApiException apiEx =>
                $"Leaderboard API returned HTTP {apiEx.StatusCode}.",
            ApiClient.ApiConnectionException =>
                "Cannot reach the shop API. Check network/VPN.",
            _ => "Leaderboard failed to load. Check client logs."
        };

        private static string GetUploadErrorMessage(Exception ex) => ex switch
        {
            ApiClient.ApiException { StatusCode: 404 } =>
                "Score not saved (404). Check API and technician roster.",
            ApiClient.ApiException { StatusCode: 400 } =>
                "Score rejected (400). Check API validation.",
            ApiClient.ApiException { StatusCode: 500 } =>
                "Score not saved (500). Check API logs / DB table.",
            ApiClient.ApiException apiEx =>
                $"Score not saved (HTTP {apiEx.StatusCode}).",
            ApiClient.ApiConnectionException =>
                "Score not saved: cannot reach the shop API.",
            _ => "Score upload failed. Check client logs."
        };

        private async Task RefreshLeaderboardAsync()
        {
            _leaderboardStatus = "Loading shop leaderboard...";
            _canvas.InvalidateVisual();

            try
            {
                var response = await _arcadeApi.GetAsync<List<ArcadeLeaderboardEntryDto>>(
                    "api/arcade/leaderboard");

                if (!IsLoaded)
                    return;

                _leaderboard = response ?? new List<ArcadeLeaderboardEntryDto>();
                _leaderboardStatus = _leaderboard.Count == 0
                    ? "No shop scores yet. Finish a shift to submit one!"
                    : "Top 10 technicians by cash kept";
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Arcade leaderboard request failed: " + ex);
                if (!IsLoaded)
                    return;

                _leaderboardStatus = GetLeaderboardErrorMessage(ex);
            }

            if (IsLoaded)
                _canvas.InvalidateVisual();
        }

        private async Task SubmitScoreAsync()
        {
            if (string.IsNullOrWhiteSpace(_employeeId))
            {
                _scoreUploadStatus = "Not on tech roster: local score only.";
                _canvas.InvalidateVisual();
                return;
            }

            _scoreUploadStatus = "Uploading shop score...";
            _canvas.InvalidateVisual();

            try
            {
                await _arcadeApi.PostAsync<ArcadeScoreRequest, ArcadeLeaderboardEntryDto>(
                    "api/arcade/scores",
                    new ArcadeScoreRequest
                    {
                        EmployeeId = _employeeId,
                        Score = _cash,
                        Wave = _wave,
                        Repairs = _repairs
                    });

                if (!IsLoaded)
                    return;

                _scoreUploadStatus = "Score submitted to shop leaderboard!";
                await RefreshLeaderboardAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Arcade score submission failed: " + ex);
                if (!IsLoaded)
                    return;

                _scoreUploadStatus = GetUploadErrorMessage(ex);
                _canvas.InvalidateVisual();
            }
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
            _ = SubmitScoreAsync();
            if (_cash > _bestCash)
            {
                _bestCash = _cash;
                SaveHighScore(_cash);
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
            Text(dc, _playerName, 608, 30, 14, GreenBrush, pixelsPerDip, true);
            Text(dc, "A TOTALLY UNAUTHORIZED TRAINING SIMULATOR", 28, 69,
                12, DimBrush, pixelsPerDip);
            Text(dc, "S = RESTOCK   U = TRUCK UPGRADES ($)   GOLD = PRIORITY",
                28, 96, 13, GoldBrush, pixelsPerDip, true);

            DrawCity(dc, pixelsPerDip);
            DrawSidebar(dc, pixelsPerDip);

            dc.DrawRectangle(PanelBrush, new Pen(Brush("#335c85"), 1),
                new Rect(25, 537, 859, 43));
            Text(dc, "WASD / ARROWS  DRIVE", 39, 549, 13, WhiteBrush, pixelsPerDip, true);
            Text(dc, "SPACE  INTERACT", 268, 549, 13, GreenBrush, pixelsPerDip, true);
            Text(dc, "P  PAUSE", 461, 549, 13, GoldBrush, pixelsPerDip, true);
            Text(dc, "L  TOP 10", 597, 549, 13, CyanBrush, pixelsPerDip, true);
            Text(dc, "ESC  QUIT", 751, 549, 13, DimBrush, pixelsPerDip, true);

            if (!_started && !_gameOver)
                DrawStartOverlay(dc, pixelsPerDip);
            else if (_paused || _gameOver)
                DrawEndOverlay(dc, pixelsPerDip);

            // Draw last, including after Game Over, so standings stay visible.
            if (_leaderboardVisible)
                DrawLeaderboard(dc, pixelsPerDip);
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

            // Center of the map: the shop (S) and separate upgrade garage (U).
            double shopLeft = BoardLeft + ShopX * Cell;
            double shopTop = BoardTop + ShopY * Cell;
            dc.DrawRectangle(Brush("#18495e"), new Pen(CyanBrush, 2),
                new Rect(shopLeft + 1, shopTop + 1, Cell - 2, Cell - 2));
            Text(dc, "S", shopLeft + 10, shopTop + 6, 21,
                CyanBrush, pixelsPerDip, true);

            double garageLeft = BoardLeft + GarageX * Cell;
            double garageTop = BoardTop + GarageY * Cell;
            dc.DrawRectangle(Brush("#49325e"), new Pen(GoldBrush, 2),
                new Rect(garageLeft + 1, garageTop + 1, Cell - 2, Cell - 2));
            Text(dc, "U", garageLeft + 9, garageTop + 6, 21,
                GoldBrush, pixelsPerDip, true);

            DrawTruck(dc);
            DrawInventoryHud(dc, pixelsPerDip);
        }

        private void DrawInventoryHud(DrawingContext dc, double pixelsPerDip)
        {
            // Supplies belong IN the playable city rather than the side panel.
            double left = BoardLeft + 8;
            double top = BoardTop + 8;
            dc.DrawRectangle(Brush("#07172b"), new Pen(CyanBrush, 1.5),
                new Rect(left, top, 157, 37));
            Text(dc, $"RADIOS  {_spareRadios}/{_truckCapacity}",
                left + 11, top + 8, 17,
                _spareRadios == 0 ? RedBrush : WhiteBrush, pixelsPerDip, true);
        }

        private void DrawTower(DrawingContext dc, RadioSite site, double pixelsPerDip)
        {
            var x = BoardLeft + site.X * Cell;
            var y = BoardTop + site.Y * Cell;
            var glow = site.IsOffline ? RedBrush : GreenBrush;
            var pen = new Pen(glow, 2.2);
            var cx = x + Cell / 2;

            dc.DrawRectangle(Brush("#071123"),
                new Pen(site.PriorityDeadline.HasValue ? GoldBrush : glow, site.IsOffline ? 2 : 1),
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
            Text(dc, "CASH / SCORE", x + 154, 174, 12, DimBrush, pixelsPerDip, true);
            Text(dc, "$" + _cash.ToString("N0", CultureInfo.InvariantCulture),
                x + 154, 198, 21, GreenBrush, pixelsPerDip, true);

            Text(dc, "WAVE " + _wave, x + 15, 251, 16, GreenBrush, pixelsPerDip, true);
            Text(dc, "TRUCK " + _truckCapacity + "-RADIO", x + 120, 253, 13,
                GoldBrush, pixelsPerDip, true);

            dc.DrawLine(new Pen(Brush("#335c85"), 1),
                new Point(x + 14, 283), new Point(x + 262, 283));

            Text(dc, "OFFLINE SITES", x + 15, 301, 15, RedBrush, pixelsPerDip, true);
            var offline = _sites.Where(s => s.IsOffline).ToList();
            for (int i = 0; i < Math.Min(5, offline.Count); i++)
            {
                var site = offline[i];
                var priority = site.PriorityDeadline.HasValue;
                Text(dc, (priority ? "!  " : "■  ") + site.Code, x + 18, 328 + i * 21,
                    14, priority ? GoldBrush : RedBrush, pixelsPerDip, true);
                if (priority)
                    Text(dc, Math.Max(0, (int)Math.Ceiling(
                            site.PriorityDeadline.Value - _gameClock.Elapsed.TotalSeconds)) + "s",
                        x + 171, 328 + i * 21, 14, GoldBrush, pixelsPerDip, true);
            }

            if (offline.Count > 5)
                Text(dc, "+ " + (offline.Count - 5) + " more", x + 18,
                    434, 12, DimBrush, pixelsPerDip);

            dc.DrawLine(new Pen(Brush("#335c85"), 1),
                new Point(x + 14, 453), new Point(x + 262, 453));

            Text(dc, "UPGRADE GARAGE", x + 15, 456, 14, GoldBrush, pixelsPerDip, true);
            Text(dc, NextUpgradePrice.HasValue
                    ? $"U SQUARE: ${NextUpgradePrice.Value:N0} FOR +2 SPARES"
                    : "U SQUARE: MAX CAPACITY",
                x + 15, 477, 12, WhiteBrush, pixelsPerDip, false, 249);
            Text(dc, _radioMessage, x + 15, 504, 12,
                CyanBrush, pixelsPerDip, false, 248);
        }

        private void DrawLeaderboard(DrawingContext dc, double pixelsPerDip)
        {
            dc.DrawRectangle(Brush("#081326"), new Pen(CyanBrush, 2),
                new Rect(105, 92, 726, 433));
            Text(dc, "SHOP HIGH SCORES — CASH KEPT", 128, 110, 26,
                CyanBrush, pixelsPerDip, true);
            Text(dc, _leaderboardStatus, 128, 149, 14,
                DimBrush, pixelsPerDip);

            int position = 0;
            foreach (var item in _leaderboard.Take(10))
            {
                var y = 187 + position * 30;
                Text(dc, item.Rank.ToString("00") + ".  " + item.TechnicianName,
                    131, y, 17, item.Rank == 1 ? GoldBrush : WhiteBrush,
                    pixelsPerDip, true, 440);
                Text(dc, "$" + item.Score.ToString("N0", CultureInfo.InvariantCulture),
                    645, y, 17, GreenBrush, pixelsPerDip, true);
                position++;
            }
            Text(dc, "L TO RETURN TO GAME", 132, 487, 14,
                DimBrush, pixelsPerDip, true);
        }

        private void DrawStartOverlay(DrawingContext dc, double pixelsPerDip)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(243, 5, 11, 27)),
                new Pen(CyanBrush, 3), new Rect(123, 167, 676, 305));

            Text(dc, "RADIO SHOP ARCADE", 157, 193, 31,
                CyanBrush, pixelsPerDip, true);
            Text(dc, "RUN THE SHOP. FIX RADIOS. MAKE MONEY.", 158, 248, 17,
                WhiteBrush, pixelsPerDip, true);
            Text(dc, "Repairs earn cash. Return to S for spares.", 158, 284, 16,
                DimBrush, pixelsPerDip);
            Text(dc, "Buy bigger trucks at U; upgrades CUT your score.", 158, 310, 16,
                GoldBrush, pixelsPerDip);
            Text(dc, "PRESS SPACE TO START", 158, 385, 25,
                GreenBrush, pixelsPerDip, true);
            Text(dc, "L TO VIEW SHOP LEADERBOARD    ESC TO QUIT", 158, 430, 13,
                DimBrush, pixelsPerDip, true);
        }

        private void DrawEndOverlay(DrawingContext dc, double pixelsPerDip)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(230, 5, 11, 27)),
                new Pen(CyanBrush, 3), new Rect(133, 184, 660, 262));
            Text(dc, _gameOver ? "END OF SHIFT" : "RADIO SILENCE — PAUSED",
                165, 222, 31, _gameOver ? GoldBrush : CyanBrush, pixelsPerDip, true);

            if (_gameOver)
            {
                Text(dc, "RADIOS FIXED: " + _repairs + "    CASH: $" + _cash.ToString("N0", CultureInfo.InvariantCulture),
                    166, 289, 19, WhiteBrush, pixelsPerDip, true);
                Text(dc, _cash >= _bestCash && _cash > 0
                        ? "NEW PERSONAL HIGH SCORE!"
                        : "DISPATCH: SEE YOU TOMORROW.",
                    166, 329, 17, GreenBrush, pixelsPerDip);
                Text(dc, _scoreUploadStatus, 166, 363, 13,
                    CyanBrush, pixelsPerDip, false, 555);
                Text(dc, "R TO TRY AGAIN     L FOR TOP 10     ESC TO QUIT",
                    166, 404, 15, DimBrush, pixelsPerDip, true);
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
                "SmartGridSuite", "Arcade", "highscore-v3-cash.txt");

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
            _chopChopPlayer.Stop();
            _chopChopPlayer.Close();
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
            public double? PriorityDeadline { get; set; }
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
