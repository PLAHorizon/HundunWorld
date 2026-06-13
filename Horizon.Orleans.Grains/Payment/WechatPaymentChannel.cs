using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Events;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using System;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains.Payment
{
    public class WechatPaySettings
    {
        public string MerchantId { get; set; } = "";
        public string MerchantV3Secret { get; set; } = "";
        public string CertSerialNumber { get; set; } = "";
        public string PrivateKey { get; set; } = "";
        public string NotifyUrl { get; set; } = "";
        public bool IsSandbox { get; set; }
        public string AppId { get; set; } = "";
    }

    public class WechatPaymentChannel : IPaymentChannel
    {
        private readonly ILogger<WechatPaymentChannel> _logger;
        private readonly string _merchantId;
        private readonly string _merchantV3Secret;
        private readonly string _certSerialNumber;
        private readonly string _privateKey;
        private readonly string _notifyUrl;
        private readonly bool _isSandbox;
        private readonly string _appId;
        private WechatTenpayClient? _client;
        private readonly object _clientLock = new();

        public PaymentChannel ChannelType => PaymentChannel.WechatPay;

        public WechatPaymentChannel(
            ILogger<WechatPaymentChannel> logger,
            string merchantId,
            string merchantV3Secret,
            string certSerialNumber,
            string privateKey,
            string notifyUrl,
            bool isSandbox,
            string appId)
        {
            _logger = logger;
            _merchantId = merchantId;
            _merchantV3Secret = merchantV3Secret;
            _certSerialNumber = certSerialNumber;
            _privateKey = privateKey;
            _notifyUrl = notifyUrl;
            _isSandbox = isSandbox;
            _appId = appId;
        }

        private WechatTenpayClient GetClient()
        {
            if (_client != null)
                return _client;

            lock (_clientLock)
            {
                if (_client != null)
                    return _client;

                var options = new WechatTenpayClientOptions()
                {
                    MerchantId = _merchantId,
                    MerchantV3Secret = _merchantV3Secret,
                    MerchantCertificateSerialNumber = _certSerialNumber,
                    MerchantCertificatePrivateKey = _privateKey,
                    PlatformCertificateManager = new InMemoryCertificateManager()
                };

                if (_isSandbox)
                {
                    options.Endpoint = "https://api.mch.weixin.qq.com/sandboxnew/";
                }

                _client = WechatTenpayClientBuilder.Create(options).Build();
                return _client;
            }
        }

        public async Task<PrepayResult> CreatePrepayAsync(long orderId, string orderNo, decimal amount, string description, PaymentScene scene = PaymentScene.Native)
        {
            try
            {
                var client = GetClient();
                var totalFen = (int)Math.Round(amount * 100m);

                switch (scene)
                {
                    case PaymentScene.JsApi:
                    {
                        var request = new CreatePayTransactionJsapiRequest()
                        {
                            OutTradeNumber = orderNo,
                            AppId = _appId,
                            Description = description,
                            NotifyUrl = _notifyUrl,
                            Amount = new CreatePayTransactionJsapiRequest.Types.Amount()
                            {
                                Total = totalFen,
                                Currency = "CNY"
                            },
                            Payer = new CreatePayTransactionJsapiRequest.Types.Payer()
                            {
                                OpenId = description
                            }
                        };
                        var response = await client.ExecuteCreatePayTransactionJsapiAsync(request);
                        if (response.IsSuccessful())
                        {
                            return new PrepayResult
                            {
                                Success = true,
                                PrepayId = response.PrepayId ?? ""
                            };
                        }
                        return new PrepayResult
                        {
                            Success = false,
                            ErrorMessage = $"{response.ErrorCode} - {response.ErrorMessage}"
                        };
                    }
                    case PaymentScene.H5:
                    {
                        var request = new CreatePayTransactionH5Request()
                        {
                            OutTradeNumber = orderNo,
                            AppId = _appId,
                            Description = description,
                            NotifyUrl = _notifyUrl,
                            Amount = new CreatePayTransactionH5Request.Types.Amount()
                            {
                                Total = totalFen,
                                Currency = "CNY"
                            }
                        };
                        var response = await client.ExecuteCreatePayTransactionH5Async(request);
                        if (response.IsSuccessful())
                        {
                            return new PrepayResult
                            {
                                Success = true,
                                PayUrl = response.H5Url ?? ""
                            };
                        }
                        return new PrepayResult
                        {
                            Success = false,
                            ErrorMessage = $"{response.ErrorCode} - {response.ErrorMessage}"
                        };
                    }
                    case PaymentScene.App:
                    {
                        var request = new CreatePayTransactionAppRequest()
                        {
                            OutTradeNumber = orderNo,
                            AppId = _appId,
                            Description = description,
                            NotifyUrl = _notifyUrl,
                            Amount = new CreatePayTransactionAppRequest.Types.Amount()
                            {
                                Total = totalFen,
                                Currency = "CNY"
                            }
                        };
                        var response = await client.ExecuteCreatePayTransactionAppAsync(request);
                        if (response.IsSuccessful())
                        {
                            return new PrepayResult
                            {
                                Success = true,
                                PrepayId = response.PrepayId ?? ""
                            };
                        }
                        return new PrepayResult
                        {
                            Success = false,
                            ErrorMessage = $"{response.ErrorCode} - {response.ErrorMessage}"
                        };
                    }
                    default:
                    {
                        var request = new CreatePayTransactionNativeRequest()
                        {
                            OutTradeNumber = orderNo,
                            AppId = _appId,
                            Description = description,
                            NotifyUrl = _notifyUrl,
                            Amount = new CreatePayTransactionNativeRequest.Types.Amount()
                            {
                                Total = totalFen,
                                Currency = "CNY"
                            }
                        };
                        var response = await client.ExecuteCreatePayTransactionNativeAsync(request);
                        if (response.IsSuccessful())
                        {
                            return new PrepayResult
                            {
                                Success = true,
                                PrepayId = "",
                                PayUrl = response.QrcodeUrl ?? ""
                            };
                        }
                        return new PrepayResult
                        {
                            Success = false,
                            ErrorMessage = $"{response.ErrorCode} - {response.ErrorMessage}"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信支付预下单失败: OrderNo={OrderNo}, Scene={Scene}", orderNo, scene);
                return new PrepayResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public Task<PaymentCallbackResult> HandleCallbackAsync(string callbackData)
        {
            try
            {
                var client = GetClient();
                var callbackModel = client.DeserializeEvent(callbackData);
                var resource = client.DecryptEventResource<TransactionResource>(callbackModel);

                if (resource.TradeState != "SUCCESS")
                {
                    return Task.FromResult(new PaymentCallbackResult
                    {
                        Success = false,
                        RawData = callbackData
                    });
                }

                return Task.FromResult(new PaymentCallbackResult
                {
                    Success = true,
                    TransactionNo = resource.OutTradeNumber ?? "",
                    ChannelTransactionNo = resource.TransactionId ?? "",
                    Amount = resource.Amount?.Total / 100m ?? 0m,
                    PaidAt = resource.SuccessTime.DateTime,
                    RawData = callbackData,
                    NotifyId = resource.TransactionId ?? ""
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信支付回调处理失败");
                return Task.FromResult(new PaymentCallbackResult
                {
                    Success = false,
                    RawData = callbackData
                });
            }
        }

        public async Task<PaymentQueryResult> QueryPaymentStatusAsync(string transactionNo)
        {
            try
            {
                var client = GetClient();
                var request = new GetPayTransactionByOutTradeNumberRequest()
                {
                    OutTradeNumber = transactionNo,
                    MerchantId = _merchantId
                };

                var response = await client.ExecuteGetPayTransactionByOutTradeNumberAsync(request);

                if (response.IsSuccessful())
                {
                    var status = response.TradeState switch
                    {
                        "NOTPAY" => 0,
                        "SUCCESS" => 1,
                        "CLOSED" => 2,
                        "REFUND" => 3,
                        _ => 0
                    };

                    return new PaymentQueryResult
                    {
                        Success = true,
                        Status = status,
                        ChannelTransactionNo = response.TransactionId ?? "",
                        Amount = response.Amount?.Total / 100m ?? 0m,
                        PaidAt = response.SuccessTime?.DateTime
                    };
                }
                else
                {
                    return new PaymentQueryResult
                    {
                        Success = false
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信支付查询失败: TransactionNo={TransactionNo}", transactionNo);
                return new PaymentQueryResult
                {
                    Success = false
                };
            }
        }

        public async Task<bool> CloseTransactionAsync(string transactionNo)
        {
            try
            {
                var client = GetClient();
                var request = new ClosePayTransactionRequest()
                {
                    OutTradeNumber = transactionNo,
                    MerchantId = _merchantId
                };
                var response = await client.ExecuteClosePayTransactionAsync(request);
                if (response.IsSuccessful())
                {
                    _logger.LogInformation("微信关单成功: TransactionNo={TransactionNo}", transactionNo);
                    return true;
                }
                _logger.LogWarning("微信关单失败: TransactionNo={TransactionNo}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}",
                    transactionNo, response.ErrorCode, response.ErrorMessage);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信关单异常: TransactionNo={TransactionNo}", transactionNo);
                return false;
            }
        }

        public async Task<RefundResult> RefundAsync(string transactionNo, decimal refundAmount, string reason)
        {
            try
            {
                var client = GetClient();
                var outRefundNo = $"RF{DateTime.Now:yyyyMMddHHmmss}{Guid.NewGuid():N}";

                var queryRequest = new GetPayTransactionByOutTradeNumberRequest()
                {
                    OutTradeNumber = transactionNo,
                    MerchantId = _merchantId
                };
                var queryResponse = await client.ExecuteGetPayTransactionByOutTradeNumberAsync(queryRequest);

                if (!queryResponse.IsSuccessful())
                {
                    return new RefundResult
                    {
                        Success = false,
                        ErrorMessage = "查询原订单失败"
                    };
                }

                int originalTotalFen = queryResponse.Amount?.Total ?? (int)Math.Round(refundAmount * 100m);

                var request = new CreateRefundDomesticRefundRequest()
                {
                    OutTradeNumber = transactionNo,
                    OutRefundNumber = outRefundNo,
                    Reason = reason,
                    Amount = new CreateRefundDomesticRefundRequest.Types.Amount()
                    {
                        Refund = (int)Math.Round(refundAmount * 100m),
                        Total = originalTotalFen,
                        Currency = "CNY"
                    }
                };

                var response = await client.ExecuteCreateRefundDomesticRefundAsync(request);

                if (response.IsSuccessful())
                {
                    return new RefundResult
                    {
                        Success = true,
                        ChannelRefundNo = response.OutRefundNumber ?? outRefundNo
                    };
                }
                else
                {
                    return new RefundResult
                    {
                        Success = false,
                        ErrorMessage = $"{response.ErrorCode} - {response.ErrorMessage}"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信退款失败: TransactionNo={TransactionNo}", transactionNo);
                return new RefundResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
