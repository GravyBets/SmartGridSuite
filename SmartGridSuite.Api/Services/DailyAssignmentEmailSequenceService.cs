using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;

namespace SmartGridSuite.Api.Services
{
    public sealed class DailyAssignmentEmailSequenceService
    {
        private readonly SmartGridDbContext _db;

        public DailyAssignmentEmailSequenceService(
            SmartGridDbContext db)
        {
            _db = db;
        }

        public async Task<DailyAssignmentEmailSequence> GetNextAsync(
            string? targetDisplay,
            DateTime workDate,
            CancellationToken ct = default)
        {
            var cleanTarget =
                string.IsNullOrWhiteSpace(targetDisplay)
                    ? "Daily Assignment Target"
                    : targetDisplay.Trim();

            var date =
                workDate.Date;

            var dateText =
                date.ToString("MM/dd/yyyy");

            var subjectPrefix =
                $"{cleanTarget} - ";

            var subjectSuffix =
                $" - {dateText}";

            /*
             * Sent represents a delivered email.
             * DryRun represents a simulated successful send and should behave
             * exactly like a delivery while testing the sequence.
             *
             * Skipped and Failed attempts do not consume a sequence number.
             */
            var previousSuccessfulEmailCount =
                await _db.EmailLogs
                    .AsNoTracking()
                    .Where(x =>
                        x.EmailType == "DailyAssignment" &&
                        (
                            x.Status == "Sent" ||
                            x.Status == "DryRun"
                        ) &&
                        x.Subject.StartsWith(subjectPrefix) &&
                        x.Subject.EndsWith(subjectSuffix))
                    .CountAsync(ct);

            if (previousSuccessfulEmailCount == 0)
            {
                return new DailyAssignmentEmailSequence
                {
                    IsFirstEmailOfDay = true,
                    ModificationNumber = 0,
                    Title = "Daily Assignment"
                };
            }

            return new DailyAssignmentEmailSequence
            {
                IsFirstEmailOfDay = false,

                /*
                 * One earlier successful email means this is Modified(1).
                 * Two earlier successful emails means Modified(2), and so on.
                 */
                ModificationNumber =
                    previousSuccessfulEmailCount,

                Title =
                    $"Modified({previousSuccessfulEmailCount}) " +
                    "Daily Assignments"
            };
        }
    }

    public sealed class DailyAssignmentEmailSequence
    {
        public bool IsFirstEmailOfDay { get; init; }

        public int ModificationNumber { get; init; }

        public string Title { get; init; } =
            "Daily Assignment";
    }
}