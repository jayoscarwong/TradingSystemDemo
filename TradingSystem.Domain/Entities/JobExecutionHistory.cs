using System;

namespace TradingSystem.Domain.Entities
{
    public class JobExecutionHistory
    {
        public Guid Id { get; set; }
        public required string JobName { get; set; }
        public int? ServerId { get; set; } // Changed to int?
        public required string Status { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string? ErrorMessage { get; set; }
    }
}