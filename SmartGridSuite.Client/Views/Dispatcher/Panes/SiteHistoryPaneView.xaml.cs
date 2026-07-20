#nullable enable

using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Tickets;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteHistoryPaneView : UserControl, INotifyPropertyChanged
    {
        private readonly ApiClient _api;
        private readonly TicketsApi _ticketsApi;

        private DispatcherSiteHistoryRowViewModel? _selectedHistoryRow;

        private string _currentSiteId = "";
        private string _originalPrimaryTech = "";
        private string _originalSecondaryTech = "";
        private string _originalIssue = "";
        private string _originalNarrative = "";

        private bool _isEditing;
        private bool _isLoading;

        private int _busyOverlayDepth;

        public SiteHistoryPaneView()
        {
            InitializeComponent();

            _api = new ApiClient("https://localhost:7140");
            _ticketsApi = new TicketsApi(_api);

            DataContext = this;

            TechnicianOptions.Add("—");

            _ = LoadTechnicianOptionsAsync();
        }

        public ObservableCollection<DispatcherSiteHistoryRowViewModel> HistoryRows { get; } = new();

        public ObservableCollection<string> TechnicianOptions { get; } = new();

        public DispatcherSiteHistoryRowViewModel? SelectedHistoryRow
        {
            get => _selectedHistoryRow;
            private set
            {
                if (_selectedHistoryRow == value)
                    return;

                _selectedHistoryRow = value;
                OnPropertyChanged();

                ApplySelectedRow();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            await LoadHistoryAsync();
        }

        private async void SiteSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            e.Handled = true;
            await LoadHistoryAsync();
        }

        private async Task LoadHistoryAsync(bool skipUnsavedCheck = false)
        {
            if (_isLoading)
                return;

            if (!skipUnsavedCheck && _isEditing && HasUnsavedChanges())
            {
                var confirm = MessageBox.Show(
                    Window.GetWindow(this),
                    "You have unsaved changes. Discard them and search again?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            var siteId = (SiteSearchTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "Enter a site ID first.",
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                SiteSearchTextBox.Focus();
                return;
            }

            _isLoading = true;
            _currentSiteId = siteId;

            ShowBusyOverlay($"Loading history for {siteId}...");

            try
            {
                SetEditMode(false);

                HistoryRows.Clear();
                HistoryGrid.SelectedItem = null;
                SelectedHistoryRow = null;

                ResultsHeaderTextBlock.Text = $"Loading history for {siteId}...";
                ResultsCountTextBlock.Text = "";
                SelectedHintTextBlock.Text = "Select a history row to view it.";
                ClearDetails();

                var rows = await _ticketsApi.GetSiteHistoryAsync(siteId);

                foreach (var row in rows.Select(MapHistoryRow))
                    HistoryRows.Add(row);

                ResultsHeaderTextBlock.Text = $"Site History for {siteId}";
                ResultsCountTextBlock.Text = $"{HistoryRows.Count} row(s)";

                if (HistoryRows.Count == 0)
                    SelectedHintTextBlock.Text = "No site history was found for this site.";
            }
            catch (ApiClient.ApiException ex)
            {
                ResultsHeaderTextBlock.Text = "Search failed";
                ResultsCountTextBlock.Text = "";

                MessageBox.Show(
                    Window.GetWindow(this),
                    ex.Body ?? ex.Message,
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                ResultsHeaderTextBlock.Text = "Search failed";
                ResultsCountTextBlock.Text = "";

                MessageBox.Show(
                    Window.GetWindow(this),
                    ex.Message,
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                HideBusyOverlay();
                UpdateButtons();
            }
        }

        private async Task LoadTechnicianOptionsAsync()
        {
            ShowBusyOverlay("Loading technician list...");

            try
            {
                var currentPrimary = GetSelectedComboText(Tech1ComboBox);
                var currentSecondary = GetSelectedComboText(Tech2ComboBox);

                var techs = await _api.GetAsync<List<TechnicianLookupDto>>(
                    "api/technicians");

                var names = (techs ?? new List<TechnicianLookupDto>())
                    .Where(x => x.IsActive)
                    .Select(GetTechnicianDisplayName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

                TechnicianOptions.Clear();
                TechnicianOptions.Add("—");

                foreach (var name in names)
                    TechnicianOptions.Add(name);

                RestoreComboSelection(Tech1ComboBox, currentPrimary);
                RestoreComboSelection(Tech2ComboBox, currentSecondary);
            }
            catch
            {
                if (TechnicianOptions.Count == 0)
                    TechnicianOptions.Add("—");
            }
            finally
            {
                HideBusyOverlay();
            }
        }

        private static DispatcherSiteHistoryRowViewModel MapHistoryRow(SiteHistoryPreviewDto dto)
        {
            var effectiveDateTime =
                dto.SubmittedAt ??
                dto.VisitDate;

            return new DispatcherSiteHistoryRowViewModel
            {
                HistoryId = dto.HistoryId,
                SubmissionId = dto.SubmissionId,
                SiteId = dto.SiteId ?? "",
                SourceType = dto.SourceType ?? "",

                SubmittedAt = dto.SubmittedAt,
                VisitDate = dto.VisitDate,

                DateTimeText = FormatHistoryDateTime(effectiveDateTime),
                Tech1Text = string.IsNullOrWhiteSpace(dto.PrimaryTech) ? "—" : dto.PrimaryTech.Trim(),
                Tech2Text = string.IsNullOrWhiteSpace(dto.SecondaryTech) ? "—" : dto.SecondaryTech.Trim(),
                IssueText = string.IsNullOrWhiteSpace(dto.IssueText) ? "Other" : dto.IssueText.Trim(),
                NarrativeText = dto.Narrative ?? "",

                EditedAt = dto.EditedAt,
                EditedBy = dto.EditedBy ?? ""
            };
        }

        private static string FormatHistoryDateTime(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("MM-dd-yyyy HH:mm")
                : "—";
        }

        private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isEditing && HasUnsavedChanges())
            {
                var confirm = MessageBox.Show(
                    Window.GetWindow(this),
                    "You have unsaved changes. Discard them and select another row?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                {
                    HistoryGrid.SelectionChanged -= HistoryGrid_SelectionChanged;
                    HistoryGrid.SelectedItem = SelectedHistoryRow;
                    HistoryGrid.SelectionChanged += HistoryGrid_SelectionChanged;
                    return;
                }
            }

            SelectedHistoryRow = HistoryGrid.SelectedItem as DispatcherSiteHistoryRowViewModel;
        }

        private void ApplySelectedRow()
        {
            SetEditMode(false);

            if (SelectedHistoryRow == null)
            {
                _originalPrimaryTech = "";
                _originalSecondaryTech = "";
                _originalIssue = "";
                _originalNarrative = "";

                ClearDetails();

                SelectedHintTextBlock.Text = "Select a history row to view it.";
                UpdateButtons();
                return;
            }

            _originalPrimaryTech = NormalizeTechSelection(SelectedHistoryRow.Tech1Text);
            _originalSecondaryTech = NormalizeTechSelection(SelectedHistoryRow.Tech2Text);
            _originalIssue = (SelectedHistoryRow.IssueText ?? string.Empty).Trim();
            _originalNarrative = CleanNarrativeText(SelectedHistoryRow.NarrativeText);

            DateTimeTextBox.Text = SelectedHistoryRow.DateTimeText;

            RestoreComboSelection(Tech1ComboBox, _originalPrimaryTech);
            RestoreComboSelection(Tech2ComboBox, _originalSecondaryTech);

            IssueTextBox.Text = _originalIssue;
            NarrativeTextBox.Text = _originalNarrative;

            SelectedHintTextBlock.Text = SelectedHistoryRow.CanEditWriteUp
                ? "SmartGridSuite write-up selected. Dispatch may edit the techs, issue/body, or soft-delete this entry."
                : "Legacy or imported history selected. This entry is read-only.";

            EditAuditTextBlock.Text = SelectedHistoryRow.EditedText;

            UpdateButtons();
        }

        private void ClearDetails()
        {
            DateTimeTextBox.Text = "";
            Tech1ComboBox.SelectedItem = null;
            Tech2ComboBox.SelectedItem = null;
            IssueTextBox.Text = "";
            NarrativeTextBox.Text = "";
            EditAuditTextBlock.Text = "";
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedHistoryRow?.CanEditWriteUp != true)
                return;

            SetEditMode(true);

            Tech1ComboBox.Focus();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedHistoryRow?.CanEditWriteUp != true ||
                !SelectedHistoryRow.SubmissionId.HasValue)
            {
                return;
            }

            var primaryTech = GetSelectedComboText(Tech1ComboBox);
            var secondaryTech = GetSelectedComboText(Tech2ComboBox);
            var issueText = (IssueTextBox.Text ?? string.Empty).Trim();
            var narrative = (NarrativeTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(narrative))
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    "Write-up text is required.",
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                NarrativeTextBox.Focus();
                return;
            }

            ShowBusyOverlay("Saving site history write-up...");

            try
            {
                SaveButton.IsEnabled = false;

                await _ticketsApi.UpdateSubmittedWriteUpAsync(
                    SelectedHistoryRow.SubmissionId.Value,
                    new UpdateSubmittedWriteUpRequest
                    {
                        Narrative = narrative,
                        IssueText = issueText,
                        PrimaryTech = primaryTech,
                        SecondaryTech = secondaryTech,
                        UpdatedBy = GetWindowsEmployeeId()
                    });

                _originalPrimaryTech = primaryTech;
                _originalSecondaryTech = secondaryTech;
                _originalIssue = issueText;
                _originalNarrative = narrative;

                SetEditMode(false);

                await LoadHistoryAsync(skipUnsavedCheck: true);

                MessageBox.Show(
                    Window.GetWindow(this),
                    "Write-up updated.",
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    ex.Body ?? ex.Message,
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    ex.Message,
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusyOverlay();
                UpdateButtons();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            RestoreComboSelection(Tech1ComboBox, _originalPrimaryTech);
            RestoreComboSelection(Tech2ComboBox, _originalSecondaryTech);

            IssueTextBox.Text = _originalIssue;
            NarrativeTextBox.Text = _originalNarrative;

            SetEditMode(false);
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedHistoryRow?.CanEditWriteUp != true ||
                !SelectedHistoryRow.SubmissionId.HasValue)
            {
                return;
            }

            var confirm = MessageBox.Show(
                Window.GetWindow(this),
                "Delete this submitted write-up from normal Site History and Field History views?\n\n" +
                "This is a soft delete. The record remains in the database for audit/recovery.",
                "Delete Write-Up",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            ShowBusyOverlay("Deleting site history write-up...");

            try
            {
                DeleteButton.IsEnabled = false;

                await _ticketsApi.DeleteSubmittedWriteUpAsync(
                    SelectedHistoryRow.SubmissionId.Value,
                    new DeleteSubmittedWriteUpRequest
                    {
                        DeletedBy = GetWindowsEmployeeId()
                    });

                SetEditMode(false);

                await LoadHistoryAsync(skipUnsavedCheck: true);

                MessageBox.Show(
                    Window.GetWindow(this),
                    "Write-up deleted.",
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (ApiClient.ApiException ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    ex.Body ?? ex.Message,
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    ex.Message,
                    "Site History",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                HideBusyOverlay();
                UpdateButtons();
            }
        }

        private void DetailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void DetailComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void SetEditMode(bool isEditing)
        {
            _isEditing = isEditing;

            Tech1ComboBox.IsHitTestVisible = _isEditing;
            Tech1ComboBox.Focusable = _isEditing;

            Tech2ComboBox.IsHitTestVisible = _isEditing;
            Tech2ComboBox.Focusable = _isEditing;

            IssueTextBox.IsReadOnly = !_isEditing;
            NarrativeTextBox.IsReadOnly = !_isEditing;

            UpdateButtons();
        }

        private bool HasUnsavedChanges()
        {
            var currentPrimaryTech = GetSelectedComboText(Tech1ComboBox);
            var currentSecondaryTech = GetSelectedComboText(Tech2ComboBox);

            var currentIssue =
                (IssueTextBox.Text ?? string.Empty).Trim();

            var currentNarrative =
                (NarrativeTextBox.Text ?? string.Empty).Trim();

            return
                !string.Equals(
                    currentPrimaryTech,
                    NormalizeTechSelection(_originalPrimaryTech),
                    StringComparison.Ordinal) ||

                !string.Equals(
                    currentSecondaryTech,
                    NormalizeTechSelection(_originalSecondaryTech),
                    StringComparison.Ordinal) ||

                !string.Equals(
                    currentIssue,
                    (_originalIssue ?? string.Empty).Trim(),
                    StringComparison.Ordinal) ||

                !string.Equals(
                    currentNarrative,
                    (_originalNarrative ?? string.Empty).Trim(),
                    StringComparison.Ordinal);
        }

        private void UpdateButtons()
        {
            if (!IsLoaded)
                return;

            var canEdit =
                SelectedHistoryRow?.CanEditWriteUp == true;

            var isDirty =
                _isEditing &&
                HasUnsavedChanges();

            EditButton.IsEnabled =
                canEdit &&
                !_isEditing &&
                !_isLoading;

            SaveButton.IsEnabled =
                canEdit &&
                _isEditing &&
                isDirty &&
                !_isLoading;

            CancelButton.IsEnabled =
                canEdit &&
                _isEditing &&
                !_isLoading;

            DeleteButton.IsEnabled =
                canEdit &&
                !_isEditing &&
                !_isLoading;
        }

        private static string NormalizeTechSelection(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return "—";

            return text;
        }

        private string GetSelectedComboText(ComboBox comboBox)
        {
            return comboBox.SelectedItem?.ToString()?.Trim() ?? "—";
        }

        private void RestoreComboSelection(ComboBox comboBox, string? value)
        {
            var text = NormalizeTechSelection(value);

            if (!TechnicianOptions.Any(x =>
                    string.Equals(x, text, StringComparison.OrdinalIgnoreCase)))
            {
                TechnicianOptions.Add(text);
            }

            var match = TechnicianOptions.FirstOrDefault(x =>
                string.Equals(x, text, StringComparison.OrdinalIgnoreCase));

            comboBox.SelectedItem = match ?? "—";
        }

        private static string CleanNarrativeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");

            while (normalized.Contains("\n\n\n"))
                normalized = normalized.Replace("\n\n\n", "\n\n");

            return normalized.Trim();
        }

        private static string GetTechnicianDisplayName(TechnicianLookupDto tech)
        {
            var name = FirstNonBlank(
                tech.Name,
                tech.FullName,
                $"{tech.FirstName} {tech.LastName}",
                tech.EmployeeId);

            return name.Trim();
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string GetWindowsEmployeeId()
        {
            var name =
                WindowsIdentity.GetCurrent()?.Name ??
                string.Empty;

            if (name.Contains('\\'))
                name = name.Split('\\').Last();

            if (name.Contains('@'))
                name = name.Split('@').First();

            return string.IsNullOrWhiteSpace(name)
                ? "Dispatcher"
                : name.Trim();
        }

        private void ShowBusyOverlay(string message)
        {
            _busyOverlayDepth++;

            if (BusyOverlay is null ||
                BusyOverlayMessageTextBlock is null)
            {
                return;
            }

            BusyOverlayMessageTextBlock.Text = string.IsNullOrWhiteSpace(message)
                ? "Loading..."
                : message;

            BusyOverlay.Visibility = Visibility.Visible;
            SetSiteHistoryControlsEnabled(false);

            Cursor = Cursors.Wait;
        }

        private void HideBusyOverlay()
        {
            if (_busyOverlayDepth > 0)
                _busyOverlayDepth--;

            if (_busyOverlayDepth > 0)
                return;

            if (BusyOverlay is null)
                return;

            BusyOverlay.Visibility = Visibility.Collapsed;
            SetSiteHistoryControlsEnabled(true);

            Cursor = null;

            UpdateButtons();
        }

        private void SetSiteHistoryControlsEnabled(bool enabled)
        {
            SiteSearchTextBox.IsEnabled = enabled;
            SearchSiteHistoryButton.IsEnabled = enabled;

            HistoryListCard.IsEnabled = enabled;
            HistoryDetailsCard.IsEnabled = enabled;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
        }

        private sealed class TechnicianLookupDto
        {
            public uint Id { get; set; }

            public string EmployeeId { get; set; } = "";

            public string Name { get; set; } = "";

            public string FullName { get; set; } = "";

            public string FirstName { get; set; } = "";

            public string LastName { get; set; } = "";

            public bool IsActive { get; set; } = true;
        }
    }
}