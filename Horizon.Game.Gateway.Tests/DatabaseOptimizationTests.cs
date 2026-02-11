using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model.GameModel;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 数据库优化相关测试
    /// 测试IDataContext.CountAsync接口定义、GameEntityContext索引配置、数据服务实现
    /// </summary>
    public class DatabaseOptimizationTests
    {
        #region IDataContext.CountAsync 接口测试

        [Fact]
        public void IDataContext_Has_CountAsync_Method()
        {
            var type = typeof(IDataContext<,,>);
            var method = type.GetMethod("CountAsync");

            Assert.NotNull(method);
        }

        [Fact]
        public void IDataContext_CountAsync_Returns_TaskOfInt()
        {
            var type = typeof(IDataContext<,,>);
            var method = type.GetMethod("CountAsync");

            Assert.NotNull(method);
            Assert.Equal(typeof(Task<int>), method!.ReturnType);
        }

        [Fact]
        public void IDataContext_CountAsync_Accepts_Expression_Parameter()
        {
            var type = typeof(IDataContext<,,>);
            var method = type.GetMethod("CountAsync");

            Assert.NotNull(method);
            var parameters = method!.GetParameters();
            Assert.Single(parameters);
            Assert.True(parameters[0].ParameterType.IsGenericType);
            Assert.Equal(typeof(Expression<>), parameters[0].ParameterType.GetGenericTypeDefinition());
        }

        #endregion

        #region DataServiceProvide.CountAsync 实现测试

        [Fact]
        public void DataServiceProvide_Implements_CountAsync()
        {
            var type = typeof(DataServiceProvide<,,>);
            var method = type.GetMethod("CountAsync");

            Assert.NotNull(method);
            Assert.Equal(typeof(Task<int>), method!.ReturnType);
        }

        #endregion

        #region GameEntityContext 索引配置测试

        [Fact]
        public void GameEntityContext_OnModelCreating_ConfiguresIndexes()
        {
            // 使用InMemory数据库来验证模型配置
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            // 验证CharacterEntity索引
            var characterEntity = model.FindEntityType(typeof(CharacterEntity));
            Assert.NotNull(characterEntity);

            var characterIndexes = characterEntity!.GetIndexes().ToList();
            Assert.True(characterIndexes.Count >= 2, 
                $"CharacterEntity应至少有2个索引，实际有{characterIndexes.Count}个");
        }

        [Fact]
        public void GameEntityContext_Has_Character_UserId_GameId_Index()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var characterEntity = model.FindEntityType(typeof(CharacterEntity));
            Assert.NotNull(characterEntity);

            var indexes = characterEntity!.GetIndexes().ToList();
            var compositeIndex = indexes.FirstOrDefault(i =>
                i.Properties.Any(p => p.Name == "UserId") &&
                i.Properties.Any(p => p.Name == "GameId"));

            Assert.NotNull(compositeIndex);
        }

        [Fact]
        public void GameEntityContext_Has_Character_LastLoginTime_Index()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var characterEntity = model.FindEntityType(typeof(CharacterEntity));
            Assert.NotNull(characterEntity);

            var indexes = characterEntity!.GetIndexes().ToList();
            var loginTimeIndex = indexes.FirstOrDefault(i =>
                i.Properties.Count == 1 &&
                i.Properties.Any(p => p.Name == "LastLoginTime"));

            Assert.NotNull(loginTimeIndex);
        }

        [Fact]
        public void GameEntityContext_Has_TradeLog_SellerId_Index()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var tradeLogEntity = model.FindEntityType(typeof(TradeLogEntity));
            Assert.NotNull(tradeLogEntity);

            var indexes = tradeLogEntity!.GetIndexes().ToList();
            var sellerIndex = indexes.FirstOrDefault(i =>
                i.Properties.Count == 1 &&
                i.Properties.Any(p => p.Name == "SellerId"));

            Assert.NotNull(sellerIndex);
        }

        [Fact]
        public void GameEntityContext_Has_TradeLog_BuyerId_Index()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var tradeLogEntity = model.FindEntityType(typeof(TradeLogEntity));
            Assert.NotNull(tradeLogEntity);

            var indexes = tradeLogEntity!.GetIndexes().ToList();
            var buyerIndex = indexes.FirstOrDefault(i =>
                i.Properties.Count == 1 &&
                i.Properties.Any(p => p.Name == "BuyerId"));

            Assert.NotNull(buyerIndex);
        }

        [Fact]
        public void GameEntityContext_Has_TradeLog_TradeTime_Index()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var tradeLogEntity = model.FindEntityType(typeof(TradeLogEntity));
            Assert.NotNull(tradeLogEntity);

            var indexes = tradeLogEntity!.GetIndexes().ToList();
            var tradeTimeIndex = indexes.FirstOrDefault(i =>
                i.Properties.Count == 1 &&
                i.Properties.Any(p => p.Name == "TradeTime"));

            Assert.NotNull(tradeTimeIndex);
        }

        [Fact]
        public void GameEntityContext_Has_Bag_CharacterId_Index()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var bagEntity = model.FindEntityType(typeof(BagEntity));
            Assert.NotNull(bagEntity);

            var indexes = bagEntity!.GetIndexes().ToList();
            var characterIdIndex = indexes.FirstOrDefault(i =>
                i.Properties.Count == 1 &&
                i.Properties.Any(p => p.Name == "CharacterId"));

            Assert.NotNull(characterIdIndex);
        }

        [Fact]
        public void GameEntityContext_Has_ChatMessage_SendTime_Index()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var chatEntity = model.FindEntityType(typeof(ChatMessageEntity));
            Assert.NotNull(chatEntity);

            var indexes = chatEntity!.GetIndexes().ToList();
            var sendTimeIndex = indexes.FirstOrDefault(i =>
                i.Properties.Count == 1 &&
                i.Properties.Any(p => p.Name == "SendTime"));

            Assert.NotNull(sendTimeIndex);
        }

        [Fact]
        public void GameEntityContext_Has_ChatMessage_Channel_SendTime_CompositeIndex()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var chatEntity = model.FindEntityType(typeof(ChatMessageEntity));
            Assert.NotNull(chatEntity);

            var indexes = chatEntity!.GetIndexes().ToList();
            var compositeIndex = indexes.FirstOrDefault(i =>
                i.Properties.Any(p => p.Name == "Channel") &&
                i.Properties.Any(p => p.Name == "SendTime"));

            Assert.NotNull(compositeIndex);
        }

        [Fact]
        public void GameEntityContext_Has_Guild_LeaderId_Index()
        {
            var options = new DbContextOptionsBuilder<GameEntityContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
                .Options;

            using var context = new GameEntityContext(options);
            var model = context.Model;

            var guildEntity = model.FindEntityType(typeof(GuildEntity));
            Assert.NotNull(guildEntity);

            var indexes = guildEntity!.GetIndexes().ToList();
            var leaderIndex = indexes.FirstOrDefault(i =>
                i.Properties.Count == 1 &&
                i.Properties.Any(p => p.Name == "LeaderId"));

            Assert.NotNull(leaderIndex);
        }

        #endregion
    }
}
