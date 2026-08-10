#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace SmartGridSuite.Client.Services
{
    public sealed class WriteUpDraftService
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };

        private readonly string _draftDirectory;

        /*
         * Serializes local file operations so rapid TextChanged events cannot
         * overwrite the same draft file at the same time.
         */
        private readonly SemaphoreSlim _fileGate =
            new(1, 1);

        public WriteUpDraftService()
        {
            var localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            _draftDirectory = Path.Combine(
                localAppData,
                "SmartGridSuite",
                "WriteUpDrafts");
        }

        // Saves a write-up draft atomically so an application or power failure
        // cannot leave behind a partially written JSON file.
        public async Task SaveDraftAsync(
            WriteUpDraftRecord draft,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(draft);

            var employeeId =
                NormalizeRequiredValue(
                    draft.EmployeeId,
                    "Employee ID");

            var siteKey =
                NormalizeRequiredValue(
                    draft.SiteKey,
                    "Site");

            draft.EmployeeId = employeeId;
            draft.SiteKey = siteKey;
            draft.UpdatedAtUtc = DateTimeOffset.UtcNow;

            if (draft.ClientSubmissionId == Guid.Empty)
            {
                draft.ClientSubmissionId =
                    Guid.NewGuid();
            }

            var draftPath = GetDraftPath(
                employeeId,
                draft.TicketId,
                siteKey);

            var temporaryPath =
                $"{draftPath}.{Guid.NewGuid():N}.tmp";

            await _fileGate.WaitAsync(ct);

            try
            {
                Directory.CreateDirectory(
                    _draftDirectory);

                var json =
                    JsonSerializer.Serialize(
                        draft,
                        JsonOptions);

                await File.WriteAllTextAsync(
                    temporaryPath,
                    json,
                    Encoding.UTF8,
                    ct);

                /*
                 * The temporary and final files are on the same volume, making
                 * the replacement atomic from the application's perspective.
                 */
                File.Move(
                    temporaryPath,
                    draftPath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch
                    {
                        // A stale temporary file is harmless and can be ignored.
                    }
                }

                _fileGate.Release();
            }
        }

        // Loads the last locally saved draft for one technician and ticket/site.
        public async Task<WriteUpDraftRecord?> LoadDraftAsync(
            string employeeId,
            long? ticketId,
            string siteKey,
            CancellationToken ct = default)
        {
            employeeId =
                NormalizeRequiredValue(
                    employeeId,
                    "Employee ID");

            siteKey =
                NormalizeRequiredValue(
                    siteKey,
                    "Site");

            var draftPath = GetDraftPath(
                employeeId,
                ticketId,
                siteKey);

            await _fileGate.WaitAsync(ct);

            try
            {
                if (!File.Exists(draftPath))
                    return null;

                var json =
                    await File.ReadAllTextAsync(
                        draftPath,
                        Encoding.UTF8,
                        ct);

                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return JsonSerializer
                    .Deserialize<WriteUpDraftRecord>(
                        json,
                        JsonOptions);
            }
            catch (JsonException)
            {
                /*
                 * Do not crash the field application because of one damaged
                 * local draft. Preserve the file for later troubleshooting.
                 */
                return null;
            }
            finally
            {
                _fileGate.Release();
            }
        }

        // Deletes a local draft only after the API has positively confirmed
        // that the write-up submission succeeded.
        public async Task DeleteDraftAsync(
            string employeeId,
            long? ticketId,
            string siteKey,
            CancellationToken ct = default)
        {
            employeeId =
                NormalizeRequiredValue(
                    employeeId,
                    "Employee ID");

            siteKey =
                NormalizeRequiredValue(
                    siteKey,
                    "Site");

            var draftPath = GetDraftPath(
                employeeId,
                ticketId,
                siteKey);

            await _fileGate.WaitAsync(ct);

            try
            {
                if (File.Exists(draftPath))
                    File.Delete(draftPath);
            }
            finally
            {
                _fileGate.Release();
            }
        }

        // Reports whether a recoverable local draft exists without reading or
        // parsing the entire JSON payload.
        public bool HasDraft(
            string employeeId,
            long? ticketId,
            string siteKey)
        {
            employeeId =
                NormalizeRequiredValue(
                    employeeId,
                    "Employee ID");

            siteKey =
                NormalizeRequiredValue(
                    siteKey,
                    "Site");

            return File.Exists(
                GetDraftPath(
                    employeeId,
                    ticketId,
                    siteKey));
        }

        // Finds the newest pending submission for one employee and site. The preferred
        // ticket is selected first, while the site fallback also supports tickets that
        // were created immediately before connectivity was lost.
        public async Task<WriteUpDraftRecord?> FindPendingDraftAsync(
            string employeeId,
            string siteKey,
            long? preferredTicketId,
            CancellationToken ct = default)
        {
            employeeId = NormalizeRequiredValue(
                employeeId,
                "Employee ID");

            siteKey = NormalizeRequiredValue(
                siteKey,
                "Site");

            await _fileGate.WaitAsync(ct);

            try
            {
                if (!Directory.Exists(_draftDirectory))
                    return null;

                var matchingDrafts =
                    new List<WriteUpDraftRecord>();

                foreach (var filePath in Directory.EnumerateFiles(
                             _draftDirectory,
                             "*.json",
                             SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var json = await File.ReadAllTextAsync(
                            filePath,
                            Encoding.UTF8,
                            ct);

                        var draft =
                            JsonSerializer.Deserialize<WriteUpDraftRecord>(
                                json,
                                JsonOptions);

                        if (draft?.IsPendingSubmission != true)
                            continue;

                        if (!string.Equals(
                                draft.EmployeeId?.Trim(),
                                employeeId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!string.Equals(
                                draft.SiteKey?.Trim(),
                                siteKey,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        matchingDrafts.Add(draft);
                    }
                    catch (JsonException)
                    {
                        // Ignore one damaged local draft without blocking other recovery.
                    }
                    catch (IOException)
                    {
                        // The file may temporarily be unavailable; continue searching.
                    }
                }

                return matchingDrafts
                    .OrderByDescending(x =>
                        preferredTicketId.HasValue &&
                        x.TicketId == preferredTicketId)
                    .ThenByDescending(x => x.UpdatedAtUtc)
                    .FirstOrDefault();
            }
            finally
            {
                _fileGate.Release();
            }
        }

        // Builds a filesystem-safe, non-identifying filename from the employee,
        // ticket, and site values that uniquely identify an editor session.
        private string GetDraftPath(
            string employeeId,
            long? ticketId,
            string siteKey)
        {
            var identity =
                $"{employeeId.Trim().ToUpperInvariant()}|" +
                $"{ticketId?.ToString() ?? "NO-TICKET"}|" +
                $"{siteKey.Trim().ToUpperInvariant()}";

            var hashBytes =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(identity));

            var fileName =
                $"{Convert.ToHexString(hashBytes)}.json";

            return Path.Combine(
                _draftDirectory,
                fileName);
        }

        // Validates key values before they are used to identify a local draft.
        private static string NormalizeRequiredValue(string? value, string fieldName)
        {
            var cleanValue =
                (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanValue))
            {
                throw new ArgumentException(
                    $"{fieldName} is required to save a write-up draft.",
                    nameof(value));
            }

            return cleanValue;
        }
    }

    // Stores both editable technician text and an exact confirmed submission
    // payload so either stage can be recovered after connectivity is lost.
    public sealed class WriteUpDraftRecord
    {
        public int FormatVersion { get; set; } = 1;

        public string EmployeeId { get; set; } = "";

        public long? TicketId { get; set; }

        public string SiteKey { get; set; } = "";

        public string ManualWriteUpText { get; set; } = "";

        public string FinalWriteUpText { get; set; } = "";

        public string SiteHistoryWriteUpText { get; set; } = "";

        public List<uint> WriteUpFlagIds { get; set; } = new();

        public List<uint> ReferToOptionIds { get; set; } = new();

        public bool EquipmentWasSwapped { get; set; }

        public bool IpAddressWasChanged { get; set; }

        public bool IsPendingSubmission { get; set; }

        /*
         * This identifier will later be sent to the API and uniquely indexed
         * so retrying an uncertain submission cannot create duplicate records.
         */
        public Guid ClientSubmissionId { get; set; } =
            Guid.NewGuid();

        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}