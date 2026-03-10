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

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs
{
    public partial class SapQueueImportWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly TicketsApi _ticketsApi;

        public ObservableCollection<SapQueuePreviewDisplayRow> PreviewRows { get; } = new();

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

        public SapQueueImportWindow(TicketsApi ticketsApi)
        {
            InitializeComponent();
            _ticketsApi = ticketsApi;
            DataContext = this;

            CreatedByDisplay = TryGetDisplayName()
                               ?? (WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName);
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

            SetBusy(true);

            try
            {
                await Task.Yield();
                var rows = await Task.Run(() => ReadSapRowsFromExcel(SelectedFilePath));

                var preview = await _ticketsApi.PreviewSapQueueImportAsync(
                    new SapQueueImportPreviewRequest(rows));

                PreviewRows.Clear();
                foreach (var row in preview.OrderBy(r => r.RowNumber))
                {
                    PreviewRows.Add(new SapQueuePreviewDisplayRow
                    {
                        RowNumber = row.RowNumber,
                        Notification = row.Notification,
                        WorkOrder = row.WorkOrder ?? "",
                        NotificationDate = row.NotificationDate,
                        Description = row.Description,
                        ParsedSite = row.ParsedSite,
                        ImportStatus = row.ImportStatus,
                        Message = row.Message
                    });
                }

                UpdateCounts();
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
            var readyRows = PreviewRows
                .Where(r => string.Equals(r.ImportStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                         && r.NotificationDate.HasValue)
                .Select(r => new SapQueueImportCommitRow(
                    RowNumber: r.RowNumber,
                    Notification: r.Notification,
                    WorkOrder: string.IsNullOrWhiteSpace(r.WorkOrder) ? null : r.WorkOrder,
                    NotificationDate: r.NotificationDate!.Value,
                    Description: r.Description,
                    ParsedSite: r.ParsedSite ?? ""))
                .ToList();

            if (readyRows.Count == 0)
            {
                MessageBox.Show("There are no Ready rows to import.", "Import SAP Queue",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Import {readyRows.Count} ready rows?",
                "Confirm SAP Import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            SetBusy(true, "Importing ready rows...");
            await Task.Yield();

            try
            {
                var result = await _ticketsApi.CommitSapQueueImportAsync(
                    new SapQueueImportCommitRequest(
                        CreatedBy: CreatedByDisplay,
                        Rows: readyRows));

                ApplyCommitResults(result);
                UpdateCounts();

                MessageBox.Show(
                    $"Imported: {result.ImportedCount}\nAlready Exists: {result.AlreadyExistsCount}\nInvalid: {result.InvalidCount}",
                    "SAP Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                if (result.ImportedCount > 0)
                {
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to import SAP queue.\n\n{ex.Message}",
                    "Import SAP Queue",
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
            TotalRows = PreviewRows.Count;
            ReadyCount = PreviewRows.Count(r => string.Equals(r.ImportStatus, "Ready", StringComparison.OrdinalIgnoreCase));
            AlreadyExistsCount = PreviewRows.Count(r => string.Equals(r.ImportStatus, "Already Exists", StringComparison.OrdinalIgnoreCase));
            InvalidCount = PreviewRows.Count(r => string.Equals(r.ImportStatus, "Invalid", StringComparison.OrdinalIgnoreCase));

            ImportBtn.IsEnabled = ReadyCount > 0;
        }

        private void SetBusy(bool busy, string message = "Working...")
        {
            BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            BusyText.Text = message;

            BrowseBtn.IsEnabled = !busy;
            LoadPreviewBtn.IsEnabled = !busy;
            ImportBtn.IsEnabled = !busy && ReadyCount > 0;
            CloseBtn.IsEnabled = !busy;

            Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static List<SapQueueImportPreviewRow> ReadSapRowsFromExcel(string filePath)
        {
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheets.First();

            var headerRow = worksheet.FirstRowUsed()
                           ?? throw new InvalidOperationException("No header row was found in the Excel file.");

            var headerMap = headerRow.CellsUsed()
                .ToDictionary(
                    c => (c.GetString() ?? "").Trim(),
                    c => c.Address.ColumnNumber,
                    StringComparer.OrdinalIgnoreCase);

            int notifCol = GetRequiredColumn(headerMap, "Notification");
            int orderCol = GetRequiredColumn(headerMap, "Order");
            int notifDateCol = GetRequiredColumn(headerMap, "Notif.date");
            int descriptionCol = GetRequiredColumn(headerMap, "Description");

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

        private static int GetRequiredColumn(Dictionary<string, int> headerMap, string header)
        {
            if (headerMap.TryGetValue(header, out var col))
                return col;

            throw new InvalidOperationException($"Required SAP column '{header}' was not found.");
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
                // ignore and try fallbacks
            }

            var text = ReadCellText(cell);

            if (DateTime.TryParse(text, out var parsed))
                return parsed;

            if (double.TryParse(text, out var oa))
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

    public sealed class SapQueuePreviewDisplayRow
    {
        public int RowNumber { get; set; }
        public string Notification { get; set; } = "";
        public string WorkOrder { get; set; } = "";
        public DateTime? NotificationDate { get; set; }
        public string Description { get; set; } = "";
        public string ParsedSite { get; set; } = "";
        public string ImportStatus { get; set; } = "";
        public string Message { get; set; } = "";
    }
}