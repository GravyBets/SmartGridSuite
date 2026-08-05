using SmartGridSuite.Contracts.SiteDashboard;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private sealed class TowerSectorPingCard
        {
            public string Sector { get; set; } = "";

            public Border? CardBorder { get; set; }

            public TextBox? PingCountTextBox { get; set; }

            public Button? PingButton { get; set; }
            public Button? ClearButton { get; set; }

            public CancellationTokenSource? PingCts { get; set; }
            public bool IsRunning { get; set; }

            public List<TowerPingEndpoint> Endpoints { get; set; } = new();
        }

        private sealed class TowerPingEndpoint
        {
            public TowerSectorPingCard? ParentSector { get; set; }

            public string Label { get; set; } = "";
            public string IpAddress { get; set; } = "";

            public Button? PingButton { get; set; }

            public TextBox? IpTextBox { get; set; }
            public Brush? DefaultIpBorderBrush { get; set; }
            public Brush? DefaultIpBackground { get; set; }
            public Brush? DefaultIpForeground { get; set; }

            public TextBox? ResultTextBox { get; set; }
            public TextBlock? SummaryTextBlock { get; set; }

            public bool? TestSuccessful { get; set; }
            public bool IsRunning { get; set; }
        }

        public void SetTowerSectors(IEnumerable<TowerSectorDto>? sectors)
        {
            _towerPingCards.Clear();

            if (TowerSectorCardsPanel is null)
                return;

            TowerSectorCardsPanel.Children.Clear();

            var sectorList = (sectors ?? Enumerable.Empty<TowerSectorDto>())
                .OrderBy(x => GetTowerSectorSortRank(x.Sector))
                .ThenBy(x => x.Sector)
                .ThenBy(x => x.TopSiteId)
                .ToList();

            if (sectorList.Count == 0)
            {
                TowerSectorCardsPanel.Children.Add(new TextBlock
                {
                    Text = "No tower sectors returned.",
                    Foreground = TryFindResource("TextSecondary") as Brush,
                    FontStyle = FontStyles.Italic
                });

                return;
            }

            foreach (var sector in sectorList)
            {
                var card = new TowerSectorPingCard
                {
                    Sector = string.IsNullOrWhiteSpace(sector.Sector) ? "Sector" : sector.Sector.Trim()
                };

                AddTowerEndpoint(card, "IP A", sector.IPa);
                AddTowerEndpoint(card, "IP B", sector.IPb);

                _towerPingCards.Add(card);
                TowerSectorCardsPanel.Children.Add(CreateTowerSectorCard(card));
            }
            Dispatcher.BeginInvoke(new Action(RefreshTowerSectorCardLayout));
        }

        private static int GetTowerSectorSortRank(string? sector)
        {
            var value = (sector ?? string.Empty).Trim().ToUpperInvariant();

            if (value == "AP1")
                return 1;

            if (value == "AP2")
                return 2;

            if (value == "AP3")
                return 3;

            if (value.StartsWith("AP") &&
                int.TryParse(value[2..], out var apNumber))
            {
                return 100 + apNumber;
            }

            return 1000;
        }

        private static void AddTowerEndpoint(TowerSectorPingCard card, string label, string? ip)
        {
            var cleanIp = (ip ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanIp) || cleanIp == "—")
                return;

            card.Endpoints.Add(new TowerPingEndpoint
            {
                ParentSector = card,
                Label = label,
                IpAddress = cleanIp
            });
        }

        private FrameworkElement CreateTowerSectorCard(TowerSectorPingCard card)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(1),
                Background = TryFindResource("SurfaceBg") as Brush,
                Padding = new Thickness(12),
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            card.CardBorder = border;

            var root = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var title = new TextBlock
            {
                Text = $"Sector {card.Sector}",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var controls = new Grid();
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pingCountBox = new TextBox
            {
                Style = (Style)FindResource("ModernWatermarkTextBox"),
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = "Ping Count",
                Text = string.Empty
            };

            card.PingCountTextBox = pingCountBox;

            var pingSectorButton = new Button
            {
                Content = "Ping Sector",
                Style = (Style)FindResource("PrimaryButtonStyle"),
                Height = 28,
                MinWidth = 100,
                Padding = new Thickness(12, 0, 12, 0),
                Tag = card
            };
            pingSectorButton.Click += PingTowerSectorButton_Click;
            card.PingButton = pingSectorButton;

            var clearButton = new Button
            {
                Content = "Clear",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 28,
                MinWidth = 70,
                Padding = new Thickness(12, 0, 12, 0),
                Tag = card
            };
            clearButton.Click += ClearTowerSectorButton_Click;
            card.ClearButton = clearButton;

            Grid.SetColumn(pingCountBox, 0);
            Grid.SetColumn(pingSectorButton, 2);
            Grid.SetColumn(clearButton, 4);

            controls.Children.Add(pingCountBox);
            controls.Children.Add(pingSectorButton);
            controls.Children.Add(clearButton);

            Grid.SetRow(controls, 2);
            root.Children.Add(controls);

            var endpointGrid = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var endpointCount = Math.Max(1, card.Endpoints.Count);

            for (var i = 0; i < endpointCount; i++)
            {
                endpointGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                });
            }

            for (var i = 0; i < card.Endpoints.Count; i++)
            {
                var endpointCard = CreateTowerPingEndpointCard(card, card.Endpoints[i]);

                if (endpointCard is FrameworkElement fe)
                {
                    fe.Margin = i == card.Endpoints.Count - 1
                        ? new Thickness(0)
                        : new Thickness(0, 0, 0, 8);
                }

                Grid.SetRow(endpointCard, i);
                endpointGrid.Children.Add(endpointCard);
            }

            Grid.SetRow(endpointGrid, 4);
            root.Children.Add(endpointGrid);

            border.Child = root;
            return border;
        }

        private FrameworkElement CreateTowerPingEndpointCard(TowerSectorPingCard sector, TowerPingEndpoint endpoint)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Background = TryFindResource("CardBg") as Brush,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var root = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            top.Children.Add(new TextBlock
            {
                Text = endpoint.Label,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextPrimary") as Brush,
                VerticalAlignment = VerticalAlignment.Center
            });

            var pingButton = new Button
            {
                Content = "Ping",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 24,
                MinWidth = 58,
                Padding = new Thickness(8, 0, 8, 0),
                Tag = endpoint
            };

            pingButton.Click += PingTowerEndpointButton_Click;

            endpoint.PingButton = pingButton;

            Grid.SetColumn(pingButton, 1);
            top.Children.Add(pingButton);

            Grid.SetRow(top, 0);
            root.Children.Add(top);

            var ipBox = new TextBox
            {
                Text = endpoint.IpAddress,
                Style = (Style)FindResource("ModernTextBox"),
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = true
            };

            endpoint.IpTextBox = ipBox;
            endpoint.DefaultIpBorderBrush = ipBox.BorderBrush;
            endpoint.DefaultIpBackground = ipBox.Background;
            endpoint.DefaultIpForeground = ipBox.Foreground;

            Grid.SetRow(ipBox, 2);
            root.Children.Add(ipBox);

            var resultBox = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Top,
                Padding = new Thickness(8),
                Text = string.Empty,
                Height = double.NaN,
                MinHeight = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            endpoint.ResultTextBox = resultBox;

            Grid.SetRow(resultBox, 4);
            root.Children.Add(resultBox);

            var summary = new TextBlock
            {
                Text = "Ready.",
                Foreground = TryFindResource("TextSecondary") as Brush,
                FontSize = 11
            };

            endpoint.SummaryTextBlock = summary;

            Grid.SetRow(summary, 6);
            root.Children.Add(summary);

            border.Child = root;
            return border;
        }

        private bool TryGetTowerSectorPingCount(TowerSectorPingCard sector, out int pingCount, out bool continuous)
        {
            pingCount = 0;
            continuous = false;

            var raw = (sector.PingCountTextBox?.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                continuous = true;
                return true;
            }

            if (!int.TryParse(raw, out pingCount) || pingCount < 1 || pingCount > 99999)
            {
                MessageBox.Show(
                    "Enter a whole number between 1 and 99,999, or leave it blank for continuous ping.",
                    "Tower Ping Count",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                sector.PingCountTextBox?.Focus();
                return false;
            }

            return true;
        }

        private void ResetTowerIpStatus(TowerPingEndpoint endpoint)
        {
            if (endpoint.IpTextBox is null)
                return;

            /*
             * Remove the local success/failure brush references so the
             * TextBox returns to its theme-controlled style.
             */
            endpoint.IpTextBox.ClearValue(
                Control.BackgroundProperty);

            endpoint.IpTextBox.ClearValue(
                Control.BorderBrushProperty);

            endpoint.IpTextBox.ClearValue(
                Control.ForegroundProperty);

            endpoint.IpTextBox.ClearValue(
                TextBox.CaretBrushProperty);

            endpoint.IpTextBox.ClearValue(
                Control.BorderThicknessProperty);

            endpoint.TestSuccessful = null;
        }

        private void ApplyTowerIpStatus(TowerPingEndpoint endpoint, bool success)
        {
            if (endpoint.IpTextBox is null)
                return;

            var resourcePrefix =
                success
                    ? "NetworkPingSuccess"
                    : "NetworkPingFailure";

            endpoint.IpTextBox.SetResourceReference(
                Control.BackgroundProperty,
                $"{resourcePrefix}Bg");

            endpoint.IpTextBox.SetResourceReference(
                Control.BorderBrushProperty,
                $"{resourcePrefix}Border");

            endpoint.IpTextBox.SetResourceReference(
                Control.ForegroundProperty,
                $"{resourcePrefix}Text");

            endpoint.IpTextBox.SetResourceReference(
                TextBox.CaretBrushProperty,
                $"{resourcePrefix}Text");

            endpoint.IpTextBox.BorderThickness =
                new Thickness(1.5);

            endpoint.TestSuccessful = success;
        }

        private void TowerSectorCardsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshTowerSectorCardLayout();
        }

        private void RefreshTowerSectorCardLayout()
        {
            if (TowerSectorCardsScrollViewer is null || _towerPingCards.Count == 0)
                return;

            var viewportWidth = TowerSectorCardsScrollViewer.ViewportWidth;
            if (double.IsNaN(viewportWidth) || viewportWidth <= 0)
                viewportWidth = TowerSectorCardsScrollViewer.ActualWidth;

            var availableHeight = TowerSectorCardsScrollViewer.ActualHeight;

            if (double.IsNaN(viewportWidth) || viewportWidth <= 100 ||
                double.IsNaN(availableHeight) || availableHeight <= 100)
            {
                return;
            }

            var showHorizontalScroll = _towerPingCards.Count > 3;
            TowerSectorCardsScrollViewer.HorizontalScrollBarVisibility =
                showHorizontalScroll ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

            var visibleColumns = Math.Min(3, _towerPingCards.Count);

            const double cardGap = 10;
            var totalGapWidth = cardGap * Math.Max(0, visibleColumns - 1);

            var cardWidth = (viewportWidth - totalGapWidth) / visibleColumns;
            cardWidth = Math.Max(280, cardWidth);

            var horizontalBarAllowance = showHorizontalScroll
                ? SystemParameters.HorizontalScrollBarHeight + 6
                : 0;

            var cardHeight = Math.Max(320, availableHeight - horizontalBarAllowance - 6);

            for (var i = 0; i < _towerPingCards.Count; i++)
            {
                var card = _towerPingCards[i];

                if (card.CardBorder is null)
                    continue;

                card.CardBorder.Width = cardWidth;
                card.CardBorder.Height = cardHeight;
                card.CardBorder.Margin = (i == _towerPingCards.Count - 1)
                    ? new Thickness(0)
                    : new Thickness(0, 0, 10, 0);
            }
        }

        private async void PingTowerEndpointButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not TowerPingEndpoint endpoint)
                return;

            var sector = endpoint.ParentSector;

            if (endpoint.IsRunning || sector?.IsRunning == true)
            {
                if (sector is not null)
                    StopTowerSectorPings(sector);

                RefreshTowerPingButtonStates();
                return;
            }

            await RunSingleTowerEndpointPingAsync(endpoint);
        }

        private async Task RunSingleTowerEndpointPingAsync(TowerPingEndpoint endpoint)
        {
            var sector = endpoint.ParentSector;

            if (sector is null)
                return;

            if (!TryGetTowerSectorPingCount(sector, out var pingCount, out var continuous))
                return;

            StopTowerSectorPings(sector);

            var cts = new CancellationTokenSource();
            sector.PingCts = cts;
            sector.IsRunning = true;
            RefreshTowerPingButtonStates();

            try
            {
                await PingTowerEndpointAsync(endpoint, pingCount, continuous, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // expected when stopped
            }
            finally
            {
                if (ReferenceEquals(sector.PingCts, cts))
                {
                    sector.PingCts.Dispose();
                    sector.PingCts = null;
                }

                sector.IsRunning = false;
                RefreshTowerPingButtonStates();
            }
        }

        private async Task PingTowerEndpointAsync(TowerPingEndpoint endpoint, int pingCount, bool continuous, CancellationToken token)
        {
            if (endpoint.IsRunning)
                return;

            endpoint.IsRunning = true;
            RefreshTowerPingButtonStates();

            try
            {
                var ip = (endpoint.IpAddress ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(ip))
                    return;

                ResetTowerIpStatus(endpoint);

                if (endpoint.ResultTextBox is not null)
                    endpoint.ResultTextBox.Text = string.Empty;

                if (endpoint.SummaryTextBlock is not null)
                    endpoint.SummaryTextBlock.Text = "Testing...";

                var sent = 0;
                var received = 0;
                var outputLines = new List<string>();

                using var ping = new Ping();

                while (!token.IsCancellationRequested && (continuous || sent < pingCount))
                {
                    sent++;

                    try
                    {
                        var reply = await ping.SendPingAsync(ip, 1000);

                        if (reply.Status == IPStatus.Success)
                        {
                            received++;
                            outputLines.Add($"Reply from {ip}: Time={reply.RoundtripTime}ms");
                            ApplyTowerIpStatus(endpoint, true);
                        }
                        else
                        {
                            outputLines.Add($"{ip}: {reply.Status}");
                            ApplyTowerIpStatus(endpoint, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        outputLines.Add($"{ip}: {ex.Message}");
                        ApplyTowerIpStatus(endpoint, false);
                    }

                    if (outputLines.Count > 150)
                        outputLines.RemoveRange(0, outputLines.Count - 150);

                    if (endpoint.ResultTextBox is not null)
                    {
                        endpoint.ResultTextBox.Text = string.Join(Environment.NewLine, outputLines);
                        endpoint.ResultTextBox.ScrollToEnd();
                    }

                    var lost = sent - received;
                    var lossPercent = sent == 0
                        ? 0
                        : (int)Math.Round((lost / (double)sent) * 100);

                    if (endpoint.SummaryTextBlock is not null)
                    {
                        endpoint.SummaryTextBlock.Text = continuous
                            ? $"Sent = {sent}, Lost = {lost} ({lossPercent}% loss) • Running..."
                            : $"Sent = {sent}, Lost = {lost} ({lossPercent}% loss)";
                    }

                    var delayMs = continuous ? 1000 : 150;
                    await Task.Delay(delayMs, token);
                }
            }
            finally
            {
                endpoint.IsRunning = false;
                RefreshTowerPingButtonStates();
            }
        }

        private async void PingTowerSectorButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not TowerSectorPingCard sector)
                return;

            if (sector.IsRunning || sector.PingCts is not null || sector.Endpoints.Any(x => x.IsRunning))
            {
                StopTowerSectorPings(sector);
                RefreshTowerPingButtonStates();
                return;
            }

            await RunTowerSectorPingAsync(sector);
        }

        private async Task RunTowerSectorPingAsync(TowerSectorPingCard sector)
        {
            if (sector.IsRunning)
                return;

            if (!TryGetTowerSectorPingCount(sector, out var pingCount, out var continuous))
                return;

            StopTowerSectorPings(sector);

            var cts = new CancellationTokenSource();
            sector.PingCts = cts;
            sector.IsRunning = true;
            RefreshTowerPingButtonStates();

            try
            {
                var tasks = sector.Endpoints
                    .Where(x => !x.IsRunning)
                    .Select(x => PingTowerEndpointAsync(x, pingCount, continuous, cts.Token))
                    .ToList();

                if (tasks.Count > 0)
                    await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // expected when stopped
            }
            finally
            {
                if (ReferenceEquals(sector.PingCts, cts))
                {
                    sector.PingCts.Dispose();
                    sector.PingCts = null;
                }

                sector.IsRunning = false;
                RefreshTowerPingButtonStates();
            }
        }

        private void StopTowerSectorButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not TowerSectorPingCard sector)
                return;

            StopTowerSectorPings(sector);
        }

        private void ClearTowerSectorButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not TowerSectorPingCard sector)
                return;

            StopTowerSectorPings(sector);

            foreach (var endpoint in sector.Endpoints)
            {
                ResetTowerIpStatus(endpoint);

                if (endpoint.ResultTextBox is not null)
                    endpoint.ResultTextBox.Text = string.Empty;

                if (endpoint.SummaryTextBlock is not null)
                    endpoint.SummaryTextBlock.Text = "Ready.";
            }
        }

        private void StopTowerSectorPings(TowerSectorPingCard sector)
        {
            try
            {
                sector.PingCts?.Cancel();
            }
            catch
            {
                // ignore
            }

            sector.PingCts?.Dispose();
            sector.PingCts = null;
            sector.IsRunning = false;

            foreach (var endpoint in sector.Endpoints)
                endpoint.IsRunning = false;

            RefreshTowerPingButtonStates();
        }

        private async void TestAllTowerSectorsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_towerTestAllCts is not null || _towerPingCards.Any(x => x.IsRunning || x.Endpoints.Any(y => y.IsRunning)))
            {
                StopTowerPings();
                return;
            }

            StopTowerPings();

            var cts = new CancellationTokenSource();
            _towerTestAllCts = cts;
            RefreshTowerPingButtonStates();

            try
            {
                foreach (var sector in _towerPingCards)
                    await TestTowerSectorAsync(sector, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // expected when stopped
            }
            finally
            {
                if (ReferenceEquals(_towerTestAllCts, cts))
                {
                    _towerTestAllCts.Dispose();
                    _towerTestAllCts = null;
                }

                RefreshTowerPingButtonStates();
            }
        }

        private async Task TestTowerSectorAsync(TowerSectorPingCard sector, CancellationToken token)
        {
            StopTowerSectorPings(sector);

            sector.IsRunning = true;
            RefreshTowerPingButtonStates();

            try
            {
                foreach (var endpoint in sector.Endpoints)
                    await TestTowerEndpointAsync(endpoint, token);
            }
            finally
            {
                sector.IsRunning = false;
                RefreshTowerPingButtonStates();
            }
        }

        private async Task TestTowerEndpointAsync(TowerPingEndpoint endpoint, CancellationToken token)
        {
            var ip = (endpoint.IpAddress ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(ip))
                return;

            token.ThrowIfCancellationRequested();

            endpoint.IsRunning = true;
            RefreshTowerPingButtonStates();

            try
            {
                ResetTowerIpStatus(endpoint);

                if (endpoint.SummaryTextBlock is not null)
                    endpoint.SummaryTextBlock.Text = "Testing...";

                var successAfterWarmup = false;

                using var ping = new Ping();

                for (var i = 0; i < 5; i++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        var reply = await ping.SendPingAsync(ip, 1000);

                        if (reply.Status == IPStatus.Success && i > 0)
                            successAfterWarmup = true;
                    }
                    catch
                    {
                        // Treat failed ping attempt as no response.
                    }
                }

                ApplyTowerIpStatus(endpoint, successAfterWarmup);

                if (endpoint.SummaryTextBlock is not null)
                    endpoint.SummaryTextBlock.Text = successAfterWarmup ? "Test Successful" : "Test Failed";
            }
            finally
            {
                endpoint.IsRunning = false;
                RefreshTowerPingButtonStates();
            }
        }

        public void StopTowerPings()
        {
            try
            {
                _towerTestAllCts?.Cancel();
            }
            catch
            {
                // ignore
            }

            _towerTestAllCts?.Dispose();
            _towerTestAllCts = null;

            foreach (var sector in _towerPingCards)
                StopTowerSectorPings(sector);

            RefreshTowerPingButtonStates();
        }

        private void RefreshTowerHeaderDisplay()
        {
            if (TowerHeaderTextBlock is null)
                return;

            var topName = GetTowerSummaryValue("Top Name");
            var description = GetTowerSummaryValue("Description");

            var cleanedDescription = CleanTowerHeaderDescription(description);

            if (!string.IsNullOrWhiteSpace(cleanedDescription) &&
                !string.IsNullOrWhiteSpace(topName))
            {
                TowerHeaderTextBlock.Text = $"Tower {cleanedDescription} ({topName})";
            }
            else if (!string.IsNullOrWhiteSpace(topName))
            {
                TowerHeaderTextBlock.Text = $"Tower {topName}";
            }
            else
            {
                TowerHeaderTextBlock.Text = "Tower";
            }
        }

        private string GetTowerSummaryValue(string label)
        {
            var lines = (_towerSummaryText ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (!line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                    continue;

                var idx = line.IndexOf(':');
                if (idx < 0)
                    continue;

                return line[(idx + 1)..].Trim();
            }

            return string.Empty;
        }

        private static string CleanTowerHeaderDescription(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (text.EndsWith(" SUB", StringComparison.OrdinalIgnoreCase))
                text = text[..^4].Trim();

            return text;
        }

        private string GetTowerPingStatsForWriteUp()
        {
            var lines = new List<string>
            {
                "Ping Stats:"
            };

            foreach (var sector in _towerPingCards)
            {
                var sectorLines = new List<string>();

                foreach (var endpoint in sector.Endpoints)
                {
                    var ip = (endpoint.IpAddress ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(ip) || ip == "—")
                        continue;

                    var summary = CleanTowerPingSummaryForWriteUp(endpoint.SummaryTextBlock?.Text);

                    sectorLines.Add(string.IsNullOrWhiteSpace(summary)
                        ? $"{endpoint.Label} ({ip})"
                        : $"{endpoint.Label} ({ip}) - {summary}");
                }

                if (sectorLines.Count == 0)
                    continue;

                if (lines.Count > 1)
                    lines.Add(string.Empty);

                lines.Add($"Sector {sector.Sector}:");
                lines.AddRange(sectorLines);
            }

            return lines.Count > 1
                ? string.Join(Environment.NewLine, lines)
                : string.Empty;
        }

        private static string CleanTowerPingSummaryForWriteUp(string? summary)
        {
            var value = (summary ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("Ready.", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Ready", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Testing...", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("No IP available.", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return value.TrimEnd('.');
        }

        private bool IsAnyTowerPingRunning()
        {
            return _towerTestAllCts is not null ||
                   _towerPingCards.Any(x =>
                       x.IsRunning ||
                       x.PingCts is not null ||
                       x.Endpoints.Any(y => y.IsRunning));
        }

        private void RefreshTowerPingButtonStates()
        {
            var testAllRunning = _towerTestAllCts is not null;

            foreach (var sector in _towerPingCards)
            {
                var sectorManualPingRunning =
                    !testAllRunning &&
                    (sector.IsRunning ||
                     sector.PingCts is not null ||
                     sector.Endpoints.Any(x => x.IsRunning));

                // Sector Ping button:
                // - Red Stop only for manual sector/endpoint ping
                // - Normal/disabled during Test All
                SetTowerButtonState(
                    sector.PingButton,
                    sectorManualPingRunning,
                    normalText: "Ping Sector",
                    normalStyleKey: "PrimaryButtonStyle");

                if (sector.PingButton is not null && testAllRunning)
                {
                    sector.PingButton.Content = "Ping Sector";
                    sector.PingButton.Style = (Style)FindResource("PrimaryButtonStyle");
                    sector.PingButton.IsEnabled = false;
                }
                else if (sector.PingButton is not null)
                {
                    sector.PingButton.IsEnabled = true;
                }

                if (sector.ClearButton is not null)
                    sector.ClearButton.IsEnabled = !testAllRunning && !sectorManualPingRunning;

                foreach (var endpoint in sector.Endpoints)
                {
                    var endpointManualPingRunning =
                        !testAllRunning &&
                        endpoint.IsRunning;

                    // Endpoint Ping button:
                    // - Red Stop only for manual endpoint ping
                    // - Normal/disabled during Test All
                    SetTowerButtonState(
                        endpoint.PingButton,
                        endpointManualPingRunning,
                        normalText: "Ping",
                        normalStyleKey: "SecondaryButtonStyle");

                    if (endpoint.PingButton is not null && testAllRunning)
                    {
                        endpoint.PingButton.Content = "Ping";
                        endpoint.PingButton.Style = (Style)FindResource("SecondaryButtonStyle");
                        endpoint.PingButton.IsEnabled = false;
                    }
                    else if (endpoint.PingButton is not null)
                    {
                        endpoint.PingButton.IsEnabled = true;
                    }
                }
            }

            // Test All button:
            // - Turns into Stop
            // - Does NOT turn red
            if (TestAllTowerSectorsButton is not null)
            {
                TestAllTowerSectorsButton.Content = testAllRunning ? "Stop" : "Test All";
                TestAllTowerSectorsButton.Style = (Style)FindResource("PrimaryButtonStyle");
                TestAllTowerSectorsButton.IsEnabled = true;
            }
        }

        private void SetTowerButtonState(Button? button, bool isRunning, string normalText, string normalStyleKey)
        {
            if (button is null)
                return;

            button.Content = isRunning ? "Stop" : normalText;

            button.Style = isRunning
                ? (Style)FindResource("DangerButtonStyle")
                : (Style)FindResource(normalStyleKey);
        }
    }
}