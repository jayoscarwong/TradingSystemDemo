using System;

namespace TradingSystem.Domain.Entities
{
    public class JobExecutionHistory
    {
        public Guid Id { get; set; }
        public long? ScheduledTaskId { get; set; }
        public string? TaskName { get; set; }
        public string? TaskType { get; set; }
        public required string JobName { get; set; }
        public int? ServerId { get; set; }
        public string? Ticker { get; set; }
        public required string Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double? DurationMs { get; set; }
        public string? SchedulerInstance { get; set; }
        public string? ErrorMessage { get; set; }

        public ScheduledTask? ScheduledTask { get; set; }
    }
}
