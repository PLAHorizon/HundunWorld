using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using BizRefundStatus = Horizon.Game.Message.Enums.RefundStatus;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains
{
    public class FlowerOrderRefundGrain : Grain, IOrderRefundGrain
    {
        private readonly ILogger<FlowerOrderRefundGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderRefund, long> _context;
        private readonly IDataContext<FlowerEntityContext, FlowerReturnShipment, long> _shipmentContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> _paymentContext;
        private readonly IDataContext<FlowerEntityContext, FlowerShopShipper, long> _shipperContext;
        private readonly IGrainFactory _grainFactory;

        public FlowerOrderRefundGrain(
            ILogger<FlowerOrderRefundGrain> logger,
            IDataContext<FlowerEntityContext, FlowerOrderRefund, long> context,
            IDataContext<FlowerEntityContext, FlowerReturnShipment, long> shipmentContext,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> paymentContext,
            IDataContext<FlowerEntityContext, FlowerShopShipper, long> shipperContext,
            IGrainFactory grainFactory)
        {
            _logger = logger;
            _context = context;
            _shipmentContext = shipmentContext;
            _orderContext = orderContext;
            _paymentContext = paymentContext;
            _shipperContext = shipperContext;
            _grainFactory = grainFactory;
        }

        public async Task<OrderRefundState> GetRefundAsync(long refundId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == refundId);
            return MapToState(entity);
        }

        public async Task<OrderRefundState> RequestRefundAsync(OrderRefundState refund)
        {
            var entity = new FlowerOrderRefund
            {
                OrderId = refund.OrderId,
                OrderItemId = refund.OrderItemId,
                RefundNo = "RF" + DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(1000, 9999),
                RefundAmount = refund.RefundAmount,
                Reason = refund.Reason,
                Status = (int)BizRefundStatus.PendingAudit,
                RefundMode = refund.RefundMode,
                BuyerId = refund.BuyerId,
                MerchantId = refund.MerchantId,
                EnabledRefundAmount = refund.EnabledRefundAmount,
                ReturnQuantity = refund.ReturnQuantity
            };
            var result = await _context.AddAsync(entity);
            return result != null ? MapToState(result) : null;
        }

        public async Task<OrderRefundState> SellerAuditRefundAsync(long refundId, bool approved, string remark)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == refundId);
            if (entity == null) return null;

            if (approved)
            {
                entity.Status = (int)BizRefundStatus.SellerAgreed;
                entity.SellerAuditRemark = remark ?? "";
                entity.SellerAuditTime = DateTime.Now;

                if (entity.RefundMode == (int)RefundMode.RefundOnly)
                {
                    var refundResult = await ProcessChannelRefundAsync(entity);
                    if (refundResult)
                    {
                        entity.Status = (int)BizRefundStatus.RefundCompleted;
                    }
                    else
                    {
                        _logger.LogError("仅退款渠道调用失败，保持SellerAgreed状态: RefundId={RefundId}", refundId);
                    }
                }
                else
                {
                    entity.ReturnDeadline = DateTime.Now.AddDays(7);
                    var shippers = await _shipperContext.QueryAsync(s => s.ShopId == entity.MerchantId && s.IsDefaultSendGoods);
                    var defaultShipper = shippers.FirstOrDefault();
                    if (defaultShipper != null)
                    {
                        entity.ReturnAddress = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            ShipperName = defaultShipper.ShipperName,
                            RegionId = defaultShipper.RegionId,
                            Address = defaultShipper.Address,
                            TelPhone = defaultShipper.TelPhone
                        });
                    }
                }
            }
            else
            {
                entity.Status = (int)BizRefundStatus.SellerRefused;
                entity.SellerAuditRemark = remark ?? "";
                entity.SellerAuditTime = DateTime.Now;
            }

            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<OrderRefundState> PlatformAuditRefundAsync(long refundId, bool approved, string remark)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == refundId);
            if (entity == null) return null;

            if (approved)
            {
                entity.Status = (int)BizRefundStatus.Refunding;
                entity.PlatformRemark = remark ?? "";
                entity.PlatformAuditTime = DateTime.Now;
                await _context.UpdateAsync(entity, entity.Id);

                var refundResult = await ProcessChannelRefundAsync(entity);
                if (refundResult)
                {
                    entity.Status = (int)BizRefundStatus.RefundCompleted;
                    await _context.UpdateAsync(entity, entity.Id);
                }
                else
                {
                    _logger.LogError("平台审核退款渠道调用失败: RefundId={RefundId}", refundId);
                }
            }
            else
            {
                entity.Status = (int)BizRefundStatus.RefundClosed;
                entity.PlatformRemark = remark ?? "";
                entity.PlatformAuditTime = DateTime.Now;
                await _context.UpdateAsync(entity, entity.Id);
            }

            return MapToState(entity);
        }

        public async Task<OrderRefundState> SubmitReturnShipmentAsync(long refundId, string expressCompanyName, string shipOrderNumber)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == refundId);
            if (entity == null) return null;

            if (entity.Status != (int)BizRefundStatus.SellerAgreed)
            {
                _logger.LogWarning("退款单状态不允许提交退货物流: RefundId={RefundId}, Status={Status}", refundId, entity.Status);
                return MapToState(entity);
            }

            var shipment = new FlowerReturnShipment
            {
                RefundId = refundId,
                ExpressCompanyName = expressCompanyName,
                ShipOrderNumber = shipOrderNumber,
                ReturnAddress = entity.ReturnAddress ?? "",
                ShippedAt = DateTime.Now,
                Status = (int)ReturnShipmentStatus.Shipped
            };
            var shipmentResult = await _shipmentContext.AddAsync(shipment);
            if (shipmentResult == null)
            {
                _logger.LogError("创建退货物流记录失败: RefundId={RefundId}", refundId);
                return MapToState(entity);
            }

            entity.ReturnShipmentId = shipmentResult.Id;
            entity.SellerConfirmDeadline = DateTime.Now.AddDays(7);
            entity.Status = (int)BizRefundStatus.Refunding;
            await _context.UpdateAsync(entity, entity.Id);

            return MapToState(entity);
        }

        public async Task<OrderRefundState> ConfirmReturnReceivedAsync(long refundId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == refundId);
            if (entity == null) return null;

            if (entity.Status != (int)BizRefundStatus.Refunding)
            {
                _logger.LogWarning("退款单状态不允许确认收货: RefundId={RefundId}, Status={Status}", refundId, entity.Status);
                return MapToState(entity);
            }

            if (entity.ReturnShipmentId.HasValue)
            {
                var shipment = await _shipmentContext.QueryFirstOrDefaultAsync(s => s.Id == entity.ReturnShipmentId.Value);
                if (shipment != null)
                {
                    shipment.ReceivedAt = DateTime.Now;
                    shipment.Status = (int)ReturnShipmentStatus.Received;
                    await _shipmentContext.UpdateAsync(shipment, shipment.Id);
                }
            }

            var refundResult = await ProcessChannelRefundAsync(entity);
            if (refundResult)
            {
                entity.Status = (int)BizRefundStatus.RefundCompleted;
            }
            else
            {
                _logger.LogError("确认收货后退款渠道调用失败: RefundId={RefundId}", refundId);
            }

            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<OrderRefundState> AutoConfirmReturnAsync(long refundId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == refundId);
            if (entity == null) return null;

            if (entity.Status != (int)BizRefundStatus.Refunding)
                return MapToState(entity);

            if (!entity.SellerConfirmDeadline.HasValue || entity.SellerConfirmDeadline.Value > DateTime.Now)
                return MapToState(entity);

            return await ConfirmReturnReceivedAsync(refundId);
        }

        public async Task<OrderRefundState> AutoCloseReturnAsync(long refundId)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == refundId);
            if (entity == null) return null;

            if (entity.Status != (int)BizRefundStatus.SellerAgreed)
                return MapToState(entity);

            if (!entity.ReturnDeadline.HasValue || entity.ReturnDeadline.Value > DateTime.Now)
                return MapToState(entity);

            if (entity.ReturnShipmentId.HasValue)
                return MapToState(entity);

            entity.Status = (int)BizRefundStatus.RefundClosed;
            await _context.UpdateAsync(entity, entity.Id);
            return MapToState(entity);
        }

        public async Task<OrderRefundState> OnRefundCompletedAsync(long refundId, long orderId, decimal refundAmount, decimal orderTotalAmount)
        {
            var entity = await _context.QueryFirstOrDefaultAsync(e => e.Id == refundId);
            if (entity == null) return null;

            var order = await _orderContext.QueryFirstOrDefaultAsync(o => o.Id == orderId);
            if (order != null)
            {
                if (refundAmount >= orderTotalAmount)
                {
                    order.RefundStatus = (int)OrderRefundStatus.Refunded;
                    order.Status = (int)OrderOperateStatus.Closed;
                }
                else
                {
                    order.RefundStatus = (int)OrderRefundStatus.PartialRefunded;
                }
                await _orderContext.UpdateAsync(order, order.Id);
            }

            var billingGrain = _grainFactory.GetGrain<IShopBillingGrain>(entity.MerchantId);
            await billingGrain.RefundDeductFromPendingAsync(orderId, refundAmount);

            return MapToState(entity);
        }

        public async Task<List<OrderRefundState>> GetMerchantRefundsAsync(long merchantId, int? status)
        {
            var query = await _context.QueryAsync(e => e.MerchantId == merchantId);
            if (status.HasValue)
            {
                query = query.Where(e => e.Status == status.Value);
            }
            return query.OrderByDescending(e => e.CreateTime).Select(MapToState).ToList();
        }

        public async Task<List<OrderRefundState>> GetBuyerRefundsAsync(Guid buyerId)
        {
            var entities = await _context.QueryAsync(e => e.BuyerId == buyerId);
            return entities.OrderByDescending(e => e.CreateTime).Select(MapToState).ToList();
        }

        private async Task<bool> ProcessChannelRefundAsync(FlowerOrderRefund entity)
        {
            try
            {
                var transactions = await _paymentContext.QueryAsync(t => t.OrderId == entity.OrderId && t.Status == 1);
                var transaction = transactions.FirstOrDefault();
                if (transaction == null)
                {
                    _logger.LogError("未找到已支付的交易记录: OrderId={OrderId}", entity.OrderId);
                    return false;
                }

                var paymentGrain = _grainFactory.GetGrain<IPaymentTransactionGrain>(transaction.Id);
                var refundResult = await paymentGrain.RefundAsync(entity.RefundAmount, entity.Reason ?? "");

                if (refundResult)
                {
                    var order = await _orderContext.QueryFirstOrDefaultAsync(o => o.Id == entity.OrderId);
                    await OnRefundCompletedAsync(entity.Id, entity.OrderId, entity.RefundAmount, order?.OrderTotalAmount ?? 0);
                }

                return refundResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "渠道退款处理失败: RefundId={RefundId}, OrderId={OrderId}", entity.Id, entity.OrderId);
                return false;
            }
        }

        private OrderRefundState MapToState(FlowerOrderRefund entity)
        {
            if (entity == null) return null;
            return new OrderRefundState
            {
                Id = entity.Id,
                OrderId = entity.OrderId,
                OrderItemId = entity.OrderItemId,
                RefundNo = entity.RefundNo ?? "",
                RefundAmount = entity.RefundAmount,
                Reason = entity.Reason ?? "",
                Status = entity.Status,
                RefundMode = entity.RefundMode,
                SellerAuditRemark = entity.SellerAuditRemark ?? "",
                SellerAuditTime = entity.SellerAuditTime,
                PlatformRemark = entity.PlatformRemark ?? "",
                PlatformAuditTime = entity.PlatformAuditTime,
                BuyerId = entity.BuyerId,
                MerchantId = entity.MerchantId,
                EnabledRefundAmount = entity.EnabledRefundAmount,
                ReturnQuantity = entity.ReturnQuantity
            };
        }
    }
}
