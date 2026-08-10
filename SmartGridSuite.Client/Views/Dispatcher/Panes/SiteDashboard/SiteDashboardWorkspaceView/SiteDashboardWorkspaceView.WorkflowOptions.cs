#nullable enable
using SmartGridSuite.Client.Services;
using SmartGridSuite.Contracts.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard
{
    public partial class SiteDashboardWorkspaceView
    {
        private readonly ApiClient _writeUpWorkflowApi =
            ClientAppSettings.CreateApiClient();

        private readonly HashSet<uint> _restoredWriteUpFlagIds = new();
        private readonly HashSet<uint> _restoredReferToOptionIds = new();

        private bool _writeUpWorkflowOptionsLoaded;
        private bool _writeUpWorkflowOptionsLoading;

        private async void SiteDashboardWorkspaceView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await LoadWriteUpWorkflowOptionsAsync();
        }

        private async Task LoadWriteUpWorkflowOptionsAsync()
        {
            if (_writeUpWorkflowOptionsLoaded ||
                _writeUpWorkflowOptionsLoading)
            {
                return;
            }

            _writeUpWorkflowOptionsLoading = true;

            try
            {
                var flagsTask =
                    _writeUpWorkflowApi.GetWriteUpFlagsAsync(
                        activeOnly: true,
                        technicianVisibleOnly: true);

                var referToTask =
                    _writeUpWorkflowApi.GetReferToOptionsAsync(
                        activeOnly: true);

                await Task.WhenAll(
                    flagsTask,
                    referToTask);

                var flags = (await flagsTask)
                    .Where(x =>
                        x.IsActive &&
                        x.IsTechnicianVisible)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

                var referToOptions = (await referToTask)
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

                BuildWriteUpFlagCheckBoxes(flags);
                BuildReferToOptionCheckBoxes(referToOptions);

                _writeUpWorkflowOptionsLoaded = true;

                ApplyRestoredWriteUpWorkflowSelections();
            }
            catch
            {
                /*
                 * Submission remains usable if configuration cannot be loaded.
                 * A later Loaded event can retry the configuration request.
                 */
                WriteUpFlagsOptionsPanel.Children.Clear();
                ReferToOptionsPanel.Children.Clear();

                WriteUpFlagsSection.Visibility =
                    Visibility.Collapsed;

                ReferToOptionsSection.Visibility =
                    Visibility.Collapsed;
            }
            finally
            {
                _writeUpWorkflowOptionsLoading = false;
            }
        }

        private void BuildWriteUpFlagCheckBoxes(
            IReadOnlyCollection<WriteUpFlagDto> flags)
        {
            WriteUpFlagsOptionsPanel.Children.Clear();

            foreach (var flag in flags)
            {
                WriteUpFlagsOptionsPanel.Children.Add(
                    CreateWorkflowOptionCheckBox(
                        flag.Id,
                        flag.DisplayName));
            }

            WriteUpFlagsSection.Visibility =
                WriteUpFlagsOptionsPanel.Children.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void BuildReferToOptionCheckBoxes(
            IReadOnlyCollection<ReferToOptionDto> options)
        {
            ReferToOptionsPanel.Children.Clear();

            foreach (var option in options)
            {
                ReferToOptionsPanel.Children.Add(
                    CreateWorkflowOptionCheckBox(
                        option.Id,
                        option.DisplayName));
            }

            var hasOptions =
                ReferToOptionsPanel.Children.Count > 0;

            ReferToOptionsSection.Visibility =
                hasOptions
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (!hasOptions)
            {
                IncludeReferToCheckBox.IsChecked = false;
                ReferToOptionsPanel.Visibility =
                    Visibility.Collapsed;
            }
        }

        private void IncludeReferToCheckBox_CheckedChanged(
            object sender,
            RoutedEventArgs e)
        {
            if (ReferToOptionsPanel is null)
                return;

            var includeReferTo =
                IncludeReferToCheckBox.IsChecked == true;

            ReferToOptionsPanel.Visibility =
                includeReferTo
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (includeReferTo)
                return;

            _restoredReferToOptionIds.Clear();

            foreach (var checkBox in
                     ReferToOptionsPanel.Children.OfType<CheckBox>())
            {
                checkBox.IsChecked = false;
            }
        }

        private static CheckBox CreateWorkflowOptionCheckBox(
            uint id,
            string displayName)
        {
            return new CheckBox
            {
                Tag = id,
                Content = displayName ?? string.Empty,
                Margin = new Thickness(0, 0, 0, 7),

                Foreground =
                    Application.Current.TryFindResource(
                        "TextPrimary") as System.Windows.Media.Brush
            };
        }

        private IReadOnlyList<uint> GetSelectedWriteUpFlagIds()
        {
            if (!_writeUpWorkflowOptionsLoaded)
            {
                return _restoredWriteUpFlagIds
                    .OrderBy(x => x)
                    .ToList();
            }

            return GetCheckedIds(
                WriteUpFlagsOptionsPanel);
        }

        private IReadOnlyList<uint> GetSelectedReferToOptionIds()
        {
            if (IncludeReferToCheckBox.IsChecked != true)
                return Array.Empty<uint>();

            if (!_writeUpWorkflowOptionsLoaded)
            {
                return _restoredReferToOptionIds
                    .OrderBy(x => x)
                    .ToList();
            }

            return GetCheckedIds(
                ReferToOptionsPanel);
        }

        private static IReadOnlyList<uint> GetCheckedIds(
            Panel panel)
        {
            return panel.Children
                .OfType<CheckBox>()
                .Where(x => x.IsChecked == true)
                .Select(x => x.Tag)
                .OfType<uint>()
                .Distinct()
                .ToList();
        }

        private void RestoreWriteUpWorkflowSelections(
            IEnumerable<uint>? writeUpFlagIds,
            IEnumerable<uint>? referToOptionIds)
        {
            _restoredWriteUpFlagIds.Clear();

            foreach (var id in writeUpFlagIds ??
                     Array.Empty<uint>())
            {
                if (id > 0)
                    _restoredWriteUpFlagIds.Add(id);
            }

            _restoredReferToOptionIds.Clear();

            foreach (var id in referToOptionIds ??
                     Array.Empty<uint>())
            {
                if (id > 0)
                    _restoredReferToOptionIds.Add(id);
            }

            if (_restoredReferToOptionIds.Count > 0)
                IncludeReferToCheckBox.IsChecked = true;

            ApplyRestoredWriteUpWorkflowSelections();
        }

        private void ApplyRestoredWriteUpWorkflowSelections()
        {
            ApplyCheckedIds(
                WriteUpFlagsOptionsPanel,
                _restoredWriteUpFlagIds);

            ApplyCheckedIds(
                ReferToOptionsPanel,
                _restoredReferToOptionIds);
        }

        private static void ApplyCheckedIds(
            Panel panel,
            IReadOnlySet<uint> selectedIds)
        {
            foreach (var checkBox in
                     panel.Children.OfType<CheckBox>())
            {
                checkBox.IsChecked =
                    checkBox.Tag is uint id &&
                    selectedIds.Contains(id);
            }
        }

        public void ClearWriteUpWorkflowSelections()
        {
            _restoredWriteUpFlagIds.Clear();
            _restoredReferToOptionIds.Clear();

            ApplyRestoredWriteUpWorkflowSelections();

            IncludeReferToCheckBox.IsChecked = false;
            ReferToOptionsPanel.Visibility =
                Visibility.Collapsed;
        }
    }
}