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
    public class FlowerShopBillingController : OrleansControllerBase
    {
        private readonly ILogger<FlowerShopBillingController> _logger;

        public FlowerShopBillingController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerShopBillingController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("pending/{shopId}")]
        public async Task<ResultVM<List<PendingSettlementState>>> GetPendingSettlementsAsync(long shopId)
        {
            var result = new ResultVM<List<PendingSettlementState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopBillingGrain>(shopId);
                result.Data = await grain.GetPendingSettlementsAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取待结算列表失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取待结算列表失败";
            }
            return result;
        }

        [HttpPost("settle/{shopId}")]
        public async Task<ResultVM<SettlementState>> SettleAsync(long shopId, [FromBody] SettleRequest request)
        {
            var result = new ResultVM<SettlementState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopBillingGrain>(shopId);
                result.Data = await grain.SettleAsync(shopId, request.PeriodStart, request.PeriodEnd);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行结算失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "执行结算失败";
            }
            return result;
        }

        [HttpPost("withdraw")]
        public async Task<ResultVM<ShopWithdrawState>> RequestWithdrawAsync([FromBody] ShopWithdrawState withdraw)
        {
            var result = new ResultVM<ShopWithdrawState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopBillingGrain>(withdraw.ShopId);
                result.Data = await grain.RequestWithdrawAsync(withdraw);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "申请提现失败");
                result.ErrorMessage = "申请提现失败";
            }
            return result;
        }

        [HttpPost("withdraw/{withdrawId}/audit")]
        public async Task<ResultVM<ShopWithdrawState>> AuditWithdrawAsync(long withdrawId, [FromBody] AuditWithdrawRequest request)
        {
            var result = new ResultVM<ShopWithdrawState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopBillingGrain>(0);
                result.Data = await grain.AuditWithdrawAsync(withdrawId, request.Approved, request.Remark ?? "");
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "审核提现失败: WithdrawId={WithdrawId}", withdrawId);
                result.ErrorMessage = "审核提现失败";
            }
            return result;
        }

        [HttpGet("account-items/{shopId}")]
        public async Task<ResultVM<List<ShopAccountItemState>>> GetShopAccountItemsAsync(long shopId)
        {
            var result = new ResultVM<List<ShopAccountItemState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopBillingGrain>(shopId);
                result.Data = await grain.GetShopAccountItemsAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取资金流水失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取资金流水失败";
            }
            return result;
        }
    }

    public class SettleRequest
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }

    public class AuditWithdrawRequest
    {
        public bool Approved { get; set; }
        public string Remark { get; set; } = "";
    }
}
