using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Settings;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Administration.WriteUpWorkflow
{
    public partial class WriteUpWorkflowAdminView
    {
        private List<WriteUpFlagDto> _writeUpFlags = new();

        private uint? _editingWriteUpFlagId;

        private bool _editingWriteUpFlagIsSystem;

        private void UpdateWriteUpFlagBusyState(
            bool isBusy)
        {
            NewWriteUpFlagNameTextBox.IsEnabled =
                !isBusy &&
                !_editingWriteUpFlagIsSystem;

            NewWriteUpFlagSortOrderTextBox.IsEnabled =
                !isBusy;

            NewWriteUpFlagActiveCheckBox.IsEnabled =
                !isBusy;

            NewWriteUpFlagTechnicianVisibleCheckBox.IsEnabled =
                !isBusy;

            AddWriteUpFlagButton.IsEnabled =
                !isBusy;

            CancelAddWriteUpFlagButton.IsEnabled =
                !isBusy;

            ToggleAddWriteUpFlagButton.IsEnabled =
                !isBusy;

            ReloadWriteUpFlagsButton.IsEnabled =
                !isBusy;

            WriteUpFlagsDataGrid.IsEnabled =
                !isBusy;

            UpdateWriteUpFlagSelectionButtons();
        }

        private async Task LoadWriteUpFlagsAsync()
        {
            try
            {
                SetBusy(true);

                WriteUpFlagsStatusTextBlock.Text =
                    "Loading technician write-up flags...";

                _writeUpFlags =
                    await _api.GetWriteUpFlagsAsync(
                        activeOnly: false,
                        technicianVisibleOnly: false);

                RenderWriteUpFlags();
            }
            catch (ApiClient.ApiException ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Load failed ({ex.StatusCode})."
                        : $"Load failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    $"Load failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RenderWriteUpFlags()
        {
            var sorted =
                _writeUpFlags
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            WriteUpFlagsDataGrid.ItemsSource = null;
            WriteUpFlagsDataGrid.ItemsSource = sorted;

            WriteUpFlagsStatusTextBlock.Text =
                sorted.Count == 0
                    ? "No technician write-up flags have been added yet."
                    : $"{sorted.Count} technician write-up flag(s) loaded.";

            UpdateWriteUpFlagSelectionButtons();
            RefreshDispatchCloseoutWriteUpFlagOptions();
        }

        private async Task<bool> SaveWriteUpFlagAsync(
            uint id,
            string displayName,
            bool isActive,
            int sortOrder,
            bool isTechnicianVisible)
        {
            try
            {
                SetBusy(true);

                WriteUpFlagsStatusTextBlock.Text =
                    $"Saving {displayName}...";

                await _api.UpdateWriteUpFlagAsync(
                    id,
                    new SaveWriteUpFlagRequest
                    {
                        DisplayName = displayName,
                        IsActive = isActive,
                        SortOrder = sortOrder,
                        IsTechnicianVisible =
                            isTechnicianVisible
                    });

                await LoadWriteUpFlagsAsync();

                WriteUpFlagsStatusTextBlock.Text =
                    $"{displayName} saved.";

                return true;
            }
            catch (ApiClient.ApiException ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Save failed ({ex.StatusCode})."
                        : $"Save failed: {ex.Body}";

                return false;
            }
            catch (Exception ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    $"Save failed: {ex.Message}";

                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task AddOrUpdateWriteUpFlagAsync()
        {
            var name =
                (NewWriteUpFlagNameTextBox.Text ??
                 string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                WriteUpFlagsStatusTextBlock.Text =
                    "Write-up flag name is required.";

                NewWriteUpFlagNameTextBox.Focus();
                return;
            }

            if (name.Length > 100)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    "Write-up flag name is limited to 100 characters.";

                NewWriteUpFlagNameTextBox.Focus();
                return;
            }

            if (!int.TryParse(
                    NewWriteUpFlagSortOrderTextBox.Text,
                    out var sortOrder))
            {
                WriteUpFlagsStatusTextBlock.Text =
                    "Sort order must be a number.";

                NewWriteUpFlagSortOrderTextBox.Focus();
                return;
            }

            var isActive =
                NewWriteUpFlagActiveCheckBox.IsChecked == true;

            var technicianVisible =
                NewWriteUpFlagTechnicianVisibleCheckBox
                    .IsChecked == true;

            if (_editingWriteUpFlagId is uint editId)
            {
                var saved =
                    await SaveWriteUpFlagAsync(
                        editId,
                        name,
                        isActive,
                        sortOrder,
                        technicianVisible);

                if (saved)
                {
                    ResetWriteUpFlagForm();
                    SetWriteUpFlagFormVisible(false);
                }

                return;
            }

            try
            {
                SetBusy(true);

                WriteUpFlagsStatusTextBlock.Text =
                    $"Adding {name}...";

                await _api.CreateWriteUpFlagAsync(
                    new SaveWriteUpFlagRequest
                    {
                        DisplayName = name,
                        IsActive = isActive,
                        SortOrder = sortOrder,
                        IsTechnicianVisible =
                            technicianVisible
                    });

                ResetWriteUpFlagForm();
                SetWriteUpFlagFormVisible(false);

                await LoadWriteUpFlagsAsync();

                WriteUpFlagsStatusTextBlock.Text =
                    $"{name} added.";
            }
            catch (ApiClient.ApiException ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Add failed ({ex.StatusCode})."
                        : $"Add failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    $"Add failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task DeleteWriteUpFlagAsync(
            WriteUpFlagDto flag)
        {
            if (flag.IsSystem)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    "System write-up flags cannot be deleted. " +
                    "Deactivate the flag instead.";

                return;
            }

            var name =
                string.IsNullOrWhiteSpace(flag.DisplayName)
                    ? $"ID {flag.Id}"
                    : flag.DisplayName.Trim();

            try
            {
                SetBusy(true);

                WriteUpFlagsStatusTextBlock.Text =
                    $"Deleting {name}...";

                await _api.DeleteWriteUpFlagAsync(
                    flag.Id);

                if (_editingWriteUpFlagId == flag.Id)
                {
                    ResetWriteUpFlagForm();
                    SetWriteUpFlagFormVisible(false);
                }

                await LoadWriteUpFlagsAsync();

                WriteUpFlagsDataGrid.SelectedItem = null;

                WriteUpFlagsStatusTextBlock.Text =
                    $"{name} deleted.";
            }
            catch (ApiClient.ApiException ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Delete failed ({ex.StatusCode})."
                        : $"Delete failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    $"Delete failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ResetWriteUpFlagForm()
        {
            _editingWriteUpFlagId = null;
            _editingWriteUpFlagIsSystem = false;

            WriteUpFlagFormTitleTextBlock.Text =
                "Add Technician Write-Up Flag";

            WriteUpFlagFormHelpTextBlock.Text =
                "Create a new selectable technician write-up flag.";

            AddWriteUpFlagButton.Content =
                "Add";

            NewWriteUpFlagNameTextBox.Clear();

            NewWriteUpFlagSortOrderTextBox.Text =
                "100";

            NewWriteUpFlagActiveCheckBox.IsChecked =
                true;

            NewWriteUpFlagTechnicianVisibleCheckBox.IsChecked =
                true;

            NewWriteUpFlagNameTextBox.IsEnabled =
                !_loading;
        }

        private void SetWriteUpFlagFormVisible(
            bool visible)
        {
            AddWriteUpFlagCard.Visibility =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ToggleAddWriteUpFlagButton.Content =
                visible
                    ? "Hide New Flag"
                    : "+ New Flag";

            if (visible)
            {
                NewWriteUpFlagNameTextBox.Focus();
                NewWriteUpFlagNameTextBox.SelectAll();
            }
        }

        private void LoadWriteUpFlagIntoForm(
            WriteUpFlagDto selected)
        {
            _editingWriteUpFlagId =
                selected.Id;

            _editingWriteUpFlagIsSystem =
                selected.IsSystem;

            WriteUpFlagFormTitleTextBlock.Text =
                selected.IsSystem
                    ? "Edit Protected Write-Up Flag"
                    : "Edit Technician Write-Up Flag";

            WriteUpFlagFormHelpTextBlock.Text =
                selected.IsSystem
                    ? "This protected flag may be activated, deactivated, reordered, or hidden from technicians. Its name cannot be changed."
                    : $"Editing ID {selected.Id}. Make changes below, then save.";

            AddWriteUpFlagButton.Content =
                "Save";

            NewWriteUpFlagNameTextBox.Text =
                selected.DisplayName ?? string.Empty;

            NewWriteUpFlagSortOrderTextBox.Text =
                selected.SortOrder.ToString();

            NewWriteUpFlagActiveCheckBox.IsChecked =
                selected.IsActive;

            NewWriteUpFlagTechnicianVisibleCheckBox.IsChecked =
                selected.IsTechnicianVisible;

            NewWriteUpFlagNameTextBox.IsEnabled =
                !_loading &&
                !selected.IsSystem;

            SetWriteUpFlagFormVisible(true);
        }

        private void UpdateWriteUpFlagSelectionButtons()
        {
            var selected =
                WriteUpFlagsDataGrid.SelectedItem
                    as WriteUpFlagDto;

            var hasSelection =
                selected is not null;

            EditSelectedWriteUpFlagButton.IsEnabled =
                !_loading &&
                hasSelection;

            DeleteSelectedWriteUpFlagButton.IsEnabled =
                !_loading &&
                selected is not null &&
                !selected.IsSystem;

            var ordered =
                _writeUpFlags
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            var selectedIndex =
                selected is null
                    ? -1
                    : ordered.FindIndex(
                        x => x.Id == selected.Id);

            MoveWriteUpFlagUpButton.IsEnabled =
                !_loading &&
                selectedIndex > 0;

            MoveWriteUpFlagDownButton.IsEnabled =
                !_loading &&
                selectedIndex >= 0 &&
                selectedIndex < ordered.Count - 1;
        }

        private async Task MoveSelectedWriteUpFlagAsync(
            int direction)
        {
            if (WriteUpFlagsDataGrid.SelectedItem
                is not WriteUpFlagDto selected)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    "Select a write-up flag to move.";

                return;
            }

            var ordered =
                _writeUpFlags
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            var currentIndex =
                ordered.FindIndex(
                    x => x.Id == selected.Id);

            var targetIndex =
                currentIndex + direction;

            if (currentIndex < 0 ||
                targetIndex < 0 ||
                targetIndex >= ordered.Count)
            {
                return;
            }

            (ordered[currentIndex], ordered[targetIndex]) =
                (ordered[targetIndex], ordered[currentIndex]);

            try
            {
                SetBusy(true);

                WriteUpFlagsStatusTextBlock.Text =
                    $"Reordering {selected.DisplayName}...";

                for (var index = 0;
                     index < ordered.Count;
                     index++)
                {
                    var flag =
                        ordered[index];

                    var desiredSortOrder =
                        (index + 1) * 10;

                    if (flag.SortOrder == desiredSortOrder)
                        continue;

                    await _api.UpdateWriteUpFlagAsync(
                        flag.Id,
                        new SaveWriteUpFlagRequest
                        {
                            DisplayName =
                                flag.DisplayName,

                            IsActive =
                                flag.IsActive,

                            SortOrder =
                                desiredSortOrder,

                            IsTechnicianVisible =
                                flag.IsTechnicianVisible
                        });
                }

                await LoadWriteUpFlagsAsync();

                var refreshedSelection =
                    _writeUpFlags.FirstOrDefault(
                        x => x.Id == selected.Id);

                if (refreshedSelection is not null)
                {
                    WriteUpFlagsDataGrid.SelectedItem =
                        refreshedSelection;

                    WriteUpFlagsDataGrid.ScrollIntoView(
                        refreshedSelection);
                }

                WriteUpFlagsStatusTextBlock.Text =
                    $"{selected.DisplayName} reordered.";
            }
            catch (ApiClient.ApiException ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Reorder failed ({ex.StatusCode})."
                        : $"Reorder failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    $"Reorder failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ReloadWriteUpFlagsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await LoadWriteUpFlagsAsync();
        }

        private async void AddWriteUpFlagButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await AddOrUpdateWriteUpFlagAsync();
        }

        private void ToggleAddWriteUpFlagButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (AddWriteUpFlagCard.Visibility ==
                Visibility.Visible)
            {
                SetWriteUpFlagFormVisible(false);
                return;
            }

            ResetWriteUpFlagForm();
            SetWriteUpFlagFormVisible(true);
        }

        private void CancelAddWriteUpFlagButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            ResetWriteUpFlagForm();
            SetWriteUpFlagFormVisible(false);
        }

        private void EditSelectedWriteUpFlagButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (WriteUpFlagsDataGrid.SelectedItem
                is not WriteUpFlagDto selected)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    "Select a write-up flag to edit.";

                return;
            }

            LoadWriteUpFlagIntoForm(selected);
        }

        private async void DeleteSelectedWriteUpFlagButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (WriteUpFlagsDataGrid.SelectedItem
                is not WriteUpFlagDto selected)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    "Select a write-up flag to delete.";

                return;
            }

            if (selected.IsSystem)
            {
                WriteUpFlagsStatusTextBlock.Text =
                    "System write-up flags cannot be deleted. " +
                    "Deactivate the flag instead.";

                return;
            }

            var name =
                string.IsNullOrWhiteSpace(selected.DisplayName)
                    ? $"ID {selected.Id}"
                    : selected.DisplayName.Trim();

            var confirm =
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"Delete technician write-up flag \"{name}\"?\n\n" +
                    "This permanently removes it from the selectable write-up flag list.",
                    "Delete Technician Write-Up Flag",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            await DeleteWriteUpFlagAsync(selected);
        }

        private async void MoveWriteUpFlagUpButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await MoveSelectedWriteUpFlagAsync(-1);
        }

        private async void MoveWriteUpFlagDownButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await MoveSelectedWriteUpFlagAsync(1);
        }

        private void WriteUpFlagsDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateWriteUpFlagSelectionButtons();
        }
    }
}
