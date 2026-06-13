using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Share.VMs;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Horizon.WebApi.Configs;
using Horizon.WebApi.Identity.Users;
using Orleans;
using Orleans.Configuration;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Basic)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerPaymentController : OrleansControllerBase
    {
        private readonly ILogger<FlowerPaymentController> _logger;
        private readonly IPassportCurrentUser _passportCurrentUser;

        private static readonly ConcurrentDictionary<Guid, DateTime> _userRateLimit = new();
        private static readonly TimeSpan _rateLimitWindow = TimeSpan.FromSeconds(3);

        public FlowerPaymentController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerPaymentController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrentUser)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrentUser = passportCurrentUser;
        }

        [HttpPost("initiate")]
        public async Task<ResultVM<PaymentState>> InitiatePaymentAsync([FromBody] InitiatePaymentRequest request)
        {
            var result = new ResultVM<PaymentState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (IsRateLimited(userId))
                {
                    _logger.LogWarning("支付请求过于频繁: UserId={UserId}, OrderId={OrderId}", userId, request.OrderId);
                    result.ErrorMessage = "请求过于频繁，请稍后再试";
                    return result;
                }

                if (request.OrderId <= 0)
                {
                    result.ErrorMessage = "无效的订单ID";
                    return result;
                }

                if (!Enum.IsDefined(typeof(PaymentChannel), request.Channel))
                {
                    result.ErrorMessage = "不支持的支付渠道";
                    return result;
                }

                if (request.Timestamp > 0)
                {
                    var requestTime = DateTimeOffset.FromUnixTimeMilliseconds(request.Timestamp);
                    var timeDiff = Math.Abs((DateTimeOffset.Now - requestTime).TotalMinutes);
                    if (timeDiff > 5)
                    {
                        _logger.LogWarning("请求时间戳过期: UserId={UserId}, OrderId={OrderId}, TimeDiff={TimeDiff}min", userId, request.OrderId, timeDiff);
                        result.ErrorMessage = "请求已过期，请重新发起";
                        return result;
                    }
                }

                var client = await OrleansConnectClient();
                var orderGrain = client.GetGrain<IOrderGrain>(request.OrderId);
                var order = await orderGrain.GetOrderAsync();
                if (order == null)
                {
                    result.ErrorMessage = "订单不存在";
                    return result;
                }

                if (order.BuyerId != userId)
                {
                    _logger.LogWarning("订单归属权校验失败: OrderId={OrderId}, BuyerId={BuyerId}, UserId={UserId}", request.OrderId, order.BuyerId, userId);
                    result.ErrorMessage = "无权操作此订单";
                    return result;
                }

                if (order.Status != OrderStatus.Pending)
                {
                    result.ErrorMessage = "订单状态不允许支付";
                    return result;
                }

                if (order.Items != null && order.Items.Count > 0)
                {
                    foreach (var item in order.Items)
                    {
                        var productGrain = client.GetGrain<IProductGrain>(item.ProductId);
                        var product = await productGrain.GetProductAsync();
                        if (product == null || !product.IsActive || product.Stock < item.Quantity)
                        {
                            _logger.LogWarning("商品库存不足: ProductId={ProductId}, Required={Required}, Available={Available}",
                                item.ProductId, item.Quantity, product?.Stock ?? 0);
                            result.ErrorMessage = "商品库存不足";
                            return result;
                        }
                    }
                }

                var serverAmount = order.OrderTotalAmount;
                if (serverAmount <= 0 || serverAmount > 100000000m)
                {
                    result.ErrorMessage = "订单金额异常";
                    return result;
                }

                if (request.Amount > 0 && Math.Abs(request.Amount - serverAmount) > 0.01m)
                {
                    _logger.LogWarning("客户端金额与服务端金额不匹配: OrderId={OrderId}, ClientAmount={ClientAmount}, ServerAmount={ServerAmount}",
                        request.OrderId, request.Amount, serverAmount);
                }

                var transactionId = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                var grain = client.GetGrain<IPaymentTransactionGrain>(transactionId);
                var paymentState = await grain.CreatePrepayAsync(request.OrderId, request.Channel, serverAmount, userId, request.IdempotencyKey ?? "", request.Scene);

                result.Data = paymentState;
                result.IsSuccess = paymentState != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发起支付失败: OrderId={OrderId}", request.OrderId);
                result.ErrorMessage = "发起支付失败";
            }

            return result;
        }

        [HttpPost("callback/alipay")]
        [AllowAnonymous]
        public async Task<IActionResult> AlipayCallbackAsync()
        {
            try
            {
                var dict = new Dictionary<string, string>();
                foreach (var kv in Request.Form)
                    dict[kv.Key] = kv.Value;
                foreach (var kv in Request.Query)
                    dict[kv.Key] = kv.Value;

                var alipayGrainKey = dict.TryGetValue("out_trade_no", out var outTradeNo) && !string.IsNullOrEmpty(outTradeNo)
                    ? outTradeNo
                    : dict.Values.Aggregate(0, (h, v) => h ^ v.GetHashCode()).ToString("x");

                var client = await OrleansConnectClient();
                var processorGrain = client.GetGrain<IPaymentCallbackProcessorGrain>(alipayGrainKey);
                var success = await processorGrain.ProcessAlipayCallbackAsync(dict);
                return success ? Content("success") : Content("failure");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付宝异步通知处理失败");
                return Content("failure");
            }
        }

        [HttpPost("callback/wechat")]
        [AllowAnonymous]
        public async Task<IActionResult> WechatCallbackAsync()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();

                var wechatGrainKey = ExtractWechatOutTradeNo(body) ?? body.GetHashCode().ToString("x");

                var client = await OrleansConnectClient();
                var processorGrain = client.GetGrain<IPaymentCallbackProcessorGrain>(wechatGrainKey);
                var success = await processorGrain.ProcessWechatCallbackAsync(body);

                var reply = Newtonsoft.Json.JsonConvert.SerializeObject(new { code = success ? "SUCCESS" : "FAIL", message = "" });
                return Content(reply, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信支付异步通知处理失败");
                var reply = Newtonsoft.Json.JsonConvert.SerializeObject(new { code = "FAIL", message = "internal error" });
                return Content(reply, "application/json");
            }
        }

        [HttpGet("{transactionId}/status")]
        public async Task<ResultVM<PaymentState>> QueryPaymentStatusAsync(long transactionId)
        {
            var result = new ResultVM<PaymentState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (transactionId <= 0)
                {
                    result.ErrorMessage = "无效的交易ID";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IPaymentTransactionGrain>(transactionId);
                var state = await grain.GetTransactionAsync();

                if (state != null && state.TransactionId > 0 && state.BuyerId != userId)
                {
                    var orderGrain = client.GetGrain<IOrderGrain>(state.OrderId);
                    var order = await orderGrain.GetOrderAsync();
                    if (order == null || order.BuyerId != userId)
                    {
                        _logger.LogWarning("交易查询归属权校验失败: TransactionId={TxId}, UserId={UserId}", transactionId, userId);
                        result.ErrorMessage = "无权查询此交易";
                        return result;
                    }
                }

                result.Data = state;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询支付状态失败: TransactionId={TransactionId}", transactionId);
                result.ErrorMessage = "查询支付状态失败";
            }
            return result;
        }

        [HttpPost("{transactionId}/refund")]
        public async Task<ResultVM<bool>> RefundAsync(long transactionId, [FromBody] RefundRequest request)
        {
            var result = new ResultVM<bool>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (transactionId <= 0)
                {
                    result.ErrorMessage = "无效的交易ID";
                    return result;
                }

                if (request.RefundAmount <= 0)
                {
                    result.ErrorMessage = "退款金额必须大于0";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IPaymentTransactionGrain>(transactionId);
                var state = await grain.GetTransactionAsync();

                if (state == null || state.TransactionId <= 0)
                {
                    result.ErrorMessage = "交易不存在";
                    return result;
                }

                if (state.BuyerId != userId)
                {
                    var orderGrain = client.GetGrain<IOrderGrain>(state.OrderId);
                    var order = await orderGrain.GetOrderAsync();
                    if (order == null)
                    {
                        _logger.LogWarning("退款归属权校验失败(仅商户可退款): TransactionId={TxId}, UserId={UserId}", transactionId, userId);
                        result.ErrorMessage = "仅商户可发起退款";
                        return result;
                    }

                    var merchantGrain = client.GetGrain<IMerchantGrain>(order.MerchantId);
                    var merchant = await merchantGrain.GetMerchantAsync();
                    if (merchant == null || merchant.UserId != userId)
                    {
                        _logger.LogWarning("退款归属权校验失败(仅商户可退款): TransactionId={TxId}, UserId={UserId}", transactionId, userId);
                        result.ErrorMessage = "仅商户可发起退款";
                        return result;
                    }
                }

                if (request.RefundAmount + state.TotalRefundedAmount > state.Amount)
                {
                    _logger.LogWarning("累计退款金额超过支付金额: TransactionId={TransactionId}, RefundAmount={RefundAmount}, TotalRefunded={TotalRefunded}, PaidAmount={PaidAmount}",
                        transactionId, request.RefundAmount, state.TotalRefundedAmount, state.Amount);
                    result.ErrorMessage = "累计退款金额不能超过支付金额";
                    return result;
                }

                result.Data = await grain.RefundAsync(request.RefundAmount, request.Reason ?? "");
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "退款失败: TransactionId={TransactionId}", transactionId);
                result.ErrorMessage = "退款失败";
            }
            return result;
        }

        private Guid GetAuthenticatedUserId()
        {
            if (!_passportCurrentUser.IsAuthenticated)
                return Guid.Empty;

            var passportId = _passportCurrentUser.PassportId;
            if (string.IsNullOrEmpty(passportId))
                return Guid.Empty;

            if (Guid.TryParse(passportId, out var userId))
                return userId;

            return Guid.Empty;
        }

        private bool IsRateLimited(Guid userId)
        {
            var now = DateTime.Now;
            if (_userRateLimit.TryGetValue(userId, out var lastRequest))
            {
                if (now - lastRequest < _rateLimitWindow)
                    return true;
            }
            _userRateLimit[userId] = now;
            return false;
        }

        private static string ExtractWechatOutTradeNo(string xmlBody)
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xmlBody);
                var node = doc.SelectSingleNode("//out_trade_no");
                return node?.InnerText;
            }
            catch
            {
                return null;
            }
        }

    }

    public class InitiatePaymentRequest
    {
        public long OrderId { get; set; }
        public PaymentChannel Channel { get; set; }
        public decimal Amount { get; set; }
        public string IdempotencyKey { get; set; }
        public long Timestamp { get; set; }
        public PaymentScene Scene { get; set; } = PaymentScene.Native;
    }

    public class RefundRequest
    {
        public decimal RefundAmount { get; set; }
        public string Reason { get; set; }
    }
}
