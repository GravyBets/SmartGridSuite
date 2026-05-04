using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartGridSuite.Client.Views.Administration.GeneralSettings
{
    public partial class GeneralSettingsAdminView : UserControl
    {
        private readonly ApiClient _api;
        private bool _loading;
        private List<CommunicationDeviceTypeDto> _deviceTypes = new();

        public GeneralSettingsAdminView(ApiClient api)
        {
            InitializeComponent();
            _api = api;

            Loaded += GeneralSettingsAdminView_Loaded;
        }

        private async void GeneralSettingsAdminView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= GeneralSettingsAdminView_Loaded;

            await LoadIgsdPortalUrlAsync();
            await LoadRangeExtenderLinkUrlAsync();
            await LoadCommunicationDeviceTypesAsync();
        }

        private void SetBusy(bool isBusy)
        {
            _loading = isBusy;

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
            ReloadDeviceTypesButton.IsEnabled = !isBusy;
            DeviceTypesPanel.IsEnabled = !isBusy;
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
            DeviceTypesPanel.Children.Clear();

            foreach (var item in _deviceTypes
                         .OrderBy(x => x.SortOrder)
                         .ThenBy(x => x.DisplayName))
            {
                DeviceTypesPanel.Children.Add(CreateCommunicationDeviceTypeRow(item));
            }

            if (DeviceTypesPanel.Children.Count == 0)
            {
                DeviceTypesPanel.Children.Add(new TextBlock
                {
                    Text = "No communication device types have been added yet.",
                    FontStyle = FontStyles.Italic,
                    Foreground = TryFindResource("TextSecondary") as Brush
                });
            }
        }

        private FrameworkElement CreateCommunicationDeviceTypeRow(CommunicationDeviceTypeDto item)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                BorderBrush = TryFindResource("SurfaceBorder") as Brush,
                BorderThickness = new Thickness(1),
                Background = TryFindResource("SurfaceBg") as Brush
            };

            var grid = new Grid();

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var nameBox = CreateLabeledTextBox("Device Type", item.DisplayName);
            Grid.SetColumn(nameBox, 0);

            var sortBox = CreateLabeledTextBox("Sort Order", item.SortOrder.ToString());
            Grid.SetColumn(sortBox, 2);

            var activeCheckBox = new CheckBox
            {
                Content = "Active",
                IsChecked = item.IsActive,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 7),
                Foreground = TryFindResource("TextPrimary") as Brush
            };
            Grid.SetColumn(activeCheckBox, 4);

            var saveButton = new Button
            {
                Content = "Save",
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumn(saveButton, 6);

            saveButton.Click += async (_, _) =>
            {
                if (_loading)
                    return;

                var name = GetTextBoxText(nameBox).Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    DeviceTypesStatusTextBlock.Text = "Device type name is required.";
                    return;
                }

                if (!int.TryParse(GetTextBoxText(sortBox), out var sortOrder))
                {
                    DeviceTypesStatusTextBlock.Text = "Sort order must be a number.";
                    return;
                }

                await SaveCommunicationDeviceTypeAsync(
                    item.Id,
                    name,
                    activeCheckBox.IsChecked == true,
                    sortOrder);
            };

            grid.Children.Add(nameBox);
            grid.Children.Add(sortBox);
            grid.Children.Add(activeCheckBox);
            grid.Children.Add(saveButton);

            border.Child = grid;
            return border;
        }

        private FrameworkElement CreateLabeledTextBox(string label, string value)
        {
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindResource("TextSecondary") as Brush,
                Margin = new Thickness(0, 0, 0, 3)
            });

            stack.Children.Add(new TextBox
            {
                Text = value,
                Style = (Style)FindResource("ModernTextBox"),
                Height = 32,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            });

            return stack;
        }

        private static string GetTextBoxText(FrameworkElement container)
        {
            if (container is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBox textBox)
                        return textBox.Text ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private async Task SaveCommunicationDeviceTypeAsync(
            uint id,
            string displayName,
            bool isActive,
            int sortOrder)
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

        private async Task AddCommunicationDeviceTypeAsync()
        {
            var name = (NewDeviceTypeNameTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                DeviceTypesStatusTextBlock.Text = "Device type name is required.";
                return;
            }

            if (!int.TryParse(NewDeviceTypeSortOrderTextBox.Text, out var sortOrder))
            {
                DeviceTypesStatusTextBlock.Text = "Sort order must be a number.";
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
                        IsActive = NewDeviceTypeActiveCheckBox.IsChecked == true,
                        SortOrder = sortOrder
                    });

                NewDeviceTypeNameTextBox.Clear();
                NewDeviceTypeSortOrderTextBox.Text = "100";
                NewDeviceTypeActiveCheckBox.IsChecked = true;

                DeviceTypesStatusTextBlock.Text = $"{name} added.";

                await LoadCommunicationDeviceTypesAsync();
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
    }
}