using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Tickets;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows;

namespace SmartGridSuite.Client.Views.Dispatcher.Dialogs;

public partial class NewTicketWindow : Window
{
    private readonly TicketsApi _ticketsApi;

    public long? CreatedTicketId { get; private set; }

    private NewTicketDraft Draft => (NewTicketDraft)DataContext;

    public NewTicketWindow(TicketsApi ticketsApi, IEnumerable<string>? techNames = null)
    {
        InitializeComponent();
        _ticketsApi = ticketsApi;

        // Drop-downs
        WorkOrderTypeBox.ItemsSource = new[] { "", "Maintenance", "Capital", "Distribution" };
        PriorityBox.ItemsSource = new[] { "", "1", "3", "5", "15" };

        if (techNames != null)
            AssignedToBox.ItemsSource = techNames;

        // CreatedBy best-effort (you'll swap this later to your EmployeeId->Name lookup)
        var createdBy = TryGetDisplayName() ?? (WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName);

        DataContext = new NewTicketDraft
        {
            CreatedBy = createdBy,
            PriorityDays = "" // blank default
        };
    }

    private static string? TryGetDisplayName()
    {
        // Best-effort without needing AD libs/packages.
        // If you later add a real lookup, replace this method.
        try
        {
            // Sometimes corp env var exists; harmless if not.
            var full = Environment.GetEnvironmentVariable("FULLNAME");
            if (!string.IsNullOrWhiteSpace(full)) return full.Trim();
        }
        catch { }

        return null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        // Required: Site + Problem
        var site = (Draft.Site ?? "").Trim();
        if (string.IsNullOrWhiteSpace(site))
        {
            MessageBox.Show("Site is required.", "New Ticket", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var problem = (Draft.Problem ?? "").Trim();
        if (string.IsNullOrWhiteSpace(problem))
        {
            MessageBox.Show("Problem is required.", "New Ticket", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Optional: Notification # => 10 digits
        var notif = (Draft.NotificationNumber ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(notif) && !Regex.IsMatch(notif, @"^\d{10}$"))
        {
            MessageBox.Show("Notification # must be exactly 10 digits when provided.", "New Ticket",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Optional: WorkOrder => 9 digits
        string? workOrder = null;
        var wo = (Draft.WorkOrder ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(wo))
        {
            if (!Regex.IsMatch(wo, @"^\d{9}$"))
            {
                MessageBox.Show("Work Order must be exactly 9 digits when provided.", "New Ticket",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            workOrder = wo;
        }

        // Priority: blank allowed => 0
        int priority = 0;
        var pri = (Draft.PriorityDays ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(pri))
        {
            if (!int.TryParse(pri, out priority) || (priority != 1 && priority != 3 && priority != 5 && priority != 15))
            {
                MessageBox.Show("Priority must be blank or one of: 1, 3, 5, 15.", "New Ticket",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // AssignedTo optional; status computed
        var assigned = (Draft.AssignedTo ?? "").Trim();
        var assignedTech = string.IsNullOrWhiteSpace(assigned) ? "(Unassigned)" : assigned;
        var status = assignedTech == "(Unassigned)" ? "Open" : "Assigned";

        // WO type/code only meaningful if WO exists
        var woType = (Draft.WorkOrderType ?? "").Trim();
        var woCode = (Draft.WorkOrderCode ?? "").Trim();
        if (workOrder == null)
        {
            woType = "";
            woCode = "";
        }

        var createdBy = (Draft.CreatedBy ?? "").Trim();
        if (string.IsNullOrWhiteSpace(createdBy))
            createdBy = WindowsIdentity.GetCurrent()?.Name ?? Environment.UserName;

        // IMPORTANT: Your CreateTicketRequest record currently requires non-null strings for many fields.
        // We send "" for optional text fields the user can leave blank.
        var req = new CreateTicketRequest(
            Site: site,
            NotificationName: (Draft.NotificationName ?? "").Trim(),
            Notification: notif,                 // "" if blank (API should allow if optional)
            WorkOrder: workOrder,                // null if blank
            WorkOrderClass: woType,              // storing "Maintenance"/"Capital"/"Distribution" (or "")
            GroupCode: woCode,                   // filtered by type (or "")
            PriorityDays: priority,              // 0 = blank
            Status: status,                      // computed
            AssignedTech: assignedTech,          // "(Unassigned)" or tech name
            Problem: problem,
            Notes: (Draft.Notes ?? "").Trim(),
            CreatedBy: createdBy
        );

        SetBusy(true);
        try
        {
            CreatedTicketId = await _ticketsApi.CreateTicketAsync(req);
            DialogResult = true;
            Close();
        }
        catch (ApiClient.ApiException ex) when (ex.StatusCode == 409)
        {
            MessageBox.Show(
                ex.Body ?? "A ticket already exists with that Notification #.",
                "Duplicate Notification",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (ApiClient.ApiException ex) when (ex.StatusCode == 400)
        {
            MessageBox.Show(
                ex.Body ?? "Request was invalid.",
                "Create Ticket",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Create Ticket Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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