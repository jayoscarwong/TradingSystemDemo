using System.Security.Cryptography;
using System.Text;

namespace TradingSystem.Auth.Services
{
    public sealed class PasswordHashService
    {
        public (byte[] Hash, byte[] Salt) CreateHash(string password)
        {
            using var hmac = new HMACSHA512();
            var salt = hmac.Key;
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            return (hash, salt);
        }

        public bool Verify(string password, byte[] expectedHash, byte[] salt)
        {
            using var hmac = new HMACSHA512(salt);
            var actualHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
