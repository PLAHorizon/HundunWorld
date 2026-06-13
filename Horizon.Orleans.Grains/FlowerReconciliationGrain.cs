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
    public class FlowerReconciliationGrain : Grain, IReconciliationGrain
    {
        private readonly ILogger<FlowerReconciliationGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderLog, long> _orderLogContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> _paymentContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long> _paymentLogContext;
        private readonly IDataContext<FlowerEntityContext, FlowerDataPool, long> _dataPoolContext;
        private readonly IPersistentState<ReconciliationState> _state;

        public FlowerReconciliationGrain(
            ILogger<FlowerReconciliationGrain> logger,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IDataContext<FlowerEntityContext, FlowerOrderLog, long> orderLogContext,
            IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> paymentContext,
            IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long> paymentLogContext,
            IDataContext<FlowerEntityContext, FlowerDataPool, long> dataPoolContext,
            [PersistentState("reconciliation", "FlowerStore")] IPersistentState<ReconciliationState> state)
        {
            _logger = logger;
            _orderContext = orderContext;
            _orderLogContext = orderLogContext;
            _paymentContext = paymentContext;
            _paymentLogContext = paymentLogContext;
            _dataPoolContext = dataPoolContext;
            _state = state;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (_state.State.LastRunTime == default)
                _state.State.LastRunTime = DateTime.Now;

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<ReconciliationResult> RunReconciliationAsync()
        {
            var result = new ReconciliationResult
            {
                RunTime = DateTime.Now,
                Inconsistencies = new List<ReconciliationInconsistency>()
            };

            try
            {
                _logger.LogInformation("开始对账任务");

                await ReconcileOrdersAsync(result);
                await ReconcilePaymentsAsync(result);

                _state.State.LastRunTime = result.RunTime;
                _state.State.LastInconsistencyCount = result.Inconsistencies.Count;
                await _state.WriteStateAsync();

                if (result.Inconsistencies.Count > 0)
                {
                    _logger.LogWarning("对账发现 {Count} 处不一致", result.Inconsistencies.Count);
                    await LogInconsistenciesToDataPoolAsync(result);
                }
                else
                {
                    _logger.LogInformation("对账完成，数据一致");
                }

                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "对账任务失败");
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public Task<DateTime> GetLastRunTimeAsync()
        {
            return Task.FromResult(_state.State.LastRunTime);
        }

        public Task<int> GetLastInconsistencyCountAsync()
        {
            return Task.FromResult(_state.State.LastInconsistencyCount);
        }

        private async Task ReconcileOrdersAsync(ReconciliationResult result)
        {
            var since = _state.State.LastRunTime;
            var recentOrders = await _orderContext.QueryAsync(
                o => o.CreateTime >= since && o.CreateTime < DateTime.Now);
            var orderList = recentOrders.ToList();

            foreach (var order in orderList)
            {
                var logs = await _orderLogContext.QueryAsync(
                    l => l.OrderId == order.Id);
                var logList = logs.ToList();

                if (logList.Count == 0)
                {
                    result.Inconsistencies.Add(new ReconciliationInconsistency
                    {
                        EntityType = "Order",
                        EntityId = order.Id,
                        IssueType = "MissingLog",
                        Description = $"订单 {order.Id} 无对应操作日志",
                        CurrentValue = $"Status={order.Status}",
                        ExpectedValue = "至少1条OrderLog"
                    });
                    continue;
                }

                var latestLog = logList.OrderByDescending(l => l.OperatedAt).First();
                var logStatus = ExtractStatusFromLog(latestLog);

                if (logStatus.HasValue && order.Status != logStatus.Value)
                {
                    result.Inconsistencies.Add(new ReconciliationInconsistency
                    {
                        EntityType = "Order",
                        EntityId = order.Id,
                        IssueType = "StatusMismatch",
                        Description = $"订单 {order.Id} 状态不一致",
                        CurrentValue = $"Order.Status={order.Status}",
                        ExpectedValue = $"LogStatus={logStatus.Value}"
                    });

                    order.Status = logStatus.Value;
                    await _orderContext.UpdateAsync(order, order.Id);
                    _logger.LogWarning("自动修复: 订单 {OrderId} 状态从 {OldStatus} 修正为 {NewStatus}",
                        order.Id, order.Status, logStatus.Value);
                }
            }
        }

        private async Task ReconcilePaymentsAsync(ReconciliationResult result)
        {
            var since = _state.State.LastRunTime;
            var recentPayments = await _paymentContext.QueryAsync(
                p => p.CreateTime >= since && p.CreateTime < DateTime.Now);
            var paymentList = recentPayments.ToList();

            foreach (var payment in paymentList)
            {
                var logs = await _paymentLogContext.QueryAsync(
                    l => l.TransactionId == payment.Id);
                var logList = logs.ToList();

                if (logList.Count == 0)
                {
                    result.Inconsistencies.Add(new ReconciliationInconsistency
                    {
                        EntityType = "Payment",
                        EntityId = payment.Id,
                        IssueType = "MissingLog",
                        Description = $"支付交易 {payment.Id} 无对应状态变更日志",
                        CurrentValue = $"Status={payment.Status}",
                        ExpectedValue = "至少1条PaymentStatusChangeLog"
                    });
                    continue;
                }

                var latestLog = logList.OrderByDescending(l => l.ChangedAt).First();
                if (payment.Status != latestLog.AfterStatus)
                {
                    result.Inconsistencies.Add(new ReconciliationInconsistency
                    {
                        EntityType = "Payment",
                        EntityId = payment.Id,
                        IssueType = "StatusMismatch",
                        Description = $"支付交易 {payment.Id} 状态不一致",
                        CurrentValue = $"Payment.Status={payment.Status}",
                        ExpectedValue = $"LogAfterStatus={latestLog.AfterStatus}"
                    });

                    payment.Status = latestLog.AfterStatus;
                    await _paymentContext.UpdateAsync(payment, payment.Id);
                    _logger.LogWarning("自动修复: 支付交易 {TransactionId} 状态从 {OldStatus} 修正为 {NewStatus}",
                        payment.Id, payment.Status, latestLog.AfterStatus);
                }
            }
        }

        private int? ExtractStatusFromLog(FlowerOrderLog log)
        {
            return log.ActionType switch
            {
                "Created" => 0,
                "Paid" => 1,
                "Shipped" => 2,
                "Delivered" => 3,
                "Completed" => 4,
                "Cancelled" => 5,
                "Refunding" => 6,
                _ => null
            };
        }

        private async Task LogInconsistenciesToDataPoolAsync(ReconciliationResult result)
        {
            try
            {
                var dataPoolGrain = GrainFactory.GetGrain<IFlowerDataPoolGrain>(0);
                foreach (var inc in result.Inconsistencies)
                {
                    var entry = new DataPoolEntry
                    {
                        DataType = DataPoolDataType.AlertEvent,
                        DataSource = 10,
                        RawPayload = System.Convert.ToBase64String(
                            System.Text.Encoding.UTF8.GetBytes(
                                $"{inc.EntityType}:{inc.EntityId} - {inc.IssueType}: {inc.Description}")),
                        Timestamp = result.RunTime,
                        RelatedEntityId = inc.EntityId.ToString(),
                        ModelVersion = "reconciliation-v1",
                        Confidence = null
                    };
                    await dataPoolGrain.WriteAsync(entry);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记录对账不一致到DataPool失败");
            }
        }
    }

    [Serializable]
    [GenerateSerializer]
    public class ReconciliationState
    {
        [Id(0)]
        public DateTime LastRunTime { get; set; }
        [Id(1)]
        public int LastInconsistencyCount { get; set; }
    }
}
