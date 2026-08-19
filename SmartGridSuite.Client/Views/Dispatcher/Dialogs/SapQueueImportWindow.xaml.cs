using ClosedXML.Excel;
using Microsoft.Win32;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Tickets;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Windows;
using System.Globalization;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class SapQueueImportWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly TicketsApi _ticketsApi;

        public ObservableCollection<SapQueuePreviewDisplayRow> PreviewRows { get; } = new();

        public ObservableCollection<string> SapStatusOptions { get; } = new();

        private string _selectedFilePath = "";
        public string SelectedFilePath
        {
            get => _selectedFilePath;
            set
            {
                if (_selectedFilePath == value) return;
                _selectedFilePath = value;
                OnPropertyChanged();
            }
        }

        private string _createdByDisplay = "";
        public string CreatedByDisplay
        {
            get => _createdByDisplay;
            set
            {
                if (_createdByDisplay == value) return;
                _createdByDisplay = value;
                OnPropertyChanged();
            }
        }

        private string _lastImportDisplay = "Never";
        public string LastImportDisplay
        {
            get => _lastImportDisplay;
            set
            {
                if (_lastImportDisplay == value)
                    return;

                _lastImportDisplay = value;
                OnPropertyChanged();
            }
        }

        private int _totalRows;
        public int TotalRows
        {
            get => _totalRows;
            set
            {
                if (_totalRows == value) return;
                _totalRows = value;
                OnPropertyChanged();
            }
        }

        private int _readyCount;
        public int ReadyCount
        {
            get => _readyCount;
            set
            {
                if (_readyCount == value) return;
                _readyCount = value;
                OnPropertyChanged();
            }
        }

        private int _alreadyExistsCount;
        public int AlreadyExistsCount
        {
            get => _alreadyExistsCount;
            set
            {
                if (_alreadyExistsCount == value) return;
                _alreadyExistsCount = value;
                OnPropertyChanged();
            }
        }

        private int _invalidCount;
        public int InvalidCount
        {
            get => _invalidCount;
            set
            {
                if (_invalidCount == value) return;
                _invalidCount = value;
                OnPropertyChanged();
            }
        }

        private int _willImportOpenCount;
        public int WillImportOpenCount
        {
            get => _willImportOpenCount;
            set
            {
                if (_willImportOpenCount == value) return;
                _willImportOpenCount = value;
                OnPropertyChanged();
            }
        }

        private int _willImportNeedsReviewCount;
        public int WillImportNeedsReviewCount
        {
            get => _willImportNeedsReviewCount;
            set
            {
                if (_willImportNeedsReviewCount == value) return;
                _willImportNeedsReviewCount = value;
                OnPropertyChanged();
            }
        }

        private int _missingSiteCount;
        public int MissingSiteCount
        {
            get => _missingSiteCount;
            set
            {
                if (_missingSiteCount == value) return;
                _missingSiteCount = value;
                OnPropertyChanged();
            }
        }

        private int _withWorkOrderCount;
        public int WithWorkOrderCount
        {
            get => _withWorkOrderCount;
            set
            {
                if (_withWorkOrderCount == value) return;
                _withWorkOrderCount = value;
                OnPropertyChanged();
            }
        }

        private int _withoutWorkOrderCount;
        public int WithoutWorkOrderCount
        {
            get => _withoutWorkOrderCount;
            set
            {
                if (_withoutWorkOrderCount == value) return;
                _withoutWorkOrderCount = value;
                OnPropertyChanged();
            }
        }

        private int _missingProblemCount;
        public int MissingProblemCount
        {
            get => _missingProblemCount;
            set
            {
                if (_missingProblemCount == value) return;
                _missingProblemCount = value;
                OnPropertyChanged();
            }
        }

        private int _spreadsheetReviewCount;
        public int SpreadsheetReviewCount
        {
            get => _spreadsheetReviewCount;
            set
            {
                if (_spreadsheetReviewCount == value)
                    return;

                _spreadsheetReviewCount = value;
                OnPropertyChanged();
            }
        }

        private int _existingAppReviewCount;
        public int ExistingAppReviewCount
        {
            get => _existingAppReviewCount;
            set
            {
                if (_existingAppReviewCount == value)
                    return;

                _existingAppReviewCount = value;
                OnPropertyChanged();
            }
        }

        public SapQueueImportWindow(TicketsApi ticketsApi)
        {
            InitializeComponent();
            _ticketsApi = ticketsApi;
            DataContext = this;

            CreatedByDisplay = TryGetDisplayName()
                               ?? (WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName);

            Loaded += async (_, _) =>
            {
                await LoadLastImportAsync();
                await LoadSapStatusOptionsAsync();
            };
        }

        private async Task LoadLastImportAsync()
        {
            try
            {
                var lastImport =
                    await _ticketsApi.GetLastSapQueueImportAsync();

                if (!lastImport.ImportedAt.HasValue)
                {
                    LastImportDisplay = "Never";
                    return;
                }

                LastImportDisplay =
                    $"{lastImport.ImportedAt.Value:MM/dd/yyyy HH:mm} by " +
                    $"{lastImport.ImportedBy} " +
                    $"({lastImport.ImportedCount} row(s))";
            }
            catch
            {
                LastImportDisplay = "Unavailable";
            }
        }

        private async Task LoadSapStatusOptionsAsync()
        {
            try
            {
                var statuses =
                    await _ticketsApi.GetSapQueueImportStatusOptionsAsync();

                SapStatusOptions.Clear();

                foreach (var status in statuses
                             .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                             .OrderBy(x => x.SortOrder)
                             .ThenBy(x => x.Name))
                {
                    SapStatusOptions.Add(status.Name.Trim());
                }
            }
            catch
            {
                /*
                 * Do not crash the SAP window merely because status options
                 * failed to load. The preview itself can still explain the
                 * reconciliation conditions.
                 *
                 * Before committing reconciliation changes later, we will
                 * explicitly prevent status-changing actions if this list
                 * could not be loaded.
                 */
                SapStatusOptions.Clear();
            }
        }

        private static string? TryGetDisplayName()
        {
            try
            {
                var full = Environment.GetEnvironmentVariable("FULLNAME");
                if (!string.IsNullOrWhiteSpace(full))
                    return full.Trim();
            }
            catch { }

            return null;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select SAP Queue Export",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
                SelectedFilePath = dlg.FileName;
        }

        private async void LoadPreview_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedFilePath) || !File.Exists(SelectedFilePath))
            {
                MessageBox.Show("Please select a valid SAP export file.", "Import SAP Queue",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true, "Loading SAP preview...");

            try
            {
                await Task.Yield();
                var rows = await Task.Run(() => ReadSapRowsFromExcel(SelectedFilePath));

                var preview = await _ticketsApi.PreviewSapQueueImportAsync(
                    new SapQueueImportPreviewRequest(rows));

                PreviewRows.Clear();

                foreach (var row in preview.OrderBy(r => r.RowNumber))
                {
                    var displayRow =
                        new SapQueuePreviewDisplayRow
                        {
                            RowNumber = row.RowNumber,

                            Notification =
                                row.Notification,

                            WorkOrder =
                                row.WorkOrder ?? string.Empty,

                            NotificationDate =
                                row.NotificationDate,

                            Description =
                                row.Description,

                            ParsedSite =
                                row.ParsedSite,

                            ImportStatus =
                                row.ImportStatus,

                            Message =
                                row.Message,

                            RowSource =
                                string.IsNullOrWhiteSpace(row.RowSource)
                                    ? "Spreadsheet"
                                    : row.RowSource,

                            ExistingTicketId =
                                row.ExistingTicketId,

                            CurrentTicketStatus =
                                row.CurrentTicketStatus ?? string.Empty,

                            RequiresReview =
                                row.RequiresReview,

                            ReviewReason =
                                row.ReviewReason ?? string.Empty
                        };

                    ConfigureReconciliationDefaults(displayRow);

                    displayRow.PropertyChanged += PreviewRow_PropertyChanged;

                    PreviewRows.Add(displayRow);
                }

                UpdateCounts();
            }
            catch (IOException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "SAP Export File Is Locked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load SAP preview.\n\n{ex.Message}",
                    "Import SAP Queue",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ImportReady_Click(object sender, RoutedEventArgs e)
        {
            /*
             * ------------------------------------------------------------
             * SPREADSHEET ROWS SELECTED FOR IMPORT
             * ------------------------------------------------------------
             */
            var spreadsheetRowsToImport =
                PreviewRows
                    .Where(r =>
                        r.IsSpreadsheetRow &&
                        string.Equals(
                            r.SelectedAction,
                            "Import",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            /*
             * Validate client-side before we send anything.
             * The API will validate all of this again.
             */
            var missingDateRow =
                spreadsheetRowsToImport
                    .FirstOrDefault(r =>
                        !r.NotificationDate.HasValue);

            if (missingDateRow != null)
            {
                MessageBox.Show(
                    $"Notification {missingDateRow.Notification} cannot be imported " +
                    "because its notification date is missing or invalid.",
                    "SAP Queue Reconciliation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var missingTargetStatusRow =
                spreadsheetRowsToImport
                    .FirstOrDefault(r =>
                        string.IsNullOrWhiteSpace(
                            r.TargetStatus));

            if (missingTargetStatusRow != null)
            {
                MessageBox.Show(
                    $"Choose a target status for notification " +
                    $"{missingTargetStatusRow.Notification}.",
                    "SAP Queue Reconciliation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var commitRows =
                spreadsheetRowsToImport
                    .Select(r =>
                        new SapQueueImportCommitRow(
                            RowNumber:
                                r.RowNumber,

                            Notification:
                                r.Notification,

                            WorkOrder:
                                string.IsNullOrWhiteSpace(r.WorkOrder)
                                    ? null
                                    : r.WorkOrder,

                            NotificationDate:
                                r.NotificationDate!.Value,

                            Description:
                                r.Description,

                            ParsedSite:
                                r.ParsedSite ?? string.Empty,

                            TargetStatus:
                                r.TargetStatus))
                    .ToList();

            /*
             * ------------------------------------------------------------
             * EXISTING SMARTGRIDSUITE TICKET ACTIONS
             * ------------------------------------------------------------
             *
             * Send both:
             *
             *     Keep As Is
             *     Change Status
             *
             * This lets the API report exactly how the reconciliation was
             * handled, even though Keep As Is performs no database mutation.
             */
            var existingRows =
                PreviewRows
                    .Where(r =>
                        r.IsExistingAppRow)
                    .ToList();

            var invalidExistingRow =
                existingRows
                    .FirstOrDefault(r =>
                        !r.ExistingTicketId.HasValue ||
                        r.ExistingTicketId.Value <= 0);

            if (invalidExistingRow != null)
            {
                MessageBox.Show(
                    $"An existing SmartGridSuite reconciliation row does not have " +
                    $"a valid ticket ID. Reload the SAP preview.",
                    "SAP Queue Reconciliation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var missingExistingStatus =
                existingRows
                    .FirstOrDefault(r =>
                        string.Equals(
                            r.SelectedAction,
                            "Change Status",
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        string.IsNullOrWhiteSpace(
                            r.TargetStatus));

            if (missingExistingStatus != null)
            {
                MessageBox.Show(
                    $"Choose a new status for existing ticket " +
                    $"{missingExistingStatus.Notification}.",
                    "SAP Queue Reconciliation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var existingActions =
                existingRows
                    .Select(r =>
                        new SapQueueExistingTicketAction(
                            TicketId:
                                r.ExistingTicketId!.Value,

                            Action:
                                string.Equals(
                                    r.SelectedAction,
                                    "Change Status",
                                    StringComparison.OrdinalIgnoreCase)
                                    ? "Change Status"
                                    : "Keep Current",

                            TargetStatus:
                                string.Equals(
                                    r.SelectedAction,
                                    "Change Status",
                                    StringComparison.OrdinalIgnoreCase)
                                    ? r.TargetStatus
                                    : null))
                    .ToList();

            var changeStatusCount =
                existingRows.Count(r =>
                    string.Equals(
                        r.SelectedAction,
                        "Change Status",
                        StringComparison.OrdinalIgnoreCase));

            var keepCurrentCount =
                existingRows.Count -
                changeStatusCount;

            var skippedSpreadsheetCount =
                PreviewRows.Count(r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.SelectedAction,
                        "Skip",
                        StringComparison.OrdinalIgnoreCase));

            /*
             * There is nothing meaningful to submit if there are neither
             * spreadsheet imports nor existing ticket reconciliation rows.
             */
            if (commitRows.Count == 0 &&
                existingActions.Count == 0)
            {
                MessageBox.Show(
                    "There are no SAP reconciliation actions to apply.",
                    "SAP Queue Reconciliation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var confirmText =
                $"Apply SAP Queue reconciliation?\n\n" +
                $"Spreadsheet tickets to import: {commitRows.Count}\n" +
                $"  As Open: {WillImportOpenCount}\n" +
                $"  As Needs Review: {WillImportNeedsReviewCount}\n" +
                $"  Other selected status: " +
                $"{Math.Max(0, commitRows.Count - WillImportOpenCount - WillImportNeedsReviewCount)}\n" +
                $"Spreadsheet rows skipped: {skippedSpreadsheetCount}\n\n" +
                $"Existing app tickets kept: {keepCurrentCount}\n" +
                $"Existing app tickets changing status: {changeStatusCount}\n\n" +
                $"SAP site conflicts found: {SpreadsheetReviewCount}\n" +
                $"Existing app tickets flagged for review: {ExistingAppReviewCount}";

            var confirm =
                MessageBox.Show(
                    confirmText,
                    "Confirm SAP Queue Reconciliation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            SetBusy(
                true,
                "Applying SAP reconciliation...");

            await Task.Yield();

            try
            {
                var result =
                    await _ticketsApi.CommitSapQueueImportAsync(
                        new SapQueueImportCommitRequest(
                            CreatedBy:
                                CreatedByDisplay,

                            Rows:
                                commitRows,

                            ExistingTicketActions:
                                existingActions));

                ApplyCommitResults(result);

                UpdateCounts();

                var resultMessage =
                    $"SAP Queue reconciliation complete.\n\n" +
                    $"Imported: {result.ImportedCount}\n" +
                    $"Already existed: {result.AlreadyExistsCount}\n" +
                    $"Invalid: {result.InvalidCount}\n" +
                    $"Existing kept: {result.ExistingKeptCount}\n" +
                    $"Existing status changed: {result.ExistingStatusChangedCount}";

                MessageBox.Show(
                    resultMessage,
                    "SAP Reconciliation Complete",
                    MessageBoxButton.OK,
                    result.InvalidCount > 0
                        ? MessageBoxImage.Warning
                        : MessageBoxImage.Information);

                /*
                 * Return true when the operation changed ticket data.
                 * TicketsPaneView will then refresh after this dialog closes.
                 */
                if (result.ImportedCount > 0 ||
                    result.ExistingStatusChangedCount > 0)
                {
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to apply SAP Queue reconciliation.\n\n{ex.Message}",
                    "SAP Queue Reconciliation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ApplyCommitResults(SapQueueImportCommitResponse result)
        {
            var byRow = result.Rows.ToDictionary(x => x.RowNumber);

            foreach (var row in PreviewRows)
            {
                if (!byRow.TryGetValue(row.RowNumber, out var update))
                    continue;

                row.ImportStatus = update.ImportStatus;
                row.Message = update.Message;
            }

            PreviewGrid.Items.Refresh();
        }

        private void UpdateCounts()
        {
            /*
             * Total means rows actually contained in the SAP spreadsheet.
             * Existing SmartGridSuite reconciliation rows are counted separately.
             */
            TotalRows = PreviewRows.Count(
                r => r.IsSpreadsheetRow);

            ReadyCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.ImportStatus,
                        "Ready",
                        StringComparison.OrdinalIgnoreCase));

            SpreadsheetReviewCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    r.RequiresReview);

            ExistingAppReviewCount = PreviewRows.Count(
                r =>
                    r.IsExistingAppRow &&
                    r.RequiresReview);

            AlreadyExistsCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.ImportStatus,
                        "Already Exists",
                        StringComparison.OrdinalIgnoreCase));

            InvalidCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.ImportStatus,
                        "Invalid",
                        StringComparison.OrdinalIgnoreCase));

            /*
             * These next two counts reflect the dispatcher's CURRENT choices,
             * not merely the initial API recommendation.
             */
            WillImportOpenCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.SelectedAction,
                        "Import",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        r.TargetStatus,
                        "Open",
                        StringComparison.OrdinalIgnoreCase));

            WillImportNeedsReviewCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.SelectedAction,
                        "Import",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        r.TargetStatus,
                        "Needs Review",
                        StringComparison.OrdinalIgnoreCase));

            MissingSiteCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    (
                        string.Equals(
                            r.SelectedAction,
                            "Import",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        string.Equals(
                            r.ImportStatus,
                            "Ready",
                            StringComparison.OrdinalIgnoreCase)
                    ) &&
                    string.IsNullOrWhiteSpace(
                        r.ParsedSite));

            WithWorkOrderCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.SelectedAction,
                        "Import",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(
                        r.WorkOrder));

            WithoutWorkOrderCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.SelectedAction,
                        "Import",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(
                        r.WorkOrder));

            /*
             * SAP still does not provide the SmartGridSuite Problem field.
             */
            MissingProblemCount = PreviewRows.Count(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.SelectedAction,
                        "Import",
                        StringComparison.OrdinalIgnoreCase));

            /*
             * Eventually the commit button will handle:
             *
             * - normal Ready rows
             * - Review Required rows selected for Import
             * - existing app tickets selected for Change Status
             *
             * For now we allow the button if there is at least one spreadsheet
             * row that currently intends to import.
             */
            var hasRowsToImport = PreviewRows.Any(
                r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.SelectedAction,
                        "Import",
                        StringComparison.OrdinalIgnoreCase));

            ImportBtn.IsEnabled =
                HasReconciliationActions();
        }

        private bool HasReconciliationActions()
        {
            var hasSpreadsheetImport =
                PreviewRows.Any(r =>
                    r.IsSpreadsheetRow &&
                    string.Equals(
                        r.SelectedAction,
                        "Import",
                        StringComparison.OrdinalIgnoreCase));

            var hasExistingTicketReview =
                PreviewRows.Any(r =>
                    r.IsExistingAppRow);

            return
                hasSpreadsheetImport ||
                hasExistingTicketReview;
        }

        private void PreviewRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is
                nameof(SapQueuePreviewDisplayRow.SelectedAction)
                or nameof(SapQueuePreviewDisplayRow.TargetStatus)
                or nameof(SapQueuePreviewDisplayRow.ImportStatus))
            {
                UpdateCounts();
            }
        }

        private void ConfigureReconciliationDefaults(SapQueuePreviewDisplayRow row)
        {
            row.ActionOptions.Clear();

            /*
             * ------------------------------------------------------------
             * EXISTING SMARTGRIDSUITE TICKET
             * ------------------------------------------------------------
             *
             * Safest default: do absolutely nothing.
             */
            if (row.IsExistingAppRow)
            {
                row.ActionOptions.Add("Keep As Is");
                row.ActionOptions.Add("Change Status");

                row.SelectedAction =
                    "Keep As Is";

                /*
                 * Keep the existing status visible in the target-status field,
                 * but it will not be editable until Change Status is selected.
                 */
                row.TargetStatus =
                    row.CurrentTicketStatus;

                return;
            }

            /*
             * ------------------------------------------------------------
             * SPREADSHEET ROW REQUIRING RECONCILIATION
             * ------------------------------------------------------------
             *
             * These are valid SAP rows, but the site is associated with more
             * than one notification. Do not silently treat them as normal Open
             * tickets.
             */
            if (string.Equals(
                    row.ImportStatus,
                    "Review Required",
                    StringComparison.OrdinalIgnoreCase))
            {
                row.ActionOptions.Add("Import");
                row.ActionOptions.Add("Skip");

                row.SelectedAction =
                    "Import";

                row.TargetStatus =
                    FindStatusOption("Needs Review")
                    ?? string.Empty;

                return;
            }

            /*
             * ------------------------------------------------------------
             * ORDINARY READY SPREADSHEET ROW
             * ------------------------------------------------------------
             */
            if (string.Equals(
                    row.ImportStatus,
                    "Ready",
                    StringComparison.OrdinalIgnoreCase))
            {
                row.ActionOptions.Add("Import");

                row.SelectedAction =
                    "Import";

                row.TargetStatus =
                    string.IsNullOrWhiteSpace(row.ParsedSite)
                        ? FindStatusOption("Needs Review") ?? string.Empty
                        : FindStatusOption("Open") ?? string.Empty;

                return;
            }

            /*
             * Already Exists / Invalid rows have no commit action.
             */
            row.SelectedAction =
                string.Empty;

            row.TargetStatus =
                string.Empty;
        }

        private string? FindStatusOption(string statusName)
        {
            return SapStatusOptions.FirstOrDefault(
                x => string.Equals(
                    x,
                    statusName,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsReadyRow(SapQueuePreviewDisplayRow row)
        {
            return string.Equals(row.ImportStatus, "Ready", StringComparison.OrdinalIgnoreCase);
        }

        private void SetBusy(bool busy, string message = "Working...")
        {
            BusyOverlay.Visibility = busy
                ? Visibility.Visible
                : Visibility.Collapsed;

            BusyText.Text = string.IsNullOrWhiteSpace(message)
                ? "Working..."
                : message;

            BrowseBtn.IsEnabled = !busy;
            LoadPreviewBtn.IsEnabled = !busy;
            ImportBtn.IsEnabled =
                !busy &&
                HasReconciliationActions();
            CloseBtn.IsEnabled = !busy;

            PreviewGrid.IsEnabled = !busy;

            Cursor = busy
                ? System.Windows.Input.Cursors.Wait
                : null;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static List<SapQueueImportPreviewRow> ReadSapRowsFromExcel(string filePath)
        {
            using var workbookStream = OpenWorkbookSnapshot(filePath);
            using var workbook = new XLWorkbook(workbookStream);

            var worksheet = workbook.Worksheets.First();

            var headerRow = worksheet.FirstRowUsed()
                           ?? throw new InvalidOperationException("No header row was found in the Excel file.");

            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var cell in headerRow.CellsUsed())
            {
                var normalizedHeader =
                    NormalizeSapHeader(ReadCellText(cell));

                if (!string.IsNullOrWhiteSpace(normalizedHeader) &&
                    !headerMap.ContainsKey(normalizedHeader))
                {
                    headerMap[normalizedHeader] =
                        cell.Address.ColumnNumber;
                }
            }

            int notifCol = GetRequiredColumn(
                headerMap,
                "Notification",
                "Notif",
                "Notification #",
                "Notification Number");

            int orderCol = GetRequiredColumn(
                headerMap,
                "Order",
                "Work Order",
                "WorkOrder",
                "WO",
                "WO #");

            int notifDateCol = GetRequiredColumn(
                headerMap,
                "Notif.date",
                "Notif. date",
                "Notif Date",
                "Notification Date",
                "NotificationDate");

            int descriptionCol = GetRequiredColumn(
                headerMap,
                "Description",
                "Short Text",
                "Notification Description");

            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
            var result = new List<SapQueueImportPreviewRow>();

            for (int rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
            {
                var row = worksheet.Row(rowNumber);

                var notification = ReadCellText(row.Cell(notifCol));
                var order = ReadCellText(row.Cell(orderCol));
                var description = ReadCellText(row.Cell(descriptionCol));
                var notifDate = ReadCellDate(row.Cell(notifDateCol));

                if (string.IsNullOrWhiteSpace(notification)
                    && string.IsNullOrWhiteSpace(order)
                    && string.IsNullOrWhiteSpace(description)
                    && notifDate is null)
                {
                    continue;
                }

                result.Add(new SapQueueImportPreviewRow(
                    RowNumber: rowNumber,
                    Notification: notification,
                    WorkOrder: string.IsNullOrWhiteSpace(order) ? null : order,
                    NotificationDate: notifDate,
                    Description: description));
            }

            return result;
        }

        private static MemoryStream OpenWorkbookSnapshot(string filePath)
        {
            try
            {
                using var source = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                var memory = new MemoryStream();
                source.CopyTo(memory);
                memory.Position = 0;

                return memory;
            }
            catch (IOException ex)
            {
                throw new IOException(
                    "The SAP export file is open or locked by another program. Close Excel, close File Explorer preview pane, or save a copy of the file and try again.",
                    ex);
            }
        }

        private static int GetRequiredColumn(Dictionary<string, int> headerMap, params string[] headers)
        {
            foreach (var header in headers)
            {
                var normalizedHeader =
                    NormalizeSapHeader(header);

                if (headerMap.TryGetValue(normalizedHeader, out var col))
                    return col;
            }

            throw new InvalidOperationException(
                $"Required SAP column '{headers.FirstOrDefault() ?? "Unknown"}' was not found.");
        }

        private static string NormalizeSapHeader(string? value)
        {
            var text = (value ?? string.Empty)
                .Trim()
                .Replace('\u00A0', ' ');

            return new string(
                text
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToLowerInvariant)
                    .ToArray());
        }

        private static string ReadCellText(IXLCell cell)
        {
            var formatted = cell.GetFormattedString()?.Trim();
            if (!string.IsNullOrWhiteSpace(formatted))
                return formatted;

            return (cell.GetString() ?? "").Trim();
        }

        private static DateTime? ReadCellDate(IXLCell cell)
        {
            if (cell.IsEmpty())
                return null;

            try
            {
                return cell.GetDateTime();
            }
            catch
            {
                // Ignore and try text/numeric fallbacks.
            }

            var text = ReadCellText(cell);

            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = text.Trim();

            var digitsOnly = new string(
                text
                    .Where(char.IsDigit)
                    .ToArray());

            if (digitsOnly.Length == 8 &&
                DateTime.TryParseExact(
                    digitsOnly,
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var sapCompactDate))
            {
                return sapCompactDate;
            }

            if (DateTime.TryParse(
                    text,
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return parsed;
            }

            if (double.TryParse(
                    text,
                    NumberStyles.Any,
                    CultureInfo.CurrentCulture,
                    out var oa))
            {
                try
                {
                    return DateTime.FromOADate(oa);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }

    public sealed class SapQueuePreviewDisplayRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name));
        }

        public int RowNumber { get; set; }

        public string Notification { get; set; } =
            string.Empty;

        public string WorkOrder { get; set; } =
            string.Empty;

        public DateTime? NotificationDate { get; set; }

        public string Description { get; set; } =
            string.Empty;

        public string ParsedSite { get; set; } =
            string.Empty;

        private string _importStatus =
            string.Empty;

        public string ImportStatus
        {
            get => _importStatus;

            set
            {
                if (_importStatus == value)
                    return;

                _importStatus =
                    value ?? string.Empty;

                OnPropertyChanged();
                OnPropertyChanged(nameof(WillBecome));
                OnPropertyChanged(nameof(SiteCheck));
                OnPropertyChanged(nameof(IsTargetStatusEditable));
            }
        }

        private string _message =
            string.Empty;

        public string Message
        {
            get => _message;

            set
            {
                if (_message == value)
                    return;

                _message =
                    value ?? string.Empty;

                OnPropertyChanged();
            }
        }

        public string RowSource { get; set; } =
            "Spreadsheet";

        public long? ExistingTicketId { get; set; }

        public string CurrentTicketStatus { get; set; } =
            string.Empty;

        public bool RequiresReview { get; set; }

        public string ReviewReason { get; set; } =
            string.Empty;

        public ObservableCollection<string> ActionOptions { get; } =
            new();

        private string _selectedAction =
            string.Empty;

        public string SelectedAction
        {
            get => _selectedAction;

            set
            {
                if (_selectedAction == value)
                    return;

                _selectedAction =
                    value ?? string.Empty;

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTargetStatusEditable));
                OnPropertyChanged(nameof(WillBecome));
            }
        }

        private string _targetStatus =
            string.Empty;

        public string TargetStatus
        {
            get => _targetStatus;

            set
            {
                if (_targetStatus == value)
                    return;

                _targetStatus =
                    value ?? string.Empty;

                OnPropertyChanged();
                OnPropertyChanged(nameof(WillBecome));
            }
        }

        public bool IsExistingAppRow =>
            string.Equals(
                RowSource,
                "Existing App",
                StringComparison.OrdinalIgnoreCase);

        public bool IsSpreadsheetRow =>
            !IsExistingAppRow;

        public bool IsTargetStatusEditable
        {
            get
            {
                if (IsExistingAppRow)
                {
                    return string.Equals(
                        SelectedAction,
                        "Change Status",
                        StringComparison.OrdinalIgnoreCase);
                }

                /*
                 * Normal Ready rows keep their normal destination.
                 * Only reconciliation rows get an editable status.
                 */
                return string.Equals(
                           ImportStatus,
                           "Review Required",
                           StringComparison.OrdinalIgnoreCase)
                       &&
                       string.Equals(
                           SelectedAction,
                           "Import",
                           StringComparison.OrdinalIgnoreCase);
            }
        }

        public string WillBecome
        {
            get
            {
                if (IsExistingAppRow)
                {
                    if (string.Equals(
                            SelectedAction,
                            "Keep As Is",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return "No Change";
                    }

                    if (string.Equals(
                            SelectedAction,
                            "Change Status",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return string.IsNullOrWhiteSpace(TargetStatus)
                            ? "Choose Status"
                            : TargetStatus;
                    }
                }

                if (string.Equals(
                        SelectedAction,
                        "Skip",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "Will Not Import";
                }

                if (string.Equals(
                        SelectedAction,
                        "Import",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(TargetStatus)
                        ? "Choose Status"
                        : TargetStatus;
                }

                if (string.Equals(
                        ImportStatus,
                        "Imported",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "Imported";
                }

                if (string.Equals(
                        ImportStatus,
                        "Already Exists",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "No Change";
                }

                if (string.Equals(
                        ImportStatus,
                        "Invalid",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "Will Not Import";
                }

                return string.Empty;
            }
        }

        public string SiteCheck
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ParsedSite))
                    return "Site Parsed";

                return IsSpreadsheetRow &&
                       (
                           string.Equals(
                               ImportStatus,
                               "Ready",
                               StringComparison.OrdinalIgnoreCase)
                           ||
                           string.Equals(
                               ImportStatus,
                               "Review Required",
                               StringComparison.OrdinalIgnoreCase)
                       )
                    ? "Missing Site"
                    : string.Empty;
            }
        }

        public string WorkOrderCheck =>
            string.IsNullOrWhiteSpace(WorkOrder)
                ? "No WO"
                : "Has WO";
    }
}