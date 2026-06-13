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
    public class FlowerBrandGrain : Grain, IBrandGrain
    {
        private readonly ILogger<FlowerBrandGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerBrand, long> _brandContext;
        private readonly IDataContext<FlowerEntityContext, FlowerShopBrandApply, long> _applyContext;

        public FlowerBrandGrain(
            ILogger<FlowerBrandGrain> logger,
            IDataContext<FlowerEntityContext, FlowerBrand, long> brandContext,
            IDataContext<FlowerEntityContext, FlowerShopBrandApply, long> applyContext)
        {
            _logger = logger;
            _brandContext = brandContext;
            _applyContext = applyContext;
        }

        public async Task<BrandState> GetBrandAsync(long brandId)
        {
            var entity = await _brandContext.QueryFirstOrDefaultAsync(e => e.Id == brandId && !e.IsDeleted);
            return MapBrandToState(entity);
        }

        public async Task<List<BrandState>> GetAllBrandsAsync()
        {
            var entities = await _brandContext.QueryAsync(e => !e.IsDeleted);
            return entities.Select(MapBrandToState).ToList();
        }

        public async Task<BrandState> AddBrandAsync(BrandState brand)
        {
            var entity = new FlowerBrand
            {
                Name = brand.Name,
                Logo = brand.Logo,
                Description = brand.Description,
                DisplaySequence = brand.DisplaySequence,
                IsRecommend = brand.IsRecommend,
                AuditStatus = brand.AuditStatus
            };
            var result = await _brandContext.AddAsync(entity);
            return MapBrandToState(result);
        }

        public async Task<BrandState> UpdateBrandAsync(BrandState brand)
        {
            var entity = await _brandContext.QueryFirstOrDefaultAsync(e => e.Id == brand.Id);
            if (entity == null) return null;
            entity.Name = brand.Name;
            entity.Logo = brand.Logo;
            entity.Description = brand.Description;
            entity.DisplaySequence = brand.DisplaySequence;
            entity.IsRecommend = brand.IsRecommend;
            entity.AuditStatus = brand.AuditStatus;
            await _brandContext.UpdateAsync(entity, entity.Id);
            return MapBrandToState(entity);
        }

        public async Task<bool> DeleteBrandAsync(long brandId)
        {
            var entity = await _brandContext.QueryFirstOrDefaultAsync(e => e.Id == brandId);
            if (entity == null) return false;
            entity.IsDeleted = true;
            await _brandContext.UpdateAsync(entity, entity.Id);
            return true;
        }

        public async Task<ShopBrandApplyState> ApplyBrandAsync(ShopBrandApplyState apply)
        {
            var entity = new FlowerShopBrandApply
            {
                ShopId = apply.ShopId,
                BrandName = apply.BrandName,
                ProofMaterial = apply.ProofMaterial,
                AuditStatus = 0
            };
            var result = await _applyContext.AddAsync(entity);
            return MapApplyToState(result);
        }

        public async Task<ShopBrandApplyState> AuditBrandApplyAsync(long applyId, bool approved, string remark)
        {
            var entity = await _applyContext.QueryFirstOrDefaultAsync(e => e.Id == applyId);
            if (entity == null) return null;
            entity.AuditStatus = approved ? 1 : 2;
            entity.AuditRemark = remark;
            await _applyContext.UpdateAsync(entity, entity.Id);
            return MapApplyToState(entity);
        }

        public async Task<List<ShopBrandApplyState>> GetShopBrandAppliesAsync(long shopId)
        {
            var entities = await _applyContext.QueryAsync(e => e.ShopId == shopId);
            return entities.Select(MapApplyToState).ToList();
        }

        private BrandState MapBrandToState(FlowerBrand entity)
        {
            if (entity == null) return null;
            return new BrandState
            {
                Id = entity.Id,
                Name = entity.Name ?? "",
                Logo = entity.Logo ?? "",
                Description = entity.Description ?? "",
                DisplaySequence = entity.DisplaySequence,
                IsRecommend = entity.IsRecommend,
                AuditStatus = entity.AuditStatus
            };
        }

        private ShopBrandApplyState MapApplyToState(FlowerShopBrandApply entity)
        {
            if (entity == null) return null;
            return new ShopBrandApplyState
            {
                Id = entity.Id,
                ShopId = entity.ShopId,
                BrandName = entity.BrandName ?? "",
                ProofMaterial = entity.ProofMaterial ?? "",
                AuditStatus = entity.AuditStatus,
                AuditRemark = entity.AuditRemark ?? ""
            };
        }
    }
}
