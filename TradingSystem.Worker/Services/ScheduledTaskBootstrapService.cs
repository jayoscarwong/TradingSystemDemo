using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Scheduling;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Infrastructure.Services;

namespace TradingSystem.Worker.Services
{
    public sealed class ScheduledTaskBootstrapService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ScheduledTaskBootstrapService> _logger;

        public ScheduledTaskBootstrapService(IServiceScopeFactory scopeFactory, ILogger<ScheduledTaskBootstrapService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
            var quartzService = scope.ServiceProvider.GetRequiredService<ScheduledTaskQuartzService>();
            var nowUtc = DateTime.UtcNow;

            var masterTask = await dbContext.ScheduledTasks
                .SingleOrDefaultAsync(task =>
                    task.IsSystemTask &&
                    task.TaskType == ScheduledTaskTypes.MasterOrchestrator,
                    cancellationToken);

            if (masterTask == null)
            {
                masterTask = new ScheduledTask
                {
                    Name = "Master Task Orchestrator",
                    Description = "System-owned parent task that reconciles server and ticker state into child polling tasks.",
                    TaskType = ScheduledTaskTypes.MasterOrchestrator,
                    ScheduleType = ScheduledTaskScheduleTypes.Cron,
                    CronExpression = "0 0/5 * * * ?",
                    IsSystemTask = true,
                    IsEnabled = true,
                    IsPaused = false,
                    AllowConcurrentExecution = false,
                    RuntimeStatus = ScheduledTaskRuntimeStatuses.Scheduled,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };

                dbContext.ScheduledTasks.Add(masterTask);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var tasks = await dbContext.ScheduledTasks
                .Where(task => task.RuntimeStatus != ScheduledTaskRuntimeStatuses.Deleted)
                .OrderBy(task => task.Id)
                .ToListAsync(cancellationToken);
            var deletedTasks = await dbContext.ScheduledTasks
                .Where(task => task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Deleted)
                .Select(task => task.Id)
                .ToListAsync(cancellationToken);

            foreach (var deletedTaskId in deletedTasks)
            {
                await quartzService.DeleteAsync(deletedTaskId, cancellationToken);
            }

            foreach (var task in tasks)
            {
                var state = await quartzService.UpsertAsync(task, cancellationToken);
                task.NextFireTime = state.NextFireTime;
                task.RuntimeStatus = task.IsPaused
                    ? ScheduledTaskRuntimeStatuses.Paused
                    : ScheduledTaskRuntimeStatuses.Scheduled;
                task.UpdatedAt = nowUtc;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Bootstrapped {TaskCount} scheduled task records into Quartz.", tasks.Count);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
