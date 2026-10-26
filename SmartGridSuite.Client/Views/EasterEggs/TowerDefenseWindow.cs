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
        private readonly List<DefenseFlash> _defenseFlashes = new();
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
        private bool _perkPending;
        private double _securityRingTime;
        private double _rfRingTime;
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
            _perkPending = false;
            _securityRingTime = 0;
            _rfRingTime = 0;
            _defenseFlashes.Clear();
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
            if (!_started || _paused || _gameOver || _waveActive ||
                _perkPending)
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
            _droneCooldown = Math.Max(0, _droneCooldown - dt);
            _flashRemaining = Math.Max(0, _flashRemaining - dt);
            _securityRingTime = Math.Max(0, _securityRingTime - dt);
            _rfRingTime = Math.Max(0, _rfRingTime - dt);
            _defenseFlashes.RemoveAll(flash => flash.EndTime <= now);

            if (!_waveActive)
            {
                _surface.InvalidateVisual();
                return;
            }

            int patrol = _progression.Level(0);
            int patrolSpeed = _progression.Level(1);
            int coverage = _progression.Level(2);
            int vehicles = _progression.Level(3);
            int rfTeams = _progression.Level(4);
            int filters = _progression.Level(5);
            int drone = _progression.Level(6);
            int droneRange = _progression.Level(7);

            if (_thievesToSpawn > 0)
            {
                _spawnThiefIn -= dt;
                if (_spawnThiefIn <= 0)
                {
                    SpawnCopperThief();
                    _thievesToSpawn--;
                    _spawnThiefIn = Math.Max(0.22, 1.5 / (1 + _wave * 0.08));
                }
            }

            if (_jammersToSpawn > 0)
            {
                _spawnJammerIn -= dt;
                if (_spawnJammerIn <= 0)
                {
                    SpawnInterferenceTower();
                    _jammersToSpawn--;
                    _spawnJammerIn = Math.Max(1.0, 6.8 / (1 + _wave * 0.12));
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
                    _spawnLightningIn = Math.Max(2.5, 8.3 / (1 + _wave * 0.08));
                    _message = "LIGHTNING INCOMING! ARRESTORS BETTER BE INSTALLED!";
                }
            }

            // Autonomous countermeasures give keyboard-only players meaningful
            // choices without requiring a trackpad and pixel-perfect aiming.
            if (_patrolCooldown <= 0)
            {
                int count = Math.Min(_thieves.Count,
                    1 + patrol / 3 + vehicles / 2);
                double radius = Math.Min(450, 105 + coverage * 17);
                for (int i = 0; i < count; i++)
                {
                    int closest = IndexOfNearestThief();
                    if (_thieves[closest].Distance > radius)
                        break;
                    _defenseFlashes.Add(new DefenseFlash(
                        new Point(TowerX, TowerY - 38),
                        ThiefPoint(_thieves[closest]), now + 0.16, Cyan));
                    _thieves.RemoveAt(closest);
                    _thievesStopped++;
                    _cash = AddCash(_cash, 14 + _progression.Level(17) * 4);
                    _score = AddCash(_score, 24);
                }
                _patrolCooldown = Math.Max(0.2,
                    3.4 / (1 + patrolSpeed * 0.20 + patrol * 0.07));
            }

            // The drone intercepts nearby trouble without manual targeting.
            // RF target priority increases when RF response teams are funded.
            if (drone > 0 && _droneCooldown <= 0)
            {
                double radius = Math.Min(270, 90 + droneRange * 18);
                var nearbyJammer = _jammers.FirstOrDefault(j =>
                    Distance(new Point(j.X, j.Y), new Point(TowerX, TowerY)) <= radius);
                if (nearbyJammer != null && rfTeams > 0)
                {
                    _defenseFlashes.Add(new DefenseFlash(
                        new Point(TowerX, TowerY - 38),
                        new Point(nearbyJammer.X, nearbyJammer.Y), now + 0.19, Purple));
                    _jammers.Remove(nearbyJammer);
                    _interferenceRemoved++;
                    _score = AddCash(_score, 30);
                }
                else if (_thieves.Count > 0 &&
                         _thieves.Min(t => t.Distance) <= radius)
                {
                    int closest = IndexOfNearestThief();
                    _defenseFlashes.Add(new DefenseFlash(
                        new Point(TowerX, TowerY - 38),
                        ThiefPoint(_thieves[closest]), now + 0.19, Gold));
                    _thieves.RemoveAt(closest);
                    _thievesStopped++;
                    _score = AddCash(_score, 26);
                }
                else if (nearbyJammer != null)
                {
                    _defenseFlashes.Add(new DefenseFlash(
                        new Point(TowerX, TowerY - 38),
                        new Point(nearbyJammer.X, nearbyJammer.Y), now + 0.19, Purple));
                    _jammers.Remove(nearbyJammer);
                    _interferenceRemoved++;
                }
                _droneCooldown = Math.Max(0.25, 3.1 / (1 + drone * 0.20));
            }

            for (int i = _thieves.Count - 1; i >= 0; i--)
            {
                var thief = _thieves[i];
                thief.Distance = Math.Max(0, thief.Distance - thief.Speed * dt);

                if (_fenceLevel > 0 && _fenceHealth > 0 &&
                    thief.Distance <= FenceRadius)
                {
                    double wear = Math.Max(0.3, (4.5 + _wave * 0.18) /
                        (1 + _fenceLevel * 0.18 +
                         _progression.Level(13) * 0.23));
                    _fenceHealth = Math.Max(0, _fenceHealth - wear);
                    _thieves.RemoveAt(i);
                    _thievesStopped++;
                    _score = AddCash(_score, 12);
                    if (_fenceHealth <= 0)
                        _message = "SECURITY FENCE BREACHED! PRESS F TO REPAIR IT.";
                }
                else if (thief.Distance <= HubRadius)
                {
                    DamageTower(6 + _wave * 0.65);
                    _thieves.RemoveAt(i);
                    _message = "COPPER THIEVES GOT INTO THE TOWER COMPOUND!";
                }
            }

            double jammerLifetime = Math.Max(1.1,
                12.0 / (1 + filters * 0.23 + _progression.Level(4) * 0.10));
            for (int i = _jammers.Count - 1; i >= 0; i--)
            {
                _jammers[i].Age += dt;
                if (_jammers[i].Age >= jammerLifetime)
                {
                    _jammers.RemoveAt(i);
                    _interferenceRemoved++;
                    _score = AddCash(_score, 15);
                }
            }

            if (_jammers.Count > 0)
            {
                double reduction = 1.0 / (1 + filters * 0.58);
                DamageTower(_jammers.Count *
                    (1.05 + 0.10 * _wave) * reduction * dt);
            }

            // Workshop-funded passive maintenance is available every wave.
            _health = Math.Min(MaxTowerHealth, _health +
                _progression.Level(9) * 0.055 * dt);
            if (_fenceLevel > 0)
                _fenceHealth = Math.Min(FenceMaxHealth, _fenceHealth +
                    _progression.Level(14) * 0.09 * dt);

            if (_health <= 0)
            {
                _health = 0;
                EndGame();
                return;
            }

            if (_thievesToSpawn == 0 && _jammersToSpawn == 0 &&
                _lightningToSpawn == 0 && _warningRemaining <= 0 &&
                _thieves.Count == 0 && _jammers.Count == 0)
                EndWave();

            _surface.InvalidateVisual();
        }

        private static int AddCash(int value, int delta) =>
            (int)Math.Min(int.MaxValue, (long)value + Math.Max(0, delta));

        private double MaxTowerHealth =>
            100 + Math.Min(2500, _progression.Level(8) * 15);

        private void DamageTower(double damage)
        {
            double grounded = damage / (1 + _progression.Level(15) * 0.10);
            _health = Math.Max(0, _health - grounded);
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
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
            DamageTower(damage);
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
            int reward = 120 + _wave * 65 +
                _progression.Level(16) * (24 + _wave * 3);
            _cash = AddCash(_cash, reward);
            _score = AddCash(_score, 90 * _wave + (int)Math.Ceiling(_health));
            int research = 8 + _wave * 3 + _progression.Level(19) * 5;
            _progression.AddResearchCredits(research);

            _health = Math.Min(MaxTowerHealth, _health +
                _progression.Level(23) * 3);

            _message = "WAVE " + _wave + " CLEARED! +$" +
                reward.ToString("N0", MoneyCulture) + " / +" +
                research.ToString(MoneyCulture) + " RESEARCH.";

            if (_progression.Level(21) > 0 &&
                _random.NextDouble() < Math.Min(0.75,
                    _progression.Level(21) * 0.06))
            {
                var choices = Enumerable.Range(0, TowerDefenseProgression.Catalog.Length)
                    .Where(i => _progression.Unlocked(i, _wave)).ToArray();
                if (choices.Length > 0)
                {
                    int index = choices[_random.Next(choices.Length)];
                    double previousMaxHp = MaxTowerHealth;
                    double previousFenceHp = FenceMaxHealth;
                    _progression.FreeUpgrade(index);
                    ApplyUpgradeEffects(index, previousMaxHp, previousFenceHp);
                    _message += " FREE " +
                        TowerDefenseProgression.Catalog[index].Name.ToUpperInvariant() + "!";
                }
            }

            if (_wave % 5 == 0)
            {
                _perkPending = true;
                _message = "MILESTONE BONUS! CHOOSE 1 CASH / 2 REPAIR / 3 RESEARCH.";
            }
        }

        private void SelectPerk(int option)
        {
            if (!_perkPending || option < 1 || option > 3)
                return;

            _perkPending = false;
            switch (option)
            {
                case 1:
                    int cash = 380 + _wave * 120;
                    _cash = AddCash(_cash, cash);
                    _message = "OVERTIME PAY: +$" +
                        cash.ToString("N0", MoneyCulture) + ". SPACE FOR NEXT WAVE.";
                    break;
                case 2:
                    double repaired = Math.Min(MaxTowerHealth - _health,
                        MaxTowerHealth * 0.40);
                    _health += repaired;
                    if (_fenceLevel > 0)
                        _fenceHealth = FenceMaxHealth;
                    _message = "STORM RECOVERY: RESTORED " +
                        repaired.ToString("0", MoneyCulture) + " HP + FENCE.";
                    break;
                case 3:
                    int research = 25 + _wave * 8;
                    _progression.AddResearchCredits(research);
                    _message = "R&D GRANT: +" +
                        research.ToString(MoneyCulture) + " RESEARCH CREDITS.";
                    break;
            }
            _surface.InvalidateVisual();
        }

        private void EndGame()
        {
            _gameOver = true;
            _timer.Stop();
            _clock.Stop();
            _message = "RADIO SILENCE. DISPATCH IS NOT HAPPY.";
            _progression.BankRun(_wave, _score);
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
                if (_progression.WorkshopOpen)
                {
                    _progression.WorkshopOpen = false;
                    _surface.InvalidateVisual();
                }
                else
                    Close();
                return;
            }

            if (e.Key == Key.W)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    ToggleWorkshop();
                return;
            }

            if (_gameOver && e.Key == Key.R && !_progression.WorkshopOpen)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    NewGame();
                return;
            }

            if (e.Key == Key.H && !_progression.WorkshopOpen)
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

            // 24 upgrades: 3 tab pages x 8 rows. Entirely usable on laptop
            // keyboards, including purchases within the permanent workshop.
            if (e.Key is Key.Tab or Key.Left or Key.Right)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                {
                    int step = e.Key == Key.Left || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                        ? -1 : 1;
                    _progression.SelectedCategory =
                        (_progression.SelectedCategory + step + 3) % 3;
                    _surface.InvalidateVisual();
                }
                return;
            }

            if (e.Key is Key.Up or Key.Down)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                {
                    int step = e.Key == Key.Up ? -1 : 1;
                    _progression.SelectedRow = (_progression.SelectedRow + step + 8) % 8;
                    _surface.InvalidateVisual();
                }
                return;
            }

            int row = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                Key.D4 or Key.NumPad4 => 3,
                Key.D5 or Key.NumPad5 => 4,
                Key.D6 or Key.NumPad6 => 5,
                Key.D7 or Key.NumPad7 => 6,
                Key.D8 or Key.NumPad8 => 7,
                Key.Enter or Key.B => _progression.SelectedRow,
                _ => -1
            };

            if (row >= 0)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    BuySelected(row);
                return;
            }

            if (_progression.WorkshopOpen)
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
                    _message = "BUILD YOUR RADIO DEFENSES, THEN SPACE FOR WAVE 1.";
                    _surface.InvalidateVisual();
                }
                else if (!_paused && !_waveActive)
                    StartWave();
                return;
            }

            if (e.Key == Key.P && _started && !_gameOver)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    SetPaused(!_paused);
                return;
            }

            if (!_started || _paused || _gameOver)
                return;

            if (e.Key is Key.Q or Key.E or Key.T or Key.F)
            {
                e.Handled = true;
                if (e.IsRepeat) return;
                if (e.Key == Key.Q)
                    CallSecuritySweep();
                else if (e.Key == Key.E)
                    CallRfSweep();
                else if (e.Key == Key.T)
                    RepairTower();
                else
                    RepairFence();
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
                int count = Math.Min(_thieves.Count,
                    1 + _progression.Level(3) / 2 + _progression.Level(0) / 4);
                for (int i = 0; i < count; i++)
                {
                    _thieves.RemoveAt(IndexOfNearestThief());
                    _thievesStopped++;
                    _cash = AddCash(_cash, 22 + _progression.Level(17) * 4);
                    _score = AddCash(_score, 35);
                }

                _securitySweepCooldown = Math.Max(1.0,
                    8.0 / (1 + _progression.Level(18) * 0.18));
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
                int count = Math.Min(_jammers.Count,
                    1 + _progression.Level(4) / 2);
                _jammers.RemoveRange(0, count);
                _interferenceRemoved += count;
                _cash = AddCash(_cash, count * 40);
                _score = AddCash(_score, count * 60);
                _rfSweepCooldown = Math.Max(1.5,
                    11.0 / (1 + _progression.Level(18) * 0.16));
                _message = "RF SWEEP: " + count + " INTERFERENCE TOWERS REMOVED! +$" +
                    (count * 40).ToString(MoneyCulture) + ".";
            }
            _surface.InvalidateVisual();
        }

        private void BuySelected(int row)
        {
            int index = _progression.SelectedCategory * 8 + row;
            if (index < 0 || index >= TowerDefenseProgression.Catalog.Length)
                return;
            _progression.SelectedRow = row;

            var upgrade = TowerDefenseProgression.Catalog[index];
            if (!_progression.Unlocked(index, _wave))
            {
                _message = upgrade.Name.ToUpperInvariant() +
                    " UNLOCKS AFTER WAVE " + upgrade.UnlockWave + ".";
                _surface.InvalidateVisual();
                return;
            }

            double previousMax = MaxTowerHealth;
            double previousFence = FenceMaxHealth;

            if (_progression.WorkshopOpen)
            {
                if (!_progression.BuyWorkshop(index))
                {
                    _message = "RESEARCH WORKSHOP: NEED " +
                        _progression.WorkshopPrice(index).ToString("N0", MoneyCulture) +
                        " CREDITS.";
                }
                else
                {
                    ApplyUpgradeEffects(index, previousMax, previousFence);
                    _message = "PERMANENT RESEARCH: " +
                        upgrade.Name.ToUpperInvariant() + " LV" +
                        _progression.PermanentLevel(index) + ".";
                }
            }
            else if (_started && !_gameOver && !_paused)
            {
                int price = _progression.RunPrice(index);
                if (_progression.BuyRun(index, ref _cash, _wave))
                {
                    ApplyUpgradeEffects(index, previousMax, previousFence);
                    _message = upgrade.Name.ToUpperInvariant() + " LV" +
                        _progression.Level(index) + "  -$" +
                        price.ToString("N0", MoneyCulture) + ".";
                }
                else
                    _message = "NEED $" + price.ToString("N0", MoneyCulture) +
                        " FOR " + upgrade.Name.ToUpperInvariant() + ".";
            }
            else
            {
                _message = "SPACE TO START, OR W FOR THE PERMANENT WORKSHOP.";
            }
            _surface.InvalidateVisual();
        }

        private void ApplyUpgradeEffects(int index, double oldMaxHealth,
            double oldFenceHealth)
        {
            if (index == 8) // structural tower reinforcement
                _health = Math.Min(MaxTowerHealth,
                    _health + (MaxTowerHealth - oldMaxHealth));
            else if (index == 12 && oldFenceHealth == 0) // install fence
                _fenceHealth = FenceMaxHealth;
            else if (index == 12 || index == 13)
                _fenceHealth = Math.Min(FenceMaxHealth,
                    _fenceHealth + FenceMaxHealth - oldFenceHealth);
        }

        private void RepairTower()
        {
            if (!_started || _paused || _gameOver || _progression.WorkshopOpen)
                return;
            if (_health >= MaxTowerHealth)
            {
                _message = "TOWER INTEGRITY ALREADY FULL.";
                _surface.InvalidateVisual();
                return;
            }
            int cost = 135 + _wave * 30;
            cost = (int)Math.Max(1,
                Math.Ceiling(cost / (1 + _progression.Level(20) * 0.025)));
            if (_cash < cost)
                _message = "TOWER REPAIR NEEDS $" +
                    cost.ToString("N0", MoneyCulture) + ".";
            else
            {
                _cash -= cost;
                double heal = Math.Min(MaxTowerHealth - _health,
                    25 + 5 * _progression.Level(22));
                _health += heal;
                _message = "REPAIR CREW RESTORED " +
                    heal.ToString("0", MoneyCulture) + " TOWER HP. -$" +
                    cost.ToString("N0", MoneyCulture) + ".";
            }
            _surface.InvalidateVisual();
        }

        private void RepairFence()
        {
            if (!_started || _paused || _gameOver || _progression.WorkshopOpen)
                return;
            if (_fenceLevel == 0)
                _message = "INSTALL A SECURITY FENCE BEFORE REPAIRING IT.";
            else if (_fenceHealth >= FenceMaxHealth)
                _message = "SECURITY FENCE IS ALREADY FULLY REPAIRED.";
            else
            {
                int price = (int)Math.Max(1,
                    Math.Ceiling((90 + _wave * 8.0) /
                        (1 + _progression.Level(20) * 0.025)));
                if (_cash < price)
                    _message = "FENCE REPAIR NEEDS $" +
                        price.ToString("N0", MoneyCulture) + ".";
                else
                {
                    _cash -= price;
                    _fenceHealth = FenceMaxHealth;
                    _message = "FENCE RESTORED! -$" +
                        price.ToString("N0", MoneyCulture) + ".";
                }
            }
            _surface.InvalidateVisual();
        }

        private void ToggleWorkshop()
        {
            if (_waveActive || (_wave > 0 && !_gameOver))
            {
                _message = "PERMANENT WORKSHOP AVAILABLE BEFORE OR AFTER RUNS.";
                _surface.InvalidateVisual();
                return;
            }

            _progression.WorkshopOpen = !_progression.WorkshopOpen;
            _showHelp = false;
            _surface.InvalidateVisual();
        }

        private double FenceMaxHealth => _fenceLevel == 0 ? 0 :
            Math.Min(3000, 24 + _fenceLevel * 20 +
                _progression.Level(13) * 16);

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
            Text(dc, "Q SECURITY  E RF  T TOWER REPAIR  F FENCE",
                37, 551, 11, White, dpi, true);
            Text(dc, "1-8 BUY  TAB CATEGORY  W WORKSHOP  SPACE WAVE",
                478, 551, 10.5, Cyan, dpi, true, 400);

            if (!_started || _paused || _gameOver)
                DrawOverlay(dc, dpi);
            if (_showHelp)
                DrawHelp(dc, dpi);
            if (_progression.WorkshopOpen)
                DrawWorkshop(dc, dpi);
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
                    84 * Math.Clamp(_health / MaxTowerHealth, 0, 1), 7));

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

        private static string Money(long amount)
        {
            if (amount >= 1_000_000_000L)
                return "$" + (amount / 1_000_000_000.0).ToString("0.0", MoneyCulture) + "B";
            if (amount >= 1_000_000L)
                return "$" + (amount / 1_000_000.0).ToString("0.0", MoneyCulture) + "M";
            if (amount >= 100_000L)
                return "$" + (amount / 1000.0).ToString("0.0", MoneyCulture) + "K";
            return "$" + amount.ToString("N0", MoneyCulture);
        }

        private static string ShortLabel(string name, int maxChars = 16) =>
            name.Length <= maxChars ? name.ToUpperInvariant() :
                name.Substring(0, maxChars).ToUpperInvariant();

        private static readonly string[] CategoryNames =
        {
            "OPERATIONS", "PROTECTION", "LOGISTICS"
        };

        private void DrawSidebar(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Panel, new Pen(GridInk, 1),
                new Rect(607, FieldY, 277, FieldHeight));
            Text(dc, "TOWER STATUS", 621, 138, 17, Cyan, dpi, true);
            Text(dc, "HP", 621, 173, 12, Muted, dpi);
            Text(dc, Math.Ceiling(_health).ToString("0", MoneyCulture) +
                "/" + MaxTowerHealth.ToString("0", MoneyCulture),
                621, 190, 21, _health < 28 ? Red : Green, dpi, true);
            Text(dc, "CASH", 760, 173, 12, Muted, dpi);
            Text(dc, Money(_cash), 754, 193, 20, Gold, dpi, true);

            Text(dc, "WAVE " + _wave + (_waveActive ? " LIVE" : " READY"),
                621, 224, 13, White, dpi, true);
            Text(dc, "RESEARCH +" +
                _progression.PendingCredits.ToString("N0", MoneyCulture),
                737, 225, 11, Purple, dpi, true);

            for (int cat = 0; cat < 3; cat++)
                Text(dc, cat == _progression.SelectedCategory
                        ? "[" + CategoryNames[cat] + "]"
                        : CategoryNames[cat],
                    cat == 0 ? 618 : cat == 1 ? 707 : 797, 250, 10,
                    cat == _progression.SelectedCategory ? Gold : Muted,
                    dpi, true);

            for (int i = 0; i < 8; i++)
                DrawUpgradeRow(dc, i, 274 + i * 29, dpi);
        }

        private void DrawUpgradeRow(DrawingContext dc, int row,
            double y, double dpi)
        {
            int index = _progression.SelectedCategory * 8 + row;
            var upgrade = TowerDefenseProgression.Catalog[index];
            bool locked = !_progression.Unlocked(index, _wave);
            bool selected = _progression.SelectedRow == row;

            dc.DrawRectangle(ButtonFace,
                new Pen(selected ? Gold : GridInk, selected ? 1.6 : 0.6),
                new Rect(620, y, 250, 26));

            string label = (row + 1) + " " + ShortLabel(upgrade.Name) +
                " L" + _progression.Level(index);
            string price = locked
                ? "W" + upgrade.UnlockWave
                : Money(_progression.RunPrice(index));

            Text(dc, label, 626, y + 5, 11.2,
                locked ? Muted : selected ? Gold : White, dpi,
                selected, 181);
            Text(dc, price, 806, y + 5, 10.9,
                locked ? Red : Green, dpi, true, 62);
        }

        private void DrawWorkshop(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(248, 5, 14, 31)),
                new Pen(Purple, 2.6), new Rect(105, 116, 720, 411));
            Text(dc, "RADIO SHOP RESEARCH WORKSHOP", 132, 131, 24,
                Gold, dpi, true);
            Text(dc, "PERMANENT CREDITS: " + Money(_progression.Credits) +
                "     BEST WAVE: " + _progression.BestWave, 133, 168,
                15, White, dpi, true);

            for (int cat = 0; cat < 3; cat++)
                Text(dc, cat == _progression.SelectedCategory
                        ? "[" + CategoryNames[cat] + "]"
                        : CategoryNames[cat],
                    cat == 0 ? 132 : cat == 1 ? 348 : 571, 207, 15,
                    cat == _progression.SelectedCategory ? Gold : Muted,
                    dpi, true);

            for (int row = 0; row < 8; row++)
            {
                int index = _progression.SelectedCategory * 8 + row;
                var upgrade = TowerDefenseProgression.Catalog[index];
                double y = 244 + row * 27;
                bool selected = row == _progression.SelectedRow;
                bool unlocked = _progression.Unlocked(index, _wave);

                if (selected)
                    dc.DrawRectangle(ButtonFace, new Pen(Gold, 1.5),
                        new Rect(131, y - 2, 665, 27));

                Text(dc, (row + 1) + "  " + upgrade.Name,
                    141, y, 15, unlocked ? selected ? Gold : White : Muted,
                    dpi, selected, 410);
                Text(dc, "LV " + _progression.PermanentLevel(index),
                    572, y, 13, Green, dpi, true, 75);
                Text(dc, unlocked ? Money(_progression.WorkshopPrice(index)) :
                    "WAVE " + upgrade.UnlockWave,
                    680, y, 13, unlocked ? Gold : Red, dpi, true, 112);
            }

            int picked = _progression.SelectedCategory * 8 +
                _progression.SelectedRow;
            Text(dc, TowerDefenseProgression.Catalog[picked].Detail,
                133, 468, 13, Cyan, dpi, false, 620);
            Text(dc, "1-8 BUY   TAB CATEGORIES   UP/DOWN SELECT   W CLOSE",
                133, 498, 13, Muted, dpi, true, 650);
        }

        private void DrawOverlay(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(242, 5, 13, 29)),
                new Pen(Cyan, 2.5), new Rect(103, 164, 709, 307));
            Text(dc, _gameOver ? "TOWER DESTROYED" :
                _paused ? "RADIO SILENCE — PAUSED" : "GRID UNDER ATTACK",
                151, 191, 29, _gameOver ? Red : Cyan, dpi, true);

            if (!_started)
            {
                Text(dc, "ONE RADIO TOWER. UNLIMITED WAYS TO IMPROVE IT.",
                    151, 245, 17, White, dpi, true);
                Text(dc, "24 UPGRADES • 3 SYSTEMS • PERSISTENT RESEARCH",
                    151, 280, 17, Purple, dpi, true);
                Text(dc, "INVEST IN YOUR SHOP BETWEEN EVERY SHIFT.",
                    151, 315, 15, Gold, dpi, true);
                Text(dc, "PRESS SPACE TO PREPARE", 151, 370, 24, Green, dpi, true);
                Text(dc, "W RESEARCH WORKSHOP   H HELP   SPACE THEN WAVE 1",
                    151, 422, 14, Muted, dpi);
            }
            else if (_gameOver)
            {
                Text(dc, "WAVE " + _wave + "  •  SCORE " +
                    _score.ToString("N0", MoneyCulture),
                    151, 275, 20, White, dpi, true);
                Text(dc, "RESEARCH BANK: " + Money(_progression.Credits) +
                    "   BEST WAVE: " + _progression.BestWave,
                    151, 314, 17, Gold, dpi, true);
                Text(dc, "W FOR PERMANENT UPGRADES  •  R FOR NEXT SHIFT",
                    151, 366, 16, Green, dpi, true);
                Text(dc, "ESC BACK TO ARCADE", 151, 410, 14, Muted, dpi);
            }
            else
            {
                Text(dc, "WAVE " + _wave + "    TOWER HP " +
                    Math.Ceiling(_health).ToString(MoneyCulture) +
                    "/" + MaxTowerHealth.ToString("0", MoneyCulture),
                    151, 285, 20, Gold, dpi, true);
                Text(dc, "P TO RESUME    ESC BACK TO ARCADE",
                    151, 395, 17, White, dpi);
            }
        }

        private void DrawHelp(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(249, 6, 18, 38)),
                new Pen(Gold, 2.4), new Rect(112, 143, 698, 369));
            Text(dc, "GRID UNDER ATTACK — FIELD MANUAL", 143, 160, 22,
                Gold, dpi, true);
            Text(dc, "24 UPGRADES: OPERATIONS / PROTECTION / LOGISTICS",
                143, 203, 15, Cyan, dpi, true);
            Text(dc, "TAB OR LEFT/RIGHT: SWITCH CATEGORY (8 EACH)",
                143, 231, 14, White, dpi);
            Text(dc, "1-8: BUY UPGRADE  •  ARROWS + ENTER: SELECT/BUY",
                143, 259, 14, White, dpi);
            Text(dc, "Q SECURITY SWEEP  •  E RF SWEEP  •  T TOWER REPAIR",
                143, 290, 14, White, dpi);
            Text(dc, "F FENCE REPAIR  •  SPACE NEXT WAVE  •  P PAUSE",
                143, 318, 14, White, dpi);
            Text(dc, "W: PERMANENT WORKSHOP BEFORE / AFTER RUNS",
                143, 352, 15, Purple, dpi, true);
            Text(dc, "EARN RESEARCH ON WAVES; KEEP IT AFTER DEFEAT.",
                143, 380, 14, Green, dpi, true);
            Text(dc, "THREATS ARE AUTOMATIC — NEVER NEED A TRACKPAD.",
                143, 409, 14, Muted, dpi);
            Text(dc, "H CLOSES HELP   •   ESC BACK TO ARCADE",
                143, 465, 16, Gold, dpi, true);
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
            "SmartGridSuite", "Arcade", "tower-defense-workshop-score.txt");

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
            _progression.BankRun(_wave, _score);
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

        private sealed record DefenseFlash(
            Point From, Point To, double EndTime, Brush Ink);

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
