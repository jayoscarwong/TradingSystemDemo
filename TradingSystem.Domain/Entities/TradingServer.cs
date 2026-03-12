using System;
using System.Collections.Generic;
using System.Text;

namespace TradingSystem.Domain.Entities
{
    public class TradingServer
    {
        public string Id { get; set; }
        public string ServerName { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime? LastPingAt { get; set; }
    }
}