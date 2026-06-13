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
    public class FlowerProductCommentGrain : Grain, IProductCommentGrain
    {
        private readonly ILogger<FlowerProductCommentGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerProductComment, long> _context;
        private readonly IDataContext<FlowerEntityContext, FlowerProduct, long> _productContext;

        public FlowerProductCommentGrain(
            ILogger<FlowerProductCommentGrain> logger,
            IDataContext<FlowerEntityContext, FlowerProductComment, long> context,
            IDataContext<FlowerEntityContext, FlowerProduct, long> productContext)
        {
            _logger = logger;
            _context = context;
            _productContext = productContext;
        }

        public async Task<ProductCommentState> SubmitCommentAsync(ProductCommentState comment)
        {
            var entity = new FlowerProductComment
            {
                ProductId = comment.ProductId,
                OrderId = comment.OrderId,
                UserId = comment.UserId,
                Rank = comment.Rank,
                Content = comment.Content,
                Images = comment.Images,
                IsAnonymous = comment.IsAnonymous
            };
            var result = await _context.AddAsync(entity);
            return result != null ? MapToState(result) : null;
        }

        public async Task<ProductCommentState> ReplyCommentAsync(long commentId, string replyContent)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == commentId);
            if (entity == null) return null;
            entity.ReplyContent = replyContent;
            entity.ReplyTime = DateTime.Now;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<List<ProductCommentState>> GetProductCommentsAsync(long productId, int page, int pageSize)
        {
            var entities = await _context.QueryAsync(e => e.ProductId == productId && e.IsValid);
            return entities.OrderByDescending(e => e.CreateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapToState).ToList();
        }

        public async Task<List<ProductCommentState>> GetMerchantCommentsAsync(long merchantId, int page, int pageSize)
        {
            var comments = await _context.QueryAsync(e => e.IsValid);
            var merchantProducts = await _productContext.QueryAsync(p => p.MerchantId == merchantId);
            var entities = comments
                .Join(merchantProducts, c => c.ProductId, p => p.Id, (c, _) => c)
                .OrderByDescending(e => e.CreateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return entities.Select(MapToState).ToList();
        }

        private ProductCommentState MapToState(FlowerProductComment entity)
        {
            if (entity == null) return null;
            return new ProductCommentState
            {
                Id = entity.Id,
                ProductId = entity.ProductId,
                OrderId = entity.OrderId,
                UserId = entity.UserId,
                Rank = entity.Rank,
                Content = entity.Content ?? "",
                Images = entity.Images ?? "",
                ReplyContent = entity.ReplyContent ?? "",
                ReplyTime = entity.ReplyTime,
                IsAnonymous = entity.IsAnonymous
            };
        }
    }
}
