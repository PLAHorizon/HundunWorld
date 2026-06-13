using NBomber.CSharp;
using NBomber.Contracts;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Horizon.PerformanceTests
{
    public class FlowerPerformanceTests : IAsyncDisposable
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
        public async Task Performance_MarketOverview_ConcurrentReads()
        {
            await InitializeCluster();

            var marketGrain = _cluster!.GrainFactory.GetGrain<IFlowerMarketGrain>(0);
            for (int i = 0; i < 5; i++)
            {
                await marketGrain.UpdateSnapshotAsync(new FlowerPriceSnapshot
                {
                    SpeciesId = i + 1,
                    MarketId = 1,
                    AvgPrice = 10 + i,
                    MinPrice = 8 + i,
                    MaxPrice = 12 + i,
                    Volume = 1000 * (i + 1),
                    TradeCount = 50 * (i + 1),
                    SnapshotTime = DateTime.UtcNow
                });
            }

            var scenario = Scenario.Create("market_overview_reads", async context =>
            {
                var grain = _cluster!.GrainFactory.GetGrain<IFlowerMarketGrain>(0);
                var overview = await grain.GetMarketOverviewAsync();
                return overview != null && overview.Count > 0 ? Response.Ok() : Response.Fail();
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10))
            );

            var stats = NBomberRunner.RegisterScenarios(scenario).Run();
            var scenarioStats = stats.ScenarioStats[0];
            var rps = scenarioStats.Ok.Request.RPS;

            Assert.True(rps > 50, $"Market overview RPS should be > 50, actual: {rps}");
        }

        [Fact]
        public async Task Performance_PricePrediction_ConcurrentRequests()
        {
            await InitializeCluster();

            for (int speciesId = 1; speciesId <= 5; speciesId++)
            {
                var speciesGrain = _cluster!.GrainFactory.GetGrain<IFlowerSpeciesGrain>(speciesId);
                for (int d = 30; d >= 1; d--)
                {
                    await speciesGrain.UpdatePriceHistoryAsync(10.0m + (decimal)(Random.Shared.NextDouble() * 5), DateTime.UtcNow.AddDays(-d));
                }
            }

            var scenario = Scenario.Create("price_prediction_requests", async context =>
            {
                var speciesId = Random.Shared.Next(1, 6);
                var grain = _cluster!.GrainFactory.GetGrain<IFlowerSpeciesGrain>(speciesId);
                var forecast = await grain.PredictPriceAsync(ForecastTimeScale.ShortTerm, 7);
                return forecast != null && forecast.PredictedPrices.Count > 0 ? Response.Ok() : Response.Fail();
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10))
            );

            var stats = NBomberRunner.RegisterScenarios(scenario).Run();
            var scenarioStats = stats.ScenarioStats[0];
            var rps = scenarioStats.Ok.Request.RPS;

            Assert.True(rps > 20, $"Price prediction RPS should be > 20, actual: {rps}");
        }

        [Fact]
        public async Task Performance_OrderCreation_ConcurrentWrites()
        {
            await InitializeCluster();

            var scenario = Scenario.Create("order_creation_writes", async context =>
            {
                var orderId = Random.Shared.NextInt64(1, 100000);
                var grain = _cluster!.GrainFactory.GetGrain<IOrderGrain>(orderId);
                var order = new OrderState
                {
                    BuyerId = Guid.NewGuid(),
                    MerchantId = Random.Shared.Next(1, 50),
                    TotalAmount = (decimal)(Random.Shared.NextDouble() * 500 + 10),
                    Items = new System.Collections.Generic.List<OrderItemState>()
                };
                var result = await grain.CreateOrderAsync(order);
                return result != null ? Response.Ok() : Response.Fail();
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 50, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10))
            );

            var stats = NBomberRunner.RegisterScenarios(scenario).Run();
            var scenarioStats = stats.ScenarioStats[0];
            var rps = scenarioStats.Ok.Request.RPS;

            Assert.True(rps > 20, $"Order creation RPS should be > 20, actual: {rps}");
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
