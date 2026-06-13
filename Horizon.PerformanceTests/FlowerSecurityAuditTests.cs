using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Orleans.TestingHost;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.PerformanceTests
{
    public class FlowerSecurityAuditTests : IAsyncDisposable
    {
        private TestCluster? _cluster;

        private async Task InitializeCluster()
        {
            if (_cluster != null) return;
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<FlowerTestSiloConfigurations>();
            _cluster = builder.Build();
            await _cluster.DeployAsync();
        }

        //[Fact]
        //public async Task Security_ApiKeyValidation_InvalidKeyRejected()
        //{
        //    var service = new FlowerAICostControlService(
        //        Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole()).CreateLogger<FlowerAICostControlService>(),
        //        null!);

        //    Assert.False(service.ContainsSensitiveContent("今天红玫瑰均价多少？"));
        //    Assert.True(service.ContainsSensitiveContent("如何制作暴力工具？"));
        //}

        //[Fact]
        //public async Task Security_SensitiveWordFiltering()
        //{
        //    var service = new FlowerAICostControlService(
        //        Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole()).CreateLogger<FlowerAICostControlService>(),
        //        null!);

        //    var filtered = service.FilterSensitiveContent("如何制作暴力工具");
        //    Assert.DoesNotContain("暴力", filtered);
        //    Assert.Contains("***", filtered);
        //}

        //[Fact]
        //public async Task Security_RateLimiting_Enforced()
        //{
        //    var service = new FlowerAICostControlService(
        //        Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole()).CreateLogger<FlowerAICostControlService>(),
        //        null!);

        //    var userId = Guid.NewGuid();
        //    for (int i = 0; i < 50; i++)
        //    {
        //        Assert.True(service.CheckRateLimit(userId));
        //    }

        //    Assert.False(service.CheckRateLimit(userId));
        //    Assert.Equal(0, service.GetRemainingCalls(userId));
        //}

        //[Fact]
        //public async Task Security_RateLimiting_ResetsNextDay()
        //{
        //    var service = new FlowerAICostControlService(
        //        Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole()).CreateLogger<FlowerAICostControlService>(),
        //        null!);

        //    var userId = Guid.NewGuid();
        //    for (int i = 0; i < 50; i++)
        //        service.CheckRateLimit(userId);

        //    Assert.Equal(0, service.GetRemainingCalls(userId));
        //}

        //[Fact]
        //public async Task Security_ApiKeyService_GeneratesValidKeys()
        //{
        //    var liteKey = Horizon.WebApi.Middleware.ApiKeyService.GenerateApiKey("lite");
        //    Assert.StartsWith("fk_l_", liteKey);
        //    Assert.True(liteKey.Length > 32);

        //    var proKey = Horizon.WebApi.Middleware.ApiKeyService.GenerateApiKey("pro");
        //    Assert.StartsWith("fk_p_", proKey);

        //    var teamKey = Horizon.WebApi.Middleware.ApiKeyService.GenerateApiKey("team");
        //    Assert.StartsWith("fk_t_", teamKey);
        //}

        //[Fact]
        //public async Task Security_ApiKeyService_PlanRateLimits()
        //{
        //    Assert.Equal(60, Horizon.WebApi.Middleware.ApiKeyService.GetRateLimitForPlan("lite"));
        //    Assert.Equal(300, Horizon.WebApi.Middleware.ApiKeyService.GetRateLimitForPlan("team"));
        //    Assert.Equal(600, Horizon.WebApi.Middleware.ApiKeyService.GetRateLimitForPlan("pro"));
        //    Assert.Equal(30, Horizon.WebApi.Middleware.ApiKeyService.GetRateLimitForPlan("unknown"));
        //}

        [Fact]
        public async Task Security_AnomalyAttribution_NoSensitiveDataLeak()
        {
            var attribution = FlowerAnomalyAttributionService.GenerateAttribution(1, 25.0m, null);
            Assert.DoesNotContain("Exception", attribution);
            Assert.DoesNotContain("Stack", attribution);
            Assert.Contains("红玫瑰", attribution);
        }

        [Fact]
        public async Task Security_PaymentStateTransition_InvalidTransitionsBlocked()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var paymentGrain = grainFactory.GetGrain<IPaymentTransactionGrain>(200);
            await paymentGrain.CreatePrepayAsync(10, PaymentChannel.WechatPay, 100.0m, Guid.NewGuid(), "idem_sec_10");

            var refundBeforePaid = await paymentGrain.RefundAsync(100.0m, "test");
            Assert.False(refundBeforePaid);

            var expireResult = await paymentGrain.ExpireTransactionAsync();
            Assert.True(expireResult);

            var callbackAfterExpire = await paymentGrain.HandlePaymentCallbackAsync("late", "late", PaymentChannel.WechatPay);
            Assert.False(callbackAfterExpire);
        }

        [Fact]
        public async Task Security_OrderStateTransition_InvalidTransitionsBlocked()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var orderGrain = grainFactory.GetGrain<IOrderGrain>(300);
            var order = new OrderState
            {
                BuyerId = Guid.NewGuid(),
                MerchantId = 1,
                TotalAmount = 100,
                Items = new System.Collections.Generic.List<OrderItemState>()
            };
            await orderGrain.CreateOrderAsync(order);

            var deliverBeforePay = await orderGrain.DeliverOrderAsync();
            Assert.False(deliverBeforePay);

            var completeBeforePay = await orderGrain.CompleteOrderAsync();
            Assert.False(completeBeforePay);
        }

        public async ValueTask DisposeAsync()
        {
            if (_cluster != null)
            {
                await _cluster.StopAllSilosAsync();
                _cluster.Dispose();
            }
        }
    }
}
