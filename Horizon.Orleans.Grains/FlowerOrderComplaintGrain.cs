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
    public class FlowerOrderComplaintGrain : Grain, IOrderComplaintGrain
    {
        private readonly ILogger<FlowerOrderComplaintGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderComplaint, long> _context;

        public FlowerOrderComplaintGrain(
            ILogger<FlowerOrderComplaintGrain> logger,
            IDataContext<FlowerEntityContext, FlowerOrderComplaint, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<OrderComplaintState> SubmitComplaintAsync(OrderComplaintState complaint)
        {
            var entity = new FlowerOrderComplaint
            {
                OrderId = complaint.OrderId,
                UserId = complaint.UserId,
                ShopId = complaint.ShopId,
                ComplaintReason = complaint.ComplaintReason,
                ComplaintContent = complaint.ComplaintContent,
                Status = 0,
                CreatedAt = DateTime.Now
            };
            var result = await _context.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<OrderComplaintState> GetComplaintAsync(long complaintId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == complaintId);
            return MapToState(entity);
        }

        public async Task<OrderComplaintState> GetOrderComplaintAsync(long orderId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.OrderId == orderId);
            return MapToState(entity);
        }

        public async Task<OrderComplaintState> HandleComplaintAsync(long complaintId, string replyContent)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == complaintId);
            if (entity == null) return null;
            entity.ReplyContent = replyContent;
            entity.Status = 2;
            entity.ResolvedAt = DateTime.Now;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<List<OrderComplaintState>> GetShopComplaintsAsync(long shopId)
        {
            var entities = await _context.QueryAsync(e => e.ShopId == shopId);
            return entities.Select(MapToState).ToList();
        }

        public async Task<List<OrderComplaintState>> GetUserComplaintsAsync(Guid userId)
        {
            var entities = await _context.QueryAsync(e => e.UserId == userId);
            return entities.Select(MapToState).ToList();
        }

        private OrderComplaintState MapToState(FlowerOrderComplaint entity)
        {
            if (entity == null) return null;
            return new OrderComplaintState
            {
                Id = entity.Id,
                OrderId = entity.OrderId,
                UserId = entity.UserId,
                ShopId = entity.ShopId,
                ComplaintReason = entity.ComplaintReason ?? "",
                ComplaintContent = entity.ComplaintContent ?? "",
                Status = entity.Status,
                ReplyContent = entity.ReplyContent ?? "",
                CreatedAt = entity.CreatedAt,
                ResolvedAt = entity.ResolvedAt
            };
        }
    }
}
