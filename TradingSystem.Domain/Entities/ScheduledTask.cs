namespace TradingSystem.Domain.Entities
{
    public class ScheduledTask
    {
        public long Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public required string TaskType { get; set; }
        public required string ScheduleType { get; set; }
        public string? CronExpression { get; set; }
        public int? IntervalSeconds { get; set; }
        public int? RepeatCount { get; set; }
        public int? ServerId { get; set; }
        public string? Ticker { get; set; }
        public bool IsSystemTask { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsPaused { get; set; }
        public bool AllowConcurrentExecution { get; set; }
        public required string RuntimeStatus { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastTriggeredAt { get; set; }
        public DateTime? LastCompletedAt { get; set; }
        public DateTime? CurrentExecutionStartedAt { get; set; }
        public DateTime? NextFireTime { get; set; }
        public string? LastExecutionStatus { get; set; }
        public double? LastExecutionDurationMs { get; set; }
        public double AverageDurationMs { get; set; }
        public long ExecutionCount { get; set; }
        public long FailureCount { get; set; }
        public string? LastSchedulerInstance { get; set; }
        public string? LastError { get; set; }
        public DateTime RowVersion { get; set; }

        public TradingServer? TradingServer { get; set; }
        public ICollection<JobExecutionHistory> ExecutionHistories { get; set; } = new List<JobExecutionHistory>();
    }
}
