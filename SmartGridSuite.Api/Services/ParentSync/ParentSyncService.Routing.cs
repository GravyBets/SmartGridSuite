using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        public async Task<SiteDashboardRouteInfo?> GetSiteDashboardRouteInfoAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            var sql = """
            SELECT
                s.SiteId,
                s.SiteType_id AS SiteTypeId,
                dt.CodeValue  AS SiteType,
                s.Config_id   AS ConfigId,
                cfg.Config    AS SiteConfigName,
                cfg.Comm1Type AS PrimaryCommType,
                cfg.Comm2Type AS SecondaryCommType,
                cfg.Descr     AS SiteConfigDescription
            FROM [sgc_main].[Site] s
            LEFT JOIN [sgc_main].[Decode] dt
                ON s.SiteType_id = dt.CodeId
               AND dt.CodeType = 'SiteType'
            LEFT JOIN [sgc_main].[Config] cfg
                ON s.Config_id = cfg.id
            WHERE s.SiteId = @SiteId;
            """;

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@SiteId", SqlDbType.NVarChar, 50) { Value = siteId });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new SiteDashboardRouteInfo
            {
                SiteId = GetString(reader, "SiteId") ?? "",
                SiteTypeId = GetNullableInt32(reader, "SiteTypeId"),
                SiteType = GetString(reader, "SiteType"),
                ConfigId = GetNullableInt32(reader, "ConfigId"),
                SiteConfigName = GetString(reader, "SiteConfigName"),
                PrimaryCommType = GetString(reader, "PrimaryCommType"),
                SecondaryCommType = GetString(reader, "SecondaryCommType"),
                SiteConfigDescription = GetString(reader, "SiteConfigDescription")
            };
        }

        public async Task<SiteDashboardResponse?> GetSiteDashboardAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            siteId = (siteId ?? "").Trim();

            if (string.IsNullOrWhiteSpace(siteId))
            {
                return null;
            }

            var route = await GetSiteDashboardRouteInfoAsync(siteId, cancellationToken);

            if (route is null)
            {
                return null;
            }

            var dashboardKind = ResolveDashboardKind(route);

            object? data = dashboardKind switch
            {
                "ams-mr" => await GetAmsMrSiteAsync(siteId, cancellationToken),
                "dacs" => await GetDacsSiteAsync(siteId, cancellationToken),
                "rx" => await GetRxSiteAsync(siteId, cancellationToken),
                "igsd" => await GetIgsdSiteAsync(siteId, cancellationToken),
                _ => null
            };

            if (data is null)
            {
                return null;
            }

            return new SiteDashboardResponse
            {
                SiteId = siteId,
                DashboardKind = dashboardKind,
                Route = route,
                Data = data
            };
        }

        private static string? ResolveDashboardKind(SiteDashboardRouteInfo route)
        {
            var siteType = (route.SiteType ?? "").Trim().ToUpperInvariant();
            var siteId = (route.SiteId ?? "").Trim().ToUpperInvariant();

            if (siteType.Contains("AMS") || siteId.EndsWith("MR"))
            {
                return "ams-mr";
            }

            if (siteType.Contains("DACS") || siteId.StartsWith("DAC"))
            {
                return "dacs";
            }

            if (siteType.Contains("RE") || siteId.StartsWith("RX"))
            {
                return "rx";
            }

            if (siteType.Contains("IG") || siteId.StartsWith("G"))
            {
                return "igsd";
            }

            return null;
        }

    }
}