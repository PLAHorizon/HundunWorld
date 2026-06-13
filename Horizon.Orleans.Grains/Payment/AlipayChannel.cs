using Alipay.AopSdk.Core;
using Alipay.AopSdk.Core.Domain;
using Alipay.AopSdk.Core.Request;
using Alipay.AopSdk.Core.Util;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Payment
{
    public class AlipaySettings
    {
        public string AppId { get; set; } = "";
        public string PrivateKey { get; set; } = "";
        public string AlipayPublicKey { get; set; } = "";
        public string NotifyUrl { get; set; } = "";
        public string ReturnUrl { get; set; } = "";
        public bool IsSandbox { get; set; }
    }

    public class AlipayChannel : IPaymentChannel
    {
        private readonly ILogger<AlipayChannel> _logger;
        private DefaultAopClient? _client;
        private readonly object _clientLock = new();
        private readonly string _privateKey;
        private readonly string _alipayPublicKey;
        private readonly string _notifyUrl;
        private readonly string _returnUrl;
        private readonly string _serverUrl;
        private readonly string _appId;

        public PaymentChannel ChannelType => PaymentChannel.Alipay;

        public AlipayChannel(ILogger<AlipayChannel> logger, string appId, string privateKey, string alipayPublicKey, string notifyUrl, string returnUrl, bool isSandbox = false)
        {
            _logger = logger;
            _appId = appId;
            _privateKey = privateKey;
            _alipayPublicKey = alipayPublicKey;
            _notifyUrl = notifyUrl;
            _returnUrl = returnUrl;
            _serverUrl = isSandbox
                ? "https://openapi-sandbox.dl.alipaydev.com/gateway.do"
                : "https://openapi.alipay.com/gateway.do";
        }

        private DefaultAopClient GetClient()
        {
            if (_client != null) return _client;
            lock (_clientLock)
            {
                if (_client != null) return _client;
                _client = new DefaultAopClient(_serverUrl, _appId, _privateKey, "json", "1.0", "RSA2", _alipayPublicKey, "UTF-8");
                return _client;
            }
        }

        public async Task<PrepayResult> CreatePrepayAsync(long orderId, string orderNo, decimal amount, string description, PaymentScene scene = PaymentScene.Native)
        {
            try
            {
                switch (scene)
                {
                    case PaymentScene.Wap:
                    {
                        var request = new AlipayTradeWapPayRequest();
                        request.SetNotifyUrl(_notifyUrl);
                        request.SetReturnUrl(_returnUrl);
                        request.SetBizModel(new AlipayTradeWapPayModel
                        {
                            OutTradeNo = orderNo,
                            TotalAmount = amount.ToString("0.00"),
                            Subject = description,
                            ProductCode = "QUICK_WAP_WAY"
                        });
                        var response = await Task.Run(() => GetClient().SdkExecute(request));
                        var payUrl = $"{_serverUrl}?{response.Body}";
                        return new PrepayResult
                        {
                            Success = true,
                            PrepayId = orderNo,
                            PayUrl = payUrl
                        };
                    }
                    case PaymentScene.App:
                    {
                        var request = new AlipayTradeAppPayRequest();
                        request.SetNotifyUrl(_notifyUrl);
                        request.SetBizModel(new AlipayTradeAppPayModel
                        {
                            OutTradeNo = orderNo,
                            TotalAmount = amount.ToString("0.00"),
                            Subject = description,
                            ProductCode = "QUICK_MSECURITY_PAY"
                        });
                        var response = await Task.Run(() => GetClient().SdkExecute(request));
                        return new PrepayResult
                        {
                            Success = true,
                            PrepayId = orderNo,
                            PayUrl = response.Body
                        };
                    }
                    default:
                    {
                        var request = new AlipayTradePagePayRequest();
                        request.SetNotifyUrl(_notifyUrl);
                        request.SetReturnUrl(_returnUrl);
                        request.SetBizModel(new AlipayTradePagePayModel
                        {
                            OutTradeNo = orderNo,
                            TotalAmount = amount.ToString("0.00"),
                            Subject = description,
                            ProductCode = "FAST_INSTANT_TRADE_PAY",
                            QrPayMode = "2"
                        });
                        var response = await Task.Run(() => GetClient().SdkExecute(request));
                        var payUrl = $"{_serverUrl}?{response.Body}";
                        return new PrepayResult
                        {
                            Success = true,
                            PrepayId = orderNo,
                            PayUrl = payUrl
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付宝预下单失败: OrderNo={OrderNo}, Scene={Scene}", orderNo, scene);
                return new PrepayResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        Task<PaymentCallbackResult> IPaymentChannel.HandleCallbackAsync(string callbackData)
        {
            try
            {
                var dict = new Dictionary<string, string>();
                var json = Newtonsoft.Json.Linq.JObject.Parse(callbackData);
                foreach (var prop in json.Properties())
                {
                    dict[prop.Name] = prop.Value.ToString();
                }
                return HandleCallbackAsync(dict);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付宝回调处理异常");
                return Task.FromResult(new PaymentCallbackResult { Success = false, RawData = callbackData });
            }
        }

        public Task<PaymentCallbackResult> HandleCallbackAsync(Dictionary<string, string> dict)
        {
            try
            {
                var verified = AlipaySignature.RSACheckV1(dict, _alipayPublicKey, "UTF-8", "RSA2", false);
                if (!verified)
                {
                    _logger.LogWarning("支付宝回调签名验证失败");
                    return Task.FromResult(new PaymentCallbackResult { Success = false });
                }

                if (!dict.TryGetValue("trade_status", out var tradeStatus) || tradeStatus != "TRADE_SUCCESS")
                {
                    _logger.LogInformation("支付宝回调trade_status非TRADE_SUCCESS: {TradeStatus}", tradeStatus);
                    return Task.FromResult(new PaymentCallbackResult { Success = false });
                }

                if (dict.TryGetValue("app_id", out var callbackAppId) && callbackAppId != _appId)
                {
                    _logger.LogWarning("支付宝回调app_id不匹配: CallbackAppId={CallbackAppId}, ExpectedAppId={ExpectedAppId}", callbackAppId, _appId);
                    return Task.FromResult(new PaymentCallbackResult { Success = false });
                }

                dict.TryGetValue("out_trade_no", out var outTradeNo);
                dict.TryGetValue("trade_no", out var tradeNo);
                dict.TryGetValue("total_amount", out var totalAmountStr);
                dict.TryGetValue("gmt_payment", out var gmtPaymentStr);
                dict.TryGetValue("notify_id", out var notifyId);

                var amount = decimal.TryParse(totalAmountStr, out var amt) ? amt : 0;
                var paidAt = DateTime.TryParse(gmtPaymentStr, out var dt) ? (DateTime?)dt : null;

                return Task.FromResult(new PaymentCallbackResult
                {
                    Success = true,
                    TransactionNo = outTradeNo ?? "",
                    ChannelTransactionNo = tradeNo ?? "",
                    Amount = amount,
                    PaidAt = paidAt,
                    NotifyId = notifyId ?? ""
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付宝回调处理异常");
                return Task.FromResult(new PaymentCallbackResult { Success = false });
            }
        }

        public async Task<PaymentQueryResult> QueryPaymentStatusAsync(string transactionNo)
        {
            try
            {
                var request = new AlipayTradeQueryRequest();
                request.SetBizModel(new AlipayTradeQueryModel
                {
                    OutTradeNo = transactionNo
                });

                var response = await Task.Run(() => _client.Execute(request));
                if (response.IsError)
                {
                    _logger.LogWarning("支付宝查询失败: {Code}-{SubCode}-{SubMsg}", response.Code, response.SubCode, response.SubMsg);
                    return new PaymentQueryResult { Success = false };
                }

                var status = response.TradeStatus switch
                {
                    "WAIT_BUYER_PAY" => 0,
                    "TRADE_SUCCESS" => 1,
                    "TRADE_CLOSED" => 2,
                    _ => 0
                };

                return new PaymentQueryResult
                {
                    Success = true,
                    Status = status,
                    ChannelTransactionNo = response.TradeNo ?? "",
                    Amount = decimal.TryParse(response.TotalAmount, out var amt) ? amt : 0,
                    PaidAt = DateTime.TryParse(response.SendPayDate, out var paidAt) ? (DateTime?)paidAt : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付宝查询异常: TransactionNo={TransactionNo}", transactionNo);
                return new PaymentQueryResult { Success = false };
            }
        }

        public async Task<bool> CloseTransactionAsync(string transactionNo)
        {
            try
            {
                var request = new AlipayTradeCloseRequest();
                request.SetBizModel(new AlipayTradeCloseModel
                {
                    OutTradeNo = transactionNo
                });
                var response = await Task.Run(() => _client.Execute(request));
                if (response.IsError)
                {
                    _logger.LogWarning("支付宝关单失败: {Code}-{SubCode}-{SubMsg}", response.Code, response.SubCode, response.SubMsg);
                    return false;
                }
                _logger.LogInformation("支付宝关单成功: TransactionNo={TransactionNo}", transactionNo);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付宝关单异常: TransactionNo={TransactionNo}", transactionNo);
                return false;
            }
        }

        public async Task<RefundResult> RefundAsync(string transactionNo, decimal refundAmount, string reason)
        {
            try
            {
                var outRequestNo = $"{transactionNo}_refund_{Guid.NewGuid():N}";
                var request = new AlipayTradeRefundRequest();
                request.SetBizModel(new AlipayTradeRefundModel
                {
                    OutTradeNo = transactionNo,
                    RefundAmount = refundAmount.ToString("0.00"),
                    RefundReason = reason,
                    OutRequestNo = outRequestNo
                });

                var response = await Task.Run(() => GetClient().Execute(request));
                if (response.IsError)
                {
                    _logger.LogWarning("支付宝退款失败: {Code}-{SubCode}-{SubMsg}", response.Code, response.SubCode, response.SubMsg);
                    return new RefundResult
                    {
                        Success = false,
                        ErrorMessage = $"{response.SubCode}-{response.SubMsg}"
                    };
                }

                return new RefundResult
                {
                    Success = true,
                    ChannelRefundNo = outRequestNo
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付宝退款异常: TransactionNo={TransactionNo}", transactionNo);
                return new RefundResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
