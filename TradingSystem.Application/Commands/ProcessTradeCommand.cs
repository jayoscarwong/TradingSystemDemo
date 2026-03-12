using System;

namespace TradingSystem.Application.Commands
{
    public record ProcessTradeCommand
    {
        public Guid OrderId { get; init; }
    }
}