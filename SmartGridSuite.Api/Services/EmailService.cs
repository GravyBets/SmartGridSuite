using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using System.Net;
using System.Net.Mail;

namespace SmartGridSuite.Api.Services
{
    public sealed class EmailService
    {
        private readonly SmartGridDbContext _db;
        private readonly EmailOptions _options;
        private readonly ILogger<EmailService> _logger;

        private const string EmailEnabledKey = "Email.Enabled";
        private const string EmailDryRunKey = "Email.DryRun";
        private const string DailyAssignmentsEnabledKey = "Email.DailyAssignmentsEnabled";
        private const string WriteUpsEnabledKey = "Email.WriteUpsEnabled";
        private const string BccSenderKey = "Email.BccSender";
        private const string TestRecipientOverrideKey = "Email.TestRecipientOverride";

        public EmailService(SmartGridDbContext db, IOptions<EmailOptions> options, ILogger<EmailService> logger)
        {
            _db = db;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken ct)
        {
            request ??= new EmailSendRequest();

            var settings = await LoadRuntimeSettingsAsync(ct);

            var toAddresses = CleanAddresses(request.ToAddresses);
            var ccAddresses = CleanAddresses(request.CcAddresses);
            var bccAddresses = CleanAddresses(request.BccAddresses);
            var replyToAddresses = CleanAddresses(request.ReplyToAddresses);

            var usingTestRecipientOverride =
                !string.IsNullOrWhiteSpace(settings.TestRecipientOverride);

            var subject = (request.Subject ?? string.Empty).Trim();
            var body = request.Body ?? string.Empty;

            var emailType = string.IsNullOrWhiteSpace(request.EmailType)
                ? "General"
                : request.EmailType.Trim();

            var sender = ResolveSender(request);

            if (usingTestRecipientOverride)
            {
                toAddresses = CleanAddresses(new[] { settings.TestRecipientOverride });
                ccAddresses.Clear();
                bccAddresses.Clear();
            }

            if (settings.BccSender &&
                !usingTestRecipientOverride &&
                sender.WasOverrideAccepted &&
                !string.IsNullOrWhiteSpace(sender.FromAddress))
            {
                bccAddresses.Add(sender.FromAddress);

                bccAddresses = bccAddresses
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (string.IsNullOrWhiteSpace(_options.SmtpHost))
            {
                return await LogOnlyAsync(
                    request,
                    settings,
                    sender,
                    toAddresses,
                    ccAddresses,
                    bccAddresses,
                    "Failed",
                    "SMTP host is missing in API configuration.",
                    ct);
            }

            if (string.IsNullOrWhiteSpace(sender.FromAddress))
            {
                return await LogOnlyAsync(
                    request,
                    settings,
                    sender,
                    toAddresses,
                    ccAddresses,
                    bccAddresses,
                    "Failed",
                    "Email From address is missing. Configure DefaultFromAddress or provide a workflow FromAddress.",
                    ct);
            }

            if (string.IsNullOrWhiteSpace(subject))
            {
                return await LogOnlyAsync(
                    request,
                    settings,
                    sender,
                    toAddresses,
                    ccAddresses,
                    bccAddresses,
                    "Failed",
                    "Email subject is required.",
                    ct);
            }

            if (toAddresses.Count == 0)
            {
                return await LogOnlyAsync(
                    request,
                    settings,
                    sender,
                    toAddresses,
                    ccAddresses,
                    bccAddresses,
                    "Failed",
                    "At least one recipient is required.",
                    ct);
            }

            if (!settings.EmailEnabled)
            {
                return await LogOnlyAsync(
                    request,
                    settings,
                    sender,
                    toAddresses,
                    ccAddresses,
                    bccAddresses,
                    "Skipped",
                    "Emails are disabled in General Settings.",
                    ct);
            }

            if (!IsEmailTypeEnabled(emailType, settings))
            {
                return await LogOnlyAsync(
                    request,
                    settings,
                    sender,
                    toAddresses,
                    ccAddresses,
                    bccAddresses,
                    "Skipped",
                    $"{emailType} emails are disabled in General Settings.",
                    ct);
            }

            if (settings.DryRun)
            {
                return await LogOnlyAsync(
                    request,
                    settings,
                    sender,
                    toAddresses,
                    ccAddresses,
                    bccAddresses,
                    "DryRun",
                    "Dry run is enabled. Email was logged but not sent.",
                    ct);
            }

            var log = CreateLogEntity(
                request,
                settings,
                sender,
                toAddresses,
                ccAddresses,
                bccAddresses,
                status: "Pending",
                errorMessage: null);

            _db.EmailLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            try
            {
                using var mail = new MailMessage
                {
                    From = CreateMailAddress(
                        sender.FromAddress,
                        sender.FromDisplayName,
                        "From"),

                    Subject = subject,
                    Body = body,
                    IsBodyHtml = request.IsHtml
                };

                foreach (var to in toAddresses)
                {
                    mail.To.Add(CreateMailAddress(
                        to,
                        null,
                        "To"));
                }

                foreach (var cc in ccAddresses)
                {
                    mail.CC.Add(CreateMailAddress(
                        cc,
                        null,
                        "CC"));
                }

                foreach (var bcc in bccAddresses)
                {
                    mail.Bcc.Add(CreateMailAddress(
                        bcc,
                        null,
                        "BCC"));
                }

                foreach (var replyTo in replyToAddresses)
                {
                    mail.ReplyToList.Add(CreateMailAddress(
                        replyTo,
                        null,
                        "Reply-To"));
                }

                using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
                {
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    EnableSsl = _options.UseStartTls,
                    UseDefaultCredentials = false
                };

                if (_options.UseAuthentication)
                {
                    client.Credentials = new NetworkCredential(
                        _options.Username,
                        _options.Password);
                }

                await client.SendMailAsync(mail);

                log.Status = "Sent";
                log.ErrorMessage = null;

                await _db.SaveChangesAsync(ct);

                return new EmailSendResult
                {
                    LogId = log.Id,
                    Status = "Sent",
                    Message = "Email sent."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Email send failed. EmailLogId={EmailLogId}",
                    log.Id);

                log.Status = "Failed";
                log.ErrorMessage = ex.Message;

                await _db.SaveChangesAsync(ct);

                return new EmailSendResult
                {
                    LogId = log.Id,
                    Status = "Failed",
                    Message = ex.Message
                };
            }
        }

        private ResolvedEmailSender ResolveSender(EmailSendRequest request)
        {
            var defaultFromAddress = (_options.DefaultFromAddress ?? string.Empty).Trim();
            var defaultFromDisplayName = (_options.DefaultFromDisplayName ?? string.Empty).Trim();

            var requestedFromAddress = (request.FromAddress ?? string.Empty).Trim();
            var requestedFromDisplayName = (request.FromDisplayName ?? string.Empty).Trim();

            if (_options.AllowFromOverride &&
                IsValidEmailAddress(requestedFromAddress))
            {
                return new ResolvedEmailSender
                {
                    FromAddress = requestedFromAddress,
                    FromDisplayName = string.IsNullOrWhiteSpace(requestedFromDisplayName)
                        ? requestedFromAddress
                        : requestedFromDisplayName,
                    WasOverrideAccepted = true
                };
            }

            return new ResolvedEmailSender
            {
                FromAddress = defaultFromAddress,
                FromDisplayName = defaultFromDisplayName,
                WasOverrideAccepted = false
            };
        }

        private async Task<EmailSendResult> LogOnlyAsync(
            EmailSendRequest request,
            EmailRuntimeSettings settings,
            ResolvedEmailSender sender,
            List<string> toAddresses,
            List<string> ccAddresses,
            List<string> bccAddresses,
            string status,
            string message,
            CancellationToken ct)
        {
            var log = CreateLogEntity(
                request,
                settings,
                sender,
                toAddresses,
                ccAddresses,
                bccAddresses,
                status,
                message);

            _db.EmailLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            return new EmailSendResult
            {
                LogId = log.Id,
                Status = status,
                Message = message
            };
        }

        private EmailLogEntity CreateLogEntity(
            EmailSendRequest request,
            EmailRuntimeSettings settings,
            ResolvedEmailSender sender,
            List<string> toAddresses,
            List<string> ccAddresses,
            List<string> bccAddresses,
            string status,
            string? errorMessage)
        {
            return new EmailLogEntity
            {
                CreatedAt = DateTime.Now,

                EmailType = string.IsNullOrWhiteSpace(request.EmailType)
                    ? "General"
                    : TrimTo(request.EmailType, 64),

                EnabledAtSendTime = settings.EmailEnabled,
                DryRun = settings.DryRun,

                FromAddress = TrimTo(sender.FromAddress, 255),
                FromDisplayName = string.IsNullOrWhiteSpace(sender.FromDisplayName)
                    ? null
                    : TrimTo(sender.FromDisplayName, 255),

                ToAddresses = string.Join("; ", toAddresses),
                CcAddresses = ccAddresses.Count == 0
                    ? null
                    : string.Join("; ", ccAddresses),

                BccAddresses = bccAddresses.Count == 0
                    ? null
                    : string.Join("; ", bccAddresses),

                Subject = TrimTo(request.Subject, 255),
                BodyPreview = TrimTo(request.Body, 1000),

                Status = status,
                ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                    ? null
                    : errorMessage.Trim(),

                RelatedTicketId = request.RelatedTicketId,
                RelatedSite = string.IsNullOrWhiteSpace(request.RelatedSite)
                    ? null
                    : TrimTo(request.RelatedSite, 64),

                CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy)
                    ? null
                    : TrimTo(request.CreatedBy, 100)
            };
        }

        private async Task<EmailRuntimeSettings> LoadRuntimeSettingsAsync(CancellationToken ct)
        {
            var keys = new[]
            {
                EmailEnabledKey,
                EmailDryRunKey,
                DailyAssignmentsEnabledKey,
                WriteUpsEnabledKey,
                BccSenderKey,
                TestRecipientOverrideKey
            };

            var values = await _db.AppSettings
                .AsNoTracking()
                .Where(x => keys.Contains(x.SettingKey))
                .ToDictionaryAsync(
                    x => x.SettingKey,
                    x => x.SettingValue ?? "",
                    StringComparer.OrdinalIgnoreCase,
                    ct);

            return new EmailRuntimeSettings
            {
                EmailEnabled = GetBool(values, EmailEnabledKey, defaultValue: false),
                DryRun = GetBool(values, EmailDryRunKey, defaultValue: true),
                DailyAssignmentsEnabled = GetBool(values, DailyAssignmentsEnabledKey, defaultValue: false),
                WriteUpsEnabled = GetBool(values, WriteUpsEnabledKey, defaultValue: false),
                BccSender = GetBool(values, BccSenderKey, defaultValue: true),
                TestRecipientOverride = values.TryGetValue(TestRecipientOverrideKey, out var overrideValue)
                    ? CleanRuntimeString(overrideValue)
                    : ""
            };
        }

        private static bool IsEmailTypeEnabled(string emailType, EmailRuntimeSettings settings)
        {
            if (emailType.Equals("DailyAssignment", StringComparison.OrdinalIgnoreCase))
                return settings.DailyAssignmentsEnabled;

            if (emailType.Equals("WriteUp", StringComparison.OrdinalIgnoreCase))
                return settings.WriteUpsEnabled;

            return true;
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

        private static List<string> CleanAddresses(IEnumerable<string>? values)
        {
            var result = new List<string>();

            foreach (var raw in values ?? Array.Empty<string>())
            {
                var text = (raw ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var pieces = text
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x));

                foreach (var piece in pieces)
                {
                    try
                    {
                        var parsed = new MailAddress(piece);
                        result.Add(parsed.Address);
                    }
                    catch
                    {
                        result.Add(piece);
                    }
                }
            }

            return result
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsValidEmailAddress(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            try
            {
                _ = new MailAddress(value.Trim());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string TrimTo(string? value, int maxLength)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.Length <= maxLength)
                return text;

            return text[..maxLength];
        }

        private static string CleanRuntimeString(string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.Equals("string", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return text;
        }

        private static MailAddress CreateMailAddress(string address, string? displayName, string fieldName)
        {
            var cleanAddress = (address ?? string.Empty).Trim();
            var cleanDisplayName = (displayName ?? string.Empty).Trim();

            try
            {
                return string.IsNullOrWhiteSpace(cleanDisplayName)
                    ? new MailAddress(cleanAddress)
                    : new MailAddress(cleanAddress, cleanDisplayName);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{fieldName} email address is invalid: '{cleanAddress}'",
                    ex);
            }
        }

        private sealed class EmailRuntimeSettings
        {
            public bool EmailEnabled { get; set; }

            public bool DryRun { get; set; }

            public bool DailyAssignmentsEnabled { get; set; }

            public bool WriteUpsEnabled { get; set; }

            public bool BccSender { get; set; }

            public string TestRecipientOverride { get; set; } = "";
        }

        private sealed class ResolvedEmailSender
        {
            public string FromAddress { get; set; } = "";

            public string FromDisplayName { get; set; } = "";

            public bool WasOverrideAccepted { get; set; }
        }
    }

    public sealed class EmailSendRequest
    {
        public string EmailType { get; set; } = "General";

        public IEnumerable<string> ToAddresses { get; set; } = Array.Empty<string>();

        public IEnumerable<string> CcAddresses { get; set; } = Array.Empty<string>();

        public IEnumerable<string> BccAddresses { get; set; } = Array.Empty<string>();

        public IEnumerable<string> ReplyToAddresses { get; set; } = Array.Empty<string>();

        public string? FromAddress { get; set; }

        public string? FromDisplayName { get; set; }

        public string Subject { get; set; } = "";

        public string Body { get; set; } = "";

        public bool IsHtml { get; set; }

        public long? RelatedTicketId { get; set; }

        public string? RelatedSite { get; set; }

        public string? CreatedBy { get; set; }
    }

    public sealed class EmailSendResult
    {
        public long? LogId { get; set; }

        public string Status { get; set; } = "";

        public string Message { get; set; } = "";
    }
}