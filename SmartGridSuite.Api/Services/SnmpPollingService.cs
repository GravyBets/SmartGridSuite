using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Contracts.Snmp;
using System.Net;
using System.Globalization;
using NetIPAddress = System.Net.IPAddress;

namespace SmartGridSuite.Api.Services
{
    public sealed class SnmpPollingService
    {
        private readonly SmartGridDbContext _db;

        public SnmpPollingService(SmartGridDbContext db)
        {
            _db = db;
        }

        public async Task<SnmpRunResultDto> RunSelectedAsync(SnmpRunSelectedRequestDto req, CancellationToken ct)
        {
            if (req.ProfileId == 0)
                return Fail("ProfileId is required.", req);

            if (req.OidId == 0)
                return Fail("OidId is required.", req);

            if (string.IsNullOrWhiteSpace(req.TargetIp))
                return Fail("Target IP is required.", req);

            if (!IPAddress.TryParse(req.TargetIp.Trim(), out var ip))
                return Fail("Target IP is not a valid IP address.", req);

            var profile = await _db.SnmpProfiles
                .AsNoTracking()
                .Include(x => x.Oids)
                    .ThenInclude(x => x.DecodeValues)
                .FirstOrDefaultAsync(x => x.Id == req.ProfileId && !x.IsDeleted && x.IsActive, ct);

            if (profile is null)
                return Fail("SNMP profile not found.", req);

            var oid = profile.Oids
                .FirstOrDefault(x => x.Id == req.OidId && !x.IsDeleted);

            if (oid is null)
                return Fail("SNMP OID not found.", req, profile.Name);

            try
            {
                var endpoint = new IPEndPoint(ip, 161);
                var variables = new List<Variable>
        {
            new(new ObjectIdentifier(oid.Oid))
        };

                string rawValue;

                if (IsV3(profile))
                {
                    if (string.IsNullOrWhiteSpace(profile.UsmUser) ||
                        string.IsNullOrWhiteSpace(profile.AuthKey) ||
                        string.IsNullOrWhiteSpace(profile.PrivacyKey))
                    {
                        return Fail(
                            "Profile is set to SNMPv3 but is missing one or more required v3 credentials.",
                            req,
                            profile.Name,
                            oid.Label,
                            oid.Oid,
                            oid.DecodeMode);
                    }

                    rawValue = PollV3(profile, oid, endpoint, variables);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(profile.ReadCommunity))
                    {
                        return Fail(
                            "Profile is set to SNMPv2c but Read Community is missing.",
                            req,
                            profile.Name,
                            oid.Label,
                            oid.Oid,
                            oid.DecodeMode);
                    }

                    var result = Messenger.Get(
                        VersionCode.V2,
                        endpoint,
                        new OctetString(profile.ReadCommunity),
                        variables,
                        profile.TimeoutMs);

                    rawValue = result.First().Data.ToString();
                }

                var displayValue = DecodeValue(oid, rawValue);

                return new SnmpRunResultDto
                {
                    Success = true,
                    TargetIp = req.TargetIp.Trim(),
                    ProfileName = profile.Name,
                    Label = oid.Label,
                    Oid = oid.Oid,
                    DecodeMode = oid.DecodeMode,
                    RawValue = rawValue,
                    DisplayValue = displayValue
                };
            }
            catch (Exception ex)
            {
                return Fail(
                    ex.Message,
                    req,
                    profile.Name,
                    oid.Label,
                    oid.Oid,
                    oid.DecodeMode);
            }
        }

        private static string PollV3(Data.Entities.SnmpProfileEntity profile, Data.Entities.SnmpOidEntity oid, IPEndPoint endpoint, IList<Variable> variables)
        {
            var auth = CreateAuthenticationProvider(profile);
            var privacy = CreatePrivacyProvider(profile, auth);

            var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
            var report = discovery.GetResponse(profile.TimeoutMs, endpoint);

            ISnmpMessage request = new GetRequestMessage(
                VersionCode.V3,
                Messenger.NextMessageId,
                Messenger.NextRequestId,
                new OctetString(profile.UsmUser!),
                new OctetString(profile.ContextName ?? string.Empty),
                variables,
                privacy,
                Messenger.MaxMessageSize,
                report);

            var reply = request.GetResponse(profile.TimeoutMs, endpoint);

            if (reply is ReportMessage)
            {
                if (reply.Pdu().Variables.Count == 0)
                    throw new InvalidOperationException("Unexpected empty v3 report message.");

                var id = reply.Pdu().Variables[0].Id;
                if (id != Messenger.NotInTimeWindow)
                    throw new InvalidOperationException(id.GetErrorMessage());

                request = new GetRequestMessage(
                    VersionCode.V3,
                    Messenger.NextMessageId,
                    Messenger.NextRequestId,
                    new OctetString(profile.UsmUser!),
                    new OctetString(profile.ContextName ?? string.Empty),
                    variables,
                    privacy,
                    Messenger.MaxMessageSize,
                    reply);

                reply = request.GetResponse(profile.TimeoutMs, endpoint);
            }

            if (reply.Pdu().ErrorStatus.ToInt32() != 0)
                throw ErrorException.Create("error in response", endpoint.Address, reply);

            return reply.Pdu().Variables[0].Data.ToString();
        }

        public async Task<SnmpSetResultDto> SetSelectedAsync(SnmpSetSelectedRequestDto req, CancellationToken ct)
        {
            if (req.ProfileId == 0)
                return FailSet("ProfileId is required.", req);

            if (req.OidId == 0)
                return FailSet("OidId is required.", req);

            if (string.IsNullOrWhiteSpace(req.TargetIp))
                return FailSet("Target IP is required.", req);

            if (string.IsNullOrWhiteSpace(req.Value))
                return FailSet("Set value is required.", req);

            if (!NetIPAddress.TryParse(req.TargetIp.Trim(), out var ip))
                return FailSet("Target IP is not a valid IP address.", req);

            var profile = await _db.SnmpProfiles
                .AsNoTracking()
                .Include(x => x.Oids)
                    .ThenInclude(x => x.DecodeValues)
                .FirstOrDefaultAsync(x => x.Id == req.ProfileId && !x.IsDeleted && x.IsActive, ct);

            if (profile is null)
                return FailSet("SNMP profile not found.", req);

            var oid = profile.Oids.FirstOrDefault(x => x.Id == req.OidId && !x.IsDeleted);
            if (oid is null)
                return FailSet("SNMP OID not found.", req, profile.Name);

            if (!oid.IsWritable)
                return FailSet("Selected OID is not writable.", req, profile.Name, oid.Label, oid.Oid, oid.DecodeMode);

            try
            {
                var endpoint = new IPEndPoint(ip, 161);

                // This is what the user typed in the WPF Set Tool.
                // For Raw / ValueMap OIDs, this is already the raw radio value.
                // For Formula OIDs, this is the displayed value, like 1.2 or 757.00125.
                var requestedValue = req.Value.Trim();

                // Convert the requested value into the raw whole-number value the radio expects.
                // Example:
                //   User enters: 1.2
                //   WriteFormula: x * 10
                //   Raw SET value: 12
                var rawSetValue = BuildRawSetValue(oid, requestedValue);

                var snmpData = BuildSnmpData(oid.ValueType, rawSetValue);

                var variables = new List<Variable>
                {
                    new(new ObjectIdentifier(oid.Oid), snmpData)
                };

                string rawValue;

                if (IsV3(profile))
                {
                    if (string.IsNullOrWhiteSpace(profile.UsmUser) ||
                        string.IsNullOrWhiteSpace(profile.AuthKey) ||
                        string.IsNullOrWhiteSpace(profile.PrivacyKey))
                    {
                        return FailSet(
                            "Profile is set to SNMPv3 but is missing one or more required v3 credentials.",
                            req,
                            profile.Name,
                            oid.Label,
                            oid.Oid,
                            oid.DecodeMode);
                    }

                    rawValue = SetV3(profile, endpoint, variables);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(profile.WriteCommunity))
                    {
                        return FailSet(
                            "Profile is set to SNMPv2c but Write Community is missing.",
                            req,
                            profile.Name,
                            oid.Label,
                            oid.Oid,
                            oid.DecodeMode);
                    }

                    var result = Messenger.Set(
                        VersionCode.V2,
                        endpoint,
                        new OctetString(profile.WriteCommunity),
                        variables,
                        profile.TimeoutMs);

                    rawValue = result.First().Data.ToString();
                }

                var displayValue = DecodeValue(oid, rawValue);

                return new SnmpSetResultDto
                {
                    Success = true,
                    TargetIp = req.TargetIp.Trim(),
                    ProfileName = profile.Name,
                    Label = oid.Label,
                    Oid = oid.Oid,
                    DecodeMode = oid.DecodeMode,
                    // Keep this as the user-entered/display value.
                    // RawValue below is the value returned by the radio after SET.
                    RequestedValue = requestedValue,
                    RawValue = rawValue,
                    DisplayValue = displayValue
                };
            }
            catch (Exception ex)
            {
                return FailSet(
                    ex.Message,
                    req,
                    profile.Name,
                    oid.Label,
                    oid.Oid,
                    oid.DecodeMode);
            }
        }

        private static string DecodeValue(Data.Entities.SnmpOidEntity oid, string rawValue)
        {
            var mode = (oid.DecodeMode ?? "Raw").Trim();

            // Formula mode:
            // raw radio value -> displayed value
            //
            // Example:
            //   RawValue: 75700125
            //   ReadFormula: x / 100000
            //   DecimalPlaces: 5
            //   UnitLabel: MHz
            //   DisplayValue: 757.00125 MHz
            if (mode.Equals("Formula", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryDecodeFormulaValue(oid, rawValue, out var formulaDisplayValue))
                    return rawValue;

                if (oid.ShowRawValueAlongsideDecoded)
                    return $"{formulaDisplayValue} ({rawValue})";

                return formulaDisplayValue;
            }

            // Raw mode returns the radio value exactly as received.
            if (!mode.Equals("ValueMap", StringComparison.OrdinalIgnoreCase))
                return rawValue;

            // ValueMap mode:
            // exact raw value -> configured display text.
            var match = oid.DecodeValues
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.RawValue)
                .FirstOrDefault(x => string.Equals(
                    (x.RawValue ?? string.Empty).Trim(),
                    (rawValue ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return rawValue;

            if (oid.ShowRawValueAlongsideDecoded)
                return $"{match.DisplayText} ({rawValue})";

            return match.DisplayText;
        }

        private static bool TryDecodeFormulaValue(Data.Entities.SnmpOidEntity oid, string rawValue, out string displayValue)
        {
            displayValue = string.Empty;

            var cleanRaw = (rawValue ?? string.Empty).Trim();

            if (!decimal.TryParse(
                    cleanRaw,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var rawNumber))
            {
                return false;
            }

            // ReadFormula converts raw radio response into display value.
            // Example: raw 12 with "x / 10" becomes 1.2.
            if (!SnmpFormulaEvaluator.TryEvaluate(
                    oid.ReadFormula,
                    rawNumber,
                    out var decodedNumber))
            {
                return false;
            }

            var formatted = oid.DecimalPlaces.HasValue
                ? Math.Round(decodedNumber, oid.DecimalPlaces.Value, MidpointRounding.AwayFromZero)
                    .ToString($"F{oid.DecimalPlaces.Value}", CultureInfo.InvariantCulture)
                : decodedNumber.ToString("0.##########", CultureInfo.InvariantCulture);

            var unit = (oid.UnitLabel ?? string.Empty).Trim();

            displayValue = string.IsNullOrWhiteSpace(unit)
                ? formatted
                : $"{formatted} {unit}";

            return true;
        }

        private static string BuildRawSetValue(Data.Entities.SnmpOidEntity oid, string requestedValue)
        {
            var mode = (oid.DecodeMode ?? "Raw").Trim();

            // Raw and ValueMap OIDs already pass raw values to the radio.
            if (!mode.Equals("Formula", StringComparison.OrdinalIgnoreCase))
                return requestedValue.Trim();

            // Formula SET only makes sense for integer-style radio values.
            if (!string.Equals(
                    oid.ValueType,
                    "Integer",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"OID '{oid.Label}' uses Formula Decode, but its Type is not Integer.");
            }

            // WriteFormula converts display/user-entered value back into radio raw value.
            //
            // Example:
            //   User enters: 757.00125
            //   WriteFormula: x * 100000
            //   Raw SET value: 75700125
            if (!SnmpFormulaEvaluator.TryBuildWriteValue(
                    requestedValue,
                    oid.WriteFormula,
                    out var rawWriteValue))
            {
                throw new InvalidOperationException(
                    $"Unable to convert '{requestedValue}' using Write Formula for OID '{oid.Label}'. Check the OID's Write Formula.");
            }

            return rawWriteValue;
        }

        private static SnmpRunResultDto Fail(string error, SnmpRunSelectedRequestDto req, string profileName = "", string label = "", 
            string oid = "", string decodeMode = "")
        {
            return new SnmpRunResultDto
            {
                Success = false,
                TargetIp = req.TargetIp ?? string.Empty,
                ProfileName = profileName,
                Label = label,
                Oid = oid,
                DecodeMode = decodeMode,
                RawValue = string.Empty,
                DisplayValue = string.Empty,
                ErrorMessage = error
            };
        }

        //Helpers
        private static ISnmpData BuildSnmpData(string? valueType, string rawValue)
        {
            var type = (valueType ?? "String").Trim();
            var cleanRaw = (rawValue ?? string.Empty).Trim();

            // Admin now only exposes String and Integer.
            // Gauge/Counter/IpAddress are left here for compatibility with any older saved rows.
            if (type.Equals("Integer", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(
                        cleanRaw,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    throw new InvalidOperationException(
                        $"'{cleanRaw}' is not a valid Integer SNMP SET value.");
                }

                return new Integer32(parsed);
            }

            if (type.Equals("Gauge", StringComparison.OrdinalIgnoreCase))
            {
                if (!uint.TryParse(
                        cleanRaw,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    throw new InvalidOperationException(
                        $"'{cleanRaw}' is not a valid Gauge SNMP SET value.");
                }

                return new Gauge32(parsed);
            }

            if (type.Equals("Counter", StringComparison.OrdinalIgnoreCase))
            {
                if (!uint.TryParse(
                        cleanRaw,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    throw new InvalidOperationException(
                        $"'{cleanRaw}' is not a valid Counter SNMP SET value.");
                }

                return new Counter32(parsed);
            }

            if (type.Equals("IpAddress", StringComparison.OrdinalIgnoreCase))
                return new IP(cleanRaw);

            return new OctetString(cleanRaw);
        }

        private static string SetV3(Data.Entities.SnmpProfileEntity profile, IPEndPoint endpoint, IList<Variable> variables)
        {
            var auth = CreateAuthenticationProvider(profile);
            var privacy = CreatePrivacyProvider(profile, auth);

            var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
            var report = discovery.GetResponse(profile.TimeoutMs, endpoint);

            ISnmpMessage request = new SetRequestMessage(
                VersionCode.V3,
                Messenger.NextMessageId,
                Messenger.NextRequestId,
                new OctetString(profile.UsmUser!),
                new OctetString(profile.ContextName ?? string.Empty),
                variables,
                privacy,
                Messenger.MaxMessageSize,
                report);

            var reply = request.GetResponse(profile.TimeoutMs, endpoint);

            if (reply is ReportMessage)
            {
                if (reply.Pdu().Variables.Count == 0)
                    throw new InvalidOperationException("Unexpected empty v3 report message.");

                var id = reply.Pdu().Variables[0].Id;
                if (id != Messenger.NotInTimeWindow)
                    throw new InvalidOperationException(id.GetErrorMessage());

                request = new SetRequestMessage(
                    VersionCode.V3,
                    Messenger.NextMessageId,
                    Messenger.NextRequestId,
                    new OctetString(profile.UsmUser!),
                    new OctetString(profile.ContextName ?? string.Empty),
                    variables,
                    privacy,
                    Messenger.MaxMessageSize,
                    reply);

                reply = request.GetResponse(profile.TimeoutMs, endpoint);
            }

            if (reply.Pdu().ErrorStatus.ToInt32() != 0)
                throw ErrorException.Create("error in response", endpoint.Address, reply);

            return reply.Pdu().Variables[0].Data.ToString();
        }

        private static SnmpSetResultDto FailSet(string error, SnmpSetSelectedRequestDto req, string profileName = "", string label = "", 
            string oid = "", string decodeMode = "")
        {
            return new SnmpSetResultDto
            {
                Success = false,
                TargetIp = req.TargetIp ?? string.Empty,
                ProfileName = profileName,
                Label = label,
                Oid = oid,
                DecodeMode = decodeMode,
                RequestedValue = req.Value ?? string.Empty,
                RawValue = string.Empty,
                DisplayValue = string.Empty,
                ErrorMessage = error
            };
        }

        private static string NormalizeVersion(string? version)
        {
            return string.IsNullOrWhiteSpace(version) ? "v3" : version.Trim().ToLowerInvariant();
        }

        private static string NormalizeAuthProtocol(string? protocol)
        {
            return string.IsNullOrWhiteSpace(protocol) ? "MD5" : protocol.Trim();
        }

        private static string NormalizePrivacyProtocol(string? protocol)
        {
            return string.IsNullOrWhiteSpace(protocol) ? "DES" : protocol.Trim();
        }

        private static bool IsV3(Data.Entities.SnmpProfileEntity profile)
        {
            return string.Equals(NormalizeVersion(profile.SnmpVersion), "v3", StringComparison.OrdinalIgnoreCase);
        }

        private static IAuthenticationProvider CreateAuthenticationProvider(Data.Entities.SnmpProfileEntity profile)
        {
            var authProtocol = NormalizeAuthProtocol(profile.AuthProtocol);

#pragma warning disable CS0618
            if (string.Equals(authProtocol, "SHA-512", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(authProtocol, "SHA512", StringComparison.OrdinalIgnoreCase))
            {
                return new SHA512AuthenticationProvider(new OctetString(profile.AuthKey!));
            }

            if (string.Equals(authProtocol, "SHA-384", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(authProtocol, "SHA384", StringComparison.OrdinalIgnoreCase))
            {
                return new SHA384AuthenticationProvider(new OctetString(profile.AuthKey!));
            }

            if (string.Equals(authProtocol, "SHA-256", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(authProtocol, "SHA256", StringComparison.OrdinalIgnoreCase))
            {
                return new SHA256AuthenticationProvider(new OctetString(profile.AuthKey!));
            }

            if (string.Equals(authProtocol, "SHA-1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(authProtocol, "SHA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(authProtocol, "SHA1", StringComparison.OrdinalIgnoreCase))
            {
                return new SHA1AuthenticationProvider(new OctetString(profile.AuthKey!));
            }

            return new MD5AuthenticationProvider(new OctetString(profile.AuthKey!));
#pragma warning restore CS0618
        }

        private static IPrivacyProvider CreatePrivacyProvider(Data.Entities.SnmpProfileEntity profile, IAuthenticationProvider auth)
        {
            var privacyProtocol = NormalizePrivacyProtocol(profile.PrivacyProtocol);

#pragma warning disable CS0618
            if (string.Equals(privacyProtocol, "AES-256", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(privacyProtocol, "AES256", StringComparison.OrdinalIgnoreCase))
            {
                return new AES256PrivacyProvider(new OctetString(profile.PrivacyKey!), auth);
            }

            if (string.Equals(privacyProtocol, "AES-128", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(privacyProtocol, "AES128", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(privacyProtocol, "AES", StringComparison.OrdinalIgnoreCase))
            {
                return new AESPrivacyProvider(new OctetString(profile.PrivacyKey!), auth);
            }

            return new DESPrivacyProvider(new OctetString(profile.PrivacyKey!), auth);
#pragma warning restore CS0618
        }
    }
}