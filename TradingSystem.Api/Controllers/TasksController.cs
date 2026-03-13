using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl.Matchers;
using System.Linq;
using TradingSystem.Api.DTOs;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TasksController : ControllerBase
    {
        private const string DataPullGroup = "DataPullGroup";

        private readonly ISchedulerFactory _schedulerFactory;
        private readonly TradingDbContext _dbContext;

        public TasksController(ISchedulerFactory schedulerFactory, TradingDbContext dbContext)
        {
            _schedulerFactory = schedulerFactory;
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllScheduledJobs(
            [FromQuery] string? jobName = null,
            [FromQuery] string? groupName = null,
            [FromQuery] string? ticker = null,
            [FromQuery] int? serverId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var scheduler = await _schedulerFactory.GetScheduler();
            var jobGroupNames = await scheduler.GetJobGroupNames();
            var jobs = new List<ScheduledJobSummary>();

            foreach (var currentGroup in jobGroupNames)
            {
                var groupMatcher = GroupMatcher<JobKey>.GroupEquals(currentGroup);
                var jobKeys = await scheduler.GetJobKeys(groupMatcher);

                foreach (var jobKey in jobKeys)
                {
                    var jobDetail = await scheduler.GetJobDetail(jobKey);
                    var trigger = (await scheduler.GetTriggersOfJob(jobKey)).OrderBy(t => t.GetNextFireTimeUtc()).FirstOrDefault();

                    jobs.Add(new ScheduledJobSummary(
                        jobKey.Name,
                        jobKey.Group,
                        jobDetail?.JobDataMap.ContainsKey("ServerId") == true ? (int?)jobDetail.JobDataMap.GetInt("ServerId") : null,
                        jobDetail?.JobDataMap.GetString("Ticker"),
                        trigger?.GetNextFireTimeUtc(),
                        trigger?.GetPreviousFireTimeUtc()));
                }
            }

            var filteredJobs = jobs
                .Where(job => string.IsNullOrWhiteSpace(jobName) || job.JobName.Contains(jobName, StringComparison.OrdinalIgnoreCase))
                .Where(job => string.IsNullOrWhiteSpace(groupName) || string.Equals(job.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                .Where(job => string.IsNullOrWhiteSpace(ticker) || string.Equals(job.Ticker, ticker.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
                .Where(job => !serverId.HasValue || job.ServerId == serverId.Value)
                .OrderBy(job => job.GroupName)
                .ThenBy(job => job.JobName)
                .ToList();

            var pagedJobs = filteredJobs
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = filteredJobs.Count,
                Items = pagedJobs
            });
        }

        [HttpGet("{jobName}")]
        public async Task<IActionResult> GetTaskDetails(string jobName)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = await FindJobKeyAsync(scheduler, jobName);

            if (jobKey == null)
            {
                return NotFound($"Task '{jobName}' not found.");
            }

            var jobDetail = await scheduler.GetJobDetail(jobKey);
            var trigger = (await scheduler.GetTriggersOfJob(jobKey)).OrderBy(t => t.GetNextFireTimeUtc()).FirstOrDefault();
            var lastExecution = await _dbContext.JobExecutionHistories
                .AsNoTracking()
                .Where(history => history.JobName == jobKey.Name)
                .OrderByDescending(history => history.StartTime)
                .FirstOrDefaultAsync();

            var isRunning = (await scheduler.GetCurrentlyExecutingJobs())
                .Any(executingJob => executingJob.JobDetail.Key.Equals(jobKey));

            return Ok(new
            {
                JobName = jobKey.Name,
                GroupName = jobKey.Group,
                ServerId = jobDetail?.JobDataMap.ContainsKey("ServerId") == true ? (int?)jobDetail.JobDataMap.GetInt("ServerId") : null,
                Ticker = jobDetail?.JobDataMap.GetString("Ticker"),
                NextFireTime = trigger?.GetNextFireTimeUtc(),
                PreviousFireTime = trigger?.GetPreviousFireTimeUtc(),
                LastStatus = lastExecution?.Status,
                LastTriggeredAt = lastExecution?.StartTime,
                IsRunning = isRunning
            });
        }

        [HttpGet("{jobName}/status")]
        public async Task<IActionResult> GetTaskStatus(string jobName)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = await FindJobKeyAsync(scheduler, jobName);

            var history = await _dbContext.JobExecutionHistories
                .AsNoTracking()
                .Where(entry => entry.JobName == jobName)
                .OrderByDescending(entry => entry.StartTime)
                .Take(20)
                .ToListAsync();

            if (jobKey == null && !history.Any())
            {
                return NotFound(new { Message = $"No execution history or active task found for job: {jobName}" });
            }

            var trigger = jobKey == null
                ? null
                : (await scheduler.GetTriggersOfJob(jobKey)).OrderBy(t => t.GetNextFireTimeUtc()).FirstOrDefault();

            var completedExecutions = history
                .Where(entry => entry.EndTime >= entry.StartTime)
                .Select(entry => (entry.EndTime - entry.StartTime).TotalMilliseconds)
                .ToList();

            var isRunning = jobKey != null && (await scheduler.GetCurrentlyExecutingJobs())
                .Any(executingJob => executingJob.JobDetail.Key.Equals(jobKey));

            return Ok(new
            {
                JobName = jobKey?.Name ?? jobName,
                GroupName = jobKey?.Group,
                IsRunning = isRunning,
                LastTriggeredAt = history.FirstOrDefault()?.StartTime,
                NextFireTime = trigger?.GetNextFireTimeUtc(),
                AverageDurationMs = completedExecutions.Any() ? completedExecutions.Average() : (double?)null,
                ExecutionCount = history.Count,
                History = history
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey(request.JobName, DataPullGroup);

            if (await scheduler.CheckExists(jobKey))
            {
                return Conflict($"Task '{request.JobName}' already exists.");
            }

            var jobType = ResolveSymbolDataPullJobType();
            var normalizedTicker = request.Ticker.Trim().ToUpperInvariant();

            var job = JobBuilder.Create(jobType)
                .WithIdentity(jobKey)
                .UsingJobData("ServerId", request.ServerId)
                .UsingJobData("Ticker", normalizedTicker)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"Trigger-{request.JobName}", DataPullGroup)
                .ForJob(jobKey)
                .WithCronSchedule(request.CronExpression)
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            return Created($"/api/tasks/{request.JobName}", new
            {
                Message = "Task successfully scheduled.",
                JobName = request.JobName,
                request.ServerId,
                Ticker = normalizedTicker,
                request.CronExpression
            });
        }

        [HttpPut("{jobName}")]
        public async Task<IActionResult> UpdateTask(string jobName, [FromBody] UpdateTaskRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.CronExpression)
                && request.ServerId == null
                && string.IsNullOrWhiteSpace(request.Ticker))
            {
                return BadRequest("At least one field must be supplied to update a task.");
            }

            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = await FindJobKeyAsync(scheduler, jobName);

            if (jobKey == null)
            {
                return NotFound($"Task '{jobName}' not found.");
            }

            var existingJob = await scheduler.GetJobDetail(jobKey);
            if (existingJob == null)
            {
                return NotFound($"Task '{jobName}' not found.");
            }

            var updatedServerId = request.ServerId
                ?? (existingJob.JobDataMap.ContainsKey("ServerId") ? existingJob.JobDataMap.GetInt("ServerId") : 0);

            if (updatedServerId <= 0)
            {
                return BadRequest("A valid ServerId is required.");
            }

            var updatedTicker = string.IsNullOrWhiteSpace(request.Ticker)
                ? existingJob.JobDataMap.GetString("Ticker")
                : request.Ticker.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(updatedTicker))
            {
                return BadRequest("Ticker is required.");
            }

            var updatedJob = JobBuilder.Create(existingJob.JobType)
                .WithIdentity(jobKey)
                .StoreDurably(existingJob.Durable)
                .RequestRecovery(existingJob.RequestsRecovery)
                .UsingJobData("ServerId", updatedServerId)
                .UsingJobData("Ticker", updatedTicker)
                .Build();

            await scheduler.AddJob(updatedJob, true, true);

            if (!string.IsNullOrWhiteSpace(request.CronExpression))
            {
                var existingTrigger = (await scheduler.GetTriggersOfJob(jobKey)).FirstOrDefault();
                if (existingTrigger == null)
                {
                    return BadRequest($"No active trigger found for job '{jobName}'.");
                }

                var updatedTrigger = TriggerBuilder.Create()
                    .WithIdentity(existingTrigger.Key)
                    .ForJob(jobKey)
                    .WithCronSchedule(request.CronExpression)
                    .Build();

                await scheduler.RescheduleJob(existingTrigger.Key, updatedTrigger);
            }

            return Ok(new
            {
                Message = $"Successfully updated task '{jobName}'.",
                JobName = jobKey.Name,
                ServerId = updatedServerId,
                Ticker = updatedTicker,
                request.CronExpression
            });
        }

        [HttpPut("{jobName}/schedule")]
        public Task<IActionResult> UpdateJobSchedule(string jobName, [FromBody] UpdateScheduleRequest request)
        {
            return UpdateTask(jobName, new UpdateTaskRequest
            {
                CronExpression = request.CronExpression
            });
        }

        [HttpDelete("{jobName}")]
        public async Task<IActionResult> DeleteTask(string jobName)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = await FindJobKeyAsync(scheduler, jobName);

            if (jobKey == null)
            {
                return NotFound($"Task '{jobName}' not found.");
            }

            await scheduler.DeleteJob(jobKey);
            return Ok(new { Message = $"Task '{jobName}' successfully deleted." });
        }

        private static async Task<JobKey?> FindJobKeyAsync(IScheduler scheduler, string jobName)
        {
            var preferredKey = new JobKey(jobName, DataPullGroup);
            if (await scheduler.CheckExists(preferredKey))
            {
                return preferredKey;
            }

            foreach (var group in await scheduler.GetJobGroupNames())
            {
                var candidateKey = new JobKey(jobName, group);
                if (await scheduler.CheckExists(candidateKey))
                {
                    return candidateKey;
                }
            }

            return null;
        }

        private static Type ResolveSymbolDataPullJobType()
        {
            return Type.GetType("TradingSystem.Worker.Jobs.SymbolDataPullJob, TradingSystem.Worker")
                ?? throw new InvalidOperationException("TradingSystem.Worker.Jobs.SymbolDataPullJob could not be loaded by the API host.");
        }

        private sealed record ScheduledJobSummary(
            string JobName,
            string GroupName,
            int? ServerId,
            string? Ticker,
            DateTimeOffset? NextFireTime,
            DateTimeOffset? PreviousFireTime);

        public class UpdateScheduleRequest
        {
            public string CronExpression { get; set; } = string.Empty;
        }

        public class UpdateTaskRequest
        {
            public string? CronExpression { get; set; }
            public int? ServerId { get; set; }
            public string? Ticker { get; set; }
        }
    }
}
