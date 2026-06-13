using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Options;
using Orleans;
using Orleans.Configuration;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

namespace Horizon.WebApi.Controllers
{
    /// <summary>
    /// 花卉预警
    /// </summary>
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerAlertController : OrleansControllerBase
    {
        private readonly ILogger<FlowerAlertController> _logger;

        public FlowerAlertController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<FlowerAlertController> logger,
                                IClusterClient clusterClient)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        /// <summary>
        /// 获取用户预警规则列表
        /// </summary>
        [HttpGet("alerts/rules")]
        public async Task<ResultVM<List<FlowerAlertRuleInfo>>> GetAlertRules([FromQuery] Guid userId)
        {
            var result = new ResultVM<List<FlowerAlertRuleInfo>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerAlertManagementGrain>(userId);
                result.Data = await grain.GetAlertRulesAsync();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户预警规则列表失败: UserId={UserId}", userId);
                result.ErrorMessage = "获取预警规则列表失败";
            }
            return result;
        }

        /// <summary>
        /// 创建预警规则
        /// </summary>
        [HttpPost("alerts/rules")]
        public async Task<ResultVM<FlowerAlertRuleInfo>> CreateAlertRule([FromBody] FlowerAlertRuleInfo rule)
        {
            var result = new ResultVM<FlowerAlertRuleInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerAlertManagementGrain>(rule.UserId);
                result.Data = await grain.CreateAlertRuleAsync(rule);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建预警规则失败: UserId={UserId}", rule.UserId);
                result.ErrorMessage = "创建预警规则失败";
            }
            return result;
        }

        /// <summary>
        /// 更新预警规则
        /// </summary>
        [HttpPut("alerts/rules/{id}")]
        public async Task<ResultVM<FlowerAlertRuleInfo>> UpdateAlertRule([FromQuery] Guid userId, long id, [FromBody] UpdateAlertRuleRequest request)
        {
            var result = new ResultVM<FlowerAlertRuleInfo>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerAlertManagementGrain>(userId);
                result.Data = await grain.UpdateAlertRuleAsync(id, request.ConditionType, request.ThresholdValue, request.IsEnabled);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新预警规则失败: UserId={UserId}, RuleId={RuleId}", userId, id);
                result.ErrorMessage = "更新预警规则失败";
            }
            return result;
        }

        /// <summary>
        /// 删除预警规则
        /// </summary>
        [HttpDelete("alerts/rules/{id}")]
        public async Task<ResultVM<bool>> DeleteAlertRule([FromQuery] Guid userId, long id)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerAlertManagementGrain>(userId);
                result.Data = await grain.DeleteAlertRuleAsync(id);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除预警规则失败: UserId={UserId}, RuleId={RuleId}", userId, id);
                result.ErrorMessage = "删除预警规则失败";
            }
            return result;
        }

        /// <summary>
        /// 获取用户预警日志
        /// </summary>
        [HttpGet("alerts/logs")]
        public async Task<ResultVM<List<FlowerAlertLogInfo>>> GetAlertLogs([FromQuery] Guid userId, [FromQuery] int skip = 0, [FromQuery] int take = 20)
        {
            var result = new ResultVM<List<FlowerAlertLogInfo>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerAlertManagementGrain>(userId);
                result.Data = await grain.GetAlertLogsAsync(skip, take);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户预警日志失败: UserId={UserId}", userId);
                result.ErrorMessage = "获取预警日志失败";
            }
            return result;
        }
    }

    public class UpdateAlertRuleRequest
    {
        public int ConditionType { get; set; }
        public decimal ThresholdValue { get; set; }
        public bool IsEnabled { get; set; }
    }
}
