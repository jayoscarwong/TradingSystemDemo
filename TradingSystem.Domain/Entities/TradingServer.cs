using System;
using System.Collections.Generic;
using System.Text;

namespace TradingSystem.Domain.Entities
{
    public class TradingServer
    {
        public required string Id { get; set; }
        public required string ServerName { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime? LastPingAt { get; set; }
    }
}