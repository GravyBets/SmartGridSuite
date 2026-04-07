using Microsoft.Data.SqlClient;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {

        private const string DashboardKindAmsMr = "ams-mr";
        private const string DashboardKindDacs = "dacs";
        private const string DashboardKindRx = "rx";
        private const string DashboardKindIgsd = "igsd";

        public async Task<SiteDashboardRouteInfo?> GetSiteDashboardRouteInfoAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
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

            foreach (var candidateSiteId in GetLookupSiteIdCandidates(siteId))
            {
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@SiteId", SqlDbType.NVarChar, 50) { Value = candidateSiteId });

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    continue;
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

            return null;
        }

        public async Task<SiteDashboardResponse?> GetSiteDashboardAsync(
            string siteId, CancellationToken cancellationToken = default)
        {
            var requestedSiteId = NormalizeSiteId(siteId);

            if (string.IsNullOrWhiteSpace(requestedSiteId))
            {
                return null;
            }

            var route = await GetSiteDashboardRouteInfoAsync(requestedSiteId, cancellationToken);

            if (route is null)
            {
                return null;
            }

            var resolvedSiteId = NormalizeSiteId(route.SiteId);
            var dashboardKind = ResolveDashboardKind(route);
            if (string.IsNullOrWhiteSpace(dashboardKind))
            {
                return null;
            }

            object? data = dashboardKind switch
            {
                DashboardKindAmsMr => await GetAmsMrSiteAsync(siteId, cancellationToken),
                DashboardKindDacs => await GetDacsSiteAsync(siteId, cancellationToken),
                DashboardKindRx => await GetRxSiteAsync(siteId, cancellationToken),
                DashboardKindIgsd => await GetIgsdSiteAsync(siteId, cancellationToken),
                _ => null
            };

            if (data is null)
            {
                return null;
            }

            return new SiteDashboardResponse
            {
                SiteId = resolvedSiteId,
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
                return DashboardKindAmsMr;
            }

            if (siteType.Contains("DACS") || siteId.StartsWith("DAC"))
            {
                return DashboardKindDacs;
            }

            if (siteType.Contains("RE") || siteId.StartsWith("RX"))
            {
                return DashboardKindRx;
            }

            if (siteType.Contains("IG") || siteId.StartsWith("G"))
            {
                return DashboardKindIgsd;
            }

            return null;
        }

        private static string NormalizeSiteId(string? siteId)
        {
            return (siteId ?? "").Trim().ToUpperInvariant();
        }

        private static IEnumerable<string> GetLookupSiteIdCandidates(string? siteId)
        {
            var normalized = NormalizeSiteId(siteId);

            if (string.IsNullOrWhiteSpace(normalized))
                yield break;

            yield return normalized;

            if (normalized.StartsWith("RX") && normalized.Length > 2)
            {
                yield return "RE" + normalized[2..];
            }
            else if (normalized.StartsWith("RE") && normalized.Length > 2)
            {
                yield return "RX" + normalized[2..];
            }
        }

    }
}