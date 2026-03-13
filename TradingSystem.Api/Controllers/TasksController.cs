using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Api.DTOs;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Scheduling;
using TradingSystem.Domain.Security;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Infrastructure.Services;

namespace TradingSystem.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = AuthorizationPolicies.TasksRead)]
    public class TasksController : ControllerBase
    {
        private readonly TradingDbContext _dbContext;
        private readonly ScheduledTaskQuartzService _quartzService;

        public TasksController(TradingDbContext dbContext, ScheduledTaskQuartzService quartzService)
        {
            _dbContext = dbContext;
            _quartzService = quartzService;
        }

        [HttpGet]
        public async Task<IActionResult> GetTasks(
            [FromQuery] string? name = null,
            [FromQuery] string? taskType = null,
            [FromQuery] string? runtimeStatus = null,
            [FromQuery] string? ticker = null,
            [FromQuery] int? serverId = null,
            [FromQuery] bool? isPaused = null,
            [FromQuery] bool includeDeleted = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _dbContext.ScheduledTasks.AsNoTracking().AsQueryable();

            if (!includeDeleted)
            {
                query = query.Where(task => task.RuntimeStatus != ScheduledTaskRuntimeStatuses.Deleted);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                query = query.Where(task => task.Name.Contains(name));
            }

            if (!string.IsNullOrWhiteSpace(taskType))
            {
                var normalizedTaskType = NormalizeTaskType(taskType);
                query = query.Where(task => task.TaskType == normalizedTaskType);
            }

            if (!string.IsNullOrWhiteSpace(runtimeStatus))
            {
                var normalizedRuntimeStatus = NormalizeRuntimeStatus(runtimeStatus);
                query = query.Where(task => task.RuntimeStatus == normalizedRuntimeStatus);
            }

            if (!string.IsNullOrWhiteSpace(ticker))
            {
                var normalizedTicker = ticker.Trim().ToUpperInvariant();
                query = query.Where(task => task.Ticker == normalizedTicker);
            }

            if (serverId.HasValue)
            {
                query = query.Where(task => task.ServerId == serverId.Value);
            }

            if (isPaused.HasValue)
            {
                query = query.Where(task => task.IsPaused == isPaused.Value);
            }

            var totalCount = await query.CountAsync();
            var tasks = await query
                .OrderBy(task => task.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = tasks.Select(ToSummary);

            return Ok(new
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                Items = items
            });
        }

        [HttpGet("monitoring/overview")]
        public async Task<IActionResult> GetMonitoringOverview()
        {
            var tasks = await _dbContext.ScheduledTasks
                .AsNoTracking()
                .Where(task => task.RuntimeStatus != ScheduledTaskRuntimeStatuses.Deleted)
                .OrderBy(task => task.Id)
                .ToListAsync();

            return Ok(new
            {
                TotalTasks = tasks.Count,
                RunningTasks = tasks.Count(task => task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Running),
                PausedTasks = tasks.Count(task => task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Paused),
                FailedTasks = tasks.Count(task => task.LastExecutionStatus == ScheduledTaskRuntimeStatuses.Failed),
                ScheduledTasks = tasks.Count(task => task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Scheduled),
                Items = tasks.Select(ToSummary)
            });
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetTaskDetails(long id)
        {
            var task = await _dbContext.ScheduledTasks
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == id);

            if (task == null)
            {
                return NotFound(new { Message = $"Task {id} was not found." });
            }

            var history = await _dbContext.JobExecutionHistories
                .AsNoTracking()
                .Where(entry => entry.ScheduledTaskId == id)
                .OrderByDescending(entry => entry.StartTime)
                .Take(10)
                .ToListAsync();

            return Ok(new
            {
                Task = ToDetail(task),
                RecentHistory = history
            });
        }

        [HttpGet("{id:long}/status")]
        public async Task<IActionResult> GetTaskStatus(long id)
        {
            var task = await _dbContext.ScheduledTasks
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == id);

            if (task == null)
            {
                return NotFound(new { Message = $"Task {id} was not found." });
            }

            var history = await _dbContext.JobExecutionHistories
                .AsNoTracking()
                .Where(entry => entry.ScheduledTaskId == id)
                .OrderByDescending(entry => entry.StartTime)
                .Take(20)
                .ToListAsync();

            return Ok(new
            {
                Task = ToDetail(task),
                CurrentState = new
                {
                    task.RuntimeStatus,
                    task.LastExecutionStatus,
                    task.IsPaused,
                    IsRunning = task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Running,
                    task.LastTriggeredAt,
                    task.CurrentExecutionStartedAt,
                    task.LastCompletedAt,
                    task.NextFireTime,
                    task.LastSchedulerInstance,
                    task.LastError
                },
                Metrics = new
                {
                    task.ExecutionCount,
                    task.FailureCount,
                    task.LastExecutionDurationMs,
                    task.AverageDurationMs
                },
                History = history
            });
        }

        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.TasksManage)]
        public async Task<IActionResult> CreateTask([FromBody] CreateTaskRequest request, CancellationToken cancellationToken)
        {
            var normalizedName = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return BadRequest(new { Message = "Name is required." });
            }

            if (string.Equals(request.TaskType, ScheduledTaskTypes.MasterOrchestrator, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { Message = "The master orchestrator is system-managed. Update the existing master task instead of creating a duplicate." });
            }

            if (await _dbContext.ScheduledTasks.AnyAsync(task => task.Name == normalizedName, cancellationToken))
            {
                return Conflict(new { Message = $"Task name '{normalizedName}' already exists." });
            }

            var task = new ScheduledTask
            {
                Name = normalizedName,
                Description = request.Description?.Trim(),
                TaskType = NormalizeTaskType(request.TaskType),
                ScheduleType = NormalizeScheduleType(request.ScheduleType),
                CronExpression = NormalizeCronExpression(request.CronExpression),
                IntervalSeconds = request.IntervalSeconds,
                RepeatCount = request.RepeatCount,
                ServerId = request.ServerId,
                Ticker = NormalizeTicker(request.Ticker),
                IsSystemTask = false,
                IsEnabled = true,
                IsPaused = false,
                AllowConcurrentExecution = false,
                RuntimeStatus = ScheduledTaskRuntimeStatuses.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var validationError = await ValidateTaskAsync(task, cancellationToken);
            if (validationError != null)
            {
                return validationError;
            }

            _dbContext.ScheduledTasks.Add(task);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var schedulerState = await _quartzService.UpsertAsync(task, cancellationToken);
            ApplySchedulerState(task, schedulerState);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Created($"/api/tasks/{task.Id}", ToDetail(task));
        }

        [HttpPut("{id:long}")]
        [Authorize(Policy = AuthorizationPolicies.TasksManage)]
        public async Task<IActionResult> UpdateTask(long id, [FromBody] UpdateTaskRequest request, CancellationToken cancellationToken)
        {
            var task = await _dbContext.ScheduledTasks.SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);
            if (task == null)
            {
                return NotFound(new { Message = $"Task {id} was not found." });
            }

            if (task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Deleted)
            {
                return Conflict(new { Message = $"Task {id} was deleted. Create a new task instead." });
            }

            if (request.Name == null
                && request.Description == null
                && request.ScheduleType == null
                && request.CronExpression == null
                && request.IntervalSeconds == null
                && request.RepeatCount == null
                && request.ServerId == null
                && request.Ticker == null)
            {
                return BadRequest(new { Message = "At least one field must be supplied to update a task." });
            }

            if (task.IsSystemTask && task.TaskType == ScheduledTaskTypes.SymbolDataPull)
            {
                if (request.ServerId.HasValue || request.Ticker != null)
                {
                    return Conflict(new { Message = "System-managed polling tasks cannot be reassigned to a different server or ticker." });
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                var normalizedName = request.Name.Trim();
                var nameExists = await _dbContext.ScheduledTasks.AnyAsync(existing => existing.Id != id && existing.Name == normalizedName, cancellationToken);
                if (nameExists)
                {
                    return Conflict(new { Message = $"Task name '{normalizedName}' already exists." });
                }

                task.Name = normalizedName;
            }

            if (request.Description != null)
            {
                task.Description = string.IsNullOrWhiteSpace(request.Description)
                    ? null
                    : request.Description.Trim();
            }

            if (request.ScheduleType != null)
            {
                task.ScheduleType = NormalizeScheduleType(request.ScheduleType);
            }

            if (request.CronExpression != null || task.ScheduleType == ScheduledTaskScheduleTypes.Cron)
            {
                task.CronExpression = NormalizeCronExpression(request.CronExpression ?? task.CronExpression);
            }

            if (request.IntervalSeconds.HasValue)
            {
                task.IntervalSeconds = request.IntervalSeconds.Value;
            }

            if (request.RepeatCount.HasValue)
            {
                task.RepeatCount = request.RepeatCount.Value;
            }

            if (request.ServerId.HasValue)
            {
                task.ServerId = request.ServerId.Value;
            }

            if (request.Ticker != null)
            {
                task.Ticker = NormalizeTicker(request.Ticker);
            }

            task.UpdatedAt = DateTime.UtcNow;

            var validationError = await ValidateTaskAsync(task, cancellationToken);
            if (validationError != null)
            {
                return validationError;
            }

            var schedulerState = await _quartzService.UpsertAsync(task, cancellationToken);
            ApplySchedulerState(task, schedulerState);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(ToDetail(task));
        }

        [HttpDelete("{id:long}")]
        [Authorize(Policy = AuthorizationPolicies.TasksManage)]
        public async Task<IActionResult> DeleteTask(long id, CancellationToken cancellationToken)
        {
            var task = await _dbContext.ScheduledTasks.SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);
            if (task == null)
            {
                return NotFound(new { Message = $"Task {id} was not found." });
            }

            if (task.IsSystemTask && task.TaskType == ScheduledTaskTypes.MasterOrchestrator)
            {
                return Conflict(new { Message = "The master orchestrator cannot be deleted. Use the shutdown endpoint if you need to stop it temporarily." });
            }

            await _quartzService.DeleteAsync(id, cancellationToken);

            task.IsEnabled = false;
            task.IsPaused = false;
            task.RuntimeStatus = ScheduledTaskRuntimeStatuses.Deleted;
            task.NextFireTime = null;
            task.CurrentExecutionStartedAt = null;
            task.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(new { Message = $"Task {id} was removed from the scheduler." });
        }

        [HttpPost("{id:long}/shutdown")]
        [Authorize(Policy = AuthorizationPolicies.TasksManage)]
        public async Task<IActionResult> ShutdownTask(long id, CancellationToken cancellationToken)
        {
            var task = await _dbContext.ScheduledTasks.SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);
            if (task == null)
            {
                return NotFound(new { Message = $"Task {id} was not found." });
            }

            if (task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Deleted)
            {
                return Conflict(new { Message = $"Task {id} was deleted and cannot be shut down." });
            }

            task.IsPaused = true;
            task.RuntimeStatus = ScheduledTaskRuntimeStatuses.Paused;
            task.UpdatedAt = DateTime.UtcNow;

            await _quartzService.PauseAsync(id, cancellationToken);
            var schedulerState = await _quartzService.ReadStateAsync(id, cancellationToken);
            if (schedulerState != null)
            {
                ApplySchedulerState(task, schedulerState);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(ToDetail(task));
        }

        [HttpPost("{id:long}/start")]
        [Authorize(Policy = AuthorizationPolicies.TasksManage)]
        public async Task<IActionResult> StartTask(long id, CancellationToken cancellationToken)
        {
            var task = await _dbContext.ScheduledTasks.SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);
            if (task == null)
            {
                return NotFound(new { Message = $"Task {id} was not found." });
            }

            if (task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Deleted)
            {
                return Conflict(new { Message = $"Task {id} was deleted and cannot be started again." });
            }

            task.IsEnabled = true;
            task.IsPaused = false;
            task.RuntimeStatus = ScheduledTaskRuntimeStatuses.Scheduled;
            task.UpdatedAt = DateTime.UtcNow;

            var validationError = await ValidateTaskAsync(task, cancellationToken);
            if (validationError != null)
            {
                return validationError;
            }

            var schedulerState = await _quartzService.UpsertAsync(task, cancellationToken);
            ApplySchedulerState(task, schedulerState);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(ToDetail(task));
        }

        [HttpPost("{id:long}/run-now")]
        [Authorize(Policy = AuthorizationPolicies.TasksManage)]
        public async Task<IActionResult> RunNow(long id, CancellationToken cancellationToken)
        {
            var task = await _dbContext.ScheduledTasks
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            if (task == null)
            {
                return NotFound(new { Message = $"Task {id} was not found." });
            }

            if (task.RuntimeStatus == ScheduledTaskRuntimeStatuses.Deleted)
            {
                return Conflict(new { Message = $"Task {id} was deleted and cannot be triggered." });
            }

            if (task.IsPaused)
            {
                return Conflict(new { Message = $"Task {id} is paused. Start it before triggering an immediate run." });
            }

            await _quartzService.TriggerNowAsync(id, cancellationToken);
            return Accepted(new { Message = $"Task {id} was sent to Quartz for immediate execution." });
        }

        private async Task<IActionResult?> ValidateTaskAsync(ScheduledTask task, CancellationToken cancellationToken)
        {
            if (task.TaskType != ScheduledTaskTypes.SymbolDataPull && task.TaskType != ScheduledTaskTypes.MasterOrchestrator)
            {
                return BadRequest(new { Message = $"Unsupported task type '{task.TaskType}'." });
            }

            if (task.ScheduleType != ScheduledTaskScheduleTypes.Cron && task.ScheduleType != ScheduledTaskScheduleTypes.Simple)
            {
                return BadRequest(new { Message = $"Unsupported schedule type '{task.ScheduleType}'." });
            }

            if (task.ScheduleType == ScheduledTaskScheduleTypes.Cron)
            {
                if (string.IsNullOrWhiteSpace(task.CronExpression))
                {
                    return BadRequest(new { Message = "CronExpression is required when ScheduleType is Cron." });
                }

                task.IntervalSeconds = null;
                task.RepeatCount = null;
            }
            else
            {
                if (!task.IntervalSeconds.HasValue || task.IntervalSeconds.Value <= 0)
                {
                    return BadRequest(new { Message = "IntervalSeconds must be greater than zero when ScheduleType is Simple." });
                }

                task.CronExpression = null;
            }

            if (task.TaskType == ScheduledTaskTypes.SymbolDataPull)
            {
                if (!task.ServerId.HasValue || task.ServerId.Value <= 0)
                {
                    return BadRequest(new { Message = "ServerId is required for SymbolDataPull tasks." });
                }

                if (string.IsNullOrWhiteSpace(task.Ticker))
                {
                    return BadRequest(new { Message = "Ticker is required for SymbolDataPull tasks." });
                }

                var serverExists = await _dbContext.TradingServers
                    .AsNoTracking()
                    .AnyAsync(server => server.Id == task.ServerId.Value && server.IsEnabled, cancellationToken);

                if (!serverExists)
                {
                    return BadRequest(new { Message = $"Trading server {task.ServerId.Value} is not enabled." });
                }

                var stockExists = await _dbContext.StockPrices
                    .AsNoTracking()
                    .AnyAsync(stock => stock.Ticker == task.Ticker, cancellationToken);

                if (!stockExists)
                {
                    return NotFound(new { Message = $"Ticker '{task.Ticker}' is not configured." });
                }
            }

            if (task.TaskType == ScheduledTaskTypes.MasterOrchestrator)
            {
                task.ServerId = null;
                task.Ticker = null;
            }

            return null;
        }

        private static void ApplySchedulerState(ScheduledTask task, SchedulerTaskState schedulerState)
        {
            task.NextFireTime = schedulerState.NextFireTime;
            task.RuntimeStatus = task.IsPaused
                ? ScheduledTaskRuntimeStatuses.Paused
                : ScheduledTaskRuntimeStatuses.Scheduled;
            task.UpdatedAt = DateTime.UtcNow;
        }

        private static object ToSummary(ScheduledTask task)
        {
            return new
            {
                task.Id,
                task.Name,
                task.TaskType,
                task.ScheduleType,
                task.ServerId,
                task.Ticker,
                task.IsSystemTask,
                task.IsEnabled,
                task.IsPaused,
                task.RuntimeStatus,
                task.LastExecutionStatus,
                task.LastTriggeredAt,
                task.LastCompletedAt,
                task.NextFireTime,
                task.ExecutionCount,
                task.FailureCount,
                task.AverageDurationMs,
                task.LastSchedulerInstance
            };
        }

        private static object ToDetail(ScheduledTask task)
        {
            return new
            {
                task.Id,
                task.Name,
                task.Description,
                task.TaskType,
                task.ScheduleType,
                task.CronExpression,
                task.IntervalSeconds,
                task.RepeatCount,
                task.ServerId,
                task.Ticker,
                task.IsSystemTask,
                task.IsEnabled,
                task.IsPaused,
                task.RuntimeStatus,
                task.LastExecutionStatus,
                task.LastExecutionDurationMs,
                task.LastTriggeredAt,
                task.CurrentExecutionStartedAt,
                task.LastCompletedAt,
                task.NextFireTime,
                task.ExecutionCount,
                task.FailureCount,
                task.AverageDurationMs,
                task.LastSchedulerInstance,
                task.LastError,
                task.CreatedAt,
                task.UpdatedAt
            };
        }

        private static string NormalizeTaskType(string taskType)
        {
            var normalized = taskType.Trim();
            if (string.Equals(normalized, ScheduledTaskTypes.MasterOrchestrator, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskTypes.MasterOrchestrator;
            }

            if (string.Equals(normalized, ScheduledTaskTypes.SymbolDataPull, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskTypes.SymbolDataPull;
            }

            return normalized;
        }

        private static string NormalizeScheduleType(string scheduleType)
        {
            var normalized = scheduleType.Trim();
            if (string.Equals(normalized, ScheduledTaskScheduleTypes.Cron, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskScheduleTypes.Cron;
            }

            if (string.Equals(normalized, ScheduledTaskScheduleTypes.Simple, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskScheduleTypes.Simple;
            }

            return normalized;
        }

        private static string? NormalizeCronExpression(string? cronExpression)
        {
            return string.IsNullOrWhiteSpace(cronExpression)
                ? null
                : cronExpression.Trim();
        }

        private static string NormalizeRuntimeStatus(string runtimeStatus)
        {
            var normalized = runtimeStatus.Trim();
            if (string.Equals(normalized, ScheduledTaskRuntimeStatuses.Scheduled, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskRuntimeStatuses.Scheduled;
            }

            if (string.Equals(normalized, ScheduledTaskRuntimeStatuses.Running, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskRuntimeStatuses.Running;
            }

            if (string.Equals(normalized, ScheduledTaskRuntimeStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskRuntimeStatuses.Completed;
            }

            if (string.Equals(normalized, ScheduledTaskRuntimeStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskRuntimeStatuses.Failed;
            }

            if (string.Equals(normalized, ScheduledTaskRuntimeStatuses.Paused, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskRuntimeStatuses.Paused;
            }

            if (string.Equals(normalized, ScheduledTaskRuntimeStatuses.Deleted, StringComparison.OrdinalIgnoreCase))
            {
                return ScheduledTaskRuntimeStatuses.Deleted;
            }

            return normalized;
        }

        private static string? NormalizeTicker(string? ticker)
        {
            return string.IsNullOrWhiteSpace(ticker)
                ? null
                : ticker.Trim().ToUpperInvariant();
        }
    }
}
