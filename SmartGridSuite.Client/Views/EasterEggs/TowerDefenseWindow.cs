using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
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
        // Virtual design is scaled by Viewbox to fit available laptop space.
        // Hit testing uses unscaled design coordinates inside the Viewbox.
        private const double DesignWidth = 1320;
        private const double DesignHeight = 820;
        private const double FieldX = 24;
        private const double FieldY = 129;
        private const double FieldWidth = 768;
        private const double FieldHeight = 608;
        private const double PanelX = 814;
        private const double PanelWidth = 484;
        private const double FirstUpgradeY = 303;
        private const double UpgradePitch = 54.5;
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
        private readonly Canvas _controls = new();
        private readonly Button[] _upgradeButtons = new Button[8];
        private readonly TextBlock[] _upgradeNames = new TextBlock[8];
        private readonly TextBlock[] _upgradeDetails = new TextBlock[8];
        private readonly TextBlock[] _upgradeLevels = new TextBlock[8];
        private readonly TextBlock[] _upgradePrices = new TextBlock[8];
        private readonly Button[] _categoryButtons = new Button[3];
        private Button _workshopButton = null!;
        private Button _towerRepairButton = null!;
        private Button _fenceRepairButton = null!;
        private Button _nextWaveButton = null!;
        private readonly Button[] _perkButtons = new Button[3];
        private string _lastUpgradeSignature = "";
        private DateTime _lastControlsRefreshUtc = DateTime.MinValue;
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
        private double _leftGunCooldown;
        private double _rightGunCooldown;
        private Point? _leftGunAim;
        private Point? _rightGunAim;
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
            Width = Math.Min(1360, Math.Max(760, SystemParameters.WorkArea.Width - 24));
            Height = Math.Min(890, Math.Max(535, SystemParameters.WorkArea.Height - 18));
            MinWidth = 760;
            MinHeight = 535;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Backdrop;
            FontFamily = new FontFamily("Consolas");
            Focusable = true;

            _surface = new GameSurface(this);
            var design = new Grid
            {
                Width = DesignWidth,
                Height = DesignHeight
            };
            design.Children.Add(_surface);
            _controls.Width = DesignWidth;
            _controls.Height = DesignHeight;
            design.Children.Add(_controls);
            BuildUpgradeControls();

            // Larger by default, with graceful downscaling for small screens.
            // This keeps the radio tower, panel, and buttons inside the window.
            Content = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = design
            };

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            _timer.Tick += Tick;
            _timer.Tick += RefreshControlsOnTick;
            PreviewKeyDown += HandleKey;

            _host.Closed += HostClosed;
            _host.IsVisibleChanged += HostVisibilityChanged;
            _host.StateChanged += HostStateChanged;
            Closed += WindowClosed;
            Loaded += (_, _) =>
            {
                NewGame();
                RefreshUpgradeControls();
                Activate();
                Keyboard.Focus(this);
            };
            Deactivated += (_, _) =>
            {
                if (_started && !_paused && !_gameOver)
                    SetPaused(true);
            };
        }

        private static ControlTemplate MakeButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "chrome";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1.2));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
            var source = new RelativeSource(RelativeSourceMode.TemplatedParent);
            border.SetBinding(Border.BackgroundProperty,
                new Binding("Background") { RelativeSource = source });
            border.SetBinding(Border.BorderBrushProperty,
                new Binding("BorderBrush") { RelativeSource = source });

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,
                HorizontalAlignment.Stretch);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.ContentProperty,
                new Binding("Content") { RelativeSource = source });
            border.AppendChild(presenter);
            template.VisualTree = border;

            var hover = new Trigger
            {
                Property = Button.IsMouseOverProperty,
                Value = true
            };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                Paint("#315574"), "chrome"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty,
                Gold, "chrome"));
            template.Triggers.Add(hover);

            var pressed = new Trigger
            {
                Property = Button.IsPressedProperty,
                Value = true
            };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty,
                Paint("#426483"), "chrome"));
            template.Triggers.Add(pressed);

            var disabled = new Trigger
            {
                Property = Button.IsEnabledProperty,
                Value = false
            };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.54));
            template.Triggers.Add(disabled);
            return template;
        }

        private static Button ArcadeButton(string title, double width,
            double height, Brush accent)
        {
            return new Button
            {
                Content = title,
                Width = width,
                Height = height,
                Background = ButtonFace,
                BorderBrush = accent,
                Foreground = White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                Cursor = Cursors.Hand,
                Focusable = false,
                Template = MakeButtonTemplate()
            };
        }

        private static TextBlock CardText(
            double size, Brush color, bool bold = false)
        {
            return new TextBlock
            {
                FontSize = size,
                Foreground = color,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontFamily = new FontFamily("Consolas"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void BuildUpgradeControls()
        {
            for (int category = 0; category < 3; category++)
            {
                int selectedCategory = category;
                var button = ArcadeButton(
                    CategoryNames[category], 148, 37, Cyan);
                button.Click += (_, _) =>
                {
                    _progression.SelectedCategory = selectedCategory;
                    DescribeSelection();
                    RefreshUpgradeControls();
                };
                _categoryButtons[category] = button;
                _controls.Children.Add(button);
            }

            for (int row = 0; row < 8; row++)
            {
                int selectedRow = row;
                var button = ArcadeButton("", PanelWidth - 22,
                    50, GridInk);
                button.BorderBrush = GridInk;
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(122)
                });
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                });
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                });

                _upgradeNames[row] = CardText(15.6, White, true);
                _upgradeDetails[row] = CardText(12, Muted);
                _upgradeLevels[row] = CardText(11.7, Cyan, true);
                _upgradePrices[row] = CardText(15, Green, true);
                _upgradeLevels[row].TextAlignment = TextAlignment.Right;
                _upgradePrices[row].TextAlignment = TextAlignment.Right;

                Grid.SetRow(_upgradeNames[row], 0);
                Grid.SetColumn(_upgradeNames[row], 0);
                Grid.SetRow(_upgradeDetails[row], 1);
                Grid.SetColumn(_upgradeDetails[row], 0);
                Grid.SetRow(_upgradeLevels[row], 1);
                Grid.SetColumn(_upgradeLevels[row], 1);
                Grid.SetRow(_upgradePrices[row], 0);
                Grid.SetColumn(_upgradePrices[row], 1);

                grid.Children.Add(_upgradeNames[row]);
                grid.Children.Add(_upgradeDetails[row]);
                grid.Children.Add(_upgradeLevels[row]);
                grid.Children.Add(_upgradePrices[row]);
                button.Content = grid;
                ToolTipService.SetShowOnDisabled(button, true);
                button.Click += (_, _) =>
                {
                    BuySelected(selectedRow);
                    RefreshUpgradeControls();
                };
                _upgradeButtons[row] = button;
                _controls.Children.Add(button);
            }

            _workshopButton = ArcadeButton("RESEARCH WORKSHOP", 180, 36, Purple);
            _workshopButton.Click += (_, _) =>
            {
                ToggleWorkshop();
                RefreshUpgradeControls();
            };
            _controls.Children.Add(_workshopButton);

            _towerRepairButton = ArcadeButton("REPAIR TOWER", 164, 36, Green);
            _towerRepairButton.Click += (_, _) =>
            {
                RepairTower();
                RefreshUpgradeControls();
            };
            _controls.Children.Add(_towerRepairButton);

            _fenceRepairButton = ArcadeButton("REPAIR FENCE", 160, 36, Cyan);
            _fenceRepairButton.Click += (_, _) =>
            {
                RepairFence();
                RefreshUpgradeControls();
            };
            _controls.Children.Add(_fenceRepairButton);

            _nextWaveButton = ArcadeButton("PREPARE", 177, 36, Gold);
            _nextWaveButton.Click += (_, _) =>
            {
                PrepareOrStartWave();
                RefreshUpgradeControls();
            };
            _controls.Children.Add(_nextWaveButton);

            string[] choices =
            {
                "OVERTIME PAY • CASH",
                "STORM RECOVERY • HEAL",
                "R&D GRANT • RESEARCH"
            };
            for (int choice = 0; choice < 3; choice++)
            {
                int selectedChoice = choice + 1;
                _perkButtons[choice] = ArcadeButton(
                    choices[choice], 913, 51,
                    choice == 0 ? Green : choice == 1 ? Cyan : Purple);
                _perkButtons[choice].Click += (_, _) =>
                {
                    SelectPerk(selectedChoice);
                    RefreshUpgradeControls();
                };
                Canvas.SetLeft(_perkButtons[choice], 173);
                Canvas.SetTop(_perkButtons[choice], 315 + 65 * choice);
                _controls.Children.Add(_perkButtons[choice]);
            }
        }

        private void RefreshControlsOnTick(object? sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _lastControlsRefreshUtc).TotalMilliseconds < 170)
                return;
            _lastControlsRefreshUtc = DateTime.UtcNow;
            RefreshUpgradeControls();
        }

        private void RefreshUpgradeControls()
        {
            if (_upgradeButtons[0] == null)
                return;

            bool workshop = _progression.WorkshopOpen;
            bool gameUsable = _started && !_gameOver && !_paused &&
                !_showHelp && !_perkPending;
            bool rowsVisible = workshop || gameUsable;

            string signature = string.Join("|",
                workshop, _started, _paused, _gameOver, _showHelp,
                _perkPending, _wave, _cash, _progression.Credits,
                _progression.SelectedCategory, _progression.SelectedRow,
                _health.ToString("0", MoneyCulture),
                _fenceHealth.ToString("0", MoneyCulture),
                string.Join(",", Enumerable.Range(0, 8).Select(row =>
                {
                    int index = _progression.SelectedCategory * 8 + row;
                    return _progression.Level(index) + ":" +
                        _progression.PermanentLevel(index);
                })));

            if (signature == _lastUpgradeSignature)
                return;
            _lastUpgradeSignature = signature;

            for (int category = 0; category < 3; category++)
            {
                var button = _categoryButtons[category];
                button.Visibility = rowsVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                button.Background = _progression.SelectedCategory == category
                    ? Paint("#294D69") : ButtonFace;
                button.BorderBrush = _progression.SelectedCategory == category
                    ? Gold : GridInk;
                Canvas.SetLeft(button, workshop ? 183 + category * 315
                    : PanelX + 10 + category * 155);
                Canvas.SetTop(button, workshop ? 217 : 258);
                button.Width = workshop ? 302 : 148;
            }

            for (int row = 0; row < 8; row++)
            {
                int index = _progression.SelectedCategory * 8 + row;
                var upgrade = TowerDefenseProgression.Catalog[index];
                bool unlocked = _progression.Unlocked(index, _wave);
                long price = workshop
                    ? _progression.WorkshopPrice(index)
                    : _progression.RunPrice(index);
                bool affordable = workshop
                    ? _progression.Credits >= price
                    : _cash >= price;

                var button = _upgradeButtons[row];
                button.Visibility = rowsVisible
                    ? Visibility.Visible : Visibility.Collapsed;
                button.IsEnabled = unlocked && affordable && rowsVisible;
                button.BorderBrush = row == _progression.SelectedRow
                    ? Gold : GridInk;
                button.Background = row == _progression.SelectedRow
                    ? Paint("#223A56") : ButtonFace;
                button.Width = workshop ? 947 : PanelWidth - 22;
                button.Height = workshop ? 51 : 50;
                Canvas.SetLeft(button, workshop ? 182 : PanelX + 11);
                Canvas.SetTop(button, workshop ? 266 + row * 55
                    : FirstUpgradeY + row * UpgradePitch);

                _upgradeNames[row].Text = upgrade.Name;
                _upgradeDetails[row].Text = upgrade.Detail;
                _upgradeLevels[row].Text = "LV " +
                    (workshop ? _progression.PermanentLevel(index)
                              : _progression.Level(index));
                _upgradePrices[row].Text = !unlocked
                    ? "WAVE " + upgrade.UnlockWave : Money(price);
                _upgradePrices[row].Foreground = !unlocked
                    ? Red : affordable ? Green : Gold;
                button.ToolTip = upgrade.Detail + Environment.NewLine +
                    (workshop ? "Permanent research" : "Current run") +
                    " • " + (!unlocked
                        ? "Unlock at wave " + upgrade.UnlockWave
                        : "Price: " + Money(price)) +
                    (affordable ? "" : " • Not enough funds");
            }

            bool showFooter = !workshop && !_showHelp && !_perkPending;
            _workshopButton.Visibility = !_showHelp && !_perkPending &&
                (workshop || !_waveActive && (!_started || _gameOver))
                    ? Visibility.Visible : Visibility.Collapsed;
            _workshopButton.Content = workshop
                ? "CLOSE WORKSHOP" : "RESEARCH WORKSHOP";
            Canvas.SetLeft(_workshopButton, workshop ? 1052 : 949);
            Canvas.SetTop(_workshopButton, workshop ? 709 : 764);
            _workshopButton.Width = workshop ? 168 : 160;

            _towerRepairButton.Visibility = showFooter && _started && !_gameOver
                ? Visibility.Visible : Visibility.Collapsed;
            _towerRepairButton.Content = "TOWER +" +
                Math.Ceiling(TowerRepairAvailable).ToString("0", MoneyCulture) +
                "HP  " + Money(TowerRepairCost);
            _towerRepairButton.ToolTip = "Tower: " +
                Math.Ceiling(_health).ToString("0", MoneyCulture) +
                "/" + MaxTowerHealth.ToString("0", MoneyCulture) +
                " HP. Restores up to " + TowerRepairCapacity.ToString("0", MoneyCulture) +
                " HP for " + Money(TowerRepairCost) + ".";
            _towerRepairButton.IsEnabled = gameUsable &&
                TowerRepairAvailable > 0.01 && _cash >= TowerRepairCost;
            _towerRepairButton.Width = 208;
            Canvas.SetLeft(_towerRepairButton, 525);
            Canvas.SetTop(_towerRepairButton, 764);

            _fenceRepairButton.Visibility = showFooter && _started && !_gameOver
                ? Visibility.Visible : Visibility.Collapsed;
            _fenceRepairButton.Content = "FENCE +" +
                Math.Ceiling(FenceRepairAvailable).ToString("0", MoneyCulture) +
                "HP  " + Money(FenceRepairCost);
            _fenceRepairButton.ToolTip = "Fence: " +
                Math.Ceiling(_fenceHealth).ToString("0", MoneyCulture) +
                "/" + FenceMaxHealth.ToString("0", MoneyCulture) +
                " HP. Restores up to " + FenceRepairCapacity.ToString("0", MoneyCulture) +
                " HP for " + Money(FenceRepairCost) + ".";
            _fenceRepairButton.IsEnabled = gameUsable &&
                _fenceLevel > 0 && FenceRepairAvailable > 0.01 &&
                _cash >= FenceRepairCost;
            _fenceRepairButton.Width = 207;
            Canvas.SetLeft(_fenceRepairButton, 738);
            Canvas.SetTop(_fenceRepairButton, 764);

            _nextWaveButton.Visibility = showFooter && !_gameOver
                ? Visibility.Visible : Visibility.Collapsed;
            _nextWaveButton.IsEnabled = !workshop && !_paused &&
                !_waveActive && !_perkPending && !_showHelp;
            _nextWaveButton.Content = !_started ? "PREPARE" :
                _wave == 0 ? "START WAVE 1" : "NEXT WAVE";
            Canvas.SetLeft(_nextWaveButton, 1120);
            Canvas.SetTop(_nextWaveButton, 764);

            for (int choice = 0; choice < 3; choice++)
                _perkButtons[choice].Visibility = _perkPending &&
                    !_showHelp && !workshop
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PrepareOrStartWave()
        {
            if (_progression.WorkshopOpen || _perkPending ||
                _paused || _gameOver)
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
            else if (!_waveActive)
                StartWave();
            RefreshUpgradeControls();
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
            _leftGunCooldown = 0.15;
            _rightGunCooldown = 0.65;
            _leftGunAim = null;
            _rightGunAim = null;
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
            RefreshUpgradeControls();
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
            RefreshUpgradeControls();
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
            _leftGunCooldown = Math.Max(0, _leftGunCooldown - dt);
            _rightGunCooldown = Math.Max(0, _rightGunCooldown - dt);
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
            // Mounted guns deal real damage before automated cleanup teams.
            FireMountedGun(now, false);
            FireMountedGun(now, true);
            ResolveDefeatedThreats();

            if (_patrolCooldown <= 0)
            {
                int count = Math.Min(_thieves.Count,
                    1 + vehicles / 2);
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
                _patrolCooldown = Math.Max(0.8,
                    4.5 / (1 + vehicles * 0.13));
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

            // Q and E are no longer required. Nearby threats automatically
            // trigger each response service as soon as its cooldown ends.
            if (_securitySweepCooldown <= 0 &&
                _thieves.Any(t => t.Distance <= 165))
                CallSecuritySweep();
            if (_rfSweepCooldown <= 0 && _jammers.Count > 0)
                CallRfSweep();

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

        private void FireMountedGun(double now, bool right)
        {
            double cooldown = right ? _rightGunCooldown : _leftGunCooldown;
            if (cooldown > 0)
                return;

            int gunPower = _progression.Level(0);
            int fireRate = _progression.Level(1);
            int rangeLevel = _progression.Level(2);
            double range = Math.Min(500, 225 + rangeLevel * 23);

            CopperThief? thief = _thieves
                .Where(t => t.Health > 0 && t.Distance <= range)
                .OrderBy(t => t.Distance).FirstOrDefault();
            InterferenceTower? rf = _jammers
                .Where(t => t.Health > 0 &&
                    Distance(new Point(t.X, t.Y), new Point(TowerX, TowerY)) <= range)
                .OrderBy(t => Distance(new Point(t.X, t.Y),
                    new Point(TowerX, TowerY))).FirstOrDefault();

            Point from = new(TowerX + (right ? 21 : -21), TowerY - 23);
            Point? target = null;
            Brush ink;
            double damage = (right ? 12 : 14) +
                gunPower * (right ? 5.5 : 6.5);

            if ((right && rf != null) || (thief == null && rf != null))
            {
                rf!.Health -= damage;
                target = new Point(rf.X, rf.Y);
                ink = Purple;
            }
            else if (thief != null)
            {
                thief.Health -= damage;
                target = ThiefPoint(thief);
                ink = Gold;
            }
            else
            {
                if (right) _rightGunCooldown = 0.12;
                else _leftGunCooldown = 0.12;
                return;
            }

            _defenseFlashes.Add(new DefenseFlash(from, target.Value,
                now + 0.13, ink));
            if (right) _rightGunAim = target;
            else _leftGunAim = target;
            double reload = Math.Max(0.17, (right ? 1.35 : 1.02) /
                (1 + fireRate * 0.17));
            if (right) _rightGunCooldown = reload;
            else _leftGunCooldown = reload;
        }

        private void ResolveDefeatedThreats()
        {
            for (int i = _thieves.Count - 1; i >= 0; i--)
            {
                if (_thieves[i].Health > 0) continue;
                _thieves.RemoveAt(i);
                _thievesStopped++;
                _cash = AddCash(_cash, 25 + _progression.Level(17) * 4);
                _score = AddCash(_score, 40);
            }
            for (int i = _jammers.Count - 1; i >= 0; i--)
            {
                if (_jammers[i].Health > 0) continue;
                _jammers.RemoveAt(i);
                _interferenceRemoved++;
                _cash = AddCash(_cash, 45);
                _score = AddCash(_score, 65);
            }
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
                Speed = 22 + _wave * 2.2 + _random.NextDouble() * 9,
                MaxHealth = 22 + _wave * 5,
                Health = 22 + _wave * 5
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
                Y = TowerY + Math.Sin(angle) * radius,
                MaxHealth = 42 + _wave * 9,
                Health = 42 + _wave * 9
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
            RefreshUpgradeControls();
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
            RefreshUpgradeControls();
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
            RefreshUpgradeControls();
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
                    RefreshUpgradeControls();
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

            // Milestone rewards temporarily take precedence over the normal
            // 1-8 purchase hotkeys. Space cannot skip a reward decision.
            if (_perkPending && !_progression.WorkshopOpen)
            {
                int choice = e.Key switch
                {
                    Key.D1 or Key.NumPad1 => 1,
                    Key.D2 or Key.NumPad2 => 2,
                    Key.D3 or Key.NumPad3 => 3,
                    _ => 0
                };
                e.Handled = true;
                if (!e.IsRepeat && choice != 0)
                    SelectPerk(choice);
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
                    DescribeSelection();
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
                    DescribeSelection();
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
                if (!e.IsRepeat)
                    PrepareOrStartWave();
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

            if (e.Key is Key.T or Key.F)
            {
                e.Handled = true;
                if (e.IsRepeat) return;
                if (e.Key == Key.T)
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
            RefreshUpgradeControls();
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
            if (_securitySweepCooldown > 0 || _thieves.Count == 0)
                return;

            // Independent automated response. No Q press or pointer needed.
            // Damage-based sweeps allow health bars and mounted guns to matter.
            int count = Math.Min(_thieves.Count,
                1 + _progression.Level(3) / 2);
            double damage = 19 + _progression.Level(3) * 11;
            int dispatched = 0;
            foreach (var thief in _thieves
                .Where(t => t.Distance <= 165)
                .OrderBy(t => t.Distance).Take(count))
            {
                thief.Health -= damage;
                dispatched++;
            }

            if (dispatched == 0)
                return;

            _securityRingTime = 0.55;
            _securitySweepCooldown = Math.Max(1.0,
                8.0 / (1 + _progression.Level(18) * 0.18));
            ResolveDefeatedThreats();
        }

        private void CallRfSweep()
        {
            if (_rfSweepCooldown > 0 || _jammers.Count == 0)
                return;

            // Automated interference suppression damages small RF towers.
            // The guns and drones finish survivors.
            int count = Math.Min(_jammers.Count,
                1 + _progression.Level(4) / 2);
            double damage = 26 + _progression.Level(4) * 14;
            foreach (var tower in _jammers.Take(count))
                tower.Health -= damage;

            _rfRingTime = 0.65;
            _rfSweepCooldown = Math.Max(1.5,
                11.0 / (1 + _progression.Level(18) * 0.16));
            ResolveDefeatedThreats();
        }

        private void DescribeSelection()
        {
            int index = _progression.SelectedCategory * 8 +
                _progression.SelectedRow;
            var item = TowerDefenseProgression.Catalog[index];
            _message = item.Name.ToUpperInvariant() + ": " + item.Detail +
                (_progression.Unlocked(index, _wave) ? "" :
                    " (UNLOCK WAVE " + item.UnlockWave + ")");
            _surface.InvalidateVisual();
            RefreshUpgradeControls();
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
            RefreshUpgradeControls();
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

        // Explicit cost / restored HP metrics drive BOTH repairs and buttons.
        // Pricing rises by wave and Parts Discount applies to both.
        private int TowerRepairCost => (int)Math.Clamp(Math.Ceiling(
            (200 + _wave * 28.0) /
            (1 + _progression.Level(20) * 0.025)), 1, int.MaxValue);

        private int FenceRepairCost => (int)Math.Clamp(Math.Ceiling(
            (120 + _wave * 16.0) /
            (1 + _progression.Level(20) * 0.025)), 1, int.MaxValue);

        private double TowerRepairCapacity =>
            Math.Min(1000, 100 + 10 * _progression.Level(22));

        private double FenceRepairCapacity =>
            Math.Min(1000, 100 + 15 * _progression.Level(13));

        private double TowerRepairAvailable =>
            Math.Max(0, Math.Min(MaxTowerHealth - _health, TowerRepairCapacity));

        private double FenceRepairAvailable =>
            Math.Max(0, Math.Min(FenceMaxHealth - _fenceHealth, FenceRepairCapacity));

        private void RepairTower()
        {
            if (!_started || _paused || _gameOver ||
                _progression.WorkshopOpen || _showHelp || _perkPending)
                return;

            double restored = TowerRepairAvailable;
            if (restored < 0.01)
                _message = "TOWER HP ALREADY FULL.";
            else if (_cash < TowerRepairCost)
                _message = "REPAIR TOWER: NEED " + Money(TowerRepairCost) +
                    " FOR UP TO " + TowerRepairCapacity.ToString("0", MoneyCulture) + " HP.";
            else
            {
                _cash -= TowerRepairCost;
                _health = Math.Min(MaxTowerHealth, _health + restored);
                _message = "TOWER +" + restored.ToString("0", MoneyCulture) +
                    " HP FOR " + Money(TowerRepairCost) + ".";
            }
            _surface.InvalidateVisual();
            RefreshUpgradeControls();
        }

        private void RepairFence()
        {
            if (!_started || _paused || _gameOver ||
                _progression.WorkshopOpen || _showHelp || _perkPending)
                return;

            double restored = FenceRepairAvailable;
            if (_fenceLevel == 0)
                _message = "BUY A SECURITY FENCE BEFORE REPAIRING IT.";
            else if (restored < 0.01)
                _message = "FENCE HP ALREADY FULL.";
            else if (_cash < FenceRepairCost)
                _message = "REPAIR FENCE: NEED " + Money(FenceRepairCost) +
                    " FOR UP TO " + FenceRepairCapacity.ToString("0", MoneyCulture) + " HP.";
            else
            {
                _cash -= FenceRepairCost;
                _fenceHealth = Math.Min(FenceMaxHealth, _fenceHealth + restored);
                _message = "FENCE +" + restored.ToString("0", MoneyCulture) +
                    " HP FOR " + Money(FenceRepairCost) + ".";
            }
            _surface.InvalidateVisual();
            RefreshUpgradeControls();
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
            RefreshUpgradeControls();
        }

        private double FenceMaxHealth => _fenceLevel == 0 ? 0 :
            Math.Min(3000, 24 + _fenceLevel * 20 +
                _progression.Level(13) * 16);

        private void Draw(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Backdrop, null,
                new Rect(0, 0, DesignWidth, DesignHeight));
            dc.DrawRectangle(Panel, new Pen(GridInk, 2),
                new Rect(12, 12, 1296, 735));
            Text(dc, "GRID UNDER ATTACK", 27, 23, 29, Cyan, dpi, true);
            Text(dc, "360 DEGREE RADIO TOWER DEFENSE", 28, 64, 13, Muted, dpi);
            Text(dc, _message, 28, 96, 15, Gold, dpi, true, 1250);

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
            Text(dc, "AUTO SECURITY: " + patrol + "    AUTO RF: " + rf,
                38, 738, 13, Cyan, dpi, true);

            dc.DrawRectangle(Panel, new Pen(GridInk, 1),
                new Rect(24, 759, 1274, 47));
            Text(dc, "AUTO: GUNS / SECURITY / RF    H HELP  P PAUSE",
                35, 773, 12, White, dpi, true);

            if (!_started || _paused || _gameOver)
                DrawOverlay(dc, dpi);
            if (_perkPending)
                DrawPerk(dc, dpi);
            if (_showHelp)
                DrawHelp(dc, dpi);
            if (_progression.WorkshopOpen)
                DrawWorkshop(dc, dpi);
        }

        private void DrawField(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(Ground, new Pen(Cyan, 2),
                new Rect(FieldX - 2, FieldY - 2, FieldWidth + 4, FieldHeight + 4));

            for (int y = 0; y < 19; y++)
            for (int x = 0; x < 24; x++)
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

            DrawMountedGun(dc, false);
            DrawMountedGun(dc, true);

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

            // Illegal RF sites are MINIATURE lattice radio towers,
            // visually distinct from the approaching copper thieves.
            foreach (var jammer in _jammers)
            {
                var p = new Point(jammer.X, jammer.Y);
                dc.DrawEllipse(null, new Pen(Purple, 1.7), p, 27, 27);
                var frame = new Pen(Purple, 2.2);
                var braces = new Pen(DarkSteel, 1.3);
                dc.DrawLine(frame, new Point(p.X, p.Y - 21),
                    new Point(p.X - 13, p.Y + 16));
                dc.DrawLine(frame, new Point(p.X, p.Y - 21),
                    new Point(p.X + 13, p.Y + 16));
                dc.DrawLine(frame, new Point(p.X - 13, p.Y + 16),
                    new Point(p.X + 13, p.Y + 16));
                for (int rung = 0; rung < 3; rung++)
                {
                    double yy = p.Y - 11 + rung * 9;
                    double h = 3 + rung * 3.6;
                    dc.DrawLine(braces, new Point(p.X - h, yy),
                        new Point(p.X + h, yy + 8));
                    dc.DrawLine(braces, new Point(p.X + h, yy),
                        new Point(p.X - h, yy + 8));
                }
                dc.DrawRectangle(White, null,
                    new Rect(p.X - 10, p.Y - 9, 4, 11));
                dc.DrawRectangle(White, null,
                    new Rect(p.X + 5, p.Y - 9, 4, 11));
                dc.DrawEllipse(Red, null, new Point(p.X, p.Y - 23), 3, 3);
                DrawHealthBar(dc, p.X, p.Y - 41,
                    jammer.Health, jammer.MaxHealth, 39);
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
                DrawHealthBar(dc, p.X, p.Y - 25,
                    thief.Health, thief.MaxHealth, 27);
            }

            foreach (var flash in _defenseFlashes)
                dc.DrawLine(new Pen(flash.Ink, 2.5), flash.From, flash.To);

            if (_securityRingTime > 0)
            {
                double radius = 55 + (0.55 - _securityRingTime) / 0.55 * 180;
                dc.DrawEllipse(null, new Pen(Cyan, 2.3),
                    new Point(TowerX, TowerY), radius, radius);
            }
            if (_rfRingTime > 0)
            {
                double radius = 50 + (0.65 - _rfRingTime) / 0.65 * 175;
                dc.DrawEllipse(null, new Pen(Purple, 2.3),
                    new Point(TowerX, TowerY), radius, radius);
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

        private static void DrawHealthBar(DrawingContext dc,
            double centerX, double y, double health, double maximum, double width)
        {
            double pct = Math.Clamp(health / Math.Max(1, maximum), 0, 1);
            dc.DrawRectangle(Backdrop, new Pen(White, 0.7),
                new Rect(centerX - width / 2, y, width, 5));
            dc.DrawRectangle(pct < 0.3 ? Red : pct < 0.65 ? Gold : Green,
                null, new Rect(centerX - width / 2, y, width * pct, 5));
        }

        private void DrawMountedGun(DrawingContext dc, bool right)
        {
            Point basePoint = new(
                TowerX + (right ? 21 : -21), TowerY - 23);
            Point defaultDirection = new(right ? 1 : -1, -0.40);
            Point? aim = right ? _rightGunAim : _leftGunAim;
            double dx = (aim?.X ?? basePoint.X + defaultDirection.X) -
                basePoint.X;
            double dy = (aim?.Y ?? basePoint.Y + defaultDirection.Y) -
                basePoint.Y;
            double length = Math.Max(0.01, Math.Sqrt(dx * dx + dy * dy));
            double ux = dx / length, uy = dy / length;
            Brush ink = right ? Purple : Gold;
            dc.DrawEllipse(DarkSteel, new Pen(White, 1.3),
                basePoint, 9, 9);
            dc.DrawLine(new Pen(ink, 5.0), basePoint,
                new Point(basePoint.X + ux * 20, basePoint.Y + uy * 20));
            dc.DrawEllipse(ink, null,
                new Point(basePoint.X + ux * 20, basePoint.Y + uy * 20),
                3.2, 3.2);
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
            dc.DrawRectangle(Panel, new Pen(GridInk, 1.6),
                new Rect(PanelX, FieldY, PanelWidth, FieldHeight));
            Text(dc, "UPGRADES", PanelX + 17, 140, 22, Cyan, dpi, true);
            Text(dc, "TOWER HEALTH", PanelX + 17, 174, 13, Muted, dpi, true);
            Text(dc, Math.Ceiling(_health).ToString("0", MoneyCulture) +
                "/" + MaxTowerHealth.ToString("0", MoneyCulture),
                PanelX + 17, 193, 23, _health < 28 ? Red : Green, dpi, true);
            Text(dc, "CASH", PanelX + 281, 174, 13, Muted, dpi, true);
            Text(dc, Money(_cash), PanelX + 281, 193, 22, Gold, dpi, true);
            Text(dc, "WAVE " + _wave + (_waveActive ? " IN PROGRESS" : " READY"),
                PanelX + 17, 231, 14, White, dpi, true);
            Text(dc, "RESEARCH +" +
                _progression.PendingCredits.ToString("N0", MoneyCulture),
                PanelX + 291, 232, 13, Purple, dpi, true);
            // Category selectors and all eight upgrade cards are actual WPF
            // Buttons above this drawing surface, not tiny painted rectangles.
        }

        private void DrawWorkshop(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(248, 5, 14, 31)),
                new Pen(Purple, 2.6), new Rect(140, 99, 1093, 647));
            Text(dc, "RADIO SHOP RESEARCH WORKSHOP", 174, 116, 27,
                Gold, dpi, true);
            Text(dc, "PERMANENT CREDITS: " + Money(_progression.Credits) +
                "        BEST WAVE: " + _progression.BestWave,
                175, 157, 17, White, dpi, true);
            Text(dc, "SELECT AN UPGRADE TO KEEP IT IN EVERY FUTURE SHIFT.",
                175, 184, 15, Cyan, dpi);

            Text(dc, "CLOSE WORKSHOP TO START OR RETRY YOUR SHIFT",
                178, 713, 13, Muted, dpi);
        }

        private void DrawPerk(DrawingContext dc, double dpi)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(247, 5, 14, 31)),
                new Pen(Gold, 2.7), new Rect(128, 178, 1010, 435));
            Text(dc, "MILESTONE: WAVE " + _wave + " SURVIVED!",
                174, 199, 29, Gold, dpi, true);
            Text(dc, "CHOOSE YOUR BONUS BEFORE THE NEXT WAVE:",
                178, 249, 18, White, dpi, true);
            Text(dc, "CLICK A BONUS CARD OR PRESS 1 / 2 / 3",
                178, 560, 16, Muted, dpi);
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
            Text(dc, "CLICK A CATEGORY TAB TO VIEW ITS 8 UPGRADES.",
                143, 231, 14, White, dpi);
            Text(dc, "CLICK A LARGE UPGRADE CARD TO PURCHASE IT.",
                143, 259, 14, White, dpi);
            Text(dc, "GUNS, SECURITY AND RF TEAMS OPERATE AUTOMATICALLY.",
                143, 290, 14, White, dpi);
            Text(dc, "T TOWER REPAIR  •  F FENCE REPAIR  •  SPACE WAVE",
                143, 318, 14, White, dpi);
            Text(dc, "WORKSHOP BUTTON: PERMANENT UPGRADES BETWEEN RUNS",
                143, 352, 15, Purple, dpi, true);
            Text(dc, "EARN RESEARCH ON WAVES; KEEP IT AFTER DEFEAT.",
                143, 380, 14, Green, dpi, true);
            Text(dc, "KEYBOARD BUYING STILL WORKS: TAB + 1–8.",
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
            _timer.Tick -= RefreshControlsOnTick;
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
            public double Health { get; set; }
            public double MaxHealth { get; set; }
        }

        private sealed record DefenseFlash(
            Point From, Point To, double EndTime, Brush Ink);

        private sealed class InterferenceTower
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Age { get; set; }
            public double Health { get; set; }
            public double MaxHealth { get; set; }
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
                new(DesignWidth, DesignHeight);

            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);
                _game.Draw(drawingContext,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
            }
        }
    }
}
