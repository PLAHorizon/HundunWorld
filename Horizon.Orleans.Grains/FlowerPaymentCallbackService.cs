using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Grains.Payment;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerPaymentCallbackService
    {
        private readonly IGrainFactory _grainFactory;
        private readonly ILogger<FlowerPaymentCallbackService> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long> _logContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> _transactionContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly WechatPaymentChannel _wechatChannel;
        private readonly AlipayChannel _alipayChannel;

        public FlowerPaymentCallbackService(
            IGrainFactory grainFactory,
            ILogger<FlowerPaymentCallbackService> logger,
            IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long> logContext,
            IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long> transactionContext,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            WechatPaymentChannel wechatChannel,
            AlipayChannel alipayChannel)
        {
            _grainFactory = grainFactory;
            _logger = logger;
            _logContext = logContext;
            _transactionContext = transactionContext;
            _orderContext = orderContext;
            _wechatChannel = wechatChannel;
            _alipayChannel = alipayChannel;
        }

        public async Task<bool> HandleWechatCallbackAsync(string callbackData)
        {
            try
            {
                var channel = _wechatChannel;
                var result = await channel.HandleCallbackAsync(callbackData);

                if (!result.Success)
                {
                    _logger.LogWarning("微信回调验签失败");
                    return false;
                }

                return await ProcessCallbackAsync(result, PaymentChannel.WechatPay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理微信支付回调失败");
                return false;
            }
        }

        public async Task<bool> HandleAlipayCallbackAsync(Dictionary<string, string> callbackData)
        {
            try
            {
                var channel = _alipayChannel;
                var result = await channel.HandleCallbackAsync(callbackData);

                if (!result.Success)
                {
                    _logger.LogWarning("支付宝回调验签失败");
                    return false;
                }

                return await ProcessCallbackAsync(result, PaymentChannel.Alipay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理支付宝回调失败");
                return false;
            }
        }

        private async Task<bool> ProcessCallbackAsync(PaymentCallbackResult callbackResult, PaymentChannel channel)
        {
            if (string.IsNullOrEmpty(callbackResult.TransactionNo))
            {
                _logger.LogWarning("回调缺少交易号, Channel={Channel}", channel);
                return false;
            }

            if (!string.IsNullOrEmpty(callbackResult.NotifyId))
            {
                var existingNotify = await _logContext.QueryAsync(
                    l => l.NotifyId == callbackResult.NotifyId);
                if (existingNotify.Any())
                {
                    _logger.LogWarning("重复的notify_id, 已跳过: NotifyId={NotifyId}", SanitizeForLog(callbackResult.NotifyId));
                    return true;
                }
            }

            var callbackLockKey = callbackResult.TransactionNo;

            var transactionEntity = await _transactionContext.QueryFirstOrDefaultAsync(
                t => t.TransactionNo == callbackResult.TransactionNo);
            if (transactionEntity == null)
            {
                _logger.LogWarning("交易不存在: TransactionNo={TransactionNo}", SanitizeForLog(callbackResult.TransactionNo));
                return false;
            }

            var transactionGrain = _grainFactory.GetGrain<IPaymentTransactionGrain>(transactionEntity.Id);
            if (!await transactionGrain.TryLockForCallbackAsync(callbackLockKey))
            {
                _logger.LogWarning("回调正在处理中，跳过并发请求: TransactionNo={TransactionNo}", SanitizeForLog(callbackResult.TransactionNo));
                return true;
            }

            try
            {
                if (callbackResult.Amount > 0 && Math.Abs(callbackResult.Amount - transactionEntity.Amount) > 0.01m)
                {
                    _logger.LogWarning("回调金额与交易金额不匹配: TransactionNo={TransactionNo}, CallbackAmount={CallbackAmount}, TransactionAmount={TransactionAmount}",
                        SanitizeForLog(callbackResult.TransactionNo), callbackResult.Amount, transactionEntity.Amount);
                    return false;
                }

                if (transactionEntity.Channel != (int)channel)
                {
                    _logger.LogWarning("回调渠道与交易渠道不匹配: TransactionNo={TransactionNo}, CallbackChannel={CallbackChannel}, TransactionChannel={TransactionChannel}",
                        SanitizeForLog(callbackResult.TransactionNo), channel, transactionEntity.Channel);
                    return false;
                }

                if (transactionEntity.ExpiredAt.HasValue && transactionEntity.ExpiredAt.Value < DateTime.Now)
                {
                    _logger.LogWarning("交易已过期: TransactionNo={TransactionNo}", SanitizeForLog(callbackResult.TransactionNo));
                    return false;
                }

                var orderEntity = await _orderContext.QueryFirstOrDefaultAsync(o => o.Id == transactionEntity.OrderId);
                if (orderEntity == null)
                {
                    _logger.LogWarning("回调时订单不存在(数据库校验): OrderId={OrderId}", transactionEntity.OrderId);
                    return false;
                }

                if (Math.Abs(transactionEntity.Amount - orderEntity.OrderTotalAmount) > 0.01m)
                {
                    _logger.LogWarning("回调时交易金额与订单金额不一致(数据库二次校验): TransactionNo={TransactionNo}, TxAmount={TxAmount}, OrderAmount={OrderAmount}",
                        SanitizeForLog(callbackResult.TransactionNo), transactionEntity.Amount, orderEntity.OrderTotalAmount);
                    return false;
                }

                if (orderEntity.Status != (int)OrderStatus.Pending)
                {
                    _logger.LogWarning("回调时订单状态已变更(数据库校验): OrderId={OrderId}, Status={Status}", orderEntity.Id, orderEntity.Status);
                    return true;
                }

                var state = await transactionGrain.GetTransactionAsync();

                if (state == null || state.Status != 0)
                {
                    _logger.LogWarning("交易不存在或已处理: TransactionNo={TransactionNo}, Status={Status}",
                        SanitizeForLog(callbackResult.TransactionNo), state?.Status);
                    return true;
                }

                var sanitizedRawData = SanitizeSensitiveData(callbackResult.RawData);
                var channelResponseWithNotifyId = string.IsNullOrEmpty(callbackResult.NotifyId)
                    ? sanitizedRawData
                    : $"NotifyId:{callbackResult.NotifyId}|{sanitizedRawData}";
                await _logContext.AddAsync(new FlowerPaymentStatusChangeLog
                {
                    TransactionId = state.TransactionId,
                    BeforeStatus = state.Status,
                    AfterStatus = 1,
                    ChannelResponse = channelResponseWithNotifyId,
                    ChangedAt = DateTime.Now
                });

                var handled = await transactionGrain.HandlePaymentCallbackAsync(
                    callbackResult.ChannelTransactionNo,
                    $"Verified:{channel}|Amount:{callbackResult.Amount}",
                    channel);

                if (handled)
                {
                    var archiveGrain = _grainFactory.GetGrain<ITradeArchiveGrain>(state.OrderId);
                    var archiveData = System.Text.Encoding.UTF8.GetBytes(
                        $"Payment:{state.TransactionId}|Channel:{channel}|Amount:{state.Amount}|PaidAt:{callbackResult.PaidAt:O}");
                    await archiveGrain.ArchivePaymentAsync(state.TransactionId, archiveData);
                }

                _logger.LogInformation("支付回调处理完成: TransactionNo={TransactionNo}, Handled={Handled}",
                    SanitizeForLog(callbackResult.TransactionNo), handled);
                return handled;
            }
            finally
            {
                await transactionGrain.ReleaseCallbackLockAsync();
            }
        }

        private static string SanitizeSensitiveData(string rawData)
        {
            if (string.IsNullOrEmpty(rawData)) return "";
            var sanitized = rawData;
            var sensitiveKeys = new[] { "buyer_id", "buyer_logon_id", "buyer_open_id", "fund_bill_list" };
            foreach (var key in sensitiveKeys)
            {
                var pattern = $"\"{key}\":\"[^\"]*\"";
                sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, pattern, $"\"{key}\":\"***\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            return sanitized;
        }

        private static string SanitizeForLog(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Replace('\r', ' ').Replace('\n', ' ');
        }

        public async Task CompensatePendingTransactionsAsync()
        {
            try
            {
                _logger.LogInformation("开始补偿查询待处理交易");

                var expiredTransactions = await _transactionContext.QueryAsync(
                    t => t.Status == 0 && t.ExpiredAt <= DateTime.Now);
                foreach (var tx in expiredTransactions)
                {
                    try
                    {
                        var grain = _grainFactory.GetGrain<IPaymentTransactionGrain>(tx.Id);
                        await grain.ExpireTransactionAsync();

                        try
                        {
                            if (Enum.IsDefined(typeof(PaymentChannel), tx.Channel))
                            {
                                var channel = (PaymentChannel)tx.Channel;
                                var channelImpl = ResolveChannel(channel);
                                await channelImpl.CloseTransactionAsync(tx.TransactionNo);
                            }
                        }
                        catch (Exception closeEx)
                        {
                            _logger.LogWarning(closeEx, "过期交易渠道关单失败: TransactionId={TransactionId}", tx.Id);
                        }

                        _logger.LogInformation("过期交易自动关闭: TransactionId={TransactionId}", tx.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "过期交易关闭跳过: TransactionId={TransactionId}", tx.Id);
                    }
                }

                var pendingTransactions = await _transactionContext.QueryAsync(
                    t => t.Status == 0 && t.ExpiredAt > DateTime.Now);

                foreach (var tx in pendingTransactions)
                {
                    try
                    {
                        var grain = _grainFactory.GetGrain<IPaymentTransactionGrain>(tx.Id);
                        var state = await grain.GetTransactionAsync();

                        if (state == null || state.Status != 0) continue;

                        if (!Enum.IsDefined(typeof(PaymentChannel), state.Channel))
                        {
                            _logger.LogWarning("交易渠道值无效: TransactionId={TransactionId}, Channel={Channel}", state.TransactionId, state.Channel);
                            continue;
                        }

                        var channel = (PaymentChannel)state.Channel;
                        var channelImpl = ResolveChannel(channel);
                        var queryResult = await channelImpl.QueryPaymentStatusAsync(state.TransactionNo);

                        if (queryResult.Success && queryResult.Status == 1)
                        {
                            await grain.HandlePaymentCallbackAsync(
                                queryResult.ChannelTransactionNo,
                                $"Compensated:{channel}",
                                channel);
                            _logger.LogInformation("补偿成功: TransactionId={TransactionId}", state.TransactionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "补偿查询跳过: TransactionId={TransactionId}", tx.Id);
                    }
                }

                var paidTransactions = await _transactionContext.QueryAsync(
                    t => t.Status == 1);

                foreach (var tx in paidTransactions)
                {
                    try
                    {
                        var grain = _grainFactory.GetGrain<IPaymentTransactionGrain>(tx.Id);
                        var state = await grain.GetTransactionAsync();

                        if (state == null || !state.NeedsOrderSync) continue;

                        var orderGrain = _grainFactory.GetGrain<IOrderGrain>(state.OrderId);
                        var payResult = await orderGrain.PayOrderAsync(((PaymentChannel)state.Channel).ToString());

                        if (payResult)
                        {
                            state.NeedsOrderSync = false;
                            var transactionGrain = _grainFactory.GetGrain<IPaymentTransactionGrain>(tx.Id);
                            await transactionGrain.ClearNeedsOrderSyncAsync();
                            _logger.LogInformation("补偿订单同步成功: TransactionId={TransactionId}, OrderId={OrderId}", tx.Id, state.OrderId);
                        }
                        else
                        {
                            _logger.LogWarning("补偿订单同步失败(PayOrderAsync返回false): TransactionId={TransactionId}, OrderId={OrderId}", tx.Id, state.OrderId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "补偿订单同步跳过: TransactionId={TransactionId}", tx.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "补偿查询失败");
            }
        }

        private IPaymentChannel ResolveChannel(PaymentChannel channel)
        {
            return channel switch
            {
                PaymentChannel.WechatPay => _wechatChannel,
                PaymentChannel.Alipay => _alipayChannel,
                _ => throw new ArgumentException($"不支持的支付渠道: {channel}")
            };
        }
    }
}
