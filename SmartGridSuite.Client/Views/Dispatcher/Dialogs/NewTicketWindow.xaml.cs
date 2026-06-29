using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Tickets;
using System.Security.Principal;
using System.Windows;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs;

public partial class NewTicketWindow : Window
{
    private readonly TicketsApi _ticketsApi;
    private readonly TicketAdminApi _ticketAdminApi;
    private readonly List<string> _techSuggestions;

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

    public long? CreatedTicketId { get; private set; }

    private bool IsEditMode => _editingTicketId.HasValue;

    private NewTicketDraft Draft => (NewTicketDraft)DataContext;

    public NewTicketWindow(TicketsApi ticketsApi, IEnumerable<string>? techNames = null, DispatchTicket? existingTicket = null)
    {
        InitializeComponent();

        _ticketsApi = ticketsApi;
        _ticketAdminApi = new TicketAdminApi(new ApiClient("https://localhost:7140"));

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
        _preservedNotes = existingTicket?.Notes ?? "";

        if (existingTicket != null)
        {
            Title = "Edit Ticket";
            CreateBtn.Content = "Save Changes";

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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoadedLookups)
            return;

        _hasLoadedLookups = true;

        try
        {
            await LoadStatusesAsync();
            ApplyAssignedTechSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to load ticket setup data.\n\n{ex.Message}",
                IsEditMode ? "Edit Ticket" : "New Ticket",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

    private void AssignedTechDropDownButton_Click(object sender, RoutedEventArgs e)
    {
        AssignedTechDropDownPopup.IsOpen =
            !AssignedTechDropDownPopup.IsOpen;
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
            _syncingAssignedTechSelection = false;
            UpdateAssignedTechSummary();
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

    private void AssignedToUnassignedCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingAssignedTechSelection)
            return;

        _syncingAssignedTechSelection = true;

        try
        {
            foreach (var option in AssignedTechOptions)
                option.IsSelected = false;

            Draft.AssignedTo = "(Unassigned)";
        }
        finally
        {
            _syncingAssignedTechSelection = false;
            UpdateAssignedTechSummary();
        }
    }

    private void AssignedToUnassignedCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_syncingAssignedTechSelection)
            return;

        UpdateAssignedTechSummary();
    }

    private void AssignedTechOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingAssignedTechSelection)
            return;

        if (AssignedTechOptions.Any(x => x.IsSelected))
            AssignedToUnassignedCheckBox.IsChecked = false;

        Draft.AssignedTo = BuildAssignedTechDisplayText();

        UpdateAssignedTechSummary();
    }

    private void SelectAllAssignedTechs_Click(object sender, RoutedEventArgs e)
    {
        _syncingAssignedTechSelection = true;

        try
        {
            AssignedToUnassignedCheckBox.IsChecked = false;

            foreach (var option in AssignedTechOptions)
                option.IsSelected = true;

            Draft.AssignedTo = BuildAssignedTechDisplayText();
        }
        finally
        {
            _syncingAssignedTechSelection = false;
            UpdateAssignedTechSummary();
        }
    }

    private void ClearAssignedTechs_Click(object sender, RoutedEventArgs e)
    {
        _syncingAssignedTechSelection = true;

        try
        {
            foreach (var option in AssignedTechOptions)
                option.IsSelected = false;

            AssignedToUnassignedCheckBox.IsChecked = true;
            Draft.AssignedTo = "(Unassigned)";
        }
        finally
        {
            _syncingAssignedTechSelection = false;
            UpdateAssignedTechSummary();
        }
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

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var site = (Draft.Site ?? "").Trim();

        if (string.IsNullOrWhiteSpace(site))
        {
            MessageBox.Show(
                "Site is required.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var problem = (Draft.Problem ?? "").Trim();

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

        var notification = (Draft.NotificationNumber ?? "").Trim();

        var workOrderText = (Draft.WorkOrder ?? "").Trim();

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
                    NotificationName: (Draft.NotificationName ?? "").Trim(),
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

                    // Technician write-ups are shown read-only and preserved exactly.
                    Notes: _preservedNotes,

                    DispatchNotes: (Draft.DispatchNotes ?? "").Trim()
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
                    NotificationName: (Draft.NotificationName ?? "").Trim(),
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

                    DispatchNotes: (Draft.DispatchNotes ?? "").Trim(),
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

    private void SetBusy(bool busy)
    {
        CreateBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = !busy;
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