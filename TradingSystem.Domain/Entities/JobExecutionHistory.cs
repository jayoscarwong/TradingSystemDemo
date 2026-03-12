using System;

namespace TradingSystem.Domain.Entities
{
    public class JobExecutionHistory
    {
        public Guid Id { get; set; }
        public string JobName { get; set; }
        public string ServerId { get; set; }
        public string Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string ErrorMessage { get; set; }
    }
}