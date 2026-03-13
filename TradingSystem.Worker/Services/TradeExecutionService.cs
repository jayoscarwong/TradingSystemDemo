using TradingSystem.Domain.Entities;

namespace TradingSystem.Worker.Services
{
    public sealed class TradeExecutionService
    {
        private const decimal QueuedPressureRatio = 0.005m;
        private const decimal ImbalanceDriftRatio = 0.0025m;

        public TradeExecutionSummary Apply(TradeOrder order, StockPrice stock, DateTime processedAtUtc)
        {
            var maxAvailableVolume = Math.Max(0m, stock.TotalStockVolume);
            stock.AvailableVolume = Math.Clamp(stock.AvailableVolume, 0m, maxAvailableVolume);

            var priceBefore = stock.CurrentPrice;
            var totalVolume = stock.TotalStockVolume <= 0m ? 1m : stock.TotalStockVolume;
            var remainingVolume = order.Volume;

            if (order.IsBuy)
            {
                var queuedSellMatch = Math.Min(remainingVolume, stock.PendingSellVolume);
                stock.PendingSellVolume -= queuedSellMatch;
                remainingVolume -= queuedSellMatch;

                var immediatelyAvailableVolume = Math.Min(remainingVolume, stock.AvailableVolume);
                stock.AvailableVolume -= immediatelyAvailableVolume;
                remainingVolume -= immediatelyAvailableVolume;

                if (remainingVolume > 0m)
                {
                    stock.PendingBuyVolume += remainingVolume;
                }

                stock.BuyVolume += order.Volume;
            }
            else
            {
                var queuedBuyMatch = Math.Min(remainingVolume, stock.PendingBuyVolume);
                stock.PendingBuyVolume -= queuedBuyMatch;
                remainingVolume -= queuedBuyMatch;

                var restockCapacity = Math.Max(0m, maxAvailableVolume - stock.AvailableVolume);
                var restockedVolume = Math.Min(remainingVolume, restockCapacity);
                stock.AvailableVolume += restockedVolume;
                remainingVolume -= restockedVolume;

                if (remainingVolume > 0m)
                {
                    stock.PendingSellVolume += remainingVolume;
                }

                stock.SellVolume += order.Volume;
            }

            var executedVolume = order.Volume - remainingVolume;
            var executedFraction = Math.Min(1m, executedVolume / totalVolume);
            var queuedFraction = Math.Min(1m, remainingVolume / totalVolume);
            var imbalanceFraction = Math.Clamp((stock.PendingBuyVolume - stock.PendingSellVolume) / totalVolume, -1m, 1m);

            var baseMove = (order.BidAmount - priceBefore) * executedFraction;
            var pressureMove = priceBefore * queuedFraction * QueuedPressureRatio * (order.IsBuy ? 1m : -1m);
            var imbalanceMove = priceBefore * imbalanceFraction * ImbalanceDriftRatio;

            stock.CurrentPrice = Round(Math.Max(0.01m, priceBefore + baseMove + pressureMove + imbalanceMove));
            stock.AvailableVolume = Round(Math.Clamp(stock.AvailableVolume, 0m, maxAvailableVolume));
            stock.PendingBuyVolume = Round(Math.Max(0m, stock.PendingBuyVolume));
            stock.PendingSellVolume = Round(Math.Max(0m, stock.PendingSellVolume));
            stock.BuyVolume = Round(stock.BuyVolume);
            stock.SellVolume = Round(stock.SellVolume);
            stock.LastUpdatedAt = processedAtUtc;

            order.ExecutedVolume = Round(executedVolume);
            order.QueuedVolume = Round(remainingVolume);
            order.Status = remainingVolume switch
            {
                0m => "Completed",
                _ when executedVolume > 0m => "PartiallyQueued",
                _ => "QueuedForLiquidity"
            };
            order.IsProcessed = true;
            order.ProcessedAt = processedAtUtc;

            return new TradeExecutionSummary(
                priceBefore,
                stock.CurrentPrice,
                order.ExecutedVolume,
                order.QueuedVolume,
                stock.AvailableVolume,
                stock.PendingBuyVolume,
                stock.PendingSellVolume);
        }

        private static decimal Round(decimal value)
        {
            return decimal.Round(value, 4, MidpointRounding.AwayFromZero);
        }
    }

    public sealed record TradeExecutionSummary(
        decimal PreviousPrice,
        decimal NewPrice,
        decimal ExecutedVolume,
        decimal QueuedVolume,
        decimal AvailableVolume,
        decimal PendingBuyVolume,
        decimal PendingSellVolume);
}
