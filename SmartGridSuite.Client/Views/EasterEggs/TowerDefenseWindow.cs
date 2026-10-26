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
    /// Entirely fictional, standalone arcade simulation. No real towers,
    /// communications, radio commands, tickets, or operational APIs are used.
    /// </summary>
    public sealed class TowerDefenseWindow : Window
    {
        private const double FieldX = 27;
        private const double FieldY = 126;
        private const double FieldWidth = 544;
        private const double FieldHeight = 388;
        private const double TowerX = FieldX + FieldWidth / 2;
        private const double TowerY = FieldY + FieldHeight / 2;
        private const double HubRadius = 31;
        private const int MaxUpgradeLevel = 3;
        private static readonly CultureInfo MoneyCulture = CultureInfo.InvariantCulture;

        private static TowerDefenseWindow? _active;
        private static readonly Brush Background = Paint("#081224");
        private static readonly Brush Panel = Paint("#11233D");
        private static readonly Brush Ground = Paint("#0C243B");
        private static readonly Brush GridInk = Paint("#234159");
        private static readonly Brush Cyan = Paint("#5CE8FA");
        private static readonly Brush Green = Paint("#66F3B8");
        private static readonly Brush Red = Paint("#FF6889");
        private static readonly Brush Gold = Paint("#F9DA79");
        private static readonly Brush Purple = Paint("#CD9BFF");
        private static readonly Brush White = Paint("#F2F7FF");
        private static readonly Brush Muted = Paint("#A0B5D3");
        private static readonly Brush FenceInk = Paint("#9DCBC5");
        private static readonly Brush TowerSteel = Paint("#B5C5D8");
        private static readonly Brush DarkSteel = Paint("#526E86");
        private static readonly Brush ButtonFace = Paint("#1B3350");
        private static readonly Brush Tree = Paint("#12334B");
        private static readonly Brush Copper = Paint("#DDA36B");
        private static readonly Brush RedFlash = new SolidColorBrush(Color.FromArgb(105, 255, 92, 113));
        private static readonly Brush PulseFill = new SolidColorBrush(Color.FromArgb(35, 92, 232, 250));

        private readonly Window _host;
        private readonly GameSurface _surface;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _clock = new();
        private readonly Random _random = new();
        private readonly List<CopperThief> _thieves = new();
        private readonly List<InterferenceTower> _jammers = new();

        private int _cash;
        private int _score;
        private int _bestScore;
        private int _wave;
        private int _thievesStopped;
        private int _interferenceRemoved;
        private int _lightningLevel;
        private int _surgeLevel;
        private int _fenceLevel;
        private int _filterLevel;
        private double _health;
        private double _fenceHealth;
        private double _lastTickTime;
        private double _spawnThiefIn;
        private double _spawnJammerIn;
        private double _spawnLightningIn;
        private double _warningRemaining = -1;
        private double _flashRemaining;
        private double _clickCooldown;
        private int _thievesToSpawn;
        private int _jammersToSpawn;
        private int _lightningToSpawn;
        private bool _started;
        private bool _waveActive;
        private bool _paused;
        private bool _gameOver;
        private bool _showHelp;
        private bool _helpAutoPaused;
        private string _message = "PRESS SPACE TO DEFEND YOUR TOWER.";

        private TowerDefenseWindow(Window host)
        {
            _host = host;
            Owner = host;
            Title = "Grid Under Attack — 360° Tower Defense";
            Width = 940;
            Height = 650;
            MinWidth = Width;
            MinHeight = Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Background;
            FontFamily = new FontFamily("Consolas");
            Focusable = true;

            _surface = new GameSurface(this);
            Content = _surface;
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            _timer.Tick += Tick;
            PreviewKeyDown += HandleKey;
            PreviewMouseLeftButtonDown += HandleClick;

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
                if (_started && !_paused && !_gameOver)
                    SetPaused(true);
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
                var window = new TowerDefenseWindow(host);
                _active = window;
                window.Show();
                window.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Tower Defense failed to open: " + ex);
                _active = null;
            }
        }

        private void NewGame()
        {
            _timer.Stop();
            _clock.Reset();
            _cash = 450;
            _score = 0;
            _wave = 0;
            _thievesStopped = 0;
            _interferenceRemoved = 0;
            _health = 100;
            _fenceHealth = 0;
            _lightningLevel = 0;
            _surgeLevel = 0;
            _fenceLevel = 0;
            _filterLevel = 0;
            _thievesToSpawn = 0;
            _jammersToSpawn = 0;
            _lightningToSpawn = 0;
            _lastTickTime = 0;
            _warningRemaining = -1;
            _flashRemaining = 0;
            _clickCooldown = 0;
            _paused = false;
            _started = false;
            _waveActive = false;
            _gameOver = false;
            _showHelp = false;
            _helpAutoPaused = false;
            _message = "PRESS SPACE TO DEFEND YOUR TOWER.";
            _thieves.Clear();
            _jammers.Clear();
            _bestScore = ReadBestScore();
            _surface.InvalidateVisual();
        }

        private void StartWave()
        {
            if (!_started || _paused || _gameOver || _waveActive)
                return;

            _wave++;
            _thievesToSpawn = Math.Min(36, 3 + _wave * 2);
            _jammersToSpawn = Math.Min(5, 1 + (_wave - 1) / 3);
            _lightningToSpawn = _wave <= 1 ? 0 : Math.Min(4, 1 + (_wave - 2) / 4);
            _spawnThiefIn = 0.8;
            _spawnJammerIn = 2.4;
            _spawnLightningIn = 4.8;
            _waveActive = true;
            _message = "WAVE " + _wave + ": TROUBLE IN EVERY DIRECTION!";
            _surface.InvalidateVisual();
        }

        private void Tick(object? sender, EventArgs e)
        {
            if (!_started || _paused || _gameOver)
                return;

            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(now - _lastTickTime, 0, 0.1);
            _lastTickTime = now;
            _clickCooldown = Math.Max(0, _clickCooldown - dt);
            _flashRemaining = Math.Max(0, _flashRemaining - dt);

            if (!_waveActive)
            {
                _surface.InvalidateVisual();
                return;
            }

            if (_thievesToSpawn > 0)
            {
                _spawnThiefIn -= dt;
                if (_spawnThiefIn <= 0)
                {
                    SpawnCopperThief();
                    _thievesToSpawn--;
                    _spawnThiefIn = Math.Max(0.35, 1.5 - _wave * 0.055);
                }
            }

            if (_jammersToSpawn > 0)
            {
                _spawnJammerIn -= dt;
                if (_spawnJammerIn <= 0)
                {
                    SpawnInterferenceTower();
                    _jammersToSpawn--;
                    _spawnJammerIn = Math.Max(2.5, 6.8 - _wave * 0.20);
                }
            }

            if (_warningRemaining > 0)
            {
                _warningRemaining -= dt;
                if (_warningRemaining <= 0)
                    LightningStrike();
            }
            else if (_lightningToSpawn > 0)
            {
                _spawnLightningIn -= dt;
                if (_spawnLightningIn <= 0)
                {
                    _lightningToSpawn--;
                    _warningRemaining = 2.5;
                    _spawnLightningIn = Math.Max(3.0, 8.3 - _wave * 0.15);
                    _message = "LIGHTNING INCOMING! ARRESTORS BETTER BE INSTALLED!";
                }
            }

            for (int i = _thieves.Count - 1; i >= 0; i--)
            {
                var thief = _thieves[i];
                thief.Distance = Math.Max(0, thief.Distance - thief.Speed * dt);

                // A functioning fence automatically intercepts thieves.
                // Each collision damages the fence and sends the thief fleeing.
                if (_fenceLevel > 0 && _fenceHealth > 0 &&
                    thief.Distance <= FenceRadius)
                {
                    _fenceHealth = Math.Max(0, _fenceHealth - Math.Max(1, 4 - _fenceLevel));
                    _thieves.RemoveAt(i);
                    _thievesStopped++;
                    _score += 12;
                    if (_fenceHealth <= 0)
                        _message = "THE FENCE IS DOWN! REPAIR IT WITH 5!";
                }
                else if (thief.Distance <= HubRadius)
                {
                    _health -= 6 + _wave * 0.6;
                    _thieves.RemoveAt(i);
                    _message = "COPPER THIEVES GOT INTO THE TOWER COMPOUND!";
                }
            }

            // Interference towers are planted nearby and continuously drain
            // site integrity until the player clicks to remove them.
            if (_jammers.Count > 0)
            {
                double filterMultiplier = 1.0 - 0.27 * _filterLevel;
                _health -= _jammers.Count *
                    (1.05 + 0.10 * _wave) * filterMultiplier * dt;
            }

            if (_health <= 0)
            {
                _health = 0;
                EndGame();
                return;
            }

            if (_thievesToSpawn == 0 && _jammersToSpawn == 0 &&
                _lightningToSpawn == 0 && _warningRemaining <= 0 &&
                _thieves.Count == 0 && _jammers.Count == 0)
            {
                EndWave();
            }

            _surface.InvalidateVisual();
        }

        private void SpawnCopperThief()
        {
            double angle = _random.NextDouble() * Math.PI * 2;
            double radius = BoundaryRadius(angle, 18);
            _thieves.Add(new CopperThief
            {
                Angle = angle,
                Distance = radius,
                Speed = 22 + _wave * 2.2 + _random.NextDouble() * 9
            });
        }

        private void SpawnInterferenceTower()
        {
            double angle = _random.NextDouble() * Math.PI * 2;
            double radius = Math.Min(
                BoundaryRadius(angle, 30) - 6,
                102 + _random.NextDouble() * 48);
            _jammers.Add(new InterferenceTower
            {
                X = TowerX + Math.Cos(angle) * radius,
                Y = TowerY + Math.Sin(angle) * radius
            });
            _message = "ILLEGAL INTERFERENCE TOWER DETECTED! CLICK TO REMOVE!";
        }

        private static double BoundaryRadius(double angle, double inset)
        {
            double cos = Math.Abs(Math.Cos(angle));
            double sin = Math.Abs(Math.Sin(angle));
            return Math.Min((FieldWidth / 2 - inset) / Math.Max(0.0001, cos),
                (FieldHeight / 2 - inset) / Math.Max(0.0001, sin));
        }

        private static Point ThiefPoint(CopperThief thief) =>
            new(TowerX + Math.Cos(thief.Angle) * thief.Distance,
                TowerY + Math.Sin(thief.Angle) * thief.Distance);

        private double FenceRadius => 58 + _fenceLevel * 7;

        private void LightningStrike()
        {
            _warningRemaining = -1;
            _flashRemaining = 0.43;

            double damage = (10 + _wave * 1.4)
                * (1 - 0.27 * _lightningLevel)
                * (1 - 0.22 * _surgeLevel);
            damage = Math.Max(0.5, damage);
            _health -= damage;
            _message = "LIGHTNING STRIKE! -" +
                damage.ToString("0.0", MoneyCulture) + " TOWER HP.";

            if (_health <= 0)
            {
                _health = 0;
                EndGame();
            }
        }

        private void EndWave()
        {
            _waveActive = false;
            int reward = 90 + _wave * 45;
            _cash += reward;
            _score += 90 * _wave + (int)Math.Ceiling(_health);
            _message = "WAVE " + _wave + " SURVIVED! +$" +
                reward.ToString(MoneyCulture) + ". SPACE FOR NEXT WAVE.";
        }

        private void EndGame()
        {
            _gameOver = true;
            _timer.Stop();
            _clock.Stop();
            _message = "RADIO SILENCE. DISPATCH IS NOT HAPPY.";
            if (_score > _bestScore)
            {
                _bestScore = _score;
                SaveBestScore(_score);
            }
            _surface.InvalidateVisual();
        }

        private void SetPaused(bool paused)
        {
            if (_paused == paused)
                return;
            _paused = paused;
            if (paused)
                _clock.Stop();
            else
                _clock.Start();
            // Account for stopwatch stopping without a large simulation jump.
            _lastTickTime = _clock.Elapsed.TotalSeconds;
            _surface.InvalidateVisual();
        }

        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }

            if (_gameOver && e.Key == Key.R)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    NewGame();
                return;
            }

            if (e.Key == Key.H)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    ToggleHelp();
                return;
            }

            if (_showHelp)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                e.Handled = true;
                if (e.IsRepeat || _gameOver)
                    return;

                if (!_started)
                {
                    _started = true;
                    _clock.Restart();
                    _lastTickTime = 0;
                    _timer.Start();
                    _message = "PREPARE YOUR TOWER. BUY PROTECTION, THEN SPACE FOR WAVE 1.";
                    _surface.InvalidateVisual();
                }
                else if (!_paused && !_waveActive)
                    StartWave();
                return;
            }

            if (e.Key == Key.P && _started && !_gameOver)
            {
                e.Handled = true;
                SetPaused(!_paused);
                return;
            }

            if (!_started || _paused || _gameOver)
                return;

            int option = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 1,
                Key.D2 or Key.NumPad2 => 2,
                Key.D3 or Key.NumPad3 => 3,
                Key.D4 or Key.NumPad4 => 4,
                Key.D5 or Key.NumPad5 => 5,
                _ => 0
            };

            if (option > 0)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    Purchase(option);
            }
        }

        private void ToggleHelp()
        {
            _showHelp = !_showHelp;
            if (_showHelp)
            {
                _helpAutoPaused = _started && !_paused && !_gameOver;
                if (_helpAutoPaused)
                    SetPaused(true);
            }
            else if (_helpAutoPaused && !_gameOver)
            {
                _helpAutoPaused = false;
                SetPaused(false);
            }
            _surface.InvalidateVisual();
        }

        private void HandleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_started || _paused || _gameOver || _showHelp)
                return;

            Point p = e.GetPosition(_surface);
            if (p.X >= FieldX && p.X <= FieldX + FieldWidth &&
                p.Y >= FieldY && p.Y <= FieldY + FieldHeight)
            {
                e.Handled = true;
                if (_clickCooldown > 0)
                    return;

                var nearestInterference = _jammers
                    .Where(j => Distance(p, new Point(j.X, j.Y)) <= 19)
                    .OrderBy(j => Distance(p, new Point(j.X, j.Y)))
                    .FirstOrDefault();

                if (nearestInterference != null)
                {
                    _jammers.Remove(nearestInterference);
                    _interferenceRemoved++;
                    _cash += 45;
                    _score += 65;
                    _message = "INTERFERENCE TOWER DISMANTLED! +$45.";
                    _clickCooldown = 0.22;
                    _surface.InvalidateVisual();
                    return;
                }

                var nearestThief = _thieves
                    .Where(t => Distance(p, ThiefPoint(t)) <= 17)
                    .OrderBy(t => Distance(p, ThiefPoint(t)))
                    .FirstOrDefault();

                if (nearestThief != null)
                {
                    _thieves.Remove(nearestThief);
                    _thievesStopped++;
                    _cash += 25;
                    _score += 40;
                    _message = "COPPER THIEF CHASED OFF! +$25.";
                    _clickCooldown = 0.22;
                    _surface.InvalidateVisual();
                }
                return;
            }

            // The sidebar buttons are also mouse-clickable.
            if (p.X >= 620 && p.X <= 876)
            {
                int option = p.Y switch
                {
                    >= 269 and <= 305 => 1,
                    >= 316 and <= 352 => 2,
                    >= 363 and <= 399 => 3,
                    >= 410 and <= 446 => 4,
                    >= 457 and <= 493 => 5,
                    _ => 0
                };

                if (option != 0)
                {
                    e.Handled = true;
                    Purchase(option);
                }
            }
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static readonly int[][] UpgradePrices =
        {
            new[] { 180, 350, 600 }, // lightning arrestor
            new[] { 160, 280, 460 }, // surge arrestor
            new[] { 220, 420, 650 }, // security fencing
            new[] { 200, 390, 640 }  // RF filtering
        };

        private int LevelFor(int option) => option switch
        {
            1 => _lightningLevel,
            2 => _surgeLevel,
            3 => _fenceLevel,
            4 => _filterLevel,
            _ => 0
        };

        private int? NextPrice(int option)
        {
            if (option == 5)
                return _fenceLevel > 0 ? 90 : null;

            int level = LevelFor(option);
            return level >= MaxUpgradeLevel ? null : UpgradePrices[option - 1][level];
        }

        private void Purchase(int option)
        {
            int? price = NextPrice(option);
            if (!price.HasValue)
            {
                _message = option == 5
                    ? "INSTALL A FENCE BEFORE CALLING FOR REPAIRS."
                    : "THAT SYSTEM IS ALREADY AT MAX LEVEL.";
                _surface.InvalidateVisual();
                return;
            }

            if (option == 5 && _fenceHealth >= FenceMaxHealth)
            {
                _message = "YOUR FENCE IS ALREADY FULLY REPAIRED.";
                _surface.InvalidateVisual();
                return;
            }

            if (_cash < price.Value)
            {
                _message = "NEED $" + price.Value.ToString(MoneyCulture) +
                    ". CURRENT CASH: $" + _cash.ToString(MoneyCulture) + ".";
                _surface.InvalidateVisual();
                return;
            }

            _cash -= price.Value;
            switch (option)
            {
                case 1:
                    _lightningLevel++;
                    _message = "LIGHTNING ARRESTOR UPGRADED TO LVL " + _lightningLevel + ".";
                    break;
                case 2:
                    _surgeLevel++;
                    _message = "SURGE ARRESTOR UPGRADED TO LVL " + _surgeLevel + ".";
                    break;
                case 3:
                    _fenceLevel++;
                    _fenceHealth = FenceMaxHealth;
                    _message = "SECURITY FENCE UPGRADED TO LVL " + _fenceLevel + ".";
                    break;
                case 4:
                    _filterLevel++;
                    _message = "RF INTERFERENCE FILTER UPGRADED TO LVL " + _filterLevel + ".";
                    break;
                case 5:
                    _fenceHealth = FenceMaxHealth;
                    _message = "FENCE REPAIRED. DISPATCH WANTS A WORK ORDER.";
                    break;
            }
            _surface.InvalidateVisual();
        }

        private double FenceMaxHealth => _fenceLevel == 0 ? 0 : 12 + _fenceLevel * 15;

        private void Draw(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Background, null, new Rect(0, 0, 940, 650));
            dc.DrawRectangle(Panel, new Pen(GridInk, 2), new Rect(12, 12, 894, 580));
            Text(dc, "GRID UNDER ATTACK", 27, 23, 29, Cyan, dpi, true);
            Text(dc, "360 DEGREE RADIO TOWER DEFENSE", 28, 64, 13, Muted, dpi);
            Text(dc, _message, 28, 97, 12, Gold, dpi, true, 858);

            DrawField(dc, dpi);
            DrawSidebar(dc, dpi);

            dc.DrawRectangle(Panel, new Pen(GridInk, 1), new Rect(25, 539, 859, 42));
            Text(dc, "CLICK THIEVES + INTERFERENCE TOWERS TO STOP THEM",
                39, 551, 12, White, dpi, true);
            Text(dc, "1-5 BUY  |  SPACE NEXT WAVE  |  H HELP  |  P PAUSE",
                462, 551, 11, Cyan, dpi, true, 400);

            if (!_started || _paused || _gameOver)
                DrawOverlay(dc, dpi);
            if (_showHelp)
                DrawHelp(dc, dpi);
        }

        private void DrawField(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Ground, new Pen(Cyan, 2),
                new Rect(FieldX - 2, FieldY - 2, FieldWidth + 4, FieldHeight + 4));

            for (int y = 0; y < 12; y++)
            for (int x = 0; x < 17; x++)
            {
                double xx = FieldX + x * 32, yy = FieldY + y * 32;
                dc.DrawRectangle(Ground, new Pen(GridInk, 0.5),
                    new Rect(xx, yy, 32, 32));
                if ((x * 13 + y * 19) % 7 == 0)
                    dc.DrawEllipse(Tree, null, new Point(xx + 10, yy + 12), 4, 3);
            }

            if (_fenceLevel > 0)
            {
                dc.DrawEllipse(null,
                    new Pen(_fenceHealth > 0 ? FenceInk : Red, 3.0),
                    new Point(TowerX, TowerY), FenceRadius, FenceRadius);

                // Fence posts around the perimeter telegraph the defense upgrade.
                for (int post = 0; post < 16 + _fenceLevel * 4; post++)
                {
                    double angle = Math.PI * 2 * post / (16 + _fenceLevel * 4);
                    dc.DrawRectangle(_fenceHealth > 0 ? FenceInk : Red, null,
                        new Rect(TowerX + Math.Cos(angle) * FenceRadius - 2,
                            TowerY + Math.Sin(angle) * FenceRadius - 2, 4, 4));
                }
            }

            // The one structure the entire game is about: a central radio tower
            // with lattice steel legs, antenna, sector panels and red beacon.
            var towerBottom = TowerY + 31;
            var towerTop = TowerY - 56;
            dc.DrawEllipse(PulseFill, new Pen(Cyan, 0.7),
                new Point(TowerX, TowerY), 32, 32);
            var steel = new Pen(TowerSteel, 3);
            var cross = new Pen(DarkSteel, 1.5);
            dc.DrawLine(steel, new Point(TowerX, towerTop),
                new Point(TowerX - 25, towerBottom));
            dc.DrawLine(steel, new Point(TowerX, towerTop),
                new Point(TowerX + 25, towerBottom));
            dc.DrawLine(steel, new Point(TowerX - 25, towerBottom),
                new Point(TowerX + 25, towerBottom));
            for (int n = 0; n < 4; n++)
            {
                double yy = towerTop + 18 + n * 17;
                double half = 5 + n * 5.0;
                dc.DrawLine(cross, new Point(TowerX - half, yy),
                    new Point(TowerX + half, yy + 12));
                dc.DrawLine(cross, new Point(TowerX + half, yy),
                    new Point(TowerX - half, yy + 12));
            }
            dc.DrawLine(new Pen(Cyan, 2.4),
                new Point(TowerX, towerTop - 15), new Point(TowerX, towerTop));
            dc.DrawRectangle(White, null,
                new Rect(TowerX - 14, towerTop + 14, 8, 20));
            dc.DrawRectangle(White, null,
                new Rect(TowerX + 6, towerTop + 19, 8, 20));
            dc.DrawEllipse(Red, new Pen(White, 1),
                new Point(TowerX, towerTop - 16), 4, 4);

            if (_lightningLevel > 0)
                Text(dc, "L" + _lightningLevel, TowerX + 30, TowerY - 50,
                    13, Gold, dpi, true);
            if (_surgeLevel > 0)
                Text(dc, "S" + _surgeLevel, TowerX + 30, TowerY - 30,
                    13, Green, dpi, true);
            if (_filterLevel > 0)
            {
                for (int ring = 0; ring < _filterLevel; ring++)
                    dc.DrawEllipse(null, new Pen(Purple, 0.9),
                        new Point(TowerX, TowerY),
                        84 + ring * 10, 84 + ring * 10);
            }

            for (int i = 0; i < _jammers.Count; i++)
            {
                var jammer = _jammers[i];
                var p = new Point(jammer.X, jammer.Y);
                dc.DrawEllipse(null, new Pen(Purple, 2.3), p, 22, 22);
                dc.DrawRectangle(Panel, new Pen(Purple, 2),
                    new Rect(p.X - 9, p.Y - 11, 18, 23));
                dc.DrawLine(new Pen(Purple, 2),
                    new Point(p.X, p.Y - 10), new Point(p.X, p.Y - 24));
                dc.DrawEllipse(Purple, null,
                    new Point(p.X, p.Y - 25), 3.5, 3.5);
                Text(dc, "RF", p.X - 10, p.Y - 5, 12, White, dpi, true);
            }

            for (int i = 0; i < _thieves.Count; i++)
            {
                var thief = _thieves[i];
                var p = ThiefPoint(thief);
                // Copper-colored thief carrying an oversized set of cutters.
                dc.DrawEllipse(Copper, null, new Point(p.X, p.Y - 7), 5, 5);
                dc.DrawLine(new Pen(Red, 4),
                    new Point(p.X, p.Y - 1), new Point(p.X, p.Y + 9));
                dc.DrawLine(new Pen(Copper, 2.4),
                    new Point(p.X - 7, p.Y + 2), new Point(p.X + 7, p.Y + 2));
                dc.DrawLine(new Pen(Gold, 2),
                    new Point(p.X + 7, p.Y + 2), new Point(p.X + 13, p.Y - 6));
                dc.DrawLine(new Pen(DarkSteel, 2),
                    new Point(p.X, p.Y + 9), new Point(p.X - 5, p.Y + 15));
                dc.DrawLine(new Pen(DarkSteel, 2),
                    new Point(p.X, p.Y + 9), new Point(p.X + 5, p.Y + 15));
            }

            if (_warningRemaining > 0)
            {
                dc.DrawEllipse(null, new Pen(Gold, 3),
                    new Point(TowerX, TowerY), 42, 42);
                Text(dc, "LIGHTNING " + _warningRemaining.ToString("0.0", MoneyCulture) + "s",
                    TowerX - 89, TowerY + 77, 15, Gold, dpi, true);
            }
            if (_flashRemaining > 0)
            {
                dc.DrawRectangle(RedFlash, null,
                    new Rect(FieldX, FieldY, FieldWidth, FieldHeight));
                var bolt = new Pen(White, 5);
                var x = TowerX;
                var y = FieldY;
                for (int step = 0; step < 6; step++)
                {
                    var nx = x + (step % 2 == 0 ? 22 : -19);
                    var ny = y + (towerTop - FieldY) / 6.0;
                    dc.DrawLine(bolt, new Point(x, y), new Point(nx, ny));
                    x = nx;
                    y = ny;
                }
                dc.DrawLine(bolt, new Point(x, y),
                    new Point(TowerX, towerTop - 10));
            }
        }

        private void DrawSidebar(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Panel, new Pen(GridInk, 1),
                new Rect(607, FieldY, 277, FieldHeight));
            Text(dc, "TOWER STATUS", 621, 138, 18, Cyan, dpi, true);
            Text(dc, "TOWER HP", 621, 173, 12, Muted, dpi);
            Text(dc, Math.Ceiling(_health).ToString("000", MoneyCulture),
                621, 190, 27, _health < 28 ? Red : Green, dpi, true);
            Text(dc, "CASH", 752, 173, 12, Muted, dpi);
            Text(dc, "$" + _cash.ToString("N0", MoneyCulture),
                748, 193, 20, Gold, dpi, true);

            Text(dc, "WAVE " + _wave + (_waveActive ? " LIVE" : " READY"),
                621, 228, 14, White, dpi, true);
            Text(dc, "THIEVES " + (_thieves.Count + _thievesToSpawn) +
                "   RF " + (_jammers.Count + _jammersToSpawn),
                621, 246, 12, Purple, dpi, true);

            DrawUpgrade(dc, 1, "LIGHTNING ARRESTOR", 269, Gold, dpi);
            DrawUpgrade(dc, 2, "SURGE ARRESTOR", 316, Green, dpi);
            DrawUpgrade(dc, 3, "SECURITY FENCE", 363, Cyan, dpi);
            DrawUpgrade(dc, 4, "RF FILTERS", 410, Purple, dpi);
            DrawUpgrade(dc, 5, "REPAIR FENCE", 457, White, dpi);

            if (_fenceLevel > 0)
                Text(dc, "FENCE " + Math.Ceiling(_fenceHealth).ToString("0", MoneyCulture) +
                    "/" + FenceMaxHealth.ToString("0", MoneyCulture),
                    624, 496, 12, _fenceHealth > 0 ? Green : Red, dpi, true);
        }

        private void DrawUpgrade(DrawingContext dc, int option,
            string name, double y, Brush ink, double dpi)
        {
            dc.DrawRectangle(ButtonFace, new Pen(ink, 1),
                new Rect(620, y, 250, 36));
            string label = option == 5
                ? name
                : name + " " + LevelFor(option) + "/" + MaxUpgradeLevel;
            string price = NextPrice(option).HasValue
                ? "$" + NextPrice(option)!.Value.ToString("N0", MoneyCulture)
                : "MAX";
            if (option == 5 && !NextPrice(option).HasValue)
                price = "N/A";

            Text(dc, option + "  " + label, 628, y + 9, 12.5, ink, dpi, true, 191);
            Text(dc, price, 816, y + 9, 12.5, White, dpi, true, 54);
        }

        private void DrawOverlay(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(241, 5, 13, 29)),
                new Pen(Cyan, 2.5), new Rect(103, 164, 709, 307));
            Text(dc, _gameOver ? "TOWER DESTROYED" :
                _paused ? "RADIO SILENCE — PAUSED" : "GRID UNDER ATTACK",
                151, 191, 29, _gameOver ? Red : Cyan, dpi, true);

            if (!_started)
            {
                Text(dc, "ONE RADIO TOWER. TROUBLE FROM EVERY DIRECTION.",
                    151, 246, 16, White, dpi, true);
                Text(dc, "COPPER THIEVES • ILLEGAL INTERFERENCE • LIGHTNING",
                    151, 280, 15, Red, dpi);
                Text(dc, "BUY ARRESTORS, FENCES AND RF FILTERS TO SURVIVE.",
                    151, 315, 15, Gold, dpi);
                Text(dc, "PRESS SPACE TO PREPARE", 151, 379, 24, Green, dpi, true);
                Text(dc, "SPACE AGAIN TO RELEASE WAVE 1", 151, 428, 15, Muted, dpi);
            }
            else if (_gameOver)
            {
                Text(dc, "WAVE " + _wave + "  •  SCORE " +
                    _score.ToString("N0", MoneyCulture),
                    151, 279, 20, White, dpi, true);
                Text(dc, "PERSONAL BEST " + _bestScore.ToString("N0", MoneyCulture),
                    151, 321, 19, Gold, dpi, true);
                Text(dc, "R TO RETRY      ESC BACK TO ARCADE", 151, 395, 17, Green, dpi);
            }
            else
            {
                Text(dc, "WAVE " + _wave + "    TOWER HP " +
                    Math.Ceiling(_health).ToString(MoneyCulture),
                    151, 287, 20, Gold, dpi, true);
                Text(dc, "P TO RESUME    ESC BACK TO ARCADE", 151, 395, 17, White, dpi);
            }
        }

        private void DrawHelp(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(248, 6, 18, 38)),
                new Pen(Gold, 2.4), new Rect(112, 149, 698, 353));
            Text(dc, "RADIO TOWER DEFENSE — FIELD MANUAL", 143, 169, 22, Gold, dpi, true);
            Text(dc, "THE THREATS", 143, 217, 15, Cyan, dpi, true);
            Text(dc, "CLICK COPPER THIEVES TO CHASE THEM OFF (+$25).",
                143, 242, 14, White, dpi);
            Text(dc, "CLICK INTERFERENCE TOWERS TO REMOVE THEM (+$45).",
                143, 267, 14, White, dpi);
            Text(dc, "LIGHTNING STRIKES AFTER A VISIBLE WARNING.",
                143, 292, 14, White, dpi);
            Text(dc, "THE UPGRADES", 143, 323, 15, Cyan, dpi, true);
            Text(dc, "1 LIGHTNING • 2 SURGE • 3 FENCE • 4 RF FILTER • 5 FENCE REPAIR",
                143, 347, 13, White, dpi);
            Text(dc, "UPGRADES CAN BE PURCHASED DURING OR BETWEEN WAVES.",
                143, 374, 13, Muted, dpi);
            Text(dc, "SPACE = START / NEXT WAVE   P = PAUSE   ESC = LEAVE",
                143, 426, 13, White, dpi);
            Text(dc, "PRESS H TO RETURN", 143, 469, 16, Gold, dpi, true);
        }

        private static void Text(DrawingContext dc, string value,
            double x, double y, double size, Brush color, double dpi,
            bool bold = false, double maxWidth = 0)
        {
            var ft = new FormattedText(
                value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Consolas"), FontStyles.Normal,
                    bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                size, color, dpi);
            if (maxWidth > 0)
                ft.MaxTextWidth = maxWidth;
            dc.DrawText(ft, new Point(x, y));
        }

        private static Brush Paint(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }

        private static string BestFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartGridSuite", "Arcade", "tower-defense-360-best.txt");

        private static int ReadBestScore()
        {
            try
            {
                return File.Exists(BestFile) &&
                    int.TryParse(File.ReadAllText(BestFile),
                        NumberStyles.Integer, MoneyCulture, out var score)
                    ? Math.Max(0, score) : 0;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private static void SaveBestScore(int score)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BestFile)!);
                File.WriteAllText(BestFile, score.ToString(MoneyCulture));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
            _clock.Stop();
            _timer.Tick -= Tick;
            PreviewKeyDown -= HandleKey;
            PreviewMouseLeftButtonDown -= HandleClick;
            _host.Closed -= HostClosed;
            _host.IsVisibleChanged -= HostVisibilityChanged;
            _host.StateChanged -= HostStateChanged;
            Closed -= WindowClosed;
            if (ReferenceEquals(_active, this))
                _active = null;
        }

        private sealed class CopperThief
        {
            public double Angle { get; set; }
            public double Distance { get; set; }
            public double Speed { get; set; }
        }

        private sealed class InterferenceTower
        {
            public double X { get; set; }
            public double Y { get; set; }
        }

        private sealed class GameSurface : FrameworkElement
        {
            private readonly TowerDefenseWindow _game;

            public GameSurface(TowerDefenseWindow game)
            {
                _game = game;
                Focusable = false;
            }

            protected override Size MeasureOverride(Size availableSize) =>
                new(940, 600);

            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);
                _game.Draw(drawingContext,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
            }
        }
    }
}
