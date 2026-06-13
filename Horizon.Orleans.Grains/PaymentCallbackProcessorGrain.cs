using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class PaymentCallbackProcessorGrain : Grain, IPaymentCallbackProcessorGrain
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PaymentCallbackProcessorGrain> _logger;

        public PaymentCallbackProcessorGrain(
            IServiceProvider serviceProvider,
            ILogger<PaymentCallbackProcessorGrain> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task<bool> ProcessAlipayCallbackAsync(Dictionary<string, string> callbackData)
        {
            try
            {
                var callbackService = GetCallbackService();
                return await callbackService.HandleAlipayCallbackAsync(callbackData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "支付宝回调Grain处理失败");
                return false;
            }
        }

        public async Task<bool> ProcessWechatCallbackAsync(string callbackData)
        {
            try
            {
                var callbackService = GetCallbackService();
                return await callbackService.HandleWechatCallbackAsync(callbackData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "微信回调Grain处理失败");
                return false;
            }
        }

        private FlowerPaymentCallbackService GetCallbackService()
        {
            return _serviceProvider.GetService(typeof(FlowerPaymentCallbackService)) as FlowerPaymentCallbackService
                ?? throw new InvalidOperationException("FlowerPaymentCallbackService未注册");
        }
    }
}
