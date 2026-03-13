using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Auth.DTOs;
using TradingSystem.Auth.Services;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Security;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Auth.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = AuthorizationPolicies.AccountsManage)]
    public class TradeAccountsController : ControllerBase
    {
        private readonly TradingDbContext _dbContext;
        private readonly PasswordHashService _passwordHashService;
        private readonly TradeSessionCacheService _sessionCacheService;

        public TradeAccountsController(
            TradingDbContext dbContext,
            PasswordHashService passwordHashService,
            TradeSessionCacheService sessionCacheService)
        {
            _dbContext = dbContext;
            _passwordHashService = passwordHashService;
            _sessionCacheService = sessionCacheService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAccounts(CancellationToken cancellationToken)
        {
            var accounts = await QueryAccounts()
                .AsNoTracking()
                .OrderBy(account => account.Id)
                .ToListAsync(cancellationToken);

            return Ok(accounts.Select(ToResponse));
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetAccount(long id, CancellationToken cancellationToken)
        {
            var account = await QueryAccounts()
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            return account == null
                ? NotFound(new { Message = $"Trade account {id} was not found." })
                : Ok(ToResponse(account));
        }

        [HttpPost]
        public async Task<IActionResult> CreateAccount([FromBody] CreateTradeAccountRequest request, CancellationToken cancellationToken)
        {
            var normalizedUsername = request.Username.Trim();
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            if (await _dbContext.TradeAccounts.AnyAsync(account => account.Username == normalizedUsername, cancellationToken))
            {
                return Conflict(new { Message = $"Username '{normalizedUsername}' already exists." });
            }

            if (await _dbContext.TradeAccounts.AnyAsync(account => account.Email == normalizedEmail, cancellationToken))
            {
                return Conflict(new { Message = $"Email '{normalizedEmail}' already exists." });
            }

            var groups = await ResolveGroupsAsync(request.GroupNames, cancellationToken);
            if (groups.Count == 0)
            {
                return BadRequest(new { Message = "At least one valid group is required." });
            }

            var (hash, salt) = _passwordHashService.CreateHash(request.Password);
            var account = new TradeAccount
            {
                Name = request.Name.Trim(),
                Username = normalizedUsername,
                Email = normalizedEmail,
                PasswordHash = hash,
                PasswordSalt = salt,
                IsDisabled = false,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.TradeAccounts.Add(account);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _dbContext.TradeAccountGroups.AddRange(groups.Select(group => new TradeAccountGroup
            {
                TradeAccountId = account.Id,
                TradeUserGroupId = group.Id
            }));

            await _dbContext.SaveChangesAsync(cancellationToken);

            var created = await QueryAccounts()
                .AsNoTracking()
                .SingleAsync(existing => existing.Id == account.Id, cancellationToken);

            return Created($"/api/tradeaccounts/{account.Id}", ToResponse(created));
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> UpdateAccount(long id, [FromBody] UpdateTradeAccountRequest request, CancellationToken cancellationToken)
        {
            var account = await QueryAccounts()
                .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            if (account == null)
            {
                return NotFound(new { Message = $"Trade account {id} was not found." });
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                account.Name = request.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                var normalizedUsername = request.Username.Trim();
                var usernameExists = await _dbContext.TradeAccounts.AnyAsync(existing =>
                    existing.Id != id &&
                    existing.Username == normalizedUsername,
                    cancellationToken);

                if (usernameExists)
                {
                    return Conflict(new { Message = $"Username '{normalizedUsername}' already exists." });
                }

                if (!string.Equals(account.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase))
                {
                    await _sessionCacheService.RevokeAsync(account.Username, cancellationToken);
                }

                account.Username = normalizedUsername;
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var normalizedEmail = request.Email.Trim().ToLowerInvariant();
                var emailExists = await _dbContext.TradeAccounts.AnyAsync(existing =>
                    existing.Id != id &&
                    existing.Email == normalizedEmail,
                    cancellationToken);

                if (emailExists)
                {
                    return Conflict(new { Message = $"Email '{normalizedEmail}' already exists." });
                }

                account.Email = normalizedEmail;
            }

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                var (hash, salt) = _passwordHashService.CreateHash(request.Password);
                account.PasswordHash = hash;
                account.PasswordSalt = salt;
                await _sessionCacheService.RevokeAsync(account.Username, cancellationToken);
            }

            if (request.GroupNames != null)
            {
                var groups = await ResolveGroupsAsync(request.GroupNames, cancellationToken);
                if (groups.Count == 0)
                {
                    return BadRequest(new { Message = "At least one valid group is required." });
                }

                _dbContext.TradeAccountGroups.RemoveRange(account.AccountGroups);
                account.AccountGroups.Clear();
                foreach (var group in groups)
                {
                    account.AccountGroups.Add(new TradeAccountGroup
                    {
                        TradeAccountId = account.Id,
                        TradeUserGroupId = group.Id
                    });
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(ToResponse(account));
        }

        [HttpPut("{id:long}/disable")]
        public async Task<IActionResult> DisableAccount(long id, [FromBody] DisableTradeAccountRequest request, CancellationToken cancellationToken)
        {
            var account = await _dbContext.TradeAccounts.SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);
            if (account == null)
            {
                return NotFound(new { Message = $"Trade account {id} was not found." });
            }

            account.IsDisabled = request.IsDisabled;
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (request.IsDisabled)
            {
                await _sessionCacheService.RevokeAsync(account.Username, cancellationToken);
            }

            return Ok(new
            {
                account.Id,
                account.Username,
                account.IsDisabled
            });
        }

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> DeleteAccount(long id, CancellationToken cancellationToken)
        {
            var account = await _dbContext.TradeAccounts.SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);
            if (account == null)
            {
                return NotFound(new { Message = $"Trade account {id} was not found." });
            }

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.Equals(currentUserId, id.ToString(), StringComparison.Ordinal))
            {
                return Conflict(new { Message = "You cannot delete the account currently being used for this request." });
            }

            await _sessionCacheService.RevokeAsync(account.Username, cancellationToken);

            var accountGroups = await _dbContext.TradeAccountGroups
                .Where(link => link.TradeAccountId == id)
                .ToListAsync(cancellationToken);

            _dbContext.TradeAccountGroups.RemoveRange(accountGroups);
            _dbContext.TradeAccounts.Remove(account);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new { Message = $"Trade account {id} was deleted." });
        }

        private IQueryable<TradeAccount> QueryAccounts()
        {
            return _dbContext.TradeAccounts
                .Include(account => account.AccountGroups)
                    .ThenInclude(link => link.TradeUserGroup)
                        .ThenInclude(group => group!.GroupPermissions)
                            .ThenInclude(link => link.TradePermission);
        }

        private async Task<List<TradeUserGroup>> ResolveGroupsAsync(string[]? requestedGroupNames, CancellationToken cancellationToken)
        {
            var normalizedGroupNames = (requestedGroupNames == null || requestedGroupNames.Length == 0
                    ? new[] { TradeGroupNames.Traders }
                    : requestedGroupNames)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return await _dbContext.TradeUserGroups
                .Where(group => normalizedGroupNames.Contains(group.Name))
                .ToListAsync(cancellationToken);
        }

        private static object ToResponse(TradeAccount account)
        {
            var groups = account.AccountGroups
                .Select(link => link.TradeUserGroup!.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToArray();
            var permissions = account.AccountGroups
                .SelectMany(link => link.TradeUserGroup!.GroupPermissions)
                .Select(link => link.TradePermission!.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToArray();

            return new
            {
                account.Id,
                account.Name,
                account.Username,
                account.Email,
                account.IsDisabled,
                account.CreatedAt,
                account.LastLoginAt,
                Groups = groups,
                Permissions = permissions
            };
        }
    }
}
