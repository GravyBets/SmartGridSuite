using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardTopBarView : UserControl
    {
        public event EventHandler? LoadRequested;
        public event EventHandler? AddTabRequested;
        public event EventHandler<string?>? SelectedTabChanged;
        public event EventHandler<string?>? CloseTabRequested;
        public event EventHandler? PopOutRequested;

        private bool _syncingTabs;

        public SiteDashboardTopBarView()
        {
            InitializeComponent();
            UpdateSearchWatermark();
        }

        public string SearchText
        {
            get => SearchTextBox.Text;
            set => SearchTextBox.Text = value ?? string.Empty;
        }

        public string AddressText
        {
            get => AddressTextBlock.Text;
            set => AddressTextBlock.Text = string.IsNullOrWhiteSpace(value) ? "—" : value;
        }

        public string CoordinatesText
        {
            get => CoordinatesTextBlock.Text;
            set => CoordinatesTextBlock.Text = string.IsNullOrWhiteSpace(value) ? "—" : value;
        }

        public string StatusText
        {
            get => SearchWatermarkTextBlock?.Text ?? string.Empty;
            set
            {
                if (SearchWatermarkTextBlock != null)
                    SearchWatermarkTextBlock.Text = value ?? string.Empty;

                UpdateSearchWatermark();
            }
        }

        public void SetLoading(bool isLoading)
        {
            SearchTextBox.IsEnabled = !isLoading;
            LoadButton.IsEnabled = !isLoading;
            AddTabButton.IsEnabled = !isLoading;
            SiteTabsControl.IsEnabled = !isLoading;
        }

        public void ResetHeader()
        {
            AddressText = "—";
            CoordinatesText = "—";
            StatusText = "Enter a site ID and load the dashboard.";
        }

        public void SetTabs(IEnumerable<SiteDashboardTabSession> sessions, string? selectedSessionKey)
        {
            _syncingTabs = true;

            SiteTabsControl.Items.Clear();

            var tabIndex = 0;

            foreach (var session in sessions)
            {
                var sessionKey = session.SessionKey;

                var headerGrid = new Grid
                {
                    Margin = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titleText = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(session.HeaderText) ? "Blank" : session.HeaderText,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 2, 0)
                };

                Grid.SetColumn(titleText, 0);
                headerGrid.Children.Add(titleText);

                var closeButton = new Button
                {
                    Content = "×",
                    Tag = sessionKey,
                    ToolTip = "Close tab",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                if (TryFindResource("TabCloseButtonStyle") is Style closeStyle)
                    closeButton.Style = closeStyle;

                closeButton.Click += CloseButton_Click;

                Grid.SetColumn(closeButton, 1);
                headerGrid.Children.Add(closeButton);

                var tab = new TabItem
                {
                    Header = headerGrid,
                    Tag = sessionKey,
                    Content = new Grid(),
                    Margin = tabIndex == 0
                        ? new Thickness(0, 0, 0, 0)
                        : new Thickness(-4, 0, 0, 0)
                };

                SiteTabsControl.Items.Add(tab);

                if (session.SessionKey == selectedSessionKey)
                    SiteTabsControl.SelectedItem = tab;

                tabIndex++;
            }

            if (SiteTabsControl.SelectedItem is null && SiteTabsControl.Items.Count > 0)
                SiteTabsControl.SelectedIndex = 0;

            _syncingTabs = false;
        }

        public void SetSelectedTab(string? sessionKey)
        {
            _syncingTabs = true;

            foreach (var item in SiteTabsControl.Items.OfType<TabItem>())
            {
                if (Equals(item.Tag, sessionKey))
                {
                    SiteTabsControl.SelectedItem = item;
                    break;
                }
            }

            _syncingTabs = false;
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            LoadRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            LoadRequested?.Invoke(this, EventArgs.Empty);
        }

        private void AddTabButton_Click(object sender, RoutedEventArgs e)
        {
            AddTabRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SiteTabsControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingTabs)
                return;

            if (SiteTabsControl.SelectedItem is TabItem item)
                SelectedTabChanged?.Invoke(this, item.Tag as string);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is Button button)
                CloseTabRequested?.Invoke(this, button.Tag as string);
        }

        //Watermark in Search Box Helpers
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchWatermark();
        }

        private void UpdateSearchWatermark()
        {
            if (SearchWatermarkTextBlock == null || SearchTextBox == null)
                return;

            SearchWatermarkTextBlock.Visibility =
                string.IsNullOrWhiteSpace(SearchTextBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        //Pop-Out Button
        private void PopOutButton_Click(object sender, RoutedEventArgs e)
        {
            PopOutRequested?.Invoke(this, EventArgs.Empty);
        }

        public void SetPopOutButtonVisible(bool isVisible)
        {
            if (PopOutButton is null)
                return;

            PopOutButton.Visibility = isVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Copy Buttons
        private const string TopBarCopyGlyph = "\uE8C8";   // Copy
        private const string TopBarCheckGlyph = "\uE73E";  // Check

        private async void CopyAddressButton_Click(object sender, RoutedEventArgs e)
        {
            await CopyTopBarValueWithFeedbackAsync(
                sender,
                AddressTextBlock?.Text,
                "Copy address");
        }

        private async void CopyCoordinatesButton_Click(object sender, RoutedEventArgs e)
        {
            await CopyTopBarValueWithFeedbackAsync(
                sender,
                CoordinatesTextBlock?.Text,
                "Copy coordinates");
        }

        private async Task CopyTopBarValueWithFeedbackAsync(object sender, string? value, string defaultToolTip)
        {
            var cleanValue = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanValue) || cleanValue == "—")
                return;

            if (sender is not Button button)
                return;

            try
            {
                Clipboard.SetText(cleanValue);
            }
            catch
            {
                button.ToolTip = "Could not copy. Try again.";
                return;
            }

            if (button.Content is not TextBlock glyphBlock)
                return;

            var originalToolTip = button.ToolTip;

            glyphBlock.Text = TopBarCheckGlyph;
            button.ToolTip = "Copied!";

            await Task.Delay(TimeSpan.FromSeconds(3));

            glyphBlock.Text = TopBarCopyGlyph;
            button.ToolTip = originalToolTip ?? defaultToolTip;
        }
    }
}