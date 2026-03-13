using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using TradingSystem.Auth.Options;

namespace TradingSystem.Auth.Services
{
    public sealed class TradeSessionCacheService
    {
        private readonly IDistributedCache _cache;
        private readonly AuthenticationSettings _settings;

        public TradeSessionCacheService(IDistributedCache cache, IOptions<AuthenticationSettings> settings)
        {
            _cache = cache;
            _settings = settings.Value;
        }

        public async Task<TradeSessionInfo> StartSessionAsync(string username, long tradeAccountId, CancellationToken cancellationToken = default)
        {
            var session = new TradeSessionInfo(
                Guid.NewGuid().ToString("N"),
                username,
                tradeAccountId,
                DateTime.UtcNow.AddMinutes(_settings.SessionMinutes));

            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_settings.SessionMinutes)
            };

            await _cache.SetStringAsync(
                GetCacheKey(username),
                JsonSerializer.Serialize(session),
                cacheOptions,
                cancellationToken);

            return session;
        }

        public async Task<bool> IsSessionValidAsync(string username, string sessionId, CancellationToken cancellationToken = default)
        {
            var session = await GetSessionAsync(username, cancellationToken);
            return session != null
                && string.Equals(session.SessionId, sessionId, StringComparison.Ordinal)
                && session.ExpiresAtUtc > DateTime.UtcNow;
        }

        public async Task<TradeSessionInfo?> GetSessionAsync(string username, CancellationToken cancellationToken = default)
        {
            var payload = await _cache.GetStringAsync(GetCacheKey(username), cancellationToken);
            return string.IsNullOrWhiteSpace(payload)
                ? null
                : JsonSerializer.Deserialize<TradeSessionInfo>(payload);
        }

        public Task RevokeAsync(string username, CancellationToken cancellationToken = default)
        {
            return _cache.RemoveAsync(GetCacheKey(username), cancellationToken);
        }

        private static string GetCacheKey(string username)
        {
            return $"auth:user:{username.Trim().ToLowerInvariant()}";
        }
    }

    public sealed record TradeSessionInfo(
        string SessionId,
        string Username,
        long TradeAccountId,
        DateTime ExpiresAtUtc);
}
