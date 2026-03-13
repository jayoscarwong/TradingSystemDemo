using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TradingSystem.Auth.Options;

namespace TradingSystem.Auth.Services
{
    public sealed class RefreshTokenService
    {
        private readonly AuthenticationSettings _settings;

        public RefreshTokenService(IOptions<AuthenticationSettings> settings)
        {
            _settings = settings.Value;
        }

        public RefreshTokenPayload Create()
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

            return new RefreshTokenPayload(
                token,
                tokenHash,
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(_settings.RefreshTokenDays));
        }

        public string ComputeHash(string refreshToken)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
        }
    }

    public sealed record RefreshTokenPayload(
        string Token,
        string TokenHash,
        DateTime CreatedAtUtc,
        DateTime ExpiresAtUtc);
}
