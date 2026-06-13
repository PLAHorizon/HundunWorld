using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model;
using Horizon.Model.Flower;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;
using Horizon.Orleans.Interface;

namespace Horizon.PerformanceTests
{
    public class FlowerTestSiloConfigurations : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .UseInMemoryReminderService()
                .AddMemoryGrainStorage("Default")
                .AddMemoryGrainStorage("FlowerStore")
                .AddMemoryGrainStorage("AIStore")
                .ConfigureServices(services =>
                {
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerMarketSnapshot, long>, InMemoryFlowerDataContext<FlowerMarketSnapshot, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerAlertLog, long>, InMemoryFlowerDataContext<FlowerAlertLog, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerOrder, long>, InMemoryFlowerDataContext<FlowerOrder, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerOrderItem, long>, InMemoryFlowerDataContext<FlowerOrderItem, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerOrderLog, long>, InMemoryFlowerDataContext<FlowerOrderLog, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerPaymentTransaction, long>, InMemoryFlowerDataContext<FlowerPaymentTransaction, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerPaymentStatusChangeLog, long>, InMemoryFlowerDataContext<FlowerPaymentStatusChangeLog, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerProduct, long>, InMemoryFlowerDataContext<FlowerProduct, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerSettlementBill, long>, InMemoryFlowerDataContext<FlowerSettlementBill, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerRefundOrder, long>, InMemoryFlowerDataContext<FlowerRefundOrder, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerTradeArchive, long>, InMemoryFlowerDataContext<FlowerTradeArchive, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerDailyPriceStats, long>, InMemoryFlowerDataContext<FlowerDailyPriceStats, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerDataPool, long>, InMemoryFlowerDataContext<FlowerDataPool, long>>();
                    services.AddScoped<IDataContext<FlowerEntityContext, FlowerApiKey, long>, InMemoryFlowerDataContext<FlowerApiKey, long>>();
                });
        }
    }

    internal sealed class InMemoryFlowerDataContext<TEntity, TKey> : IDataContext<FlowerEntityContext, TEntity, TKey>
        where TEntity : BaseModel<TKey>
        where TKey : notnull
    {
        private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<TKey, TEntity>> Stores = new();

        public FlowerEntityContext DbCurrent => throw new NotSupportedException();
        public System.Data.IDbConnection DbConnection => throw new NotSupportedException();
        public string ConnectionStr => "in-memory";
        public DataContextType ContextType => DataContextType.SqlServer;

        private static ConcurrentDictionary<TKey, TEntity> GetStore()
            => Stores.GetOrAdd(typeof(TEntity), _ => new ConcurrentDictionary<TKey, TEntity>());

        public Task<TEntity> AddAsync(TEntity entity)
        {
            EnsureEntityId(entity);
            GetStore()[entity.Id] = entity;
            return Task.FromResult(entity);
        }

        public Task<bool> AddRangeAsync(IList<TEntity> entities)
        {
            foreach (var entity in entities) { EnsureEntityId(entity); GetStore()[entity.Id] = entity; }
            return Task.FromResult(true);
        }

        public Task<bool> UpdateAsync(TEntity entity, TKey id)
        {
            entity.Id = id;
            GetStore()[id] = entity;
            return Task.FromResult(true);
        }

        public Task<bool> UpdateRangeAsync(IList<TEntity> entities)
        {
            foreach (var entity in entities) { EnsureEntityId(entity); GetStore()[entity.Id] = entity; }
            return Task.FromResult(true);
        }

        public Task<bool> DeletedAsync<TDelEntity, TDelKey>(TDelKey id) where TDelEntity : BaseModel<TDelKey>
            => Task.FromResult(true);

        public Task<bool> DeletedsAsync<TDelEntity, TDelKey>(IList<TDelKey> ids) where TDelEntity : BaseModel<TDelKey>
            => Task.FromResult(true);

        public Task<IQueryable<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> condition, bool isTracking = false)
        {
            var predicate = condition.Compile();
            return Task.FromResult(GetStore().Values.Where(predicate).AsQueryable());
        }

        public Task<IList<TDto>> QueryAsync<TDto>(Expression<Func<TEntity, bool>> condition, Func<TEntity, TDto> selecterAction)
        {
            var predicate = condition.Compile();
            return Task.FromResult<IList<TDto>>(GetStore().Values.Where(predicate).Select(selecterAction).ToList());
        }

        public Task<TEntity> QueryFirstOrDefaultAsync(Expression<Func<TEntity, bool>> condition, bool isTracking = false)
        {
            var predicate = condition.Compile();
            return Task.FromResult(GetStore().Values.FirstOrDefault(predicate))!;
        }

        public Task<TDto> QueryFirstOrDefaultAsync<TDto>(Expression<Func<TEntity, bool>> condition, Func<TEntity, TDto> selecterAction)
        {
            var predicate = condition.Compile();
            var entity = GetStore().Values.FirstOrDefault(predicate);
            return Task.FromResult(entity == null ? default : selecterAction(entity))!;
        }

        public Task<int> CountAsync(Expression<Func<TEntity, bool>> condition)
        {
            var predicate = condition.Compile();
            return Task.FromResult(GetStore().Values.Count(predicate));
        }

        public void Dispose() { }

        private static void EnsureEntityId(TEntity entity)
        {
            if (!EqualityComparer<TKey>.Default.Equals(entity.Id, default!)) return;
            object generated = typeof(TKey) switch
            {
                var t when t == typeof(Guid) => Guid.NewGuid(),
                var t when t == typeof(long) => DateTime.UtcNow.Ticks,
                var t when t == typeof(int) => Random.Shared.Next(1, int.MaxValue),
                var t when t == typeof(string) => Guid.NewGuid().ToString("N"),
                _ => throw new InvalidOperationException($"Unsupported key type: {typeof(TKey).FullName}")
            };
            entity.Id = (TKey)generated;
        }
    }

    public class FlowerEndToEndTests : IAsyncDisposable
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
        public async Task FullPipeline_MarketSnapshot_To_Alert_To_Notification()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var marketGrain = grainFactory.GetGrain<IFlowerMarketGrain>(0);
            var snapshot = new FlowerPriceSnapshot
            {
                SpeciesId = 1,
                MarketId = 1,
                AvgPrice = 15.5m,
                MinPrice = 14.0m,
                MaxPrice = 17.0m,
                Volume = 5000,
                TradeCount = 120,
                SnapshotTime = DateTime.UtcNow
            };
            await marketGrain.UpdateSnapshotAsync(snapshot);

            var latest = await marketGrain.GetLatestSnapshotAsync(1);
            Assert.NotNull(latest);
            Assert.Equal(15.5m, latest.AvgPrice);

            var overview = await marketGrain.GetMarketOverviewAsync();
            Assert.NotNull(overview);
            Assert.True(overview.Count > 0);

            var alertGrain = grainFactory.GetGrain<IIoTAlertRuleGrain>("1");
            await alertGrain.UpdateThresholdsAsync(AlertConditionType.PriceAbove, 10.0m, true);
            await alertGrain.EvaluateAsync(new SensorReading { DeviceId = "1", Temperature = 15.5, ReadingTime = DateTime.UtcNow });

            var ruleState = await alertGrain.GetRuleStateAsync();
            Assert.True(ruleState.IsEnabled);
        }

        [Fact]
        public async Task FullPipeline_CreateOrder_To_Payment_To_Completion()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var orderGrain = grainFactory.GetGrain<IOrderGrain>(1);
            var order = new OrderState
            {
                BuyerId = Guid.NewGuid(),
                MerchantId = 200,
                TotalAmount = 99.9m,
                ShippingAddress = "昆明市斗南花卉市场",
                Items = new List<OrderItemState>
                {
                    new() { ProductId = 1, SpeciesId = 1, ProductName = "红玫瑰A级", Price = 5.0m, Quantity = 20, Subtotal = 100.0m }
                }
            };

            var createdOrder = await orderGrain.CreateOrderAsync(order);
            Assert.NotNull(createdOrder);
            Assert.Equal(OrderStatus.Pending, createdOrder.Status);

            var paid = await orderGrain.PayOrderAsync("WechatPay");
            Assert.True(paid);
            var orderState = await orderGrain.GetOrderAsync();
            Assert.Equal(OrderStatus.Paid, orderState.Status);

            var shipped = await orderGrain.ShipOrderAsync();
            Assert.True(shipped);

            var delivered = await orderGrain.DeliverOrderAsync();
            Assert.True(delivered);

            var completed = await orderGrain.CompleteOrderAsync();
            Assert.True(completed);

            var finalState = await orderGrain.GetOrderAsync();
            Assert.Equal(OrderStatus.Completed, finalState.Status);
        }

        [Fact]
        public async Task FullPipeline_PricePrediction_With_FestivalFactor()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var speciesGrain = grainFactory.GetGrain<IFlowerSpeciesGrain>(1);

            for (int i = 30; i >= 1; i--)
            {
                await speciesGrain.UpdatePriceHistoryAsync(10.0m + (decimal)(Random.Shared.NextDouble() * 5), DateTime.UtcNow.AddDays(-i));
            }

            var forecast = await speciesGrain.PredictPriceAsync(ForecastTimeScale.ShortTerm, 14);
            Assert.NotNull(forecast);
            Assert.True(forecast.PredictedPrices.Count > 0);
            Assert.True(forecast.Confidence > 0);

            foreach (var point in forecast.PredictedPrices)
            {
                Assert.True(point.UpperBound >= point.PredictedPrice);
                Assert.True(point.LowerBound <= point.PredictedPrice);
            }
        }

        [Fact]
        public async Task FullPipeline_RegionDemand_HotSpecies()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var regionGrain = grainFactory.GetGrain<IRegionDemandGrain>(1);
            await regionGrain.UpdateDemandAsync(1, 2.5, DateTime.UtcNow);
            await regionGrain.UpdateDemandAsync(2, 1.8, DateTime.UtcNow);
            await regionGrain.UpdateDemandAsync(3, 0.3, DateTime.UtcNow);

            var hotSpecies = await regionGrain.GetHotSpeciesAsync(3);
            Assert.NotNull(hotSpecies);
            Assert.Equal(3, hotSpecies.Count);
            Assert.Equal(1, hotSpecies[0]);
            Assert.Equal(2, hotSpecies[1]);
            Assert.Equal(3, hotSpecies[2]);

            var demand = await regionGrain.GetRegionalDemandAsync(1);
            Assert.NotNull(demand);
            Assert.True(demand.ContainsKey(1));
            Assert.Equal(2.5, demand[1]);
        }

        [Fact]
        public async Task FullPipeline_Order_Cancellation()
        {
            await InitializeCluster();
            var grainFactory = _cluster!.GrainFactory;

            var orderGrain = grainFactory.GetGrain<IOrderGrain>(2);
            var order = new OrderState
            {
                BuyerId = Guid.NewGuid(),
                MerchantId = 200,
                TotalAmount = 50.0m,
                Items = new List<OrderItemState>()
            };

            var created = await orderGrain.CreateOrderAsync(order);
            Assert.Equal(OrderStatus.Pending, created.Status);

            var cancelled = await orderGrain.CancelOrderAsync("不想要了");
            Assert.True(cancelled);

            var state = await orderGrain.GetOrderAsync();
            Assert.Equal(OrderStatus.Cancelled, state.Status);
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
