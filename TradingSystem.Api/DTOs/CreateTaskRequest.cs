namespace TradingSystem.Api.DTOs
{
    public class CreateTaskRequest
    {
        public required string JobName { get; set; }
        public required int ServerId { get; set; }
        public required string Ticker { get; set; } // <-- Added dynamic Ticker property
        public required string CronExpression { get; set; }
    }
}


