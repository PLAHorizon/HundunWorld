using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public class FlowerOrderController : OrleansControllerBase
    {
        private readonly ILogger<FlowerOrderController> _logger;
        private readonly IPassportCurrentUser _passportCurrentUser;

        private static readonly ConcurrentDictionary<Guid, DateTime> _orderCreateRateLimit = new();
        private static readonly ConcurrentDictionary<Guid, DateTime> _activeOrderCreations = new();
        private static readonly TimeSpan _rateLimitWindow = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _activeCreationExpiry = TimeSpan.FromSeconds(30);

        public FlowerOrderController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerOrderController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrentUser)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrentUser = passportCurrentUser;
        }

        [HttpGet("{orderId}")]
        public async Task<ResultVM<OrderState>> GetOrderAsync(long orderId)
        {
            var result = new ResultVM<OrderState>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (orderId <= 0)
                {
                    result.ErrorMessage = "无效的订单ID";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderGrain>(orderId);
                var order = await grain.GetOrderAsync();

                if (order != null && order.OrderId > 0 && order.BuyerId != userId)
                {
                    var merchantGrain = client.GetGrain<IMerchantGrain>(order.MerchantId);
                    var merchant = await merchantGrain.GetMerchantAsync();
                    if (merchant == null || merchant.UserId != userId)
                    {
                        _logger.LogWarning("订单查询归属权校验失败: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                        result.ErrorMessage = "无权查看此订单";
                        return result;
                    }
                }

                result.Data = order;
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单详情失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "获取订单详情失败";
            }
            return result;
        }

        [HttpPost]
        public async Task<ResultVM<OrderState>> CreateOrderAsync([FromBody] OrderState order)
        {
            var result = new ResultVM<OrderState>();
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
                    _logger.LogWarning("创建订单请求过于频繁: UserId={UserId}", userId);
                    result.ErrorMessage = "请求过于频繁，请稍后再试";
                    return result;
                }

                if (order == null)
                {
                    result.ErrorMessage = "订单数据不能为空";
                    return result;
                }

                if (order.Items == null || order.Items.Count == 0)
                {
                    result.ErrorMessage = "订单商品不能为空";
                    return result;
                }

                if (order.Items.Count > 100)
                {
                    result.ErrorMessage = "单笔订单商品数量不能超过100";
                    return result;
                }

                order.BuyerId = userId;

                if (order.MerchantId <= 0)
                {
                    result.ErrorMessage = "商户ID无效";
                    return result;
                }

                foreach (var item in order.Items)
                {
                    if (item.ProductId <= 0)
                    {
                        result.ErrorMessage = "商品ID无效";
                        return result;
                    }
                    if (item.Quantity <= 0)
                    {
                        result.ErrorMessage = "商品数量必须大于0";
                        return result;
                    }
                    if (item.Quantity > 9999)
                    {
                        result.ErrorMessage = "商品数量超限";
                        return result;
                    }
                }

                if (!_activeOrderCreations.TryAdd(userId, DateTime.UtcNow))
                {
                    if (_activeOrderCreations.TryGetValue(userId, out var activeTime) && DateTime.UtcNow - activeTime < _activeCreationExpiry)
                    {
                        _logger.LogWarning("用户订单创建请求正在处理中: UserId={UserId}", userId);
                        result.ErrorMessage = "订单创建请求正在处理中，请勿重复提交";
                        return result;
                    }
                    _activeOrderCreations.TryRemove(userId, out _);
                    _activeOrderCreations.TryAdd(userId, DateTime.UtcNow);
                }

                try
                {
                    var client = await OrleansConnectClient();
                    var orderId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var grain = client.GetGrain<IOrderGrain>(orderId);
                    var created = await grain.CreateOrderAsync(order);
                    if (created != null)
                    {
                        created.OrderId = created.OrderId > 0 ? created.OrderId : orderId;
                        result.Data = created;
                        result.IsSuccess = true;
                    }
                    else
                    {
                        result.ErrorMessage = "创建订单失败";
                    }
                }
                finally
                {
                    _activeOrderCreations.TryRemove(userId, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建订单失败");
                result.ErrorMessage = "创建订单失败";
            }

            CleanupExpiredEntries();
            return result;
        }

        [HttpPost("{orderId}/cancel")]
        public async Task<ResultVM<bool>> CancelOrderAsync(long orderId, [FromQuery] string? reason = null)
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

                if (orderId <= 0)
                {
                    result.ErrorMessage = "无效的订单ID";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderGrain>(orderId);
                var order = await grain.GetOrderAsync();

                if (order != null && order.OrderId > 0 && order.BuyerId != userId)
                {
                    var merchantGrain = client.GetGrain<IMerchantGrain>(order.MerchantId);
                    var merchant = await merchantGrain.GetMerchantAsync();
                    if (merchant == null || merchant.UserId != userId)
                    {
                        _logger.LogWarning("取消订单归属权校验失败: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                        result.ErrorMessage = "无权操作此订单";
                        return result;
                    }
                }

                result.Data = await grain.CancelOrderAsync(reason ?? "");
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订单失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "取消订单失败";
            }
            return result;
        }

        [HttpPost("{orderId}/confirm-delivery")]
        public async Task<ResultVM<bool>> ConfirmDeliveryAsync(long orderId)
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

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderGrain>(orderId);
                var order = await grain.GetOrderAsync();

                if (order != null && order.OrderId > 0 && order.BuyerId != userId)
                {
                    _logger.LogWarning("确认收货归属权校验失败: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                    result.ErrorMessage = "无权操作此订单";
                    return result;
                }

                result.Data = await grain.DeliverOrderAsync();
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确认收货失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "确认收货失败";
            }
            return result;
        }

        [HttpPost("{orderId}/complete")]
        public async Task<ResultVM<bool>> CompleteOrderAsync(long orderId)
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

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderGrain>(orderId);
                var order = await grain.GetOrderAsync();

                if (order != null && order.OrderId > 0 && order.BuyerId != userId)
                {
                    _logger.LogWarning("完成订单归属权校验失败: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                    result.ErrorMessage = "无权操作此订单";
                    return result;
                }

                result.Data = await grain.CompleteOrderAsync();
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成订单失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "完成订单失败";
            }
            return result;
        }

        [HttpGet("my-orders")]
        public async Task<ResultVM<List<OrderState>>> MyOrdersAsync([FromQuery] Guid buyerId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<OrderState>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (buyerId != userId)
                {
                    _logger.LogWarning("查询订单归属权校验失败: BuyerId={BuyerId}, UserId={UserId}", buyerId, userId);
                    result.ErrorMessage = "只能查询自己的订单";
                    return result;
                }

                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                var client = await OrleansConnectClient();
                var queryGrain = client.GetGrain<IFlowerQueryGrain>(0);
                var skip = (page - 1) * pageSize;
                result.Data = await queryGrain.QueryOrdersByBuyerAsync(buyerId, skip, pageSize);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询我的订单失败: BuyerId={BuyerId}", buyerId);
                result.ErrorMessage = "查询我的订单失败";
            }
            return result;
        }

        [HttpGet("merchant-orders")]
        public async Task<ResultVM<List<OrderState>>> MerchantOrdersAsync([FromQuery] long merchantId, [FromQuery] int? status = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<OrderState>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();
                var merchantGrain = client.GetGrain<IMerchantGrain>(merchantId);
                var merchant = await merchantGrain.GetMerchantAsync();
                if (merchant == null || merchant.UserId != userId)
                {
                    _logger.LogWarning("商户订单查询归属权校验失败: MerchantId={MerchantId}, UserId={UserId}", merchantId, userId);
                    result.ErrorMessage = "只能查询自己店铺的订单";
                    return result;
                }

                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 20;

                if (status.HasValue)
                {
                    var orderGrain = client.GetGrain<IOrderGrain>(merchantId);
                    result.Data = await orderGrain.GetMerchantOrdersByStatusAsync(merchantId, status, page, pageSize);
                }
                else
                {
                    var queryGrain = client.GetGrain<IFlowerQueryGrain>(0);
                    var skip = (page - 1) * pageSize;
                    result.Data = await queryGrain.QueryOrdersByMerchantAsync(merchantId, skip, pageSize);
                }
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询商户订单失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "查询商户订单失败";
            }
            return result;
        }

        [HttpPost("{orderId}/ship")]
        public async Task<ResultVM<bool>> ShipOrderAsync(long orderId, [FromBody] ShipOrderRequest request)
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

                if (orderId <= 0)
                {
                    result.ErrorMessage = "无效的订单ID";
                    return result;
                }

                if (request == null || string.IsNullOrWhiteSpace(request.ExpressCompanyName) || string.IsNullOrWhiteSpace(request.ShipOrderNumber))
                {
                    result.ErrorMessage = "物流信息不完整";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderGrain>(orderId);
                var order = await grain.GetOrderAsync();

                if (order != null && order.OrderId > 0)
                {
                    var merchantGrain = client.GetGrain<IMerchantGrain>(order.MerchantId);
                    var merchant = await merchantGrain.GetMerchantAsync();
                    if (merchant == null || merchant.UserId != userId)
                    {
                        _logger.LogWarning("发货归属权校验失败: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                        result.ErrorMessage = "无权操作此订单";
                        return result;
                    }
                }

                var orderState = await grain.ShipOrderAsync(orderId, request.ExpressCompanyName, request.ShipOrderNumber, request.ShipperId);
                result.Data = orderState != null;
                result.IsSuccess = orderState != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发货失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "发货失败";
            }
            return result;
        }

        [HttpPost("{orderId}/refund")]
        public async Task<ResultVM<bool>> RequestRefundAsync(long orderId, [FromQuery] string? reason = null)
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

                if (orderId <= 0)
                {
                    result.ErrorMessage = "无效的订单ID";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderGrain>(orderId);
                var order = await grain.GetOrderAsync();

                if (order == null || order.OrderId <= 0)
                {
                    result.ErrorMessage = "订单不存在";
                    return result;
                }

                if (order.MerchantId > 0)
                {
                    var merchantGrain = client.GetGrain<IMerchantGrain>(order.MerchantId);
                    var merchant = await merchantGrain.GetMerchantAsync();
                    if (merchant == null || merchant.UserId != userId)
                    {
                        _logger.LogWarning("退款归属权校验失败(仅商户可退款): OrderId={OrderId}, UserId={UserId}, MerchantId={MerchantId}", orderId, userId, order.MerchantId);
                        result.ErrorMessage = "仅商户可发起退款";
                        return result;
                    }
                }
                else
                {
                    _logger.LogWarning("退款归属权校验失败(仅商户可退款): OrderId={OrderId}, UserId={UserId}, MerchantId={MerchantId}", orderId, userId, order.MerchantId);
                    result.ErrorMessage = "仅商户可发起退款";
                    return result;
                }

                result.Data = await grain.RequestRefundAsync(reason ?? "");
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "申请退款失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "申请退款失败";
            }
            return result;
        }

        [HttpPost("{orderId}/repurchase")]
        public async Task<ResultVM<bool>> RepurchaseAsync(long orderId)
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

                var client = await OrleansConnectClient();
                var orderGrain = client.GetGrain<IOrderGrain>(orderId);
                var order = await orderGrain.GetOrderAsync();
                if (order == null)
                {
                    result.ErrorMessage = "订单不存在";
                    return result;
                }

                if (order.BuyerId != userId)
                {
                    _logger.LogWarning("复购归属权校验失败: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                    result.ErrorMessage = "无权操作此订单";
                    return result;
                }

                result.Data = await orderGrain.RepurchaseAsync(order.BuyerId, orderId);
                result.IsSuccess = result.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "复购失败: OrderId={OrderId}", orderId);
                result.ErrorMessage = "复购失败";
            }
            return result;
        }

        [HttpPost("batch-ship")]
        public async Task<ResultVM<List<OrderState>>> BatchShipOrdersAsync([FromBody] BatchShipRequest request)
        {
            var result = new ResultVM<List<OrderState>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (request == null || request.OrderIds == null || request.OrderIds.Count == 0)
                {
                    result.ErrorMessage = "批量发货数据无效";
                    return result;
                }

                if (request.OrderIds.Count > 50)
                {
                    result.ErrorMessage = "单次批量发货不能超过50个订单";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(request.ExpressCompanyName))
                {
                    result.ErrorMessage = "物流公司名称不能为空";
                    return result;
                }

                var client = await OrleansConnectClient();
                var verifiedOrderIds = new List<long>();
                foreach (var orderId in request.OrderIds)
                {
                    if (orderId <= 0) continue;
                    var orderGrain = client.GetGrain<IOrderGrain>(orderId);
                    var order = await orderGrain.GetOrderAsync();
                    if (order != null && order.OrderId > 0)
                    {
                        var merchantGrain = client.GetGrain<IMerchantGrain>(order.MerchantId);
                        var merchant = await merchantGrain.GetMerchantAsync();
                        if (merchant != null && merchant.UserId == userId)
                        {
                            verifiedOrderIds.Add(orderId);
                        }
                        else
                        {
                            _logger.LogWarning("批量发货归属权校验失败: OrderId={OrderId}, UserId={UserId}", orderId, userId);
                        }
                    }
                }

                if (verifiedOrderIds.Count == 0)
                {
                    result.ErrorMessage = "没有可发货的订单";
                    return result;
                }

                var verifiedRequest = new BatchShipRequest
                {
                    OrderIds = verifiedOrderIds,
                    ExpressCompanyName = request.ExpressCompanyName,
                    ShipOrderNumberPrefix = request.ShipOrderNumberPrefix,
                    ShipperId = request.ShipperId
                };

                var grain = client.GetGrain<IOrderGrain>(0);
                result.Data = await grain.BatchShipOrdersAsync(verifiedRequest);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量发货失败");
                result.ErrorMessage = "批量发货失败";
            }
            return result;
        }

        [HttpGet("frequent-products/{buyerId}")]
        public async Task<ResultVM<List<RepurchaseState>>> GetFrequentProductsAsync(Guid buyerId)
        {
            var result = new ResultVM<List<RepurchaseState>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                if (buyerId != userId)
                {
                    result.ErrorMessage = "只能查询自己的常购商品";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IOrderGrain>(0);
                result.Data = await grain.GetFrequentProductsAsync(buyerId, 10);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取常购商品失败: BuyerId={BuyerId}", buyerId);
                result.ErrorMessage = "获取常购商品失败";
            }
            return result;
        }

        private Guid GetAuthenticatedUserId()
        {
            Guid.TryParse(_passportCurrentUser.UserId, out Guid id);
            return id;
        }

        private bool IsRateLimited(Guid userId)
        {
            var now = DateTime.UtcNow;
            if (_orderCreateRateLimit.TryGetValue(userId, out var lastRequest))
            {
                if (now - lastRequest < _rateLimitWindow)
                    return true;
            }
            _orderCreateRateLimit[userId] = now;
            return false;
        }

        private void CleanupExpiredEntries()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _orderCreateRateLimit)
            {
                if (now - kvp.Value > _rateLimitWindow)
                    _orderCreateRateLimit.TryRemove(kvp.Key, out _);
            }
            foreach (var kvp in _activeOrderCreations)
            {
                if (now - kvp.Value > _activeCreationExpiry)
                    _activeOrderCreations.TryRemove(kvp.Key, out _);
            }
        }
    }

    public class ShipOrderRequest
    {
        public string ExpressCompanyName { get; set; } = "";
        public string ShipOrderNumber { get; set; } = "";
        public long ShipperId { get; set; }
    }
}
