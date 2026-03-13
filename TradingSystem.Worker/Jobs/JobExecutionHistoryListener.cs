using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TradingSystem.Domain.Entities;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Worker.Jobs
{
    public class JobExecutionHistoryListener : IJobListener
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public JobExecutionHistoryListener(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public string Name => "JobExecutionHistoryListener";

        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        // FIX: Added '?' to JobExecutionException to match the interface
        public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            var status = jobException == null ? "Completed" : "Failed";

            // FIX: Safely extract the ServerId as an int, or leave it null if it's a global job (like MasterOrchestrator)
            int? serverId = null;
            if (context.JobDetail.JobDataMap.ContainsKey("ServerId"))
            {
                serverId = context.JobDetail.JobDataMap.GetInt("ServerId");
            }

            var history = new JobExecutionHistory
            {
                Id = Guid.NewGuid(),
                JobName = context.JobDetail.Key.Name,
                ServerId = serverId,
                Status = status,
                StartTime = context.FireTimeUtc.UtcDateTime,
                EndTime = DateTime.UtcNow,
                ErrorMessage = jobException?.Message
            };

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

            dbContext.JobExecutionHistories.Add(history);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}