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
    public class FlowerBusinessCategoryController : OrleansControllerBase
    {
        private readonly ILogger<FlowerBusinessCategoryController> _logger;

        public FlowerBusinessCategoryController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerBusinessCategoryController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("{id}")]
        public async Task<ResultVM<BusinessCategoryState>> GetBusinessCategoryAsync(long id)
        {
            var result = new ResultVM<BusinessCategoryState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBusinessCategoryGrain>(0);
                result.Data = await grain.GetBusinessCategoryAsync(id);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取经营类目失败: Id={Id}", id);
                result.ErrorMessage = "获取经营类目失败";
            }
            return result;
        }

        [HttpGet("shop/{shopId}")]
        public async Task<ResultVM<List<BusinessCategoryState>>> GetShopBusinessCategoriesAsync(long shopId)
        {
            var result = new ResultVM<List<BusinessCategoryState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBusinessCategoryGrain>(0);
                result.Data = await grain.GetShopBusinessCategoriesAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店铺经营类目失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取经营类目失败";
            }
            return result;
        }

        [HttpPost("apply")]
        public async Task<ResultVM<BusinessCategoryState>> ApplyBusinessCategoryAsync([FromBody] BusinessCategoryState category)
        {
            var result = new ResultVM<BusinessCategoryState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBusinessCategoryGrain>(0);
                result.Data = await grain.ApplyBusinessCategoryAsync(category);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "申请经营类目失败");
                result.ErrorMessage = "申请经营类目失败";
            }
            return result;
        }

        [HttpPost("{id}/audit")]
        public async Task<ResultVM<BusinessCategoryState>> AuditBusinessCategoryAsync(long id, [FromBody] AuditBusinessCategoryRequest request)
        {
            var result = new ResultVM<BusinessCategoryState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IBusinessCategoryGrain>(0);
                result.Data = await grain.AuditBusinessCategoryAsync(id, request.Approved, request.Remark ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审核经营类目失败: Id={Id}", id);
                result.ErrorMessage = "审核经营类目失败";
            }
            return result;
        }
    }

    public class AuditBusinessCategoryRequest
    {
        public bool Approved { get; set; }
        public string Remark { get; set; } = "";
    }
}
