using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Settings;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Administration.GeneralSettings
{
    public partial class GeneralSettingsAdminView : UserControl
    {
        private readonly ApiClient _api;
        private bool _loading;
        private List<CommunicationDeviceTypeDto> _deviceTypes = new();

        private uint? _editingDeviceTypeId;

        public GeneralSettingsAdminView(ApiClient api)
        {
            InitializeComponent();
            _api = api;

            ReloadEmailSettingsButton.Click += ReloadEmailSettingsButton_Click;
            SaveEmailSettingsButton.Click += SaveEmailSettingsButton_Click;
            SendTestEmailButton.Click += SendTestEmailButton_Click;

            Loaded += GeneralSettingsAdminView_Loaded;
        }

        private async void GeneralSettingsAdminView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= GeneralSettingsAdminView_Loaded;

            await LoadEmailSettingsAsync();
            await LoadIgsdPortalUrlAsync();
            await LoadRangeExtenderLinkUrlAsync();
            await LoadCommunicationDeviceTypesAsync();
        }

        private void SetBusy(bool isBusy)
        {
            _loading = isBusy;

            EmailEnabledCheckBox.IsEnabled = !isBusy;
            EmailDryRunCheckBox.IsEnabled = !isBusy;
            EmailDailyAssignmentsCheckBox.IsEnabled = !isBusy;
            EmailWriteUpsCheckBox.IsEnabled = !isBusy;
            EmailBccSenderCheckBox.IsEnabled = !isBusy;
            EmailAllEmailsAddressTextBox.IsEnabled = !isBusy;
            EmailTestRecipientOverrideTextBox.IsEnabled = !isBusy;
            EmailTestRecipientTextBox.IsEnabled = !isBusy;
            ReloadEmailSettingsButton.IsEnabled = !isBusy;
            SaveEmailSettingsButton.IsEnabled = !isBusy;
            SendTestEmailButton.IsEnabled = !isBusy;

            IgsdPortalUrlTextBox.IsEnabled = !isBusy;
            ReloadPortalUrlButton.IsEnabled = !isBusy;
            SavePortalUrlButton.IsEnabled = !isBusy;

            RangeExtenderLinkUrlTextBox.IsEnabled = !isBusy;
            ReloadRangeExtenderLinkButton.IsEnabled = !isBusy;
            SaveRangeExtenderLinkButton.IsEnabled = !isBusy;

            NewDeviceTypeNameTextBox.IsEnabled = !isBusy;
            NewDeviceTypeSortOrderTextBox.IsEnabled = !isBusy;
            NewDeviceTypeActiveCheckBox.IsEnabled = !isBusy;
            AddDeviceTypeButton.IsEnabled = !isBusy;
            CancelAddDeviceTypeButton.IsEnabled = !isBusy;
            ToggleAddDeviceTypeButton.IsEnabled = !isBusy;
            EditSelectedDeviceTypeButton.IsEnabled = !isBusy && DeviceTypesDataGrid.SelectedItem is CommunicationDeviceTypeDto;
            DeleteSelectedDeviceTypeButton.IsEnabled = !isBusy && DeviceTypesDataGrid.SelectedItem is CommunicationDeviceTypeDto;
            ReloadDeviceTypesButton.IsEnabled = !isBusy;
            DeviceTypesDataGrid.IsEnabled = !isBusy;
        }

        // -------------------------
        // Email Settings
        // -------------------------

        private async Task LoadEmailSettingsAsync()
        {
            try
            {
                SetBusy(true);
                EmailSettingsStatusTextBlock.Text = "Loading email settings...";

                var dto = await _api.GetEmailSettingsAsync();

                if (dto == null)
                {
                    EmailSettingsStatusTextBlock.Text = "Email settings were not returned by the API.";
                    return;
                }

                EmailEnabledCheckBox.IsChecked = dto.EmailEnabled;
                EmailDryRunCheckBox.IsChecked = dto.DryRun;
                EmailDailyAssignmentsCheckBox.IsChecked = dto.DailyAssignmentsEnabled;
                EmailWriteUpsCheckBox.IsChecked = dto.WriteUpsEnabled;
                EmailBccSenderCheckBox.IsChecked = dto.BccSender;
                EmailAllEmailsAddressTextBox.Text = dto.AllEmailsAddress ?? string.Empty;
                EmailTestRecipientOverrideTextBox.Text = dto.TestRecipientOverride ?? string.Empty;

                EmailSettingsStatusTextBlock.Text = "Email settings loaded.";
            }
            catch (ApiClient.ApiException ex)
            {
                EmailSettingsStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Load failed ({ex.StatusCode})."
                    : $"Load failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                EmailSettingsStatusTextBlock.Text = $"Load failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task SaveEmailSettingsAsync()
        {
            try
            {
                SetBusy(true);
                EmailSettingsStatusTextBlock.Text = "Saving email settings...";

                var dto = await _api.UpdateEmailSettingsAsync(
                    new UpdateEmailSettingsRequest
                    {
                        EmailEnabled = EmailEnabledCheckBox.IsChecked == true,
                        DryRun = EmailDryRunCheckBox.IsChecked == true,
                        DailyAssignmentsEnabled = EmailDailyAssignmentsCheckBox.IsChecked == true,
                        WriteUpsEnabled = EmailWriteUpsCheckBox.IsChecked == true,
                        BccSender = EmailBccSenderCheckBox.IsChecked == true,
                        AllEmailsAddress = (EmailAllEmailsAddressTextBox.Text ?? string.Empty).Trim(),
                        TestRecipientOverride = (EmailTestRecipientOverrideTextBox.Text ?? string.Empty).Trim()
                    });

                if (dto != null)
                {
                    EmailEnabledCheckBox.IsChecked = dto.EmailEnabled;
                    EmailDryRunCheckBox.IsChecked = dto.DryRun;
                    EmailDailyAssignmentsCheckBox.IsChecked = dto.DailyAssignmentsEnabled;
                    EmailWriteUpsCheckBox.IsChecked = dto.WriteUpsEnabled;
                    EmailBccSenderCheckBox.IsChecked = dto.BccSender;
                    EmailAllEmailsAddressTextBox.Text = dto.AllEmailsAddress ?? string.Empty;
                    EmailTestRecipientOverrideTextBox.Text = dto.TestRecipientOverride ?? string.Empty;
                }

                EmailSettingsStatusTextBlock.Text = "Email settings saved.";
            }
            catch (ApiClient.ApiException ex)
            {
                EmailSettingsStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Save failed ({ex.StatusCode})."
                    : $"Save failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                EmailSettingsStatusTextBlock.Text = $"Save failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task SendTestEmailAsync()
        {
            var toAddress = (EmailTestRecipientTextBox.Text ?? string.Empty).Trim();
            var overrideAddress = (EmailTestRecipientOverrideTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(toAddress) &&
                string.IsNullOrWhiteSpace(overrideAddress))
            {
                EmailSettingsStatusTextBlock.Text = "Enter a test recipient or set a Test Recipient Override.";
                return;
            }

            try
            {
                SetBusy(true);
                EmailSettingsStatusTextBlock.Text = "Sending test email...";

                var result = await _api.SendTestEmailAsync(
                    new SendTestEmailRequest
                    {
                        ToAddress = toAddress,
                        CreatedBy = Environment.UserName,
                        FromAddress = string.Empty,
                        FromDisplayName = string.Empty
                    });

                if (result == null)
                {
                    EmailSettingsStatusTextBlock.Text = "Test email request completed, but no response was returned.";
                    return;
                }

                EmailSettingsStatusTextBlock.Text =
                    $"Test email {result.Status}: {result.Message} LogId={result.LogId}";
            }
            catch (ApiClient.ApiException ex)
            {
                EmailSettingsStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Test failed ({ex.StatusCode})."
                    : $"Test failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                EmailSettingsStatusTextBlock.Text = $"Test failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ReloadEmailSettingsButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await LoadEmailSettingsAsync();
        }

        private async void SaveEmailSettingsButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await SaveEmailSettingsAsync();
        }

        private async void SendTestEmailButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await SendTestEmailAsync();
        }

        // -------------------------
        // IGSD Portal URL
        // -------------------------

        private async Task LoadIgsdPortalUrlAsync()
        {
            try
            {
                SetBusy(true);
                PortalUrlStatusTextBlock.Text = "Loading portal URL...";

                var dto = await _api.GetIgsdPortalUrlAsync();
                IgsdPortalUrlTextBox.Text = dto?.Url?.Trim() ?? string.Empty;

                PortalUrlStatusTextBlock.Text = "Portal URL loaded.";
            }
            catch (ApiClient.ApiException ex)
            {
                PortalUrlStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Load failed ({ex.StatusCode})."
                    : $"Load failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                PortalUrlStatusTextBlock.Text = $"Load failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task SaveIgsdPortalUrlAsync()
        {
            var url = (IgsdPortalUrlTextBox.Text ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(url) &&
                (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                PortalUrlStatusTextBlock.Text = "Enter a valid http or https URL, or leave it blank to disable the portal.";
                return;
            }

            try
            {
                SetBusy(true);

                PortalUrlStatusTextBlock.Text = string.IsNullOrWhiteSpace(url)
                    ? "Clearing portal URL..."
                    : "Saving portal URL...";

                var dto = await _api.UpdateIgsdPortalUrlAsync(url);

                IgsdPortalUrlTextBox.Text = dto?.Url?.Trim() ?? string.Empty;

                PortalUrlStatusTextBlock.Text = string.IsNullOrWhiteSpace(IgsdPortalUrlTextBox.Text)
                    ? "Portal URL cleared. IGSD Portal tab is now disabled."
                    : "Portal URL saved.";
            }
            catch (ApiClient.ApiException ex)
            {
                PortalUrlStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Save failed ({ex.StatusCode})."
                    : $"Save failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                PortalUrlStatusTextBlock.Text = $"Save failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ReloadPortalUrlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await LoadIgsdPortalUrlAsync();
        }

        private async void SavePortalUrlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await SaveIgsdPortalUrlAsync();
        }

        // -------------------------
        // RX Portal URL
        // -------------------------

        private async Task LoadRangeExtenderLinkUrlAsync()
        {
            try
            {
                SetBusy(true);
                RangeExtenderLinkStatusTextBlock.Text = "Loading Range Extender link URL...";

                var dto = await _api.GetRangeExtenderLinkUrlAsync();
                RangeExtenderLinkUrlTextBox.Text = dto?.Url?.Trim() ?? string.Empty;

                RangeExtenderLinkStatusTextBlock.Text = "Range Extender link URL loaded.";
            }
            catch (ApiClient.ApiException ex)
            {
                RangeExtenderLinkStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Load failed ({ex.StatusCode})."
                    : $"Load failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                RangeExtenderLinkStatusTextBlock.Text = $"Load failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task SaveRangeExtenderLinkUrlAsync()
        {
            var url = (RangeExtenderLinkUrlTextBox.Text ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(url) &&
                (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                RangeExtenderLinkStatusTextBlock.Text = "Enter a valid http or https URL, or leave it blank.";
                return;
            }

            try
            {
                SetBusy(true);
                RangeExtenderLinkStatusTextBlock.Text = string.IsNullOrWhiteSpace(url)
                    ? "Clearing Range Extender link URL..."
                    : "Saving Range Extender link URL...";

                var dto = await _api.UpdateRangeExtenderLinkUrlAsync(url);

                RangeExtenderLinkUrlTextBox.Text = dto?.Url?.Trim() ?? string.Empty;
                RangeExtenderLinkStatusTextBlock.Text = string.IsNullOrWhiteSpace(RangeExtenderLinkUrlTextBox.Text)
                    ? "Range Extender link URL cleared."
                    : "Range Extender link URL saved.";
            }
            catch (ApiClient.ApiException ex)
            {
                RangeExtenderLinkStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Save failed ({ex.StatusCode})."
                    : $"Save failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                RangeExtenderLinkStatusTextBlock.Text = $"Save failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ReloadRangeExtenderLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await LoadRangeExtenderLinkUrlAsync();
        }

        private async void SaveRangeExtenderLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await SaveRangeExtenderLinkUrlAsync();
        }

        // -------------------------
        // Communication Device Types
        // -------------------------

        private async Task LoadCommunicationDeviceTypesAsync()
        {
            try
            {
                SetBusy(true);
                DeviceTypesStatusTextBlock.Text = "Loading communication device types...";

                _deviceTypes = await _api.GetCommunicationDeviceTypesAsync(activeOnly: false);
                RenderCommunicationDeviceTypes();

                DeviceTypesStatusTextBlock.Text = $"{_deviceTypes.Count} communication device type(s) loaded.";
            }
            catch (ApiClient.ApiException ex)
            {
                DeviceTypesStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Load failed ({ex.StatusCode})."
                    : $"Load failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                DeviceTypesStatusTextBlock.Text = $"Load failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RenderCommunicationDeviceTypes()
        {
            var sorted = _deviceTypes
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .ToList();

            DeviceTypesDataGrid.ItemsSource = null;
            DeviceTypesDataGrid.ItemsSource = sorted;

            if (sorted.Count == 0)
            {
                DeviceTypesStatusTextBlock.Text = "No communication device types have been added yet.";
                return;
            }

            DeviceTypesStatusTextBlock.Text = $"{sorted.Count} communication device type(s) loaded.";
        }

        private async Task SaveCommunicationDeviceTypeAsync(uint id, string displayName, bool isActive, int sortOrder)
        {
            try
            {
                SetBusy(true);
                DeviceTypesStatusTextBlock.Text = $"Saving {displayName}...";

                await _api.UpdateCommunicationDeviceTypeAsync(
                    id,
                    new SaveCommunicationDeviceTypeRequest
                    {
                        DisplayName = displayName,
                        IsActive = isActive,
                        SortOrder = sortOrder
                    });

                DeviceTypesStatusTextBlock.Text = $"{displayName} saved.";

                await LoadCommunicationDeviceTypesAsync();
            }
            catch (ApiClient.ApiException ex)
            {
                DeviceTypesStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Save failed ({ex.StatusCode})."
                    : $"Save failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                DeviceTypesStatusTextBlock.Text = $"Save failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task DeleteCommunicationDeviceTypeAsync(uint id, string displayName)
        {
            try
            {
                SetBusy(true);
                DeviceTypesStatusTextBlock.Text = $"Deleting {displayName}...";

                await _api.DeleteCommunicationDeviceTypeAsync(id);

                if (_editingDeviceTypeId == id)
                {
                    ResetDeviceTypeForm();
                    SetDeviceTypeFormVisible(false);
                }

                await LoadCommunicationDeviceTypesAsync();

                DeviceTypesDataGrid.SelectedItem = null;
                DeviceTypesStatusTextBlock.Text = $"{displayName} deleted.";
            }
            catch (ApiClient.ApiException ex)
            {
                DeviceTypesStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Delete failed ({ex.StatusCode})."
                    : $"Delete failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                DeviceTypesStatusTextBlock.Text = $"Delete failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task AddCommunicationDeviceTypeAsync()
        {
            var name = (NewDeviceTypeNameTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                DeviceTypesStatusTextBlock.Text = "Device type name is required.";
                NewDeviceTypeNameTextBox.Focus();
                return;
            }

            if (name.Length > 100)
            {
                DeviceTypesStatusTextBlock.Text = "Device type name is limited to 100 characters.";
                NewDeviceTypeNameTextBox.Focus();
                return;
            }

            if (!int.TryParse(NewDeviceTypeSortOrderTextBox.Text, out var sortOrder))
            {
                DeviceTypesStatusTextBlock.Text = "Sort order must be a number.";
                NewDeviceTypeSortOrderTextBox.Focus();
                return;
            }

            var isActive = NewDeviceTypeActiveCheckBox.IsChecked == true;

            if (_editingDeviceTypeId is uint editId)
            {
                await SaveCommunicationDeviceTypeAsync(editId, name, isActive, sortOrder);

                ResetDeviceTypeForm();
                SetDeviceTypeFormVisible(false);
                return;
            }

            try
            {
                SetBusy(true);
                DeviceTypesStatusTextBlock.Text = $"Adding {name}...";

                await _api.CreateCommunicationDeviceTypeAsync(
                    new SaveCommunicationDeviceTypeRequest
                    {
                        DisplayName = name,
                        IsActive = isActive,
                        SortOrder = sortOrder
                    });

                ResetDeviceTypeForm();
                SetDeviceTypeFormVisible(false);

                await LoadCommunicationDeviceTypesAsync();

                DeviceTypesStatusTextBlock.Text = $"{name} added.";
            }
            catch (ApiClient.ApiException ex)
            {
                DeviceTypesStatusTextBlock.Text = string.IsNullOrWhiteSpace(ex.Body)
                    ? $"Add failed ({ex.StatusCode})."
                    : $"Add failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                DeviceTypesStatusTextBlock.Text = $"Add failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ReloadDeviceTypesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await LoadCommunicationDeviceTypesAsync();
        }

        private async void AddDeviceTypeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            await AddCommunicationDeviceTypeAsync();
        }

        private void ResetDeviceTypeForm()
        {
            _editingDeviceTypeId = null;

            DeviceTypeFormTitleTextBlock.Text = "Add Communication Device Type";
            DeviceTypeFormHelpTextBlock.Text = "Create a new selectable communication device type.";
            AddDeviceTypeButton.Content = "Add";

            NewDeviceTypeNameTextBox.Clear();
            NewDeviceTypeSortOrderTextBox.Text = "100";
            NewDeviceTypeActiveCheckBox.IsChecked = true;
        }

        private void SetDeviceTypeFormVisible(bool visible)
        {
            AddDeviceTypeCard.Visibility = visible
                ? Visibility.Visible
                : Visibility.Collapsed;

            ToggleAddDeviceTypeButton.Content = visible
                ? "Hide New Type"
                : "+ New Type";

            if (visible)
            {
                NewDeviceTypeNameTextBox.Focus();
                NewDeviceTypeNameTextBox.SelectAll();
            }
        }

        private void LoadDeviceTypeIntoForm(CommunicationDeviceTypeDto selected)
        {
            _editingDeviceTypeId = selected.Id;

            DeviceTypeFormTitleTextBlock.Text = "Edit Communication Device Type";
            DeviceTypeFormHelpTextBlock.Text = $"Editing ID {selected.Id}. Make changes below, then save.";
            AddDeviceTypeButton.Content = "Save";

            NewDeviceTypeNameTextBox.Text = selected.DisplayName ?? string.Empty;
            NewDeviceTypeSortOrderTextBox.Text = selected.SortOrder.ToString();
            NewDeviceTypeActiveCheckBox.IsChecked = selected.IsActive;

            SetDeviceTypeFormVisible(true);
        }

        private void ToggleAddDeviceTypeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (AddDeviceTypeCard.Visibility == Visibility.Visible)
            {
                SetDeviceTypeFormVisible(false);
                return;
            }

            ResetDeviceTypeForm();
            SetDeviceTypeFormVisible(true);
        }

        private void CancelAddDeviceTypeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            ResetDeviceTypeForm();
            SetDeviceTypeFormVisible(false);
        }

        private void EditSelectedDeviceTypeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (DeviceTypesDataGrid.SelectedItem is not CommunicationDeviceTypeDto selected)
            {
                DeviceTypesStatusTextBlock.Text = "Select a communication device type to edit.";
                return;
            }

            LoadDeviceTypeIntoForm(selected);
        }

        private async void DeleteSelectedDeviceTypeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (DeviceTypesDataGrid.SelectedItem is not CommunicationDeviceTypeDto selected)
            {
                DeviceTypesStatusTextBlock.Text = "Select a communication device type to delete.";
                return;
            }

            var name = (selected.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
                name = $"ID {selected.Id}";

            var confirm = MessageBox.Show(
                Window.GetWindow(this),
                $"Delete communication device type \"{name}\"?\n\nThis removes it from the selectable Device Type list.",
                "Delete Communication Device Type",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            await DeleteCommunicationDeviceTypeAsync(selected.Id, name);
        }

        private void DeviceTypesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = DeviceTypesDataGrid.SelectedItem as CommunicationDeviceTypeDto;
            var hasSelection = selected is not null;

            EditSelectedDeviceTypeButton.IsEnabled = !_loading && hasSelection;
            DeleteSelectedDeviceTypeButton.IsEnabled = !_loading && hasSelection;
        }
    }
}