using Quartz;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Scheduling;

namespace TradingSystem.Infrastructure.Services
{
    public sealed class ScheduledTaskQuartzService
    {
        public const string ManagedTaskGroup = "ManagedTasks";
        public const string ScheduledTaskIdKey = "ScheduledTaskId";
        public const string TaskTypeKey = "TaskType";
        public const string ServerIdKey = "ServerId";
        public const string TickerKey = "Ticker";
        public const string TaskNameKey = "TaskName";

        private readonly ISchedulerFactory _schedulerFactory;

        public ScheduledTaskQuartzService(ISchedulerFactory schedulerFactory)
        {
            _schedulerFactory = schedulerFactory;
        }

        public async Task<SchedulerTaskState> UpsertAsync(ScheduledTask task, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            var jobKey = BuildJobKey(task.Id);
            var triggerKey = BuildTriggerKey(task.Id);
            var jobType = ResolveJobType(task.TaskType);

            var job = JobBuilder.Create(jobType)
                .WithIdentity(jobKey)
                .UsingJobData(ScheduledTaskIdKey, task.Id.ToString())
                .UsingJobData(TaskTypeKey, task.TaskType)
                .UsingJobData(TaskNameKey, task.Name)
                .Build();

            if (task.ServerId.HasValue)
            {
                job.JobDataMap.Put(ServerIdKey, task.ServerId.Value);
            }

            if (!string.IsNullOrWhiteSpace(task.Ticker))
            {
                job.JobDataMap.Put(TickerKey, task.Ticker);
            }

            var trigger = BuildTrigger(task, triggerKey, jobKey);

            if (await scheduler.CheckExists(jobKey, cancellationToken))
            {
                await scheduler.AddJob(job, true, true, cancellationToken);

                var existingTriggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);
                var existingTrigger = existingTriggers.FirstOrDefault();

                if (existingTrigger == null)
                {
                    await scheduler.ScheduleJob(trigger, cancellationToken);
                }
                else
                {
                    await scheduler.RescheduleJob(existingTrigger.Key, trigger, cancellationToken);
                }
            }
            else
            {
                await scheduler.ScheduleJob(job, trigger, cancellationToken);
            }

            if (task.IsPaused || !task.IsEnabled)
            {
                await scheduler.PauseJob(jobKey, cancellationToken);
            }
            else
            {
                await scheduler.ResumeJob(jobKey, cancellationToken);
            }

            return await ReadStateAsync(task.Id, cancellationToken)
                ?? new SchedulerTaskState(task.Id, null, null, task.IsPaused, task.RuntimeStatus);
        }

        public async Task<SchedulerTaskState?> ReadStateAsync(long taskId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            var jobKey = BuildJobKey(taskId);

            if (!await scheduler.CheckExists(jobKey, cancellationToken))
            {
                return null;
            }

            var triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);
            var trigger = triggers.OrderBy(t => t.GetNextFireTimeUtc()).FirstOrDefault();
            var triggerState = trigger == null
                ? TriggerState.None
                : await scheduler.GetTriggerState(trigger.Key, cancellationToken);

            return new SchedulerTaskState(
                taskId,
                trigger?.GetNextFireTimeUtc()?.UtcDateTime,
                trigger?.GetPreviousFireTimeUtc()?.UtcDateTime,
                triggerState == TriggerState.Paused,
                triggerState.ToString());
        }

        public async Task DeleteAsync(long taskId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            var jobKey = BuildJobKey(taskId);

            if (await scheduler.CheckExists(jobKey, cancellationToken))
            {
                await scheduler.DeleteJob(jobKey, cancellationToken);
            }
        }

        public async Task PauseAsync(long taskId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            var jobKey = BuildJobKey(taskId);

            if (await scheduler.CheckExists(jobKey, cancellationToken))
            {
                await scheduler.PauseJob(jobKey, cancellationToken);
            }
        }

        public async Task ResumeAsync(long taskId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            var jobKey = BuildJobKey(taskId);

            if (await scheduler.CheckExists(jobKey, cancellationToken))
            {
                await scheduler.ResumeJob(jobKey, cancellationToken);
            }
        }

        public async Task TriggerNowAsync(long taskId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
            var jobKey = BuildJobKey(taskId);

            if (await scheduler.CheckExists(jobKey, cancellationToken))
            {
                await scheduler.TriggerJob(jobKey, cancellationToken);
            }
        }

        public static JobKey BuildJobKey(long taskId)
        {
            return new JobKey($"task-{taskId}", ManagedTaskGroup);
        }

        public static TriggerKey BuildTriggerKey(long taskId)
        {
            return new TriggerKey($"task-trigger-{taskId}", ManagedTaskGroup);
        }

        public static long? TryGetScheduledTaskId(IJobExecutionContext context)
        {
            var rawValue = context.JobDetail.JobDataMap.GetString(ScheduledTaskIdKey);
            return long.TryParse(rawValue, out var taskId) ? taskId : null;
        }

        private static ITrigger BuildTrigger(ScheduledTask task, TriggerKey triggerKey, JobKey jobKey)
        {
            var triggerBuilder = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartNow();

            if (string.Equals(task.ScheduleType, ScheduledTaskScheduleTypes.Cron, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(task.CronExpression))
                {
                    throw new InvalidOperationException($"Task {task.Id} requires a cron expression.");
                }

                triggerBuilder = triggerBuilder.WithCronSchedule(task.CronExpression);
            }
            else
            {
                if (!task.IntervalSeconds.HasValue || task.IntervalSeconds.Value <= 0)
                {
                    throw new InvalidOperationException($"Task {task.Id} requires IntervalSeconds for simple schedules.");
                }

                triggerBuilder = triggerBuilder.WithSimpleSchedule(schedule =>
                {
                    schedule.WithIntervalInSeconds(task.IntervalSeconds.Value);

                    if (task.RepeatCount.HasValue && task.RepeatCount.Value >= 0)
                    {
                        schedule.WithRepeatCount(task.RepeatCount.Value);
                    }
                    else
                    {
                        schedule.RepeatForever();
                    }
                });
            }

            return triggerBuilder.Build();
        }

        private static Type ResolveJobType(string taskType)
        {
            var jobTypeName = taskType switch
            {
                ScheduledTaskTypes.MasterOrchestrator => "TradingSystem.Worker.Jobs.MasterOrchestratorJob, TradingSystem.Worker",
                ScheduledTaskTypes.SymbolDataPull => "TradingSystem.Worker.Jobs.SymbolDataPullJob, TradingSystem.Worker",
                _ => throw new InvalidOperationException($"Unsupported task type '{taskType}'.")
            };

            return Type.GetType(jobTypeName)
                ?? throw new InvalidOperationException($"Quartz job type '{jobTypeName}' could not be resolved.");
        }
    }

    public sealed record SchedulerTaskState(
        long TaskId,
        DateTime? NextFireTime,
        DateTime? PreviousFireTime,
        bool IsPaused,
        string TriggerState);
}
