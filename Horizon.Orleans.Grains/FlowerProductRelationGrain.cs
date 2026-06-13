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
    public class FlowerProductRelationGrain : Grain, IProductRelationGrain
    {
        private readonly ILogger<FlowerProductRelationGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerProductRelation, long> _context;

        public FlowerProductRelationGrain(
            ILogger<FlowerProductRelationGrain> logger,
            IDataContext<FlowerEntityContext, FlowerProductRelation, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<List<ProductRelationState>> GetProductRelationsAsync(long productId)
        {
            var entities = await _context.QueryAsync(e => e.ProductId == productId);
            return entities.Select(MapToState).ToList();
        }

        public async Task<bool> SetProductRelationsAsync(long productId, List<ProductRelationState> relations)
        {
            var existing = await _context.QueryAsync(e => e.ProductId == productId);
            foreach (var item in existing)
            {
                await _context.DeletedAsync<FlowerProductRelation, long>(item.Id);
            }

            for (int i = 0; i < relations.Count; i++)
            {
                var entity = new FlowerProductRelation
                {
                    ProductId = productId,
                    RelatedProductId = relations[i].RelatedProductId,
                    DisplaySequence = i
                };
                await _context.AddAsync(entity);
            }
            return true;
        }

        private ProductRelationState MapToState(FlowerProductRelation entity)
        {
            if (entity == null) return null;
            return new ProductRelationState
            {
                Id = entity.Id,
                ProductId = entity.ProductId,
                RelatedProductId = entity.RelatedProductId,
                DisplaySequence = entity.DisplaySequence
            };
        }
    }
}
