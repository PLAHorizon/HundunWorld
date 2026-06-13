using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Share.VMs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers
{
    [Route("FlowerOrderRefund")]
    [ApiController]
    [Authorize]
    public class FlowerOrderRefundController : OrleansControllerBase
    {
        private readonly ILogger<FlowerOrderRefundController> _logger;
        private readonly IClusterClient _clusterClient;

        public FlowerOrderRefundController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<FlowerOrderRefundController> logger,
                                IClusterClient clusterClient)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _clusterClient = clusterClient;
            _logger = logger;
        }

        [HttpGet("{refundId}")]
        public async Task<ResultVM<OrderRefundState>> GetRefund(long refundId)
        {
            var result = new ResultVM<OrderRefundState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderRefundGrain>(0);
                result.Data = await grain.GetRefundAsync(refundId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取退款详情失败: {RefundId}", refundId);
                result.ErrorMessage = "获取退款详情失败";
            }
            return result;
        }

        [HttpPost("request")]
        public async Task<ResultVM<OrderRefundState>> RequestRefund([FromBody] OrderRefundState refund)
        {
            var result = new ResultVM<OrderRefundState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderRefundGrain>(0);
                result.Data = await grain.RequestRefundAsync(refund);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "申请退款失败");
                result.ErrorMessage = "申请退款失败";
            }
            return result;
        }

        [HttpPost("{refundId}/seller-audit")]
        public async Task<ResultVM<OrderRefundState>> SellerAuditRefund(long refundId, [FromBody] SellerAuditRequest request)
        {
            var result = new ResultVM<OrderRefundState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderRefundGrain>(0);
                result.Data = await grain.SellerAuditRefundAsync(refundId, request.Approved, request.Remark);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "商户审核退款失败: {RefundId}", refundId);
                result.ErrorMessage = "审核退款失败";
            }
            return result;
        }

        [HttpGet("merchant/{merchantId}")]
        public async Task<ResultVM<List<OrderRefundState>>> GetMerchantRefunds(long merchantId, [FromQuery] int? status)
        {
            var result = new ResultVM<List<OrderRefundState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderRefundGrain>(0);
                result.Data = await grain.GetMerchantRefundsAsync(merchantId, status);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商户退款列表失败: {MerchantId}", merchantId);
                result.ErrorMessage = "获取退款列表失败";
            }
            return result;
        }

        [HttpGet("buyer/{buyerId}")]
        public async Task<ResultVM<List<OrderRefundState>>> GetBuyerRefunds(Guid buyerId)
        {
            var result = new ResultVM<List<OrderRefundState>>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderRefundGrain>(0);
                result.Data = await grain.GetBuyerRefundsAsync(buyerId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取买家退款列表失败: {BuyerId}", buyerId);
                result.ErrorMessage = "获取退款列表失败";
            }
            return result;
        }

        [HttpPost("{refundId}/return-shipment")]
        public async Task<ResultVM<OrderRefundState>> SubmitReturnShipment(long refundId, [FromBody] ReturnShipmentRequest request)
        {
            var result = new ResultVM<OrderRefundState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderRefundGrain>(0);
                result.Data = await grain.SubmitReturnShipmentAsync(refundId, request.ExpressCompanyName, request.ShipOrderNumber);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交退货物流失败: {RefundId}", refundId);
                result.ErrorMessage = "提交退货物流失败";
            }
            return result;
        }

        [HttpPost("{refundId}/confirm-received")]
        public async Task<ResultVM<OrderRefundState>> ConfirmReturnReceived(long refundId)
        {
            var result = new ResultVM<OrderRefundState>();
            try
            {
                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderRefundGrain>(0);
                result.Data = await grain.ConfirmReturnReceivedAsync(refundId);
                result.IsSuccess = result.Data != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确认退货收货失败: {RefundId}", refundId);
                result.ErrorMessage = "确认退货收货失败";
            }
            return result;
        }
    }

    public class SellerAuditRequest
    {
        public bool Approved { get; set; }
        public string Remark { get; set; }
    }

    public class ReturnShipmentRequest
    {
        public string ExpressCompanyName { get; set; } = "";
        public string ShipOrderNumber { get; set; } = "";
    }
}
