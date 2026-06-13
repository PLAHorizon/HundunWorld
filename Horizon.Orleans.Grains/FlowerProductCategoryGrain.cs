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
    public class FlowerProductCategoryGrain : Grain, IProductCategoryGrain
    {
        private readonly ILogger<FlowerProductCategoryGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerProductCategory, long> _context;

        public FlowerProductCategoryGrain(
            ILogger<FlowerProductCategoryGrain> logger,
            IDataContext<FlowerEntityContext, FlowerProductCategory, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<ProductCategoryState> GetCategoryAsync(long categoryId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == categoryId);
            return MapToState(entity);
        }

        public async Task<List<ProductCategoryState>> GetCategoryTreeAsync()
        {
            var entities = await _context.QueryAsync(e => e.IsValid);
            return entities.OrderBy(e => e.Depth).ThenBy(e => e.DisplaySequence).Select(MapToState).ToList();
        }

        public async Task<List<ProductCategoryState>> GetSubCategoriesAsync(long parentCategoryId)
        {
            var entities = await _context.QueryAsync(e => e.ParentCategoryId == parentCategoryId && e.IsValid);
            return entities.OrderBy(e => e.DisplaySequence).Select(MapToState).ToList();
        }

        public async Task<ProductCategoryState> AddCategoryAsync(ProductCategoryState category)
        {
            var entity = new FlowerProductCategory
            {
                Name = category.Name,
                Depth = category.Depth,
                Path = category.Path,
                ParentCategoryId = category.ParentCategoryId,
                DisplaySequence = category.DisplaySequence,
                Icon = category.Icon,
                Image = category.Image,
                Passport = "SYSTEM",
                CreateTime = DateTime.Now,
                IsValid = true,
                IsDeleted = false
            };
            var result = await _context.AddAsync(entity);
            return result != null ? MapToState(result) : null;
        }

        public async Task<ProductCategoryState> UpdateCategoryAsync(ProductCategoryState category)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == category.Id);
            if (entity == null) return null;
            entity.Name = category.Name;
            entity.DisplaySequence = category.DisplaySequence;
            entity.Icon = category.Icon;
            entity.Image = category.Image;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<bool> DeleteCategoryAsync(long categoryId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == categoryId);
            if (entity == null) return false;
            entity.IsDeleted = true;
            await _context.UpdateAsync(entity, entity.Id);
            return true;
        }

        private ProductCategoryState MapToState(FlowerProductCategory entity)
        {
            if (entity == null) return null;
            return new ProductCategoryState
            {
                Id = entity.Id,
                Name = entity.Name ?? "",
                Depth = entity.Depth,
                Path = entity.Path ?? "",
                ParentCategoryId = entity.ParentCategoryId,
                DisplaySequence = entity.DisplaySequence,
                Icon = entity.Icon ?? "",
                Image = entity.Image ?? ""
            };
        }
    }
}
