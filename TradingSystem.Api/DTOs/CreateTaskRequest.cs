namespace TradingSystem.Api.DTOs
{
    public class CreateTaskRequest
    {
        public required string Name { get; set; }
        public string? Description { get; set; }
        public string TaskType { get; set; } = "SymbolDataPull";
        public string ScheduleType { get; set; } = "Cron";
        public string? CronExpression { get; set; }
        public int? IntervalSeconds { get; set; }
        public int? RepeatCount { get; set; }
        public int? ServerId { get; set; }
        public string? Ticker { get; set; }
    }
}
