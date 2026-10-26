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
        private static readonly CultureInfo MoneyCulture = CultureInfo.InvariantCulture;

        private static TowerDefenseWindow? _active;
        private static readonly Brush Backdrop = Paint("#081224");
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
        private static readonly Pen TileOutline = FreezePen(GridInk, 0.5);
        private static readonly Pen RadarOutline = FreezePen(GridInk, 0.95);
        private static readonly Brush RedFlash = new SolidColorBrush(Color.FromArgb(105, 255, 92, 113));
        private static readonly Brush PulseFill = new SolidColorBrush(Color.FromArgb(35, 92, 232, 250));

        private readonly Window _host;
        private readonly GameSurface _surface;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _clock = new();
        private readonly Random _random = new();
        private readonly List<CopperThief> _thieves = new();
        private readonly List<InterferenceTower> _jammers = new();
        private readonly TowerDefenseProgression _progression = new();

        private int _cash;
        private int _score;
        private int _bestScore;
        private int _wave;
        private int _thievesStopped;
        private int _interferenceRemoved;
        // Compatibility aliases for the original center-tower mechanics.
        // The 24 upgrade paths now live in TowerDefenseProgression.
        private int _lightningLevel => _progression.Level(10);
        private int _surgeLevel => _progression.Level(11);
        private int _fenceLevel => _progression.Level(12);
        private int _filterLevel => _progression.Level(5);
        private double _health;
        private double _fenceHealth;
        private double _lastTickTime;
        private double _spawnThiefIn;
        private double _spawnJammerIn;
        private double _spawnLightningIn;
        private double _warningRemaining = -1;
        private double _flashRemaining;
        private double _securitySweepCooldown;
        private double _rfSweepCooldown;
        private double _patrolCooldown;
        private double _droneCooldown;
        private bool _freeUpgradeAnnounced;
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
            Background = Backdrop;
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
            _progression.NewRun();
            _cash = 450 + _progression.Level(16) * 35;
            _score = 0;
            _wave = 0;
            _thievesStopped = 0;
            _interferenceRemoved = 0;
            _health = MaxTowerHealth;
            _fenceHealth = FenceMaxHealth;
            _thievesToSpawn = 0;
            _jammersToSpawn = 0;
            _lightningToSpawn = 0;
            _lastTickTime = 0;
            _warningRemaining = -1;
            _flashRemaining = 0;
            _securitySweepCooldown = 0;
            _rfSweepCooldown = 0;
            _patrolCooldown = 1.6;
            _droneCooldown = 0.8;
            _freeUpgradeAnnounced = false;
            _paused = false;
            _started = false;
            _waveActive = false;
            _gameOver = false;
            _showHelp = false;
            _helpAutoPaused = false;
            _message = "PRESS SPACE TO DEFEND YOUR TOWER.";
            _thieves.Clear();
            _jammers.Clear();
            _bestScore = Math.Max(ReadBestScore(), _progression.BestScore);
            _surface.InvalidateVisual();
        }

        private void StartWave()
        {
            if (!_started || _paused || _gameOver || _waveActive)
                return;

            _wave++;
            _thievesToSpawn = Math.Min(75, 4 + _wave * 2 +
                (int)Math.Pow(_wave, 1.2));
            _jammersToSpawn = Math.Min(12, 1 + _wave / 2);
            _lightningToSpawn = _wave <= 1 ? 0 :
                Math.Min(8, 1 + (_wave - 2) / 3);
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
            _securitySweepCooldown = Math.Max(0, _securitySweepCooldown - dt);
            _rfSweepCooldown = Math.Max(0, _rfSweepCooldown - dt);
            _patrolCooldown = Math.Max(0, _patrolCooldown - dt);
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

            // The baseline security patrol works automatically. Buying
            // fencing protects the compound, while Q calls a faster sweep.
            // Nothing on the field has to be clicked to complete a wave.
            if (_patrolCooldown <= 0)
            {
                if (_thieves.Count > 0)
                {
                    int index = IndexOfNearestThief();
                    _thieves.RemoveAt(index);
                    _thievesStopped++;
                    _cash += 14;
                    _score += 24;
                }

                _patrolCooldown = Math.Max(1.1, 3.4 / (1 + _fenceLevel * 0.11));
            }

            for (int i = _thieves.Count - 1; i >= 0; i--)
            {
                var thief = _thieves[i];
                thief.Distance = Math.Max(0, thief.Distance - thief.Speed * dt);

                if (_fenceLevel > 0 && _fenceHealth > 0 &&
                    thief.Distance <= FenceRadius)
                {
                    double fenceWear = Math.Max(1, 4.5 + _wave * 0.15 -
                        _fenceLevel * 0.42);
                    _fenceHealth = Math.Max(0, _fenceHealth - fenceWear);
                    _thieves.RemoveAt(i);
                    _thievesStopped++;
                    _score += 12;
                    if (_fenceHealth <= 0)
                        _message = "FENCE BREACHED! PRESS 5 TO REPAIR IT.";
                }
                else if (thief.Distance <= HubRadius)
                {
                    _health -= 6 + _wave * 0.65;
                    _thieves.RemoveAt(i);
                    _message = "COPPER THIEVES GOT INTO THE TOWER COMPOUND!";
                }
            }

            // RF sites self-expire, even without player action. Filters reduce
            // damage AND accelerate their removal, so the game never gets
            // stuck waiting for a mouse click on a jammer.
            double jammerLifetime = Math.Max(2.1, 12.0 /
                (1 + _filterLevel * 0.23));
            for (int i = _jammers.Count - 1; i >= 0; i--)
            {
                _jammers[i].Age += dt;
                if (_jammers[i].Age >= jammerLifetime)
                {
                    _jammers.RemoveAt(i);
                    _interferenceRemoved++;
                    _score += 15;
                }
            }

            if (_jammers.Count > 0)
            {
                double filterMultiplier = 1.0 / (1 + _filterLevel * 0.58);
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
            _message = "INTERFERENCE TOWER DETECTED! E = RF SWEEP.";
        }

        private static double BoundaryRadius(double angle, double inset)
        {
            double cos = Math.Abs(Math.Cos(angle));
            double sin = Math.Abs(Math.Sin(angle));
            return Math.Min((FieldWidth / 2 - inset) / Math.Max(0.0001, cos),
                (FieldHeight / 2 - inset) / Math.Max(0.0001, sin));
        }

        private Point ThiefPoint(CopperThief thief)
        {
            // The scavengers dart from side to side rather than marching in
            // unnaturally perfect straight lines. Rendering and clicking both
            // use the same calculated location.
            double wobble = Math.Sin(
                _clock.Elapsed.TotalSeconds * 14 + thief.Angle * 6) * 3.5;
            double dx = Math.Cos(thief.Angle);
            double dy = Math.Sin(thief.Angle);
            return new Point(
                TowerX + dx * thief.Distance - dy * wobble,
                TowerY + dy * thief.Distance + dx * wobble);
        }

        private double FenceRadius => Math.Min(145, 58 + _fenceLevel * 7);

        private void LightningStrike()
        {
            _warningRemaining = -1;
            _flashRemaining = 0.43;

            // Diminishing returns: protection always remains valuable but
            // lightning never becomes harmless at high upgrade levels.
            double damage = (10 + _wave * 1.4) /
                (1 + _lightningLevel * 0.58) /
                (1 + _surgeLevel * 0.42);
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
            int reward = 120 + _wave * 65;
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
                Key.D6 or Key.NumPad6 => 6,
                _ => 0
            };

            if (option > 0)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    Purchase(option);
                return;
            }

            if (e.Key == Key.Q || e.Key == Key.E)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                {
                    if (e.Key == Key.Q)
                        CallSecuritySweep();
                    else
                        CallRfSweep();
                }
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

        private int IndexOfNearestThief()
        {
            int nearestIndex = 0;
            for (int i = 1; i < _thieves.Count; i++)
            {
                if (_thieves[i].Distance < _thieves[nearestIndex].Distance)
                    nearestIndex = i;
            }
            return nearestIndex;
        }

        private void CallSecuritySweep()
        {
            if (_securitySweepCooldown > 0)
            {
                _message = "SECURITY PATROL READY IN " +
                    Math.Ceiling(_securitySweepCooldown).ToString(MoneyCulture) + "s.";
            }
            else if (_thieves.Count == 0)
            {
                _message = "PATROL: NO COPPER THIEVES TO CHASE.";
            }
            else
            {
                int count = Math.Min(_thieves.Count, 1 + _fenceLevel / 2);
                for (int i = 0; i < count; i++)
                {
                    _thieves.RemoveAt(IndexOfNearestThief());
                    _thievesStopped++;
                    _cash += 22;
                    _score += 35;
                }

                _securitySweepCooldown = Math.Max(3.0, 8.0 - _fenceLevel * 0.14);
                _message = "SECURITY SWEEP: " + count + " THIEVES CHASED OFF! +$" +
                    (count * 22).ToString(MoneyCulture) + ".";
            }
            _surface.InvalidateVisual();
        }

        private void CallRfSweep()
        {
            if (_rfSweepCooldown > 0)
            {
                _message = "RF TEAM READY IN " +
                    Math.Ceiling(_rfSweepCooldown).ToString(MoneyCulture) + "s.";
            }
            else if (_jammers.Count == 0)
            {
                _message = "RF TEAM: NO INTERFERENCE TOWERS TO REMOVE.";
            }
            else
            {
                int count = Math.Min(_jammers.Count, 1 + _filterLevel / 2);
                _jammers.RemoveRange(0, count);
                _interferenceRemoved += count;
                _cash += count * 40;
                _score += count * 60;
                _rfSweepCooldown = Math.Max(4.0, 11.0 - _filterLevel * 0.15);
                _message = "RF SWEEP: " + count + " INTERFERENCE TOWERS REMOVED! +$" +
                    (count * 40).ToString(MoneyCulture) + ".";
            }
            _surface.InvalidateVisual();
        }

        // Upgrade prices increase every time you invest. There is no three-level
        // maximum; keep long arithmetic for future waves and saturate before int.
        private static readonly int[] BaseUpgradePrice = { 180, 160, 220, 200 };
        private static readonly int[] UpgradeStep = { 165, 155, 175, 180 };

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
                return _fenceLevel > 0 ? 90 + _wave * 8 : null;

            if (option == 6)
                return 150 + _wave * 25;

            if (option < 1 || option > 4)
                return null;

            long level = LevelFor(option);
            long cost = BaseUpgradePrice[option - 1] +
                UpgradeStep[option - 1] * level + 75 * level * level;
            return (int)Math.Min(int.MaxValue, cost);
        }

        private void Purchase(int option)
        {
            int? price = NextPrice(option);
            if (!price.HasValue)
            {
                _message = "INSTALL THE FENCE BEFORE BUYING FENCE REPAIRS.";
                _surface.InvalidateVisual();
                return;
            }

            if (option == 5 && _fenceHealth >= FenceMaxHealth)
            {
                _message = "YOUR FENCE IS ALREADY FULLY REPAIRED.";
                _surface.InvalidateVisual();
                return;
            }

            if (option == 6 && _health >= 100)
            {
                _message = "TOWER HP IS ALREADY FULL.";
                _surface.InvalidateVisual();
                return;
            }

            if (_cash < price.Value)
            {
                _message = "NEED $" + price.Value.ToString("N0", MoneyCulture) +
                    ". AVAILABLE $" + _cash.ToString("N0", MoneyCulture) + ".";
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
                    _message = "RF FILTERS UPGRADED TO LVL " + _filterLevel + ".";
                    break;
                case 5:
                    _fenceHealth = FenceMaxHealth;
                    _message = "SECURITY FENCE REPAIRED!";
                    break;
                case 6:
                    double healed = Math.Min(25, 100 - _health);
                    _health += healed;
                    _message = "TOWER REPAIRED! +" +
                        healed.ToString("0", MoneyCulture) + " HP.";
                    break;
            }
            _surface.InvalidateVisual();
        }

        private double FenceMaxHealth => _fenceLevel == 0 ? 0 : 24 + _fenceLevel * 20;

        private void Draw(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Backdrop, null, new Rect(0, 0, 940, 650));
            dc.DrawRectangle(Panel, new Pen(GridInk, 2), new Rect(12, 12, 894, 580));
            Text(dc, "GRID UNDER ATTACK", 27, 23, 29, Cyan, dpi, true);
            Text(dc, "360 DEGREE RADIO TOWER DEFENSE", 28, 64, 13, Muted, dpi);
            Text(dc, _message, 28, 97, 12, Gold, dpi, true, 858);

            DrawField(dc, dpi);
            DrawSidebar(dc, dpi);

            // Cooldown indicators are visible without a trackpad or
            // repeatedly pressing hotkeys to ask whether the team is ready.
            string patrol = _securitySweepCooldown <= 0
                ? "READY"
                : Math.Ceiling(_securitySweepCooldown).ToString(MoneyCulture) + "s";
            string rf = _rfSweepCooldown <= 0
                ? "READY"
                : Math.Ceiling(_rfSweepCooldown).ToString(MoneyCulture) + "s";
            Text(dc, "Q SECURITY: " + patrol + "     E RF SWEEP: " + rf,
                37, 518, 11, Cyan, dpi, true);

            dc.DrawRectangle(Panel, new Pen(GridInk, 1), new Rect(25, 539, 859, 42));
            Text(dc, "Q SECURITY SWEEP    E RF SWEEP", 39, 551, 12, White, dpi, true);
            Text(dc, "1-6 BUY/REPAIR   SPACE WAVE   H HELP   P PAUSE",
                403, 551, 11, Cyan, dpi, true, 475);

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
                dc.DrawRectangle(Ground, TileOutline,
                    new Rect(xx, yy, 32, 32));
                if ((x * 13 + y * 19) % 7 == 0)
                    dc.DrawEllipse(Tree, null, new Point(xx + 10, yy + 12), 4, 3);
            }

            // Circular radar rings emphasize that threats can approach
            // from any bearing around this one central radio tower.
            dc.DrawEllipse(null, RadarOutline,
                new Point(TowerX, TowerY), 100, 100);
            dc.DrawEllipse(null, RadarOutline,
                new Point(TowerX, TowerY), 160, 160);

            if (_fenceLevel > 0)
            {
                dc.DrawEllipse(null,
                    new Pen(_fenceHealth > 0 ? FenceInk : Red, 3.0),
                    new Point(TowerX, TowerY), FenceRadius, FenceRadius);

                // Fence posts around the perimeter telegraph the defense upgrade.
                int postCount = Math.Min(48, 16 + _fenceLevel * 4);
                for (int post = 0; post < postCount; post++)
                {
                    double angle = Math.PI * 2 * post / postCount;
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

            // The tower's integrity can be read directly beneath it.
            dc.DrawRectangle(DarkSteel, null,
                new Rect(TowerX - 42, TowerY + 43, 84, 7));
            dc.DrawRectangle(_health < 28 ? Red : Green, null,
                new Rect(TowerX - 42, TowerY + 43,
                    84 * Math.Clamp(_health / 100.0, 0, 1), 7));

            if (_lightningLevel > 0)
                Text(dc, "L" + _lightningLevel, TowerX + 30, TowerY - 50,
                    13, Gold, dpi, true);
            if (_surgeLevel > 0)
                Text(dc, "S" + _surgeLevel, TowerX + 30, TowerY - 30,
                    13, Green, dpi, true);
            if (_filterLevel > 0)
            {
                for (int ring = 0; ring < Math.Min(4, _filterLevel); ring++)
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
                621, 226, 13, White, dpi, true);
            Text(dc, _fenceLevel > 0
                    ? "FENCE " + Math.Ceiling(_fenceHealth).ToString("0", MoneyCulture)
                    : "NO FENCE",
                742, 228, 12, _fenceHealth > 0 ? Green : Red, dpi, true);
            Text(dc, "THIEVES " + (_thieves.Count + _thievesToSpawn) +
                "   RF " + (_jammers.Count + _jammersToSpawn),
                621, 245, 12, Purple, dpi, true);

            // All upgrade/repair actions are accessible from the number row,
            // including the new tower-health repair on key 6.
            DrawUpgrade(dc, 1, "LIGHTNING ARRESTOR", 265, Gold, dpi);
            DrawUpgrade(dc, 2, "SURGE ARRESTOR", 305, Green, dpi);
            DrawUpgrade(dc, 3, "SECURITY FENCE", 345, Cyan, dpi);
            DrawUpgrade(dc, 4, "RF FILTERS", 385, Purple, dpi);
            DrawUpgrade(dc, 5, "REPAIR FENCE", 425, White, dpi);
            DrawUpgrade(dc, 6, "REPAIR TOWER +25HP", 465, Green, dpi);
        }

        private void DrawUpgrade(DrawingContext dc, int option,
            string name, double y, Brush ink, double dpi)
        {
            dc.DrawRectangle(ButtonFace, new Pen(ink, 1),
                new Rect(620, y, 250, 36));

            string label = option <= 4
                ? name + " LV" + LevelFor(option)
                : name;
            int? next = NextPrice(option);
            string price = next.HasValue
                ? "$" + next.Value.ToString("N0", MoneyCulture)
                : "N/A";

            Text(dc, option + "  " + label, 628, y + 9, 12.3, ink, dpi, true, 182);
            Text(dc, price, 811, y + 9, 12.3, White, dpi, true, 57);
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
                Text(dc, "1-4 UPGRADES   5 FENCE REPAIR   6 TOWER REPAIR",
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
            Text(dc, "KEYBOARD ONLY — NO TRACKPAD REQUIRED", 143, 213, 16, Cyan, dpi, true);
            Text(dc, "SECURITY PATROLS CHASE THIEVES AUTOMATICALLY.",
                143, 242, 14, White, dpi);
            Text(dc, "RF TOWERS EXPIRE; FILTERS LIMIT INTERFERENCE.",
                143, 266, 14, White, dpi);
            Text(dc, "Q = SECURITY SWEEP; E = RF SWEEP (COOLDOWNS).",
                143, 290, 14, White, dpi);
            Text(dc, "1 LIGHTNING     2 SURGE     3 FENCE     4 RF FILTERS",
                143, 326, 14, Green, dpi, true);
            Text(dc, "5 FENCE REPAIR     6 TOWER REPAIR (+25 HP)",
                143, 352, 14, Green, dpi, true);
            Text(dc, "UPGRADES HAVE NO 3-LEVEL CAP; PRICES KEEP RISING.",
                143, 388, 14, Gold, dpi);
            Text(dc, "SPACE NEXT WAVE    P PAUSE    ESC BACK TO ARCADE",
                143, 424, 14, White, dpi);
            Text(dc, "PRESS H TO RETURN", 143, 466, 16, Gold, dpi, true);
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

        private static Pen FreezePen(Brush brush, double width)
        {
            var pen = new Pen(brush, width);
            pen.Freeze();
            return pen;
        }

        private static Brush Paint(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            brush.Freeze();
            return brush;
        }

        private static string BestFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartGridSuite", "Arcade", "tower-defense-keyboard-best.txt");

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
            public double Age { get; set; }
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
