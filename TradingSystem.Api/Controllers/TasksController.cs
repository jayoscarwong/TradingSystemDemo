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
    }
}