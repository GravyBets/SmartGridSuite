using System.Buffers;
using System.Net;
using System.Net.Security;
using Microsoft.Data.SqlClient;

namespace SmartGridSuite.Api.Services.ParentSync
{
    /*
     * Supplies explicit AD credentials to Microsoft.Data.SqlClient
     * on Linux.
     *
     * Package = "NTLM" is intentional. This allows the API to use the
     * dedicated Windows service account without requiring the Linux VM
     * itself to be domain joined or relying on a Kerberos ticket cache.
     */
    public sealed class ExplicitWindowsSspiContextProvider
        : SspiContextProvider
    {
        private readonly NetworkCredential _credential;

        private readonly string _identityKey;

        private NegotiateAuthentication? _authentication;

        public ExplicitWindowsSspiContextProvider(
            string domain,
            string username,
            string password)
        {
            _credential =
                new NetworkCredential(
                    username,
                    password,
                    domain);

            /*
             * SqlClient includes SspiContextProvider in its connection
             * pool identity. Keep Equals/GetHashCode stable for the same
             * service identity while still allowing each connection to
             * have its own authentication exchange state.
             */
            _identityKey =
                $"{domain}\\{username}";
        }

        protected override bool GenerateContext(
            ReadOnlySpan<byte> incomingBlob,
            IBufferWriter<byte> outgoingBlobWriter,
            SspiAuthenticationParameters authParams)
        {
            _authentication ??=
                new NegotiateAuthentication(
                    new NegotiateAuthenticationClientOptions
                    {
                        Package = "NTLM",

                        Credential =
                            _credential,

                        TargetName =
                            authParams.Resource
                    });

            var outgoingBlob =
                _authentication.GetOutgoingBlob(
                    incomingBlob,
                    out var statusCode);

            if (statusCode is not
                    NegotiateAuthenticationStatusCode.Completed &&
                statusCode is not
                    NegotiateAuthenticationStatusCode.ContinueNeeded)
            {
                return false;
            }

            if (outgoingBlob is not null)
            {
                outgoingBlobWriter.Write(
                    outgoingBlob);
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return
                obj is ExplicitWindowsSspiContextProvider other &&
                StringComparer.OrdinalIgnoreCase.Equals(
                    _identityKey,
                    other._identityKey);
        }

        public override int GetHashCode()
        {
            return
                StringComparer.OrdinalIgnoreCase.GetHashCode(
                    _identityKey);
        }
    }
}