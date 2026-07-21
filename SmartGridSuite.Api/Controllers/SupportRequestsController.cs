using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Contracts.Settings;
using System.Text;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/support-requests")]
    public sealed class SupportRequestsController : ControllerBase
    {
        private const string AllEmailsAddressKey =
            "Email.AllEmailsAddress";

        private readonly SmartGridDbContext _db;
        private readonly EmailService _emailService;

        public SupportRequestsController(
            SmartGridDbContext db,
            EmailService emailService)
        {
            _db = db;
            _emailService = emailService;
        }

        [HttpPost("bug-feature")]
        public async Task<ActionResult<SubmitBugFeatureResponse>>
            SubmitBugFeatureRequest(
                [FromBody] SubmitBugFeatureRequest req,
                CancellationToken ct)
        {
            req ??= new SubmitBugFeatureRequest();

            var requestType = CleanSingleLine(
                req.RequestType,
                50);

            var applicationArea = CleanSingleLine(
                req.ApplicationArea,
                100);

            var title = CleanSingleLine(
                req.Title,
                120);

            var details = CleanMultiline(
                req.Details,
                6000);

            var submittedBy = CleanSingleLine(
                req.SubmittedBy,
                100);

            var applicationVersion = CleanSingleLine(
                req.ApplicationVersion,
                50);

            if (string.IsNullOrWhiteSpace(requestType))
                requestType = "Request";

            if (string.IsNullOrWhiteSpace(applicationArea))
                applicationArea = "Other";

            if (string.IsNullOrWhiteSpace(title))
            {
                return BadRequest(
                    "A request title is required.");
            }

            if (string.IsNullOrWhiteSpace(details))
            {
                return BadRequest(
                    "Request details are required.");
            }

            if (string.IsNullOrWhiteSpace(submittedBy))
                submittedBy = "Unknown Windows User";

            if (string.IsNullOrWhiteSpace(applicationVersion))
                applicationVersion = "Unknown";

            /*
             * The destination is controlled by Administration ->
             * General Settings -> All Emails Address.
             */
            var recipientRow = await _db.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.SettingKey == AllEmailsAddressKey,
                    ct);

            var recipient = CleanSettingValue(
                recipientRow?.SettingValue);

            if (string.IsNullOrWhiteSpace(recipient))
            {
                return BadRequest(
                    "No All Emails Address is configured in General Settings.");
            }

            var subject = TrimTo(
                $"[SmartGridSuite {requestType}] " +
                $"{applicationArea}: {title}",
                255);

            var body = BuildEmailBody(
                requestType,
                applicationArea,
                title,
                details,
                submittedBy,
                applicationVersion);

            /*
             * BugFeatureRequest is not controlled by the Daily Assignment
             * or Write-Up workflow switches. EmailService still applies:
             *
             * - Email.Enabled
             * - Email.DryRun
             * - Email.TestRecipientOverride
             * - SMTP configuration
             * - Email logging
             */
            var result = await _emailService.SendAsync(
                new EmailSendRequest
                {
                    EmailType = "BugFeatureRequest",

                    ToAddresses = new[]
                    {
                        recipient
                    },

                    Subject = subject,
                    Body = body,
                    IsHtml = false,

                    CreatedBy = submittedBy
                },
                ct);

            return Ok(
                new SubmitBugFeatureResponse
                {
                    Sent = result.Status.Equals(
                        "Sent",
                        StringComparison.OrdinalIgnoreCase),

                    LogId = result.LogId,
                    Status = result.Status,
                    Message = result.Message
                });
        }

        private static string BuildEmailBody(
            string requestType,
            string applicationArea,
            string title,
            string details,
            string submittedBy,
            string applicationVersion)
        {
            var builder = new StringBuilder();

            builder.AppendLine(
                "SMART GRID SUITE - BUG / FEATURE REQUEST");

            builder.AppendLine();

            builder.AppendLine(
                $"Type: {requestType}");

            builder.AppendLine(
                $"Title: {title}");

            builder.AppendLine(
                $"Area: {applicationArea}");

            builder.AppendLine(
                $"Submitted By: {submittedBy}");

            builder.AppendLine(
                $"Submitted At: {DateTime.Now:MM/dd/yyyy h:mm tt}");

            builder.AppendLine(
                $"Application Version: {applicationVersion}");

            builder.AppendLine();

            builder.AppendLine("DETAILS");
            builder.AppendLine("-------");
            builder.AppendLine(details);

            return builder.ToString();
        }

        private static string CleanSingleLine(
            string? value,
            int maxLength)
        {
            var text = (value ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            return TrimTo(text, maxLength);
        }

        private static string CleanMultiline(
            string? value,
            int maxLength)
        {
            var text = (value ?? string.Empty).Trim();

            return TrimTo(text, maxLength);
        }

        private static string CleanSettingValue(
            string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.Equals(
                    "string",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return text;
        }

        private static string TrimTo(
            string? value,
            int maxLength)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.Length <= maxLength)
                return text;

            return text[..maxLength];
        }
    }
}