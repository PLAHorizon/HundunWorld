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
    public class FlowerShopGradeGrain : Grain, IShopGradeGrain
    {
        private readonly ILogger<FlowerShopGradeGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerShopGrade, long> _context;

        public FlowerShopGradeGrain(
            ILogger<FlowerShopGradeGrain> logger,
            IDataContext<FlowerEntityContext, FlowerShopGrade, long> context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<ShopGradeState> GetShopGradeAsync(long gradeId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == gradeId);
            return MapToState(entity);
        }

        public async Task<List<ShopGradeState>> GetAllShopGradesAsync()
        {
            var entities = await _context.QueryAsync(e => !e.IsDeleted);
            return entities.Select(MapToState).ToList();
        }

        public async Task<ShopGradeState> AddShopGradeAsync(ShopGradeState grade)
        {
            var entity = new FlowerShopGrade
            {
                Name = grade.Name,
                ProductLimit = grade.ProductLimit,
                ImageLimit = grade.ImageLimit,
                TemplateLimit = grade.TemplateLimit,
                ChargeStandard = grade.ChargeStandard,
                Remark = grade.Remark
            };
            var result = await _context.AddAsync(entity);
            return result != null ? MapToState(result) : null;
        }

        public async Task<ShopGradeState> UpdateShopGradeAsync(ShopGradeState grade)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == grade.Id);
            if (entity == null) return null;
            entity.Name = grade.Name;
            entity.ProductLimit = grade.ProductLimit;
            entity.ImageLimit = grade.ImageLimit;
            entity.TemplateLimit = grade.TemplateLimit;
            entity.ChargeStandard = grade.ChargeStandard;
            entity.Remark = grade.Remark;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<bool> DeleteShopGradeAsync(long gradeId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == gradeId);
            if (entity == null) return false;
            entity.IsDeleted = true;
            await _context.UpdateAsync(entity, entity.Id);
            return true;
        }

        private ShopGradeState MapToState(FlowerShopGrade entity)
        {
            if (entity == null) return null;
            return new ShopGradeState
            {
                Id = entity.Id,
                Name = entity.Name ?? "",
                ProductLimit = entity.ProductLimit,
                ImageLimit = entity.ImageLimit,
                TemplateLimit = entity.TemplateLimit,
                ChargeStandard = entity.ChargeStandard,
                Remark = entity.Remark ?? ""
            };
        }
    }
}
