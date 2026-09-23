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
    public class FlowerSettlementController : OrleansControllerBase
    {
        private readonly ILogger<FlowerSettlementController> _logger;

        public FlowerSettlementController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerSettlementController> logger,
            IClusterClient clusterClient)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
        }

        [HttpGet("{settlementId}")]
        public async Task<ResultVM<SettlementDto>> GetSettlementAsync(long settlementId)
        {
            var result = new ResultVM<SettlementDto>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettlementGrain>(settlementId);
                var state = await grain.GetSettlementAsync();
                result.Data = state is null ? null : FlowerDtoMapper.ToDto(state);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取结算单失败: SettlementId={SettlementId}", settlementId);
                result.ErrorMessage = "获取结算单失败";
            }
            return result;
        }

        [HttpPost("create")]
        public async Task<ResultVM<SettlementState>> CreateSettlementAsync([FromBody] CreateSettlementRequest request)
        {
            var result = new ResultVM<SettlementState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettlementGrain>(request.MerchantId);
                result.Data = await grain.CreateSettlementAsync(request.PeriodStart, request.PeriodEnd);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建结算单失败: MerchantId={MerchantId}", request.MerchantId);
                result.ErrorMessage = "创建结算单失败";
            }
            return result;
        }

        [HttpPost("{settlementId}/complete")]
        public async Task<ResultVM<bool>> CompleteSettlementAsync(long settlementId)
        {
            var result = new ResultVM<bool>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettlementGrain>(settlementId);
                result.Data = await grain.CompleteSettlementAsync();
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成结算失败: SettlementId={SettlementId}", settlementId);
                result.ErrorMessage = "完成结算失败";
            }
            return result;
        }

        [HttpGet("{merchantId}/account")]
        public async Task<ResultVM<SettlementAccountDto>> GetSettlementAccountAsync(long merchantId)
        {
            var result = new ResultVM<SettlementAccountDto>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettlementGrain>(merchantId);
                var state = await grain.GetSettlementAccountAsync(merchantId);
                result.Data = state is null ? null : FlowerDtoMapper.ToDto(state);
                result.IsSuccess = state != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取结算账户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "获取结算账户失败";
            }
            return result;
        }

        [HttpPut("{merchantId}/account")]
        public async Task<ResultVM<SettlementAccountState>> SaveSettlementAccountAsync(long merchantId, [FromBody] SettlementAccountState account)
        {
            var result = new ResultVM<SettlementAccountState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettlementGrain>(merchantId);
                result.Data = await grain.SaveSettlementAccountAsync(merchantId, account);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存结算账户失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "保存结算账户失败";
            }
            return result;
        }

        [HttpGet("{merchantId}/bills")]
        public async Task<ResultVM<List<SettlementDto>>> GetSettlementBillsAsync(long merchantId, [FromQuery] int pageNo = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<SettlementDto>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettlementGrain>(merchantId);
                var skip = (pageNo - 1) * pageSize;
                var bills = await grain.GetSettlementBillsAsync(merchantId, skip, pageSize);
                result.Data = bills?.Select(b => FlowerDtoMapper.ToDto(b)).ToList();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取结算账单失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "获取结算账单失败";
            }
            return result;
        }

        [HttpPost("withdraw")]
        public async Task<ResultVM<ShopWithdrawState>> RequestWithdrawAsync([FromBody] RequestWithdrawRequest request)
        {
            var result = new ResultVM<ShopWithdrawState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IShopBillingGrain>(request.ShopId);
                var withdraw = new ShopWithdrawState
                {
                    ShopId = request.ShopId,
                    Amount = request.Amount,
                    BankName = request.BankName,
                    AccountNo = request.AccountNo,
                    AccountName = request.AccountName
                };
                result.Data = await grain.RequestWithdrawAsync(withdraw);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "申请提现失败: ShopId={ShopId}", request.ShopId);
                result.ErrorMessage = "申请提现失败";
            }
            return result;
        }

        [HttpGet("{settlementBillId}/details")]
        public async Task<ResultVM<List<SettlementDetailDto>>> GetSettlementDetailsAsync(long settlementBillId)
        {
            var result = new ResultVM<List<SettlementDetailDto>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettlementGrain>(settlementBillId);
                var details = await grain.GetSettlementDetailsAsync(settlementBillId);
                result.Data = details?.Select(d => FlowerDtoMapper.ToDto(d)).ToList();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取结算详情失败: SettlementBillId={SettlementBillId}", settlementBillId);
                result.ErrorMessage = "获取结算详情失败";
            }
            return result;
        }

        [HttpGet("account/{merchantId}/summary")]
        public async Task<ResultVM<SettlementAccountSummaryDto>> GetAccountSummaryAsync(long merchantId)
        {
            var result = new ResultVM<SettlementAccountSummaryDto>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<ISettlementGrain>(merchantId);
                var state = await grain.GetAccountSummaryAsync(merchantId);
                result.Data = state is null ? null : FlowerDtoMapper.ToDto(state);
                result.IsSuccess = state != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取结算账户汇总失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "获取结算账户汇总失败";
            }
            return result;
        }
    }

    public class CreateSettlementRequest
    {
        public long MerchantId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }

    public class RequestWithdrawRequest
    {
        public long ShopId { get; set; }
        public decimal Amount { get; set; }
        public string BankName { get; set; }
        public string AccountNo { get; set; }
        public string AccountName { get; set; }
    }
}
