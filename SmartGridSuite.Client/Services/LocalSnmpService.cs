#nullable enable

using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using SmartGridSuite.Contracts.Snmp;
using System.Globalization;
using System.Net;
using NetIPAddress = System.Net.IPAddress;

namespace SmartGridSuite.Client.Services
{
    /// <summary>
    /// Executes SNMP directly from the client laptop.
    ///
    /// The API remains responsible for storing and returning profiles,
    /// credentials, OIDs, formulas, and decode mappings.
    ///
    /// This service performs the actual UDP SNMP communication locally.
    /// </summary>
    public sealed class LocalSnmpService
    {
        public Task<SnmpRunResultDto> RunSelectedAsync(
            SnmpProfileDetailDto profile,
            SnmpOidConfigDto oid,
            string targetIp,
            CancellationToken ct = default)
        {
            return Task.Run(
                () => RunSelectedCore(
                    profile,
                    oid,
                    targetIp,
                    ct),
                ct);
        }

        public Task<SnmpSetResultDto> SetSelectedAsync(
            SnmpProfileDetailDto profile,
            SnmpOidConfigDto oid,
            string targetIp,
            string value,
            CancellationToken ct = default)
        {
            return Task.Run(
                () => SetSelectedCore(
                    profile,
                    oid,
                    targetIp,
                    value,
                    ct),
                ct);
        }

        private static SnmpRunResultDto RunSelectedCore(
            SnmpProfileDetailDto profile,
            SnmpOidConfigDto oid,
            string targetIp,
            CancellationToken ct)
        {
            if (profile is null)
            {
                return Fail(
                    "SNMP profile is required.",
                    targetIp,
                    null,
                    oid);
            }

            if (oid is null)
            {
                return Fail(
                    "SNMP OID is required.",
                    targetIp,
                    profile,
                    null);
            }

            var cleanTargetIp =
                (targetIp ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanTargetIp))
            {
                return Fail(
                    "Target IP is required.",
                    cleanTargetIp,
                    profile,
                    oid);
            }

            if (!NetIPAddress.TryParse(
                    cleanTargetIp,
                    out var ip))
            {
                return Fail(
                    "Target IP is not a valid IP address.",
                    cleanTargetIp,
                    profile,
                    oid);
            }

            if (string.IsNullOrWhiteSpace(oid.Oid))
            {
                return Fail(
                    "The selected SNMP OID is blank.",
                    cleanTargetIp,
                    profile,
                    oid);
            }

            if (IsV3(profile))
            {
                if (string.IsNullOrWhiteSpace(profile.UsmUser) ||
                    string.IsNullOrWhiteSpace(profile.AuthKey) ||
                    string.IsNullOrWhiteSpace(profile.PrivacyKey))
                {
                    return Fail(
                        "Profile is set to SNMPv3 but is missing one or more required v3 credentials.",
                        cleanTargetIp,
                        profile,
                        oid);
                }
            }
            else if (string.IsNullOrWhiteSpace(
                         profile.ReadCommunity))
            {
                return Fail(
                    "Profile is set to SNMPv2c but Read Community is missing.",
                    cleanTargetIp,
                    profile,
                    oid);
            }

            var endpoint =
                new IPEndPoint(ip, 161);

            var variables =
                new List<Variable>
                {
                    new(
                        new ObjectIdentifier(
                            oid.Oid.Trim()))
                };

            var attempts =
                Math.Max(1, profile.Retries + 1);

            Exception? lastException = null;

            for (var attempt = 1;
                 attempt <= attempts;
                 attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var rawValue =
                        IsV3(profile)
                            ? PollV3(
                                profile,
                                endpoint,
                                variables)
                            : PollV2(
                                profile,
                                endpoint,
                                variables);

                    return new SnmpRunResultDto
                    {
                        Success = true,
                        TargetIp = cleanTargetIp,
                        ProfileName = profile.Name,
                        Label = oid.Label,
                        Oid = oid.Oid,
                        DecodeMode = oid.DecodeMode,
                        RawValue = rawValue,
                        DisplayValue = DecodeValue(
                            oid,
                            rawValue)
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            return Fail(
                lastException?.Message
                ?? "The SNMP request failed.",
                cleanTargetIp,
                profile,
                oid);
        }

        private static SnmpSetResultDto SetSelectedCore(
            SnmpProfileDetailDto profile,
            SnmpOidConfigDto oid,
            string targetIp,
            string value,
            CancellationToken ct)
        {
            if (profile is null)
            {
                return FailSet(
                    "SNMP profile is required.",
                    targetIp,
                    value,
                    null,
                    oid);
            }

            if (oid is null)
            {
                return FailSet(
                    "SNMP OID is required.",
                    targetIp,
                    value,
                    profile,
                    null);
            }

            var cleanTargetIp =
                (targetIp ?? string.Empty).Trim();

            var requestedValue =
                (value ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(cleanTargetIp))
            {
                return FailSet(
                    "Target IP is required.",
                    cleanTargetIp,
                    requestedValue,
                    profile,
                    oid);
            }

            if (!NetIPAddress.TryParse(
                    cleanTargetIp,
                    out var ip))
            {
                return FailSet(
                    "Target IP is not a valid IP address.",
                    cleanTargetIp,
                    requestedValue,
                    profile,
                    oid);
            }

            if (string.IsNullOrWhiteSpace(requestedValue))
            {
                return FailSet(
                    "Set value is required.",
                    cleanTargetIp,
                    requestedValue,
                    profile,
                    oid);
            }

            if (!oid.IsWritable)
            {
                return FailSet(
                    "Selected OID is not writable.",
                    cleanTargetIp,
                    requestedValue,
                    profile,
                    oid);
            }

            if (string.IsNullOrWhiteSpace(oid.Oid))
            {
                return FailSet(
                    "The selected SNMP OID is blank.",
                    cleanTargetIp,
                    requestedValue,
                    profile,
                    oid);
            }

            if (IsV3(profile))
            {
                if (string.IsNullOrWhiteSpace(profile.UsmUser) ||
                    string.IsNullOrWhiteSpace(profile.AuthKey) ||
                    string.IsNullOrWhiteSpace(profile.PrivacyKey))
                {
                    return FailSet(
                        "Profile is set to SNMPv3 but is missing one or more required v3 credentials.",
                        cleanTargetIp,
                        requestedValue,
                        profile,
                        oid);
                }
            }
            else if (string.IsNullOrWhiteSpace(
                         profile.WriteCommunity))
            {
                return FailSet(
                    "Profile is set to SNMPv2c but Write Community is missing.",
                    cleanTargetIp,
                    requestedValue,
                    profile,
                    oid);
            }

            string rawSetValue;

            try
            {
                rawSetValue =
                    BuildRawSetValue(
                        oid,
                        requestedValue);
            }
            catch (Exception ex)
            {
                return FailSet(
                    ex.Message,
                    cleanTargetIp,
                    requestedValue,
                    profile,
                    oid);
            }

            ISnmpData snmpData;

            try
            {
                snmpData =
                    BuildSnmpData(
                        oid.ValueType,
                        rawSetValue);
            }
            catch (Exception ex)
            {
                return FailSet(
                    ex.Message,
                    cleanTargetIp,
                    requestedValue,
                    profile,
                    oid);
            }

            var endpoint =
                new IPEndPoint(ip, 161);

            var variables =
                new List<Variable>
                {
                    new(
                        new ObjectIdentifier(
                            oid.Oid.Trim()),
                        snmpData)
                };

            var attempts =
                Math.Max(1, profile.Retries + 1);

            Exception? lastException = null;

            for (var attempt = 1;
                 attempt <= attempts;
                 attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var rawValue =
                        IsV3(profile)
                            ? SetV3(
                                profile,
                                endpoint,
                                variables)
                            : SetV2(
                                profile,
                                endpoint,
                                variables);

                    return new SnmpSetResultDto
                    {
                        Success = true,
                        TargetIp = cleanTargetIp,
                        ProfileName = profile.Name,
                        Label = oid.Label,
                        Oid = oid.Oid,
                        DecodeMode = oid.DecodeMode,
                        RequestedValue = requestedValue,
                        RawValue = rawValue,
                        DisplayValue = DecodeValue(
                            oid,
                            rawValue)
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            return FailSet(
                lastException?.Message
                ?? "The SNMP SET request failed.",
                cleanTargetIp,
                requestedValue,
                profile,
                oid);
        }

        private static string PollV2(
            SnmpProfileDetailDto profile,
            IPEndPoint endpoint,
            IList<Variable> variables)
        {
            var result =
                Messenger.Get(
                    VersionCode.V2,
                    endpoint,
                    new OctetString(
                        profile.ReadCommunity!),
                    variables,
                    GetTimeout(profile));

            if (result.Count == 0)
            {
                throw new InvalidOperationException(
                    "The radio returned an empty SNMP response.");
            }

            return result[0].Data.ToString();
        }

        private static string PollV3(
            SnmpProfileDetailDto profile,
            IPEndPoint endpoint,
            IList<Variable> variables)
        {
            var auth =
                CreateAuthenticationProvider(profile);

            var privacy =
                CreatePrivacyProvider(
                    profile,
                    auth);

            var discovery =
                Messenger.GetNextDiscovery(
                    SnmpType.GetRequestPdu);

            var report =
                discovery.GetResponse(
                    GetTimeout(profile),
                    endpoint);

            ISnmpMessage request =
                new GetRequestMessage(
                    VersionCode.V3,
                    Messenger.NextMessageId,
                    Messenger.NextRequestId,
                    new OctetString(
                        profile.UsmUser!),
                    new OctetString(
                        profile.ContextName
                        ?? string.Empty),
                    variables,
                    privacy,
                    Messenger.MaxMessageSize,
                    report);

            var reply =
                request.GetResponse(
                    GetTimeout(profile),
                    endpoint);

            if (reply is ReportMessage)
            {
                if (reply.Pdu().Variables.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Unexpected empty SNMPv3 report message.");
                }

                var id =
                    reply.Pdu().Variables[0].Id;

                if (id != Messenger.NotInTimeWindow)
                {
                    throw new InvalidOperationException(
                        id.GetErrorMessage());
                }

                request =
                    new GetRequestMessage(
                        VersionCode.V3,
                        Messenger.NextMessageId,
                        Messenger.NextRequestId,
                        new OctetString(
                            profile.UsmUser!),
                        new OctetString(
                            profile.ContextName
                            ?? string.Empty),
                        variables,
                        privacy,
                        Messenger.MaxMessageSize,
                        reply);

                reply =
                    request.GetResponse(
                        GetTimeout(profile),
                        endpoint);
            }

            if (reply.Pdu()
                    .ErrorStatus
                    .ToInt32() != 0)
            {
                throw ErrorException.Create(
                    "error in response",
                    endpoint.Address,
                    reply);
            }

            if (reply.Pdu().Variables.Count == 0)
            {
                throw new InvalidOperationException(
                    "The radio returned an empty SNMP response.");
            }

            return reply.Pdu()
                .Variables[0]
                .Data
                .ToString();
        }

        private static string SetV2(
            SnmpProfileDetailDto profile,
            IPEndPoint endpoint,
            IList<Variable> variables)
        {
            var result =
                Messenger.Set(
                    VersionCode.V2,
                    endpoint,
                    new OctetString(
                        profile.WriteCommunity!),
                    variables,
                    GetTimeout(profile));

            if (result.Count == 0)
            {
                throw new InvalidOperationException(
                    "The radio returned an empty SNMP SET response.");
            }

            return result[0].Data.ToString();
        }

        private static string SetV3(
            SnmpProfileDetailDto profile,
            IPEndPoint endpoint,
            IList<Variable> variables)
        {
            var auth =
                CreateAuthenticationProvider(profile);

            var privacy =
                CreatePrivacyProvider(
                    profile,
                    auth);

            var discovery =
                Messenger.GetNextDiscovery(
                    SnmpType.GetRequestPdu);

            var report =
                discovery.GetResponse(
                    GetTimeout(profile),
                    endpoint);

            ISnmpMessage request =
                new SetRequestMessage(
                    VersionCode.V3,
                    Messenger.NextMessageId,
                    Messenger.NextRequestId,
                    new OctetString(
                        profile.UsmUser!),
                    new OctetString(
                        profile.ContextName
                        ?? string.Empty),
                    variables,
                    privacy,
                    Messenger.MaxMessageSize,
                    report);

            var reply =
                request.GetResponse(
                    GetTimeout(profile),
                    endpoint);

            if (reply is ReportMessage)
            {
                if (reply.Pdu().Variables.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Unexpected empty SNMPv3 report message.");
                }

                var id =
                    reply.Pdu().Variables[0].Id;

                if (id != Messenger.NotInTimeWindow)
                {
                    throw new InvalidOperationException(
                        id.GetErrorMessage());
                }

                request =
                    new SetRequestMessage(
                        VersionCode.V3,
                        Messenger.NextMessageId,
                        Messenger.NextRequestId,
                        new OctetString(
                            profile.UsmUser!),
                        new OctetString(
                            profile.ContextName
                            ?? string.Empty),
                        variables,
                        privacy,
                        Messenger.MaxMessageSize,
                        reply);

                reply =
                    request.GetResponse(
                        GetTimeout(profile),
                        endpoint);
            }

            if (reply.Pdu()
                    .ErrorStatus
                    .ToInt32() != 0)
            {
                throw ErrorException.Create(
                    "error in response",
                    endpoint.Address,
                    reply);
            }

            if (reply.Pdu().Variables.Count == 0)
            {
                throw new InvalidOperationException(
                    "The radio returned an empty SNMP SET response.");
            }

            return reply.Pdu()
                .Variables[0]
                .Data
                .ToString();
        }

        private static string DecodeValue(
            SnmpOidConfigDto oid,
            string rawValue)
        {
            var mode =
                string.IsNullOrWhiteSpace(
                    oid.DecodeMode)
                    ? "Raw"
                    : oid.DecodeMode.Trim();

            if (mode.Equals(
                    "Formula",
                    StringComparison.OrdinalIgnoreCase))
            {
                var cleanRaw =
                    (rawValue ?? string.Empty).Trim();

                if (!decimal.TryParse(
                        cleanRaw,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var rawNumber))
                {
                    return cleanRaw;
                }

                if (!SnmpFormulaEvaluator.TryEvaluate(
                        oid.ReadFormula,
                        rawNumber,
                        out var decodedNumber))
                {
                    return cleanRaw;
                }

                var formatted =
                    oid.DecimalPlaces.HasValue
                        ? Math.Round(
                                decodedNumber,
                                oid.DecimalPlaces.Value,
                                MidpointRounding.AwayFromZero)
                            .ToString(
                                $"F{oid.DecimalPlaces.Value}",
                                CultureInfo.InvariantCulture)
                        : decodedNumber.ToString(
                            "0.##########",
                            CultureInfo.InvariantCulture);

                var unit =
                    (oid.UnitLabel
                     ?? string.Empty).Trim();

                var display =
                    string.IsNullOrWhiteSpace(unit)
                        ? formatted
                        : $"{formatted} {unit}";

                return oid.ShowRawValueAlongsideDecoded
                    ? $"{display} ({cleanRaw})"
                    : display;
            }

            if (!mode.Equals(
                    "ValueMap",
                    StringComparison.OrdinalIgnoreCase))
            {
                return rawValue;
            }

            var cleanValue =
                (rawValue ?? string.Empty).Trim();

            var match =
                (oid.DecodeValues
                 ?? new List<SnmpOidDecodeValueDto>())
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.RawValue)
                .FirstOrDefault(
                    x => string.Equals(
                        (x.RawValue
                         ?? string.Empty).Trim(),
                        cleanValue,
                        StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                return rawValue
                    ?? string.Empty;
            }

            var displayText =
                match.DisplayText
                ?? string.Empty;

            return oid.ShowRawValueAlongsideDecoded
                ? $"{displayText} ({cleanValue})"
                : displayText;
        }

        private static string BuildRawSetValue(
            SnmpOidConfigDto oid,
            string requestedValue)
        {
            var mode =
                string.IsNullOrWhiteSpace(
                    oid.DecodeMode)
                    ? "Raw"
                    : oid.DecodeMode.Trim();

            if (!mode.Equals(
                    "Formula",
                    StringComparison.OrdinalIgnoreCase))
            {
                return requestedValue.Trim();
            }

            if (!string.Equals(
                    oid.ValueType,
                    "Integer",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"OID '{oid.Label}' uses Formula Decode, but its Type is not Integer.");
            }

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

        private static ISnmpData BuildSnmpData(
            string? valueType,
            string rawValue)
        {
            var type =
                string.IsNullOrWhiteSpace(valueType)
                    ? "String"
                    : valueType.Trim();

            var cleanRaw =
                (rawValue ?? string.Empty).Trim();

            if (type.Equals(
                    "Integer",
                    StringComparison.OrdinalIgnoreCase))
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

            if (type.Equals(
                    "Gauge",
                    StringComparison.OrdinalIgnoreCase))
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

            if (type.Equals(
                    "Counter",
                    StringComparison.OrdinalIgnoreCase))
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

            if (type.Equals(
                    "IpAddress",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new IP(cleanRaw);
            }

            return new OctetString(cleanRaw);
        }

        private static int GetTimeout(
            SnmpProfileDetailDto profile)
        {
            return profile.TimeoutMs > 0
                ? profile.TimeoutMs
                : 1500;
        }

        private static bool IsV3(
            SnmpProfileDetailDto profile)
        {
            var version =
                string.IsNullOrWhiteSpace(
                    profile.SnmpVersion)
                    ? "v3"
                    : profile.SnmpVersion.Trim();

            return version.Equals(
                "v3",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAuthProtocol(
            string? protocol)
        {
            return string.IsNullOrWhiteSpace(protocol)
                ? "MD5"
                : protocol.Trim();
        }

        private static string NormalizePrivacyProtocol(
            string? protocol)
        {
            return string.IsNullOrWhiteSpace(protocol)
                ? "DES"
                : protocol.Trim();
        }

        private static IAuthenticationProvider
            CreateAuthenticationProvider(
                SnmpProfileDetailDto profile)
        {
            var authProtocol =
                NormalizeAuthProtocol(
                    profile.AuthProtocol);

#pragma warning disable CS0618

            if (authProtocol.Equals(
                    "SHA-512",
                    StringComparison.OrdinalIgnoreCase) ||
                authProtocol.Equals(
                    "SHA512",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new SHA512AuthenticationProvider(
                    new OctetString(profile.AuthKey!));
            }

            if (authProtocol.Equals(
                    "SHA-384",
                    StringComparison.OrdinalIgnoreCase) ||
                authProtocol.Equals(
                    "SHA384",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new SHA384AuthenticationProvider(
                    new OctetString(profile.AuthKey!));
            }

            if (authProtocol.Equals(
                    "SHA-256",
                    StringComparison.OrdinalIgnoreCase) ||
                authProtocol.Equals(
                    "SHA256",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new SHA256AuthenticationProvider(
                    new OctetString(profile.AuthKey!));
            }

            if (authProtocol.Equals(
                    "SHA-1",
                    StringComparison.OrdinalIgnoreCase) ||
                authProtocol.Equals(
                    "SHA",
                    StringComparison.OrdinalIgnoreCase) ||
                authProtocol.Equals(
                    "SHA1",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new SHA1AuthenticationProvider(
                    new OctetString(profile.AuthKey!));
            }

            return new MD5AuthenticationProvider(
                new OctetString(profile.AuthKey!));

#pragma warning restore CS0618
        }

        private static IPrivacyProvider
            CreatePrivacyProvider(
                SnmpProfileDetailDto profile,
                IAuthenticationProvider auth)
        {
            var privacyProtocol =
                NormalizePrivacyProtocol(
                    profile.PrivacyProtocol);

#pragma warning disable CS0618

            if (privacyProtocol.Equals(
                    "AES-256",
                    StringComparison.OrdinalIgnoreCase) ||
                privacyProtocol.Equals(
                    "AES256",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new AES256PrivacyProvider(
                    new OctetString(
                        profile.PrivacyKey!),
                    auth);
            }

            if (privacyProtocol.Equals(
                    "AES-128",
                    StringComparison.OrdinalIgnoreCase) ||
                privacyProtocol.Equals(
                    "AES128",
                    StringComparison.OrdinalIgnoreCase) ||
                privacyProtocol.Equals(
                    "AES",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new AESPrivacyProvider(
                    new OctetString(
                        profile.PrivacyKey!),
                    auth);
            }

            return new DESPrivacyProvider(
                new OctetString(
                    profile.PrivacyKey!),
                auth);

#pragma warning restore CS0618
        }

        private static SnmpRunResultDto Fail(
            string error,
            string targetIp,
            SnmpProfileDetailDto? profile,
            SnmpOidConfigDto? oid)
        {
            return new SnmpRunResultDto
            {
                Success = false,
                TargetIp = targetIp ?? string.Empty,
                ProfileName = profile?.Name
                              ?? string.Empty,
                Label = oid?.Label
                        ?? string.Empty,
                Oid = oid?.Oid
                      ?? string.Empty,
                DecodeMode = oid?.DecodeMode
                             ?? string.Empty,
                RawValue = string.Empty,
                DisplayValue = string.Empty,
                ErrorMessage = error
            };
        }

        private static SnmpSetResultDto FailSet(
            string error,
            string targetIp,
            string requestedValue,
            SnmpProfileDetailDto? profile,
            SnmpOidConfigDto? oid)
        {
            return new SnmpSetResultDto
            {
                Success = false,
                TargetIp = targetIp ?? string.Empty,
                ProfileName = profile?.Name
                              ?? string.Empty,
                Label = oid?.Label
                        ?? string.Empty,
                Oid = oid?.Oid
                      ?? string.Empty,
                DecodeMode = oid?.DecodeMode
                             ?? string.Empty,
                RequestedValue = requestedValue
                                 ?? string.Empty,
                RawValue = string.Empty,
                DisplayValue = string.Empty,
                ErrorMessage = error
            };
        }
    }
}