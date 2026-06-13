using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains
{
    public class FlowerFreightTemplateGrain : Grain, IFreightTemplateGrain
    {
        private readonly ILogger<FlowerFreightTemplateGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerFreightTemplate, long> _context;

        public FlowerFreightTemplateGrain(
            ILogger<FlowerFreightTemplateGrain> logger,
            IDataContext<FlowerEntityContext, FlowerFreightTemplate, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<FreightTemplateState> GetTemplateAsync(long templateId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == templateId);
            return MapToState(entity);
        }

        public async Task<List<FreightTemplateState>> GetMerchantTemplatesAsync(long merchantId)
        {
            var entities = await _context.QueryAsync(e => e.MerchantId == merchantId && !e.IsDeleted);
            return entities.Select(MapToState).ToList();
        }

        public async Task<FreightTemplateState> AddTemplateAsync(FreightTemplateState template)
        {
            var entity = new FlowerFreightTemplate
            {
                MerchantId = template.MerchantId,
                Name = template.Name,
                ValuationMethod = template.ValuationMethod,
                IsFree = template.IsFree,
                FirstUnit = template.FirstUnit,
                FirstPrice = template.FirstPrice,
                ContinueUnit = template.ContinueUnit,
                ContinuePrice = template.ContinuePrice,
                FreeConditionAmount = template.FreeConditionAmount,
                AreaRules = template.AreaRules,
               Passport="",
            };
            var result = await _context.AddAsync(entity);
            return result != null ? MapToState(result) : null;
        }

        public async Task<FreightTemplateState> UpdateTemplateAsync(FreightTemplateState template)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == template.Id);
            if (entity == null) return null;
            entity.Name = template.Name;
            entity.ValuationMethod = template.ValuationMethod;
            entity.IsFree = template.IsFree;
            entity.FirstUnit = template.FirstUnit;
            entity.FirstPrice = template.FirstPrice;
            entity.ContinueUnit = template.ContinueUnit;
            entity.ContinuePrice = template.ContinuePrice;
            entity.FreeConditionAmount = template.FreeConditionAmount;
            entity.AreaRules = template.AreaRules;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<bool> DeleteTemplateAsync(long templateId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == templateId);
            if (entity == null) return false;
            entity.IsDeleted = true;
            await _context.UpdateAsync(entity, entity.Id);
            return true;
        }

        public async Task<decimal> CalculateFreightAsync(long templateId, decimal quantity, string regionId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == templateId);
            if (entity == null) return 0;
            if (entity.IsFree) return 0;
            if (entity.FreeConditionAmount.HasValue && quantity >= entity.FreeConditionAmount.Value) return 0;

            var freight = entity.FirstPrice;
            if (quantity > entity.FirstUnit)
            {
                var continueCount = System.Math.Ceiling((quantity - entity.FirstUnit) / entity.ContinueUnit);
                freight += continueCount * entity.ContinuePrice;
            }
            return freight;
        }

        private FreightTemplateState MapToState(FlowerFreightTemplate entity)
        {
            if (entity == null) return null;
            return new FreightTemplateState
            {
                Id = entity.Id,
                MerchantId = entity.MerchantId,
                Name = entity.Name ?? "",
                ValuationMethod = entity.ValuationMethod,
                IsFree = entity.IsFree,
                FirstUnit = entity.FirstUnit,
                FirstPrice = entity.FirstPrice,
                ContinueUnit = entity.ContinueUnit,
                ContinuePrice = entity.ContinuePrice,
                FreeConditionAmount = entity.FreeConditionAmount,
                AreaRules = entity.AreaRules ?? ""
            };
        }
    }
}
