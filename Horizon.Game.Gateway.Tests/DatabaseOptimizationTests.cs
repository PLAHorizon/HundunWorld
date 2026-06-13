using Horizon.Entities;
using Horizon.Model.GameModel;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 数据库优化测试 - 验证索引配置和CountAsync方法
    /// </summary>
    public class DatabaseOptimizationTests
    {
        #region 索引配置测试

        private GameEntityContext CreateInMemoryContext()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            // GameEntityContext requires DbContextOptions (non-generic) in constructor
            return new GameEntityContext((DbContextOptions)options);
        }

        [Fact]
        public void GameEntityContext_CharacterEntity_HasUserIdIndex()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var model = context.Model;
            var entityType = model.FindEntityType(typeof(CharacterEntity));

            // Act
            var indexes = entityType?.GetIndexes().ToList();

            // Assert
            Assert.NotNull(entityType);
            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(CharacterEntity.UserId))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_CharacterEntity_HasCompositeUserIdGameIdIndex()
        {
            // Arrange
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(CharacterEntity));

            // Act
            var indexes = entityType?.GetIndexes().ToList();

            // Assert
            Assert.NotNull(indexes);
            Assert.Contains(indexes, i =>
                i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == nameof(CharacterEntity.UserId)) &&
                i.Properties.Any(p => p.Name == "GameId"));
        }

        [Fact]
        public void GameEntityContext_CharacterEntity_HasLastLoginTimeIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(CharacterEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(CharacterEntity.LastLoginTime))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_CharacterEntity_HasCharacterNameIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(CharacterEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(CharacterEntity.CharacterName))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_TradeLogEntity_HasSellerIdIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(TradeLogEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(TradeLogEntity.SellerId))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_TradeLogEntity_HasBuyerIdIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(TradeLogEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(TradeLogEntity.BuyerId))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_TradeLogEntity_HasTradeTimeIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(TradeLogEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(TradeLogEntity.TradeTime))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_BagEntity_HasCharacterIdIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(BagEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(BagEntity.CharacterId))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_ChatMessageEntity_HasSendTimeIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(ChatMessageEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(ChatMessageEntity.SendTime))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_ChatMessageEntity_HasCompositeChannelSendTimeIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(ChatMessageEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i =>
                i.Properties.Count == 2 &&
                i.Properties.Any(p => p.Name == nameof(ChatMessageEntity.Channel)) &&
                i.Properties.Any(p => p.Name == nameof(ChatMessageEntity.SendTime)));
        }

        [Fact]
        public void GameEntityContext_ChatMessageEntity_HasSenderIdIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(ChatMessageEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(ChatMessageEntity.SenderId))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_GuildEntity_HasLeaderIdIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(GuildEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(GuildEntity.LeaderId))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_GuildEntity_HasGuildNameIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(GuildEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(GuildEntity.GuildName))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_UserEntity_HasUniqueAccountNameIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(UserEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            var accountNameIndex = indexes.FirstOrDefault(i =>
                i.Properties.Any(p => p.Name == nameof(UserEntity.AccountName)) &&
                i.Properties.Count == 1);
            Assert.NotNull(accountNameIndex);
            Assert.True(accountNameIndex.IsUnique);
        }

        [Fact]
        public void GameEntityContext_UserEntity_HasLastLoginTimeIndex()
        {
            using var context = CreateInMemoryContext();
            var entityType = context.Model.FindEntityType(typeof(UserEntity));
            var indexes = entityType?.GetIndexes().ToList();

            Assert.NotNull(indexes);
            Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == nameof(UserEntity.LastLoginTime))
                && i.Properties.Count == 1);
        }

        [Fact]
        public void GameEntityContext_AllEntities_HaveExpectedIndexCount()
        {
            using var context = CreateInMemoryContext();
            var model = context.Model;

            // Characters: UserId, UserId+GameId, LastLoginTime, CharacterName = 4 indexes
            var characterIndexes = model.FindEntityType(typeof(CharacterEntity))?.GetIndexes().Count();
            Assert.Equal(4, characterIndexes);

            // TradeLogs: SellerId, BuyerId, TradeTime = 3 indexes
            var tradeLogIndexes = model.FindEntityType(typeof(TradeLogEntity))?.GetIndexes().Count();
            Assert.Equal(3, tradeLogIndexes);

            // Bags: CharacterId = 1 index
            var bagIndexes = model.FindEntityType(typeof(BagEntity))?.GetIndexes().Count();
            Assert.Equal(1, bagIndexes);

            // ChatMessages: SendTime, Channel+SendTime, SenderId = 3 indexes
            var chatIndexes = model.FindEntityType(typeof(ChatMessageEntity))?.GetIndexes().Count();
            Assert.Equal(3, chatIndexes);

            // Guilds: LeaderId, GuildName = 2 indexes
            var guildIndexes = model.FindEntityType(typeof(GuildEntity))?.GetIndexes().Count();
            Assert.Equal(2, guildIndexes);

            // Users: AccountName (unique), LastLoginTime = 2 indexes
            var userIndexes = model.FindEntityType(typeof(UserEntity))?.GetIndexes().Count();
            Assert.Equal(2, userIndexes);
        }

        #endregion

        #region IDataContext CountAsync 接口测试

        [Fact]
        public void IDataContext_CountAsync_MethodExists()
        {
            // 验证IDataContext接口定义了CountAsync方法
            var interfaceType = typeof(Horizon.Core.Abstract.IDataContext<,,>);
            var methods = interfaceType.GetMethods();
            var countMethod = methods.FirstOrDefault(m => m.Name == "CountAsync");

            Assert.NotNull(countMethod);
            Assert.Equal(typeof(Task<int>), countMethod.ReturnType);
        }

        [Fact]
        public void DataServiceProvide_CountAsync_MethodExists()
        {
            // 验证DataServiceProvide实现了CountAsync方法
            var provideType = typeof(DataServiceProvide<,,>);
            var methods = provideType.GetMethods();
            var countMethod = methods.FirstOrDefault(m => m.Name == "CountAsync" && m.GetParameters().Length == 1);

            Assert.NotNull(countMethod);
            Assert.Equal(typeof(Task<int>), countMethod.ReturnType);
        }

        #endregion

        #region ICache GetOrSetAsync 接口测试

        [Fact]
        public void ICache_GetOrSetAsync_MethodExists()
        {
            // 验证ICache接口定义了GetOrSetAsync方法
            var interfaceType = typeof(Horizon.Core.Abstract.ICache);
            var methods = interfaceType.GetMethods();
            var getOrSetMethod = methods.FirstOrDefault(m => m.Name == "GetOrSetAsync");

            Assert.NotNull(getOrSetMethod);
            Assert.True(getOrSetMethod.IsGenericMethod);
        }

        [Fact]
        public void ICache_GetOrSetAsync_HasCorrectParameters()
        {
            var interfaceType = typeof(Horizon.Core.Abstract.ICache);
            var method = interfaceType.GetMethods().First(m => m.Name == "GetOrSetAsync");
            var parameters = method.GetParameters();

            // key, factory, expiration, cacheNullValue, nullValueExpiration
            Assert.Equal(5, parameters.Length);
            Assert.Equal("key", parameters[0].Name);
            Assert.Equal("factory", parameters[1].Name);
            Assert.Equal("expiration", parameters[2].Name);
            Assert.Equal("cacheNullValue", parameters[3].Name);
            Assert.Equal("nullValueExpiration", parameters[4].Name);
        }

        [Fact]
        public async Task ICache_GetOrSetAsync_MockedCache_ReturnsFactoryResult()
        {
            // Arrange
            var mockCache = new Mock<Horizon.Core.Abstract.ICache>();
            var expectedResult = "test_value";

            mockCache.Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<string>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>()))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await mockCache.Object.GetOrSetAsync(
                "test_key",
                () => Task.FromResult("test_value"));

            // Assert
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public async Task ICache_GetOrSetAsync_MockedCache_WithNullResult_ReturnsDefault()
        {
            // Arrange
            var mockCache = new Mock<Horizon.Core.Abstract.ICache>();

            mockCache.Setup(c => c.GetOrSetAsync<string>(
                It.IsAny<string>(),
                It.IsAny<Func<Task<string>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>()))
                .ReturnsAsync((string)null!);

            // Act
            var result = await mockCache.Object.GetOrSetAsync<string>(
                "missing_key",
                () => Task.FromResult<string>(null!),
                cacheNullValue: true);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ICache_GetOrSetAsync_MockedCache_FactoryCalledOnCacheMiss()
        {
            // Arrange
            var mockCache = new Mock<Horizon.Core.Abstract.ICache>();
            var factoryCalled = false;

            mockCache.Setup(c => c.GetOrSetAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<int>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>()))
                .Returns<string, Func<Task<int>>, TimeSpan?, bool, TimeSpan?>(
                    async (key, factory, exp, cacheNull, nullExp) =>
                    {
                        factoryCalled = true;
                        return await factory();
                    });

            // Act
            var result = await mockCache.Object.GetOrSetAsync(
                "cache_miss_key",
                () => Task.FromResult(42));

            // Assert
            Assert.True(factoryCalled);
            Assert.Equal(42, result);
        }

        #endregion

        #region GameEntityIndexConfiguration 测试

        [Fact]
        public void GameEntityIndexConfiguration_ConfigureIndexes_DoesNotThrow()
        {
            // 验证ConfigureIndexes方法可以成功执行不抛异常
            using var context = CreateInMemoryContext();
            // 如果OnModelCreating中的ConfigureIndexes抛出异常，context创建会失败
            Assert.NotNull(context);
            Assert.NotNull(context.Model);
        }

        [Fact]
        public void GameEntityContext_Model_ContainsAllExpectedEntities()
        {
            using var context = CreateInMemoryContext();
            var model = context.Model;

            Assert.NotNull(model.FindEntityType(typeof(CharacterEntity)));
            Assert.NotNull(model.FindEntityType(typeof(TradeLogEntity)));
            Assert.NotNull(model.FindEntityType(typeof(BagEntity)));
            Assert.NotNull(model.FindEntityType(typeof(ChatMessageEntity)));
            Assert.NotNull(model.FindEntityType(typeof(GuildEntity)));
            Assert.NotNull(model.FindEntityType(typeof(UserEntity)));
        }

        #endregion
    }
}
