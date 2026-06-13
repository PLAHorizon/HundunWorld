using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Grains.Payment;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerRefundService
    {
        private readonly IGrainFactory _grainFactory;
        private readonly ILogger<FlowerRefundService> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerRefundOrder, long> _refundContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long> _logContext;

        public FlowerRefundService(
            IGrainFactory grainFactory,
            ILogger<FlowerRefundService> logger,
            IDataContext<FlowerEntityContext, FlowerRefundOrder, long> refundContext,
            IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long> logContext)
        {
            _grainFactory = grainFactory;
            _logger = logger;
            _refundContext = refundContext;
            _logContext = logContext;
        }

        public async Task<FlowerRefundOrder> CreateRefundRequestAsync(
            long orderId, long paymentTransactionId, decimal refundAmount, string reason)
        {
            try
            {
                Span<byte> bytes = stackalloc byte[4];
                RandomNumberGenerator.Fill(bytes);
                var randomPart = BitConverter.ToUInt32(bytes) % 10000;
                var refundNo = $"FR{DateTime.Now:yyyyMMddHHmmss}{randomPart:D4}";

                var entity = new FlowerRefundOrder
                {
                    OrderId = orderId,
                    PaymentTransactionId = paymentTransactionId,
                    RefundNo = refundNo,
                    RefundAmount = refundAmount,
                    Reason = reason,
                    Status = (int)RefundStatus.Pending
                };

                var result = await _refundContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("创建退款申请失败: 数据库保存返回null");
                    return null;
                }

                _logger.LogInformation("创建退款申请: RefundId={RefundId}, RefundNo={RefundNo}, OrderId={OrderId}, Amount={Amount}",
                    result.Id, refundNo, orderId, refundAmount);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建退款申请失败: OrderId={OrderId}", orderId);
                throw;
            }
        }

        public async Task<bool> ApproveAndProcessRefundAsync(long refundId)
        {
            try
            {
                var entity = await _refundContext.QueryFirstOrDefaultAsync(r => r.Id == refundId);
                if (entity == null)
                {
                    _logger.LogWarning("退款单不存在: RefundId={RefundId}", refundId);
                    return false;
                }

                if (entity.Status != (int)RefundStatus.Pending)
                {
                    _logger.LogWarning("退款单状态不允许审批: RefundId={RefundId}, Status={Status}", refundId, entity.Status);
                    return false;
                }

                entity.Status = (int)RefundStatus.Approved;
                await _refundContext.UpdateAsync(entity, entity.Id);

                entity.Status = (int)RefundStatus.Processing;
                await _refundContext.UpdateAsync(entity, entity.Id);

                var paymentGrain = _grainFactory.GetGrain<IPaymentTransactionGrain>(entity.PaymentTransactionId);
                var refundResult = await paymentGrain.RefundAsync(entity.RefundAmount, entity.Reason);

                if (refundResult)
                {
                    entity.Status = (int)RefundStatus.Completed;
                    entity.RefundedAt = DateTime.Now;
                    await _refundContext.UpdateAsync(entity, entity.Id);

                    await _logContext.AddAsync(new FlowerPaymentStatusChangeLog
                    {
                        TransactionId = entity.PaymentTransactionId,
                        BeforeStatus = (int)RefundStatus.Processing,
                        AfterStatus = 3,
                        ChannelResponse = $"RefundApproved:{entity.RefundNo}",
                        ChangedAt = DateTime.Now
                    });

                    var archiveGrain = _grainFactory.GetGrain<ITradeArchiveGrain>(entity.OrderId);
                    var archiveData = System.Text.Encoding.UTF8.GetBytes(
                        $"Refund:{entity.Id}|Order:{entity.OrderId}|Amount:{entity.RefundAmount}|Reason:{entity.Reason}");
                    await archiveGrain.ArchiveRefundAsync(entity.Id, archiveData);

                    _logger.LogInformation("退款完成: RefundId={RefundId}, RefundNo={RefundNo}", refundId, entity.RefundNo);
                    return true;
                }

                entity.Status = (int)RefundStatus.Rejected;
                await _refundContext.UpdateAsync(entity, entity.Id);

                _logger.LogWarning("退款渠道调用失败: RefundId={RefundId}", refundId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "退款处理失败: RefundId={RefundId}", refundId);
                throw;
            }
        }

        public async Task<bool> HandleRefundCallbackAsync(string refundNo, string channelRefundNo, bool success)
        {
            try
            {
                var entity = await _refundContext.QueryFirstOrDefaultAsync(r => r.RefundNo == refundNo);
                if (entity == null)
                {
                    _logger.LogWarning("退款单不存在: RefundNo={RefundNo}", refundNo);
                    return false;
                }

                if (success)
                {
                    entity.Status = (int)RefundStatus.Completed;
                    entity.ChannelRefundNo = channelRefundNo;
                    entity.RefundedAt = DateTime.Now;
                }
                else
                {
                    entity.Status = (int)RefundStatus.Rejected;
                    entity.ChannelRefundNo = channelRefundNo;
                }

                await _refundContext.UpdateAsync(entity, entity.Id);

                await _logContext.AddAsync(new FlowerPaymentStatusChangeLog
                {
                    TransactionId = entity.PaymentTransactionId,
                    BeforeStatus = entity.Status,
                    AfterStatus = success ? 3 : 1,
                    ChannelResponse = $"RefundCallback:{refundNo}|Success:{success}",
                    ChangedAt = DateTime.Now
                });

                _logger.LogInformation("退款回调处理: RefundNo={RefundNo}, Success={Success}", refundNo, success);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "退款回调处理失败: RefundNo={RefundNo}", refundNo);
                return false;
            }
        }

        public async Task RejectRefundAsync(long refundId, string rejectReason)
        {
            try
            {
                var entity = await _refundContext.QueryFirstOrDefaultAsync(r => r.Id == refundId);
                if (entity == null || entity.Status != (int)RefundStatus.Pending)
                {
                    _logger.LogWarning("退款单不存在或状态不允许拒绝: RefundId={RefundId}", refundId);
                    return;
                }

                entity.Status = (int)RefundStatus.Rejected;
                await _refundContext.UpdateAsync(entity, entity.Id);

                _logger.LogInformation("退款拒绝: RefundId={RefundId}, Reason={Reason}", refundId, rejectReason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "退款拒绝失败: RefundId={RefundId}", refundId);
                throw;
            }
        }
    }
}
