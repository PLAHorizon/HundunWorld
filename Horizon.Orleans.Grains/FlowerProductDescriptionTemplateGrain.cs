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
    public class FlowerProductDescriptionTemplateGrain : Grain, IProductDescriptionTemplateGrain
    {
        private readonly ILogger<FlowerProductDescriptionTemplateGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerProductDescriptionTemplate, long> _context;

        public FlowerProductDescriptionTemplateGrain(
            ILogger<FlowerProductDescriptionTemplateGrain> logger,
            IDataContext<FlowerEntityContext, FlowerProductDescriptionTemplate, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<ProductDescriptionTemplateState> GetTemplateAsync(long templateId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == templateId && !e.IsDeleted);
            return MapToState(entity);
        }

        public async Task<List<ProductDescriptionTemplateState>> GetShopTemplatesAsync(long shopId)
        {
            var entities = await _context.QueryAsync(e => e.ShopId == shopId && !e.IsDeleted);
            return entities.Select(MapToState).ToList();
        }

        public async Task<ProductDescriptionTemplateState> AddTemplateAsync(ProductDescriptionTemplateState template)
        {
            var entity = new FlowerProductDescriptionTemplate
            {
                ShopId = template.ShopId,
                TemplateName = template.TemplateName,
                TopContent = template.TopContent,
                BottomContent = template.BottomContent
            };
            var result = await _context.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<ProductDescriptionTemplateState> UpdateTemplateAsync(ProductDescriptionTemplateState template)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == template.Id);
            if (entity == null) return null;
            entity.TemplateName = template.TemplateName;
            entity.TopContent = template.TopContent;
            entity.BottomContent = template.BottomContent;
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

        private ProductDescriptionTemplateState MapToState(FlowerProductDescriptionTemplate entity)
        {
            if (entity == null) return null;
            return new ProductDescriptionTemplateState
            {
                Id = entity.Id,
                ShopId = entity.ShopId,
                TemplateName = entity.TemplateName ?? "",
                TopContent = entity.TopContent ?? "",
                BottomContent = entity.BottomContent ?? ""
            };
        }
    }
}
