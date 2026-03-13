using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Scheduling;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Infrastructure.Services;

namespace TradingSystem.Worker.Jobs
{
    [DisallowConcurrentExecution]
    public class MasterOrchestratorJob : IJob
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public MasterOrchestratorJob(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
            var quartzService = scope.ServiceProvider.GetRequiredService<ScheduledTaskQuartzService>();
            var nowUtc = DateTime.UtcNow;

            var activeServers = await dbContext.TradingServers.Where(s => s.IsEnabled).ToListAsync();
            var activeTickers = await dbContext.StockPrices.Select(s => s.Ticker).ToListAsync();
            var desiredPairs = activeServers
                .SelectMany(server => activeTickers.Select(ticker => new { server.Id, Ticker = ticker }))
                .ToList();

            var systemPullTasks = await dbContext.ScheduledTasks
                .Where(task => task.IsSystemTask && task.TaskType == ScheduledTaskTypes.SymbolDataPull)
                .ToListAsync();

            foreach (var pair in desiredPairs)
            {
                var task = systemPullTasks.FirstOrDefault(existing =>
                    existing.ServerId == pair.Id &&
                    string.Equals(existing.Ticker, pair.Ticker, StringComparison.OrdinalIgnoreCase));

                if (task == null)
                {
                    task = new ScheduledTask
                    {
                        Name = $"Market data pull S{pair.Id} {pair.Ticker}",
                        Description = $"System-managed polling task for server {pair.Id} and ticker {pair.Ticker}.",
                        TaskType = ScheduledTaskTypes.SymbolDataPull,
                        ScheduleType = ScheduledTaskScheduleTypes.Simple,
                        IntervalSeconds = 10,
                        RepeatCount = null,
                        ServerId = pair.Id,
                        Ticker = pair.Ticker,
                        IsSystemTask = true,
                        IsEnabled = true,
                        IsPaused = false,
                        AllowConcurrentExecution = false,
                        RuntimeStatus = ScheduledTaskRuntimeStatuses.Scheduled,
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    };

                    dbContext.ScheduledTasks.Add(task);
                    systemPullTasks.Add(task);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(task.ScheduleType))
                    {
                        task.ScheduleType = ScheduledTaskScheduleTypes.Simple;
                    }

                    if (!task.IntervalSeconds.HasValue || task.IntervalSeconds.Value <= 0)
                    {
                        task.IntervalSeconds = 10;
                    }

                    task.IsEnabled = true;
                    task.IsSystemTask = true;
                    task.TaskType = ScheduledTaskTypes.SymbolDataPull;
                    task.ServerId = pair.Id;
                    task.Ticker = pair.Ticker;
                    task.RuntimeStatus = task.IsPaused
                        ? ScheduledTaskRuntimeStatuses.Paused
                        : ScheduledTaskRuntimeStatuses.Scheduled;
                    task.UpdatedAt = nowUtc;
                }
            }

            await dbContext.SaveChangesAsync();

            foreach (var task in systemPullTasks.Where(task =>
                         task.IsEnabled &&
                         task.ServerId.HasValue &&
                         !string.IsNullOrWhiteSpace(task.Ticker) &&
                         desiredPairs.Any(pair => pair.Id == task.ServerId.Value &&
                                                  string.Equals(pair.Ticker, task.Ticker, StringComparison.OrdinalIgnoreCase))))
            {
                var state = await quartzService.UpsertAsync(task, context.CancellationToken);
                task.NextFireTime = state.NextFireTime;
                task.RuntimeStatus = task.IsPaused
                    ? ScheduledTaskRuntimeStatuses.Paused
                    : ScheduledTaskRuntimeStatuses.Scheduled;
                task.UpdatedAt = nowUtc;
            }

            foreach (var task in systemPullTasks.Where(task =>
                         !task.ServerId.HasValue ||
                         string.IsNullOrWhiteSpace(task.Ticker) ||
                         !desiredPairs.Any(pair => pair.Id == task.ServerId.Value &&
                                                  string.Equals(pair.Ticker, task.Ticker, StringComparison.OrdinalIgnoreCase))))
            {
                await quartzService.DeleteAsync(task.Id, context.CancellationToken);
                task.IsEnabled = false;
                task.IsPaused = false;
                task.NextFireTime = null;
                task.CurrentExecutionStartedAt = null;
                task.RuntimeStatus = ScheduledTaskRuntimeStatuses.Deleted;
                task.UpdatedAt = nowUtc;
            }

            await dbContext.SaveChangesAsync();
        }
    }
}
