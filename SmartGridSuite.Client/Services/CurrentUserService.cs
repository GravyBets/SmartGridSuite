#nullable enable
using SmartGridSuite.Contracts.Administration.Technicians;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Services
{
    public static class CurrentUserService
    {
        private static readonly HttpClient Http = new()
        {
            BaseAddress = new Uri("https://localhost:7140/")
        };

        public static TechnicianDto? CurrentTechnician { get; private set; }

        public static string CurrentEmployeeId => GetCurrentWindowsUserName();

        public static async Task<TechnicianDto?> LoadCurrentTechnicianAsync(bool forceRefresh = false, CancellationToken ct = default)
        {
            var employeeId = CurrentEmployeeId;

            if (string.IsNullOrWhiteSpace(employeeId))
                return null;

            if (!forceRefresh &&
                CurrentTechnician != null &&
                string.Equals(CurrentTechnician.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
            {
                return CurrentTechnician;
            }

            var encodedEmployeeId = Uri.EscapeDataString(employeeId);

            CurrentTechnician = await Http.GetFromJsonAsync<TechnicianDto>(
                $"api/technicians/by-employee-id/{encodedEmployeeId}",
                ct);

            return CurrentTechnician;
        }

        public static void ClearCachedUser()
        {
            CurrentTechnician = null;
        }

        public static bool CurrentUserHasRole(string roleCode)
        {
            return HasRole(CurrentTechnician, roleCode);
        }

        public static bool HasRole(TechnicianDto? technician, string roleCode)
        {
            if (technician?.RoleCodes == null)
                return false;

            return technician.RoleCodes.Any(x =>
                string.Equals(
                    (x ?? string.Empty).Trim(),
                    roleCode.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string GetCurrentWindowsUserName()
        {
            // Your Windows profile appears to use employee ID style usernames,
            // for example: 00232505.
            var userName = Environment.UserName ?? string.Empty;

            // Safety cleanup in case this ever comes through as DOMAIN\username.
            var slashIndex = userName.LastIndexOf('\\');
            if (slashIndex >= 0 && slashIndex < userName.Length - 1)
                userName = userName[(slashIndex + 1)..];

            return userName.Trim();
        }
    }
}