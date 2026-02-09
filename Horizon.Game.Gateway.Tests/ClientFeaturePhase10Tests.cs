using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// 客户端功能集成测试 - 第十阶段
    /// 测试交易、邮件、任务、副本、成就、排行榜客户端集成消息类型和DTO
    /// </summary>
    public class ClientFeaturePhase10Tests
    {
        #region MessageType Tests - 新增消息类型

        [Fact]
        public void MessageType_TradeRequest_HasCorrectValue()
        {
            Assert.Equal(1373, (int)MessageType.TradeRequest);
        }

        [Fact]
        public void MessageType_TradeResponse_HasCorrectValue()
        {
            Assert.Equal(1374, (int)MessageType.TradeResponse);
        }

        [Fact]
        public void MessageType_TradeUpdateNotify_HasCorrectValue()
        {
            Assert.Equal(1375, (int)MessageType.TradeUpdateNotify);
        }

        [Fact]
        public void MessageType_MarketListRequest_HasCorrectValue()
        {
            Assert.Equal(1376, (int)MessageType.MarketListRequest);
        }

        [Fact]
        public void MessageType_MarketSearchRequest_HasCorrectValue()
        {
            Assert.Equal(1377, (int)MessageType.MarketSearchRequest);
        }

        [Fact]
        public void MessageType_MarketSearchResponse_HasCorrectValue()
        {
            Assert.Equal(1378, (int)MessageType.MarketSearchResponse);
        }

        [Fact]
        public void MessageType_MailListRequest_HasCorrectValue()
        {
            Assert.Equal(1379, (int)MessageType.MailListRequest);
        }

        [Fact]
        public void MessageType_MailListResponse_HasCorrectValue()
        {
            Assert.Equal(1380, (int)MessageType.MailListResponse);
        }

        [Fact]
        public void MessageType_MailOperation_HasCorrectValue()
        {
            Assert.Equal(1381, (int)MessageType.MailOperation);
        }

        [Fact]
        public void MessageType_MailNotify_HasCorrectValue()
        {
            Assert.Equal(1382, (int)MessageType.MailNotify);
        }

        [Fact]
        public void MessageType_QuestListRequest_HasCorrectValue()
        {
            Assert.Equal(1383, (int)MessageType.QuestListRequest);
        }

        [Fact]
        public void MessageType_QuestListResponse_HasCorrectValue()
        {
            Assert.Equal(1384, (int)MessageType.QuestListResponse);
        }

        [Fact]
        public void MessageType_QuestProgressNotify_HasCorrectValue()
        {
            Assert.Equal(1385, (int)MessageType.QuestProgressNotify);
        }

        [Fact]
        public void MessageType_DungeonEnterRequest_HasCorrectValue()
        {
            Assert.Equal(1386, (int)MessageType.DungeonEnterRequest);
        }

        [Fact]
        public void MessageType_DungeonStatusNotify_HasCorrectValue()
        {
            Assert.Equal(1387, (int)MessageType.DungeonStatusNotify);
        }

        [Fact]
        public void MessageType_AchievementUnlockNotify_HasCorrectValue()
        {
            Assert.Equal(1388, (int)MessageType.AchievementUnlockNotify);
        }

        [Fact]
        public void MessageType_AchievementListResponse_HasCorrectValue()
        {
            Assert.Equal(1389, (int)MessageType.AchievementListResponse);
        }

        [Fact]
        public void MessageType_RankingQueryRequest_HasCorrectValue()
        {
            Assert.Equal(1390, (int)MessageType.RankingQueryRequest);
        }

        [Fact]
        public void MessageType_RankingQueryResponse_HasCorrectValue()
        {
            Assert.Equal(1391, (int)MessageType.RankingQueryResponse);
        }

        [Fact]
        public void MessageType_Phase10Types_AreUnique()
        {
            var values = new[]
            {
                (int)MessageType.TradeRequest,
                (int)MessageType.TradeResponse,
                (int)MessageType.TradeUpdateNotify,
                (int)MessageType.MarketListRequest,
                (int)MessageType.MarketSearchRequest,
                (int)MessageType.MarketSearchResponse,
                (int)MessageType.MailListRequest,
                (int)MessageType.MailListResponse,
                (int)MessageType.MailOperation,
                (int)MessageType.MailNotify,
                (int)MessageType.QuestListRequest,
                (int)MessageType.QuestListResponse,
                (int)MessageType.QuestProgressNotify,
                (int)MessageType.DungeonEnterRequest,
                (int)MessageType.DungeonStatusNotify,
                (int)MessageType.AchievementUnlockNotify,
                (int)MessageType.AchievementListResponse,
                (int)MessageType.RankingQueryRequest,
                (int)MessageType.RankingQueryResponse
            };

            Assert.Equal(values.Length, values.Distinct().Count());
        }

        [Fact]
        public void MessageType_Phase10Types_DoNotConflictWithPreviousPhases()
        {
            var phase9Max = (int)MessageType.ChatChannelLeave; // 1372
            Assert.True((int)MessageType.TradeRequest > phase9Max);
            Assert.True((int)MessageType.TradeResponse > phase9Max);
            Assert.True((int)MessageType.TradeUpdateNotify > phase9Max);
            Assert.True((int)MessageType.MarketListRequest > phase9Max);
            Assert.True((int)MessageType.MarketSearchRequest > phase9Max);
            Assert.True((int)MessageType.MarketSearchResponse > phase9Max);
            Assert.True((int)MessageType.MailListRequest > phase9Max);
            Assert.True((int)MessageType.MailListResponse > phase9Max);
            Assert.True((int)MessageType.MailOperation > phase9Max);
            Assert.True((int)MessageType.MailNotify > phase9Max);
            Assert.True((int)MessageType.QuestListRequest > phase9Max);
            Assert.True((int)MessageType.QuestListResponse > phase9Max);
            Assert.True((int)MessageType.QuestProgressNotify > phase9Max);
            Assert.True((int)MessageType.DungeonEnterRequest > phase9Max);
            Assert.True((int)MessageType.DungeonStatusNotify > phase9Max);
            Assert.True((int)MessageType.AchievementUnlockNotify > phase9Max);
            Assert.True((int)MessageType.AchievementListResponse > phase9Max);
            Assert.True((int)MessageType.RankingQueryRequest > phase9Max);
            Assert.True((int)MessageType.RankingQueryResponse > phase9Max);
        }

        [Fact]
        public void MessageType_Phase10Types_AreSequential()
        {
            Assert.Equal((int)MessageType.TradeRequest + 1, (int)MessageType.TradeResponse);
            Assert.Equal((int)MessageType.TradeResponse + 1, (int)MessageType.TradeUpdateNotify);
            Assert.Equal((int)MessageType.TradeUpdateNotify + 1, (int)MessageType.MarketListRequest);
            Assert.Equal((int)MessageType.MarketListRequest + 1, (int)MessageType.MarketSearchRequest);
            Assert.Equal((int)MessageType.MarketSearchRequest + 1, (int)MessageType.MarketSearchResponse);
            Assert.Equal((int)MessageType.MarketSearchResponse + 1, (int)MessageType.MailListRequest);
            Assert.Equal((int)MessageType.MailListRequest + 1, (int)MessageType.MailListResponse);
            Assert.Equal((int)MessageType.MailListResponse + 1, (int)MessageType.MailOperation);
            Assert.Equal((int)MessageType.MailOperation + 1, (int)MessageType.MailNotify);
            Assert.Equal((int)MessageType.MailNotify + 1, (int)MessageType.QuestListRequest);
            Assert.Equal((int)MessageType.QuestListRequest + 1, (int)MessageType.QuestListResponse);
            Assert.Equal((int)MessageType.QuestListResponse + 1, (int)MessageType.QuestProgressNotify);
            Assert.Equal((int)MessageType.QuestProgressNotify + 1, (int)MessageType.DungeonEnterRequest);
            Assert.Equal((int)MessageType.DungeonEnterRequest + 1, (int)MessageType.DungeonStatusNotify);
            Assert.Equal((int)MessageType.DungeonStatusNotify + 1, (int)MessageType.AchievementUnlockNotify);
            Assert.Equal((int)MessageType.AchievementUnlockNotify + 1, (int)MessageType.AchievementListResponse);
            Assert.Equal((int)MessageType.AchievementListResponse + 1, (int)MessageType.RankingQueryRequest);
            Assert.Equal((int)MessageType.RankingQueryRequest + 1, (int)MessageType.RankingQueryResponse);
        }

        #endregion

        #region TradeRequestMessage Tests

        [Fact]
        public void TradeRequestMessage_DefaultValues_AreCorrect()
        {
            var msg = new TradeRequestMessage();
            Assert.Equal(MessageType.TradeRequest, msg.Type);
            Assert.Equal(ServiceType.Trade, msg.ServiceType);
            Assert.Equal(0UL, msg.InitiatorId);
            Assert.Equal(0UL, msg.TargetId);
            Assert.Equal("", msg.TargetName);
        }

        [Fact]
        public void TradeRequestMessage_SetValues_RetainCorrectly()
        {
            var msg = new TradeRequestMessage
            {
                InitiatorId = 100UL,
                TargetId = 200UL,
                TargetName = "目标玩家"
            };

            Assert.Equal(100UL, msg.InitiatorId);
            Assert.Equal(200UL, msg.TargetId);
            Assert.Equal("目标玩家", msg.TargetName);
        }

        [Fact]
        public void TradeRequestMessage_ImplementsINetworkMessage()
        {
            var msg = new TradeRequestMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region TradeResponseMessage Tests

        [Fact]
        public void TradeResponseMessage_DefaultValues_AreCorrect()
        {
            var msg = new TradeResponseMessage();
            Assert.Equal(MessageType.TradeResponse, msg.Type);
            Assert.Equal(ServiceType.Trade, msg.ServiceType);
            Assert.Equal("", msg.TradeId);
            Assert.False(msg.Accepted);
            Assert.Equal("", msg.Message);
        }

        [Fact]
        public void TradeResponseMessage_SetValues_RetainCorrectly()
        {
            var msg = new TradeResponseMessage
            {
                TradeId = "trade-001",
                Accepted = true,
                Message = "交易已接受"
            };

            Assert.Equal("trade-001", msg.TradeId);
            Assert.True(msg.Accepted);
            Assert.Equal("交易已接受", msg.Message);
        }

        [Fact]
        public void TradeResponseMessage_ImplementsINetworkMessage()
        {
            var msg = new TradeResponseMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region TradeUpdateNotifyMessage Tests

        [Fact]
        public void TradeUpdateNotifyMessage_DefaultValues_AreCorrect()
        {
            var msg = new TradeUpdateNotifyMessage();
            Assert.Equal(MessageType.TradeUpdateNotify, msg.Type);
            Assert.Equal(ServiceType.Trade, msg.ServiceType);
            Assert.Equal("", msg.TradeId);
            Assert.Equal(0, msg.Status);
            Assert.Equal("", msg.PartnerName);
            Assert.Equal(0L, msg.CurrencyAmount);
            Assert.Equal(0L, msg.Timestamp);
        }

        [Fact]
        public void TradeUpdateNotifyMessage_SetValues_RetainCorrectly()
        {
            var msg = new TradeUpdateNotifyMessage
            {
                TradeId = "trade-002",
                Status = 2,
                PartnerName = "交易对象",
                CurrencyAmount = 10000L,
                Timestamp = 1700000000L
            };

            Assert.Equal("trade-002", msg.TradeId);
            Assert.Equal(2, msg.Status);
            Assert.Equal("交易对象", msg.PartnerName);
            Assert.Equal(10000L, msg.CurrencyAmount);
            Assert.Equal(1700000000L, msg.Timestamp);
        }

        [Fact]
        public void TradeUpdateNotifyMessage_ImplementsINetworkMessage()
        {
            var msg = new TradeUpdateNotifyMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region MarketListRequestMessage Tests

        [Fact]
        public void MarketListRequestMessage_DefaultValues_AreCorrect()
        {
            var msg = new MarketListRequestMessage();
            Assert.Equal(MessageType.MarketListRequest, msg.Type);
            Assert.Equal(ServiceType.Trade, msg.ServiceType);
            Assert.Equal(0UL, msg.SellerId);
            Assert.Equal(0L, msg.ItemId);
            Assert.Equal(0, msg.Quantity);
            Assert.Equal(0L, msg.Price);
            Assert.Equal(0, msg.CurrencyType);
        }

        [Fact]
        public void MarketListRequestMessage_SetValues_RetainCorrectly()
        {
            var msg = new MarketListRequestMessage
            {
                SellerId = 300UL,
                ItemId = 5001L,
                Quantity = 10,
                Price = 500L,
                CurrencyType = 1
            };

            Assert.Equal(300UL, msg.SellerId);
            Assert.Equal(5001L, msg.ItemId);
            Assert.Equal(10, msg.Quantity);
            Assert.Equal(500L, msg.Price);
            Assert.Equal(1, msg.CurrencyType);
        }

        [Fact]
        public void MarketListRequestMessage_ImplementsINetworkMessage()
        {
            var msg = new MarketListRequestMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region MarketSearchRequestMessage Tests

        [Fact]
        public void MarketSearchRequestMessage_DefaultValues_AreCorrect()
        {
            var msg = new MarketSearchRequestMessage();
            Assert.Equal(MessageType.MarketSearchRequest, msg.Type);
            Assert.Equal(ServiceType.Trade, msg.ServiceType);
            Assert.Equal("", msg.Keyword);
            Assert.Equal(0, msg.Category);
            Assert.Equal(0, msg.SortBy);
        }

        [Fact]
        public void MarketSearchRequestMessage_SetValues_RetainCorrectly()
        {
            var msg = new MarketSearchRequestMessage
            {
                Keyword = "神兵",
                Category = 3,
                SortBy = 1
            };

            Assert.Equal("神兵", msg.Keyword);
            Assert.Equal(3, msg.Category);
            Assert.Equal(1, msg.SortBy);
        }

        [Fact]
        public void MarketSearchRequestMessage_ImplementsINetworkMessage()
        {
            var msg = new MarketSearchRequestMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region MarketSearchResponseMessage Tests

        [Fact]
        public void MarketSearchResponseMessage_DefaultValues_AreCorrect()
        {
            var msg = new MarketSearchResponseMessage();
            Assert.Equal(MessageType.MarketSearchResponse, msg.Type);
            Assert.Equal(ServiceType.Trade, msg.ServiceType);
            Assert.NotNull(msg.Listings);
            Assert.Empty(msg.Listings);
            Assert.Equal(0, msg.TotalCount);
        }

        [Fact]
        public void MarketSearchResponseMessage_WithListings_RetainCorrectly()
        {
            var msg = new MarketSearchResponseMessage
            {
                TotalCount = 1
            };
            msg.Listings.Add(new MarketListingInfo
            {
                ListingId = 1L,
                ItemId = 100L,
                ItemName = "倚天剑",
                Quantity = 1,
                Price = 99999L,
                SellerName = "卖家A"
            });

            Assert.Single(msg.Listings);
            Assert.Equal("倚天剑", msg.Listings[0].ItemName);
            Assert.Equal(99999L, msg.Listings[0].Price);
        }

        [Fact]
        public void MarketSearchResponseMessage_ImplementsINetworkMessage()
        {
            var msg = new MarketSearchResponseMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region MailListRequestMessage Tests

        [Fact]
        public void MailListRequestMessage_DefaultValues_AreCorrect()
        {
            var msg = new MailListRequestMessage();
            Assert.Equal(MessageType.MailListRequest, msg.Type);
            Assert.Equal(ServiceType.System, msg.ServiceType);
            Assert.Equal(0UL, msg.CharacterId);
            Assert.False(msg.UnreadOnly);
        }

        [Fact]
        public void MailListRequestMessage_SetValues_RetainCorrectly()
        {
            var msg = new MailListRequestMessage
            {
                CharacterId = 400UL,
                UnreadOnly = true
            };

            Assert.Equal(400UL, msg.CharacterId);
            Assert.True(msg.UnreadOnly);
        }

        [Fact]
        public void MailListRequestMessage_ImplementsINetworkMessage()
        {
            var msg = new MailListRequestMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region MailListResponseMessage Tests

        [Fact]
        public void MailListResponseMessage_DefaultValues_AreCorrect()
        {
            var msg = new MailListResponseMessage();
            Assert.Equal(MessageType.MailListResponse, msg.Type);
            Assert.Equal(ServiceType.System, msg.ServiceType);
            Assert.NotNull(msg.Mails);
            Assert.Empty(msg.Mails);
            Assert.Equal(0, msg.UnreadCount);
        }

        [Fact]
        public void MailListResponseMessage_WithMails_RetainCorrectly()
        {
            var msg = new MailListResponseMessage
            {
                UnreadCount = 1
            };
            msg.Mails.Add(new MailInfo
            {
                MailId = 1L,
                SenderName = "系统",
                Title = "欢迎来到混沌世界",
                Content = "祝您游戏愉快！",
                MailType = 0,
                IsRead = false,
                HasAttachment = true,
                AttachmentClaimed = false,
                Timestamp = 1700000000L
            });

            Assert.Single(msg.Mails);
            Assert.Equal("欢迎来到混沌世界", msg.Mails[0].Title);
            Assert.True(msg.Mails[0].HasAttachment);
        }

        [Fact]
        public void MailListResponseMessage_ImplementsINetworkMessage()
        {
            var msg = new MailListResponseMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region MailOperationMessage Tests

        [Fact]
        public void MailOperationMessage_DefaultValues_AreCorrect()
        {
            var msg = new MailOperationMessage();
            Assert.Equal(MessageType.MailOperation, msg.Type);
            Assert.Equal(ServiceType.System, msg.ServiceType);
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(0L, msg.MailId);
            Assert.Equal(0, msg.OperationType);
            Assert.False(msg.Success);
        }

        [Fact]
        public void MailOperationMessage_SetValues_RetainCorrectly()
        {
            var msg = new MailOperationMessage
            {
                CharacterId = 500UL,
                MailId = 10L,
                OperationType = 1,
                Success = true
            };

            Assert.Equal(500UL, msg.CharacterId);
            Assert.Equal(10L, msg.MailId);
            Assert.Equal(1, msg.OperationType);
            Assert.True(msg.Success);
        }

        [Fact]
        public void MailOperationMessage_ImplementsINetworkMessage()
        {
            var msg = new MailOperationMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region MailNotifyMessage Tests

        [Fact]
        public void MailNotifyMessage_DefaultValues_AreCorrect()
        {
            var msg = new MailNotifyMessage();
            Assert.Equal(MessageType.MailNotify, msg.Type);
            Assert.Equal(ServiceType.System, msg.ServiceType);
            Assert.Equal(0L, msg.MailId);
            Assert.Equal("", msg.SenderName);
            Assert.Equal("", msg.Title);
            Assert.False(msg.HasAttachment);
            Assert.Equal(0, msg.UnreadCount);
        }

        [Fact]
        public void MailNotifyMessage_SetValues_RetainCorrectly()
        {
            var msg = new MailNotifyMessage
            {
                MailId = 20L,
                SenderName = "好友A",
                Title = "你好",
                HasAttachment = true,
                UnreadCount = 5
            };

            Assert.Equal(20L, msg.MailId);
            Assert.Equal("好友A", msg.SenderName);
            Assert.Equal("你好", msg.Title);
            Assert.True(msg.HasAttachment);
            Assert.Equal(5, msg.UnreadCount);
        }

        [Fact]
        public void MailNotifyMessage_ImplementsINetworkMessage()
        {
            var msg = new MailNotifyMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region QuestListRequestMessage Tests

        [Fact]
        public void QuestListRequestMessage_DefaultValues_AreCorrect()
        {
            var msg = new QuestListRequestMessage();
            Assert.Equal(MessageType.QuestListRequest, msg.Type);
            Assert.Equal(ServiceType.Quest, msg.ServiceType);
            Assert.Equal(0UL, msg.CharacterId);
            Assert.False(msg.ActiveOnly);
        }

        [Fact]
        public void QuestListRequestMessage_SetValues_RetainCorrectly()
        {
            var msg = new QuestListRequestMessage
            {
                CharacterId = 600UL,
                ActiveOnly = true
            };

            Assert.Equal(600UL, msg.CharacterId);
            Assert.True(msg.ActiveOnly);
        }

        [Fact]
        public void QuestListRequestMessage_ImplementsINetworkMessage()
        {
            var msg = new QuestListRequestMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region QuestListResponseMessage Tests

        [Fact]
        public void QuestListResponseMessage_DefaultValues_AreCorrect()
        {
            var msg = new QuestListResponseMessage();
            Assert.Equal(MessageType.QuestListResponse, msg.Type);
            Assert.Equal(ServiceType.Quest, msg.ServiceType);
            Assert.NotNull(msg.Quests);
            Assert.Empty(msg.Quests);
        }

        [Fact]
        public void QuestListResponseMessage_WithQuests_RetainCorrectly()
        {
            var msg = new QuestListResponseMessage();
            msg.Quests.Add(new QuestSummary
            {
                QuestId = 1001,
                QuestName = "初出江湖",
                QuestType = 0,
                Status = 0,
                CurrentProgress = 3,
                TargetProgress = 10
            });

            Assert.Single(msg.Quests);
            Assert.Equal("初出江湖", msg.Quests[0].QuestName);
            Assert.Equal(3, msg.Quests[0].CurrentProgress);
        }

        [Fact]
        public void QuestListResponseMessage_ImplementsINetworkMessage()
        {
            var msg = new QuestListResponseMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region QuestProgressNotifyMessage Tests

        [Fact]
        public void QuestProgressNotifyMessage_DefaultValues_AreCorrect()
        {
            var msg = new QuestProgressNotifyMessage();
            Assert.Equal(MessageType.QuestProgressNotify, msg.Type);
            Assert.Equal(ServiceType.Quest, msg.ServiceType);
            Assert.Equal(0, msg.QuestId);
            Assert.Equal("", msg.QuestName);
            Assert.Equal(0, msg.ObjectiveIndex);
            Assert.Equal(0, msg.CurrentProgress);
            Assert.Equal(0, msg.TargetProgress);
            Assert.False(msg.IsCompleted);
        }

        [Fact]
        public void QuestProgressNotifyMessage_SetValues_RetainCorrectly()
        {
            var msg = new QuestProgressNotifyMessage
            {
                QuestId = 2001,
                QuestName = "除暴安良",
                ObjectiveIndex = 0,
                CurrentProgress = 5,
                TargetProgress = 10,
                IsCompleted = false
            };

            Assert.Equal(2001, msg.QuestId);
            Assert.Equal("除暴安良", msg.QuestName);
            Assert.Equal(0, msg.ObjectiveIndex);
            Assert.Equal(5, msg.CurrentProgress);
            Assert.Equal(10, msg.TargetProgress);
            Assert.False(msg.IsCompleted);
        }

        [Fact]
        public void QuestProgressNotifyMessage_Completed_RetainCorrectly()
        {
            var msg = new QuestProgressNotifyMessage
            {
                QuestId = 2001,
                QuestName = "除暴安良",
                CurrentProgress = 10,
                TargetProgress = 10,
                IsCompleted = true
            };

            Assert.True(msg.IsCompleted);
            Assert.Equal(msg.CurrentProgress, msg.TargetProgress);
        }

        [Fact]
        public void QuestProgressNotifyMessage_ImplementsINetworkMessage()
        {
            var msg = new QuestProgressNotifyMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region DungeonEnterRequestMessage Tests

        [Fact]
        public void DungeonEnterRequestMessage_DefaultValues_AreCorrect()
        {
            var msg = new DungeonEnterRequestMessage();
            Assert.Equal(MessageType.DungeonEnterRequest, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
            Assert.Equal(0UL, msg.CharacterId);
            Assert.Equal(0, msg.DungeonTemplateId);
            Assert.Equal(0, msg.Difficulty);
        }

        [Fact]
        public void DungeonEnterRequestMessage_SetValues_RetainCorrectly()
        {
            var msg = new DungeonEnterRequestMessage
            {
                CharacterId = 700UL,
                DungeonTemplateId = 3001,
                Difficulty = 2
            };

            Assert.Equal(700UL, msg.CharacterId);
            Assert.Equal(3001, msg.DungeonTemplateId);
            Assert.Equal(2, msg.Difficulty);
        }

        [Fact]
        public void DungeonEnterRequestMessage_ImplementsINetworkMessage()
        {
            var msg = new DungeonEnterRequestMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region DungeonStatusNotifyMessage Tests

        [Fact]
        public void DungeonStatusNotifyMessage_DefaultValues_AreCorrect()
        {
            var msg = new DungeonStatusNotifyMessage();
            Assert.Equal(MessageType.DungeonStatusNotify, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
            Assert.Equal("", msg.DungeonInstanceId);
            Assert.Equal("", msg.DungeonName);
            Assert.Equal(0, msg.Status);
            Assert.Equal(0, msg.CurrentPlayers);
            Assert.Equal(0, msg.MaxPlayers);
            Assert.Equal(0, msg.RemainingSeconds);
        }

        [Fact]
        public void DungeonStatusNotifyMessage_SetValues_RetainCorrectly()
        {
            var msg = new DungeonStatusNotifyMessage
            {
                DungeonInstanceId = "dungeon-001",
                DungeonName = "华山论剑",
                Status = 1,
                CurrentPlayers = 4,
                MaxPlayers = 5,
                RemainingSeconds = 1800
            };

            Assert.Equal("dungeon-001", msg.DungeonInstanceId);
            Assert.Equal("华山论剑", msg.DungeonName);
            Assert.Equal(1, msg.Status);
            Assert.Equal(4, msg.CurrentPlayers);
            Assert.Equal(5, msg.MaxPlayers);
            Assert.Equal(1800, msg.RemainingSeconds);
        }

        [Fact]
        public void DungeonStatusNotifyMessage_ImplementsINetworkMessage()
        {
            var msg = new DungeonStatusNotifyMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region AchievementUnlockNotifyMessage Tests

        [Fact]
        public void AchievementUnlockNotifyMessage_DefaultValues_AreCorrect()
        {
            var msg = new AchievementUnlockNotifyMessage();
            Assert.Equal(MessageType.AchievementUnlockNotify, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
            Assert.Equal(0, msg.AchievementId);
            Assert.Equal("", msg.AchievementName);
            Assert.Equal("", msg.Description);
            Assert.Equal(0, msg.Points);
            Assert.Equal(0, msg.Category);
            Assert.Equal(0L, msg.UnlockTimestamp);
        }

        [Fact]
        public void AchievementUnlockNotifyMessage_SetValues_RetainCorrectly()
        {
            var msg = new AchievementUnlockNotifyMessage
            {
                AchievementId = 4001,
                AchievementName = "初涉江湖",
                Description = "首次击败敌人",
                Points = 10,
                Category = 1,
                UnlockTimestamp = 1700000000L
            };

            Assert.Equal(4001, msg.AchievementId);
            Assert.Equal("初涉江湖", msg.AchievementName);
            Assert.Equal("首次击败敌人", msg.Description);
            Assert.Equal(10, msg.Points);
            Assert.Equal(1, msg.Category);
            Assert.Equal(1700000000L, msg.UnlockTimestamp);
        }

        [Fact]
        public void AchievementUnlockNotifyMessage_ImplementsINetworkMessage()
        {
            var msg = new AchievementUnlockNotifyMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region AchievementListResponseMessage Tests

        [Fact]
        public void AchievementListResponseMessage_DefaultValues_AreCorrect()
        {
            var msg = new AchievementListResponseMessage();
            Assert.Equal(MessageType.AchievementListResponse, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
            Assert.NotNull(msg.Achievements);
            Assert.Empty(msg.Achievements);
            Assert.Equal(0, msg.TotalPoints);
            Assert.Equal(0, msg.UnlockedCount);
        }

        [Fact]
        public void AchievementListResponseMessage_WithAchievements_RetainCorrectly()
        {
            var msg = new AchievementListResponseMessage
            {
                TotalPoints = 50,
                UnlockedCount = 2
            };
            msg.Achievements.Add(new AchievementSummary
            {
                AchievementId = 4001,
                Name = "初涉江湖",
                Category = 1,
                Points = 10,
                CurrentProgress = 1,
                TargetProgress = 1,
                IsUnlocked = true
            });

            Assert.Single(msg.Achievements);
            Assert.Equal("初涉江湖", msg.Achievements[0].Name);
            Assert.True(msg.Achievements[0].IsUnlocked);
        }

        [Fact]
        public void AchievementListResponseMessage_ImplementsINetworkMessage()
        {
            var msg = new AchievementListResponseMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region RankingQueryRequestMessage Tests

        [Fact]
        public void RankingQueryRequestMessage_DefaultValues_AreCorrect()
        {
            var msg = new RankingQueryRequestMessage();
            Assert.Equal(MessageType.RankingQueryRequest, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
            Assert.Equal(0, msg.RankingType);
            Assert.Equal(0, msg.Count);
            Assert.Equal(0UL, msg.CharacterId);
        }

        [Fact]
        public void RankingQueryRequestMessage_SetValues_RetainCorrectly()
        {
            var msg = new RankingQueryRequestMessage
            {
                RankingType = 0,
                Count = 50,
                CharacterId = 800UL
            };

            Assert.Equal(0, msg.RankingType);
            Assert.Equal(50, msg.Count);
            Assert.Equal(800UL, msg.CharacterId);
        }

        [Fact]
        public void RankingQueryRequestMessage_ImplementsINetworkMessage()
        {
            var msg = new RankingQueryRequestMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region RankingQueryResponseMessage Tests

        [Fact]
        public void RankingQueryResponseMessage_DefaultValues_AreCorrect()
        {
            var msg = new RankingQueryResponseMessage();
            Assert.Equal(MessageType.RankingQueryResponse, msg.Type);
            Assert.Equal(ServiceType.Game, msg.ServiceType);
            Assert.Equal(0, msg.RankingType);
            Assert.Equal("", msg.RankingName);
            Assert.NotNull(msg.Entries);
            Assert.Empty(msg.Entries);
            Assert.Equal(-1, msg.MyRank);
        }

        [Fact]
        public void RankingQueryResponseMessage_WithEntries_RetainCorrectly()
        {
            var msg = new RankingQueryResponseMessage
            {
                RankingType = 0,
                RankingName = "战力排行",
                MyRank = 15
            };
            msg.Entries.Add(new RankingEntryInfo
            {
                Rank = 1,
                PlayerName = "天下第一",
                Score = 999999L,
                Level = 100
            });

            Assert.Single(msg.Entries);
            Assert.Equal("天下第一", msg.Entries[0].PlayerName);
            Assert.Equal(999999L, msg.Entries[0].Score);
            Assert.Equal(15, msg.MyRank);
        }

        [Fact]
        public void RankingQueryResponseMessage_ImplementsINetworkMessage()
        {
            var msg = new RankingQueryResponseMessage();
            Assert.IsAssignableFrom<INetworkMessage>(msg);
        }

        #endregion

        #region MemoryPack Serialization Tests

        [Fact]
        public void TradeRequestMessage_CanSerializeAndDeserialize()
        {
            var original = new TradeRequestMessage
            {
                InitiatorId = 123UL,
                TargetId = 456UL,
                TargetName = "测试玩家"
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<TradeRequestMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.InitiatorId, deserialized.InitiatorId);
            Assert.Equal(original.TargetId, deserialized.TargetId);
            Assert.Equal(original.TargetName, deserialized.TargetName);
        }

        [Fact]
        public void TradeResponseMessage_CanSerializeAndDeserialize()
        {
            var original = new TradeResponseMessage
            {
                TradeId = "trade-test",
                Accepted = true,
                Message = "接受交易"
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<TradeResponseMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.TradeId, deserialized.TradeId);
            Assert.Equal(original.Accepted, deserialized.Accepted);
            Assert.Equal(original.Message, deserialized.Message);
        }

        [Fact]
        public void TradeUpdateNotifyMessage_CanSerializeAndDeserialize()
        {
            var original = new TradeUpdateNotifyMessage
            {
                TradeId = "trade-update",
                Status = 2,
                PartnerName = "伙伴",
                CurrencyAmount = 5000L,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<TradeUpdateNotifyMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.TradeId, deserialized.TradeId);
            Assert.Equal(original.Status, deserialized.Status);
            Assert.Equal(original.PartnerName, deserialized.PartnerName);
            Assert.Equal(original.CurrencyAmount, deserialized.CurrencyAmount);
            Assert.Equal(original.Timestamp, deserialized.Timestamp);
        }

        [Fact]
        public void MailNotifyMessage_CanSerializeAndDeserialize()
        {
            var original = new MailNotifyMessage
            {
                MailId = 42L,
                SenderName = "测试发件人",
                Title = "测试邮件",
                HasAttachment = true,
                UnreadCount = 3
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<MailNotifyMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.MailId, deserialized.MailId);
            Assert.Equal(original.SenderName, deserialized.SenderName);
            Assert.Equal(original.Title, deserialized.Title);
            Assert.Equal(original.HasAttachment, deserialized.HasAttachment);
            Assert.Equal(original.UnreadCount, deserialized.UnreadCount);
        }

        [Fact]
        public void QuestProgressNotifyMessage_CanSerializeAndDeserialize()
        {
            var original = new QuestProgressNotifyMessage
            {
                QuestId = 1001,
                QuestName = "序列化测试任务",
                ObjectiveIndex = 0,
                CurrentProgress = 7,
                TargetProgress = 10,
                IsCompleted = false
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<QuestProgressNotifyMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.QuestId, deserialized.QuestId);
            Assert.Equal(original.QuestName, deserialized.QuestName);
            Assert.Equal(original.ObjectiveIndex, deserialized.ObjectiveIndex);
            Assert.Equal(original.CurrentProgress, deserialized.CurrentProgress);
            Assert.Equal(original.TargetProgress, deserialized.TargetProgress);
            Assert.Equal(original.IsCompleted, deserialized.IsCompleted);
        }

        [Fact]
        public void DungeonStatusNotifyMessage_CanSerializeAndDeserialize()
        {
            var original = new DungeonStatusNotifyMessage
            {
                DungeonInstanceId = "dg-test-001",
                DungeonName = "测试副本",
                Status = 1,
                CurrentPlayers = 3,
                MaxPlayers = 5,
                RemainingSeconds = 600
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<DungeonStatusNotifyMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.DungeonInstanceId, deserialized.DungeonInstanceId);
            Assert.Equal(original.DungeonName, deserialized.DungeonName);
            Assert.Equal(original.Status, deserialized.Status);
            Assert.Equal(original.CurrentPlayers, deserialized.CurrentPlayers);
            Assert.Equal(original.MaxPlayers, deserialized.MaxPlayers);
            Assert.Equal(original.RemainingSeconds, deserialized.RemainingSeconds);
        }

        [Fact]
        public void AchievementUnlockNotifyMessage_CanSerializeAndDeserialize()
        {
            var original = new AchievementUnlockNotifyMessage
            {
                AchievementId = 5001,
                AchievementName = "序列化成就",
                Description = "完成序列化测试",
                Points = 20,
                Category = 2,
                UnlockTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<AchievementUnlockNotifyMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.AchievementId, deserialized.AchievementId);
            Assert.Equal(original.AchievementName, deserialized.AchievementName);
            Assert.Equal(original.Description, deserialized.Description);
            Assert.Equal(original.Points, deserialized.Points);
            Assert.Equal(original.Category, deserialized.Category);
            Assert.Equal(original.UnlockTimestamp, deserialized.UnlockTimestamp);
        }

        [Fact]
        public void RankingQueryResponseMessage_CanSerializeAndDeserialize()
        {
            var original = new RankingQueryResponseMessage
            {
                RankingType = 1,
                RankingName = "等级排行",
                MyRank = 42
            };
            original.Entries.Add(new RankingEntryInfo
            {
                Rank = 1,
                PlayerName = "测试玩家",
                Score = 100L,
                Level = 99
            });

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<RankingQueryResponseMessage>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.RankingType, deserialized.RankingType);
            Assert.Equal(original.RankingName, deserialized.RankingName);
            Assert.Equal(original.MyRank, deserialized.MyRank);
            Assert.Single(deserialized.Entries);
            Assert.Equal("测试玩家", deserialized.Entries[0].PlayerName);
        }

        #endregion

        #region Cross-Phase Compatibility Tests

        [Fact]
        public void ExistingTradeInfo_StillWorks()
        {
            var info = new TradeInfo();
            Assert.NotNull(info);
        }

        [Fact]
        public void ExistingQuestInfo_StillWorks()
        {
            var info = new QuestInfo
            {
                QuestId = 1,
                QuestName = "TestQuest"
            };
            Assert.Equal(MessageType.QuestUpdate, info.Type);
        }

        [Fact]
        public void ExistingQuestUpdateMessage_StillWorks()
        {
            var msg = new QuestUpdateMessage
            {
                QuestId = 1,
                CharacterId = 1UL
            };
            Assert.Equal(MessageType.QuestUpdate, msg.Type);
        }

        [Fact]
        public void Phase9_ChatNotifyMessage_StillWorks()
        {
            var msg = new ChatNotifyMessage
            {
                SenderId = 1UL,
                SenderName = "TestSender",
                Content = "Hello",
                Channel = ChatChannel.World
            };
            Assert.Equal(MessageType.ChatNotify, msg.Type);
            Assert.Equal(ServiceType.Chat, msg.ServiceType);
        }

        [Fact]
        public void Phase9_FriendStatusUpdateMessage_StillWorks()
        {
            var msg = new FriendStatusUpdateMessage
            {
                FriendId = 1UL,
                FriendName = "TestFriend",
                IsOnline = true
            };
            Assert.Equal(MessageType.FriendStatusUpdate, msg.Type);
            Assert.Equal(ServiceType.Social, msg.ServiceType);
        }

        #endregion

        #region MarketListingInfo Tests

        [Fact]
        public void MarketListingInfo_DefaultValues_AreCorrect()
        {
            var info = new MarketListingInfo();
            Assert.Equal(0L, info.ListingId);
            Assert.Equal(0L, info.ItemId);
            Assert.Equal("", info.ItemName);
            Assert.Equal(0, info.Quantity);
            Assert.Equal(0L, info.Price);
            Assert.Equal("", info.SellerName);
        }

        [Fact]
        public void MarketListingInfo_CanSerializeAndDeserialize()
        {
            var original = new MarketListingInfo
            {
                ListingId = 1L,
                ItemId = 100L,
                ItemName = "测试物品",
                Quantity = 5,
                Price = 1000L,
                SellerName = "卖家"
            };

            var bytes = MemoryPack.MemoryPackSerializer.Serialize(original);
            var deserialized = MemoryPack.MemoryPackSerializer.Deserialize<MarketListingInfo>(bytes);

            Assert.NotNull(deserialized);
            Assert.Equal(original.ListingId, deserialized.ListingId);
            Assert.Equal(original.ItemName, deserialized.ItemName);
        }

        #endregion

        #region QuestSummary Tests

        [Fact]
        public void QuestSummary_DefaultValues_AreCorrect()
        {
            var summary = new QuestSummary();
            Assert.Equal(0, summary.QuestId);
            Assert.Equal("", summary.QuestName);
            Assert.Equal(0, summary.QuestType);
            Assert.Equal(0, summary.Status);
            Assert.Equal(0, summary.CurrentProgress);
            Assert.Equal(0, summary.TargetProgress);
        }

        #endregion

        #region AchievementSummary Tests

        [Fact]
        public void AchievementSummary_DefaultValues_AreCorrect()
        {
            var summary = new AchievementSummary();
            Assert.Equal(0, summary.AchievementId);
            Assert.Equal("", summary.Name);
            Assert.Equal(0, summary.Category);
            Assert.Equal(0, summary.Points);
            Assert.Equal(0, summary.CurrentProgress);
            Assert.Equal(0, summary.TargetProgress);
            Assert.False(summary.IsUnlocked);
        }

        #endregion

        #region RankingEntryInfo Tests

        [Fact]
        public void RankingEntryInfo_DefaultValues_AreCorrect()
        {
            var entry = new RankingEntryInfo();
            Assert.Equal(0, entry.Rank);
            Assert.Equal("", entry.PlayerName);
            Assert.Equal(0L, entry.Score);
            Assert.Equal(0, entry.Level);
        }

        #endregion

        #region MailInfo Tests

        [Fact]
        public void MailInfo_DefaultValues_AreCorrect()
        {
            var info = new MailInfo();
            Assert.Equal(0L, info.MailId);
            Assert.Equal("", info.SenderName);
            Assert.Equal("", info.Title);
            Assert.Equal("", info.Content);
            Assert.Equal(0, info.MailType);
            Assert.False(info.IsRead);
            Assert.False(info.HasAttachment);
            Assert.False(info.AttachmentClaimed);
            Assert.Equal(0L, info.Timestamp);
        }

        #endregion
    }
}
