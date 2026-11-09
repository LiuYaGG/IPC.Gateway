using System.Security.Cryptography;
using System.Text;

namespace IPC.EdgeGateway
{
    public static class OpcUaPasswordHasher
    {
        public const string Algorithm = "PBKDF2-SHA256";
        private const int IterationCount = 100000;
        private const int SaltSize = 16;
        private const int HashSize = 32;

        public static void SetPassword(OpcUaServerOptions options, string password)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            string text = password ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return;

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = DeriveHash(text, salt);

            options.UserPasswordSalt = Convert.ToBase64String(salt);
            options.UserPasswordHash = Convert.ToBase64String(hash);
            options.UserPasswordAlgorithm = Algorithm;
        }

        public static bool VerifyPassword(OpcUaServerOptions options, string username, string password)
        {
            if (options == null || !IsPasswordConfigured(options))
                return false;

            string expectedUser = options.Username?.Trim() ?? string.Empty;
            string actualUser = username?.Trim() ?? string.Empty;
            if (!string.Equals(expectedUser, actualUser, StringComparison.Ordinal))
                return false;

            try
            {
                byte[] salt = Convert.FromBase64String(options.UserPasswordSalt);
                byte[] expectedHash = Convert.FromBase64String(options.UserPasswordHash);
                byte[] actualHash = DeriveHash(password ?? string.Empty, salt);
                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPasswordConfigured(OpcUaServerOptions options)
        {
            return options != null &&
                   !string.IsNullOrWhiteSpace(options.Username) &&
                   !string.IsNullOrWhiteSpace(options.UserPasswordHash) &&
                   !string.IsNullOrWhiteSpace(options.UserPasswordSalt);
        }

        private static byte[] DeriveHash(string password, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password ?? string.Empty),
                salt,
                IterationCount,
                HashAlgorithmName.SHA256,
                HashSize);
        }
    }
}
