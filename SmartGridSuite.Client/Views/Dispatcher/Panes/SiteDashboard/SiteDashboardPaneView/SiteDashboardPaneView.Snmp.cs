using SmartGridSuite.Client.Views.Dispatcher.Panes.SiteDashboard;
using SmartGridSuite.Contracts.SiteDashboard;
using SmartGridSuite.Contracts.Snmp;
using SmartGridSuite.Client.Services;

namespace SmartGridSuite.Client.Views.Dispatcher.Panes
{
    public partial class SiteDashboardPaneView
    {
        private readonly LocalSnmpService _localSnmpService = new();

        private async void WorkspaceView_RefreshSnmpRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            try
            {
                TopBarView.StatusText = "Refreshing SNMP configuration...";
                await RefreshSnmpConfigAsync(session, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSnmpOnly(session);

                TopBarView.StatusText = "SNMP configuration refreshed.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP configuration refresh failed: {ex.Message}";
            }
        }

        private void WorkspaceView_SnmpTargetChanged(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            session.SnmpTargetIp = WorkspaceView.GetSnmpTargetIp();
        }

        private async void WorkspaceView_SetSelectedSnmpRequested(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            var selectedOid = WorkspaceView.GetSelectedWritableSnmpOid();
            if (selectedOid is null)
            {
                TopBarView.StatusText = "Select a writable SNMP OID first.";
                return;
            }

            if (!selectedOid.IsWritable)
            {
                TopBarView.StatusText = "Selected OID is read-only.";
                return;
            }

            if (!session.SnmpProfileId.HasValue || session.SnmpProfileId.Value == 0)
            {
                TopBarView.StatusText = "No active SNMP profile is loaded for this site.";
                return;
            }

            var targetIp = WorkspaceView.GetSnmpTargetIp();
            if (string.IsNullOrWhiteSpace(targetIp))
            {
                TopBarView.StatusText = "Enter a target IP first.";
                return;
            }

            var setValue = WorkspaceView.GetSnmpSetValue();
            if (string.IsNullOrWhiteSpace(setValue))
            {
                TopBarView.StatusText = "Enter a value to set.";
                return;
            }

            // For Formula OIDs, the value coming from the UI is the displayed value.
            // The API/SNMP service will use WriteFormula to convert it to the raw integer
            // required by the radio before sending the SET.
            var setStatusValueText = string.Equals(
                selectedOid.DecodeMode,
                "Formula",
                StringComparison.OrdinalIgnoreCase)
                    ? $"display value {setValue}"
                    : setValue;

            try
            {
                TopBarView.StatusText = $"Setting {selectedOid.Label} to {setStatusValueText}...";

                if (session.SnmpProfile is null)
                {
                    TopBarView.StatusText =
                        "The selected SNMP profile details are not loaded.";
                    return;
                }

                var setResult =
                    await _localSnmpService.SetSelectedAsync(
                        session.SnmpProfile,
                        selectedOid,
                        targetIp,
                        setValue,
                        CancellationToken.None);

                session.SnmpTargetIp = targetIp;
                WorkspaceView.ShowSnmpSetResult(setResult);

                if (setResult?.Success != true)
                {
                    TopBarView.StatusText = $"SNMP set failed: {setResult?.ErrorMessage}";
                    return;
                }

                TopBarView.StatusText = $"Set {selectedOid.Label}. Refreshing selected field...";

                var refreshResult = await RefreshSelectedSnmpOidAfterSetAsync(
                    session,
                    selectedOid,
                    targetIp,
                    CancellationToken.None);

                WorkspaceView.ShowSnmpSetAndRefreshResult(setResult, refreshResult);

                TopBarView.StatusText = refreshResult?.Success == true
                    ? $"{selectedOid.Label} set and refreshed: {refreshResult.DisplayValue}."
                    : $"SNMP set succeeded, but refresh failed: {refreshResult?.ErrorMessage}";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP set failed: {ex.Message}";

                WorkspaceView.ShowSnmpSetResult(new SnmpSetResultDto
                {
                    Success = false,
                    TargetIp = targetIp,
                    RequestedValue = setValue,
                    Label = selectedOid.Label,
                    Oid = selectedOid.Oid,
                    DecodeMode = selectedOid.DecodeMode,
                    ErrorMessage = ex.Message
                });
            }
        }

        private async Task<SnmpRunResultDto> RefreshSelectedSnmpOidAfterSetAsync(SiteDashboardTabSession session,
            SnmpOidConfigDto selectedOid, string targetIp, CancellationToken ct)
        {
            if (session.SnmpProfile is null)
            {
                return new SnmpRunResultDto
                {
                    Success = false,
                    ErrorMessage = "No active SNMP profile is loaded for this site."
                };
            }

            try
            {
                WorkspaceView.SetSnmpOidResult(selectedOid.Id, "Refreshing...");

                var refreshResult =
                    await _localSnmpService.RunSelectedAsync(
                        session.SnmpProfile,
                        selectedOid,
                        targetIp,
                        ct);

                var display = refreshResult?.Success == true
                    ? refreshResult.DisplayValue
                    : $"ERROR: {refreshResult?.ErrorMessage}";

                session.SnmpOidResults[selectedOid.Id] = display ?? string.Empty;
                WorkspaceView.SetSnmpOidResult(selectedOid.Id, display ?? string.Empty);

                return refreshResult ?? new SnmpRunResultDto
                {
                    Success = false,
                    ErrorMessage = "No SNMP refresh result returned."
                };
            }
            catch (Exception ex)
            {
                var error = $"ERROR: {ex.Message}";

                session.SnmpOidResults[selectedOid.Id] = error;
                WorkspaceView.SetSnmpOidResult(selectedOid.Id, error);

                return new SnmpRunResultDto
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task RefreshSnmpConfigAsync(
            SiteDashboardTabSession session,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(session.SnmpTargetIp) ||
                session.SnmpTargetIp == "—")
            {
                session.SnmpTargetIp =
                    GetDefaultSnmpTargetIp(session);
            }

            var previousProfileId =
                session.SnmpProfileId;

            var primaryCommType =
                (session.SnmpPrimaryCommType ?? string.Empty)
                .Trim();

            session.SnmpSupported = false;
            session.SnmpDeviceFamily = string.Empty;
            session.SnmpProfileName = string.Empty;
            session.SnmpProfileId = null;
            session.SnmpProfile = null;
            session.SnmpSupportMessage = string.Empty;
            session.SnmpProfiles = new List<SnmpProfileListItemDto>();
            session.SnmpOids = new List<SnmpOidConfigDto>();
            session.SnmpOidResults = new Dictionary<ulong, string>();

            var allProfiles = await _api.GetAsync<List<SnmpProfileListItemDto>>(
                "api/snmp-profiles",
                ct) ?? new List<SnmpProfileListItemDto>();

            session.SnmpProfiles = allProfiles
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .ToList();

            if (session.SnmpProfiles.Count == 0)
            {
                session.SnmpSupportMessage = "No active SNMP profiles are configured.";
                return;
            }

            session.SnmpSupported = true;

            ulong selectedProfileId;

            if (previousProfileId.HasValue &&
                session.SnmpProfiles.Any(
                    x => x.Id == previousProfileId.Value))
            {
                selectedProfileId =
                    previousProfileId.Value;
            }
            else if (string.Equals(
                         session.DashboardKind,
                         SiteDashboardKinds.Tower,
                         StringComparison.OrdinalIgnoreCase))
            {
                selectedProfileId =
                    session.SnmpProfiles.FirstOrDefault(
                        x => x.Name.Contains(
                            "Tower",
                            StringComparison.OrdinalIgnoreCase))?.Id
                    ?? session.SnmpProfiles.First().Id;
            }
            else
            {
                var matchingProfile =
                    string.IsNullOrWhiteSpace(primaryCommType)
                        ? null
                        : session.SnmpProfiles.FirstOrDefault(
                            x => x.Name.Contains(
                                primaryCommType,
                                StringComparison.OrdinalIgnoreCase));

                selectedProfileId =
                    matchingProfile?.Id
                    ?? session.SnmpProfiles.First().Id;
            }

            await LoadSnmpProfileIntoSessionAsync(session, selectedProfileId, ct);
        }

        private static string GetDefaultSnmpTargetIp(SiteDashboardTabSession session)
        {
            if (string.Equals(session.DashboardKind, SiteDashboardKinds.Tower, StringComparison.OrdinalIgnoreCase))
            {
                var towerTarget = session.TowerSectors
                    .SelectMany(x => new[] { x.IPa, x.IPb })
                    .Select(x => (x ?? string.Empty).Trim())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "—");

                return towerTarget ?? string.Empty;
            }

            return new[]
                {
            session.PrimaryIp,
            session.LanIp,
            session.SecondaryIp
        }
                .Select(x => (x ?? string.Empty).Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "—")
                ?? string.Empty;
        }

        private static IEnumerable<(string Key, string Label, string IpAddress)> BuildTowerSnmpTargets(SiteDashboardTabSession session)
        {
            var sectors = session.TowerSectors
                .OrderBy(x => GetTowerSectorSortRankForSnmp(x.Sector))
                .ThenBy(x => x.Sector)
                .ThenBy(x => x.TopSiteId)
                .ToList();

            foreach (var sector in sectors)
            {
                var sectorName = string.IsNullOrWhiteSpace(sector.Sector)
                    ? $"Sector {sector.TopSiteId}"
                    : sector.Sector.Trim();

                var ipA = (sector.IPa ?? string.Empty).Trim();
                var ipB = (sector.IPb ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(ipA) && ipA != "—")
                    yield return ($"{sectorName}-IPA", $"Sector {sectorName} IP A", ipA);

                if (!string.IsNullOrWhiteSpace(ipB) && ipB != "—")
                    yield return ($"{sectorName}-IPB", $"Sector {sectorName} IP B", ipB);
            }
        }

        private static int GetTowerSectorSortRankForSnmp(string? sector)
        {
            var value = (sector ?? string.Empty).Trim().ToUpperInvariant();

            if (value == "AP1")
                return 1;

            if (value == "AP2")
                return 2;

            if (value == "AP3")
                return 3;

            if (value.StartsWith("AP") &&
                int.TryParse(value[2..], out var apNumber))
            {
                return 100 + apNumber;
            }

            return 1000;
        }

        private async Task LoadSnmpProfileIntoSessionAsync(SiteDashboardTabSession session, ulong profileId, CancellationToken ct)
        {
            var profile = await _api.GetAsync<SnmpProfileDetailDto>(
                $"api/snmp-profiles/{profileId}",
                ct);

            if (profile is null)
            {
                session.SnmpSupported = false;
                session.SnmpProfileId = null;
                session.SnmpProfile = null;
                session.SnmpProfileName = string.Empty;
                session.SnmpOids = new List<SnmpOidConfigDto>();
                session.SnmpSupportMessage = "Selected SNMP profile could not be loaded.";
                return;
            }

            session.SnmpSupported = true;
            session.SnmpProfileId = profile.Id;
            session.SnmpProfile = profile;
            session.SnmpProfileName = profile.Name;
            session.SnmpOids = profile.Oids
                .Where(x => x.ShowInWorkspace)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Label)
                .ToList();

            session.SnmpSupportMessage = string.IsNullOrWhiteSpace(session.SnmpProfileName)
                ? $"SNMP ready for {session.HeaderText}."
                : $"SNMP ready for {session.HeaderText}. Profile: {session.SnmpProfileName}.";

            session.SnmpOidResults = new Dictionary<ulong, string>();
        }

        private async void WorkspaceView_SelectedSnmpProfileChanged(object? sender, EventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            var selectedProfileId = WorkspaceView.GetSelectedSnmpProfileId();
            if (!selectedProfileId.HasValue || selectedProfileId.Value == 0)
                return;

            try
            {
                TopBarView.StatusText = "Loading SNMP profile...";
                await LoadSnmpProfileIntoSessionAsync(session, selectedProfileId.Value, CancellationToken.None);

                if (session.SessionKey == _selectedSessionKey)
                    RenderSnmpOnly(session);

                TopBarView.StatusText = $"Loaded SNMP profile {session.SnmpProfileName}.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"SNMP profile load failed: {ex.Message}";
            }
        }

        private async void WorkspaceView_RunSnmpOidRequested(object? sender, SnmpRunOidRequestedEventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            if (session.SnmpProfile is null)
            {
                TopBarView.StatusText = "No active SNMP profile is loaded for this site.";
                return;
            }

            var targetIp = WorkspaceView.GetSnmpTargetIp();
            if (string.IsNullOrWhiteSpace(targetIp))
            {
                TopBarView.StatusText = "Enter a target IP first.";
                return;
            }

            try
            {
                session.SnmpTargetIp = targetIp;
                WorkspaceView.SetSnmpOidResult(e.Oid.Id, "Running...");

                var result =
                    await _localSnmpService.RunSelectedAsync(
                        session.SnmpProfile,
                        e.Oid,
                        targetIp,
                        CancellationToken.None);

                var display = result?.Success == true
                    ? result.DisplayValue
                    : $"ERROR: {result?.ErrorMessage}";

                session.SnmpOidResults[e.Oid.Id] = display ?? string.Empty;
                WorkspaceView.SetSnmpOidResult(e.Oid.Id, display ?? string.Empty);

                TopBarView.StatusText = result?.Success == true
                    ? $"SNMP poll returned {result.DisplayValue}."
                    : $"SNMP poll failed: {result?.ErrorMessage}";
            }
            catch (Exception ex)
            {
                var error = $"ERROR: {ex.Message}";
                session.SnmpOidResults[e.Oid.Id] = error;
                WorkspaceView.SetSnmpOidResult(e.Oid.Id, error);
                TopBarView.StatusText = $"SNMP poll failed: {ex.Message}";
            }
        }

        private async void WorkspaceView_RunSnmpCategoryRequested(object? sender, SnmpRunCategoryRequestedEventArgs e)
        {
            var session = GetSelectedSession();
            if (session is null)
                return;

            if (session.SnmpProfile is null)
            {
                TopBarView.StatusText = "No active SNMP profile is loaded for this site.";
                return;
            }

            var targetIp = WorkspaceView.GetSnmpTargetIp();
            if (string.IsNullOrWhiteSpace(targetIp))
            {
                TopBarView.StatusText = "Enter a target IP first.";
                return;
            }

            try
            {
                session.SnmpTargetIp = targetIp;

                foreach (var oid in e.Oids.OrderBy(x => x.SortOrder).ThenBy(x => x.Label))
                {
                    WorkspaceView.SetSnmpOidResult(oid.Id, "Running...");

                    try
                    {
                        var result =
                            await _localSnmpService.RunSelectedAsync(
                                session.SnmpProfile,
                                oid,
                                targetIp,
                                CancellationToken.None);

                        var display = result?.Success == true
                            ? result.DisplayValue
                            : $"ERROR: {result?.ErrorMessage}";

                        session.SnmpOidResults[oid.Id] = display ?? string.Empty;
                        WorkspaceView.SetSnmpOidResult(oid.Id, display ?? string.Empty);
                    }
                    catch (Exception ex)
                    {
                        var error = $"ERROR: {ex.Message}";
                        session.SnmpOidResults[oid.Id] = error;
                        WorkspaceView.SetSnmpOidResult(oid.Id, error);
                    }
                }

                TopBarView.StatusText = $"{e.Category} SNMP poll complete.";
            }
            catch (Exception ex)
            {
                TopBarView.StatusText = $"Category poll failed: {ex.Message}";
            }
        }

        //Handler to fix the "Refresh SNMP Config" button refreshing the WHOLE dashboard
        private void RenderSnmpOnly(SiteDashboardTabSession session)
        {
            WorkspaceView.SetSnmpContext(
                session.SnmpSupported,
                session.SnmpSupportMessage,
                session.SnmpDeviceFamily,
                session.SnmpProfileName,
                session.PrimaryIp,
                session.LanIp,
                session.SecondaryIp,
                session.SnmpTargetIp);

            WorkspaceView.SetSnmpProfiles(session.SnmpProfiles, session.SnmpProfileId);
            WorkspaceView.SetSnmpOids(session.SnmpOids, session.SnmpOidResults);
        }
    }
}