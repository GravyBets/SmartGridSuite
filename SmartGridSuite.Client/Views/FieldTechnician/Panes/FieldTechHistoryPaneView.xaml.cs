#nullable enable
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.FieldTechnician;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.FieldTechnician.Panes
{
    public partial class FieldTechHistoryPaneView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ApiClient _api = new("https://localhost:7140/");

        private readonly List<HistoryDateRangeOption> _dateRangeOptions = new();

        private CancellationTokenSource? _searchDebounceCts;
        private CancellationTokenSource? _copyFeedbackCts;

        private readonly Dictionary<Button, CancellationTokenSource> _gridCopyFeedbackTokens = new();

        private readonly HashSet<long> _expandedHistorySubmissionIds = new();

        private bool _loadedOnce;
        private bool _busyLoading;
        private bool _suppressFilterEvents;

        private string _statusMessage = "Ready.";
        private int _totalCompletedCount;
        private int _withWorkOrderCount;
        private int _withoutWorkOrderCount;
        private string _appliedDateRangeText = "";

        public ObservableCollection<FieldTechHistoryItemDto> HistoryItems { get; } = new();

        public int TotalCompletedCount
        {
            get => _totalCompletedCount;
            private set
            {
                if (_totalCompletedCount == value)
                    return;

                _totalCompletedCount = value;
                OnPropertyChanged();
            }
        }

        public int WithWorkOrderCount
        {
            get => _withWorkOrderCount;
            private set
            {
                if (_withWorkOrderCount == value)
                    return;

                _withWorkOrderCount = value;
                OnPropertyChanged();
            }
        }

        public int WithoutWorkOrderCount
        {
            get => _withoutWorkOrderCount;
            private set
            {
                if (_withoutWorkOrderCount == value)
                    return;

                _withoutWorkOrderCount = value;
                OnPropertyChanged();
            }
        }

        public string AppliedDateRangeText
        {
            get => _appliedDateRangeText;
            private set
            {
                if (_appliedDateRangeText == value)
                    return;

                _appliedDateRangeText = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage == value)
                    return;

                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public FieldTechHistoryPaneView()
        {
            InitializeComponent();
            DataContext = this;

            ConfigureDateRangeFilter();

            Loaded += async (_, __) =>
            {
                if (_loadedOnce)
                    return;

                _loadedOnce = true;
                await LoadHistoryAsync();
            };
        }

        // Sets the available API date-window presets and defaults History to
        // the most recent 30 days of completed technician write-ups.
        private void ConfigureDateRangeFilter()
        {
            _suppressFilterEvents = true;

            _dateRangeOptions.AddRange(new[]
            {
                new HistoryDateRangeOption("Today", HistoryDateRangeKind.Today),
                new HistoryDateRangeOption("This Week", HistoryDateRangeKind.ThisWeek),
                new HistoryDateRangeOption("Last Week", HistoryDateRangeKind.LastWeek),
                new HistoryDateRangeOption("Last 30 Days", HistoryDateRangeKind.Last30Days),
                new HistoryDateRangeOption("Last 365 Days", HistoryDateRangeKind.Last365Days),
                new HistoryDateRangeOption("Custom Range", HistoryDateRangeKind.Custom)
            });

            DateRangeFilter.ItemsSource = _dateRangeOptions;
            DateRangeFilter.DisplayMemberPath = nameof(HistoryDateRangeOption.DisplayName);
            DateRangeFilter.SelectedItem = _dateRangeOptions
                .First(x => x.Kind == HistoryDateRangeKind.Last30Days);

            var today = DateTime.Today;
            FromDatePicker.SelectedDate = today.AddDays(-29);
            ToDatePicker.SelectedDate = today;

            _suppressFilterEvents = false;
        }

        // Reloads History from the API using write-up completion dates and
        // current ticket Work Orders rather than locally filtering stale rows.
        private async Task LoadHistoryAsync()
        {
            if (_busyLoading)
                return;

            try
            {
                _busyLoading = true;
                StatusMessage = "Loading completed write-up history...";

                var technician = await CurrentUserService
                    .LoadCurrentTechnicianAsync(forceRefresh: true);

                if (technician == null ||
                    string.IsNullOrWhiteSpace(technician.EmployeeId))
                {
                    ClearHistoryResults();
                    StatusMessage =
                        "No active technician record was found for the signed-in user.";
                    return;
                }

                var (from, to) = GetRequestedDateRange();

                var request = new FieldTechHistoryQueryRequest
                {
                    From = from,
                    To = to,
                    Search = string.IsNullOrWhiteSpace(SearchBox.Text)
                        ? null
                        : SearchBox.Text.Trim(),
                    Skip = 0,
                    Take = 2000
                };

                var employeeId = Uri.EscapeDataString(technician.EmployeeId);

                var result = await _api.PostAsync<
                    FieldTechHistoryQueryRequest,
                    FieldTechHistoryQueryResponse>(
                        $"api/tickets/field-tech/history/{employeeId}/query",
                        request);

                ClearHistoryResults();

                foreach (var row in result?.Items ??
                         new List<FieldTechHistoryItemDto>())
                {
                    HistoryItems.Add(row);
                }

                TotalCompletedCount = result?.TotalCount ?? 0;
                WithWorkOrderCount = result?.ItemsWithWorkOrderCount ?? 0;
                WithoutWorkOrderCount = result?.ItemsWithoutWorkOrderCount ?? 0;

                if (result != null)
                {
                    AppliedDateRangeText =
                        $"{result.AppliedFrom:MM/dd/yyyy} - {result.AppliedTo:MM/dd/yyyy}";
                }

                var technicianName =
                    !string.IsNullOrWhiteSpace(result?.TechnicianName)
                        ? result!.TechnicianName
                        : technician.Name;

                StatusMessage =
                    $"Loaded {TotalCompletedCount} completed write-up(s) for {technicianName}.";
            }
            catch (ApiClient.ApiConnectionException)
            {
                StatusMessage =
                    "Offline — showing the last successfully loaded History results. " +
                    "Reconnect and click Refresh to try again.";
            }
            catch (ApiClient.ApiException ex)
            {
                StatusMessage =
                    $"Unable to refresh History. The server returned error " +
                    $"{ex.StatusCode}. Existing results were kept.";
            }
            catch (Exception ex)
            {
                StatusMessage =
                    "An unexpected error occurred while refreshing History. " +
                    "Existing results were kept.";

                MessageBox.Show(
                    ex.Message,
                    "Field Technician History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _busyLoading = false;
            }
        }

        // Converts the selected date preset into an API request range; the API
        // remains responsible for enforcing the one-year History boundary.
        private (DateTime From, DateTime To) GetRequestedDateRange()
        {
            var today = DateTime.Today;

            if (DateRangeFilter.SelectedItem is not HistoryDateRangeOption selected)
                return (today.AddDays(-29), today);

            return selected.Kind switch
            {
                HistoryDateRangeKind.Today =>
                    (today, today),

                HistoryDateRangeKind.ThisWeek =>
                    (GetMondayOfWeek(today), today),

                HistoryDateRangeKind.LastWeek =>
                    (
                        GetMondayOfWeek(today).AddDays(-7),
                        GetMondayOfWeek(today).AddDays(-1)
                    ),

                HistoryDateRangeKind.Last365Days =>
                    (today.AddDays(-364), today),

                HistoryDateRangeKind.Custom =>
                    (
                        FromDatePicker.SelectedDate?.Date ?? today.AddDays(-29),
                        ToDatePicker.SelectedDate?.Date ?? today
                    ),

                _ =>
                    (today.AddDays(-29), today)
            };
        }

        // Returns the Monday used by the weekly time-entry date presets.
        private static DateTime GetMondayOfWeek(DateTime date)
        {
            var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-daysFromMonday);
        }

        // Shows or hides custom date controls and immediately requests the
        // selected preset range from the API when a standard preset is chosen.
        private async void DateRangeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents ||
                DateRangeFilter.SelectedItem is not HistoryDateRangeOption selected)
            {
                return;
            }

            var isCustom = selected.Kind == HistoryDateRangeKind.Custom;

            CustomFromColumn.Width = isCustom
                ? new GridLength(150)
                : new GridLength(0);

            CustomDateSpacerColumn.Width = isCustom
                ? new GridLength(0)
                : new GridLength(0);

            CustomToColumn.Width = isCustom
                ? new GridLength(150)
                : new GridLength(0);

            FromDateLabel.Visibility = isCustom
                ? Visibility.Visible
                : Visibility.Collapsed;

            ToDateLabel.Visibility = isCustom
                ? Visibility.Visible
                : Visibility.Collapsed;

            FromDatePicker.Visibility = isCustom
                ? Visibility.Visible
                : Visibility.Collapsed;

            ToDatePicker.Visibility = isCustom
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (!isCustom && _loadedOnce)
                await LoadHistoryAsync();
        }

        // Applies a custom date change only when the user explicitly clicks
        // Apply, preventing extra API calls while both dates are being chosen.
        private void CustomDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressFilterEvents)
                return;
        }

        // Requests the currently selected custom or preset date range from the API.
        private async void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            await LoadHistoryAsync();
        }

        // Refreshes the current server-filtered History result set.
        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadHistoryAsync();
        }

        // Debounces search typing, then sends the search expression to the API
        // so the WPF client does not perform its own History filtering.
        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_loadedOnce)
                return;

            _searchDebounceCts?.Cancel();
            _searchDebounceCts?.Dispose();

            _searchDebounceCts = new CancellationTokenSource();
            var token = _searchDebounceCts.Token;

            try
            {
                await Task.Delay(350, token);

                if (!token.IsCancellationRequested)
                    await LoadHistoryAsync();
            }
            catch (TaskCanceledException)
            {
                // Typing continued; only the final search value is sent to the API.
            }
        }

        // Restores collapsed or expanded state when WPF creates or recycles a
        // History row container during scrolling or re-rendering.
        private void HistoryGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is not FieldTechHistoryItemDto historyItem)
                return;

            e.Row.DetailsVisibility = _expandedHistorySubmissionIds.Contains(historyItem.SubmissionId)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Expands or collapses the submitted write-up beneath one History row.
        private void ToggleHistoryRowDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                FindVisualParent<DataGridRow>(button) is not DataGridRow row ||
                row.Item is not FieldTechHistoryItemDto historyItem)
            {
                return;
            }

            if (row.DetailsVisibility == Visibility.Visible)
            {
                row.DetailsVisibility = Visibility.Collapsed;
                _expandedHistorySubmissionIds.Remove(historyItem.SubmissionId);
                return;
            }

            row.DetailsVisibility = Visibility.Visible;
            _expandedHistorySubmissionIds.Add(historyItem.SubmissionId);
        }

        // Enables bulk-copy only when at least one completed-work row is selected
        // and keeps the header checkbox synchronized with the visible result set.
        private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateHistorySelectionActions();
        }

        // Selects or clears every currently displayed API-returned History row.
        private void HistoryHeaderSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox)
                return;

            if (checkBox.IsChecked == true)
                HistoryGrid.SelectAll();
            else
                HistoryGrid.UnselectAll();

            UpdateHistorySelectionActions();
        }

        // Allows each checkbox to toggle its full DataGrid row while preserving
        // multi-selection for the Copy Selected Orders workflow.
        private void HistoryRowCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not CheckBox checkBox ||
                FindVisualParent<DataGridRow>(checkBox) is not DataGridRow row)
            {
                return;
            }

            row.IsSelected = !row.IsSelected;

            e.Handled = true;
        }

        // Updates selected-copy availability and the select-all indicator.
        private void UpdateHistorySelectionActions()
        {
            var selectedCount = HistoryGrid.SelectedItems
                .OfType<FieldTechHistoryItemDto>()
                .Count();

            CopySelectedOrdersButton.IsEnabled = selectedCount > 0;

            HistoryHeaderSelectAllCheckBox.IsChecked =
                HistoryItems.Count > 0 &&
                selectedCount == HistoryItems.Count;
        }

        // Copies current valid Work Orders for the selected completed-work rows and
        // provides lightweight in-place feedback instead of interrupting time entry.
        private async void CopySelectedOrders_Click(object sender, RoutedEventArgs e)
        {
            var selectedSubmissionIds = HistoryGrid.SelectedItems
                .OfType<FieldTechHistoryItemDto>()
                .Select(x => x.SubmissionId)
                .ToHashSet();

            var selectedRows = HistoryGrid.Items
                .OfType<FieldTechHistoryItemDto>()
                .Where(x => selectedSubmissionIds.Contains(x.SubmissionId))
                .ToList();

            if (selectedRows.Count == 0)
                return;

            var workOrders = selectedRows
                .Select(x => (x.CurrentWorkOrder ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (workOrders.Count == 0)
            {
                MessageBox.Show(
                    "None of the selected completed work currently has a Work Order available to copy.",
                    "Copy Selected Orders",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            Clipboard.SetText(string.Join(Environment.NewLine, workOrders));

            await ShowCopiedFeedbackAsync();
        }

        // Shows temporary confirmation on the copy button without interrupting the
        // technician with a modal success message while entering time.
        private async Task ShowCopiedFeedbackAsync()
        {
            _copyFeedbackCts?.Cancel();
            _copyFeedbackCts?.Dispose();

            _copyFeedbackCts = new CancellationTokenSource();
            var token = _copyFeedbackCts.Token;

            CopySelectedOrdersButton.Content = "Copied";

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);

                if (!token.IsCancellationRequested)
                    CopySelectedOrdersButton.Content = "Copy Selected Orders";
            }
            catch (TaskCanceledException)
            {
                // Another copy action restarted the feedback period.
            }
        }

        // Copies one Notification or current Work Order value directly from its row
        // and confirms the action with a temporary checkmark on that specific icon.
        private async void CopyGridValue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var value = (button.Tag?.ToString() ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(value))
                return;

            Clipboard.SetText(value);

            await ShowGridCopyFeedbackAsync(button);
        }

        // Replaces one clicked mini-copy icon with a checkmark for three seconds.
        // Separate timers allow multiple cells to confirm independently.
        private async Task ShowGridCopyFeedbackAsync(Button button)
        {
            if (_gridCopyFeedbackTokens.TryGetValue(button, out var existingToken))
            {
                existingToken.Cancel();
                existingToken.Dispose();
            }

            var feedbackToken = new CancellationTokenSource();
            _gridCopyFeedbackTokens[button] = feedbackToken;

            button.Content = CreateGridFeedbackGlyph("CheckGlyph");

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), feedbackToken.Token);

                if (!feedbackToken.IsCancellationRequested)
                    button.Content = CreateGridFeedbackGlyph("CopyGlyph");
            }
            catch (TaskCanceledException)
            {
                // The same icon was clicked again, so its feedback timer restarted.
            }
            finally
            {
                if (_gridCopyFeedbackTokens.TryGetValue(button, out var currentToken) &&
                    ReferenceEquals(currentToken, feedbackToken))
                {
                    _gridCopyFeedbackTokens.Remove(button);
                    feedbackToken.Dispose();
                }
            }
        }

        // Builds either the normal copy glyph or temporary checkmark glyph
        // using the same resource styling applied in the History grid.
        private TextBlock CreateGridFeedbackGlyph(string styleKey)
        {
            return new TextBlock
            {
                Style = (Style)FindResource(styleKey)
            };
        }

        // Clears visible API results, copy feedback, and bulk-copy selection before
        // new data loads so refreshed rows always begin with their normal icons.
        private void ClearHistoryResults()
        {
            _copyFeedbackCts?.Cancel();
            _copyFeedbackCts?.Dispose();
            _copyFeedbackCts = null;

            CopySelectedOrdersButton.Content = "Copy Selected Orders";

            ResetGridCopyFeedback();

            _expandedHistorySubmissionIds.Clear();
            CollapseVisibleHistoryDetailsRows();

            HistoryGrid.UnselectAll();
            HistoryItems.Clear();

            TotalCompletedCount = 0;
            WithWorkOrderCount = 0;
            WithoutWorkOrderCount = 0;
            AppliedDateRangeText = "";

            UpdateHistorySelectionActions();
        }

        // Collapses any visible expanded History rows before the result set changes.
        private void CollapseVisibleHistoryDetailsRows()
        {
            foreach (var item in HistoryGrid.Items)
            {
                if (HistoryGrid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow row)
                    continue;

                row.DetailsVisibility = Visibility.Collapsed;
            }
        }

        // Cancels individual mini-copy confirmations when the visible History result
        // set changes and restores each affected cell to the standard copy icon.
        private void ResetGridCopyFeedback()
        {
            foreach (var feedback in _gridCopyFeedbackTokens.ToList())
            {
                feedback.Value.Cancel();
                feedback.Value.Dispose();

                feedback.Key.Content = CreateGridFeedbackGlyph("CopyGlyph");
            }

            _gridCopyFeedbackTokens.Clear();
        }

        // Finds the containing DataGrid row for checkbox interactions inside a cell template.
        private static T? FindVisualParent<T>(DependencyObject? child)
            where T : DependencyObject
        {
            var current = child;

            while (current != null)
            {
                if (current is T match)
                    return match;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private sealed class HistoryDateRangeOption
        {
            public HistoryDateRangeOption(string displayName, HistoryDateRangeKind kind)
            {
                DisplayName = displayName;
                Kind = kind;
            }

            public string DisplayName { get; }

            public HistoryDateRangeKind Kind { get; }

            // Ensures custom ComboBox templates display the selected label
            // instead of the nested option type name.
            public override string ToString()
                => DisplayName;
        }

        private enum HistoryDateRangeKind
        {
            Today,
            ThisWeek,
            LastWeek,
            Last30Days,
            Last365Days,
            Custom
        }
    }
}