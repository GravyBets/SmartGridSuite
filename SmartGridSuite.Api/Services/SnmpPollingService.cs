using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Contracts.Snmp;
using System.Net;
using static System.Runtime.InteropServices.JavaScript.JSType;
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
                var snmpData = BuildSnmpData(oid.ValueType, req.Value.Trim());

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
                    RequestedValue = req.Value.Trim(),
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

            if (!mode.Equals("ValueMap", StringComparison.OrdinalIgnoreCase))
                return rawValue;

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

            return type switch
            {
                "Integer" => new Integer32(int.Parse(rawValue)),
                "Gauge" => new Gauge32(uint.Parse(rawValue)),
                "Counter" => new Counter32(uint.Parse(rawValue)),
                "IpAddress" => new IP(rawValue),
                _ => new OctetString(rawValue)
            };
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