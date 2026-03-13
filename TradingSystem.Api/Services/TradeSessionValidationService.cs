using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace TradingSystem.Api.Services
{
    public sealed class TradeSessionValidationService
    {
        private readonly IDistributedCache _cache;

        public TradeSessionValidationService(IDistributedCache cache)
        {
            _cache = cache;
        }

        public async Task<bool> IsSessionValidAsync(string username, string sessionId, CancellationToken cancellationToken = default)
        {
            var payload = await _cache.GetStringAsync(GetCacheKey(username), cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            var session = JsonSerializer.Deserialize<TradeSessionInfo>(payload);
            return session != null
                && string.Equals(session.SessionId, sessionId, StringComparison.Ordinal)
                && session.ExpiresAtUtc > DateTime.UtcNow;
        }

        private static string GetCacheKey(string username)
        {
            return $"auth:user:{username.Trim().ToLowerInvariant()}";
        }

        private sealed record TradeSessionInfo(
            string SessionId,
            string Username,
            long TradeAccountId,
            DateTime ExpiresAtUtc);
    }
}
