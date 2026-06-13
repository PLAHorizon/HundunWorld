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
    public class FlowerShopBillingGrain : Grain, IShopBillingGrain
    {
        private readonly ILogger<FlowerShopBillingGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerPendingSettlement, long> _pendingContext;
        private readonly IDataContext<FlowerEntityContext, FlowerSettlementBill, long> _billContext;
        private readonly IDataContext<FlowerEntityContext, FlowerShopWithdraw, long> _withdrawContext;
        private readonly IDataContext<FlowerEntityContext, FlowerShopAccountItem, long> _accountItemContext;
        private readonly IDataContext<FlowerEntityContext, FlowerSettlementDetail, long> _detailContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;

        public FlowerShopBillingGrain(
            ILogger<FlowerShopBillingGrain> logger,
            IDataContext<FlowerEntityContext, FlowerPendingSettlement, long> pendingContext,
            IDataContext<FlowerEntityContext, FlowerSettlementBill, long> billContext,
            IDataContext<FlowerEntityContext, FlowerShopWithdraw, long> withdrawContext,
            IDataContext<FlowerEntityContext, FlowerShopAccountItem, long> accountItemContext,
            IDataContext<FlowerEntityContext, FlowerSettlementDetail, long> detailContext,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext)
        {
            _logger = logger;
            _pendingContext = pendingContext;
            _billContext = billContext;
            _withdrawContext = withdrawContext;
            _accountItemContext = accountItemContext;
            _detailContext = detailContext;
            _orderContext = orderContext;
        }

        public async Task<PendingSettlementState> WritePendingSettlementAsync(PendingSettlementState pending)
        {
            var entity = new FlowerPendingSettlement
            {
                OrderId = pending.OrderId,
                ShopId = pending.ShopId,
                OrderAmount = pending.OrderAmount,
                PlatformCommission = pending.PlatformCommission,
                RefundAmount = pending.RefundAmount,
                SettleableAmount = pending.SettleableAmount,
                Status = 0,
                CreatedAt = DateTime.Now
            };
            var result = await _pendingContext.AddAsync(entity);
            return MapPendingToState(result);
        }

        public async Task<List<PendingSettlementState>> GetPendingSettlementsAsync(long shopId)
        {
            var entities = await _pendingContext.QueryAsync(e => e.ShopId == shopId && e.Status == 0);
            return entities.Select(MapPendingToState).ToList();
        }

        public async Task<SettlementState> SettleAsync(long shopId, DateTime periodStart, DateTime periodEnd)
        {
            var pendings = await _pendingContext.QueryAsync(e => e.ShopId == shopId && e.Status == 0 && e.CreatedAt >= periodStart && e.CreatedAt <= periodEnd);
            if (!pendings.Any()) return null;

            var totalAmount = pendings.Sum(e => e.OrderAmount);
            var totalCommission = pendings.Sum(e => e.PlatformCommission);
            var totalRefund = pendings.Sum(e => e.RefundAmount);
            var settledAmount = totalAmount - totalCommission - totalRefund;

            var bill = new FlowerSettlementBill
            {
                MerchantId = shopId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalAmount = totalAmount,
                PlatformFee = totalCommission,
                SettledAmount = settledAmount,
                Status = 0
            };
            var result = await _billContext.AddAsync(bill);

            foreach (var pending in pendings)
            {
                pending.Status = 1;
                pending.SettlementId = result.Id;
                pending.SettledAt = DateTime.Now;
                await _pendingContext.UpdateAsync(pending, pending.Id);

                var order = await _orderContext.QueryFirstOrDefaultAsync(o => o.Id == pending.OrderId);
                await _detailContext.AddAsync(new FlowerSettlementDetail
                {
                    SettlementBillId = result.Id,
                    OrderId = pending.OrderId,
                    OrderNo = order?.OrderNo ?? "",
                    OrderAmount = pending.OrderAmount,
                    PlatformCommission = pending.PlatformCommission,
                    RefundAmount = pending.RefundAmount,
                    SettleableAmount = pending.SettleableAmount
                });
            }

            var accountItem = new FlowerShopAccountItem
            {
                ShopId = shopId,
                AccountType = 0,
                Amount = settledAmount,
                Description = $"结算周期 {periodStart:yyyy-MM-dd} 至 {periodEnd:yyyy-MM-dd}",
                RelatedId = result.Id,
                CreatedAt = DateTime.Now
            };
            await _accountItemContext.AddAsync(accountItem);

            return new SettlementState
            {
                Id = result.Id,
                MerchantId = shopId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TotalAmount = totalAmount,
                PlatformFee = totalCommission,
                SettledAmount = settledAmount,
                Status = result.Status
            };
        }

        public async Task<ShopWithdrawState> RequestWithdrawAsync(ShopWithdrawState withdraw)
        {
            var entity = new FlowerShopWithdraw
            {
                ShopId = withdraw.ShopId,
                Amount = withdraw.Amount,
                BankName = withdraw.BankName,
                AccountNo = withdraw.AccountNo,
                AccountName = withdraw.AccountName,
                Status = 0,
                CreatedAt = DateTime.Now
            };
            var result = await _withdrawContext.AddAsync(entity);
            return MapWithdrawToState(result);
        }

        public async Task<ShopWithdrawState> AuditWithdrawAsync(long withdrawId, bool approved, string remark)
        {
            var entity = await _withdrawContext.QueryFirstOrDefaultAsync(e => e.Id == withdrawId);
            if (entity == null) return null;
            entity.Status = approved ? 1 : 2;
            entity.AuditRemark = remark;
            entity.AuditedAt = DateTime.Now;
            if (approved) entity.PaidAt = DateTime.Now;
            await _withdrawContext.UpdateAsync(entity, entity.Id);

            if (approved)
            {
                var accountItem = new FlowerShopAccountItem
                {
                    ShopId = entity.ShopId,
                    AccountType = 1,
                    Amount = entity.Amount,
                    Description = $"提现到 {entity.BankName} {entity.AccountNo}",
                    RelatedId = entity.Id,
                    CreatedAt = DateTime.Now
                };
                await _accountItemContext.AddAsync(accountItem);
            }

            return MapWithdrawToState(entity);
        }

        public async Task<List<ShopAccountItemState>> GetShopAccountItemsAsync(long shopId)
        {
            var entities = await _accountItemContext.QueryAsync(e => e.ShopId == shopId);
            return entities.Select(MapAccountItemToState).ToList();
        }

        public async Task<bool> RefundDeductFromPendingAsync(long orderId, decimal refundAmount)
        {
            var pending = await _pendingContext.QueryFirstOrDefaultAsync(e => e.OrderId == orderId && e.Status == 0);
            if (pending == null) return false;

            pending.RefundAmount += refundAmount;
            pending.SettleableAmount -= refundAmount;
            if (pending.SettleableAmount < 0)
            {
                _logger.LogWarning("待结算金额不足: OrderId={OrderId}, SettleableAmount={SettleableAmount}", orderId, pending.SettleableAmount);
                pending.SettleableAmount = 0;
            }
            pending.RefundDeducted = true;
            await _pendingContext.UpdateAsync(pending, pending.Id);
            return true;
        }

        public async Task<SettlementAccountSummaryState> GetSettlementAccountSummaryAsync(long shopId)
        {
            var settledBills = await _billContext.QueryAsync(e => e.MerchantId == shopId && e.Status == 1);
            var totalSettled = settledBills.Sum(e => e.SettledAmount);

            var withdraws = await _withdrawContext.QueryAsync(e => e.ShopId == shopId && (e.Status == 1 || e.Status == 3));
            var totalWithdrawn = withdraws.Sum(e => e.Amount);

            var pendingSettlements = await _pendingContext.QueryAsync(e => e.ShopId == shopId && e.Status == 0);
            var pendingSettlement = pendingSettlements.Sum(e => e.SettleableAmount);

            var frozenAmount = pendingSettlements.Where(e => e.RefundAmount > 0).Sum(e => e.RefundAmount);

            return new SettlementAccountSummaryState
            {
                MerchantId = shopId,
                TotalSettled = totalSettled,
                TotalWithdrawn = totalWithdrawn,
                AvailableBalance = totalSettled - totalWithdrawn,
                PendingSettlement = pendingSettlement,
                FrozenAmount = frozenAmount
            };
        }

        private PendingSettlementState MapPendingToState(FlowerPendingSettlement entity)
        {
            if (entity == null) return null;
            return new PendingSettlementState
            {
                Id = entity.Id,
                OrderId = entity.OrderId,
                ShopId = entity.ShopId,
                OrderAmount = entity.OrderAmount,
                PlatformCommission = entity.PlatformCommission,
                RefundAmount = entity.RefundAmount,
                SettleableAmount = entity.SettleableAmount,
                Status = entity.Status,
                SettlementId = entity.SettlementId,
                CreatedAt = entity.CreatedAt,
                SettledAt = entity.SettledAt
            };
        }

        private ShopWithdrawState MapWithdrawToState(FlowerShopWithdraw entity)
        {
            if (entity == null) return null;
            return new ShopWithdrawState
            {
                Id = entity.Id,
                ShopId = entity.ShopId,
                Amount = entity.Amount,
                BankName = entity.BankName ?? "",
                AccountNo = entity.AccountNo ?? "",
                AccountName = entity.AccountName ?? "",
                Status = entity.Status,
                AuditRemark = entity.AuditRemark ?? "",
                CreatedAt = entity.CreatedAt,
                AuditedAt = entity.AuditedAt,
                PaidAt = entity.PaidAt
            };
        }

        private ShopAccountItemState MapAccountItemToState(FlowerShopAccountItem entity)
        {
            if (entity == null) return null;
            return new ShopAccountItemState
            {
                Id = entity.Id,
                ShopId = entity.ShopId,
                AccountType = entity.AccountType,
                Amount = entity.Amount,
                BalanceAfter = entity.BalanceAfter,
                Description = entity.Description ?? "",
                RelatedId = entity.RelatedId,
                CreatedAt = entity.CreatedAt
            };
        }
    }
}
