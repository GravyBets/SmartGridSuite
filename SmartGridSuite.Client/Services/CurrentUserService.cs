#nullable enable
using SmartGridSuite.Contracts.Administration.Technicians;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Services
{
    public static class CurrentUserService
    {
        private static readonly ApiClient Api = ClientAppSettings.CreateApiClient();

        public static TechnicianDto? CurrentTechnician { get; private set; }

        public static string CurrentEmployeeId =>
            GetCurrentWindowsUserName();

        // Loads the signed-in Windows user's technician record through the shared
        // API connection boundary. A failed refresh never erases the cached user.
        public static async Task<TechnicianDto?> LoadCurrentTechnicianAsync(
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            var employeeId = CurrentEmployeeId;

            if (string.IsNullOrWhiteSpace(employeeId))
                return null;

            if (!forceRefresh &&
                CurrentTechnician != null &&
                string.Equals(
                    CurrentTechnician.EmployeeId,
                    employeeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CurrentTechnician;
            }

            var encodedEmployeeId =
                Uri.EscapeDataString(employeeId);

            try
            {
                var technician =
                    await Api.GetAsync<TechnicianDto>(
                        $"api/technicians/by-employee-id/{encodedEmployeeId}",
                        ct);

                /*
                 * Only replace the cached user after the API successfully
                 * returns a complete technician response.
                 */
                CurrentTechnician = technician;

                return CurrentTechnician;
            }
            catch (ApiClient.ApiException ex)
                when (ex.StatusCode == 404)
            {
                /*
                 * A successful API response confirming that the employee does
                 * not exist should clear an outdated cached technician.
                 */
                CurrentTechnician = null;
                return null;
            }
            catch (ApiClient.ApiConnectionException)
            {
                /*
                 * Preserve the last successfully loaded technician while
                 * allowing the caller to display its offline state.
                 */
                throw;
            }
        }

        // Clears the current cached technician when the application explicitly
        // needs to reload identity or authorization from the server.
        public static void ClearCachedUser()
        {
            CurrentTechnician = null;
        }

        // Checks whether the currently cached technician has the requested role.
        public static bool CurrentUserHasRole(string roleCode)
        {
            return HasRole(CurrentTechnician, roleCode);
        }

        // Performs a case-insensitive role-code lookup on a technician response.
        public static bool HasRole(
            TechnicianDto? technician,
            string roleCode)
        {
            if (technician?.RoleCodes == null ||
                string.IsNullOrWhiteSpace(roleCode))
            {
                return false;
            }

            return technician.RoleCodes.Any(x =>
                string.Equals(
                    (x ?? string.Empty).Trim(),
                    roleCode.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        // Extracts the employee-style username from the current Windows account.
        private static string GetCurrentWindowsUserName()
        {
            var userName =
                Environment.UserName ?? string.Empty;

            /*
             * Safety cleanup in case Windows returns DOMAIN\username rather
             * than the plain employee ID.
             */
            var slashIndex = userName.LastIndexOf('\\');

            if (slashIndex >= 0 &&
                slashIndex < userName.Length - 1)
            {
                userName = userName[(slashIndex + 1)..];
            }

            return userName.Trim();
        }
    }
}