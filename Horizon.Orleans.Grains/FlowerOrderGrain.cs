using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerOrderGrain : Grain, IOrderGrain
    {
        private readonly ILogger<FlowerOrderGrain> _logger;
        private readonly IPersistentState<OrderState> _orderState;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _dataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderItem, long> _itemContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderLog, long> _logContext;
        private readonly IDataContext<FlowerEntityContext, FlowerProduct, long> _productContext;
        private readonly IDataContext<FlowerEntityContext, FlowerRepurchaseRecord, long> _repurchaseContext;
        private readonly IDataContext<FlowerEntityContext, FlowerShopShipper, long> _shipperContext;

        public FlowerOrderGrain(
            ILogger<FlowerOrderGrain> logger,
            [PersistentState("order", "FlowerStore")] IPersistentState<OrderState> orderState,
            IDataContext<FlowerEntityContext, FlowerOrder, long> dataContext,
            IDataContext<FlowerEntityContext, FlowerOrderItem, long> itemContext,
            IDataContext<FlowerEntityContext, FlowerOrderLog, long> logContext,
            IDataContext<FlowerEntityContext, FlowerProduct, long> productContext,
            IDataContext<FlowerEntityContext, FlowerRepurchaseRecord, long> repurchaseContext,
            IDataContext<FlowerEntityContext, FlowerShopShipper, long> shipperContext)
        {
            _logger = logger;
            _orderState = orderState;
            _dataContext = dataContext;
            _itemContext = itemContext;
            _logContext = logContext;
            _productContext = productContext;
            _repurchaseContext = repurchaseContext;
            _shipperContext = shipperContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            var key = this.GetPrimaryKeyLong();
            _logger.LogInformation("FlowerOrderGrain {GrainKey} activating.", key);
            if (_orderState.State.OrderId <= 0 || _orderState.State.OrderId != key)
            {
                _orderState.State.OrderId = key;
                await ReloadStateFromDatabaseAsync(key);
            }
            await base.OnActivateAsync(cancellationToken);
        }

        private async Task ReloadStateFromDatabaseAsync(long orderId)
        {
            try
            {
                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == orderId);
                if (entity != null)
                {
                    var items = await _itemContext.QueryAsync(i => i.OrderId == orderId);
                    _orderState.State = new OrderState
                    {
                        OrderId = entity.Id,
                        OrderNo = entity.OrderNo ?? "",
                        BuyerId = entity.BuyerId,
                        MerchantId = entity.MerchantId,
                        TotalAmount = entity.TotalAmount,
                        Status = (OrderStatus)entity.Status,
                        PaymentMethod = entity.PaymentMethod ?? "",
                        PaymentTime = entity.PaymentTime,
                        ShippingAddress = entity.ShippingAddress ?? "",
                        IsPresale = entity.IsPresale,
                        PresaleDeliveryDate = entity.PresaleDeliveryDate,
                        Items = items.Select(i => new OrderItemState
                        {
                            ProductId = i.ProductId,
                            SpeciesId = i.SpeciesId,
                            ProductName = i.ProductName ?? "",
                            Price = i.Price,
                            Quantity = i.Quantity,
                            Subtotal = i.Subtotal
                        }).ToList(),
                        ShipTo = entity.ShipTo ?? "",
                        CellPhone = entity.CellPhone ?? "",
                        ExpressCompanyName = entity.ExpressCompanyName ?? "",
                        ShipOrderNumber = entity.ShipOrderNumber ?? "",
                        Freight = entity.Freight,
                        OrderTotalAmount = entity.OrderTotalAmount,
                        RefundStatus = entity.RefundStatus,
                        SellerRemark = entity.SellerRemark ?? "",
                        DiscountAmount = entity.DiscountAmount,
                        FullDiscount = entity.FullDiscount,
                        Address = entity.Address ?? "",
                        Platform = entity.Platform.ToString(),
                        ProductTotalAmount = entity.ProductTotalAmount,
                        PresaleReadyNotifiedAt = entity.PresaleReadyNotifiedAt,
                        DeliveredAt = entity.DeliveredAt,
                        SenderName = entity.SenderName ?? "",
                        SenderPhone = entity.SenderPhone ?? "",
                        SenderAddress = entity.SenderAddress ?? "",
                        CreatedAt = entity.CreateTime
                    };
                    await _orderState.WriteStateAsync();
                    _logger.LogInformation("FlowerOrderGrain {GrainKey} state reloaded from database, Status={Status}.", orderId, entity.Status);
                }
                else
                {
                    _logger.LogWarning("FlowerOrderGrain {GrainKey} no database record found.", orderId);
                }
            }
            catch (Exception ex)
                {
                    _logger.LogError(ex, "FlowerOrderGrain {GrainKey} failed to reload state from database.", orderId);
                }
        }

        public Task<OrderState> GetOrderAsync()
        {
            return Task.FromResult(_orderState.State);
        }

        public async Task<OrderState> CreateOrderAsync(OrderState order)
        {
            try
            {
                if (order == null || order.Items == null || order.Items.Count == 0)
                {
                    _logger.LogWarning("创建订单失败: 订单或商品列表为空");
                    return null;
                }

                if (order.BuyerId == Guid.Empty)
                {
                    _logger.LogWarning("创建订单失败: BuyerId无效 BuyerId={BuyerId}", order.BuyerId);
                    return null;
                }

                if (order.Items.Count > 100)
                {
                    _logger.LogWarning("创建订单失败: 商品项数超限 Count={Count}", order.Items.Count);
                    return null;
                }

                var orderNo = GenerateOrderNo();
                var validatedItems = new List<OrderItemState>();
                var serverProductTotalAmount = 0m;

                foreach (var item in order.Items)
                {
                    if (item.ProductId <= 0)
                    {
                        _logger.LogWarning("创建订单失败: 商品ID无效 ProductId={ProductId}", item.ProductId);
                        return null;
                    }

                    if (item.Quantity <= 0)
                    {
                        _logger.LogWarning("创建订单失败: 商品数量无效 ProductId={ProductId}, Quantity={Quantity}", item.ProductId, item.Quantity);
                        return null;
                    }

                    if (item.Quantity > 9999)
                    {
                        _logger.LogWarning("创建订单失败: 商品数量超限 ProductId={ProductId}, Quantity={Quantity}", item.ProductId, item.Quantity);
                        return null;
                    }

                    var product = await _productContext.QueryFirstOrDefaultAsync(p => p.Id == item.ProductId);
                    if (product == null)
                    {
                        _logger.LogWarning("创建订单失败: 商品不存在 ProductId={ProductId}", item.ProductId);
                        return null;
                    }

                    if (!product.IsActive)
                    {
                        _logger.LogWarning("创建订单失败: 商品已下架 ProductId={ProductId}", item.ProductId);
                        return null;
                    }

                    if (product.AuditStatus != 1)
                    {
                        _logger.LogWarning("创建订单失败: 商品未审核通过 ProductId={ProductId}, AuditStatus={AuditStatus}", item.ProductId, product.AuditStatus);
                        return null;
                    }

                    if (product.IsDeleted)
                    {
                        _logger.LogWarning("创建订单失败: 商品已删除 ProductId={ProductId}", item.ProductId);
                        return null;
                    }

                    if (product.Stock < item.Quantity)
                    {
                        _logger.LogWarning("创建订单失败: 库存不足 ProductId={ProductId}, Requested={Requested}, Stock={Stock}", item.ProductId, item.Quantity, product.Stock);
                        return null;
                    }

                    if (product.MaxBuyCount > 0 && item.Quantity > product.MaxBuyCount)
                    {
                        _logger.LogWarning("创建订单失败: 超过限购数量 ProductId={ProductId}, Requested={Requested}, MaxBuy={MaxBuy}", item.ProductId, item.Quantity, product.MaxBuyCount);
                        return null;
                    }

                    var serverPrice = product.Price;
                    var subtotal = Math.Round(serverPrice * item.Quantity, 4);
                    serverProductTotalAmount += subtotal;

                    validatedItems.Add(new OrderItemState
                    {
                        ProductId = product.Id,
                        SpeciesId = product.SpeciesId,
                        ProductName = product.ProductName ?? "",
                        Price = serverPrice,
                        Quantity = item.Quantity,
                        Subtotal = subtotal
                    });
                }

                var serverFreight = 0m;
                var serverDiscountAmount = 0m;
                var serverFullDiscount = 0m;
                var orderTotalAmount = serverProductTotalAmount + serverFreight - serverDiscountAmount - serverFullDiscount;
                if (orderTotalAmount < 0) orderTotalAmount = 0;

                if (orderTotalAmount > 100000000m)
                {
                    _logger.LogWarning("创建订单失败: 订单金额超限 OrderTotalAmount={OrderTotalAmount}", orderTotalAmount);
                    return null;
                }

                var entity = new FlowerOrder
                {
                    OrderNo = orderNo,
                    BuyerId = order.BuyerId,
                    MerchantId = order.MerchantId,
                    TotalAmount = serverProductTotalAmount,
                    Status = (int)OrderStatus.Pending,
                    ShippingAddress = SanitizeInput(order.ShippingAddress),
                    ShipTo = SanitizeInput(order.ShipTo),
                    CellPhone = SanitizeInput(order.CellPhone),
                    Address = SanitizeInput(order.Address),
                    IsPresale = order.IsPresale,
                    PresaleDeliveryDate = order.PresaleDeliveryDate,
                    Freight = serverFreight,
                    ProductTotalAmount = serverProductTotalAmount,
                    OrderTotalAmount = orderTotalAmount,
                    DiscountAmount = serverDiscountAmount,
                    FullDiscount = serverFullDiscount,
                    ExpressCompanyName = "",
                    ShipOrderNumber = "",
                    SellerRemark = "",
                    PaymentMethod = "",
                    InvoiceTitle = "",
                    InvoiceCode = "",
                    SenderName = "",
                    SenderPhone = "",
                    SenderAddress = "",
                    Passport = order.BuyerId.ToString("N"),
                    CreateTime = DateTime.Now
                };

                if (int.TryParse(order.Platform, out var platformVal))
                    entity.Platform = platformVal;

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("创建订单失败: 数据库保存返回null");
                    return null;
                }

                foreach (var item in validatedItems)
                {
                    await _itemContext.AddAsync(new FlowerOrderItem
                    {
                        OrderId = result.Id,
                        ProductId = item.ProductId,
                        SpeciesId = item.SpeciesId,
                        ProductName = item.ProductName,
                        Price = item.Price,
                        Quantity = item.Quantity,
                        Subtotal = item.Subtotal
                    });
                }

                order.OrderId = result.Id;
                order.OrderNo = orderNo;
                order.Status = OrderStatus.Pending;
                order.TotalAmount = serverProductTotalAmount;
                order.OrderTotalAmount = orderTotalAmount;
                order.Freight = serverFreight;
                order.DiscountAmount = serverDiscountAmount;
                order.FullDiscount = serverFullDiscount;
                order.Items = validatedItems;
                _orderState.State = order;
                await _orderState.WriteStateAsync();

                await WriteOrderLogAsync(result.Id, "Created", null, order, entity.Passport);

                _logger.LogInformation("创建订单: OrderId={OrderId}, OrderNo={OrderNo}, BuyerId={BuyerId}, MerchantId={MerchantId}, ProductTotal={ProductTotal}, Freight={Freight}, Discount={Discount}, FullDiscount={FullDiscount}, OrderTotal={OrderTotal}, Items={ItemCount}",
                    result.Id, orderNo, order.BuyerId, order.MerchantId, serverProductTotalAmount, serverFreight, serverDiscountAmount, serverFullDiscount, orderTotalAmount, validatedItems.Count);
                return _orderState.State;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建订单失败");
                throw;
            }
        }

        public async Task<bool> PayOrderAsync(string paymentMethod)
        {
            try
            {
                var validMethods = new[] { "WechatPay", "Alipay" };
                if (string.IsNullOrEmpty(paymentMethod) || !validMethods.Contains(paymentMethod))
                {
                    _logger.LogWarning("无效的支付渠道: {PaymentMethod}", SanitizeForLog(paymentMethod));
                    return false;
                }

                var state = _orderState.State;
                if (state.Status != OrderStatus.Pending)
                {
                    _logger.LogWarning("订单状态不允许支付: OrderId={OrderId}, Status={Status}", state.OrderId, state.Status);
                    return false;
                }

                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.OrderId);
                if (entity != null)
                {
                    if (entity.Status != (int)OrderStatus.Pending)
                    {
                        _logger.LogWarning("支付时数据库状态已变更: OrderId={OrderId}, DbStatus={DbStatus}", state.OrderId, entity.Status);
                        return false;
                    }
                    entity.Status = (int)OrderStatus.Paid;
                    entity.PaymentMethod = paymentMethod;
                    entity.PaymentTime = DateTime.Now;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                }

                var beforeStatus = state.Status;
                state.Status = OrderStatus.Paid;
                state.PaymentMethod = paymentMethod;
                state.PaymentTime = DateTime.Now;
                await _orderState.WriteStateAsync();
                await WriteOrderLogAsync(state.OrderId, "Paid", beforeStatus, OrderStatus.Paid, "");

                _logger.LogInformation("订单支付: OrderId={OrderId}, PaymentMethod={PaymentMethod}", state.OrderId, paymentMethod);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订单支付失败: OrderId={OrderId}", _orderState.State.OrderId);
                throw;
            }
        }

        public async Task<bool> ShipOrderAsync()
        {
            try
            {
                var state = _orderState.State;
                if (state.Status != OrderStatus.Paid)
                {
                    _logger.LogWarning("订单状态不允许发货: OrderId={OrderId}, Status={Status}", state.OrderId, state.Status);
                    return false;
                }

                var beforeStatus = state.Status;
                state.Status = OrderStatus.Shipped;

                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.OrderId);
                if (entity != null)
                {
                    entity.Status = (int)OrderStatus.Shipped;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                }

                await _orderState.WriteStateAsync();
                await WriteOrderLogAsync(state.OrderId, "Shipped", beforeStatus, OrderStatus.Shipped, "");

                _logger.LogInformation("订单发货: OrderId={OrderId}", state.OrderId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订单发货失败: OrderId={OrderId}", _orderState.State.OrderId);
                throw;
            }
        }

        public async Task<bool> DeliverOrderAsync()
        {
            try
            {
                var state = _orderState.State;
                if (state.Status != OrderStatus.Shipped)
                {
                    _logger.LogWarning("订单状态不允许确认收货: OrderId={OrderId}, Status={Status}", state.OrderId, state.Status);
                    return false;
                }

                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.OrderId);
                if (entity != null)
                {
                    if (entity.Status != (int)OrderStatus.Shipped)
                    {
                        _logger.LogWarning("确认收货时数据库状态已变更: OrderId={OrderId}, DbStatus={DbStatus}", state.OrderId, entity.Status);
                        return false;
                    }
                    entity.Status = (int)OrderStatus.Delivered;
                    entity.DeliveredAt = DateTime.Now;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                }

                var beforeStatus = state.Status;
                state.Status = OrderStatus.Delivered;
                state.DeliveredAt = DateTime.Now;
                await _orderState.WriteStateAsync();
                await WriteOrderLogAsync(state.OrderId, "Delivered", beforeStatus, OrderStatus.Delivered, "");

                _logger.LogInformation("确认收货: OrderId={OrderId}", state.OrderId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确认收货失败: OrderId={OrderId}", _orderState.State.OrderId);
                throw;
            }
        }

        public async Task<bool> CompleteOrderAsync()
        {
            try
            {
                var state = _orderState.State;
                if (state.Status != OrderStatus.Delivered)
                {
                    _logger.LogWarning("订单状态不允许完成: OrderId={OrderId}, Status={Status}", state.OrderId, state.Status);
                    return false;
                }

                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.OrderId);
                if (entity != null)
                {
                    if (entity.Status != (int)OrderStatus.Delivered)
                    {
                        _logger.LogWarning("完成订单时数据库状态已变更: OrderId={OrderId}, DbStatus={DbStatus}", state.OrderId, entity.Status);
                        return false;
                    }
                    entity.Status = (int)OrderStatus.Completed;
                    entity.CompletionTime = DateTime.Now;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                }

                var beforeStatus = state.Status;
                state.Status = OrderStatus.Completed;
                await _orderState.WriteStateAsync();
                await WriteOrderLogAsync(state.OrderId, "Completed", beforeStatus, OrderStatus.Completed, "");

                _logger.LogInformation("订单完成: OrderId={OrderId}", state.OrderId);

                try
                {
                    var billingGrain = GrainFactory.GetGrain<IShopBillingGrain>(0);
                    var commissionRate = 0.05m;
                    var orderAmount = state.OrderTotalAmount > 0 ? state.OrderTotalAmount : state.TotalAmount;
                    var platformCommission = Math.Round(orderAmount * commissionRate, 2);
                    await billingGrain.WritePendingSettlementAsync(new PendingSettlementState
                    {
                        OrderId = state.OrderId,
                        ShopId = state.MerchantId,
                        OrderAmount = orderAmount,
                        PlatformCommission = platformCommission,
                        RefundAmount = 0,
                        SettleableAmount = orderAmount - platformCommission,
                        Status = 0
                    });
                    _logger.LogInformation("订单完成自动写入待结算: OrderId={OrderId}, ShopId={ShopId}, Amount={Amount}, Commission={Commission}",
                        state.OrderId, state.MerchantId, orderAmount, platformCommission);
                }
                catch (Exception exSettle)
                {
                    _logger.LogError(exSettle, "订单完成写入待结算失败(非阻塞): OrderId={OrderId}", state.OrderId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订单完成失败: OrderId={OrderId}", _orderState.State.OrderId);
                throw;
            }
        }

        public async Task<bool> CancelOrderAsync(string reason)
        {
            try
            {
                var state = _orderState.State;
                if (state.Status != OrderStatus.Pending && state.Status != OrderStatus.Paid)
                {
                    _logger.LogWarning("订单状态不允许取消: OrderId={OrderId}, Status={Status}", state.OrderId, state.Status);
                    return false;
                }

                if (state.Status == OrderStatus.Paid)
                {
                    _logger.LogWarning("已支付订单取消，需先退款: OrderId={OrderId}", state.OrderId);
                    return false;
                }

                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.OrderId);
                if (entity != null)
                {
                    if (entity.Status != (int)OrderStatus.Pending)
                    {
                        _logger.LogWarning("取消时数据库状态已变更: OrderId={OrderId}, DbStatus={DbStatus}", state.OrderId, entity.Status);
                        return false;
                    }
                    entity.Status = (int)OrderStatus.Cancelled;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                }

                var beforeStatus = state.Status;
                state.Status = OrderStatus.Cancelled;
                await _orderState.WriteStateAsync();
                await WriteOrderLogAsync(state.OrderId, "Cancelled", beforeStatus, OrderStatus.Cancelled, reason);

                _logger.LogInformation("取消订单: OrderId={OrderId}, Reason={Reason}", state.OrderId, SanitizeForLog(reason));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订单失败: OrderId={OrderId}", _orderState.State.OrderId);
                throw;
            }
        }

        public async Task<bool> RequestRefundAsync(string reason)
        {
            try
            {
                var state = _orderState.State;
                if (state.Status != OrderStatus.Paid && state.Status != OrderStatus.Shipped && state.Status != OrderStatus.Delivered)
                {
                    _logger.LogWarning("订单状态不允许退款: OrderId={OrderId}, Status={Status}", state.OrderId, state.Status);
                    return false;
                }

                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.OrderId);
                if (entity != null)
                {
                    if (entity.Status != (int)state.Status)
                    {
                        _logger.LogWarning("退款时数据库状态已变更: OrderId={OrderId}, DbStatus={DbStatus}, GrainStatus={GrainStatus}", state.OrderId, entity.Status, state.Status);
                        return false;
                    }
                    entity.Status = (int)OrderStatus.Refunding;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                }

                var beforeStatus = state.Status;
                state.Status = OrderStatus.Refunding;
                await _orderState.WriteStateAsync();
                await WriteOrderLogAsync(state.OrderId, "Refunding", beforeStatus, OrderStatus.Refunding, reason);

                _logger.LogInformation("申请退款: OrderId={OrderId}, Reason={Reason}", state.OrderId, SanitizeForLog(reason));
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "申请退款失败: OrderId={OrderId}", _orderState.State.OrderId);
                throw;
            }
        }

        private async Task WriteOrderLogAsync(long orderId, string actionType, object beforeSnapshot, object afterSnapshot, string operatorPassport)
        {
            await _logContext.AddAsync(new FlowerOrderLog
            {
                OrderId = orderId,
                ActionType = actionType,
                BeforeSnapshot = beforeSnapshot != null ? JsonConvert.SerializeObject(beforeSnapshot) : "",
                AfterSnapshot = afterSnapshot != null ? JsonConvert.SerializeObject(afterSnapshot) : "",
                OperatorPassport = operatorPassport,
                OperatedAt = DateTime.Now
            });
        }

        private static string SanitizeForLog(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Replace('\r', ' ').Replace('\n', ' ');
        }

        private static string GenerateOrderNo()
        {
            Span<byte> bytes = stackalloc byte[2];
            RandomNumberGenerator.Fill(bytes);
            var randomPart = BitConverter.ToUInt16(bytes) % 10000;
            return $"FO{DateTime.Now:yyyyMMddHHmmss}{randomPart:D4}";
        }

        private static string SanitizeInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            if (input.Length > 512) input = input.Substring(0, 512);
            return input.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        public async Task<OrderState> ShipOrderAsync(long orderId, string expressCompany, string shipOrderNumber, long shipperId = 0)
        {
            try
            {
                string senderName = "";
                string senderPhone = "";
                string senderAddress = "";

                if (shipperId > 0)
                {
                    var shipper = await _shipperContext.QueryFirstOrDefaultAsync(s => s.Id == shipperId && !s.IsDeleted);
                    if (shipper != null)
                    {
                        senderName = shipper.ShipperName ?? "";
                        senderPhone = shipper.TelPhone ?? "";
                        senderAddress = shipper.Address ?? "";
                    }
                    else
                    {
                        _logger.LogWarning("发货点不存在或已删除: ShipperId={ShipperId}", shipperId);
                    }
                }

                var state = _orderState.State;
                if (state == null || state.OrderId <= 0 || state.OrderId != orderId)
                {
                    var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == orderId);
                    if (entity == null) return null;
                    if (entity.Status != (int)OrderStatus.Paid)
                    {
                        _logger.LogWarning("发货状态校验失败: OrderId={OrderId}, Status={Status}", orderId, entity.Status);
                        return null;
                    }
                    entity.ExpressCompanyName = SanitizeInput(expressCompany);
                    entity.ShipOrderNumber = SanitizeInput(shipOrderNumber);
                    entity.Status = (int)OrderStatus.Shipped;
                    entity.ShippingDate = DateTime.Now;
                    entity.SenderName = senderName;
                    entity.SenderPhone = senderPhone;
                    entity.SenderAddress = senderAddress;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                    await WriteOrderLogAsync(orderId, "Shipped", OrderStatus.Paid, OrderStatus.Shipped, "");
                    return new OrderState { OrderId = entity.Id, Status = OrderStatus.Shipped, SenderName = senderName, SenderPhone = senderPhone, SenderAddress = senderAddress };
                }

                if (state.Status != OrderStatus.Paid)
                {
                    _logger.LogWarning("发货状态校验失败: OrderId={OrderId}, Status={Status}", orderId, state.Status);
                    return null;
                }

                var sanitizedExpress = SanitizeInput(expressCompany);
                var sanitizedShipNo = SanitizeInput(shipOrderNumber);

                var dbEntity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == orderId);
                if (dbEntity != null)
                {
                    if (dbEntity.Status != (int)OrderStatus.Paid)
                    {
                        _logger.LogWarning("发货时数据库状态已变更: OrderId={OrderId}, DbStatus={DbStatus}", orderId, dbEntity.Status);
                        return null;
                    }
                    dbEntity.ExpressCompanyName = sanitizedExpress;
                    dbEntity.ShipOrderNumber = sanitizedShipNo;
                    dbEntity.Status = (int)OrderStatus.Shipped;
                    dbEntity.ShippingDate = DateTime.Now;
                    dbEntity.SenderName = senderName;
                    dbEntity.SenderPhone = senderPhone;
                    dbEntity.SenderAddress = senderAddress;
                    await _dataContext.UpdateAsync(dbEntity, dbEntity.Id);
                }

                var beforeStatus = state.Status;
                state.Status = OrderStatus.Shipped;
                state.ExpressCompanyName = sanitizedExpress;
                state.ShipOrderNumber = sanitizedShipNo;
                state.SenderName = senderName;
                state.SenderPhone = senderPhone;
                state.SenderAddress = senderAddress;
                await _orderState.WriteStateAsync();
                await WriteOrderLogAsync(orderId, "Shipped", beforeStatus, OrderStatus.Shipped, "");

                _logger.LogInformation("订单发货: OrderId={OrderId}, ShipperId={ShipperId}", orderId, shipperId);
                return new OrderState { OrderId = state.OrderId, Status = OrderStatus.Shipped, SenderName = senderName, SenderPhone = senderPhone, SenderAddress = senderAddress };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发货失败: OrderId={OrderId}", orderId);
                throw;
            }
        }

        public async Task<System.Collections.Generic.List<OrderState>> GetMerchantOrdersByStatusAsync(long merchantId, int? status, int page, int pageSize)
        {
            var query = await _dataContext.QueryAsync(e => e.MerchantId == merchantId);
            if (status.HasValue)
            {
                query = query.Where(e => e.Status == status.Value);
            }
            return query.OrderByDescending(e => e.CreateTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new OrderState
                {
                    OrderId = e.Id,
                    OrderNo = e.OrderNo ?? "",
                    BuyerId = e.BuyerId,
                    MerchantId = e.MerchantId,
                    TotalAmount = e.TotalAmount,
                    Status = (OrderStatus)e.Status,
                    PaymentMethod = e.PaymentMethod ?? "",
                    ShippingAddress = e.ShippingAddress ?? "",
                    ShipTo = e.ShipTo ?? "",
                    CellPhone = e.CellPhone ?? "",
                    ExpressCompanyName = e.ExpressCompanyName ?? "",
                    ShipOrderNumber = e.ShipOrderNumber ?? "",
                    Freight = e.Freight,
                    OrderTotalAmount = e.OrderTotalAmount,
                    RefundStatus = e.RefundStatus,
                    SellerRemark = e.SellerRemark ?? "",
                    SenderName = e.SenderName ?? "",
                    SenderPhone = e.SenderPhone ?? "",
                    SenderAddress = e.SenderAddress ?? ""
                }).ToList();
        }

        public async Task<bool> NotifyPresaleReadyAsync(long orderId)
        {
            try
            {
                var order = await _dataContext.QueryFirstOrDefaultAsync(o => o.Id == orderId);
                if (order == null || !order.IsPresale)
                {
                    _logger.LogWarning("预售就绪通知失败，订单不存在或非预售订单: OrderId={OrderId}", orderId);
                    return false;
                }

                if (order.PresaleReadyNotifiedAt.HasValue)
                {
                    _logger.LogInformation("预售订单已通知过就绪: OrderId={OrderId}", orderId);
                    return true;
                }

                order.PresaleReadyNotifiedAt = DateTime.Now;
                await _dataContext.UpdateAsync(order, order.Id);

                _orderState.State.PresaleReadyNotifiedAt = order.PresaleReadyNotifiedAt;
                await _orderState.WriteStateAsync();

                _logger.LogInformation("预售订单已通知就绪: OrderId={OrderId}", orderId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "预售就绪通知失败: OrderId={OrderId}", orderId);
                throw;
            }
        }

        public async Task<bool> RepurchaseAsync(Guid buyerId, long originalOrderId)
        {
            var order = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == originalOrderId);
            if (order == null || order.BuyerId != buyerId) return false;

            var items = await _itemContext.QueryAsync(i => i.OrderId == originalOrderId);
            if (!items.Any()) return false;

            var cartGrain = GrainFactory.GetGrain<IShoppingCartGrain>(buyerId);
            bool anyAdded = false;

            foreach (var item in items)
            {
                var product = await _productContext.QueryFirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product != null && product.IsActive && product.AuditStatus == 1 && !product.IsDeleted)
                {
                    await cartGrain.AddItemAsync(item.ProductId, item.Quantity);
                    anyAdded = true;
                }
            }

            if (anyAdded)
            {
                await _repurchaseContext.AddAsync(new FlowerRepurchaseRecord
                {
                    BuyerId = buyerId,
                    OriginalOrderId = originalOrderId,
                    RepurchaseTime = DateTime.Now
                });
            }

            return anyAdded;
        }

        public async Task<List<RepurchaseState>> GetFrequentProductsAsync(Guid buyerId, int topN)
        {
            if (topN <= 0 || topN > 100) topN = 10;
            var orders = await _dataContext.QueryAsync(o => o.BuyerId == buyerId && o.IsValid);
            var orderIds = orders.Select(o => o.Id).ToList();

            if (!orderIds.Any()) return new List<RepurchaseState>();

            var allItems = new List<FlowerOrderItem>();
            foreach (var oid in orderIds)
            {
                var items = await _itemContext.QueryAsync(i => i.OrderId == oid);
                allItems.AddRange(items);
            }

            var grouped = allItems
                .GroupBy(i => i.ProductId)
                .Select(g => new { ProductId = g.Key, Count = g.Count(), LatestItem = g.OrderByDescending(i => i.Id).First() })
                .OrderByDescending(x => x.Count)
                .Take(topN);

            var result = new List<RepurchaseState>();
            foreach (var g in grouped)
            {
                var recentOrder = orders.FirstOrDefault(o => o.Id == g.LatestItem.OrderId);
                result.Add(new RepurchaseState
                {
                    BuyerId = buyerId,
                    OriginalOrderId = g.LatestItem.OrderId,
                    RepurchaseTime = recentOrder?.CreateTime ?? DateTime.Now
                });
            }

            return result;
        }

        public async Task<List<OrderState>> BatchShipOrdersAsync(BatchShipRequest request)
        {
            var results = new List<OrderState>();
            if (request?.OrderIds == null || request.OrderIds.Count == 0 || request.OrderIds.Count > 50)
                return results;

            string senderName = "";
            string senderPhone = "";
            string senderAddress = "";

            if (request.ShipperId.HasValue && request.ShipperId.Value > 0)
            {
                var shipper = await _shipperContext.QueryFirstOrDefaultAsync(s => s.Id == request.ShipperId.Value && !s.IsDeleted);
                if (shipper != null)
                {
                    senderName = shipper.ShipperName ?? "";
                    senderPhone = shipper.TelPhone ?? "";
                    senderAddress = shipper.Address ?? "";
                }
                else
                {
                    _logger.LogWarning("批量发货-发货点不存在或已删除: ShipperId={ShipperId}", request.ShipperId.Value);
                }
            }

            var index = 1;
            foreach (var orderId in request.OrderIds)
            {
                try
                {
                    if (orderId <= 0) continue;

                    var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == orderId);
                    if (entity == null) continue;

                    if (entity.Status != (int)OrderStatus.Paid)
                    {
                        _logger.LogWarning("批量发货跳过-状态不匹配: OrderId={OrderId}, Status={Status}", orderId, entity.Status);
                        continue;
                    }

                    var shipOrderNumber = $"{SanitizeInput(request.ShipOrderNumberPrefix)}{index:D4}";
                    entity.ExpressCompanyName = SanitizeInput(request.ExpressCompanyName);
                    entity.ShipOrderNumber = shipOrderNumber;
                    entity.Status = (int)OrderStatus.Shipped;
                    entity.ShippingDate = DateTime.Now;
                    entity.SenderName = senderName;
                    entity.SenderPhone = senderPhone;
                    entity.SenderAddress = senderAddress;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                    await WriteOrderLogAsync(orderId, "Shipped", OrderStatus.Paid, OrderStatus.Shipped, "BatchShip");
                    results.Add(new OrderState { OrderId = entity.Id, Status = OrderStatus.Shipped, SenderName = senderName, SenderPhone = senderPhone, SenderAddress = senderAddress });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "批量发货跳过: OrderId={OrderId}", orderId);
                }
                index++;
            }
            return results;
        }
    }
}
