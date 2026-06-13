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
    public class FlowerBusinessCategoryGrain : Grain, IBusinessCategoryGrain
    {
        private readonly ILogger<FlowerBusinessCategoryGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerBusinessCategory, long> _context;

        public FlowerBusinessCategoryGrain(
            ILogger<FlowerBusinessCategoryGrain> logger,
            IDataContext<FlowerEntityContext, FlowerBusinessCategory, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<BusinessCategoryState> GetBusinessCategoryAsync(long id)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == id);
            return MapToState(entity);
        }

        public async Task<List<BusinessCategoryState>> GetShopBusinessCategoriesAsync(long shopId)
        {
            var entities = await _context.QueryAsync(e => e.ShopId == shopId);
            return entities.Select(MapToState).ToList();
        }

        public async Task<BusinessCategoryState> ApplyBusinessCategoryAsync(BusinessCategoryState category)
        {
            var entity = new FlowerBusinessCategory
            {
                ShopId = category.ShopId,
                CategoryId = category.CategoryId,
                CommissionRate = category.CommissionRate,
                AuditStatus = 0
            };
            var result = await _context.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<BusinessCategoryState> AuditBusinessCategoryAsync(long id, bool approved, string remark)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == id);
            if (entity == null) return null;
            entity.AuditStatus = approved ? 1 : 2;
            entity.AuditRemark = remark;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        private BusinessCategoryState MapToState(FlowerBusinessCategory entity)
        {
            if (entity == null) return null;
            return new BusinessCategoryState
            {
                Id = entity.Id,
                ShopId = entity.ShopId,
                CategoryId = entity.CategoryId,
                CommissionRate = entity.CommissionRate,
                AuditStatus = entity.AuditStatus,
                AuditRemark = entity.AuditRemark ?? ""
            };
        }
    }
}
