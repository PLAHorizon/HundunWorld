using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static SKIT.FlurlHttpClient.Wechat.TenpayV3.Models.QueryMarketingPayGiftActivityMerchantsResponse.Types;

namespace Horizon.Orleans.Grains
{
    public class FlowerQueryGrain : Grain, IFlowerQueryGrain
    {
        private readonly ILogger<FlowerQueryGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IDataContext<FlowerEntityContext, FlowerProduct, long> _productContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderItem, long> _itemContext;
        private readonly IDataContext<FlowerEntityContext, FlowerUser, long> _userContext;

        public FlowerQueryGrain(
            ILogger<FlowerQueryGrain> logger,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IDataContext<FlowerEntityContext, FlowerUser, long> userContext,
            IDataContext<FlowerEntityContext, FlowerProduct, long> productContext,
            IDataContext<FlowerEntityContext, FlowerOrderItem, long> itemContext)
        {
            _logger = logger;
            _orderContext = orderContext;
            _productContext = productContext;
            _itemContext = itemContext;
            _userContext= userContext;
        }

        public async Task<List<OrderState>> QueryOrdersByBuyerAsync(Guid buyerId, int skip, int take)
        {
            try
            {
                var orders = await _orderContext.QueryAsync(o => o.BuyerId == buyerId);
                var result = orders.OrderByDescending(o => o.CreateTime).Skip(skip).Take(take).ToList();
                var states = new List<OrderState>();
                foreach (var order in result)
                {
                    states.Add(await MapOrderToStateAsync(order));
                }
                return states;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询买家订单失败: BuyerId={BuyerId}", buyerId);
                return new List<OrderState>();
            }
        }

        public async Task<List<OrderState>> QueryOrdersByMerchantAsync(long merchantId, int skip, int take)
        {
            try
            {
                var orders = await _orderContext.QueryAsync(o => o.MerchantId == merchantId);
                var result = orders.OrderByDescending(o => o.CreateTime).Skip(skip).Take(take).ToList();
                var states = new List<OrderState>();
                foreach (var order in result)
                {
                    states.Add(await MapOrderToStateAsync(order));
                }
                return states;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询商户订单失败: MerchantId={MerchantId}", merchantId);
                return new List<OrderState>();
            }
        }

        public async Task<List<ProductState>> QueryProductsByMerchantAsync(long merchantId, int skip, int take)
        {
            try
            {
                var products = await _productContext.QueryAsync(p => p.MerchantId == merchantId && !p.IsDeleted);
                var result = products.OrderByDescending(p => p.CreateTime).Skip(skip).Take(take).ToList();
                return result.Select(MapProductToState).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询商户商品失败: MerchantId={MerchantId}", merchantId);
                return new List<ProductState>();
            }
        }

        public async Task<List<ProductState>> QueryActiveProductsAsync(int speciesId, int skip, int take)
        {
            try
            {
                var products = await _productContext.QueryAsync(p => p.IsActive && !p.IsDeleted && (speciesId <= 0 || p.SpeciesId == speciesId));
                var result = products.OrderByDescending(p => p.CreateTime).Skip(skip).Take(take).ToList();
                return result.Select(MapProductToState).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询活跃商品失败: SpeciesId={SpeciesId}", speciesId);
                return new List<ProductState>();
            }
        }

        public async Task<int> CountOrdersByBuyerAsync(Guid buyerId)
        {
            try
            {
                var allOrders = await _orderContext.QueryAsync(o => o.BuyerId == buyerId);
                return allOrders.Count();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "统计买家订单数失败: BuyerId={BuyerId}", buyerId);
                return 0;
            }
        }

        public async Task<int> CountOrdersByMerchantAsync(long merchantId)
        {
            try
            {
                return await _orderContext.CountAsync(o => o.MerchantId == merchantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "统计商户订单数失败: MerchantId={MerchantId}", merchantId);
                return 0;
            }
        }

        public async Task<int> CountProductsByMerchantAsync(long merchantId)
        {
            try
            {
                return await _productContext.CountAsync(p => p.MerchantId == merchantId && !p.IsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "统计商户商品数失败: MerchantId={MerchantId}", merchantId);
                return 0;
            }
        }

        public async Task<Guid> GetUserIdAsync(string PassportId)
        {
            try
            {
                var user = await _userContext.QueryFirstOrDefaultAsync(p => p.Passport == PassportId && !p.IsDeleted);
                return user?.UserId ?? Guid.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户ID失败: PassportId={PassportId}", PassportId);
                return Guid.Empty;
            }
        }

        private async Task<OrderState> MapOrderToStateAsync(FlowerOrder o)
        {
            var state = new OrderState
            {
                OrderId = o.Id,
                OrderNo = o.OrderNo ?? "",
                BuyerId = o.BuyerId,
                MerchantId = o.MerchantId,
                Status = (OrderStatus)o.Status,
                TotalAmount = o.TotalAmount,
                PaymentMethod = o.PaymentMethod ?? "",
                PaymentTime = o.PaymentTime,
                ShippingAddress = o.ShippingAddress ?? "",
                IsPresale = o.IsPresale,
                PresaleDeliveryDate = o.PresaleDeliveryDate,
                ShipTo = o.ShipTo ?? "",
                CellPhone = o.CellPhone ?? "",
                ExpressCompanyName = o.ExpressCompanyName ?? "",
                ShipOrderNumber = o.ShipOrderNumber ?? "",
                Freight = o.Freight,
                OrderTotalAmount = o.OrderTotalAmount,
                RefundStatus = o.RefundStatus,
                SellerRemark = o.SellerRemark ?? "",
                DiscountAmount = o.DiscountAmount,
                FullDiscount = o.FullDiscount,
                Address = o.Address ?? "",
                ProductTotalAmount = o.ProductTotalAmount,
                CreatedAt = o.CreateTime
            };

            try
            {
                var items = await _itemContext.QueryAsync(i => i.OrderId == o.Id);
                state.Items = items.Select(i => new OrderItemState
                {
                    ProductId = i.ProductId,
                    SpeciesId = i.SpeciesId,
                    ProductName = i.ProductName ?? "",
                    Price = i.Price,
                    Quantity = i.Quantity,
                    Subtotal = i.Subtotal
                }).ToList();
            }
            catch { }

            return state;
        }

        private static ProductState MapProductToState(FlowerProduct p) => new()
        {
            ProductId = p.Id,
            MerchantId = p.MerchantId,
            SpeciesId = p.SpeciesId,
            ProductName = p.ProductName ?? "",
            Description = p.Description ?? "",
            Price = p.Price,
            Stock = p.Stock,
            IsActive = p.IsActive,
            Version = p.Version,
            AuditStatus = p.AuditStatus,
            MarketPrice = p.MarketPrice,
            Unit = p.Unit ?? "",
            Images = p.Images ?? ""
        };
    }
}
