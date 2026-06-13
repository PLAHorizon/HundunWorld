using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerSettlementGrain : Grain, ISettlementGrain
    {
        private readonly ILogger<FlowerSettlementGrain> _logger;
        private readonly IPersistentState<SettlementState> _settlementState;
        private readonly IDataContext<FlowerEntityContext, FlowerSettlementBill, long> _dataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerMerchantSettlementAccount, long> _accountContext;
        private readonly IDataContext<FlowerEntityContext, FlowerSettlementDetail, long> _detailContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPendingSettlement, long> _pendingContext;

        private const decimal PlatformFeeRate = 0.05m;
        private DateTime _lastSettlementCheck;

        public FlowerSettlementGrain(
            ILogger<FlowerSettlementGrain> logger,
            [PersistentState("settlement", "FlowerStore")] IPersistentState<SettlementState> settlementState,
            IDataContext<FlowerEntityContext, FlowerSettlementBill, long> dataContext,
            IDataContext<FlowerEntityContext, FlowerMerchantSettlementAccount, long> accountContext,
            IDataContext<FlowerEntityContext, FlowerSettlementDetail, long> detailContext,
            IDataContext<FlowerEntityContext, FlowerPendingSettlement, long> pendingContext)
        {
            _logger = logger;
            _settlementState = settlementState;
            _dataContext = dataContext;
            _accountContext = accountContext;
            _detailContext = detailContext;
            _pendingContext = pendingContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerSettlementGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            RegisterTimer(
                async _ => await OnSettlementTimerTick(),
                null,
                TimeSpan.FromMinutes(60),
                TimeSpan.FromMinutes(60));

            await base.OnActivateAsync(cancellationToken);
        }

        private async Task OnSettlementTimerTick()
        {
            try
            {
                var now = DateTime.Now;
                bool isWeeklySettlement = now.DayOfWeek == DayOfWeek.Monday && now.Hour >= 0 && now.Hour < 1;
                bool isMonthlySettlement = now.Day == 1 && now.Hour >= 0 && now.Hour < 1;

                if (!isWeeklySettlement && !isMonthlySettlement)
                    return;

                if (_lastSettlementCheck != default && _lastSettlementCheck.Date == now.Date)
                {
                    _logger.LogDebug("今日已执行过结算检查，跳过: {Date}", now.Date);
                    return;
                }

                _lastSettlementCheck = now;

                var periodEnd = now.Date;
                DateTime periodStart;
                string settlementType;

                if (isMonthlySettlement)
                {
                    periodStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                    settlementType = "月度";
                }
                else
                {
                    periodStart = now.Date.AddDays(-7);
                    settlementType = "周度";
                }

                _logger.LogInformation("触发{SettlementType}结算调度: PeriodStart={PeriodStart}, PeriodEnd={PeriodEnd}",
                    settlementType, periodStart, periodEnd);

                var pendingSettlements = await _pendingContext.QueryAsync(e => e.Status == 0);
                var shopIds = pendingSettlements.Select(e => e.ShopId).Distinct().ToList();

                if (!shopIds.Any())
                {
                    _logger.LogInformation("无待结算订单，跳过结算");
                    return;
                }

                _logger.LogInformation("发现 {ShopCount} 个店铺有待结算订单", shopIds.Count);

                foreach (var shopId in shopIds)
                {
                    try
                    {
                        var billingGrain = GrainFactory.GetGrain<IShopBillingGrain>(shopId);
                        var result = await billingGrain.SettleAsync(shopId, periodStart, periodEnd);
                        if (result != null)
                        {
                            _logger.LogInformation("店铺 {ShopId} 结算成功: BillId={BillId}, SettledAmount={SettledAmount}",
                                shopId, result.Id, result.SettledAmount);
                        }
                        else
                        {
                            _logger.LogInformation("店铺 {ShopId} 无待结算数据，跳过", shopId);
                        }
                    }
                    catch (Exception exShop)
                    {
                        _logger.LogError(exShop, "店铺 {ShopId} 结算失败", shopId);
                    }
                }

                _logger.LogInformation("{SettlementType}结算调度完成，共处理 {ShopCount} 个店铺",
                    settlementType, shopIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "结算周期自动调度执行失败");
            }
        }

        public Task<SettlementState> GetSettlementAsync()
        {
            return Task.FromResult(_settlementState.State);
        }

        public async Task<SettlementState> CreateSettlementAsync(DateTime periodStart, DateTime periodEnd)
        {
            try
            {
                var merchantId = this.GetPrimaryKeyLong();
                var totalAmount = _settlementState.State.TotalAmount;
                var platformFee = totalAmount * PlatformFeeRate;
                var settledAmount = totalAmount - platformFee;

                var entity = new FlowerSettlementBill
                {
                    MerchantId = merchantId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    TotalAmount = totalAmount,
                    PlatformFee = platformFee,
                    SettledAmount = settledAmount,
                    Status = 0
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("创建结算单失败: 数据库保存返回null");
                    return null;
                }

                _settlementState.State = new SettlementState
                {
                    MerchantId = merchantId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    TotalAmount = totalAmount,
                    PlatformFee = platformFee,
                    SettledAmount = settledAmount,
                    Status = 0
                };
                await _settlementState.WriteStateAsync();

                _logger.LogInformation("创建结算单: MerchantId={MerchantId}, TotalAmount={TotalAmount}, SettledAmount={SettledAmount}",
                    merchantId, totalAmount, settledAmount);
                return _settlementState.State;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建结算单失败: MerchantId={MerchantId}", this.GetPrimaryKeyLong());
                throw;
            }
        }

        public async Task<bool> CompleteSettlementAsync()
        {
            try
            {
                var state = _settlementState.State;
                if (state.Status != 0)
                {
                    _logger.LogWarning("结算单状态不允许完成: MerchantId={MerchantId}, Status={Status}", state.MerchantId, state.Status);
                    return false;
                }

                var entity = await _dataContext.QueryFirstOrDefaultAsync(
                    e => e.MerchantId == state.MerchantId && e.Status == 0);
                if (entity == null)
                {
                    _logger.LogWarning("结算单不存在: MerchantId={MerchantId}", state.MerchantId);
                    return false;
                }

                entity.Status = 1;
                entity.SettledAt = DateTime.Now;
                await _dataContext.UpdateAsync(entity, entity.Id);

                state.Status = 1;
                state.SettledAt = entity.SettledAt;
                await _settlementState.WriteStateAsync();

                _logger.LogInformation("完成结算: MerchantId={MerchantId}", state.MerchantId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成结算失败: MerchantId={MerchantId}", _settlementState.State.MerchantId);
                throw;
            }
        }

        public async Task<SettlementAccountState> GetSettlementAccountAsync(long merchantId)
        {
            var entity = await _accountContext.QueryFirstOrDefaultAsync(e => e.MerchantId == merchantId && e.IsDefault);
            if (entity == null) return null;
            return new SettlementAccountState
            {
                Id = entity.Id,
                MerchantId = entity.MerchantId,
                BankName = entity.BankName ?? "",
                AccountNo = entity.AccountNo ?? "",
                AccountName = entity.AccountName ?? "",
                IsDefault = entity.IsDefault
            };
        }

        public async Task<SettlementAccountState> SaveSettlementAccountAsync(long merchantId, SettlementAccountState account)
        {
            var entity = await _accountContext.QueryFirstOrDefaultAsync(e => e.MerchantId == merchantId && e.IsDefault);
            if (entity == null)
            {
                entity = new FlowerMerchantSettlementAccount
                {
                    MerchantId = merchantId,
                    BankName = account.BankName,
                    AccountNo = account.AccountNo,
                    AccountName = account.AccountName,
                    IsDefault = true
                };
                var result = await _accountContext.AddAsync(entity);
                return new SettlementAccountState
                {
                    Id = result.Id,
                    MerchantId = result.MerchantId,
                    BankName = result.BankName ?? "",
                    AccountNo = result.AccountNo ?? "",
                    AccountName = result.AccountName ?? "",
                    IsDefault = result.IsDefault
                };
            }
            entity.BankName = account.BankName;
            entity.AccountNo = account.AccountNo;
            entity.AccountName = account.AccountName;
            await _accountContext.UpdateAsync(entity, entity.Id);
            return new SettlementAccountState
            {
                Id = entity.Id,
                MerchantId = entity.MerchantId,
                BankName = entity.BankName ?? "",
                AccountNo = entity.AccountNo ?? "",
                AccountName = entity.AccountName ?? "",
                IsDefault = entity.IsDefault
            };
        }

        public async Task<List<SettlementState>> GetSettlementBillsAsync(long merchantId, int skip, int take)
        {
            var entities = await _dataContext.QueryAsync(e => e.MerchantId == merchantId);
            return entities.OrderByDescending(e => e.Id).Skip(skip).Take(take).Select(e => new SettlementState
            {
                Id = e.Id,
                MerchantId = e.MerchantId,
                PeriodStart = e.PeriodStart,
                PeriodEnd = e.PeriodEnd,
                TotalAmount = e.TotalAmount,
                PlatformFee = e.PlatformFee,
                SettledAmount = e.SettledAmount,
                Status = e.Status
            }).ToList();
        }

        public async Task<List<SettlementDetailState>> GetSettlementDetailsAsync(long settlementBillId)
        {
            var entities = await _detailContext.QueryAsync(e => e.SettlementBillId == settlementBillId);
            return entities.Select(e => new SettlementDetailState
            {
                Id = e.Id,
                SettlementBillId = e.SettlementBillId,
                OrderId = e.OrderId,
                OrderNo = e.OrderNo ?? "",
                OrderAmount = e.OrderAmount,
                PlatformCommission = e.PlatformCommission,
                RefundAmount = e.RefundAmount,
                SettleableAmount = e.SettleableAmount
            }).ToList();
        }

        public async Task<SettlementAccountSummaryState> GetAccountSummaryAsync(long merchantId)
        {
            var billingGrain = GrainFactory.GetGrain<IShopBillingGrain>(merchantId);
            return await billingGrain.GetSettlementAccountSummaryAsync(merchantId);
        }
    }
}
