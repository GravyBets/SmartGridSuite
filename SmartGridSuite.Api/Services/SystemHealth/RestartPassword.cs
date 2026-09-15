using System.Security.Cryptography;

namespace SmartGridSuite.Api.Services.SystemHealth
{
    public static class RestartPassword
    {
        // Server stores only this salted PBKDF2 verifier, never the shared password.
        public static bool Verify(string? password, string? encoded)
        {
            if (string.IsNullOrEmpty(password) || password.Length > 256 ||
                string.IsNullOrWhiteSpace(encoded))
                return false;

            try
            {
                var parts = encoded.Split(':');
                if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" ||
                    parts[1] != "210000")
                    return false;

                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                if (salt.Length != 16 || expected.Length != 32)
                    return false;

                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    password, salt, 210000, HashAlgorithmName.SHA256, 32);
                try
                {
                    return CryptographicOperations.FixedTimeEquals(actual, expected);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(actual);
                }
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
