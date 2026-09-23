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
                // 商户配置完整性校验：密钥缺失时给出明确错误（而非底层签名异常）
                if (string.IsNullOrEmpty(_merchantId) || string.IsNullOrEmpty(_privateKey)
                    || string.IsNullOrEmpty(_certSerialNumber) || string.IsNullOrEmpty(_appId))
                {
                    return new PrepayResult
                    {
                        Success = false,
                        ErrorMessage = "微信支付商户配置缺失（MerchantId/AppId/证书序列号/商户私钥），请检查 Silo 端 WechatPaySettings 配置"
                    };
                }

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
                            var prepayId = response.PrepayId ?? "";
                            if (string.IsNullOrEmpty(prepayId))
                            {
                                return new PrepayResult
                                {
                                    Success = false,
                                    ErrorMessage = "微信 App 下单成功但未返回 prepayId"
                                };
                            }

                            // 生成客户端 SDK 拉起参数（微信 v3 App 支付规范：服务端 RSA-SHA256 二次签名）
                            var payParams = BuildAppPayParams(prepayId);
                            return new PrepayResult
                            {
                                Success = true,
                                PrepayId = prepayId,
                                PayParams = payParams
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
        /// <summary>
        /// 生成微信 App 支付客户端拉起参数（JSON）。
        /// 签名规范（微信支付 v3）：SHA256withRSA(appId\ntimeStamp\nnonceStr\nprepayId\n)，商户私钥签名。
        /// </summary>
        private string BuildAppPayParams(string prepayId)
        {
            var timeStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var nonceStr = Guid.NewGuid().ToString("N").Substring(0, 16);
            var message = $"{_appId}\n{timeStamp}\n{nonceStr}\n{prepayId}\n";
            var sign = SignWithMerchantPrivateKey(message);

            // 转义 JSON 特殊字符后手工拼装（避免额外 JSON 依赖）
            return $"{{\"appId\":\"{EscapeJson(_appId)}\",\"partnerId\":\"{EscapeJson(_merchantId)}\","
                 + $"\"prepayId\":\"{EscapeJson(prepayId)}\",\"nonceStr\":\"{EscapeJson(nonceStr)}\","
                 + $"\"timeStamp\":\"{timeStamp}\",\"package\":\"Sign=WXPay\","
                 + $"\"sign\":\"{EscapeJson(sign)}\",\"signType\":\"RSA\"}}";
        }

        /// <summary>商户私钥 SHA256withRSA 签名（Base64）。私钥支持 PEM（含头尾）与纯 Base64（PKCS#8）两种配置格式。</summary>
        private string SignWithMerchantPrivateKey(string message)
        {
            using var rsa = System.Security.Cryptography.RSA.Create();
            var key = _privateKey.Trim();
            if (key.Contains("BEGIN"))
            {
                rsa.ImportFromPem(key);
            }
            else
            {
                // 纯 Base64 PKCS#8：补 PEM 头尾后导入
                var pem = "-----BEGIN PRIVATE KEY-----\n" + key + "\n-----END PRIVATE KEY-----";
                rsa.ImportFromPem(pem);
            }
            var data = System.Text.Encoding.UTF8.GetBytes(message);
            var signature = rsa.SignData(data,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(signature);
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
