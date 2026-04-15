using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Snmp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartGridSuite.Client.Views.Administration.SNMP
{
    public partial class SnmpAdminView : UserControl
    {
        private readonly ApiClient _api;

        private readonly List<SnmpProfileListItemDto> _allProfiles = new();
        private readonly List<SnmpOidConfigDto> _currentOids = new();

        private ulong _currentProfileId;        
        private bool _isLoadingProfile;
        private string? _loadedAuthKey;
        private string? _loadedPrivacyKey;

        
        public SnmpAdminView()
            : this(new ApiClient("https://localhost:7140"))
        {
        }

        public SnmpAdminView(ApiClient api)
        {
            InitializeComponent();
            _api = api;

            HookEvents();
            SetDefaults();

            Loaded += SnmpAdminView_Loaded;
        }

        private void HookEvents()
        {
            RefreshProfilesButton.Click += RefreshProfilesButton_Click;
            NewProfileButton.Click += NewProfileButton_Click;

            SaveProfileButton.Click += SaveProfileButton_Click;
            DeactivateProfileButton.Click += DeactivateProfileButton_Click;

            AddOidButton.Click += AddOidButton_Click;
            EditOidButton.Click += EditOidButton_Click;
            RemoveOidButton.Click += RemoveOidButton_Click;
            OidConfigDataGrid.MouseDoubleClick += OidConfigDataGrid_MouseDoubleClick;

            ProfilesDataGrid.SelectionChanged += ProfilesDataGrid_SelectionChanged;
            
            ProfileSearchTextBox.TextChanged += ProfileSearchTextBox_TextChanged;
        }

        private void SetDefaults()
        {
            ProfileIdTextBlock.Text = "(new)";
            SnmpStatusTextBlock.Text = "Ready.";

            if (DeviceFamilyComboBox.SelectedIndex < 0)
                DeviceFamilyComboBox.SelectedIndex = 0;

            if (AuthProtocolComboBox.SelectedIndex < 0)
                AuthProtocolComboBox.SelectedIndex = 0;

            if (PrivacyProtocolComboBox.SelectedIndex < 0)
                PrivacyProtocolComboBox.SelectedIndex = 0;            

            TimeoutMsTextBox.Text = "1500";
            RetriesTextBox.Text = "1";

            RefreshOidGrid();
        }

        private async void SnmpAdminView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SnmpAdminView_Loaded;
            await LoadProfilesAsync();
        }

        private async void RefreshProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadProfilesAsync();
        }

        private void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ProfilesDataGrid.SelectedItem = null;
            ClearEditorForNewProfile();
            SnmpStatusTextBlock.Text = "New SNMP profile.";
        }

        private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy("Saving SNMP profile...");

                var request = BuildSaveRequest();
                var saved = await _api.PostAsync<UpsertSnmpProfileRequest, SnmpProfileDetailDto>(
                    "api/snmp-profiles/save",
                    request);

                if (saved is null)
                {
                    SnmpStatusTextBlock.Text = "Save failed.";
                    return;
                }

                _currentProfileId = saved.Id;
                LoadProfileIntoEditor(saved);

                await LoadProfilesAsync(selectProfileId: saved.Id);
                SnmpStatusTextBlock.Text = $"Saved profile {saved.Name}.";
            }
            catch (Exception ex)
            {
                SnmpStatusTextBlock.Text = $"Save failed: {ex.Message}";
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void DeactivateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProfileId == 0)
            {
                MessageBox.Show(
                    "Select a profile first.",
                    "Deactivate SNMP Profile",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "Deactivate this SNMP profile?",
                "Deactivate SNMP Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                SetBusy("Deactivating SNMP profile...");

                await _api.PostAsync<object, object>(
                    $"api/snmp-profiles/{_currentProfileId}/deactivate",
                    new { });

                await LoadProfilesAsync();
                ClearEditorForNewProfile();

                SnmpStatusTextBlock.Text = "Profile deactivated.";
            }
            catch (Exception ex)
            {
                SnmpStatusTextBlock.Text = $"Deactivate failed: {ex.Message}";
            }
            finally
            {
                ClearBusy();
            }
        }

        private void AddOidButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new SnmpOidEditorWindow
            {
                Owner = Window.GetWindow(this)
            };

            window.LoadOid(null);

            if (window.ShowDialog() != true || window.Result is null)
                return;

            _currentOids.Add(window.Result);
            RefreshOidGrid();

            SnmpStatusTextBlock.Text = "OID added to profile editor.";
        }

        private void EditOidButton_Click(object sender, RoutedEventArgs e)
        {
            EditSelectedOid();
        }

        private void OidConfigDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            EditSelectedOid();
        }

        private void EditSelectedOid()
        {
            if (OidConfigDataGrid.SelectedItem is not SnmpOidConfigDto selected)
            {
                MessageBox.Show(
                    "Select an OID row first.",
                    "SNMP OID",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var window = new SnmpOidEditorWindow
            {
                Owner = Window.GetWindow(this)
            };

            window.LoadOid(new SnmpOidConfigDto
            {
                Id = selected.Id,
                Category = selected.Category,
                Label = selected.Label,
                Oid = selected.Oid,
                ValueType = selected.ValueType,
                IsWritable = selected.IsWritable,
                ShowInWorkspace = selected.ShowInWorkspace,
                SortOrder = selected.SortOrder,
                DecodeMode = selected.DecodeMode,
                ShowRawValueAlongsideDecoded = selected.ShowRawValueAlongsideDecoded,
                DecodeValues = selected.DecodeValues
                    .Select(x => new SnmpOidDecodeValueDto
                    {
                        Id = x.Id,
                        RawValue = x.RawValue,
                        DisplayText = x.DisplayText,
                        SortOrder = x.SortOrder
                    })
                    .ToList()
            });

            if (window.ShowDialog() != true || window.Result is null)
                return;

            selected.Category = window.Result.Category;
            selected.Label = window.Result.Label;
            selected.Oid = window.Result.Oid;
            selected.ValueType = window.Result.ValueType;
            selected.IsWritable = window.Result.IsWritable;
            selected.ShowInWorkspace = window.Result.ShowInWorkspace;
            selected.SortOrder = window.Result.SortOrder;
            selected.DecodeMode = window.Result.DecodeMode;
            selected.ShowRawValueAlongsideDecoded = window.Result.ShowRawValueAlongsideDecoded;
            selected.DecodeValues = window.Result.DecodeValues;

            RefreshOidGrid();

            SnmpStatusTextBlock.Text = "OID updated in profile editor.";
        }        

        private void RemoveOidButton_Click(object sender, RoutedEventArgs e)
        {
            if (OidConfigDataGrid.SelectedItem is not SnmpOidConfigDto selected)
            {
                MessageBox.Show(
                    "Select an OID row first.",
                    "Remove OID",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _currentOids.Remove(selected);
            RefreshOidGrid();

            SnmpStatusTextBlock.Text = "OID removed from profile editor.";
        }

        private async void ProfilesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingProfile)
                return;

            if (ProfilesDataGrid.SelectedItem is not SnmpProfileListItemDto selected)
                return;

            try
            {
                _isLoadingProfile = true;
                SetBusy($"Loading {selected.Name}...");

                var detail = await _api.GetAsync<SnmpProfileDetailDto>(
                    $"api/snmp-profiles/{selected.Id}");

                if (detail is null)
                {
                    SnmpStatusTextBlock.Text = "Profile load failed.";
                    return;
                }

                LoadProfileIntoEditor(detail);
                SnmpStatusTextBlock.Text = $"Loaded {detail.Name}.";
            }
            catch (Exception ex)
            {
                SnmpStatusTextBlock.Text = $"Profile load failed: {ex.Message}";
            }
            finally
            {
                _isLoadingProfile = false;
                ClearBusy();
            }
        }        

        private void ProfileSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyProfileFilter();
        }

        private async Task LoadProfilesAsync(ulong? selectProfileId = null)
        {
            try
            {
                SetBusy("Loading SNMP profiles...");

                var rows = await _api.GetAsync<List<SnmpProfileListItemDto>>("api/snmp-profiles")
                           ?? new List<SnmpProfileListItemDto>();

                _allProfiles.Clear();
                _allProfiles.AddRange(rows);

                ApplyProfileFilter();

                if (selectProfileId.HasValue)
                {
                    var selected = ((IEnumerable<SnmpProfileListItemDto>)ProfilesDataGrid.ItemsSource)
                        .FirstOrDefault(x => x.Id == selectProfileId.Value);

                    if (selected is not null)
                        ProfilesDataGrid.SelectedItem = selected;
                }

                SnmpStatusTextBlock.Text = "Profiles loaded.";
            }
            catch (Exception ex)
            {
                SnmpStatusTextBlock.Text = $"Load failed: {ex.Message}";
            }
            finally
            {
                ClearBusy();
            }
        }

        private void ApplyProfileFilter()
        {
            var search = (ProfileSearchTextBox.Text ?? string.Empty).Trim();

            IEnumerable<SnmpProfileListItemDto> filtered = _allProfiles;

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(x =>
                    x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    x.DeviceFamily.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            ProfilesDataGrid.ItemsSource = filtered.ToList();
        }

        private void LoadProfileIntoEditor(SnmpProfileDetailDto detail)
        {
            _currentProfileId = detail.Id;
            ProfileIdTextBlock.Text = detail.Id.ToString();

            ProfileNameTextBox.Text = detail.Name;
            SetComboText(DeviceFamilyComboBox, detail.DeviceFamily);

            ProfileIsActiveCheckBox.IsChecked = detail.IsActive;
            ProfileIsDefaultCheckBox.IsChecked = detail.IsDefaultForFamily;

            TimeoutMsTextBox.Text = detail.TimeoutMs.ToString();
            RetriesTextBox.Text = detail.Retries.ToString();

            ReadCommunityTextBox.Text = detail.ReadCommunity ?? string.Empty;
            WriteCommunityTextBox.Text = detail.WriteCommunity ?? string.Empty;
            ContextNameTextBox.Text = detail.ContextName ?? string.Empty;

            UsmUserTextBox.Text = detail.UsmUser ?? string.Empty;
            SetComboText(AuthProtocolComboBox, detail.AuthProtocol ?? "MD5");
            SetComboText(PrivacyProtocolComboBox, detail.PrivacyProtocol ?? "DES");

            _loadedAuthKey = detail.AuthKey;
            _loadedPrivacyKey = detail.PrivacyKey;
            AuthKeyPasswordBox.Password = string.Empty;
            PrivacyKeyPasswordBox.Password = string.Empty;

            _currentOids.Clear();
            _currentOids.AddRange(detail.Oids.OrderBy(x => x.SortOrder).ThenBy(x => x.Label));

            RefreshOidGrid();            
        }

        private void ClearEditorForNewProfile()
        {
            _currentProfileId = 0;
            ProfileIdTextBlock.Text = "(new)";

            ProfileNameTextBox.Text = string.Empty;
            SetComboText(DeviceFamilyComboBox, "RF700");

            ProfileIsActiveCheckBox.IsChecked = true;
            ProfileIsDefaultCheckBox.IsChecked = false;

            TimeoutMsTextBox.Text = "1500";
            RetriesTextBox.Text = "1";

            ReadCommunityTextBox.Text = string.Empty;
            WriteCommunityTextBox.Text = string.Empty;
            ContextNameTextBox.Text = string.Empty;

            UsmUserTextBox.Text = string.Empty;
            SetComboText(AuthProtocolComboBox, "MD5");
            SetComboText(PrivacyProtocolComboBox, "DES");

            _loadedAuthKey = null;
            _loadedPrivacyKey = null;
            AuthKeyPasswordBox.Password = string.Empty;
            PrivacyKeyPasswordBox.Password = string.Empty;

            _currentOids.Clear();
            RefreshOidGrid();           
        }        

        private void RefreshOidGrid()
        {
            OidConfigDataGrid.ItemsSource = null;
            OidConfigDataGrid.ItemsSource = _currentOids
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Label)
                .ToList();
        }

        private UpsertSnmpProfileRequest BuildSaveRequest()
        {
            if (!int.TryParse((TimeoutMsTextBox.Text ?? "1500").Trim(), out var timeoutMs))
                timeoutMs = 1500;

            if (!int.TryParse((RetriesTextBox.Text ?? "1").Trim(), out var retries))
                retries = 1;

            return new UpsertSnmpProfileRequest
            {
                Id = _currentProfileId > 0 ? _currentProfileId : null,
                Name = (ProfileNameTextBox.Text ?? string.Empty).Trim(),
                DeviceFamily = GetComboText(DeviceFamilyComboBox, "RF700"),
                IsActive = ProfileIsActiveCheckBox.IsChecked == true,
                IsDefaultForFamily = ProfileIsDefaultCheckBox.IsChecked == true,

                ReadCommunity = CleanNullable(ReadCommunityTextBox.Text),
                WriteCommunity = CleanNullable(WriteCommunityTextBox.Text),
                ContextName = CleanNullable(ContextNameTextBox.Text),

                UsmUser = CleanNullable(UsmUserTextBox.Text),
                AuthProtocol = GetComboText(AuthProtocolComboBox, "MD5"),
                AuthKey = string.IsNullOrWhiteSpace(AuthKeyPasswordBox.Password)
                    ? _loadedAuthKey
                    : AuthKeyPasswordBox.Password,

                PrivacyProtocol = GetComboText(PrivacyProtocolComboBox, "DES"),
                PrivacyKey = string.IsNullOrWhiteSpace(PrivacyKeyPasswordBox.Password)
                    ? _loadedPrivacyKey
                    : PrivacyKeyPasswordBox.Password,

                TimeoutMs = timeoutMs,
                Retries = retries,

                Oids = _currentOids.Select(x => new UpsertSnmpOidRequest
                {
                    Id = x.Id > 0 ? x.Id : null,
                    Category = x.Category,
                    Label = x.Label,
                    Oid = x.Oid,
                    ValueType = x.ValueType,
                    IsWritable = x.IsWritable,
                    ShowInWorkspace = x.ShowInWorkspace,
                    SortOrder = x.SortOrder,
                    DecodeMode = x.DecodeMode,
                    ShowRawValueAlongsideDecoded = x.ShowRawValueAlongsideDecoded,
                    DecodeValues = x.DecodeValues.Select(d => new UpsertSnmpOidDecodeValueRequest
                    {
                        Id = d.Id > 0 ? d.Id : null,
                        RawValue = d.RawValue,
                        DisplayText = d.DisplayText,
                        SortOrder = d.SortOrder
                    }).ToList()
                }).ToList()
            };
        }

        private void SetBusy(string message)
        {
            SnmpStatusTextBlock.Text = message;

            RefreshProfilesButton.IsEnabled = false;
            NewProfileButton.IsEnabled = false;
            SaveProfileButton.IsEnabled = false;
            DeactivateProfileButton.IsEnabled = false;
            AddOidButton.IsEnabled = false;            
            RemoveOidButton.IsEnabled = false;
        }

        private void ClearBusy()
        {
            RefreshProfilesButton.IsEnabled = true;
            NewProfileButton.IsEnabled = true;
            SaveProfileButton.IsEnabled = true;
            DeactivateProfileButton.IsEnabled = true;
            AddOidButton.IsEnabled = true;            
            RemoveOidButton.IsEnabled = true;
        }

        private static string? CleanNullable(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string CleanOrDefault(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string GetComboText(ComboBox comboBox, string fallback)
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Content is string itemText)
                return itemText;

            if (comboBox.Text is { Length: > 0 } text)
                return text.Trim();

            return fallback;
        }

        private static void SetComboText(ComboBox comboBox, string value)
        {
            var match = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Content?.ToString(),
                        value,
                        StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                comboBox.SelectedItem = match;
                return;
            }

            comboBox.Text = value;
        }

        
    }
}