using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TradingSystem.Auth.Options;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Security;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Auth.Services
{
    public sealed class IdentityBootstrapService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public IdentityBootstrapService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
            var passwordHashService = scope.ServiceProvider.GetRequiredService<PasswordHashService>();
            var settings = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationSettings>>().Value;

            await EnsurePermissionsAsync(dbContext, cancellationToken);
            await EnsureGroupsAsync(dbContext, cancellationToken);
            await EnsureGroupPermissionsAsync(dbContext, cancellationToken);
            await EnsureAdminAccountAsync(dbContext, passwordHashService, settings.BootstrapAdminPassword, cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static async Task EnsurePermissionsAsync(TradingDbContext dbContext, CancellationToken cancellationToken)
        {
            var existingCodes = await dbContext.TradePermissions
                .Select(permission => permission.Code)
                .ToListAsync(cancellationToken);

            var missingPermissions = PermissionCodes.All
                .Except(existingCodes, StringComparer.OrdinalIgnoreCase)
                .Select(code => new TradePermission
                {
                    Code = code,
                    Description = code switch
                    {
                        PermissionCodes.AccountsManage => "Create, update, disable, and delete trade accounts.",
                        PermissionCodes.TasksRead => "View task status, history, and monitoring data.",
                        PermissionCodes.TasksManage => "Create, update, pause, resume, and delete scheduled tasks.",
                        PermissionCodes.PricesRead => "Read real-time price snapshots.",
                        PermissionCodes.TradesPlace => "Place trade orders through the trading API.",
                        _ => code
                    }
                })
                .ToList();

            if (missingPermissions.Count == 0)
            {
                return;
            }

            dbContext.TradePermissions.AddRange(missingPermissions);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static async Task EnsureGroupsAsync(TradingDbContext dbContext, CancellationToken cancellationToken)
        {
            var existingGroups = await dbContext.TradeUserGroups
                .Select(group => group.Name)
                .ToListAsync(cancellationToken);

            var missingGroups = new[]
            {
                new TradeUserGroup
                {
                    Name = TradeGroupNames.Administrators,
                    Description = "Can manage accounts, tasks, and trading operations.",
                    IsSystemGroup = true
                },
                new TradeUserGroup
                {
                    Name = TradeGroupNames.Traders,
                    Description = "Can place trades and inspect live operational status.",
                    IsSystemGroup = true
                },
                new TradeUserGroup
                {
                    Name = TradeGroupNames.Observers,
                    Description = "Can view job status and live prices.",
                    IsSystemGroup = true
                }
            }
            .Where(group => !existingGroups.Contains(group.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

            if (missingGroups.Count == 0)
            {
                return;
            }

            dbContext.TradeUserGroups.AddRange(missingGroups);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static async Task EnsureGroupPermissionsAsync(TradingDbContext dbContext, CancellationToken cancellationToken)
        {
            var groups = await dbContext.TradeUserGroups.ToDictionaryAsync(group => group.Name, cancellationToken);
            var permissions = await dbContext.TradePermissions.ToDictionaryAsync(permission => permission.Code, cancellationToken);
            var existingLinks = await dbContext.TradeGroupPermissions.ToListAsync(cancellationToken);

            var requiredMappings = new Dictionary<string, string[]>
            {
                [TradeGroupNames.Administrators] = PermissionCodes.All,
                [TradeGroupNames.Traders] = new[] { PermissionCodes.TasksRead, PermissionCodes.PricesRead, PermissionCodes.TradesPlace },
                [TradeGroupNames.Observers] = new[] { PermissionCodes.TasksRead, PermissionCodes.PricesRead }
            };

            foreach (var mapping in requiredMappings)
            {
                if (!groups.TryGetValue(mapping.Key, out var group))
                {
                    continue;
                }

                foreach (var permissionCode in mapping.Value)
                {
                    if (!permissions.TryGetValue(permissionCode, out var permission))
                    {
                        continue;
                    }

                    var exists = existingLinks.Any(link =>
                        link.TradeUserGroupId == group.Id &&
                        link.TradePermissionId == permission.Id);

                    if (!exists)
                    {
                        dbContext.TradeGroupPermissions.Add(new TradeGroupPermission
                        {
                            TradeUserGroupId = group.Id,
                            TradePermissionId = permission.Id
                        });
                    }
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private static async Task EnsureAdminAccountAsync(
            TradingDbContext dbContext,
            PasswordHashService passwordHashService,
            string bootstrapAdminPassword,
            CancellationToken cancellationToken)
        {
            var adminAccount = await dbContext.TradeAccounts
                .Include(account => account.AccountGroups)
                .SingleOrDefaultAsync(account => account.Username == "admin", cancellationToken);

            var adminGroup = await dbContext.TradeUserGroups
                .SingleAsync(group => group.Name == TradeGroupNames.Administrators, cancellationToken);

            if (adminAccount == null)
            {
                var (hash, salt) = passwordHashService.CreateHash(bootstrapAdminPassword);
                adminAccount = new TradeAccount
                {
                    Name = "System Administrator",
                    Username = "admin",
                    Email = "admin@tradingsystem.local",
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    IsDisabled = false,
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.TradeAccounts.Add(adminAccount);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var adminLinkExists = await dbContext.TradeAccountGroups.AnyAsync(link =>
                link.TradeAccountId == adminAccount.Id &&
                link.TradeUserGroupId == adminGroup.Id,
                cancellationToken);

            if (!adminLinkExists)
            {
                dbContext.TradeAccountGroups.Add(new TradeAccountGroup
                {
                    TradeAccountId = adminAccount.Id,
                    TradeUserGroupId = adminGroup.Id
                });

                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
