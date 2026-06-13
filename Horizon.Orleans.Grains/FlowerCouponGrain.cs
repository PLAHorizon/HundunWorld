using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Horizon.Orleans.Grains
{
    public class FlowerCouponGrain : Grain, ICouponGrain
    {
        private readonly ILogger<FlowerCouponGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerCoupon, long> _couponContext;
        private readonly IDataContext<FlowerEntityContext, FlowerCouponRecord, long> _recordContext;

        public FlowerCouponGrain(
            ILogger<FlowerCouponGrain> logger,
            IDataContext<FlowerEntityContext, FlowerCoupon, long> couponContext,
            IDataContext<FlowerEntityContext, FlowerCouponRecord, long> recordContext)
        {
            _logger = logger;
            _couponContext = couponContext;
            _recordContext = recordContext;
        }

        public async Task<CouponState> CreateCouponAsync(CouponState coupon)
        {
            var entity = new FlowerCoupon
            {
                ShopId = coupon.ShopId,
                CouponName = coupon.CouponName,
                CouponType = coupon.CouponType,
                Denomination = coupon.Denomination,
                UseCondition = coupon.UseCondition,
                StartDate = coupon.StartDate,
                EndDate = coupon.EndDate,
                TotalCount = coupon.TotalCount,
                ReceivedCount = 0,
                UsedCount = 0,
                IsActive = true
            };
            var result = await _couponContext.AddAsync(entity);
            return MapToState(result);
        }

        public async Task<CouponState> GetCouponAsync(long couponId)
        {
            var entity = await _couponContext.QueryFirstOrDefaultAsync(e => e.Id == couponId && !e.IsDeleted);
            return MapToState(entity);
        }

        public async Task<List<CouponState>> GetShopCouponsAsync(long shopId)
        {
            var entities = await _couponContext.QueryAsync(e => e.ShopId == shopId && !e.IsDeleted && e.IsActive);
            return entities.Select(MapToState).ToList();
        }

        public async Task<CouponRecordState> ReceiveCouponAsync(long couponId, Guid userId)
        {
            var coupon = await _couponContext.QueryFirstOrDefaultAsync(e => e.Id == couponId && !e.IsDeleted && e.IsActive);
            if (coupon == null || coupon.ReceivedCount >= coupon.TotalCount) return null;
            if (coupon.EndDate < DateTime.Now) return null;

            coupon.ReceivedCount++;
            await _couponContext.UpdateAsync(coupon, coupon.Id);

            var record = new FlowerCouponRecord
            {
                CouponId = couponId,
                UserId = userId,
                Status = 0,
                ReceivedAt = DateTime.Now
            };
            var result = await _recordContext.AddAsync(record);
            return MapRecordToState(result);
        }

        public async Task<bool> UseCouponAsync(long recordId, long orderId)
        {
            var record = await _recordContext.QueryFirstOrDefaultAsync(e => e.Id == recordId && e.Status == 0);
            if (record == null) return false;

            record.Status = 1;
            record.UsedOrderId = orderId;
            record.UsedAt = DateTime.Now;
            await _recordContext.UpdateAsync(record, record.Id);

            var coupon = await _couponContext.QueryFirstOrDefaultAsync(e => e.Id == record.CouponId);
            if (coupon != null)
            {
                coupon.UsedCount++;
                await _couponContext.UpdateAsync(coupon, coupon.Id);
            }
            return true;
        }

        public async Task<List<CouponRecordState>> GetUserCouponsAsync(Guid userId)
        {
            var entities = await _recordContext.QueryAsync(e => e.UserId == userId);
            return entities.Select(MapRecordToState).ToList();
        }

        public async Task<int> ExpireCouponsAsync()
        {
            var now = DateTime.Now;
            var expiredCoupons = await _couponContext.QueryAsync(e => !e.IsDeleted && e.EndDate < now && e.IsActive);
            var count = 0;
            foreach (var coupon in expiredCoupons)
            {
                coupon.IsActive = false;
                await _couponContext.UpdateAsync(coupon, coupon.Id);
                count++;
            }

            var expiredRecords = await _recordContext.QueryAsync(e => e.Status == 0);
            foreach (var record in expiredRecords)
            {
                var coupon = await _couponContext.QueryFirstOrDefaultAsync(e => e.Id == record.CouponId);
                if (coupon != null && coupon.EndDate < now)
                {
                    record.Status = 2;
                    await _recordContext.UpdateAsync(record, record.Id);
                }
            }
            return count;
        }

        private CouponState MapToState(FlowerCoupon entity)
        {
            if (entity == null) return null;
            return new CouponState
            {
                Id = entity.Id,
                ShopId = entity.ShopId,
                CouponName = entity.CouponName ?? "",
                CouponType = entity.CouponType,
                Denomination = entity.Denomination,
                UseCondition = entity.UseCondition,
                StartDate = entity.StartDate,
                EndDate = entity.EndDate,
                TotalCount = entity.TotalCount,
                ReceivedCount = entity.ReceivedCount,
                UsedCount = entity.UsedCount,
                IsActive = entity.IsActive
            };
        }

        private CouponRecordState MapRecordToState(FlowerCouponRecord entity)
        {
            if (entity == null) return null;
            return new CouponRecordState
            {
                Id = entity.Id,
                CouponId = entity.CouponId,
                UserId = entity.UserId,
                Status = entity.Status,
                UsedOrderId = entity.UsedOrderId,
                ReceivedAt = entity.ReceivedAt,
                UsedAt = entity.UsedAt
            };
        }
    }
}
