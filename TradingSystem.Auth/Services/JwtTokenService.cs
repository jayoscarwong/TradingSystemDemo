using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TradingSystem.Auth.Options;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Security;

namespace TradingSystem.Auth.Services
{
    public sealed class JwtTokenService
    {
        private readonly AuthenticationSettings _settings;

        public JwtTokenService(IOptions<AuthenticationSettings> settings)
        {
            _settings = settings.Value;
        }

        public JwtTokenResult CreateToken(
            TradeAccount account,
            IEnumerable<string> groups,
            IEnumerable<string> permissions,
            string sessionId)
        {
            var nowUtc = DateTime.UtcNow;
            var expiresAtUtc = nowUtc.AddMinutes(_settings.SessionMinutes);
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
                new(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new(ClaimTypes.Name, account.Username),
                new(JwtRegisteredClaimNames.UniqueName, account.Username),
                new(JwtRegisteredClaimNames.Email, account.Email),
                new("display_name", account.Name),
                new(CustomClaimTypes.SessionId, sessionId)
            };

            claims.AddRange(groups.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(group => new[]
            {
                new Claim(ClaimTypes.Role, group),
                new Claim(CustomClaimTypes.Group, group)
            }));
            claims.AddRange(permissions.Distinct(StringComparer.OrdinalIgnoreCase).Select(permission => new Claim(CustomClaimTypes.Permission, permission)));

            var signingCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                notBefore: nowUtc,
                expires: expiresAtUtc,
                signingCredentials: signingCredentials);

            return new JwtTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
        }
    }

    public sealed record JwtTokenResult(string AccessToken, DateTime ExpiresAtUtc);
}
