namespace TradingSystem.Api.DTOs
{
    public class UpdateTaskRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ScheduleType { get; set; }
        public string? CronExpression { get; set; }
        public int? IntervalSeconds { get; set; }
        public int? RepeatCount { get; set; }
        public int? ServerId { get; set; }
        public string? Ticker { get; set; }
    }
}
