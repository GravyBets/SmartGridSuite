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

        private bool _isDirty;
        private bool _suppressDirtyTracking;
        private bool _suppressProfileSelection;
        private ulong _loadedProfileId;

        private bool _editorUnlocked;


        public SnmpAdminView()
            : this(new ApiClient("https://localhost:7140"))
        {
        }

        public SnmpAdminView(ApiClient api)
        {
            InitializeComponent();
            _api = api;

            _suppressDirtyTracking = true;
            try
            {
                HookEvents();
                SetDefaults();
                UpdateSnmpVersionUi();
                SetProfileEditorUnlocked(false);
            }
            finally
            {
                _suppressDirtyTracking = false;
            }

            ClearDirty();
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
            OidSearchTextBox.TextChanged += OidSearchTextBox_TextChanged;

            ProfileNameTextBox.TextChanged += EditorChanged;
            ProfileIsActiveCheckBox.Checked += EditorChanged;
            ProfileIsActiveCheckBox.Unchecked += EditorChanged;

            TimeoutMsTextBox.TextChanged += EditorChanged;
            RetriesTextBox.TextChanged += EditorChanged;

            SnmpVersionComboBox.SelectionChanged += SnmpVersionComboBox_SelectionChanged;

            ReadCommunityTextBox.TextChanged += EditorChanged;
            WriteCommunityTextBox.TextChanged += EditorChanged;
            ContextNameTextBox.TextChanged += EditorChanged;

            UsmUserTextBox.TextChanged += EditorChanged;
            AuthProtocolComboBox.SelectionChanged += EditorChanged;
            PrivacyProtocolComboBox.SelectionChanged += EditorChanged;
            AuthKeyPasswordBox.PasswordChanged += EditorChanged;
            PrivacyKeyPasswordBox.PasswordChanged += EditorChanged;

            DeleteProfileButton.Click += DeleteProfileButton_Click;
        }

        private void SetDefaults()
        {
            _suppressDirtyTracking = true;

            try
            {
                ProfileIdTextBlock.Text = "(new)";
                SnmpStatusTextBlock.Text = "Ready.";

                if (AuthProtocolComboBox.SelectedIndex < 0)
                    AuthProtocolComboBox.SelectedIndex = 0;

                if (PrivacyProtocolComboBox.SelectedIndex < 0)
                    PrivacyProtocolComboBox.SelectedIndex = 0;

                TimeoutMsTextBox.Text = "1500";
                RetriesTextBox.Text = "1";

                RefreshOidGrid();
            }
            finally
            {
                _suppressDirtyTracking = false;
            }

            ClearDirty();
        }

        private async void SnmpAdminView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SnmpAdminView_Loaded;
            await LoadProfilesAsync();
            ClearDirty();
        }

        private async void RefreshProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmPendingChangesAsync())
                return;

            await LoadProfilesAsync();
        }

        private async void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmPendingChangesAsync())
                return;

            ProfilesDataGrid.SelectedItem = null;
            ClearEditorForNewProfile();
            SetProfileEditorUnlocked(true);
            SnmpStatusTextBlock.Text = "New SNMP profile.";
        }

        private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            await SaveCurrentProfileAsync();
        }

        private async Task<bool> SaveCurrentProfileAsync()
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
                    return false;
                }

                _currentProfileId = saved.Id;
                _loadedProfileId = saved.Id;

                LoadProfileIntoEditor(saved);
                await LoadProfilesAsync(selectProfileId: saved.Id);

                ClearDirty();
                SnmpStatusTextBlock.Text = $"Saved profile {saved.Name}.";
                return true;
            }
            catch (Exception ex)
            {
                SnmpStatusTextBlock.Text = $"Save failed: {ex.Message}";
                return false;
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void DeactivateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmPendingChangesAsync())
                return;

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
                SetProfileEditorUnlocked(false);

                ClearDirty();
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

        private async void AddOidButton_Click(object sender, RoutedEventArgs e)
        {
            var nextSort = _currentOids.Count == 0
                ? 10
                : ((_currentOids.Max(x => x.SortOrder) / 10) + 1) * 10;

            var window = new SnmpOidEditorWindow
            {
                Owner = Window.GetWindow(this)
            };

            window.LoadOid(null, nextSort);

            if (window.ShowDialog() != true || window.Result is null)
                return;

            _currentOids.Add(window.Result);
            RefreshOidGrid();

            SnmpStatusTextBlock.Text = "OID added. Auto-saving profile...";

            await AutoSaveProfileAfterOidChangeAsync("OID added and profile saved.");
        }

        private async void EditOidButton_Click(object sender, RoutedEventArgs e)
        {
            await EditSelectedOidAsync();
        }

        private async void OidConfigDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            await EditSelectedOidAsync();
        }

        private async Task EditSelectedOidAsync()
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
                    .ToList(),

                // Preserve formula decoder settings when editing an existing OID.
                ReadFormula = selected.ReadFormula,
                WriteFormula = selected.WriteFormula,
                DecimalPlaces = selected.DecimalPlaces,
                UnitLabel = selected.UnitLabel
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

            // Copy formula decoder settings back into the in-memory profile editor.
            selected.ReadFormula = window.Result.ReadFormula;
            selected.WriteFormula = window.Result.WriteFormula;
            selected.DecimalPlaces = window.Result.DecimalPlaces;
            selected.UnitLabel = window.Result.UnitLabel;

            RefreshOidGrid();

            SnmpStatusTextBlock.Text = "OID updated. Auto-saving profile...";

            await AutoSaveProfileAfterOidChangeAsync("OID updated and profile saved.");
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

            MarkDirty();
        }

        private async void ProfilesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingProfile || _suppressProfileSelection)
                return;

            if (ProfilesDataGrid.SelectedItem is not SnmpProfileListItemDto selected)
                return;

            if (selected.Id == _loadedProfileId)
                return;

            if (!await ConfirmPendingChangesAsync())
            {
                RestoreProfileSelection();
                return;
            }

            try
            {
                _isLoadingProfile = true;
                SetBusy($"Loading {selected.Name}...");

                var detail = await _api.GetAsync<SnmpProfileDetailDto>(
                    $"api/snmp-profiles/{selected.Id}");

                if (detail is null)
                {
                    SnmpStatusTextBlock.Text = "Profile load failed.";
                    RestoreProfileSelection();
                    return;
                }

                LoadProfileIntoEditor(detail);
                SnmpStatusTextBlock.Text = $"Loaded {detail.Name}.";
            }
            catch (Exception ex)
            {
                SnmpStatusTextBlock.Text = $"Profile load failed: {ex.Message}";
                RestoreProfileSelection();
            }
            finally
            {
                _isLoadingProfile = false;
                ClearBusy();
            }
        }

        private void RestoreProfileSelection()
        {
            _suppressProfileSelection = true;

            try
            {
                var items = ProfilesDataGrid.ItemsSource as IEnumerable<SnmpProfileListItemDto>;
                if (items is null)
                {
                    ProfilesDataGrid.SelectedItem = null;
                    return;
                }

                var match = items.FirstOrDefault(x => x.Id == _loadedProfileId);
                ProfilesDataGrid.SelectedItem = match;
            }
            finally
            {
                _suppressProfileSelection = false;
            }
        }

        private void ProfileSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyProfileFilter();
        }

        private void OidSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshOidGrid();
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
                    (x.SnmpVersion ?? string.Empty).Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            ProfilesDataGrid.ItemsSource = filtered.ToList();
        }

        private void LoadProfileIntoEditor(SnmpProfileDetailDto detail)
        {
            _suppressDirtyTracking = true;

            try
            {
                _currentProfileId = detail.Id;
                _loadedProfileId = detail.Id;
                ProfileIdTextBlock.Text = detail.Id.ToString();

                ProfileNameTextBox.Text = detail.Name;

                ProfileIsActiveCheckBox.IsChecked = detail.IsActive;

                TimeoutMsTextBox.Text = detail.TimeoutMs.ToString();
                RetriesTextBox.Text = detail.Retries.ToString();

                ReadCommunityTextBox.Text = detail.ReadCommunity ?? string.Empty;
                WriteCommunityTextBox.Text = detail.WriteCommunity ?? string.Empty;
                ContextNameTextBox.Text = detail.ContextName ?? string.Empty;

                SetComboText(SnmpVersionComboBox, detail.SnmpVersion ?? "v3");
                UpdateSnmpVersionUi();

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
            finally
            {
                _suppressDirtyTracking = false;
            }

            SetProfileEditorUnlocked(true);
            ClearDirty();
        }

        private void ClearEditorForNewProfile()
        {
            _suppressDirtyTracking = true;

            try
            {
                _currentProfileId = 0;
                _loadedProfileId = 0;
                ProfileIdTextBlock.Text = "(new)";

                ProfileNameTextBox.Text = string.Empty;

                ProfileIsActiveCheckBox.IsChecked = true;

                TimeoutMsTextBox.Text = "1500";
                RetriesTextBox.Text = "1";

                ReadCommunityTextBox.Text = string.Empty;
                WriteCommunityTextBox.Text = string.Empty;
                ContextNameTextBox.Text = string.Empty;

                SetComboText(SnmpVersionComboBox, "v3");
                UpdateSnmpVersionUi();

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
            finally
            {
                _suppressDirtyTracking = false;
            }

            ClearDirty();
        }

        private void RefreshOidGrid()
        {
            var search = OidSearchTextBox is null
                ? string.Empty
                : (OidSearchTextBox.Text ?? string.Empty).Trim();

            IEnumerable<SnmpOidConfigDto> filtered = _currentOids;

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(x => OidMatchesSearch(x, search));
            }

            OidConfigDataGrid.ItemsSource = null;
            OidConfigDataGrid.ItemsSource = filtered
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Label)
                .ToList();
        }

        private static bool OidMatchesSearch(SnmpOidConfigDto oid, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;

            return ContainsSearch(oid.Category, search) ||
                   ContainsSearch(oid.Label, search) ||
                   ContainsSearch(oid.Oid, search) ||
                   ContainsSearch(oid.ValueType, search) ||
                   ContainsSearch(oid.DecodeMode, search) ||
                   ContainsSearch(oid.ReadFormula, search) ||
                   ContainsSearch(oid.WriteFormula, search) ||
                   ContainsSearch(oid.UnitLabel, search) ||
                   ContainsSearch(oid.SortOrder.ToString(), search);
        }

        private static bool ContainsSearch(string? value, string search)
        {
            return (value ?? string.Empty)
                .Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        private async Task AutoSaveProfileAfterOidChangeAsync(string successMessage)
        {
            // Keep the existing dirty workflow intact first.
            // If save fails, _isDirty stays true so the user still gets the unsaved-changes warning.
            MarkDirty();

            var saved = await SaveCurrentProfileAsync();

            if (saved)
            {
                SnmpStatusTextBlock.Text = successMessage;
                return;
            }

            // SaveCurrentProfileAsync already writes the detailed failure message.
            // Do not clear dirty state here.
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
                DeviceFamily = "GENERAL",
                IsActive = ProfileIsActiveCheckBox.IsChecked == true,

                SnmpVersion = GetComboText(SnmpVersionComboBox, "v3"),

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

                    // Send formula decoder settings to the API with the OID config.
                    ReadFormula = x.ReadFormula,
                    WriteFormula = x.WriteFormula,
                    DecimalPlaces = x.DecimalPlaces,
                    UnitLabel = x.UnitLabel,

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
            EditOidButton.IsEnabled = false;
            RemoveOidButton.IsEnabled = false;
            DeleteProfileButton.IsEnabled = false;
        }

        private void ClearBusy()
        {
            RefreshProfilesButton.IsEnabled = true;
            NewProfileButton.IsEnabled = true;
            SaveProfileButton.IsEnabled = true;
            DeactivateProfileButton.IsEnabled = true;
            AddOidButton.IsEnabled = true;
            EditOidButton.IsEnabled = true;
            RemoveOidButton.IsEnabled = true;
            DeleteProfileButton.IsEnabled = true;
        }

        private static string? CleanNullable(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

        public async Task<bool> ConfirmPendingChangesAsync()
        {
            if (!_isDirty)
                return true;

            var result = MessageBox.Show(
                "You have unsaved SNMP profile changes. Save before continuing?",
                "Unsaved SNMP Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.No)
            {
                ClearDirty();
                return true;
            }

            return await SaveCurrentProfileAsync();
        }

        private void SnmpVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSnmpVersionUi();
            EditorChanged(sender, e);
        }

        private void UpdateSnmpVersionUi()
        {
            if (ReadCommunityTextBox is null ||
                WriteCommunityTextBox is null ||
                ContextNameTextBox is null ||
                UsmUserTextBox is null ||
                AuthProtocolComboBox is null ||
                AuthKeyPasswordBox is null ||
                PrivacyProtocolComboBox is null ||
                PrivacyKeyPasswordBox is null)
            {
                return;
            }

            var version = GetComboText(SnmpVersionComboBox, "v3");

            var isV2c = string.Equals(version, "v2c", StringComparison.OrdinalIgnoreCase);
            var isV3 = string.Equals(version, "v3", StringComparison.OrdinalIgnoreCase);

            ReadCommunityTextBox.IsEnabled = isV2c;
            WriteCommunityTextBox.IsEnabled = isV2c;

            ContextNameTextBox.IsEnabled = isV3;
            UsmUserTextBox.IsEnabled = isV3;
            AuthProtocolComboBox.IsEnabled = isV3;
            AuthKeyPasswordBox.IsEnabled = isV3;
            PrivacyProtocolComboBox.IsEnabled = isV3;
            PrivacyKeyPasswordBox.IsEnabled = isV3;
        }

        private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await ConfirmPendingChangesAsync())
                return;

            if (_currentProfileId == 0)
            {
                MessageBox.Show(
                    "Select a profile first.",
                    "Delete SNMP Profile",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var profileName = (ProfileNameTextBox.Text ?? string.Empty).Trim();

            var confirm = MessageBox.Show(
                $"Delete SNMP profile{(string.IsNullOrWhiteSpace(profileName) ? "" : $" \"{profileName}\"")}?\n\nThis will also remove its OIDs from the admin list.",
                "Delete SNMP Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                SetBusy("Deleting SNMP profile...");

                await _api.PostAsync<object, object>(
                    $"api/snmp-profiles/{_currentProfileId}/delete",
                    new { });

                await LoadProfilesAsync();
                ClearEditorForNewProfile();
                SetProfileEditorUnlocked(false);

                ClearDirty();
                SnmpStatusTextBlock.Text = "Profile deleted.";
            }
            catch (Exception ex)
            {
                SnmpStatusTextBlock.Text = $"Delete failed: {ex.Message}";
            }
            finally
            {
                ClearBusy();
            }
        }

        //If Dirty Handlers
        private void EditorChanged(object? sender, EventArgs e)
        {
            if (_suppressDirtyTracking)
                return;

            MarkDirty();
        }

        private void MarkDirty()
        {
            _isDirty = true;
            UpdateDirtyStatus();
        }

        private void ClearDirty()
        {
            _isDirty = false;
            UpdateDirtyStatus();
        }

        private void UpdateDirtyStatus()
        {
            if (_isDirty)
                SnmpStatusTextBlock.Text = "Unsaved changes.";
            else if (string.IsNullOrWhiteSpace(SnmpStatusTextBlock.Text) || SnmpStatusTextBlock.Text == "Unsaved changes.")
                SnmpStatusTextBlock.Text = "Ready.";
        }

        private void SetProfileEditorUnlocked(bool unlocked)
        {
            _editorUnlocked = unlocked;

            if (ProfileEditorOverlay is null)
                return;

            ProfileEditorOverlay.Visibility = unlocked
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}