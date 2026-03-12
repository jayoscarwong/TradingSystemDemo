using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TasksController : ControllerBase
    {
        private readonly ISchedulerFactory _schedulerFactory;
        private readonly TradingDbContext _dbContext;

        public TasksController(ISchedulerFactory schedulerFactory, TradingDbContext dbContext)
        {
            _schedulerFactory = schedulerFactory;
            _dbContext = dbContext;
        }

        [HttpGet("{jobName}/status")]
        public async Task<IActionResult> GetTaskStatus(string jobName)
        {
            var history = await _dbContext.JobExecutionHistories
                .Where(h => h.JobName == jobName)
                .OrderByDescending(h => h.StartTime)
                .Take(10)
                .ToListAsync();

            if (!history.Any())
            {
                return NotFound(new { Message = $"No execution history found for job: {jobName}" });
            }

            return Ok(history);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllScheduledJobs()
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobGroupNames = await scheduler.GetJobGroupNames();

            var allJobs = new System.Collections.Generic.List<object>();

            foreach (var group in jobGroupNames)
            {
                var groupMatcher = Quartz.Impl.Matchers.GroupMatcher<JobKey>.GroupContains(group);
                var jobKeys = await scheduler.GetJobKeys(groupMatcher);

                foreach (var jobKey in jobKeys)
                {
                    // FIX: We remove the GetJobDetail call so Quartz doesn't try to load the Worker DLL
                    // We only request the triggers associated with the job key.
                    var triggers = await scheduler.GetTriggersOfJob(jobKey);

                    allJobs.Add(new
                    {
                        JobName = jobKey.Name,
                        GroupName = jobKey.Group,
                        NextFireTime = triggers.FirstOrDefault()?.GetNextFireTimeUtc()
                    });
                }
            }

            return Ok(allJobs);
        }


        // --- Add this below your existing methods ---

        public class UpdateScheduleRequest
        {
            public string CronExpression { get; set; }
        }

        [HttpPut("{jobName}/schedule")]
        public async Task<IActionResult> UpdateJobSchedule(string jobName, [FromBody] UpdateScheduleRequest request)
        {
            // Basic cron validation (Quartz will also throw if invalid)
            if (string.IsNullOrWhiteSpace(request.CronExpression))
            {
                return BadRequest("CronExpression is required.");
            }

            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey(jobName);

            // 1. Check if the job exists
            if (!await scheduler.CheckExists(jobKey))
            {
                return NotFound($"Job '{jobName}' not found.");
            }

            // 2. Get the existing triggers for this job
            var triggers = await scheduler.GetTriggersOfJob(jobKey);
            var oldTrigger = triggers.FirstOrDefault();

            if (oldTrigger == null)
            {
                return BadRequest($"No active triggers found for job '{jobName}'.");
            }

            // 3. Build a new trigger with the updated schedule
            var newTrigger = TriggerBuilder.Create()
                .WithIdentity(oldTrigger.Key) // Keep the same trigger ID
                .ForJob(jobKey)
                .WithCronSchedule(request.CronExpression)
                .Build();

            // 4. Tell Quartz to replace the old trigger with the new one
            await scheduler.RescheduleJob(oldTrigger.Key, newTrigger);

            return Ok(new
            {
                Message = $"Successfully updated schedule for {jobName}.",
                NewSchedule = request.CronExpression
            });
        }
    }
}