using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Settings;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Administration.WriteUpWorkflow
{
    public partial class WriteUpWorkflowAdminView
    {
        private List<DispatchCloseoutChecklistDefinitionDto>
            _dispatchCloseoutChecklistDefinitions = new();

        private uint? _editingDispatchCloseoutChecklistDefinitionId;

        private sealed class DispatchCloseoutTriggerOption
        {
            public uint Id { get; init; }

            public string DisplayName { get; init; } = "";

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private void UpdateDispatchCloseoutChecklistBusyState(
            bool isBusy)
        {
            NewDispatchCloseoutChecklistNameTextBox.IsEnabled = !isBusy;
            NewDispatchCloseoutChecklistSortOrderTextBox.IsEnabled = !isBusy;
            DispatchCloseoutConditionTypeComboBox.IsEnabled = !isBusy;
            NewDispatchCloseoutChecklistActiveCheckBox.IsEnabled = !isBusy;
            NewDispatchCloseoutChecklistRequiredCheckBox.IsEnabled = !isBusy;
            AddDispatchCloseoutChecklistButton.IsEnabled = !isBusy;
            CancelAddDispatchCloseoutChecklistButton.IsEnabled = !isBusy;
            ToggleAddDispatchCloseoutChecklistButton.IsEnabled = !isBusy;
            ReloadDispatchCloseoutChecklistButton.IsEnabled = !isBusy;
            DispatchCloseoutChecklistDataGrid.IsEnabled = !isBusy;

            UpdateDispatchCloseoutConditionControls();
            UpdateDispatchCloseoutChecklistSelectionButtons();
        }

        internal void RefreshDispatchCloseoutWriteUpFlagOptions()
        {
            if (DispatchCloseoutWriteUpFlagComboBox is null)
                return;

            var conditionType =
                DispatchCloseoutConditionTypes.Normalize(
                    DispatchCloseoutConditionTypeComboBox.SelectedItem
                        as string);

            var previousConditionType =
                DispatchCloseoutWriteUpFlagComboBox.Tag
                    as string;

            var preserveSelection =
                string.Equals(
                    previousConditionType,
                    conditionType,
                    StringComparison.OrdinalIgnoreCase);

            var selectedValue =
                preserveSelection
                    ? DispatchCloseoutWriteUpFlagComboBox.SelectedValue
                    : null;

            if (conditionType == DispatchCloseoutConditionTypes.WriteUpFlag)
            {
                DispatchCloseoutWriteUpFlagComboBox.ItemsSource =
                    _writeUpFlags
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.DisplayName)
                        .Select(x =>
                            new DispatchCloseoutTriggerOption
                            {
                                Id = x.Id,
                                DisplayName = x.DisplayName
                            })
                        .ToList();
            }
            else if (conditionType ==
                     DispatchCloseoutConditionTypes.ReferToSelection)
            {
                DispatchCloseoutWriteUpFlagComboBox.ItemsSource =
                    _referToOptions
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.DisplayName)
                        .Select(x =>
                            new DispatchCloseoutTriggerOption
                            {
                                Id = x.Id,
                                DisplayName = x.DisplayName
                            })
                        .ToList();
            }
            else
            {
                DispatchCloseoutWriteUpFlagComboBox.ItemsSource = null;
            }

            DispatchCloseoutWriteUpFlagComboBox.Tag =
                conditionType;

            if (selectedValue is not null)
            {
                DispatchCloseoutWriteUpFlagComboBox.SelectedValue =
                    selectedValue;
            }
            else
            {
                DispatchCloseoutWriteUpFlagComboBox.SelectedItem = null;
            }
        }

        private void EnsureDispatchCloseoutFormSources()
        {
            DispatchCloseoutConditionTypeComboBox.ItemsSource =
                DispatchCloseoutConditionTypes.All;

            RefreshDispatchCloseoutWriteUpFlagOptions();
        }

        private async Task LoadDispatchCloseoutChecklistDefinitionsAsync()
        {
            try
            {
                SetBusy(true);

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    "Loading Dispatch closeout checklist definitions...";

                EnsureDispatchCloseoutFormSources();

                _dispatchCloseoutChecklistDefinitions =
                    await _api
                        .GetDispatchCloseoutChecklistDefinitionsAsync(
                            activeOnly: false);

                RenderDispatchCloseoutChecklistDefinitions();
            }
            catch (ApiClient.ApiException ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Load failed ({ex.StatusCode})."
                        : $"Load failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Load failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RenderDispatchCloseoutChecklistDefinitions()
        {
            var sorted =
                _dispatchCloseoutChecklistDefinitions
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            DispatchCloseoutChecklistDataGrid.ItemsSource = null;
            DispatchCloseoutChecklistDataGrid.ItemsSource = sorted;

            DispatchCloseoutChecklistStatusTextBlock.Text =
                sorted.Count == 0
                    ? "No Dispatch closeout checklist items have been added."
                    : $"{sorted.Count} Dispatch closeout checklist item(s) loaded.";

            UpdateDispatchCloseoutChecklistSelectionButtons();
        }

        private async Task<bool>SaveDispatchCloseoutChecklistDefinitionAsync(
            uint id,
            string displayName,
            bool isActive,
            int sortOrder,
            bool isRequired,
            string conditionType,
            uint? writeUpFlagId,
            uint? referToOptionId)
        {
            try
            {
                SetBusy(true);

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Saving {displayName}...";

                await _api
                    .UpdateDispatchCloseoutChecklistDefinitionAsync(
                        id,
                        new SaveDispatchCloseoutChecklistDefinitionRequest
                        {
                            DisplayName = displayName,
                            IsActive = isActive,
                            SortOrder = sortOrder,
                            IsRequired = isRequired,
                            ConditionType = conditionType,
                            WriteUpFlagId = writeUpFlagId,
                            ReferToOptionId = referToOptionId
                        });

                await LoadDispatchCloseoutChecklistDefinitionsAsync();

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"{displayName} saved.";

                return true;
            }
            catch (ApiClient.ApiException ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Save failed ({ex.StatusCode})."
                        : $"Save failed: {ex.Body}";

                return false;
            }
            catch (Exception ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Save failed: {ex.Message}";

                return false;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool TryReadDispatchCloseoutForm(
            out string displayName,
            out int sortOrder,
            out bool isActive,
            out bool isRequired,
            out string conditionType,
            out uint? writeUpFlagId,
            out uint? referToOptionId)
        {
            displayName =
                (NewDispatchCloseoutChecklistNameTextBox.Text ??
                 string.Empty).Trim();

            sortOrder = 0;
            isActive =
                NewDispatchCloseoutChecklistActiveCheckBox.IsChecked == true;
            isRequired =
                NewDispatchCloseoutChecklistRequiredCheckBox.IsChecked == true;

            conditionType =
                DispatchCloseoutConditionTypes.Normalize(
                    DispatchCloseoutConditionTypeComboBox.SelectedItem
                        as string);

            writeUpFlagId = null;
            referToOptionId = null;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    "Checklist item name is required.";

                NewDispatchCloseoutChecklistNameTextBox.Focus();
                return false;
            }

            if (displayName.Length > 150)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    "Checklist item name is limited to 150 characters.";

                NewDispatchCloseoutChecklistNameTextBox.Focus();
                return false;
            }

            if (!int.TryParse(
                    NewDispatchCloseoutChecklistSortOrderTextBox.Text,
                    out sortOrder))
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    "Sort order must be a number.";

                NewDispatchCloseoutChecklistSortOrderTextBox.Focus();
                return false;
            }

            if (conditionType ==
                DispatchCloseoutConditionTypes.WriteUpFlag)
            {
                if (DispatchCloseoutWriteUpFlagComboBox.SelectedValue
                    is uint selectedFlagId)
                {
                    writeUpFlagId = selectedFlagId;
                }
                else
                {
                    DispatchCloseoutChecklistStatusTextBlock.Text =
                        "Select the write-up flag that triggers this item.";

                    DispatchCloseoutWriteUpFlagComboBox.Focus();
                    return false;
                }
            }
            else if (conditionType == DispatchCloseoutConditionTypes.ReferToSelection)
            {
                if (DispatchCloseoutWriteUpFlagComboBox.SelectedValue
                    is uint selectedReferToOptionId)
                {
                    referToOptionId =
                        selectedReferToOptionId;
                }
                else
                {
                    DispatchCloseoutChecklistStatusTextBlock.Text =
                        "Select the Refer To option that triggers this item.";

                    DispatchCloseoutWriteUpFlagComboBox.Focus();
                    return false;
                }
            }

            return true;
        }

        private async Task AddOrUpdateDispatchCloseoutChecklistAsync()
        {
            if (!TryReadDispatchCloseoutForm(
                    out var displayName,
                    out var sortOrder,
                    out var isActive,
                    out var isRequired,
                    out var conditionType,
                    out var writeUpFlagId,
                    out var referToOptionId))
            {
                return;
            }

            if (_editingDispatchCloseoutChecklistDefinitionId
                is uint editId)
            {
                var saved =
                    await SaveDispatchCloseoutChecklistDefinitionAsync(
                        editId,
                        displayName,
                        isActive,
                        sortOrder,
                        isRequired,
                        conditionType,
                        writeUpFlagId,
                        referToOptionId);

                if (saved)
                {
                    ResetDispatchCloseoutChecklistForm();
                    SetDispatchCloseoutChecklistFormVisible(false);
                }

                return;
            }

            try
            {
                SetBusy(true);

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Adding {displayName}...";

                await _api
                    .CreateDispatchCloseoutChecklistDefinitionAsync(
                        new SaveDispatchCloseoutChecklistDefinitionRequest
                        {
                            DisplayName = displayName,
                            IsActive = isActive,
                            SortOrder = sortOrder,
                            IsRequired = isRequired,
                            ConditionType = conditionType,
                            WriteUpFlagId = writeUpFlagId,
                            ReferToOptionId = referToOptionId
                        });

                ResetDispatchCloseoutChecklistForm();
                SetDispatchCloseoutChecklistFormVisible(false);

                await LoadDispatchCloseoutChecklistDefinitionsAsync();

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"{displayName} added.";
            }
            catch (ApiClient.ApiException ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Add failed ({ex.StatusCode})."
                        : $"Add failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Add failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task DeleteDispatchCloseoutChecklistDefinitionAsync(
            DispatchCloseoutChecklistDefinitionDto definition)
        {
            var name =
                string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? $"ID {definition.Id}"
                    : definition.DisplayName.Trim();

            try
            {
                SetBusy(true);

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Deleting {name}...";

                await _api
                    .DeleteDispatchCloseoutChecklistDefinitionAsync(
                        definition.Id);

                if (_editingDispatchCloseoutChecklistDefinitionId ==
                    definition.Id)
                {
                    ResetDispatchCloseoutChecklistForm();
                    SetDispatchCloseoutChecklistFormVisible(false);
                }

                await LoadDispatchCloseoutChecklistDefinitionsAsync();

                DispatchCloseoutChecklistDataGrid.SelectedItem = null;

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"{name} deleted.";
            }
            catch (ApiClient.ApiException ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Delete failed ({ex.StatusCode})."
                        : $"Delete failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Delete failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ResetDispatchCloseoutChecklistForm()
        {
            _editingDispatchCloseoutChecklistDefinitionId = null;

            DispatchCloseoutChecklistFormTitleTextBlock.Text =
                "Add Dispatch Closeout Checklist Item";

            DispatchCloseoutChecklistFormHelpTextBlock.Text =
                "Create a checklist item that may be required before ticket closure.";

            AddDispatchCloseoutChecklistButton.Content = "Add";

            NewDispatchCloseoutChecklistNameTextBox.Clear();
            NewDispatchCloseoutChecklistSortOrderTextBox.Text = "100";
            NewDispatchCloseoutChecklistActiveCheckBox.IsChecked = true;
            NewDispatchCloseoutChecklistRequiredCheckBox.IsChecked = true;

            EnsureDispatchCloseoutFormSources();

            DispatchCloseoutConditionTypeComboBox.SelectedItem =
                DispatchCloseoutConditionTypes.Always;

            DispatchCloseoutWriteUpFlagComboBox.SelectedItem = null;

            UpdateDispatchCloseoutConditionControls();
        }

        private void SetDispatchCloseoutChecklistFormVisible(
            bool visible)
        {
            AddDispatchCloseoutChecklistCard.Visibility =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ToggleAddDispatchCloseoutChecklistButton.Content =
                visible
                    ? "Hide New Item"
                    : "+ New Item";

            if (visible)
            {
                NewDispatchCloseoutChecklistNameTextBox.Focus();
                NewDispatchCloseoutChecklistNameTextBox.SelectAll();
            }
        }

        private void LoadDispatchCloseoutChecklistIntoForm(
            DispatchCloseoutChecklistDefinitionDto selected)
        {
            _editingDispatchCloseoutChecklistDefinitionId =
                selected.Id;

            DispatchCloseoutChecklistFormTitleTextBlock.Text =
                "Edit Dispatch Closeout Checklist Item";

            DispatchCloseoutChecklistFormHelpTextBlock.Text =
                $"Editing ID {selected.Id}. Make changes below, then save.";

            AddDispatchCloseoutChecklistButton.Content = "Save";

            EnsureDispatchCloseoutFormSources();

            NewDispatchCloseoutChecklistNameTextBox.Text =
                selected.DisplayName ?? string.Empty;

            NewDispatchCloseoutChecklistSortOrderTextBox.Text =
                selected.SortOrder.ToString();

            NewDispatchCloseoutChecklistActiveCheckBox.IsChecked =
                selected.IsActive;

            NewDispatchCloseoutChecklistRequiredCheckBox.IsChecked =
                selected.IsRequired;

            DispatchCloseoutConditionTypeComboBox.SelectedItem =
                DispatchCloseoutConditionTypes.Normalize(
                    selected.ConditionType);

            UpdateDispatchCloseoutConditionControls();

            DispatchCloseoutWriteUpFlagComboBox.SelectedValue =
                selected.WriteUpFlagId ??
                selected.ReferToOptionId;

            SetDispatchCloseoutChecklistFormVisible(true);
        }

        private void UpdateDispatchCloseoutConditionControls()
        {
            var conditionType =
                DispatchCloseoutConditionTypes.Normalize(
                    DispatchCloseoutConditionTypeComboBox.SelectedItem
                        as string);

            var needsTrigger =
                conditionType ==
                    DispatchCloseoutConditionTypes.WriteUpFlag ||
                conditionType ==
                    DispatchCloseoutConditionTypes.ReferToSelection;

            RefreshDispatchCloseoutWriteUpFlagOptions();

            DispatchCloseoutWriteUpFlagComboBox.IsEnabled =
                !_loading &&
                needsTrigger;

            if (!needsTrigger)
            {
                DispatchCloseoutWriteUpFlagComboBox.SelectedItem =
                    null;
            }
        }

        private void UpdateDispatchCloseoutChecklistSelectionButtons()
        {
            var selected =
                DispatchCloseoutChecklistDataGrid.SelectedItem
                    as DispatchCloseoutChecklistDefinitionDto;

            var hasSelection = selected is not null;

            EditSelectedDispatchCloseoutChecklistButton.IsEnabled =
                !_loading && hasSelection;

            DeleteSelectedDispatchCloseoutChecklistButton.IsEnabled =
                !_loading && hasSelection;

            var ordered =
                _dispatchCloseoutChecklistDefinitions
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            var selectedIndex =
                selected is null
                    ? -1
                    : ordered.FindIndex(
                        x => x.Id == selected.Id);

            MoveDispatchCloseoutChecklistUpButton.IsEnabled =
                !_loading &&
                selectedIndex > 0;

            MoveDispatchCloseoutChecklistDownButton.IsEnabled =
                !_loading &&
                selectedIndex >= 0 &&
                selectedIndex < ordered.Count - 1;
        }

        private async Task MoveSelectedDispatchCloseoutChecklistAsync(
            int direction)
        {
            if (DispatchCloseoutChecklistDataGrid.SelectedItem
                is not DispatchCloseoutChecklistDefinitionDto selected)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    "Select a checklist item to move.";

                return;
            }

            var ordered =
                _dispatchCloseoutChecklistDefinitions
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

            var currentIndex =
                ordered.FindIndex(x => x.Id == selected.Id);

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

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Reordering {selected.DisplayName}...";

                for (var index = 0;
                     index < ordered.Count;
                     index++)
                {
                    var definition = ordered[index];
                    var desiredSortOrder = (index + 1) * 10;

                    if (definition.SortOrder == desiredSortOrder)
                        continue;

                    await _api
                        .UpdateDispatchCloseoutChecklistDefinitionAsync(
                            definition.Id,
                            new SaveDispatchCloseoutChecklistDefinitionRequest
                            {
                                DisplayName = definition.DisplayName,
                                IsActive = definition.IsActive,
                                SortOrder = desiredSortOrder,
                                IsRequired = definition.IsRequired,
                                ConditionType = definition.ConditionType,
                                WriteUpFlagId = definition.WriteUpFlagId,
                                ReferToOptionId = definition.ReferToOptionId
                            });
                }

                await LoadDispatchCloseoutChecklistDefinitionsAsync();

                var refreshedSelection =
                    _dispatchCloseoutChecklistDefinitions
                        .FirstOrDefault(x => x.Id == selected.Id);

                if (refreshedSelection is not null)
                {
                    DispatchCloseoutChecklistDataGrid.SelectedItem =
                        refreshedSelection;

                    DispatchCloseoutChecklistDataGrid.ScrollIntoView(
                        refreshedSelection);
                }

                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"{selected.DisplayName} reordered.";
            }
            catch (ApiClient.ApiException ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    string.IsNullOrWhiteSpace(ex.Body)
                        ? $"Reorder failed ({ex.StatusCode})."
                        : $"Reorder failed: {ex.Body}";
            }
            catch (Exception ex)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    $"Reorder failed: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ReloadDispatchCloseoutChecklistButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await LoadDispatchCloseoutChecklistDefinitionsAsync();
        }

        private async void AddDispatchCloseoutChecklistButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await AddOrUpdateDispatchCloseoutChecklistAsync();
        }

        private void ToggleAddDispatchCloseoutChecklistButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (AddDispatchCloseoutChecklistCard.Visibility ==
                Visibility.Visible)
            {
                SetDispatchCloseoutChecklistFormVisible(false);
                return;
            }

            ResetDispatchCloseoutChecklistForm();
            SetDispatchCloseoutChecklistFormVisible(true);
        }

        private void CancelAddDispatchCloseoutChecklistButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            ResetDispatchCloseoutChecklistForm();
            SetDispatchCloseoutChecklistFormVisible(false);
        }

        private void EditSelectedDispatchCloseoutChecklistButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (DispatchCloseoutChecklistDataGrid.SelectedItem
                is not DispatchCloseoutChecklistDefinitionDto selected)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    "Select a checklist item to edit.";

                return;
            }

            LoadDispatchCloseoutChecklistIntoForm(selected);
        }

        private async void DeleteSelectedDispatchCloseoutChecklistButton_Click(
                object sender,
                RoutedEventArgs e)
        {
            if (_loading)
                return;

            if (DispatchCloseoutChecklistDataGrid.SelectedItem
                is not DispatchCloseoutChecklistDefinitionDto selected)
            {
                DispatchCloseoutChecklistStatusTextBlock.Text =
                    "Select a checklist item to delete.";

                return;
            }

            var name =
                string.IsNullOrWhiteSpace(selected.DisplayName)
                    ? $"ID {selected.Id}"
                    : selected.DisplayName.Trim();

            var confirm =
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"Delete Dispatch closeout checklist item \"{name}\"?\n\n" +
                    "This removes it from future ticket closeout checklists.",
                    "Delete Dispatch Closeout Checklist Item",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            await DeleteDispatchCloseoutChecklistDefinitionAsync(
                selected);
        }

        private async void MoveDispatchCloseoutChecklistUpButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await MoveSelectedDispatchCloseoutChecklistAsync(-1);
        }

        private async void MoveDispatchCloseoutChecklistDownButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_loading)
                return;

            await MoveSelectedDispatchCloseoutChecklistAsync(1);
        }

        private void DispatchCloseoutConditionTypeComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateDispatchCloseoutConditionControls();
        }

        private void DispatchCloseoutChecklistDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateDispatchCloseoutChecklistSelectionButtons();
        }
    }
}
