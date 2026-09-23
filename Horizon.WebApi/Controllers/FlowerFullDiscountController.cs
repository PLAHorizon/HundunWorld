using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Share.Dtos.Flower;
using Horizon.WebApi.Mapping;
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
    public class FlowerFullDiscountController : OrleansControllerBase
    {
        private readonly ILogger<FlowerFullDiscountController> _logger;

        public FlowerFullDiscountController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerFullDiscountController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("{ruleId}")]
        public async Task<ResultVM<FullDiscountRuleDto>> GetRuleAsync(long ruleId)
        {
            var result = new ResultVM<FullDiscountRuleDto>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFullDiscountGrain>(0);
                var state = await grain.GetRuleAsync(ruleId);
                result.Data = state is null ? null : FlowerDtoMapper.ToDto(state);
                result.IsSuccess = state != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取满减规则失败: RuleId={RuleId}", ruleId);
                result.ErrorMessage = "获取满减规则失败";
            }
            return result;
        }

        [HttpGet("shop/{shopId}")]
        public async Task<ResultVM<List<FullDiscountRuleDto>>> GetShopRulesAsync(long shopId)
        {
            var result = new ResultVM<List<FullDiscountRuleDto>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFullDiscountGrain>(0);
                var rules = await grain.GetShopRulesAsync(shopId);
                result.Data = rules?.Select(r => FlowerDtoMapper.ToDto(r)).ToList();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店铺满减规则失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取满减规则失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<FullDiscountRuleState>> AddRuleAsync([FromBody] FullDiscountRuleState rule)
        {
            var result = new ResultVM<FullDiscountRuleState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFullDiscountGrain>(0);
                result.Data = await grain.AddRuleAsync(rule);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建满减规则失败");
                result.ErrorMessage = "创建满减规则失败";
            }
            return result;
        }

        [HttpPut("{ruleId}")]
        public async Task<ResultVM<FullDiscountRuleState>> UpdateRuleAsync(long ruleId, [FromBody] FullDiscountRuleState rule)
        {
            var result = new ResultVM<FullDiscountRuleState>();
            try
            {
                rule.Id = ruleId;
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFullDiscountGrain>(0);
                result.Data = await grain.UpdateRuleAsync(rule);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新满减规则失败: RuleId={RuleId}", ruleId);
                result.ErrorMessage = "更新满减规则失败";
            }
            return result;
        }

        [HttpDelete("{ruleId}")]
        public async Task<ResultVM<bool>> DeleteRuleAsync(long ruleId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFullDiscountGrain>(0);
                result.Data = await grain.DeleteRuleAsync(ruleId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除满减规则失败: RuleId={RuleId}", ruleId);
                result.ErrorMessage = "删除满减规则失败";
            }
            return result;
        }

        [HttpPost("calculate")]
        public async Task<ResultVM<decimal>> CalculateDiscountAsync([FromBody] CalculateDiscountRequest request)
        {
            var result = new ResultVM<decimal>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFullDiscountGrain>(0);
                result.Data = await grain.CalculateDiscountAsync(request.ShopId, request.OrderAmount);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "计算满减失败");
                result.ErrorMessage = "计算满减失败";
            }
            return result;
        }
    }

    public class CalculateDiscountRequest
    {
        public long ShopId { get; set; }
        public decimal OrderAmount { get; set; }
    }
}
