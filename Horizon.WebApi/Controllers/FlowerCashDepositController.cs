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
    public class FlowerCashDepositController : OrleansControllerBase
    {
        private readonly ILogger<FlowerCashDepositController> _logger;

        public FlowerCashDepositController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerCashDepositController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("{depositId}")]
        public async Task<ResultVM<CashDepositState>> GetCashDepositAsync(long depositId)
        {
            var result = new ResultVM<CashDepositState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICashDepositGrain>(0);
                result.Data = await grain.GetCashDepositAsync(depositId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取保证金失败: DepositId={DepositId}", depositId);
                result.ErrorMessage = "获取保证金失败";
            }
            return result;
        }

        [HttpGet("shop/{shopId}")]
        public async Task<ResultVM<List<CashDepositState>>> GetShopCashDepositsAsync(long shopId)
        {
            var result = new ResultVM<List<CashDepositState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICashDepositGrain>(0);
                result.Data = await grain.GetShopCashDepositsAsync(shopId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店铺保证金失败: ShopId={ShopId}", shopId);
                result.ErrorMessage = "获取保证金列表失败";
            }
            return result;
        }

        [HttpPost("pay")]
        public async Task<ResultVM<CashDepositState>> PayCashDepositAsync([FromBody] CashDepositState deposit)
        {
            var result = new ResultVM<CashDepositState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICashDepositGrain>(0);
                result.Data = await grain.PayCashDepositAsync(deposit);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "缴纳保证金失败");
                result.ErrorMessage = "缴纳保证金失败";
            }
            return result;
        }

        [HttpPost("{depositId}/deduct")]
        public async Task<ResultVM<CashDepositState>> DeductCashDepositAsync(long depositId, [FromBody] DeductCashDepositRequest request)
        {
            var result = new ResultVM<CashDepositState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ICashDepositGrain>(0);
                result.Data = await grain.DeductCashDepositAsync(depositId, request.Amount);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扣罚保证金失败: DepositId={DepositId}", depositId);
                result.ErrorMessage = "扣罚保证金失败";
            }
            return result;
        }
    }

    public class DeductCashDepositRequest
    {
        public decimal Amount { get; set; }
    }
}
