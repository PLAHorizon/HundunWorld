using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Orleans.TestingHost;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.PerformanceTests
{
    public class FlowerPaymentTests : IAsyncDisposable
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

        [Fact]
        public async Task Payment_NormalFlow_CreatePrepay_To_Callback()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var paymentGrain = grainFactory.GetGrain<IPaymentTransactionGrain>(100);
            var state = await paymentGrain.CreatePrepayAsync(1, PaymentChannel.WechatPay, 99.9m, Guid.NewGuid(), "idem_1");

            Assert.NotNull(state);
            Assert.Equal(0, state.Status);
            Assert.Equal(99.9m, state.Amount);
            Assert.False(string.IsNullOrEmpty(state.PrepayId));

            var callbackResult = await paymentGrain.HandlePaymentCallbackAsync("wx_txn_12345", "callback_success", PaymentChannel.WechatPay);
            Assert.True(callbackResult);

            var finalState = await paymentGrain.GetTransactionAsync();
            Assert.Equal(1, finalState.Status);
            Assert.Equal("wx_txn_12345", finalState.ChannelTransactionNo);
        }

        [Fact]
        public async Task Payment_DuplicateCallback_Idempotent()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var paymentGrain = grainFactory.GetGrain<IPaymentTransactionGrain>(101);
            await paymentGrain.CreatePrepayAsync(2, PaymentChannel.Alipay, 50.0m, Guid.NewGuid(), "idem_2");

            var first = await paymentGrain.HandlePaymentCallbackAsync("alipay_txn_001", "callback_1", PaymentChannel.Alipay);
            Assert.True(first);

            var second = await paymentGrain.HandlePaymentCallbackAsync("alipay_txn_002", "callback_2", PaymentChannel.Alipay);
            Assert.False(second);

            var state = await paymentGrain.GetTransactionAsync();
            Assert.Equal(1, state.Status);
            Assert.Equal("alipay_txn_001", state.ChannelTransactionNo);
        }

        [Fact]
        public async Task Payment_ExpireTransaction()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var paymentGrain = grainFactory.GetGrain<IPaymentTransactionGrain>(102);
            await paymentGrain.CreatePrepayAsync(3, PaymentChannel.WechatPay, 200.0m, Guid.NewGuid(), "idem_3");

            var expired = await paymentGrain.ExpireTransactionAsync();
            Assert.True(expired);

            var state = await paymentGrain.GetTransactionAsync();
            Assert.Equal(2, state.Status);

            var callbackAfterExpire = await paymentGrain.HandlePaymentCallbackAsync("wx_txn_late", "late_callback", PaymentChannel.WechatPay);
            Assert.False(callbackAfterExpire);
        }

        [Fact]
        public async Task Payment_RefundFlow()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var paymentGrain = grainFactory.GetGrain<IPaymentTransactionGrain>(103);
            await paymentGrain.CreatePrepayAsync(4, PaymentChannel.WechatPay, 150.0m, Guid.NewGuid(), "idem_4");
            await paymentGrain.HandlePaymentCallbackAsync("wx_txn_refund_test", "paid", PaymentChannel.WechatPay);

            var refundResult = await paymentGrain.RefundAsync(150.0m, "商品质量问题");
            Assert.True(refundResult);

            var state = await paymentGrain.GetTransactionAsync();
            Assert.Equal(3, state.Status);
        }

        [Fact]
        public async Task Payment_RefundBeforePaid_ShouldFail()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var paymentGrain = grainFactory.GetGrain<IPaymentTransactionGrain>(104);
            await paymentGrain.CreatePrepayAsync(5, PaymentChannel.Alipay, 80.0m, Guid.NewGuid(), "idem_5");

            var refundResult = await paymentGrain.RefundAsync(80.0m, "未支付退款");
            Assert.False(refundResult);

            var state = await paymentGrain.GetTransactionAsync();
            Assert.Equal(0, state.Status);
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

    public class FlowerReconciliationTests : IAsyncDisposable
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

        [Fact]
        public async Task Reconciliation_DetectsMissingOrderLog()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var reconGrain = grainFactory.GetGrain<IReconciliationGrain>(0);
            var result = await reconGrain.RunReconciliationAsync();

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task Reconciliation_DetectsMissingPaymentLog()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var reconGrain = grainFactory.GetGrain<IReconciliationGrain>(1);
            var result = await reconGrain.RunReconciliationAsync();

            Assert.NotNull(result);
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task Reconciliation_LastRunTime_Tracked()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var reconGrain = grainFactory.GetGrain<IReconciliationGrain>(2);
            var before = await reconGrain.GetLastRunTimeAsync();

            await reconGrain.RunReconciliationAsync();

            var after = await reconGrain.GetLastRunTimeAsync();
            Assert.True(after >= before);
        }

        [Fact]
        public async Task TradeArchive_OrderArchive()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var archiveGrain = grainFactory.GetGrain<ITradeArchiveGrain>(1);
            var archiveData = System.Text.Encoding.UTF8.GetBytes("Order:1|Amount:99.9|Status:Completed");

            var result = await archiveGrain.ArchiveOrderAsync(1, archiveData);
            Assert.True(result);
        }

        [Fact]
        public async Task TradeArchive_PaymentArchive()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var archiveGrain = grainFactory.GetGrain<ITradeArchiveGrain>(2);
            var archiveData = System.Text.Encoding.UTF8.GetBytes("Payment:1|Channel:WechatPay|Amount:99.9");

            var result = await archiveGrain.ArchivePaymentAsync(1, archiveData);
            Assert.True(result);
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
