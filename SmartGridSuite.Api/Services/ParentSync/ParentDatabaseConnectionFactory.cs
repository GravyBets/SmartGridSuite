using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SmartGridSuite.Api.Configuration;

namespace SmartGridSuite.Api.Services.ParentSync
{
    public sealed class ParentDatabaseConnectionFactory
    {
        private readonly ParentDatabaseOptions _options;

        public ParentDatabaseConnectionFactory(
            IOptions<ParentDatabaseOptions> options)
        {
            _options =
                options.Value;
        }

        public SqlConnection CreateConnection()
        {
            if (string.IsNullOrWhiteSpace(
                    _options.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Parent database connection string is not configured.");
            }

            var connection =
                new SqlConnection(
                    _options.ConnectionString);

            if (!_options.UseExplicitWindowsCredentials)
            {
                return connection;
            }

            if (string.IsNullOrWhiteSpace(
                    _options.WindowsDomain) ||
                string.IsNullOrWhiteSpace(
                    _options.WindowsUsername) ||
                string.IsNullOrWhiteSpace(
                    _options.WindowsPassword))
            {
                throw new InvalidOperationException(
                    "Parent database Windows credentials are not fully configured.");
            }

            connection.SspiContextProvider =
                new ExplicitWindowsSspiContextProvider(
                    _options.WindowsDomain,
                    _options.WindowsUsername,
                    _options.WindowsPassword);

            return connection;
        }
    }
}