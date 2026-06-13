using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Grains.Payment;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerPaymentTransactionGrain : Grain, IPaymentTransactionGrain
    {
        private readonly ILogger<FlowerPaymentTransactionGrain> _logger;
        private readonly IPersistentState<PaymentState> _paymentState;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> _dataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long> _logContext;
        private readonly IGrainFactory _grainFactory;
        private readonly IServiceProvider _serviceProvider;
        private readonly WechatPaySettings _wechatPaySettings;
        private readonly AlipaySettings _alipaySettings;

        public FlowerPaymentTransactionGrain(
            ILogger<FlowerPaymentTransactionGrain> logger,
            [PersistentState("payment", "FlowerStore")] IPersistentState<PaymentState> paymentState,
            IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> dataContext,
            IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long> logContext,
            IGrainFactory grainFactory,
            IServiceProvider serviceProvider,
            WechatPaySettings wechatPaySettings,
            AlipaySettings alipaySettings)
        {
            _logger = logger;
            _paymentState = paymentState;
            _dataContext = dataContext;
            _logContext = logContext;
            _grainFactory = grainFactory;
            _serviceProvider = serviceProvider;
            _wechatPaySettings = wechatPaySettings;
            _alipaySettings = alipaySettings;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerPaymentTransactionGrain {GrainKey} activating.", this.GetPrimaryKeyLong());
            if (!string.IsNullOrEmpty(_paymentState.State.CallbackLockKey)
                && _paymentState.State.CallbackLockedAt.HasValue
                && _paymentState.State.CallbackLockedAt.Value < DateTime.Now.AddSeconds(-30))
            {
                _paymentState.State.CallbackLockKey = "";
                _paymentState.State.CallbackLockedAt = null;
                await _paymentState.WriteStateAsync();
            }
            await base.OnActivateAsync(cancellationToken);
        }

        public Task<PaymentState> GetTransactionAsync()
        {
            return Task.FromResult(_paymentState.State);
        }

        public async Task<PaymentState> CreatePrepayAsync(long orderId, PaymentChannel channel, decimal amount, Guid buyerId, string idempotencyKey, PaymentScene scene = PaymentScene.Native)
        {
            try
            {
                var state = _paymentState.State;
                if (state.TransactionId > 0)
                {
                    if (!string.IsNullOrEmpty(idempotencyKey) && state.IdempotencyKey == idempotencyKey)
                    {
                        _logger.LogInformation("幂等键命中，返回已有交易: TransactionId={TransactionId}, IdempotencyKey={Key}", state.TransactionId, SanitizeForLog(idempotencyKey));
                        return state.Status == 0 ? state : null;
                    }
                    _logger.LogWarning("交易已存在且幂等键不匹配: TransactionId={TransactionId}", state.TransactionId);
                    return null;
                }

                var existingPending = await _dataContext.QueryAsync(t => t.OrderId == orderId && t.Status == 0);
                if (existingPending.Any())
                {
                    _logger.LogWarning("订单已存在待支付交易: OrderId={OrderId}", orderId);
                    return null;
                }

                var transactionNo = GenerateTransactionNo();

                var channelImpl = ResolveChannel(channel);
                var prepayResult = await channelImpl.CreatePrepayAsync(orderId, transactionNo, amount, "", scene);

                if (!prepayResult.Success)
                {
                    _logger.LogWarning("支付预下单失败: OrderId={OrderId}, Error={Error}", orderId, prepayResult.ErrorMessage);
                    return null;
                }

                var entity = new FlowerPaymentTransaction
                {
                    OrderId = orderId,
                    TransactionNo = transactionNo,
                    Channel = (int)channel,
                    Amount = amount,
                    Status = 0,
                    PrepayId = prepayResult.PrepayId ?? string.Empty,
                    ChannelTransactionNo = string.Empty,
                    ExpiredAt = DateTime.Now.AddMinutes(30)
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("创建支付交易失败: 数据库保存返回null");
                    return null;
                }

                _paymentState.State = new PaymentState
                {
                    TransactionId = result.Id,
                    OrderId = orderId,
                    TransactionNo = transactionNo,
                    Channel = channel,
                    Amount = amount,
                    Status = 0,
                    PrepayId = prepayResult.PrepayId ?? string.Empty,
                    ExpiredAt = entity.ExpiredAt,
                    IdempotencyKey = idempotencyKey ?? "",
                    BuyerId = buyerId
                };
                await _paymentState.WriteStateAsync();

                await WriteStatusChangeLogAsync(result.Id, -1, 0, "PrepayCreated");

                _logger.LogInformation("创建预支付: TransactionId={TransactionId}, OrderId={OrderId}, Channel={Channel}, BuyerId={BuyerId}", result.Id, orderId, channel, buyerId);

                return _paymentState.State;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建预支付失败: OrderId={OrderId}", orderId);
                throw;
            }
        }

        public async Task<bool> HandlePaymentCallbackAsync(string channelTransactionNo, string channelResponse, PaymentChannel channel)
        {
            try
            {
                var state = _paymentState.State;

                if (state.Channel != channel)
                {
                    _logger.LogWarning("回调渠道与交易锁定渠道不匹配: TransactionId={TransactionId}, LockedChannel={LockedChannel}, CallbackChannel={CallbackChannel}",
                        state.TransactionId, state.Channel, channel);
                    return false;
                }

                if (state.Status != 0)
                {
                    _logger.LogWarning("交易状态不允许回调处理(可能已处理): TransactionId={TransactionId}, Status={Status}", state.TransactionId, state.Status);
                    return true;
                }

                if (state.ExpiredAt.HasValue && state.ExpiredAt.Value < DateTime.Now)
                {
                    _logger.LogWarning("交易已过期，拒绝回调: TransactionId={TransactionId}", state.TransactionId);
                    return false;
                }

                var orderGrain = _grainFactory.GetGrain<IOrderGrain>(state.OrderId);
                var order = await orderGrain.GetOrderAsync();
                if (order == null)
                {
                    _logger.LogWarning("回调时订单不存在: OrderId={OrderId}", state.OrderId);
                    return false;
                }

                if (Math.Abs(state.Amount - order.OrderTotalAmount) > 0.01m)
                {
                    _logger.LogWarning("回调时交易金额与订单金额不一致: TransactionId={TransactionId}, TransactionAmount={TxAmount}, OrderAmount={OrderAmount}",
                        state.TransactionId, state.Amount, order.OrderTotalAmount);
                    return false;
                }

                if (order.Status != OrderStatus.Pending)
                {
                    if (order.Status == OrderStatus.Paid)
                    {
                        _logger.LogInformation("订单已支付(幂等): OrderId={OrderId}, Status={Status}", state.OrderId, order.Status);
                        return true;
                    }
                    _logger.LogWarning("回调时订单状态已变更: OrderId={OrderId}, Status={Status}", state.OrderId, order.Status);
                    return false;
                }

                var beforeStatus = state.Status;
                state.Status = 1;
                state.ChannelTransactionNo = channelTransactionNo;
                state.PaidAt = DateTime.Now;

                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.TransactionId);
                if (entity != null)
                {
                    if (entity.Status != 0)
                    {
                        _logger.LogWarning("数据库交易状态已变更(并发穿透): TransactionId={TransactionId}, DbStatus={DbStatus}", state.TransactionId, entity.Status);
                        _paymentState.State.Status = entity.Status;
                        return true;
                    }

                    entity.Status = 1;
                    entity.ChannelTransactionNo = channelTransactionNo;
                    entity.PaidAt = state.PaidAt;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                }

                await _paymentState.WriteStateAsync();
                await WriteStatusChangeLogAsync(state.TransactionId, beforeStatus, 1, channelResponse);

                try
                {
                    var payResult = await orderGrain.PayOrderAsync(channel.ToString());
                    if (!payResult)
                    {
                        _logger.LogError("订单状态更新失败(待补偿): OrderId={OrderId}", state.OrderId);
                        state.NeedsOrderSync = true;
                        await _paymentState.WriteStateAsync();
                    }
                    else
                    {
                        state.NeedsOrderSync = false;
                    }
                }
                catch (Exception payEx)
                {
                    _logger.LogError(payEx, "订单状态更新异常(待补偿): OrderId={OrderId}", state.OrderId);
                    state.NeedsOrderSync = true;
                    await _paymentState.WriteStateAsync();
                }

                try
                {
                    var buyerNotificationGrain = _grainFactory.GetGrain<INotificationGrain>(state.BuyerId);
                    await buyerNotificationGrain.PushAlertAsync(new AlertMessage
                    {
                        RuleId = 0,
                        UserId = state.BuyerId,
                        Message = $"订单{state.OrderId}支付成功，金额{state.Amount:F2}元",
                        TriggeredValue = state.Amount,
                        CreatedAt = DateTime.Now
                    });
                    var order2 = await orderGrain.GetOrderAsync();
                    if (order2 != null)
                    {
                        var merchantGrain = _grainFactory.GetGrain<IMerchantGrain>(order2.MerchantId);
                        var merchant = await merchantGrain.GetMerchantAsync();
                        if (merchant != null && merchant.UserId != Guid.Empty)
                        {
                            var merchantNotificationGrain = _grainFactory.GetGrain<INotificationGrain>(merchant.UserId);
                            await merchantNotificationGrain.PushAlertAsync(new AlertMessage
                            {
                                RuleId = 0,
                                UserId = merchant.UserId,
                                Message = $"新订单{state.OrderId}，金额{state.Amount:F2}元",
                                TriggeredValue = state.Amount,
                                CreatedAt = DateTime.Now
                            });
                        }
                    }
                }
                catch (Exception notifyEx)
                {
                    _logger.LogWarning(notifyEx, "支付成功通知发送失败: OrderId={OrderId}", state.OrderId);
                }

                _logger.LogInformation("支付回调处理完成: TransactionId={TransactionId}, ChannelTransactionNo={ChannelTransactionNo}, Channel={Channel}",
                    state.TransactionId, channelTransactionNo, channel);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付回调处理失败: TransactionId={TransactionId}", _paymentState.State.TransactionId);
                throw;
            }
        }

        public async Task<bool> ExpireTransactionAsync()
        {
            try
            {
                var state = _paymentState.State;
                if (state.Status != 0)
                {
                    _logger.LogWarning("交易状态不允许过期: TransactionId={TransactionId}, Status={Status}", state.TransactionId, state.Status);
                    return false;
                }

                var beforeStatus = state.Status;

                try
                {
                    if (Enum.IsDefined(typeof(PaymentChannel), state.Channel))
                    {
                        var channelImpl = ResolveChannel(state.Channel);
                        await channelImpl.CloseTransactionAsync(state.TransactionNo);
                    }
                }
                catch (Exception closeEx)
                {
                    _logger.LogWarning(closeEx, "渠道关单失败(仍继续本地过期): TransactionNo={TransactionNo}", state.TransactionNo);
                }

                state.Status = 2;

                var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.TransactionId);
                if (entity != null)
                {
                    if (entity.Status != 0)
                    {
                        _logger.LogWarning("过期时数据库状态已变更: TransactionId={TransactionId}, DbStatus={DbStatus}", state.TransactionId, entity.Status);
                        return false;
                    }
                    entity.Status = 2;
                    await _dataContext.UpdateAsync(entity, entity.Id);
                }

                await _paymentState.WriteStateAsync();
                await WriteStatusChangeLogAsync(state.TransactionId, beforeStatus, 2, "Expired");

                _logger.LogInformation("交易过期: TransactionId={TransactionId}", state.TransactionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "交易过期失败: TransactionId={TransactionId}", _paymentState.State.TransactionId);
                throw;
            }
        }

        public async Task<bool> RefundAsync(decimal refundAmount, string reason)
        {
            try
            {
                var state = _paymentState.State;
                if (state.Status != 1)
                {
                    _logger.LogWarning("交易状态不允许退款: TransactionId={TransactionId}, Status={Status}", state.TransactionId, state.Status);
                    return false;
                }

                if (refundAmount <= 0 || refundAmount + state.TotalRefundedAmount > state.Amount)
                {
                    _logger.LogWarning("累计退款金额超过支付金额: TransactionId={TransactionId}, RefundAmount={RefundAmount}, TotalRefunded={TotalRefunded}, PaidAmount={PaidAmount}",
                        state.TransactionId, refundAmount, state.TotalRefundedAmount, state.Amount);
                    return false;
                }

                var channelImpl = ResolveChannel(state.Channel);
                var refundResult = await channelImpl.RefundAsync(state.TransactionNo, refundAmount, reason);

                if (refundResult.Success)
                {
                    var beforeStatus = state.Status;
                    state.Status = 3;
                    state.TotalRefundedAmount += refundAmount;

                    var entity = await _dataContext.QueryFirstOrDefaultAsync(e => e.Id == state.TransactionId);
                    if (entity != null)
                    {
                        if (entity.Status != 1)
                        {
                            _logger.LogWarning("退款时数据库状态已变更: TransactionId={TransactionId}, DbStatus={DbStatus}", state.TransactionId, entity.Status);
                            return false;
                        }
                        entity.Status = 3;
                        await _dataContext.UpdateAsync(entity, entity.Id);
                    }

                    await _paymentState.WriteStateAsync();
                    await WriteStatusChangeLogAsync(state.TransactionId, beforeStatus, 3, $"Refund: {SanitizeForLog(reason)}");

                    _logger.LogInformation("交易退款: TransactionId={TransactionId}, RefundAmount={RefundAmount}", state.TransactionId, refundAmount);
                    return true;
                }

                _logger.LogWarning("交易退款失败: TransactionId={TransactionId}, Error={Error}", state.TransactionId, refundResult.ErrorMessage);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "交易退款失败: TransactionId={TransactionId}", _paymentState.State.TransactionId);
                throw;
            }
        }

        public async Task<bool> TryLockForCallbackAsync(string lockKey)
        {
            var state = _paymentState.State;
            if (!string.IsNullOrEmpty(state.CallbackLockKey))
            {
                if (state.CallbackLockedAt.HasValue && state.CallbackLockedAt.Value < DateTime.Now.AddSeconds(-30))
                {
                    _logger.LogWarning("回调锁已过期，强制释放: TransactionId={TransactionId}, OldLockKey={OldLockKey}", state.TransactionId, state.CallbackLockKey);
                }
                else
                {
                    _logger.LogWarning("回调锁已被占用: TransactionId={TransactionId}, LockKey={LockKey}", state.TransactionId, state.CallbackLockKey);
                    return false;
                }
            }

            state.CallbackLockKey = lockKey;
            state.CallbackLockedAt = DateTime.Now;
            await _paymentState.WriteStateAsync();
            return true;
        }

        public async Task ReleaseCallbackLockAsync()
        {
            var state = _paymentState.State;
            if (!string.IsNullOrEmpty(state.CallbackLockKey))
            {
                state.CallbackLockKey = "";
                state.CallbackLockedAt = null;
                await _paymentState.WriteStateAsync();
            }
        }

        public async Task ClearNeedsOrderSyncAsync()
        {
            var state = _paymentState.State;
            if (state.NeedsOrderSync)
            {
                state.NeedsOrderSync = false;
                await _paymentState.WriteStateAsync();
            }
        }

        private IPaymentChannel ResolveChannel(PaymentChannel channel)
        {
            return channel switch
            {
                PaymentChannel.WechatPay => _serviceProvider.GetRequiredService<WechatPaymentChannel>(),
                PaymentChannel.Alipay => _serviceProvider.GetRequiredService<AlipayChannel>(),
                _ => throw new ArgumentException($"不支持的支付渠道: {channel}")
            };
        }

        private async Task WriteStatusChangeLogAsync(long transactionId, int beforeStatus, int afterStatus, string channelResponse)
        {
            await _logContext.AddAsync(new FlowerPaymentStatusChangeLog
            {
                TransactionId = transactionId,
                BeforeStatus = beforeStatus,
                AfterStatus = afterStatus,
                ChannelResponse = channelResponse,
                ChangedAt = DateTime.Now
            });
        }

        private static string GenerateTransactionNo()
        {
            Span<byte> bytes = stackalloc byte[4];
            RandomNumberGenerator.Fill(bytes);
            var randomPart = BitConverter.ToUInt32(bytes) % 100000000;
            return $"FP{DateTime.Now:yyyyMMddHHmmss}{randomPart:D8}";
        }

        private static string SanitizeForLog(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
