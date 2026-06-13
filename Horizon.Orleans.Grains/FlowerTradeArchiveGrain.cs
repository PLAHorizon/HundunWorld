using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerTradeArchiveGrain : Grain, ITradeArchiveGrain
    {
        private readonly ILogger<FlowerTradeArchiveGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerTradeArchive, long> _dataContext;

        public FlowerTradeArchiveGrain(
            ILogger<FlowerTradeArchiveGrain> logger,
            IDataContext<FlowerEntityContext, FlowerTradeArchive, long> dataContext)
        {
            _logger = logger;
            _dataContext = dataContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerTradeArchiveGrain {GrainKey} activating.", this.GetPrimaryKeyLong());
            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> ArchiveOrderAsync(long orderId, byte[] archiveData)
        {
            try
            {
                var entity = new FlowerTradeArchive
                {
                    ArchiveType = "Order",
                    RelatedId = orderId,
                    ArchiveData = archiveData,
                    ArchivedAt = DateTime.Now
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("归档订单失败: 数据库保存返回null, OrderId={OrderId}", orderId);
                    return false;
                }

                _logger.LogInformation("归档订单: OrderId={OrderId}, ArchiveId={ArchiveId}", orderId, result.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "归档订单失败: OrderId={OrderId}", orderId);
                return false;
            }
        }

        public async Task<bool> ArchivePaymentAsync(long transactionId, byte[] archiveData)
        {
            try
            {
                var entity = new FlowerTradeArchive
                {
                    ArchiveType = "Payment",
                    RelatedId = transactionId,
                    ArchiveData = archiveData,
                    ArchivedAt = DateTime.Now
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("归档支付失败: 数据库保存返回null, TransactionId={TransactionId}", transactionId);
                    return false;
                }

                _logger.LogInformation("归档支付: TransactionId={TransactionId}, ArchiveId={ArchiveId}", transactionId, result.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "归档支付失败: TransactionId={TransactionId}", transactionId);
                return false;
            }
        }

        public async Task<bool> ArchiveRefundAsync(long refundId, byte[] archiveData)
        {
            try
            {
                var entity = new FlowerTradeArchive
                {
                    ArchiveType = "Refund",
                    RelatedId = refundId,
                    ArchiveData = archiveData,
                    ArchivedAt = DateTime.Now
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("归档退款失败: 数据库保存返回null, RefundId={RefundId}", refundId);
                    return false;
                }

                _logger.LogInformation("归档退款: RefundId={RefundId}, ArchiveId={ArchiveId}", refundId, result.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "归档退款失败: RefundId={RefundId}", refundId);
                return false;
            }
        }

        public async Task<bool> ArchiveSettlementAsync(long settlementId, byte[] archiveData)
        {
            try
            {
                var entity = new FlowerTradeArchive
                {
                    ArchiveType = "Settlement",
                    RelatedId = settlementId,
                    ArchiveData = archiveData,
                    ArchivedAt = DateTime.Now
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("归档结算失败: 数据库保存返回null, SettlementId={SettlementId}", settlementId);
                    return false;
                }

                _logger.LogInformation("归档结算: SettlementId={SettlementId}, ArchiveId={ArchiveId}", settlementId, result.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "归档结算失败: SettlementId={SettlementId}", settlementId);
                return false;
            }
        }
    }
}
