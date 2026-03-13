namespace TradingSystem.Api.DTOs
{
    public class CreateTaskRequest
    {
        public required string JobName { get; set; }
        public required string ServerId { get; set; }
        public required string CronExpression { get; set; }
    }
}
