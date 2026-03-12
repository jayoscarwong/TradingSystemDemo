using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TradingSystem.Infrastructure.Data;

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
            var scheduler = context.Scheduler;

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

            // Query active servers dynamically
            var activeServers = await dbContext.TradingServers
                .Where(s => s.IsEnabled)
                .ToListAsync();

            foreach (var server in activeServers)
            {
                var jobKey = new JobKey($"StockPriceUpdateJob-{server.Id}", "TradingGroup");

                if (!await scheduler.CheckExists(jobKey))
                {
                    var job = JobBuilder.Create<StockPriceUpdateJob>()
                       .WithIdentity(jobKey)
                       .UsingJobData("ServerId", server.Id)
                       .UsingJobData("ticker", "AAPL") // Defaulting for demo purposes
                       .UsingJobData("orderPrice", "145.0")
                       .UsingJobData("orderVolume", "100")
                       .Build();

                    var trigger = TriggerBuilder.Create()
                       .WithIdentity($"Trigger-{server.Id}", "TradingGroup")
                       .StartNow()
                       .WithCronSchedule("0 0/5 * * * ?") // Every 5 minutes
                       .Build();

                    await scheduler.ScheduleJob(job, trigger);
                }
            }
        }
    }
}