using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Contracts.Settings;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/admin/email-settings")]
    public sealed class AdminEmailSettingsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;
        private readonly EmailService _emailService;

        private const string EmailEnabledKey = "Email.Enabled";
        private const string EmailDryRunKey = "Email.DryRun";
        private const string DailyAssignmentsEnabledKey = "Email.DailyAssignmentsEnabled";
        private const string WriteUpsEnabledKey = "Email.WriteUpsEnabled";
        private const string BccSenderKey = "Email.BccSender";
        private const string TestRecipientOverrideKey = "Email.TestRecipientOverride";
        private const string AllEmailsAddressKey = "Email.AllEmailsAddress";

        private static readonly string[] EmailSettingKeys =
        {
            EmailEnabledKey,
            EmailDryRunKey,
            DailyAssignmentsEnabledKey,
            WriteUpsEnabledKey,
            BccSenderKey,
            TestRecipientOverrideKey,
            AllEmailsAddressKey
        };

        public AdminEmailSettingsController(SmartGridDbContext db, EmailService emailService)
        {
            _db = db;
            _emailService = emailService;
        }

        [HttpGet]
        public async Task<ActionResult<EmailSettingsDto>> Get(CancellationToken ct)
        {
            return Ok(await LoadEmailSettingsAsync(ct));
        }

        [HttpPost]
        public async Task<ActionResult<EmailSettingsDto>> Save([FromBody] UpdateEmailSettingsRequest req, CancellationToken ct)
        {
            req ??= new UpdateEmailSettingsRequest();

            await UpsertSettingAsync(
                EmailEnabledKey,
                req.EmailEnabled ? "true" : "false",
                ct);

            await UpsertSettingAsync(
                EmailDryRunKey,
                req.DryRun ? "true" : "false",
                ct);

            await UpsertSettingAsync(
                DailyAssignmentsEnabledKey,
                req.DailyAssignmentsEnabled ? "true" : "false",
                ct);

            await UpsertSettingAsync(
                WriteUpsEnabledKey,
                req.WriteUpsEnabled ? "true" : "false",
                ct);

            await UpsertSettingAsync(
                BccSenderKey,
                req.BccSender ? "true" : "false",
                ct);

            await UpsertSettingAsync(
                TestRecipientOverrideKey,
                CleanSwaggerPlaceholder(req.TestRecipientOverride),
                ct);

            await UpsertSettingAsync(
                AllEmailsAddressKey,
                CleanSwaggerPlaceholder(req.AllEmailsAddress),
                ct);

            await _db.SaveChangesAsync(ct);

            return Ok(await LoadEmailSettingsAsync(ct));
        }

        [HttpPost("test")]
        public async Task<ActionResult<SendTestEmailResponse>> SendTestEmail([FromBody] SendTestEmailRequest req, CancellationToken ct)
        {
            req ??= new SendTestEmailRequest();

            req.ToAddress = CleanSwaggerPlaceholder(req.ToAddress);
            req.CreatedBy = CleanSwaggerPlaceholder(req.CreatedBy);
            req.FromAddress = CleanSwaggerPlaceholder(req.FromAddress);
            req.FromDisplayName = CleanSwaggerPlaceholder(req.FromDisplayName);

            var settings = await LoadEmailSettingsAsync(ct);

            req.ToAddress = (req.ToAddress ?? string.Empty).Trim();

            if (req.ToAddress.Equals("string", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Enter a real test recipient email address.");
            }

            if (string.IsNullOrWhiteSpace(req.ToAddress) &&
                string.IsNullOrWhiteSpace(settings.TestRecipientOverride))
            {
                return BadRequest(
                    "Enter a test recipient or set a Test Recipient Override.");
            }

            var createdBy = string.IsNullOrWhiteSpace(req.CreatedBy)
                ? "Admin"
                : req.CreatedBy.Trim();

            var result = await _emailService.SendAsync(
                new EmailSendRequest
                {
                    EmailType = "Test",

                    ToAddresses = string.IsNullOrWhiteSpace(req.ToAddress)
                        ? Array.Empty<string>()
                        : new[] { req.ToAddress.Trim() },

                    FromAddress = req.FromAddress,
                    FromDisplayName = req.FromDisplayName,

                    Subject = "SmartGridSuite Test Email",

                    Body =
                        $"SmartGridSuite test email from the API server.{Environment.NewLine}{Environment.NewLine}" +
                        $"Sent at: {DateTime.Now:MM-dd-yyyy HH:mm:ss}{Environment.NewLine}" +
                        $"Requested by: {createdBy}{Environment.NewLine}{Environment.NewLine}" +
                        "If you received this, server-side SMTP relay is working.",

                    IsHtml = false,
                    CreatedBy = createdBy
                },
                ct);

            return Ok(new SendTestEmailResponse
            {
                LogId = result.LogId,
                Status = result.Status,
                Message = result.Message
            });
        }

        private async Task<EmailSettingsDto> LoadEmailSettingsAsync(CancellationToken ct)
        {
            var values = await _db.AppSettings
                .AsNoTracking()
                .Where(x => EmailSettingKeys.Contains(x.SettingKey))
                .ToDictionaryAsync(
                    x => x.SettingKey,
                    x => x.SettingValue ?? "",
                    StringComparer.OrdinalIgnoreCase,
                    ct);

            return new EmailSettingsDto
            {
                EmailEnabled = GetBool(
                    values,
                    EmailEnabledKey,
                    defaultValue: false),

                DryRun = GetBool(
                    values,
                    EmailDryRunKey,
                    defaultValue: true),

                DailyAssignmentsEnabled = GetBool(
                    values,
                    DailyAssignmentsEnabledKey,
                    defaultValue: false),

                WriteUpsEnabled = GetBool(
                    values,
                    WriteUpsEnabledKey,
                    defaultValue: false),

                BccSender = GetBool(
                    values,
                    BccSenderKey,
                    defaultValue: true),

                TestRecipientOverride = values.TryGetValue(
                    TestRecipientOverrideKey,
                    out var overrideValue)
                        ? overrideValue ?? ""
                        : "",

                AllEmailsAddress = values.TryGetValue(
                    AllEmailsAddressKey,
                    out var allEmailsAddress)
                        ? allEmailsAddress ?? ""
                        : ""
                            };
        }

        private async Task UpsertSettingAsync(string key, string value, CancellationToken ct)
        {
            var entity = await _db.AppSettings
                .FirstOrDefaultAsync(x => x.SettingKey == key, ct);

            if (entity == null)
            {
                entity = new AppSettingEntity
                {
                    SettingKey = key
                };

                _db.AppSettings.Add(entity);
            }

            entity.SettingValue = value;
            entity.UpdatedAt = DateTime.Now;
        }

        private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool defaultValue)
        {
            if (!values.TryGetValue(key, out var raw))
                return defaultValue;

            if (bool.TryParse(raw, out var parsed))
                return parsed;

            if (raw == "1")
                return true;

            if (raw == "0")
                return false;

            return defaultValue;
        }

        private static string CleanSwaggerPlaceholder(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.Equals("string", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return text;
        }
    }
}