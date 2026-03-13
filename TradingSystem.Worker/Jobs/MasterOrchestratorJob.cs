using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl.Matchers;
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

            var activeServers = await dbContext.TradingServers.Where(s => s.IsEnabled).ToListAsync();
            var activeServerIds = activeServers.Select(s => s.Id).ToList();

            // FIX: Fetch all active tickers dynamically from the database
            var activeTickers = await dbContext.StockPrices.Select(s => s.Ticker).ToListAsync();

            // 1. Schedule new jobs for every Server + Ticker combination
            foreach (var server in activeServers)
            {
                foreach (var ticker in activeTickers)
                {
                    // Job Name Format: DataPullJob-{ServerId}-{Ticker}
                    var jobKey = new JobKey($"DataPullJob-{server.Id}-{ticker}", "DataPullGroup");

                    if (!await scheduler.CheckExists(jobKey))
                    {
                        var job = JobBuilder.Create<SymbolDataPullJob>()
                           .WithIdentity(jobKey)
                           .UsingJobData("ServerId", server.Id)
                           .UsingJobData("Ticker", ticker) // <-- Inject dynamic ticker here
                           .Build();

                        var trigger = TriggerBuilder.Create()
                           .WithIdentity($"Trigger-{server.Id}-{ticker}", "DataPullGroup")
                           .StartNow()
                           .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).RepeatForever())
                           .Build();

                        await scheduler.ScheduleJob(job, trigger);
                    }
                }
            }

            // 2. Clean up old jobs if a server is disabled OR a ticker is removed
            var existingJobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DataPullGroup"));
            foreach (var jobKey in existingJobKeys)
            {
                var parts = jobKey.Name.Split('-');

                // Ensure name matches expected format: DataPullJob-{id}-{ticker}
                if (parts.Length >= 3 && int.TryParse(parts[1], out int serverIdFromJob))
                {
                    string tickerFromJob = parts[2];

                    if (!activeServerIds.Contains(serverIdFromJob) || !activeTickers.Contains(tickerFromJob))
                    {
                        await scheduler.DeleteJob(jobKey);
                    }
                }
                else
                {
                    // Delete malformed/legacy jobs
                    await scheduler.DeleteJob(jobKey);
                }
            }
        }
    }
}