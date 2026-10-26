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
    /// Entirely fictional, local arcade simulation. No real towers, outages,
    /// radio communications or SmartGridSuite tickets are accessed.
    /// </summary>
    public sealed class TowerDefenseWindow : Window
    {
        private const int Columns = 17;
        private const int Rows = 12;
        private const double Cell = 32;
        private const double FieldX = 27;
        private const double FieldY = 130;
        private const double FieldWidth = Columns * Cell;
        private const double FieldHeight = Rows * Cell;

        private static readonly Brush Night = Paint("#080F22");
        private static readonly Brush Panel = Paint("#10203B");
        private static readonly Brush Ground = Paint("#112644");
        private static readonly Brush GridLine = Paint("#234261");
        private static readonly Brush Road = Paint("#354258");
        private static readonly Brush Cyan = Paint("#59F1FF");
        private static readonly Brush Mint = Paint("#77F5B5");
        private static readonly Brush Red = Paint("#FF6082");
        private static readonly Brush Gold = Paint("#FFE083");
        private static readonly Brush Lilac = Paint("#CD9DFF");
        private static readonly Brush White = Paint("#F1F6FE");
        private static readonly Brush Muted = Paint("#9FB2CD");

        private static TowerDefenseWindow? _active;
        private readonly Window _host;
        private readonly GameSurface _surface;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _clock = new();
        private readonly Random _random = new();
        private readonly List<(int X, int Y)> _route = new();
        private readonly HashSet<(int X, int Y)> _road = new();
        private readonly List<DefenseTower> _towers = new();
        private readonly List<Enemy> _enemies = new();
        private readonly List<LaserFlash> _flashes = new();

        private int _cash;
        private int _baseHealth;
        private int _wave;
        private int _waveSpawnsLeft;
        private int _kills;
        private int _score;
        private int _localBest;
        private double _spawnWait;
        private double _lastTickTime;
        private bool _started;
        private bool _waveRunning;
        private bool _paused;
        private bool _gameOver;
        private bool _leaderboardVisible;
        private bool _leaderboardPausedGame;
        private (int X, int Y)? _selectedCell;
        private string _message = "PRESS SPACE TO START DEFENDING THE GRID.";

        private TowerDefenseWindow(Window host)
        {
            _host = host;
            Owner = host;
            Title = "Grid Under Attack — Radio Shop Arcade";
            Width = 940;
            Height = 650;
            MinWidth = Width;
            MinHeight = Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = Night;
            FontFamily = new FontFamily("Consolas");
            Focusable = true;

            BuildRoute();
            _surface = new GameSurface(this);
            Content = _surface;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            _timer.Tick += Tick;

            PreviewKeyDown += OnKey;
            PreviewMouseLeftButtonDown += OnMouseClick;
            _host.Closed += HostClosed;
            _host.IsVisibleChanged += HostVisibilityChanged;
            _host.StateChanged += HostStateChanged;
            Closed += OnWindowClosed;
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
                var game = new TowerDefenseWindow(host);
                _active = game;
                game.Show();
                game.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Tower Defense failed to open: " + ex);
                _active = null;
            }
        }

        private void BuildRoute()
        {
            void Add(int x, int y)
            {
                var cell = (x, y);
                if (_road.Add(cell))
                    _route.Add(cell);
            }

            // A winding, one-way interference path. Starting at the west
            // edge, invaders eventually reach the network hub on the east.
            for (var x = 0; x <= 15; x++) Add(x, 2);
            for (var y = 3; y <= 5; y++) Add(15, y);
            for (var x = 14; x >= 3; x--) Add(x, 5);
            for (var y = 6; y <= 9; y++) Add(3, y);
            for (var x = 4; x <= 16; x++) Add(x, 9);
        }

        private void NewGame()
        {
            _timer.Stop();
            _clock.Reset();
            _lastTickTime = 0;
            _cash = 450;
            _baseHealth = 20;
            _wave = 0;
            _score = 0;
            _kills = 0;
            _waveRunning = false;
            _waveSpawnsLeft = 0;
            _paused = false;
            _started = false;
            _gameOver = false;
            _leaderboardVisible = false;
            _leaderboardPausedGame = false;
            _selectedCell = null;
            _message = "PRESS SPACE TO START DEFENDING THE GRID.";
            _towers.Clear();
            _enemies.Clear();
            _flashes.Clear();
            _localBest = ReadBest();
            _surface.InvalidateVisual();
        }

        private void StartWave()
        {
            if (_waveRunning || _gameOver || !_started)
                return;

            _wave++;
            _waveSpawnsLeft = Math.Min(45, 6 + _wave * 2);
            _spawnWait = 0.15;
            _waveRunning = true;
            _message = $"WAVE {_wave}: INCOMING INTERFERENCE!";
            _surface.InvalidateVisual();
        }

        private void Tick(object? sender, EventArgs args)
        {
            if (_paused || _gameOver || !_started)
                return;

            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(now - _lastTickTime, 0, 0.1);
            _lastTickTime = now;

            if (_waveRunning && _waveSpawnsLeft > 0)
            {
                _spawnWait -= dt;
                if (_spawnWait <= 0)
                {
                    SpawnEnemy();
                    _waveSpawnsLeft--;
                    _spawnWait += Math.Max(0.32, 0.9 - _wave * 0.025);
                }
            }

            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var enemy = _enemies[i];
                var movement = enemy.Speed * dt *
                    (now < enemy.SlowUntil ? 0.50 : 1.0);
                enemy.Progress += movement;
                if (enemy.Progress >= _route.Count - 1)
                {
                    _baseHealth -= enemy.DamageToHub;
                    _enemies.RemoveAt(i);
                    _message = "SIGNAL LOST! THE NETWORK HUB TOOK DAMAGE.";
                    if (_baseHealth <= 0)
                    {
                        _baseHealth = 0;
                        EndGame();
                        return;
                    }
                }
            }

            foreach (var tower in _towers)
            {
                tower.ReloadRemaining -= dt;
                if (tower.ReloadRemaining > 0)
                    continue;

                var target = _enemies
                    .Where(enemy => InRange(tower, enemy))
                    .OrderByDescending(enemy => enemy.Progress)
                    .FirstOrDefault();

                if (target == null)
                    continue;

                var targetPoint = EnemyPoint(target);
                var towerPoint = Center(tower.X, tower.Y);
                target.Health -= tower.Damage;
                if (tower.Kind == TowerKind.Jammer)
                    target.SlowUntil = Math.Max(target.SlowUntil, now + 1.8);

                _flashes.Add(new LaserFlash(
                    towerPoint, targetPoint, now + 0.12,
                    tower.Kind == TowerKind.Sniper ? Gold :
                    tower.Kind == TowerKind.Jammer ? Lilac : Cyan));
                tower.ReloadRemaining = tower.Cooldown;
            }

            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                if (_enemies[i].Health > 0)
                    continue;

                var enemy = _enemies[i];
                _cash += enemy.Bounty;
                _score += enemy.Bounty * 2;
                _kills++;
                _enemies.RemoveAt(i);
            }

            _flashes.RemoveAll(f => f.Expiry <= now);

            if (_waveRunning && _waveSpawnsLeft == 0 && _enemies.Count == 0)
            {
                _waveRunning = false;
                int bonus = 90 + 20 * _wave;
                _cash += bonus;
                _score += 100 * _wave;
                _message = $"WAVE {_wave} CLEARED! +\${bonus} CASH. SPACE FOR NEXT WAVE.";
            }

            _surface.InvalidateVisual();
        }

        private void SpawnEnemy()
        {
            int ordinal = 6 + 2 * _wave - _waveSpawnsLeft;
            bool heavy = _wave >= 3 && ordinal % 6 == 0;
            bool fast = _wave >= 2 && ordinal % 4 == 0 && !heavy;

            double hp = (heavy ? 200 : fast ? 60 : 100)
                + _wave * (heavy ? 45 : 20);
            _enemies.Add(new Enemy
            {
                Progress = 0,
                Health = hp,
                MaxHealth = hp,
                Speed = fast ? 2.8 + _wave * 0.08
                    : heavy ? 1.1 + _wave * 0.035
                    : 1.6 + _wave * 0.055,
                Bounty = heavy ? 32 : fast ? 17 : 22,
                DamageToHub = heavy ? 3 : 1,
                Variant = heavy ? 2 : fast ? 1 : 0
            });
        }

        private bool InRange(DefenseTower tower, Enemy enemy)
        {
            var a = Center(tower.X, tower.Y);
            var b = EnemyPoint(enemy);
            double dx = a.X - b.X, dy = a.Y - b.Y;
            double radius = tower.Range * Cell;
            return dx * dx + dy * dy <= radius * radius;
        }

        private Point EnemyPoint(Enemy enemy)
        {
            double progress = Math.Clamp(enemy.Progress, 0, _route.Count - 1);
            int start = Math.Min(_route.Count - 2, (int)progress);
            double t = progress - start;
            var a = Center(_route[start].X, _route[start].Y);
            var b = Center(_route[start + 1].X, _route[start + 1].Y);
            return new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private static Point Center(int x, int y) =>
            new(FieldX + (x + 0.5) * Cell, FieldY + (y + 0.5) * Cell);

        private void OnKey(object sender, KeyEventArgs e)
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
                NewGame();
                return;
            }

            if (e.Key == Key.L)
            {
                e.Handled = true;
                _leaderboardVisible = !_leaderboardVisible;
                if (_leaderboardVisible)
                {
                    _leaderboardPausedGame = _started && !_paused && !_gameOver;
                    if (_leaderboardPausedGame)
                        SetPaused(true);
                }
                else if (_leaderboardPausedGame && !_gameOver)
                {
                    _leaderboardPausedGame = false;
                    SetPaused(false);
                }
                _surface.InvalidateVisual();
                return;
            }

            if (_leaderboardVisible)
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
                    StartWave();
                }
                else if (!_paused && !_waveRunning)
                {
                    StartWave();
                }
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

            TowerKind? kind = e.Key switch
            {
                Key.D1 or Key.NumPad1 => TowerKind.Pulse,
                Key.D2 or Key.NumPad2 => TowerKind.Sniper,
                Key.D3 or Key.NumPad3 => TowerKind.Jammer,
                _ => null
            };
            if (kind != null)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    BuyTower(kind.Value);
                return;
            }

            if (e.Key == Key.U)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    UpgradeSelected();
            }
            else if (e.Key == Key.X)
            {
                e.Handled = true;
                if (!e.IsRepeat)
                    SellSelected();
            }
        }

        private void OnMouseClick(object sender, MouseButtonEventArgs e)
        {
            if (!_started || _paused || _gameOver || _leaderboardVisible)
                return;

            var p = e.GetPosition(_surface);
            if (p.X >= FieldX && p.X < FieldX + FieldWidth &&
                p.Y >= FieldY && p.Y < FieldY + FieldHeight)
            {
                int x = (int)((p.X - FieldX) / Cell);
                int y = (int)((p.Y - FieldY) / Cell);
                _selectedCell = _road.Contains((x, y)) ? null : (x, y);
                _surface.InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (p.X >= 610 && p.X <= 889)
            {
                if (p.Y >= 270 && p.Y < 311) BuyTower(TowerKind.Pulse);
                else if (p.Y >= 317 && p.Y < 358) BuyTower(TowerKind.Sniper);
                else if (p.Y >= 364 && p.Y < 405) BuyTower(TowerKind.Jammer);
                else if (p.Y >= 423 && p.Y < 460) UpgradeSelected();
                else if (p.Y >= 466 && p.Y < 503) SellSelected();
                else return;
                e.Handled = true;
            }
        }

        private void BuyTower(TowerKind kind)
        {
            if (!_selectedCell.HasValue)
            {
                _message = "SELECT AN EMPTY GRASS TILE WITH YOUR MOUSE FIRST.";
                _surface.InvalidateVisual();
                return;
            }

            var (x, y) = _selectedCell.Value;
            if (_road.Contains((x, y)) || _towers.Any(t => t.X == x && t.Y == y))
            {
                _message = "THIS SQUARE ALREADY HAS A DEFENSE.";
                _surface.InvalidateVisual();
                return;
            }

            int price = kind switch
            {
                TowerKind.Pulse => 100,
                TowerKind.Sniper => 180,
                _ => 140
            };
            if (_cash < price)
            {
                _message = $"NOT ENOUGH CASH! NEED \${price}.";
                _surface.InvalidateVisual();
                return;
            }

            _cash -= price;
            _towers.Add(new DefenseTower(x, y, kind));
            _message = $"{kind.ToString().ToUpperInvariant()} TOWER DEPLOYED. -\${price}.";
            _surface.InvalidateVisual();
        }

        private void UpgradeSelected()
        {
            var tower = SelectedTower();
            if (tower == null)
            {
                _message = "SELECT AN EXISTING DEFENSE TO UPGRADE.";
            }
            else if (tower.Level >= 3)
            {
                _message = "THIS DEFENSE IS ALREADY MAXED OUT.";
            }
            else
            {
                int price = tower.NextUpgradeCost;
                if (_cash < price)
                    _message = $"UPGRADE NEEDS \${price}.";
                else
                {
                    _cash -= price;
                    tower.Level++;
                    tower.TotalSpent += price;
                    _message = $"DEFENSE UPGRADED TO LEVEL {tower.Level}. -\${price}.";
                }
            }
            _surface.InvalidateVisual();
        }

        private void SellSelected()
        {
            var tower = SelectedTower();
            if (tower == null)
                _message = "SELECT AN EXISTING DEFENSE TO SELL.";
            else
            {
                int refund = (int)(tower.TotalSpent * 0.60);
                _cash += refund;
                _towers.Remove(tower);
                _message = $"DEFENSE SOLD FOR \${refund}.";
            }
            _surface.InvalidateVisual();
        }

        private DefenseTower? SelectedTower() => _selectedCell.HasValue
            ? _towers.FirstOrDefault(t =>
                t.X == _selectedCell.Value.X && t.Y == _selectedCell.Value.Y)
            : null;

        private void SetPaused(bool value)
        {
            _paused = value;
            if (value) _clock.Stop();
            else _clock.Start();
            _surface.InvalidateVisual();
        }

        private void EndGame()
        {
            _gameOver = true;
            _clock.Stop();
            _timer.Stop();
            _message = "NETWORK DOWN. DISPATCH WANTS AN EXPLANATION.";
            if (_score > _localBest)
            {
                _localBest = _score;
                SaveBest(_score);
            }
            _surface.InvalidateVisual();
        }

        private void Draw(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Night, null, new Rect(0, 0, 940, 650));
            dc.DrawRectangle(Panel, new Pen(GridLine, 2),
                new Rect(12, 12, 894, 580));

            Text(dc, "GRID UNDER ATTACK", 27, 23, 29, Cyan, dpi, true);
            Text(dc, "DEFEND THE RADIO NETWORK", 28, 65, 13, Muted, dpi);
            Text(dc, _message, 28, 98, 13, Gold, dpi, true, 840);

            DrawField(dc, dpi);
            DrawSidebar(dc, dpi);
            dc.DrawRectangle(Panel, new Pen(GridLine, 1),
                new Rect(25, 538, 861, 43));
            Text(dc, "CLICK GRASS TO SELECT", 36, 551, 13, White, dpi, true);
            Text(dc, "1/2/3 BUY", 278, 551, 13, Mint, dpi, true);
            Text(dc, "U UPGRADE", 429, 551, 13, Gold, dpi, true);
            Text(dc, "X SELL", 584, 551, 13, Lilac, dpi, true);
            Text(dc, "SPACE NEXT WAVE", 703, 551, 12, Cyan, dpi, true);

            if (!_started || _paused || _gameOver)
                DrawOverlay(dc, dpi);
            if (_leaderboardVisible)
                DrawLeaderboard(dc, dpi);
        }

        private void DrawField(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Ground, new Pen(Cyan, 2),
                new Rect(FieldX - 2, FieldY - 2, FieldWidth + 4, FieldHeight + 4));

            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Columns; x++)
            {
                var point = new Rect(FieldX + x * Cell, FieldY + y * Cell, Cell, Cell);
                bool road = _road.Contains((x, y));
                dc.DrawRectangle(road ? Road : Ground,
                    new Pen(GridLine, 0.65), point);
                if (!road)
                {
                    dc.DrawEllipse(Paint("#1A3652"), null,
                        Center(x, y), 2.1, 2.1);
                }
            }

            // Route starts purple and ends red at the vulnerable hub.
            var origin = Center(_route[0].X, _route[0].Y);
            var hub = Center(_route[^1].X, _route[^1].Y);
            dc.DrawEllipse(Lilac, null, origin, 10, 10);
            dc.DrawEllipse(Red, new Pen(White, 1), hub, 12, 12);
            Text(dc, "H", hub.X - 6, hub.Y - 11, 17, Night, dpi, true);

            foreach (var tower in _towers)
            {
                var p = Center(tower.X, tower.Y);
                Brush fill = tower.Kind == TowerKind.Pulse ? Cyan :
                    tower.Kind == TowerKind.Sniper ? Gold : Lilac;
                dc.DrawEllipse(Paint("#08182E"), new Pen(fill, 2.1), p, 12, 12);
                var glyph = tower.Kind == TowerKind.Pulse ? "P" :
                    tower.Kind == TowerKind.Sniper ? "S" : "J";
                Text(dc, glyph, p.X - 6, p.Y - 10, 17, fill, dpi, true);
                for (int dot = 0; dot < tower.Level; dot++)
                    dc.DrawEllipse(fill, null,
                        new Point(p.X - 8 + dot * 8, p.Y + 16), 2.1, 2.1);
            }

            if (_selectedCell.HasValue)
            {
                var (x, y) = _selectedCell.Value;
                dc.DrawRectangle(null, new Pen(Gold, 2.6),
                    new Rect(FieldX + x * Cell + 1, FieldY + y * Cell + 1,
                        Cell - 2, Cell - 2));
                var selectedTower = SelectedTower();
                if (selectedTower != null)
                {
                    dc.DrawEllipse(null,
                        new Pen(new SolidColorBrush(Color.FromArgb(115, 255, 224, 131)), 1.2),
                        Center(x, y), selectedTower.Range * Cell,
                        selectedTower.Range * Cell);
                }
            }

            double now = _clock.Elapsed.TotalSeconds;
            foreach (var flash in _flashes)
                dc.DrawLine(new Pen(flash.Color, 2.5), flash.From, flash.To);

            foreach (var enemy in _enemies)
            {
                var p = EnemyPoint(enemy);
                Brush fill = enemy.Variant == 2 ? Gold :
                    enemy.Variant == 1 ? Red : Lilac;
                double radius = enemy.Variant == 2 ? 12 : enemy.Variant == 1 ? 7 : 9;
                dc.DrawEllipse(fill, new Pen(Night, 1.5), p, radius, radius);
                dc.DrawRectangle(Red, null, new Rect(p.X - 12, p.Y - radius - 8, 24, 3.8));
                dc.DrawRectangle(Mint, null,
                    new Rect(p.X - 12, p.Y - radius - 8,
                        24 * Math.Clamp(enemy.Health / enemy.MaxHealth, 0, 1), 3.8));
                if (enemy.SlowUntil > now)
                    dc.DrawEllipse(null, new Pen(Cyan, 1.5), p, radius + 3, radius + 3);
            }
        }

        private void DrawSidebar(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Panel, new Pen(GridLine, 1),
                new Rect(608, FieldY, 276, FieldHeight));

            Text(dc, "NETWORK STATUS", 622, 140, 17, Cyan, dpi, true);
            Text(dc, "HUB HP", 622, 174, 12, Muted, dpi, true);
            Text(dc, _baseHealth.ToString("00"), 622, 192, 26,
                _baseHealth < 8 ? Red : Mint, dpi, true);
            Text(dc, "CASH", 733, 174, 12, Muted, dpi, true);
            Text(dc, "$" + _cash.ToString("N0", CultureInfo.InvariantCulture),
                732, 192, 22, Gold, dpi, true);
            Text(dc, "WAVE " + _wave, 622, 231, 16, White, dpi, true);
            Text(dc, "KILLS " + _kills, 752, 231, 14, Muted, dpi);

            DrawButton(dc, "1  PULSE    $100", 270, Cyan, dpi);
            DrawButton(dc, "2  SNIPER   $180", 317, Gold, dpi);
            DrawButton(dc, "3  JAMMER   $140", 364, Lilac, dpi);

            var tower = SelectedTower();
            string upgrade = tower == null ? "U  UPGRADE (SELECT)"
                : tower.Level >= 3 ? "U  MAX LEVEL"
                : $"U  UPGRADE  \${tower.NextUpgradeCost}";
            DrawButton(dc, upgrade, 423, Mint, dpi);

            string sell = tower == null ? "X  SELL (SELECT)"
                : $"X  SELL FOR \${(int)(tower.TotalSpent * 0.60)}";
            DrawButton(dc, sell, 466, Red, dpi);
        }

        private static void DrawButton(
            DrawingContext dc, string label, double y, Brush ink, double dpi)
        {
            dc.DrawRectangle(Paint("#182D4B"), new Pen(ink, 1),
                new Rect(620, y, 253, y >= 423 ? 36 : 40));
            Text(dc, label, 632, y + 9, 15, ink, dpi, true);
        }

        private void DrawOverlay(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(237, 5, 12, 27)),
                new Pen(Cyan, 2.5), new Rect(109, 172, 692, 302));
            Text(dc, _gameOver ? "NETWORK OFFLINE" :
                _paused ? "PAUSED — RADIO SILENCE" : "GRID UNDER ATTACK",
                150, 200, 28, _gameOver ? Red : Cyan, dpi, true);

            if (!_started)
            {
                Text(dc, "STOP THE INTERFERENCE BEFORE IT HITS THE HUB.",
                    151, 263, 17, White, dpi, true);
                Text(dc, "CLICK GRASS. BUY PULSE, SNIPER OR JAMMER TOWERS.",
                    151, 297, 15, Muted, dpi);
                Text(dc, "EARN CASH, UPGRADE DEFENSES, SURVIVE THE WAVES.",
                    151, 324, 15, Gold, dpi);
                Text(dc, "PRESS SPACE TO START", 151, 380, 25, Mint, dpi, true);
            }
            else if (_gameOver)
            {
                Text(dc, $"SURVIVED WAVE {_wave}  •  {_kills} INVADERS STOPPED",
                    151, 279, 18, White, dpi, true);
                Text(dc, $"SCORE {_score:N0}    LOCAL BEST {_localBest:N0}",
                    151, 317, 18, Gold, dpi, true);
                Text(dc, "R TO RETRY       ESC TO EXIT", 151, 391, 18, Mint, dpi, true);
            }
            else
            {
                Text(dc, $"WAVE {_wave}     SCORE {_score:N0}",
                    151, 285, 21, Gold, dpi, true);
                Text(dc, "P TO RESUME        ESC TO EXIT", 151, 389, 18, White, dpi, true);
            }

            Text(dc, "L  LEADERBOARD (COMING SOON)", 151, 446, 12, Muted, dpi);
        }

        private void DrawLeaderboard(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(248, 7, 18, 38)),
                new Pen(Gold, 2.4), new Rect(120, 154, 680, 330));
            Text(dc, "TOWER DEFENSE HIGH SCORES", 158, 184, 23, Gold, dpi, true);
            Text(dc, "Personal best: " + _localBest.ToString("N0", CultureInfo.InvariantCulture),
                160, 263, 19, White, dpi, true);
            Text(dc, "Shop-wide competition is the next upgrade.",
                160, 307, 17, Muted, dpi);
            Text(dc, "L  BACK", 160, 421, 16, Cyan, dpi, true);
        }

        private static void Text(DrawingContext dc, string value,
            double x, double y, double size, Brush brush, double dpi,
            bool bold = false, double maxWidth = 0)
        {
            var t = new FormattedText(
                value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Consolas"), FontStyles.Normal,
                    bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal), size, brush, dpi);
            if (maxWidth > 0)
                t.MaxTextWidth = maxWidth;
            dc.DrawText(t, new Point(x, y));
        }

        private static Brush Paint(string color)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
            brush.Freeze();
            return brush;
        }

        private static string BestFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartGridSuite", "Arcade", "tower-defense-best.txt");

        private static int ReadBest()
        {
            try
            {
                return File.Exists(BestFile) &&
                    int.TryParse(File.ReadAllText(BestFile),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out var best)
                    ? Math.Max(0, best) : 0;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private static void SaveBest(int score)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BestFile)!);
                File.WriteAllText(BestFile, score.ToString(CultureInfo.InvariantCulture));
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

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _clock.Stop();
            _timer.Tick -= Tick;
            PreviewKeyDown -= OnKey;
            PreviewMouseLeftButtonDown -= OnMouseClick;
            _host.Closed -= HostClosed;
            _host.IsVisibleChanged -= HostVisibilityChanged;
            _host.StateChanged -= HostStateChanged;
            Closed -= OnWindowClosed;

            if (ReferenceEquals(_active, this))
                _active = null;
        }

        private enum TowerKind
        {
            Pulse,
            Sniper,
            Jammer
        }

        private sealed class DefenseTower
        {
            public int X { get; }
            public int Y { get; }
            public TowerKind Kind { get; }
            public int Level { get; set; } = 1;
            public double ReloadRemaining { get; set; }
            public int TotalSpent { get; set; }

            public DefenseTower(int x, int y, TowerKind kind)
            {
                X = x;
                Y = y;
                Kind = kind;
                TotalSpent = kind switch
                {
                    TowerKind.Pulse => 100,
                    TowerKind.Sniper => 180,
                    _ => 140
                };
            }

            public int NextUpgradeCost => (Level + 1) * 100;

            public double Range => (Kind switch
            {
                TowerKind.Pulse => 2.5,
                TowerKind.Sniper => 4.3,
                _ => 2.8
            }) + (Level - 1) * 0.25;

            public double Damage => (Kind switch
            {
                TowerKind.Pulse => 27,
                TowerKind.Sniper => 80,
                _ => 10
            }) * (1 + (Level - 1) * 0.65);

            public double Cooldown => (Kind switch
            {
                TowerKind.Pulse => 0.52,
                TowerKind.Sniper => 1.45,
                _ => 0.64
            }) / (1 + (Level - 1) * 0.14);
        }

        private sealed class Enemy
        {
            public double Progress { get; set; }
            public double Health { get; set; }
            public double MaxHealth { get; set; }
            public double Speed { get; set; }
            public double SlowUntil { get; set; }
            public int Bounty { get; set; }
            public int DamageToHub { get; set; }
            public int Variant { get; set; }
        }

        private sealed record LaserFlash(
            Point From, Point To, double Expiry, Brush Color);

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

            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);
                _game.Draw(dc, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            }
        }
    }
}
