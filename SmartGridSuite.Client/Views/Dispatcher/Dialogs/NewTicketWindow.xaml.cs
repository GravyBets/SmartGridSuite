using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Tickets;
using System.Security.Principal;
using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs;

public partial class NewTicketWindow : Window
{
    private readonly TicketsApi _ticketsApi;
    private readonly TicketAdminApi _ticketAdminApi;
    private readonly List<string> _techSuggestions;

    private const double DefaultWindowHeight = 772;

    private const double DefaultTechnicianWriteUpHeight = 180;

    public ObservableCollection<NewTicketAssignedTechOption> AssignedTechOptions { get; } = new();

    private bool _syncingAssignedTechSelection;

    private readonly long? _editingTicketId;

    // Preserve legacy task/action values until the Tasks pane data model is simplified.
    // These values are no longer edited from New/Edit Ticket.
    private readonly ulong? _preservedTaskCategoryId;
    private readonly string? _preservedActionRequiredOverride;

    // Technician notes/write-ups are visible in Edit mode for reference only.
    // They are preserved exactly when dispatcher-controlled ticket fields are saved.
    private readonly string _preservedNotes = "";

    private bool _hasLoadedLookups;

    private TicketDraftState? _originalDraftState;

    private bool _suppressDirtyStateRefresh;

    private bool _isBusy;

    public long? CreatedTicketId { get; private set; }

    public bool WasDeleted { get; private set; }

    private bool IsEditMode => _editingTicketId.HasValue;

    private NewTicketDraft Draft => (NewTicketDraft)DataContext;

    public NewTicketWindow(
        TicketsApi ticketsApi,
        IEnumerable<string>? techNames = null,
        DispatchTicket? existingTicket = null,
        TicketWriteUpHistoryResponse? writeUpHistory = null)
    {
        InitializeComponent();

        _ticketsApi = ticketsApi;
        _ticketAdminApi = new TicketAdminApi(ClientAppSettings.CreateApiClient());

        _techSuggestions = new List<string> { "(Unassigned)" };

        _techSuggestions.AddRange(
            (techNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(x => !x.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x));

        if (existingTicket != null)
        {
            foreach (var assignedName in ParseAssignedTechDisplayNames(existingTicket.AssignedTech))
            {
                if (!_techSuggestions.Contains(
                        assignedName,
                        StringComparer.OrdinalIgnoreCase))
                {
                    _techSuggestions.Add(assignedName);
                }
            }

            var orderedSuggestions = _techSuggestions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x)
                .ToList();

            _techSuggestions.Clear();
            _techSuggestions.AddRange(orderedSuggestions);
        }

        BuildAssignedTechOptions();

        WorkOrderTypeBox.ItemsSource = new[]
        {
            "",
            "Maintenance",
            "Capital",
            "Distribution"
        };

        PriorityBox.ItemsSource = new[]
        {
            "",
            "1 Day",
            "3 Days",
            "5 Days",
            "15 Days"
        };

        var createdBy =
            TryGetDisplayName() ??
            (WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName);

        DataContext = new NewTicketDraft
        {
            CreatedBy = createdBy,
            AssignedTo = "(Unassigned)",
            PriorityDays = "",
            Status = ""
        };

        _editingTicketId = existingTicket?.Id;
        _preservedTaskCategoryId = existingTicket?.TaskCategoryId;
        _preservedActionRequiredOverride =
            string.IsNullOrWhiteSpace(existingTicket?.ActionRequiredOverride)
                ? null
                : existingTicket.ActionRequiredOverride.Trim();
        _preservedNotes = existingTicket == null
            ? ""
            : writeUpHistory?.HasSubmissionHistory == true
                ? writeUpHistory.Text
                : existingTicket.Notes ?? "";

        if (existingTicket != null)
        {
            Title = "Edit Ticket";
            CreateBtn.Content = "Save Changes";

            DeleteTicketButton.Visibility =
                Visibility.Visible;

            PopulateDraftFromExistingTicket(existingTicket, createdBy);

            TechnicianNotesPanel.Visibility =
                string.IsNullOrWhiteSpace(_preservedNotes)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }
        else
        {
            Title = "New Ticket";
            CreateBtn.Content = "Create Ticket";

            TechnicianNotesPanel.Visibility = Visibility.Collapsed;
        }
        Draft.PropertyChanged += Draft_PropertyChanged;

        _originalDraftState = CaptureCurrentDraftState();

        RefreshSaveButtonState();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (TechnicianNotesTextBox is null)
            return;

        if (TechnicianNotesPanel is null ||
            TechnicianNotesPanel.Visibility !=
                Visibility.Visible)
        {
            return;
        }

        /*
         * Give all additional vertical window space to the
         * Technician Write-Up box.
         *
         * At the normal 772px window height it remains 180px.
         * Expanding the window makes the write-up area grow
         * by the same amount.
         */
        var extraHeight =
            Math.Max(
                0,
                ActualHeight -
                DefaultWindowHeight);

        TechnicianNotesTextBox.Height =
            DefaultTechnicianWriteUpHeight +
            extraHeight;
    }

    private void Draft_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDirtyStateRefresh)
            return;

        RefreshSaveButtonState();
    }

    private TicketDraftState CaptureCurrentDraftState()
    {
        var workOrder =
            NormalizeDraftText(
                Draft.WorkOrder);

        /*
         * This mirrors Create_Click.
         *
         * Work Order Type and Code are not actually persisted
         * when there is no Work Order, so changing those alone
         * should not make Save appear enabled.
         */
        var workOrderType =
            string.IsNullOrWhiteSpace(workOrder)
                ? ""
                : NormalizeDraftText(
                    Draft.WorkOrderType);

        var workOrderCode =
            string.IsNullOrWhiteSpace(workOrder)
                ? ""
                : NormalizeDraftText(
                    Draft.WorkOrderCode);

        return new TicketDraftState(
            Site:
                NormalizeDraftText(
                    Draft.Site),

            Problem:
                NormalizeDraftText(
                    Draft.Problem),

            AssignedTo:
                NormalizeAssignedTechForComparison(
                    Draft.AssignedTo),

            Status:
                NormalizeDraftText(
                    Draft.Status),

            NotificationName:
                NormalizeDraftText(
                    Draft.NotificationName),

            NotificationNumber:
                NormalizeDraftText(
                    Draft.NotificationNumber),

            WorkOrder:
                workOrder,

            WorkOrderType:
                workOrderType,

            WorkOrderCode:
                workOrderCode,

            PriorityDays:
                NormalizeDraftText(
                    Draft.PriorityDays),

            DispatchNotes:
                NormalizeDraftText(
                    Draft.DispatchNotes),

            Notes:
                NormalizeDraftText(
                    Draft.Notes));
    }

    private static string NormalizeDraftText(
        string? value)
    {
        return (value ?? string.Empty)
            .Trim();
    }

    private static string NormalizeAssignedTechForComparison(
        string? value)
    {
        var clean =
            NormalizeDraftText(value);

        if (string.IsNullOrWhiteSpace(clean) ||
            clean.Equals(
                "(Unassigned)",
                StringComparison.OrdinalIgnoreCase))
        {
            return "(Unassigned)";
        }

        var names =
            ParseAssignedTechDisplayNames(
                clean);

        if (names.Count == 0)
            return clean;

        return FormatAssignedTechDisplayText(
            names);
    }

    private bool HasUnsavedTicketChanges()
    {
        if (_originalDraftState is null)
            return false;

        var current =
            CaptureCurrentDraftState();

        return current !=
               _originalDraftState;
    }

    private void RefreshSaveButtonState()
    {
        if (CreateBtn is null)
            return;

        CreateBtn.IsEnabled =
            !_isBusy &&
            HasUnsavedTicketChanges();
    }

    private void PopulateDraftFromExistingTicket(DispatchTicket ticket, string fallbackCreatedBy)
    {
        Draft.Site = ticket.Site ?? "";
        Draft.FullSiteName = "";
        Draft.Problem = ticket.Problem ?? "";

        Draft.AssignedTo =
            string.IsNullOrWhiteSpace(ticket.AssignedTech) ||
            ticket.AssignedTech.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase)
                ? "(Unassigned)"
                : ticket.AssignedTech;

        Draft.Status = ticket.Status ?? "";
        Draft.NotificationName = ticket.NotificationName ?? "";
        Draft.NotificationNumber = ticket.Notification ?? "";
        Draft.WorkOrder = ticket.CurrentWorkOrder ?? "";
        Draft.WorkOrderType = ticket.WorkOrderType ?? "";
        Draft.WorkOrderCode = ticket.GroupCode ?? "";

        Draft.PriorityDays = ticket.PriorityDays switch
        {
            1 => "1 Day",
            3 => "3 Days",
            5 => "5 Days",
            15 => "15 Days",
            _ => ""
        };

        Draft.DispatchNotes = ticket.DispatchNotes ?? "";

        // Visible only in Edit mode when technician notes already exist.
        // This value is never edited/saved from the UI.
        Draft.Notes = _preservedNotes;

        Draft.CreatedBy = string.IsNullOrWhiteSpace(ticket.CreatedBy)
            ? fallbackCreatedBy
            : ticket.CreatedBy;
    }

    private static string? TryGetDisplayName()
    {
        try
        {
            var fullName = Environment.GetEnvironmentVariable("FULLNAME");

            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName.Trim();
        }
        catch
        {
            // Fall back to Windows identity below.
        }

        return null;
    }

    private async void Window_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_hasLoadedLookups)
            return;

        _hasLoadedLookups = true;

        _suppressDirtyStateRefresh =
            true;

        try
        {
            await LoadStatusesAsync();

            ApplyAssignedTechSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load ticket setup data.\n\n{ex.Message}",
                IsEditMode
                    ? "Edit Ticket"
                    : "New Ticket",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            /*
             * Status may have been initialized automatically
             * from the server lookup. That is setup, not a
             * user edit.
             */
            if (_originalDraftState is not null)
            {
                _originalDraftState =
                    _originalDraftState with
                    {
                        Status =
                            NormalizeDraftText(
                                Draft.Status)
                    };
            }

            _suppressDirtyStateRefresh =
                false;

            RefreshSaveButtonState();
        }
    }

    private async Task LoadStatusesAsync(CancellationToken ct = default)
    {
        var statuses = await _ticketAdminApi.GetStatusesAsync(ct: ct);

        var availableStatuses = statuses
            .Where(x =>
                x.IsActive ||
                (IsEditMode &&
                 string.Equals(x.Name, Draft.Status, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => x.Name)
            .ToList();

        StatusBox.ItemsSource = availableStatuses;

        if (availableStatuses.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(Draft.Status) ||
            !availableStatuses.Contains(Draft.Status, StringComparer.OrdinalIgnoreCase))
        {
            Draft.Status =
                availableStatuses.FirstOrDefault(x =>
                    x.Equals("Open", StringComparison.OrdinalIgnoreCase))
                ?? availableStatuses.First();
        }
    }

    private void AssignedTechDropDownButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        /*
         * Clicking the dropdown button again while the picker
         * is open behaves like Cancel.
         */
        if (AssignedTechDropDownPopup.IsOpen)
        {
            ApplyAssignedTechSelection();

            AssignedTechDropDownPopup.IsOpen =
                false;

            return;
        }

        /*
         * Start every picker session from the value that is
         * currently committed to the ticket draft.
         */
        ApplyAssignedTechSelection();

        AssignedTechDropDownPopup.IsOpen =
            true;
    }

    private void ApplyAssignedTechSelection()
    {
        var assigned =
            string.IsNullOrWhiteSpace(Draft.AssignedTo)
                ? "(Unassigned)"
                : Draft.AssignedTo.Trim();

        var assignedNames =
            ParseAssignedTechDisplayNames(assigned);

        _syncingAssignedTechSelection = true;

        try
        {
            foreach (var option in AssignedTechOptions)
                option.IsSelected = false;

            if (assigned.Equals(
                    "(Unassigned)",
                    StringComparison.OrdinalIgnoreCase) ||
                assignedNames.Count == 0)
            {
                AssignedToUnassignedCheckBox.IsChecked = true;
                Draft.AssignedTo = "(Unassigned)";
                return;
            }

            AssignedToUnassignedCheckBox.IsChecked = false;

            foreach (var assignedName in assignedNames)
                EnsureAssignedTechOptionExists(assignedName);

            foreach (var option in AssignedTechOptions)
            {
                option.IsSelected =
                    assignedNames.Contains(
                        option.Name,
                        StringComparer.OrdinalIgnoreCase);
            }

            Draft.AssignedTo = BuildAssignedTechDisplayText();
        }
        finally
        {
            _syncingAssignedTechSelection =
                false;

            UpdateAssignedTechSummary();
            UpdateAssignedTechBulkButtonText();
        }
    }

    private void BuildAssignedTechOptions()
    {
        AssignedTechOptions.Clear();

        foreach (var techName in _techSuggestions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => !x.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x))
        {
            AssignedTechOptions.Add(
                new NewTicketAssignedTechOption(techName));
        }
    }

    private void EnsureAssignedTechOptionExists(string techName)
    {
        var cleanName = (techName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(cleanName) ||
            cleanName.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (AssignedTechOptions.Any(x =>
                x.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var names = AssignedTechOptions
            .Select(x => x.Name)
            .Concat(new[] { cleanName })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        AssignedTechOptions.Clear();

        foreach (var name in names)
        {
            AssignedTechOptions.Add(
                new NewTicketAssignedTechOption(name));
        }
    }

    private void AssignedToUnassignedCheckBox_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (_syncingAssignedTechSelection)
            return;

        _syncingAssignedTechSelection = true;

        try
        {
            foreach (var option in AssignedTechOptions)
            {
                option.IsSelected =
                    false;
            }
        }
        finally
        {
            _syncingAssignedTechSelection =
                false;

            UpdateAssignedTechSummary();
            UpdateAssignedTechBulkButtonText();
        }
    }

    private void AssignedToUnassignedCheckBox_Unchecked(
        object sender,
        RoutedEventArgs e)
    {
        if (_syncingAssignedTechSelection)
            return;

        UpdateAssignedTechSummary();
        UpdateAssignedTechBulkButtonText();
    }

    private void AssignedTechOption_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_syncingAssignedTechSelection)
            return;

        if (AssignedTechOptions.Any(
                x => x.IsSelected))
        {
            AssignedToUnassignedCheckBox.IsChecked =
                false;
        }

        UpdateAssignedTechSummary();
        UpdateAssignedTechBulkButtonText();
    }

    private void ToggleAllAssignedTechs_Click(
        object sender,
        RoutedEventArgs e)
    {
        var allSelected =
            AssignedTechOptions.Count > 0 &&
            AssignedTechOptions.All(
                x => x.IsSelected);

        _syncingAssignedTechSelection =
            true;

        try
        {
            if (allSelected)
            {
                /*
                 * Clear All returns the picker to Unassigned.
                 */
                foreach (var option in AssignedTechOptions)
                {
                    option.IsSelected =
                        false;
                }

                AssignedToUnassignedCheckBox.IsChecked =
                    true;
            }
            else
            {
                AssignedToUnassignedCheckBox.IsChecked =
                    false;

                foreach (var option in AssignedTechOptions)
                {
                    option.IsSelected =
                        true;
                }
            }
        }
        finally
        {
            _syncingAssignedTechSelection =
                false;

            UpdateAssignedTechSummary();
            UpdateAssignedTechBulkButtonText();
        }
    }

    private void ConfirmAssignedTechSelection_Click(
        object sender,
        RoutedEventArgs e)
    {
        var assigned =
            BuildAssignedTechDisplayText();

        /*
         * Do not allow an ambiguous completely blank assignment.
         * No selected technicians means Unassigned.
         */
        if (string.IsNullOrWhiteSpace(assigned))
        {
            assigned =
                "(Unassigned)";
        }

        Draft.AssignedTo =
            assigned;

        /*
         * Re-apply the committed value so the controls and
         * summary are guaranteed to match the ticket draft.
         */
        ApplyAssignedTechSelection();

        AssignedTechDropDownPopup.IsOpen =
            false;
    }

    private void CancelAssignedTechSelection_Click(
        object sender,
        RoutedEventArgs e)
    {
        /*
         * Draft.AssignedTo was never changed while the picker
         * was open, so simply reload it and discard the staged
         * checkbox changes.
         */
        ApplyAssignedTechSelection();

        AssignedTechDropDownPopup.IsOpen =
            false;
    }

    private void UpdateAssignedTechBulkButtonText()
    {
        if (SelectAllAssignedTechsButton is null)
            return;

        var allSelected =
            AssignedTechOptions.Count > 0 &&
            AssignedTechOptions.All(
                x => x.IsSelected);

        SelectAllAssignedTechsButton.Content =
            allSelected
                ? "Clear All"
                : "Select All";
    }

    private string BuildAssignedTechDisplayText()
    {
        if (AssignedToUnassignedCheckBox?.IsChecked == true)
            return "(Unassigned)";

        var selectedNames = AssignedTechOptions
            .Where(x => x.IsSelected)
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        return FormatAssignedTechDisplayText(selectedNames);
    }

    private void UpdateAssignedTechSummary()
    {
        var assignedTech =
            BuildAssignedTechDisplayText();

        if (string.IsNullOrWhiteSpace(assignedTech))
        {
            AssignedTechDropDownTextBlock.Text =
                "Choose technician(s)...";

            AssignedTechSummaryTextBlock.Text =
                "No technician selected.";

            return;
        }

        AssignedTechDropDownTextBlock.Text =
            assignedTech;

        AssignedTechSummaryTextBlock.Text =
            $"Selected: {assignedTech}";
    }

    private static List<string> ParseAssignedTechDisplayNames(string? assignedTech)
    {
        var value =
            (assignedTech ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("(Unassigned)", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Truck ", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>();
        }

        return Regex
            .Split(value, @"\s*(?:,|&)\s*")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static string FormatAssignedTechDisplayText(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return "";

        if (names.Count == 1)
            return names[0];

        if (names.Count == 2)
            return $"{names[0]} & {names[1]}";

        return string.Join(
                   ", ",
                   names.Take(names.Count - 1)) +
               " & " +
               names.Last();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void DeleteTicket_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode ||
            !_editingTicketId.HasValue)
        {
            return;
        }

        var confirmationWindow =
            new ConfirmDeleteTicketWindow(
                Draft.Site,
                Draft.NotificationNumber,
                Draft.Problem)
            {
                Owner = this
            };

        if (confirmationWindow.ShowDialog() != true)
            return;

        SetBusy(true);

        try
        {
            await _ticketsApi.DeleteTicketAsync(
                _editingTicketId.Value,
                string.IsNullOrWhiteSpace(Draft.CreatedBy)
                    ? "Unknown"
                    : Draft.CreatedBy.Trim());

            WasDeleted = true;
            CreatedTicketId = null;

            DialogResult = true;
            Close();
        }
        catch (ApiClient.ApiException ex)
        {
            MessageBox.Show(
                ex.Body ?? ex.Message,
                "Delete Ticket",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Delete Ticket",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (IsVisible)
                SetBusy(false);
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var site = (Draft.Site ?? "").Trim();

        if (string.IsNullOrWhiteSpace(site))
        {
            MessageBox.Show(
                "A Site Number is required before this ticket can be saved.\n\n" +
                "You may enter the Problem / Issue first, but Smart Grid Suite " +
                "cannot create or update the ticket until it is tied to a site.\n\n" +
                "Enter the Site Number and click Save again.\n\n" +
                "For TOP sites, enter only the TOP site, such as XX-MWB. " +
                "Do not include the sector. If no Site, enter 'No Site'",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            SiteTextBox.Focus();
            SiteTextBox.SelectAll();
            return;
        }

        var problem = (Draft.Problem ?? "").Trim();
        var notificationName = (Draft.NotificationName ?? "").Trim();
        var notification = (Draft.NotificationNumber ?? "").Trim();
        var workOrderText = (Draft.WorkOrder ?? "").Trim();
        var dispatchNotes = (Draft.DispatchNotes ?? "").Trim();
        var technicianNotes = (Draft.Notes ?? "").Trim();

        if (!ValidateTicketTextLengths(
                site,
                notificationName,
                notification,
                workOrderText,
                problem,
                dispatchNotes))
        {
            return;
        }

        var status = (Draft.Status ?? "").Trim();

        if (string.IsNullOrWhiteSpace(status))
        {
            MessageBox.Show(
                "Status is required.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        string? workOrder = string.IsNullOrWhiteSpace(workOrderText)
            ? null
            : workOrderText;

        var priority = ParsePriorityDays(Draft.PriorityDays);

        if (priority < 0)
        {
            MessageBox.Show(
                "Priority must be blank or one of: 1 Day, 3 Days, 5 Days, or 15 Days.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var assignedTech = BuildAssignedTechDisplayText();

        if (string.IsNullOrWhiteSpace(assignedTech))
        {
            MessageBox.Show(
                "Choose one or more technicians, or choose (Unassigned).",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        Draft.AssignedTo = assignedTech;

        var workOrderType = (Draft.WorkOrderType ?? "").Trim();
        var workOrderCode = (Draft.WorkOrderCode ?? "").Trim();

        if (workOrder == null)
        {
            workOrderType = "";
            workOrderCode = "";
        }

        SetBusy(true);

        try
        {
            if (IsEditMode)
            {
                var updateRequest = new UpdateTicketRequest(
                    Site: site,
                    NotificationName: notificationName,
                    Notification: notification,
                    WorkOrder: workOrder,
                    WorkOrderClass: workOrderType,
                    GroupCode: workOrderCode,
                    PriorityDays: priority,
                    Status: status,
                    TaskCategoryId: _preservedTaskCategoryId,
                    ActionRequiredOverride: _preservedActionRequiredOverride,
                    AssignedTech: assignedTech,
                    Problem: problem,

                    Notes: technicianNotes,

                    DispatchNotes: dispatchNotes
                );

                CreatedTicketId = await _ticketsApi.UpdateTicketAsync(
                    _editingTicketId!.Value,
                    updateRequest);
            }
            else
            {
                var createdBy = (Draft.CreatedBy ?? "").Trim();

                if (string.IsNullOrWhiteSpace(createdBy))
                    createdBy = WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;

                var createRequest = new CreateTicketRequest(
                    Site: site,
                    NotificationName: notificationName,
                    Notification: notification,
                    WorkOrder: workOrder,
                    WorkOrderClass: workOrderType,
                    GroupCode: workOrderCode,
                    PriorityDays: priority,
                    Status: status,
                    TaskCategoryId: null,
                    ActionRequiredOverride: null,
                    AssignedTech: assignedTech,
                    Problem: problem,

                    // New tickets cannot create technician write-ups.
                    Notes: "",

                    DispatchNotes: dispatchNotes,
                    CreatedBy: createdBy
                );

                CreatedTicketId = await _ticketsApi.CreateTicketAsync(createRequest);
            }

            DialogResult = true;
            Close();
        }
        catch (ApiClient.ApiException ex) when (ex.StatusCode == 409)
        {
            MessageBox.Show(
                ex.Body ?? "A ticket already exists with that Notification #.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (ApiClient.ApiException ex) when (ex.StatusCode == 400)
        {
            MessageBox.Show(
                ex.Body ?? "Request was invalid.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                $"{Title} Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static int ParsePriorityDays(string? priorityValue)
    {
        return (priorityValue ?? "").Trim() switch
        {
            "" => 0,
            "1 Day" => 1,
            "3 Days" => 3,
            "5 Days" => 5,
            "15 Days" => 15,
            _ => -1
        };
    }

    private bool ValidateTicketTextLengths(
    string site,
    string notificationName,
    string notification,
    string workOrder,
    string problem,
    string dispatchNotes)
    {
        if (!ValidateTextLength("Site", site, TicketTextLimits.Site, SiteTextBox))
            return false;

        if (!ValidateTextLength("Notification Name", notificationName, TicketTextLimits.NotificationName, NotificationNameTextBox))
            return false;

        if (!ValidateTextLength("Notification #", notification, TicketTextLimits.Notification, NotificationNumberTextBox))
            return false;

        if (!ValidateTextLength("Work Order", workOrder, TicketTextLimits.WorkOrder, WorkOrderTextBox))
            return false;

        if (!ValidateTextLength("Problem", problem, TicketTextLimits.Problem, ProblemTextBox))
            return false;

        if (!ValidateTextLength("Dispatch Notes", dispatchNotes, TicketTextLimits.DispatchNotes, DispatchNotesTextBox))
            return false;

        return true;
    }

    private bool ValidateTextLength(string fieldName, string? value, int maxLength, Control? controlToFocus)
    {
        var length = (value ?? string.Empty).Length;

        if (length <= maxLength)
            return true;

        MessageBox.Show(
            $"{fieldName} is too long.\n\n" +
            $"Maximum allowed: {maxLength} characters\n" +
            $"Current length: {length} characters",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        controlToFocus?.Focus();

        if (controlToFocus is TextBox textBox)
        {
            textBox.SelectAll();
        }

        return false;
    }

    private sealed record TicketDraftState(
        string Site,
        string Problem,
        string AssignedTo,
        string Status,
        string NotificationName,
        string NotificationNumber,
        string WorkOrder,
        string WorkOrderType,
        string WorkOrderCode,
        string PriorityDays,
        string DispatchNotes,
        string Notes);

    private void SetBusy(bool busy)
    {
        _isBusy =
            busy;

        RefreshSaveButtonState();

        CancelBtn.IsEnabled =
            !busy;

        DeleteTicketButton.IsEnabled =
            !busy &&
            IsEditMode;
    }
}

public sealed class NewTicketAssignedTechOption : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public NewTicketAssignedTechOption(string name)
    {
        Name = name;
    }
}