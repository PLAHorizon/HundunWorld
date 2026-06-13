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
    public class FlowerFullDiscountGrain : Grain, IFullDiscountGrain
    {
        private readonly ILogger<FlowerFullDiscountGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerFullDiscountRule, long> _context;

        public FlowerFullDiscountGrain(
            ILogger<FlowerFullDiscountGrain> logger,
            IDataContext<FlowerEntityContext, FlowerFullDiscountRule, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<FullDiscountRuleState> GetRuleAsync(long ruleId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == ruleId && !e.IsDeleted);
            return MapToState(entity);
        }

        public async Task<List<FullDiscountRuleState>> GetShopRulesAsync(long shopId)
        {
            var entities = await _context.QueryAsync(e => e.ShopId == shopId && !e.IsDeleted);
            return entities.Select(MapToState).ToList();
        }

        public async Task<FullDiscountRuleState> AddRuleAsync(FullDiscountRuleState rule)
        {
            var entity = new FlowerFullDiscountRule
            {
                ShopId = rule.ShopId,
                RuleName = rule.RuleName,
                StartDate = rule.StartDate,
                EndDate = rule.EndDate,
                LimitValue = rule.LimitValue,
                DiscountValue = rule.DiscountValue,
                IsActive = rule.IsActive
            };
            var result = await _context.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<FullDiscountRuleState> UpdateRuleAsync(FullDiscountRuleState rule)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == rule.Id);
            if (entity == null) return null;
            entity.RuleName = rule.RuleName;
            entity.StartDate = rule.StartDate;
            entity.EndDate = rule.EndDate;
            entity.LimitValue = rule.LimitValue;
            entity.DiscountValue = rule.DiscountValue;
            entity.IsActive = rule.IsActive;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<bool> DeleteRuleAsync(long ruleId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == ruleId);
            if (entity == null) return false;
            entity.IsDeleted = true;
            await _context.UpdateAsync(entity, entity.Id);
            return true;
        }

        public async Task<decimal> CalculateDiscountAsync(long shopId, decimal orderAmount)
        {
            var now = System.DateTime.Now;
            var rules = await _context.QueryAsync(e => e.ShopId == shopId && !e.IsDeleted && e.IsActive && e.StartDate <= now && e.EndDate >= now);
            var applicableRules = rules.Where(e => e.LimitValue <= orderAmount).OrderByDescending(e => e.LimitValue).ToList();
            if (applicableRules.Count == 0) return 0;
            return applicableRules.First().DiscountValue;
        }

        private FullDiscountRuleState MapToState(FlowerFullDiscountRule entity)
        {
            if (entity == null) return null;
            return new FullDiscountRuleState
            {
                Id = entity.Id,
                ShopId = entity.ShopId,
                RuleName = entity.RuleName ?? "",
                StartDate = entity.StartDate,
                EndDate = entity.EndDate,
                LimitValue = entity.LimitValue,
                DiscountValue = entity.DiscountValue,
                IsActive = entity.IsActive
            };
        }
    }
}
