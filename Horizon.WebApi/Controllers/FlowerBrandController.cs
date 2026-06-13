using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.WebApi.Configs;
using Orleans;
using Orleans.Configuration;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerBrandController : OrleansControllerBase
    {
        private readonly ILogger<FlowerBrandController> _logger;

        public FlowerBrandController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerBrandController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("{brandId}")]
        public async Task<ResultVM<BrandState>> GetBrandAsync(long brandId)
        {
            var result = new ResultVM<BrandState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBrandGrain>(0);
                result.Data = await grain.GetBrandAsync(brandId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取品牌失败: BrandId={BrandId}", brandId);
                result.ErrorMessage = "获取品牌失败";
            }
            return result;
        }

        [HttpGet]
        public async Task<ResultVM<List<BrandState>>> GetAllBrandsAsync()
        {
            var result = new ResultVM<List<BrandState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBrandGrain>(0);
                result.Data = await grain.GetAllBrandsAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取品牌列表失败");
                result.ErrorMessage = "获取品牌列表失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<BrandState>> AddBrandAsync([FromBody] BrandState brand)
        {
            var result = new ResultVM<BrandState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBrandGrain>(0);
                result.Data = await grain.AddBrandAsync(brand);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建品牌失败");
                result.ErrorMessage = "创建品牌失败";
            }
            return result;
        }

        [HttpPut("{brandId}")]
        public async Task<ResultVM<BrandState>> UpdateBrandAsync(long brandId, [FromBody] BrandState brand)
        {
            var result = new ResultVM<BrandState>();
            try
            {
                brand.Id = brandId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBrandGrain>(0);
                result.Data = await grain.UpdateBrandAsync(brand);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新品牌失败: BrandId={BrandId}", brandId);
                result.ErrorMessage = "更新品牌失败";
            }
            return result;
        }

        [HttpDelete("{brandId}")]
        public async Task<ResultVM<bool>> DeleteBrandAsync(long brandId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBrandGrain>(0);
                result.Data = await grain.DeleteBrandAsync(brandId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除品牌失败: BrandId={BrandId}", brandId);
                result.ErrorMessage = "删除品牌失败";
            }
            return result;
        }

        [HttpPost("apply")]
        public async Task<ResultVM<ShopBrandApplyState>> ApplyBrandAsync([FromBody] ShopBrandApplyState apply)
        {
            var result = new ResultVM<ShopBrandApplyState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBrandGrain>(0);
                result.Data = await grain.ApplyBrandAsync(apply);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "申请品牌失败");
                result.ErrorMessage = "申请品牌失败";
            }
            return result;
        }

        [HttpPost("apply/{applyId}/audit")]
        public async Task<ResultVM<ShopBrandApplyState>> AuditBrandApplyAsync(long applyId, [FromBody] AuditBrandApplyRequest request)
        {
            var result = new ResultVM<ShopBrandApplyState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBrandGrain>(0);
                result.Data = await grain.AuditBrandApplyAsync(applyId, request.Approved, request.Remark ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审核品牌申请失败: ApplyId={ApplyId}", applyId);
                result.ErrorMessage = "审核品牌申请失败";
            }
            return result;
        }

        [HttpGet("shop/{shopId}/applies")]
        public async Task<ResultVM<List<ShopBrandApplyState>>> GetShopBrandAppliesAsync(long shopId)
        {
            var result = new ResultVM<List<ShopBrandApplyState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBrandGrain>(0);
                result.Data = await grain.GetShopBrandAppliesAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店铺品牌申请失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取品牌申请失败";
            }
            return result;
        }
    }

    public class AuditBrandApplyRequest
    {
        public bool Approved { get; set; }
        public string Remark { get; set; } = "";
    }
}
