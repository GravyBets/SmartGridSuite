using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Settings;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Administration.WriteUpWorkflow
{
    public partial class WriteUpWorkflowAdminView
    {
        private List<ReferToOptionDto> _referToOptions = new();

        private uint? _editingReferToOptionId;

        private void UpdateReferToOptionBusyState(
            bool isBusy)
        {
            NewReferToOptionNameTextBox.IsEnabled = !isBusy;
            NewReferToOptionSortOrderTextBox.IsEnabled = !isBusy;
            NewReferToOptionActiveCheckBox.IsEnabled = !isBusy;
            AddReferToOptionButton.IsEnabled = !isBusy;
            CancelAddReferToOptionButton.IsEnabled = !isBusy;
            ToggleAddReferToOptionButton.IsEnabled = !isBusy;
            ReloadReferToOptionsButton.IsEnabled = !isBusy;
            ReferToOptionsDataGrid.IsEnabled = !isBusy;

            UpdateReferToOptionSelectionButtons();
        }

        private async Task LoadReferToOptionsAsync()
        {
            try
            {
                SetBusy(true);

                ReferToOptionsStatusTextBlock.Text =
                    "Loading Refer To options...";

                _referToOptions =
                    await _api.GetReferToOptionsAsync(
                        activeOnly: false);

                RenderReferToOptions();
            }
            catch (ApiClient.ApiException ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Load failed ({ex.StatusCode})."
                        : $"Load failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    $"Load failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RenderReferToOptions()
        {
            var sorted =
                _referToOptions
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            ReferToOptionsDataGrid.ItemsSource = null;
            ReferToOptionsDataGrid.ItemsSource = sorted;

            ReferToOptionsStatusTextBlock.Text =
                sorted.Count == 0
                    ? "No Refer To options have been added yet."
                    : $"{sorted.Count} Refer To option(s) loaded.";

            UpdateReferToOptionSelectionButtons();
            RefreshDispatchCloseoutWriteUpFlagOptions();
        }

        private async Task<bool> SaveReferToOptionAsync(
            uint id,
            string displayName,
            bool isActive,
            int sortOrder)
        {
            try
            {
                SetBusy(true);

                ReferToOptionsStatusTextBlock.Text =
                    $"Saving {displayName}...";

                await _api.UpdateReferToOptionAsync(
                    id,
                    new SaveReferToOptionRequest
                    {
                        DisplayName = displayName,
                        IsActive = isActive,
                        SortOrder = sortOrder
                    });

                await LoadReferToOptionsAsync();

                ReferToOptionsStatusTextBlock.Text =
                    $"{displayName} saved.";

                return true;
            }
            catch (ApiClient.ApiException ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Save failed ({ex.StatusCode})."
                        : $"Save failed: {ex.Body}";

                return false;
            }
            catch (Exception ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    $"Save failed: {ex.Message}";

                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task AddOrUpdateReferToOptionAsync()
        {
            var name =
                (NewReferToOptionNameTextBox.Text ??
                 string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                ReferToOptionsStatusTextBlock.Text =
                    "Refer To destination name is required.";

                NewReferToOptionNameTextBox.Focus();
                return;
            }

            if (name.Length > 100)
            {
                ReferToOptionsStatusTextBlock.Text =
                    "Refer To destination name is limited to 100 characters.";

                NewReferToOptionNameTextBox.Focus();
                return;
            }

            if (!int.TryParse(
                    NewReferToOptionSortOrderTextBox.Text,
                    out var sortOrder))
            {
                ReferToOptionsStatusTextBlock.Text =
                    "Sort order must be a number.";

                NewReferToOptionSortOrderTextBox.Focus();
                return;
            }

            var isActive =
                NewReferToOptionActiveCheckBox.IsChecked == true;

            if (_editingReferToOptionId is uint editId)
            {
                var saved =
                    await SaveReferToOptionAsync(
                        editId,
                        name,
                        isActive,
                        sortOrder);

                if (saved)
                {
                    ResetReferToOptionForm();
                    SetReferToOptionFormVisible(false);
                }

                return;
            }

            try
            {
                SetBusy(true);

                ReferToOptionsStatusTextBlock.Text =
                    $"Adding {name}...";

                await _api.CreateReferToOptionAsync(
                    new SaveReferToOptionRequest
                    {
                        DisplayName = name,
                        IsActive = isActive,
                        SortOrder = sortOrder
                    });

                ResetReferToOptionForm();
                SetReferToOptionFormVisible(false);

                await LoadReferToOptionsAsync();

                ReferToOptionsStatusTextBlock.Text =
                    $"{name} added.";
            }
            catch (ApiClient.ApiException ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Add failed ({ex.StatusCode})."
                        : $"Add failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    $"Add failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task DeleteReferToOptionAsync(
            ReferToOptionDto option)
        {
            var name =
                string.IsNullOrWhiteSpace(option.DisplayName)
                    ? $"ID {option.Id}"
                    : option.DisplayName.Trim();

            try
            {
                SetBusy(true);

                ReferToOptionsStatusTextBlock.Text =
                    $"Deleting {name}...";

                await _api.DeleteReferToOptionAsync(
                    option.Id);

                if (_editingReferToOptionId == option.Id)
                {
                    ResetReferToOptionForm();
                    SetReferToOptionFormVisible(false);
                }

                await LoadReferToOptionsAsync();

                ReferToOptionsDataGrid.SelectedItem = null;

                ReferToOptionsStatusTextBlock.Text =
                    $"{name} deleted.";
            }
            catch (ApiClient.ApiException ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Delete failed ({ex.StatusCode})."
                        : $"Delete failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    $"Delete failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ResetReferToOptionForm()
        {
            _editingReferToOptionId = null;

            ReferToOptionFormTitleTextBlock.Text =
                "Add Refer To Option";

            ReferToOptionFormHelpTextBlock.Text =
                "Create a new Refer To department or work group.";

            AddReferToOptionButton.Content = "Add";

            NewReferToOptionNameTextBox.Clear();
            NewReferToOptionSortOrderTextBox.Text = "100";
            NewReferToOptionActiveCheckBox.IsChecked = true;
        }

        private void SetReferToOptionFormVisible(
            bool visible)
        {
            AddReferToOptionCard.Visibility =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ToggleAddReferToOptionButton.Content =
                visible
                    ? "Hide New Option"
                    : "+ New Option";

            if (visible)
            {
                NewReferToOptionNameTextBox.Focus();
                NewReferToOptionNameTextBox.SelectAll();
            }
        }

        private void LoadReferToOptionIntoForm(
            ReferToOptionDto selected)
        {
            _editingReferToOptionId = selected.Id;

            ReferToOptionFormTitleTextBlock.Text =
                "Edit Refer To Option";

            ReferToOptionFormHelpTextBlock.Text =
                $"Editing ID {selected.Id}. Make changes below, then save.";

            AddReferToOptionButton.Content = "Save";

            NewReferToOptionNameTextBox.Text =
                selected.DisplayName ?? string.Empty;

            NewReferToOptionSortOrderTextBox.Text =
                selected.SortOrder.ToString();

            NewReferToOptionActiveCheckBox.IsChecked =
                selected.IsActive;

            SetReferToOptionFormVisible(true);
        }

        private void UpdateReferToOptionSelectionButtons()
        {
            var selected =
                ReferToOptionsDataGrid.SelectedItem
                    as ReferToOptionDto;

            var hasSelection = selected is not null;

            EditSelectedReferToOptionButton.IsEnabled =
                !_loading && hasSelection;

            DeleteSelectedReferToOptionButton.IsEnabled =
                !_loading && hasSelection;

            var ordered =
                _referToOptions
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            var selectedIndex =
                selected is null
                    ? -1
                    : ordered.FindIndex(
                        x => x.Id == selected.Id);

            MoveReferToOptionUpButton.IsEnabled =
                !_loading &&
                selectedIndex > 0;

            MoveReferToOptionDownButton.IsEnabled =
                !_loading &&
                selectedIndex >= 0 &&
                selectedIndex < ordered.Count - 1;
        }

        private async Task MoveSelectedReferToOptionAsync(
            int direction)
        {
            if (ReferToOptionsDataGrid.SelectedItem
                is not ReferToOptionDto selected)
            {
                ReferToOptionsStatusTextBlock.Text =
                    "Select a Refer To option to move.";

                return;
            }

            var ordered =
                _referToOptions
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            var currentIndex =
                ordered.FindIndex(
                    x => x.Id == selected.Id);

            var targetIndex = currentIndex + direction;

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

                ReferToOptionsStatusTextBlock.Text =
                    $"Reordering {selected.DisplayName}...";

                for (var index = 0;
                     index < ordered.Count;
                     index++)
                {
                    var option = ordered[index];
                    var desiredSortOrder = (index + 1) * 10;

                    if (option.SortOrder == desiredSortOrder)
                        continue;

                    await _api.UpdateReferToOptionAsync(
                        option.Id,
                        new SaveReferToOptionRequest
                        {
                            DisplayName = option.DisplayName,
                            IsActive = option.IsActive,
                            SortOrder = desiredSortOrder
                        });
                }

                await LoadReferToOptionsAsync();

                var refreshedSelection =
                    _referToOptions.FirstOrDefault(
                        x => x.Id == selected.Id);

                if (refreshedSelection is not null)
                {
                    ReferToOptionsDataGrid.SelectedItem =
                        refreshedSelection;

                    ReferToOptionsDataGrid.ScrollIntoView(
                        refreshedSelection);
                }

                ReferToOptionsStatusTextBlock.Text =
                    $"{selected.DisplayName} reordered.";
            }
            catch (ApiClient.ApiException ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Reorder failed ({ex.StatusCode})."
                        : $"Reorder failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                ReferToOptionsStatusTextBlock.Text =
                    $"Reorder failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ReloadReferToOptionsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await LoadReferToOptionsAsync();
        }

        private async void AddReferToOptionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await AddOrUpdateReferToOptionAsync();
        }

        private void ToggleAddReferToOptionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (AddReferToOptionCard.Visibility ==
                Visibility.Visible)
            {
                SetReferToOptionFormVisible(false);
                return;
            }

            ResetReferToOptionForm();
            SetReferToOptionFormVisible(true);
        }

        private void CancelAddReferToOptionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            ResetReferToOptionForm();
            SetReferToOptionFormVisible(false);
        }

        private void EditSelectedReferToOptionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (ReferToOptionsDataGrid.SelectedItem
                is not ReferToOptionDto selected)
            {
                ReferToOptionsStatusTextBlock.Text =
                    "Select a Refer To option to edit.";

                return;
            }

            LoadReferToOptionIntoForm(selected);
        }

        private async void DeleteSelectedReferToOptionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (ReferToOptionsDataGrid.SelectedItem
                is not ReferToOptionDto selected)
            {
                ReferToOptionsStatusTextBlock.Text =
                    "Select a Refer To option to delete.";

                return;
            }

            var name =
                string.IsNullOrWhiteSpace(selected.DisplayName)
                    ? $"ID {selected.Id}"
                    : selected.DisplayName.Trim();

            var confirm =
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"Delete Refer To option \"{name}\"?\n\n" +
                    "This removes it from the selectable Refer To list.",
                    "Delete Refer To Option",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            await DeleteReferToOptionAsync(selected);
        }

        private async void MoveReferToOptionUpButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await MoveSelectedReferToOptionAsync(-1);
        }

        private async void MoveReferToOptionDownButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await MoveSelectedReferToOptionAsync(1);
        }

        private void ReferToOptionsDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateReferToOptionSelectionButtons();
        }
    }
}
