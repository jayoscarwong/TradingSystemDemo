using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;
using System.Linq;
using System.Threading.Tasks;
using TradingSystem.Api.DTOs;
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

        // --- 1. POST /api/tasks (Create a new task) ---
        [HttpPost]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey(request.JobName, "DataPullGroup");

            if (await scheduler.CheckExists(jobKey))
            {
                return Conflict($"Task '{request.JobName}' already exists.");
            }

            // Tell Quartz to use the SymbolDataPullJob we created in the Worker
            var job = JobBuilder.Create(Type.GetType("TradingSystem.Worker.Jobs.SymbolDataPullJob, TradingSystem.Worker"))
                .WithIdentity(jobKey)
                .UsingJobData("ServerId", request.ServerId)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"Trigger-{request.JobName}", "DataPullGroup")
                .WithCronSchedule(request.CronExpression)
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            return Created($"/api/tasks/{request.JobName}", new { Message = "Task successfully scheduled." });
        }

        // --- 2. GET /api/tasks/{id} (Get specific task details) ---
        [HttpGet("{jobName}")]
        public async Task<IActionResult> GetTaskDetails(string jobName)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey(jobName, "DataPullGroup"); // Assuming default group for this demo

            if (!await scheduler.CheckExists(jobKey))
            {
                return NotFound($"Task '{jobName}' not found.");
            }

            var triggers = await scheduler.GetTriggersOfJob(jobKey);

            return Ok(new
            {
                JobName = jobName,
                NextFireTime = triggers.FirstOrDefault()?.GetNextFireTimeUtc(),
                PreviousFireTime = triggers.FirstOrDefault()?.GetPreviousFireTimeUtc()
            });
        }

        // --- 3. DELETE /api/tasks/{id} (Remove a task) ---
        [HttpDelete("{jobName}")]
        public async Task<IActionResult> DeleteTask(string jobName)
        {
            var scheduler = await _schedulerFactory.GetScheduler();
            var jobKey = new JobKey(jobName, "DataPullGroup");

            if (!await scheduler.CheckExists(jobKey))
            {
                return NotFound($"Task '{jobName}' not found.");
            }

            await scheduler.DeleteJob(jobKey);
            return Ok(new { Message = $"Task '{jobName}' successfully deleted." });
        }
    }
}