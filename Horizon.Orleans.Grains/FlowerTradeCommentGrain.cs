using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains
{
    public class FlowerTradeCommentGrain : Grain, ITradeCommentGrain
    {
        private readonly ILogger<FlowerTradeCommentGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerTradeComment, long> _context;

        public FlowerTradeCommentGrain(
            ILogger<FlowerTradeCommentGrain> logger,
            IDataContext<FlowerEntityContext, FlowerTradeComment, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<TradeCommentState> SubmitTradeCommentAsync(TradeCommentState comment)
        {
            var entity = new FlowerTradeComment
            {
                OrderId = comment.OrderId,
                UserId = comment.UserId,
                ShopId = comment.ShopId,
                DescriptionScore = comment.DescriptionScore,
                ServiceScore = comment.ServiceScore,
                LogisticsScore = comment.LogisticsScore,
                Content = comment.Content,
                IsAnonymous = comment.IsAnonymous
            };
            var result = await _context.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<TradeCommentState> GetOrderTradeCommentAsync(long orderId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.OrderId == orderId);
            return MapToState(entity);
        }

        public async Task<List<TradeCommentState>> GetShopTradeCommentsAsync(long shopId)
        {
            var entities = await _context.QueryAsync(e => e.ShopId == shopId);
            return entities.Select(MapToState).ToList();
        }

        public async Task<TradeCommentState> GetShopAverageScoreAsync(long shopId)
        {
            var entities = await _context.QueryAsync(e => e.ShopId == shopId);
            if (!entities.Any()) return new TradeCommentState { ShopId = shopId };
            return new TradeCommentState
            {
                ShopId = shopId,
                DescriptionScore = (int)Math.Round(entities.Average(e => e.DescriptionScore)),
                ServiceScore = (int)Math.Round(entities.Average(e => e.ServiceScore)),
                LogisticsScore = (int)Math.Round(entities.Average(e => e.LogisticsScore))
            };
        }

        private TradeCommentState MapToState(FlowerTradeComment entity)
        {
            if (entity == null) return null;
            return new TradeCommentState
            {
                Id = entity.Id,
                OrderId = entity.OrderId,
                UserId = entity.UserId,
                ShopId = entity.ShopId,
                DescriptionScore = entity.DescriptionScore,
                ServiceScore = entity.ServiceScore,
                LogisticsScore = entity.LogisticsScore,
                Content = entity.Content ?? "",
                IsAnonymous = entity.IsAnonymous
            };
        }
    }
}
