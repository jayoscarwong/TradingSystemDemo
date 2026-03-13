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
    public class TradeAccountsController : ControllerBase
    {
        private static readonly string[] SelfRegistrationGroups =
        {
            TradeGroupNames.Traders,
            TradeGroupNames.Visitors
        };

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

        /// <summary>
        /// Lists all trade accounts with their groups, permissions, and approval status.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     GET /api/TradeAccounts
        /// </remarks>
        [HttpGet]
        [Authorize(Policy = AuthorizationPolicies.AccountsManage)]
        public async Task<IActionResult> GetAccounts(CancellationToken cancellationToken)
        {
            var accounts = await QueryAccounts()
                .AsNoTracking()
                .OrderBy(account => account.Id)
                .ToListAsync(cancellationToken);

            return Ok(accounts.Select(ToResponse));
        }

        /// <summary>
        /// Returns one trade account by numeric identifier.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     GET /api/TradeAccounts/12
        /// </remarks>
        [HttpGet("{id:long}")]
        [Authorize(Policy = AuthorizationPolicies.AccountsManage)]
        public async Task<IActionResult> GetAccount(long id, CancellationToken cancellationToken)
        {
            var account = await QueryAccounts()
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            return account == null
                ? NotFound(new { Message = $"Trade account {id} was not found." })
                : Ok(ToResponse(account));
        }

        /// <summary>
        /// Returns the currently authenticated account status, groups, and permissions.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     GET /api/TradeAccounts/me/status
        /// </remarks>
        [HttpGet("me/status")]
        [Authorize]
        public async Task<IActionResult> GetMyStatus(CancellationToken cancellationToken)
        {
            var accountIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!long.TryParse(accountIdClaim, out var accountId))
            {
                return BadRequest(new { Message = "Authenticated account information is missing from the token." });
            }

            var account = await QueryAccounts()
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound(new { Message = $"Trade account {accountId} was not found." });
            }

            return Ok(new
            {
                Account = ToResponse(account),
                TokenGroups = User.FindAll(CustomClaimTypes.Group).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value),
                TokenPermissions = User.FindAll(CustomClaimTypes.Permission).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value)
            });
        }

        /// <summary>
        /// Creates a trade account directly as an administrator.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST /api/TradeAccounts
        ///     {
        ///       "name": "Desk Trader 01",
        ///       "username": "desk.trader01",
        ///       "email": "desk.trader01@tradingsystem.local",
        ///       "password": "Trader123!",
        ///       "groupNames": [ "Traders" ],
        ///       "startDisabled": false
        ///     }
        /// </remarks>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.AccountsManage)]
        public async Task<IActionResult> CreateAccount([FromBody] CreateTradeAccountRequest request, CancellationToken cancellationToken)
        {
            var validationError = await ValidateUniqueIdentityAsync(request.Username, request.Email, null, cancellationToken);
            if (validationError != null)
            {
                return validationError;
            }

            var groups = await ResolveGroupsAsync(request.GroupNames, allowSelfRegistrationOnly: false, cancellationToken);
            if (groups.Count == 0)
            {
                return BadRequest(new { Message = "At least one valid group is required." });
            }

            var (hash, salt) = _passwordHashService.CreateHash(request.Password);
            var account = new TradeAccount
            {
                Name = request.Name.Trim(),
                Username = request.Username.Trim(),
                Email = request.Email.Trim().ToLowerInvariant(),
                PasswordHash = hash,
                PasswordSalt = salt,
                IsDisabled = request.StartDisabled,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.TradeAccounts.Add(account);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await ReplaceAccountGroupsAsync(account, groups, cancellationToken);

            var created = await QueryAccounts()
                .AsNoTracking()
                .SingleAsync(existing => existing.Id == account.Id, cancellationToken);

            return Created($"/api/TradeAccounts/{account.Id}", ToResponse(created));
        }

        /// <summary>
        /// Self-registers a trader or visitor account.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST /api/TradeAccounts/register
        ///     {
        ///       "name": "Oscar Wong",
        ///       "username": "oscar",
        ///       "email": "oscar@example.com",
        ///       "password": "12345678",
        ///       "requestedGroup": "Traders"
        ///     }
        ///
        /// Allowed values for <c>requestedGroup</c> are <c>Traders</c>, <c>Visitors</c>, <c>Visitor</c>, and <c>Viewer</c>.
        /// Self-registered accounts start in disabled mode until an administrator enables them.
        /// </remarks>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterTradeAccountRequest request, CancellationToken cancellationToken)
        {
            var validationError = await ValidateUniqueIdentityAsync(request.Username, request.Email, null, cancellationToken);
            if (validationError != null)
            {
                return validationError;
            }

            var normalizedGroupName = NormalizeSelfRegistrationGroup(request.RequestedGroup);
            if (normalizedGroupName == null)
            {
                return BadRequest(new
                {
                    Message = $"RequestedGroup must be one of: {string.Join(", ", SelfRegistrationGroups)}."
                });
            }

            var groups = await ResolveGroupsAsync(new[] { normalizedGroupName }, allowSelfRegistrationOnly: true, cancellationToken);
            if (groups.Count == 0)
            {
                return BadRequest(new { Message = $"Group '{normalizedGroupName}' is not configured." });
            }

            var (hash, salt) = _passwordHashService.CreateHash(request.Password);
            var account = new TradeAccount
            {
                Name = request.Name.Trim(),
                Username = request.Username.Trim(),
                Email = request.Email.Trim().ToLowerInvariant(),
                PasswordHash = hash,
                PasswordSalt = salt,
                IsDisabled = true,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.TradeAccounts.Add(account);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await ReplaceAccountGroupsAsync(account, groups, cancellationToken);

            var created = await QueryAccounts()
                .AsNoTracking()
                .SingleAsync(existing => existing.Id == account.Id, cancellationToken);

            return Created($"/api/TradeAccounts/{account.Id}", new
            {
                Message = "Account registration received. The account is disabled until an administrator enables it.",
                Account = ToResponse(created)
            });
        }

        /// <summary>
        /// Updates a trade account profile, password, and group memberships.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     PUT /api/TradeAccounts/12
        ///     {
        ///       "name": "Oscar Wong",
        ///       "email": "oscar+desk@example.com",
        ///       "groupNames": [ "Traders", "Visitors" ]
        ///     }
        /// </remarks>
        [HttpPut("{id:long}")]
        [Authorize(Policy = AuthorizationPolicies.AccountsManage)]
        public async Task<IActionResult> UpdateAccount(long id, [FromBody] UpdateTradeAccountRequest request, CancellationToken cancellationToken)
        {
            var account = await QueryAccounts()
                .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            if (account == null)
            {
                return NotFound(new { Message = $"Trade account {id} was not found." });
            }

            if (!string.IsNullOrWhiteSpace(request.Username) || !string.IsNullOrWhiteSpace(request.Email))
            {
                var validationError = await ValidateUniqueIdentityAsync(
                    request.Username ?? account.Username,
                    request.Email ?? account.Email,
                    account.Id,
                    cancellationToken);

                if (validationError != null)
                {
                    return validationError;
                }
            }

            var shouldRevokeSessions = false;

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                account.Name = request.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                var normalizedUsername = request.Username.Trim();
                if (!string.Equals(account.Username, normalizedUsername, StringComparison.OrdinalIgnoreCase))
                {
                    shouldRevokeSessions = true;
                }

                account.Username = normalizedUsername;
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                account.Email = request.Email.Trim().ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                var (hash, salt) = _passwordHashService.CreateHash(request.Password);
                account.PasswordHash = hash;
                account.PasswordSalt = salt;
                shouldRevokeSessions = true;
            }

            if (request.GroupNames != null)
            {
                var groups = await ResolveGroupsAsync(request.GroupNames, allowSelfRegistrationOnly: false, cancellationToken);
                if (groups.Count == 0)
                {
                    return BadRequest(new { Message = "At least one valid group is required." });
                }

                await ReplaceAccountGroupsAsync(account, groups, cancellationToken, saveAccountFirst: false);
                shouldRevokeSessions = true;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (shouldRevokeSessions)
            {
                await RevokeAccessAsync(account, cancellationToken);
            }

            return Ok(ToResponse(account));
        }

        /// <summary>
        /// Enables a previously disabled trade account.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     PUT /api/TradeAccounts/12/enable
        /// </remarks>
        [HttpPut("{id:long}/enable")]
        [Authorize(Policy = AuthorizationPolicies.AccountsManage)]
        public async Task<IActionResult> EnableAccount(long id, CancellationToken cancellationToken)
        {
            var account = await QueryAccounts()
                .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            if (account == null)
            {
                return NotFound(new { Message = $"Trade account {id} was not found." });
            }

            account.IsDisabled = false;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                Message = $"Trade account {id} is enabled.",
                Account = ToResponse(account)
            });
        }

        /// <summary>
        /// Enables or disables a trade account.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     PUT /api/TradeAccounts/12/disable
        ///     {
        ///       "isDisabled": true
        ///     }
        /// </remarks>
        [HttpPut("{id:long}/disable")]
        [Authorize(Policy = AuthorizationPolicies.AccountsManage)]
        public async Task<IActionResult> DisableAccount(long id, [FromBody] DisableTradeAccountRequest request, CancellationToken cancellationToken)
        {
            var account = await QueryAccounts()
                .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            if (account == null)
            {
                return NotFound(new { Message = $"Trade account {id} was not found." });
            }

            account.IsDisabled = request.IsDisabled;
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (request.IsDisabled)
            {
                await RevokeAccessAsync(account, cancellationToken);
            }

            return Ok(new
            {
                Message = request.IsDisabled
                    ? $"Trade account {id} is disabled."
                    : $"Trade account {id} is enabled.",
                Account = ToResponse(account)
            });
        }

        /// <summary>
        /// Deletes a trade account and revokes its sessions and refresh tokens.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     DELETE /api/TradeAccounts/12
        /// </remarks>
        [HttpDelete("{id:long}")]
        [Authorize(Policy = AuthorizationPolicies.AccountsManage)]
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

            await RevokeAccessAsync(account, cancellationToken);

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

        private async Task<IActionResult?> ValidateUniqueIdentityAsync(
            string username,
            string email,
            long? currentAccountId,
            CancellationToken cancellationToken)
        {
            var normalizedUsername = username.Trim();
            var normalizedEmail = email.Trim().ToLowerInvariant();

            var usernameExists = await _dbContext.TradeAccounts.AnyAsync(account =>
                account.Username == normalizedUsername &&
                (!currentAccountId.HasValue || account.Id != currentAccountId.Value),
                cancellationToken);

            if (usernameExists)
            {
                return Conflict(new { Message = $"Username '{normalizedUsername}' already exists." });
            }

            var emailExists = await _dbContext.TradeAccounts.AnyAsync(account =>
                account.Email == normalizedEmail &&
                (!currentAccountId.HasValue || account.Id != currentAccountId.Value),
                cancellationToken);

            if (emailExists)
            {
                return Conflict(new { Message = $"Email '{normalizedEmail}' already exists." });
            }

            return null;
        }

        private async Task<List<TradeUserGroup>> ResolveGroupsAsync(
            string[]? requestedGroupNames,
            bool allowSelfRegistrationOnly,
            CancellationToken cancellationToken)
        {
            var normalizedGroupNames = (requestedGroupNames == null || requestedGroupNames.Length == 0
                    ? new[] { TradeGroupNames.Traders }
                    : requestedGroupNames)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(NormalizeGroupName)
                .Where(name => name != null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (allowSelfRegistrationOnly)
            {
                normalizedGroupNames = normalizedGroupNames
                    .Where(name => SelfRegistrationGroups.Contains(name, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
            }

            return await _dbContext.TradeUserGroups
                .Where(group => normalizedGroupNames.Contains(group.Name))
                .ToListAsync(cancellationToken);
        }

        private async Task ReplaceAccountGroupsAsync(
            TradeAccount account,
            IReadOnlyCollection<TradeUserGroup> groups,
            CancellationToken cancellationToken,
            bool saveAccountFirst = true)
        {
            if (saveAccountFirst)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
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

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task RevokeAccessAsync(TradeAccount account, CancellationToken cancellationToken)
        {
            await _sessionCacheService.RevokeAsync(account.Username, cancellationToken);

            var refreshTokens = await _dbContext.TradeRefreshTokens
                .Where(token => token.TradeAccountId == account.Id && !token.RevokedAt.HasValue)
                .ToListAsync(cancellationToken);

            if (refreshTokens.Count == 0)
            {
                return;
            }

            var revokedAtUtc = DateTime.UtcNow;
            foreach (var refreshToken in refreshTokens)
            {
                refreshToken.RevokedAt = revokedAtUtc;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private static string? NormalizeGroupName(string? groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return null;
            }

            var normalized = groupName.Trim();
            if (string.Equals(normalized, "Viewer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Visitor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, TradeGroupNames.Observers, StringComparison.OrdinalIgnoreCase))
            {
                return TradeGroupNames.Visitors;
            }

            if (string.Equals(normalized, TradeGroupNames.Visitors, StringComparison.OrdinalIgnoreCase))
            {
                return TradeGroupNames.Visitors;
            }

            if (string.Equals(normalized, TradeGroupNames.Traders, StringComparison.OrdinalIgnoreCase))
            {
                return TradeGroupNames.Traders;
            }

            if (string.Equals(normalized, TradeGroupNames.Administrators, StringComparison.OrdinalIgnoreCase))
            {
                return TradeGroupNames.Administrators;
            }

            return normalized;
        }

        private static string? NormalizeSelfRegistrationGroup(string requestedGroup)
        {
            var normalized = NormalizeGroupName(requestedGroup);
            return normalized != null && SelfRegistrationGroups.Contains(normalized, StringComparer.OrdinalIgnoreCase)
                ? normalized
                : null;
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
