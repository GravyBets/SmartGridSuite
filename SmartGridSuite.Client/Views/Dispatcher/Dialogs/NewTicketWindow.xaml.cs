using SmartGridSuite.Client.Models.Dispatcher;
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Administration.Ticket.Status;
using SmartGridSuite.Contracts.Tickets;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows;



namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs;

public partial class NewTicketWindow : Window
{
    private readonly TicketsApi _ticketsApi;
    private readonly TicketAdminApi _ticketAdminApi;
    private readonly List<string> _techSuggestions;
    private readonly List<TicketTaskCategoryDto> _taskCategories = new();

    private readonly long? _editingTicketId;
    private readonly ulong? _initialTaskCategoryId;
    private readonly string _initialTaskCategoryName = "";
    private readonly string _initialActionRequiredOverride = "";

    private bool _hasLoadedLookups;

    public long? CreatedTicketId { get; private set; }

    private bool IsEditMode => _editingTicketId.HasValue;

    private NewTicketDraft Draft => (NewTicketDraft)DataContext;

    public NewTicketWindow(TicketsApi ticketsApi, IEnumerable<string>? techNames = null, DispatchTicket? existingTicket = null)
    {
        InitializeComponent();

        _ticketsApi = ticketsApi;
        _ticketAdminApi = new TicketAdminApi(new ApiClient("https://localhost:7140"));

        _techSuggestions = (techNames ?? Enumerable.Empty<string>())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x)
                        .ToList();

        if (existingTicket != null &&
            !string.IsNullOrWhiteSpace(existingTicket.AssignedTech) &&
            !string.Equals(existingTicket.AssignedTech, "(Unassigned)", StringComparison.OrdinalIgnoreCase) &&
            !_techSuggestions.Contains(existingTicket.AssignedTech, StringComparer.OrdinalIgnoreCase))
            {
                _techSuggestions.Add(existingTicket.AssignedTech.Trim());
                _techSuggestions.Sort(StringComparer.OrdinalIgnoreCase);
            }

        WorkOrderTypeBox.ItemsSource = new[] { "", "Maintenance", "Capital", "Distribution" };
        PriorityBox.ItemsSource = new[] { "", "1", "3", "5", "15" };
        AssignedToBox.ItemsSource = _techSuggestions;

        var createdBy = TryGetDisplayName() ?? (WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName);

        DataContext = new NewTicketDraft
        {
            CreatedBy = createdBy,
            PriorityDays = "",
            Status = ""
        };

        _editingTicketId = existingTicket?.Id;
        _initialTaskCategoryId = existingTicket?.TaskCategoryId;
        _initialTaskCategoryName = existingTicket?.TaskCategoryName ?? "";
        _initialActionRequiredOverride = existingTicket?.ActionRequiredOverride ?? "";

        if (existingTicket != null)
        {
            Title = "Edit Ticket";
            CreateBtn.Content = "Save Changes";
            PopulateDraftFromExistingTicket(existingTicket, createdBy);
        }
        else
        {
            Title = "New Ticket";
            CreateBtn.Content = "Create Ticket";
        }
    }

    private void PopulateDraftFromExistingTicket(DispatchTicket ticket, string fallbackCreatedBy)
    {
        Draft.Site = ticket.Site ?? "";
        Draft.FullSiteName = "";
        Draft.Problem = ticket.Problem ?? "";
        Draft.AssignedTo = string.Equals(ticket.AssignedTech, "(Unassigned)", StringComparison.OrdinalIgnoreCase)
            ? ""
            : (ticket.AssignedTech ?? "");
        Draft.Status = ticket.Status ?? "";
        Draft.NotificationName = ticket.NotificationName ?? "";
        Draft.NotificationNumber = ticket.Notification ?? "";
        Draft.WorkOrder = ticket.CurrentWorkOrder ?? "";
        Draft.WorkOrderType = ticket.WorkOrderType ?? "";
        Draft.WorkOrderCode = ticket.GroupCode ?? "";
        Draft.PriorityDays = ticket.PriorityDays > 0 ? ticket.PriorityDays.ToString() : "";
        Draft.Notes = ticket.Notes ?? "";
        Draft.DispatchNotes = ticket.DispatchNotes ?? "";
        Draft.CreatedBy = string.IsNullOrWhiteSpace(ticket.CreatedBy) ? fallbackCreatedBy : ticket.CreatedBy;
    }

    private static string? TryGetDisplayName()
    {
        try
        {
            var full = Environment.GetEnvironmentVariable("FULLNAME");
            if (!string.IsNullOrWhiteSpace(full))
                return full.Trim();
        }
        catch
        {
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
            await LoadTaskCategoriesAsync();
            ApplyInitialLookupSelections();
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

        var visibleStatuses = statuses
            .Where(x => x.IsActive && x.ShowInFilter)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => x.Name)
            .ToList();

        StatusBox.ItemsSource = visibleStatuses;

        if (visibleStatuses.Count == 0)
            return;

        if (string.IsNullOrWhiteSpace(Draft.Status) || !visibleStatuses.Contains(Draft.Status))
        {
            Draft.Status =
                visibleStatuses.FirstOrDefault(x => x.Equals("Open", StringComparison.OrdinalIgnoreCase))
                ?? visibleStatuses.First();
        }
    }

    private async Task LoadTaskCategoriesAsync(CancellationToken ct = default)
    {
        _taskCategories.Clear();

        _taskCategories.AddRange(
            (await _ticketAdminApi.GetTaskCategoriesAsync(ct: ct))
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name));

        TaskCategoryComboBox.ItemsSource = _taskCategories
            .Select(x => x.Name)
            .ToList();

        TaskCategoryComboBox.SelectedIndex = -1;
    }

    private void ApplyInitialLookupSelections()
    {
        ActionRequiredOverrideTextBox.Text = _initialActionRequiredOverride;

        if (_taskCategories.Count == 0)
            return;

        TicketTaskCategoryDto? selectedCategory = null;

        if (_initialTaskCategoryId.HasValue)
        {
            selectedCategory = _taskCategories.FirstOrDefault(x => x.Id == _initialTaskCategoryId.Value);
        }

        if (selectedCategory == null && !string.IsNullOrWhiteSpace(_initialTaskCategoryName))
        {
            selectedCategory = _taskCategories.FirstOrDefault(x =>
                string.Equals(x.Name, _initialTaskCategoryName, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedCategory != null)
            TaskCategoryComboBox.SelectedItem = selectedCategory.Name;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ApplyAssignedTechSelection()
    {
        var assigned = (Draft.AssignedTo ?? "").Trim();

        AssignedToBox.ItemsSource = null;
        AssignedToBox.ItemsSource = _techSuggestions;

        if (string.IsNullOrWhiteSpace(assigned))
        {
            AssignedToBox.SelectedItem = null;
            Draft.AssignedTo = "";
            return;
        }

        var match = _techSuggestions.FirstOrDefault(x =>
            string.Equals(x, assigned, StringComparison.OrdinalIgnoreCase));

        if (match == null)
        {
            _techSuggestions.Add(assigned);
            _techSuggestions.Sort(StringComparer.OrdinalIgnoreCase);

            AssignedToBox.ItemsSource = null;
            AssignedToBox.ItemsSource = _techSuggestions;

            match = _techSuggestions.FirstOrDefault(x =>
                string.Equals(x, assigned, StringComparison.OrdinalIgnoreCase));
        }

        AssignedToBox.SelectedItem = match;
        Draft.AssignedTo = match ?? "";
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var site = (Draft.Site ?? "").Trim();
        if (string.IsNullOrWhiteSpace(site))
        {
            MessageBox.Show("Site is required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var problem = (Draft.Problem ?? "").Trim();

        var status = (Draft.Status ?? "").Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            MessageBox.Show("Status is required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var notif = (Draft.NotificationNumber ?? "").Trim();

        string? workOrder = null;
        var wo = (Draft.WorkOrder ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(wo))
            workOrder = wo;

        int priority = 0;
        var pri = (Draft.PriorityDays ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(pri))
        {
            if (!int.TryParse(pri, out priority) || (priority != 1 && priority != 3 && priority != 5 && priority != 15))
            {
                MessageBox.Show("Priority must be blank or one of: 1, 3, 5, 15.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var assigned = (AssignedToBox.SelectedItem as string ?? Draft.AssignedTo ?? "").Trim();
        var assignedTech = string.IsNullOrWhiteSpace(assigned) ? "(Unassigned)" : assigned;

        var woType = (Draft.WorkOrderType ?? "").Trim();
        var woCode = (Draft.WorkOrderCode ?? "").Trim();
        if (workOrder == null)
        {
            woType = "";
            woCode = "";
        }

        var selectedTaskCategoryName = (TaskCategoryComboBox.SelectedItem as string)?.Trim();

        var selectedTaskCategory = string.IsNullOrWhiteSpace(selectedTaskCategoryName)
            ? null
            : _taskCategories.FirstOrDefault(x =>
                string.Equals(x.Name, selectedTaskCategoryName, StringComparison.OrdinalIgnoreCase));

        var actionRequiredOverride = string.IsNullOrWhiteSpace(ActionRequiredOverrideTextBox.Text)
            ? null
            : ActionRequiredOverrideTextBox.Text.Trim();

        SetBusy(true);

        try
        {
            if (IsEditMode)
            {
                var updateReq = new UpdateTicketRequest(
                    Site: site,
                    NotificationName: (Draft.NotificationName ?? "").Trim(),
                    Notification: notif ?? "",
                    WorkOrder: workOrder,
                    WorkOrderClass: woType,
                    GroupCode: woCode,
                    PriorityDays: priority,
                    Status: status,
                    TaskCategoryId: selectedTaskCategory?.Id,
                    ActionRequiredOverride: actionRequiredOverride,
                    AssignedTech: assignedTech,
                    Problem: problem,
                    Notes: (Draft.Notes ?? "").Trim(),
                    DispatchNotes: (Draft.DispatchNotes ?? "").Trim()
                );

                CreatedTicketId = await _ticketsApi.UpdateTicketAsync(_editingTicketId!.Value, updateReq);
            }
            else
            {
                var createdBy = (Draft.CreatedBy ?? "").Trim();
                if (string.IsNullOrWhiteSpace(createdBy))
                    createdBy = WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;

                var createReq = new CreateTicketRequest(
                    Site: site,
                    NotificationName: (Draft.NotificationName ?? "").Trim(),
                    Notification: notif ?? "",
                    WorkOrder: workOrder,
                    WorkOrderClass: woType,
                    GroupCode: woCode,
                    PriorityDays: priority,
                    Status: status,
                    TaskCategoryId: selectedTaskCategory?.Id,
                    ActionRequiredOverride: actionRequiredOverride,
                    AssignedTech: assignedTech,
                    Problem: problem,
                    Notes: (Draft.Notes ?? "").Trim(),
                    DispatchNotes: (Draft.DispatchNotes ?? "").Trim(),
                    CreatedBy: createdBy
                );

                CreatedTicketId = await _ticketsApi.CreateTicketAsync(createReq);
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
            MessageBox.Show(ex.Message, $"{Title} Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        CreateBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = !busy;
    }
}