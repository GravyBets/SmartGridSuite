using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Services.ParentSync.Models;
using System.Data;
using System.Data.Common;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed partial class ParentSyncService
    {
        private readonly string _connectionString;
        private readonly SmartGridDbContext _appDb;
        private readonly ParentDatabaseConnectionFactory _parentDatabaseConnectionFactory;

        public ParentSyncService(
            IOptions<ParentDatabaseOptions> options,
            SmartGridDbContext appDb,
            ParentDatabaseConnectionFactory
            parentDatabaseConnectionFactory)
        {
            _connectionString =
                options.Value.ConnectionString;

            _appDb =
                appDb;

            _parentDatabaseConnectionFactory =
                parentDatabaseConnectionFactory;
        }

        public async Task<int> GetSiteCountAsync(CancellationToken cancellationToken = default)
        {
            await using var conn = _parentDatabaseConnectionFactory.CreateConnection();
            await conn.OpenAsync(cancellationToken);

            const string sql = "SELECT COUNT(*) FROM [sgc_main].[Site];";

            await using var cmd = new SqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);

            return result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        private async Task<Dictionary<string, List<SiteHistoryPreviewRow>>> GetRecentSiteHistoryLookupAsync(IEnumerable<string> siteIds,
            CancellationToken cancellationToken = default)
        {
            var requestedCandidates = siteIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var lookup = new Dictionary<string, List<SiteHistoryPreviewRow>>(StringComparer.OrdinalIgnoreCase);

            if (requestedCandidates.Count == 0)
                return lookup;

            var normalizedSearchKeys = BuildSiteHistoryMatchKeys(requestedCandidates.ToArray());

            if (normalizedSearchKeys.Count == 0)
                return lookup;

            var conn = _appDb.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;

            if (shouldClose)
                await conn.OpenAsync(cancellationToken);

            try
            {
                await using var cmd = conn.CreateCommand();

                var parameterNames = new List<string>();

                for (var i = 0; i < normalizedSearchKeys.Count; i++)
                {
                    var parameter = cmd.CreateParameter();
                    parameter.ParameterName = $"@p{i}";
                    parameter.Value = normalizedSearchKeys[i];

                    cmd.Parameters.Add(parameter);
                    parameterNames.Add(parameter.ParameterName);
                }

                cmd.CommandText = $"""
                    SELECT
                        sh.history_id,
                        sh.site_id,
                        sh.source_type,
                        sh.visit_date,
                        sh.primary_tech,
                        sh.secondary_tech,
                        sh.issue_text AS IssueText,
                        sh.narrative,
                        sh.edited_at,
                        sh.edited_by,
                        twus.submission_id
                    FROM site_history sh
                    LEFT JOIN (
                        SELECT
                            site_history_id,
                            MIN(id) AS submission_id
                        FROM ticket_writeup_submissions
                        WHERE site_history_id IS NOT NULL
                          AND is_deleted = 0
                        GROUP BY site_history_id
                    ) twus
                        ON twus.site_history_id = sh.history_id
                    WHERE sh.is_deleted = 0
                      AND UPPER(
                            REPLACE(
                            REPLACE(
                            REPLACE(
                            REPLACE(TRIM(sh.site_id), '_', ''),
                                                      '-', ''),
                                                      ' ', ''),
                                                      '.', '')
                          ) IN ({string.Join(", ", parameterNames)})
                    ORDER BY sh.visit_date DESC, sh.history_id DESC;
                    """;

                var allRows = new List<SiteHistoryPreviewRow>();

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    allRows.Add(new SiteHistoryPreviewRow
                    {
                        HistoryId = GetInt64(reader, "history_id"),
                        SubmissionId = GetNullableInt64(reader, "submission_id"),

                        SiteId = GetDbString(reader, "site_id") ?? "",
                        SourceType = GetDbString(reader, "source_type") ?? "",

                        VisitDate = GetNullableDateTime(reader, "visit_date"),
                        PrimaryTech = GetDbString(reader, "primary_tech"),
                        SecondaryTech = GetDbString(reader, "secondary_tech"),
                        IssueText = GetDbString(reader, "IssueText"),
                        Narrative = GetDbString(reader, "narrative"),

                        EditedAt = GetNullableDateTime(reader, "edited_at"),
                        EditedBy = GetDbString(reader, "edited_by"),

                        IsDeleted = false
                    });
                }

                var rowsByNormalizedSiteId = allRows
                    .GroupBy(x => NormalizeSiteHistoryKey(x.SiteId), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x
                            .OrderByDescending(r => r.VisitDate ?? DateTime.MinValue)
                            .ThenByDescending(r => r.HistoryId)
                            .ToList(),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var candidate in requestedCandidates)
                {
                    var candidateKeys = BuildSiteHistoryMatchKeys(candidate);

                    var candidateRows = candidateKeys
                        .Where(rowsByNormalizedSiteId.ContainsKey)
                        .SelectMany(key => rowsByNormalizedSiteId[key])
                        .GroupBy(x => x.HistoryId)
                        .Select(x => x.First())
                        .OrderByDescending(x => x.VisitDate ?? DateTime.MinValue)
                        .ThenByDescending(x => x.HistoryId)
                        .ToList();

                    if (candidateRows.Count == 0)
                        continue;

                    // Preserve old behavior: callers can still ask by the original candidate text.
                    lookup[candidate] = candidateRows;

                    // Also allow lookup by normalized form.
                    foreach (var key in candidateKeys)
                        lookup[key] = candidateRows;
                }

                return lookup;
            }
            finally
            {
                if (shouldClose)
                    await conn.CloseAsync();
            }
        }

        private static List<string> BuildSiteHistoryMatchKeys(params string?[] values)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var value in values)
            {
                var key = NormalizeSiteHistoryKey(value);

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                keys.Add(key);

                // MR flexibility:
                // 2837MR should also match older rows stored as 2837.
                if (key.EndsWith("MR", StringComparison.OrdinalIgnoreCase) &&
                    key.Length > 2)
                {
                    keys.Add(key[..^2]);
                }
            }

            return keys.ToList();
        }

        private static string NormalizeSiteHistoryKey(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Replace("_", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Replace(".", "")
                .ToUpperInvariant();
        }

        private static string? GetString(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal).Trim();
        }

        private static decimal? GetDecimal(SqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToDecimal(reader.GetValue(ordinal));
        }

        private static string? GetDbString(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal).Trim();
        }

        private static DateTime? GetNullableDateTime(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(reader.GetValue(ordinal));
        }

        private static long GetInt64(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt64(reader.GetValue(ordinal));
        }

        private static int? GetNullableInt32(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static long? GetNullableInt64(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);

            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt64(reader.GetValue(ordinal));
        }
    }
}