using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TradingSystem.Domain.Scheduling;
using TradingSystem.Domain.Entities;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Infrastructure.Services;

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

        public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            var taskId = ScheduledTaskQuartzService.TryGetScheduledTaskId(context);
            if (!taskId.HasValue)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
            var scheduledTask = await dbContext.ScheduledTasks.SingleOrDefaultAsync(task => task.Id == taskId.Value, cancellationToken);

            if (scheduledTask == null)
            {
                return;
            }

            scheduledTask.RuntimeStatus = ScheduledTaskRuntimeStatuses.Running;
            scheduledTask.LastTriggeredAt = context.FireTimeUtc.UtcDateTime;
            scheduledTask.CurrentExecutionStartedAt = context.FireTimeUtc.UtcDateTime;
            scheduledTask.NextFireTime = context.Trigger.GetNextFireTimeUtc()?.UtcDateTime;
            scheduledTask.LastSchedulerInstance = context.Scheduler.SchedulerInstanceId;
            scheduledTask.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            var status = jobException == null
                ? ScheduledTaskRuntimeStatuses.Completed
                : ScheduledTaskRuntimeStatuses.Failed;
            var taskId = ScheduledTaskQuartzService.TryGetScheduledTaskId(context);
            int? serverId = null;
            if (context.JobDetail.JobDataMap.ContainsKey("ServerId"))
            {
                serverId = context.JobDetail.JobDataMap.GetInt("ServerId");
            }

            var ticker = context.JobDetail.JobDataMap.GetString(ScheduledTaskQuartzService.TickerKey);
            var taskType = context.JobDetail.JobDataMap.GetString(ScheduledTaskQuartzService.TaskTypeKey);
            var taskName = context.JobDetail.JobDataMap.GetString(ScheduledTaskQuartzService.TaskNameKey);
            var durationMs = Math.Round(context.JobRunTime.TotalMilliseconds, 4, MidpointRounding.AwayFromZero);
            var completedAtUtc = DateTime.UtcNow;

            var history = new JobExecutionHistory
            {
                Id = Guid.NewGuid(),
                ScheduledTaskId = taskId,
                TaskName = taskName,
                TaskType = taskType,
                JobName = context.JobDetail.Key.Name,
                ServerId = serverId,
                Ticker = ticker,
                Status = status,
                StartTime = context.FireTimeUtc.UtcDateTime,
                EndTime = completedAtUtc,
                DurationMs = durationMs,
                SchedulerInstance = context.Scheduler.SchedulerInstanceId,
                ErrorMessage = jobException?.Message
            };

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

            dbContext.JobExecutionHistories.Add(history);

            if (taskId.HasValue)
            {
                var scheduledTask = await dbContext.ScheduledTasks.SingleOrDefaultAsync(task => task.Id == taskId.Value, cancellationToken);
                if (scheduledTask != null)
                {
                    var priorExecutions = scheduledTask.ExecutionCount;
                    scheduledTask.ExecutionCount = priorExecutions + 1;
                    scheduledTask.FailureCount += jobException == null ? 0 : 1;
                    scheduledTask.LastExecutionStatus = status;
                    scheduledTask.LastExecutionDurationMs = durationMs;
                    scheduledTask.AverageDurationMs = priorExecutions == 0
                        ? durationMs
                        : Math.Round(((scheduledTask.AverageDurationMs * priorExecutions) + durationMs) / scheduledTask.ExecutionCount, 4, MidpointRounding.AwayFromZero);
                    scheduledTask.LastCompletedAt = completedAtUtc;
                    scheduledTask.CurrentExecutionStartedAt = null;
                    scheduledTask.NextFireTime = context.Trigger.GetNextFireTimeUtc()?.UtcDateTime;
                    scheduledTask.LastSchedulerInstance = context.Scheduler.SchedulerInstanceId;
                    scheduledTask.LastError = jobException?.Message;
                    scheduledTask.RuntimeStatus = scheduledTask.IsPaused
                        ? ScheduledTaskRuntimeStatuses.Paused
                        : ScheduledTaskRuntimeStatuses.Scheduled;
                    scheduledTask.UpdatedAt = completedAtUtc;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
