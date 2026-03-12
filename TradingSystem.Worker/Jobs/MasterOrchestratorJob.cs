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

            // Query DB for active servers instead of hardcoding
            var activeServers = await dbContext.TradingServers
                .Where(s => s.IsEnabled)
                .ToListAsync();

            var activeServerIds = activeServers.Select(s => s.Id).ToList();

            foreach (var server in activeServers)
            {
                var jobKey = new JobKey($"DataPullJob-{server.Id}", "DataPullGroup");

                if (!await scheduler.CheckExists(jobKey))
                {
                    var job = JobBuilder.Create<SymbolDataPullJob>()
                       .WithIdentity(jobKey)
                       .UsingJobData("ServerId", server.Id)
                       .Build();

                    var trigger = TriggerBuilder.Create()
                       .WithIdentity($"Trigger-{server.Id}", "DataPullGroup")
                       .StartNow()
                       .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).RepeatForever())
                       .Build();

                    await scheduler.ScheduleJob(job, trigger);
                }
            }

            // Clean up old jobs if a server is disabled
            var existingJobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("DataPullGroup"));
            foreach (var jobKey in existingJobKeys)
            {
                var serverIdFromJob = jobKey.Name.Replace("DataPullJob-", "");
                if (!activeServerIds.Contains(serverIdFromJob))
                {
                    await scheduler.DeleteJob(jobKey);
                }
            }
        }
    }
}