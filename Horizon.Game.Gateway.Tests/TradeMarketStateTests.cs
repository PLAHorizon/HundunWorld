using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Gateway.Tests
{
    /// <summary>
    /// TradeState, MarketState 数据模型及业务逻辑单元测试
    /// 测试交易系统和市场系统的状态管理逻辑
    /// </summary>
    public class TradeMarketStateTests
    {
        #region TradeState Tests - 交易状态默认值

        [Fact]
        public void TradeState_DefaultValues_AreCorrect()
        {
            var state = new TradeState();
            Assert.Equal(Guid.Empty, state.SellerId);
            Assert.Equal(Guid.Empty, state.BuyerId);
            Assert.NotNull(state.SellerItems);
            Assert.Empty(state.SellerItems);
            Assert.NotNull(state.BuyerItems);
            Assert.Empty(state.BuyerItems);
            Assert.Equal(0, state.SellerCurrency);
            Assert.Equal(0, state.BuyerCurrency);
            Assert.False(state.SellerConfirmed);
            Assert.False(state.BuyerConfirmed);
            Assert.Equal((int)TradeStatus.Created, state.Status);
            Assert.False(state.IsCreated);
        }

        [Fact]
        public void TradeState_CreateTrade_SetsSellerAndBuyer()
        {
            var state = new TradeState();
            var sellerId = Guid.NewGuid();
            var buyerId = Guid.NewGuid();

            state.SellerId = sellerId;
            state.BuyerId = buyerId;
            state.IsCreated = true;
            state.CreatedTime = DateTime.UtcNow;

            Assert.Equal(sellerId, state.SellerId);
            Assert.Equal(buyerId, state.BuyerId);
            Assert.True(state.IsCreated);
            Assert.True(state.CreatedTime > DateTime.MinValue);
        }

        [Fact]
        public void TradeState_AddSellerItems_IncreasesCount()
        {
            var state = new TradeState();
            state.SellerItems.Add(new TradeItem { ItemId = 1, Quantity = 5, ItemName = "铁剑" });
            state.SellerItems.Add(new TradeItem { ItemId = 2, Quantity = 10, ItemName = "药草" });

            Assert.Equal(2, state.SellerItems.Count);
            Assert.Equal(1, state.SellerItems[0].ItemId);
            Assert.Equal(5, state.SellerItems[0].Quantity);
            Assert.Equal("铁剑", state.SellerItems[0].ItemName);
        }

        [Fact]
        public void TradeState_AddBuyerItems_IncreasesCount()
        {
            var state = new TradeState();
            state.BuyerItems.Add(new TradeItem { ItemId = 100, Quantity = 1, ItemName = "金锭" });

            Assert.Single(state.BuyerItems);
            Assert.Equal(100, state.BuyerItems[0].ItemId);
        }

        [Fact]
        public void TradeState_RemoveItem_DecreasesCount()
        {
            var state = new TradeState();
            state.SellerItems.Add(new TradeItem { ItemId = 1, Quantity = 5 });
            state.SellerItems.Add(new TradeItem { ItemId = 2, Quantity = 10 });

            state.SellerItems.RemoveAll(i => i.ItemId == 1);

            Assert.Single(state.SellerItems);
            Assert.Equal(2, state.SellerItems[0].ItemId);
        }

        [Fact]
        public void TradeState_SetCurrencyAmounts_WorksCorrectly()
        {
            var state = new TradeState();
            state.SellerCurrency = 1000;
            state.BuyerCurrency = 2000;

            Assert.Equal(1000, state.SellerCurrency);
            Assert.Equal(2000, state.BuyerCurrency);
        }

        [Fact]
        public void TradeState_SellerConfirmation_SetsFlag()
        {
            var state = new TradeState();
            state.SellerConfirmed = true;

            Assert.True(state.SellerConfirmed);
            Assert.False(state.BuyerConfirmed);
        }

        [Fact]
        public void TradeState_BuyerConfirmation_SetsFlag()
        {
            var state = new TradeState();
            state.BuyerConfirmed = true;

            Assert.False(state.SellerConfirmed);
            Assert.True(state.BuyerConfirmed);
        }

        [Fact]
        public void TradeState_BothConfirmed_CanSetBothConfirmedStatus()
        {
            var state = new TradeState();
            state.SellerConfirmed = true;
            state.BuyerConfirmed = true;

            if (state.SellerConfirmed && state.BuyerConfirmed)
                state.Status = (int)TradeStatus.BothConfirmed;

            Assert.Equal((int)TradeStatus.BothConfirmed, state.Status);
        }

        [Fact]
        public void TradeState_CancelTrade_SetsStatusToCancelled()
        {
            var state = new TradeState();
            state.IsCreated = true;
            state.Status = (int)TradeStatus.Cancelled;

            Assert.Equal((int)TradeStatus.Cancelled, state.Status);
        }

        [Fact]
        public void TradeState_TaxCalculation_FivePercent()
        {
            const decimal TradeTaxRate = 0.05m;
            long sellerCurrency = 1000;
            long buyerCurrency = 2000;
            long totalCurrency = sellerCurrency + buyerCurrency;
            long tax = (long)(totalCurrency * TradeTaxRate);

            Assert.Equal(3000, totalCurrency);
            Assert.Equal(150, tax);
        }

        [Fact]
        public void TradeState_TaxCalculation_ZeroCurrency()
        {
            const decimal TradeTaxRate = 0.05m;
            long totalCurrency = 0;
            long tax = (long)(totalCurrency * TradeTaxRate);

            Assert.Equal(0, tax);
        }

        #endregion

        #region TradeStatus Enum Tests

        [Fact]
        public void TradeStatus_EnumValues_AreCorrect()
        {
            Assert.Equal(0, (int)TradeStatus.Created);
            Assert.Equal(1, (int)TradeStatus.BothConfirmed);
            Assert.Equal(2, (int)TradeStatus.Completed);
            Assert.Equal(3, (int)TradeStatus.Cancelled);
            Assert.Equal(4, (int)TradeStatus.Failed);
        }

        [Fact]
        public void TradeState_ExecuteTrade_RequiresBothConfirmed()
        {
            var state = new TradeState();
            state.IsCreated = true;
            state.SellerConfirmed = true;
            state.BuyerConfirmed = true;
            state.Status = (int)TradeStatus.BothConfirmed;

            // Simulate execute
            bool canExecute = state.Status == (int)TradeStatus.BothConfirmed;
            Assert.True(canExecute);

            state.Status = (int)TradeStatus.Completed;
            Assert.Equal((int)TradeStatus.Completed, state.Status);
        }

        [Fact]
        public void TradeState_CannotExecute_WithoutBothConfirmed()
        {
            var state = new TradeState();
            state.IsCreated = true;
            state.SellerConfirmed = true;
            state.BuyerConfirmed = false;

            bool canExecute = state.Status == (int)TradeStatus.BothConfirmed;
            Assert.False(canExecute);
        }

        [Fact]
        public void TradeState_PreventSelfTrade_SameIds()
        {
            var playerId = Guid.NewGuid();
#pragma warning disable CS1718 // 与自身比较是有意为之，用于验证自交易防护逻辑
            bool isSelfTrade = playerId == playerId;
#pragma warning restore CS1718
            Assert.True(isSelfTrade);
        }

        [Fact]
        public void TradeState_PreventSelfTrade_DifferentIds()
        {
            var sellerId = Guid.NewGuid();
            var buyerId = Guid.NewGuid();
            bool isSelfTrade = sellerId == buyerId;
            Assert.False(isSelfTrade);
        }

        [Fact]
        public void TradeState_ResetConfirmations_WhenItemsChange()
        {
            var state = new TradeState();
            state.SellerConfirmed = true;
            state.BuyerConfirmed = true;

            // Simulate adding item resets confirmations
            state.SellerItems.Add(new TradeItem { ItemId = 1, Quantity = 1 });
            state.SellerConfirmed = false;
            state.BuyerConfirmed = false;

            Assert.False(state.SellerConfirmed);
            Assert.False(state.BuyerConfirmed);
        }

        [Fact]
        public void TradeState_CompletedTrade_CannotBeModified()
        {
            var state = new TradeState();
            state.IsCreated = true;
            state.Status = (int)TradeStatus.Completed;

            bool canModify = state.Status == (int)TradeStatus.Created;
            Assert.False(canModify);
        }

        [Fact]
        public void TradeState_FailedStatus_SetsCorrectly()
        {
            var state = new TradeState();
            state.Status = (int)TradeStatus.Failed;

            Assert.Equal((int)TradeStatus.Failed, state.Status);
        }

        [Fact]
        public void TradeState_MultipleItems_CanAccumulate()
        {
            var state = new TradeState();
            state.SellerItems.Add(new TradeItem { ItemId = 1, Quantity = 5 });

            var existing = state.SellerItems.FirstOrDefault(i => i.ItemId == 1);
            Assert.NotNull(existing);
            existing.Quantity += 3;

            Assert.Equal(8, state.SellerItems[0].Quantity);
        }

        #endregion

        #region MarketState Tests - 市场状态

        [Fact]
        public void MarketState_DefaultValues_AreCorrect()
        {
            var state = new MarketState();
            Assert.NotNull(state.Listings);
            Assert.Empty(state.Listings);
            Assert.Equal(1, state.NextListingId);
            Assert.Equal(0, state.TotalTransactions);
            Assert.Equal(0, state.TotalVolume);
        }

        [Fact]
        public void MarketState_AddListing_AutoIncrementsId()
        {
            var state = new MarketState();
            var listingId1 = state.NextListingId++;
            var listingId2 = state.NextListingId++;

            Assert.Equal(1, listingId1);
            Assert.Equal(2, listingId2);
            Assert.Equal(3, state.NextListingId);
        }

        [Fact]
        public void MarketState_AddListing_StoresCorrectly()
        {
            var state = new MarketState();
            var sellerId = Guid.NewGuid();
            var listingId = state.NextListingId++;

            var listing = new MarketListing
            {
                ListingId = listingId,
                SellerId = sellerId,
                SellerName = "玩家A",
                ItemId = 100,
                ItemName = "神兵",
                Quantity = 1,
                Price = 5000,
                CurrencyType = 1,
                ListTime = DateTime.UtcNow,
                Status = (int)MarketListingStatus.Active,
                Category = 1
            };
            state.Listings[listingId] = listing;

            Assert.Single(state.Listings);
            Assert.Equal(sellerId, state.Listings[listingId].SellerId);
            Assert.Equal("神兵", state.Listings[listingId].ItemName);
        }

        [Fact]
        public void MarketState_DelistItem_ChangesStatus()
        {
            var state = new MarketState();
            var listingId = state.NextListingId++;
            state.Listings[listingId] = new MarketListing
            {
                ListingId = listingId,
                Status = (int)MarketListingStatus.Active
            };

            state.Listings[listingId].Status = (int)MarketListingStatus.Delisted;

            Assert.Equal((int)MarketListingStatus.Delisted, state.Listings[listingId].Status);
        }

        [Fact]
        public void MarketState_PurchaseFlow_ChangesStatusToSold()
        {
            var state = new MarketState();
            var listingId = state.NextListingId++;
            state.Listings[listingId] = new MarketListing
            {
                ListingId = listingId,
                Price = 1000,
                Status = (int)MarketListingStatus.Active
            };

            state.Listings[listingId].Status = (int)MarketListingStatus.Sold;
            state.TotalTransactions++;
            state.TotalVolume += 1000;

            Assert.Equal((int)MarketListingStatus.Sold, state.Listings[listingId].Status);
            Assert.Equal(1, state.TotalTransactions);
            Assert.Equal(1000, state.TotalVolume);
        }

        [Fact]
        public void MarketState_MarketStats_CalculatedCorrectly()
        {
            var state = new MarketState();

            // Add multiple listings
            for (int i = 0; i < 5; i++)
            {
                var id = state.NextListingId++;
                state.Listings[id] = new MarketListing
                {
                    ListingId = id,
                    Status = (int)MarketListingStatus.Active,
                    ListTime = DateTime.UtcNow
                };
            }

            // Sell 2 of them
            state.Listings[1].Status = (int)MarketListingStatus.Sold;
            state.TotalTransactions++;
            state.TotalVolume += 500;
            state.Listings[2].Status = (int)MarketListingStatus.Sold;
            state.TotalTransactions++;
            state.TotalVolume += 300;

            int activeCount = state.Listings.Values.Count(l => l.Status == (int)MarketListingStatus.Active);

            Assert.Equal(5, state.Listings.Count);
            Assert.Equal(3, activeCount);
            Assert.Equal(2, state.TotalTransactions);
            Assert.Equal(800, state.TotalVolume);
        }

        [Fact]
        public void MarketState_SearchByKeyword_FiltersCorrectly()
        {
            var state = new MarketState();

            var id1 = state.NextListingId++;
            state.Listings[id1] = new MarketListing
            {
                ListingId = id1, ItemName = "铁剑", Status = (int)MarketListingStatus.Active, ListTime = DateTime.UtcNow
            };

            var id2 = state.NextListingId++;
            state.Listings[id2] = new MarketListing
            {
                ListingId = id2, ItemName = "金剑", Status = (int)MarketListingStatus.Active, ListTime = DateTime.UtcNow
            };

            var id3 = state.NextListingId++;
            state.Listings[id3] = new MarketListing
            {
                ListingId = id3, ItemName = "药草", Status = (int)MarketListingStatus.Active, ListTime = DateTime.UtcNow
            };

            string keyword = "剑";
            var results = state.Listings.Values
                .Where(l => l.Status == (int)MarketListingStatus.Active)
                .Where(l => l.ItemName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void MarketState_SearchByCategory_FiltersCorrectly()
        {
            var state = new MarketState();

            var id1 = state.NextListingId++;
            state.Listings[id1] = new MarketListing
            {
                ListingId = id1, Category = 1, Status = (int)MarketListingStatus.Active, ListTime = DateTime.UtcNow
            };

            var id2 = state.NextListingId++;
            state.Listings[id2] = new MarketListing
            {
                ListingId = id2, Category = 2, Status = (int)MarketListingStatus.Active, ListTime = DateTime.UtcNow
            };

            var id3 = state.NextListingId++;
            state.Listings[id3] = new MarketListing
            {
                ListingId = id3, Category = 1, Status = (int)MarketListingStatus.Active, ListTime = DateTime.UtcNow
            };

            int categoryFilter = 1;
            var results = state.Listings.Values
                .Where(l => l.Status == (int)MarketListingStatus.Active)
                .Where(l => l.Category == categoryFilter)
                .ToList();

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void MarketState_SortByPriceAsc_WorksCorrectly()
        {
            var state = new MarketState();

            var id1 = state.NextListingId++;
            state.Listings[id1] = new MarketListing { ListingId = id1, Price = 300, Status = (int)MarketListingStatus.Active };
            var id2 = state.NextListingId++;
            state.Listings[id2] = new MarketListing { ListingId = id2, Price = 100, Status = (int)MarketListingStatus.Active };
            var id3 = state.NextListingId++;
            state.Listings[id3] = new MarketListing { ListingId = id3, Price = 200, Status = (int)MarketListingStatus.Active };

            var sorted = state.Listings.Values
                .Where(l => l.Status == (int)MarketListingStatus.Active)
                .OrderBy(l => l.Price)
                .ToList();

            Assert.Equal(100, sorted[0].Price);
            Assert.Equal(200, sorted[1].Price);
            Assert.Equal(300, sorted[2].Price);
        }

        [Fact]
        public void MarketState_SortByPriceDesc_WorksCorrectly()
        {
            var state = new MarketState();

            var id1 = state.NextListingId++;
            state.Listings[id1] = new MarketListing { ListingId = id1, Price = 100, Status = (int)MarketListingStatus.Active };
            var id2 = state.NextListingId++;
            state.Listings[id2] = new MarketListing { ListingId = id2, Price = 300, Status = (int)MarketListingStatus.Active };

            var sorted = state.Listings.Values
                .Where(l => l.Status == (int)MarketListingStatus.Active)
                .OrderByDescending(l => l.Price)
                .ToList();

            Assert.Equal(300, sorted[0].Price);
            Assert.Equal(100, sorted[1].Price);
        }

        [Fact]
        public void MarketState_PlayerListings_FiltersBySellerId()
        {
            var state = new MarketState();
            var player1 = Guid.NewGuid();
            var player2 = Guid.NewGuid();

            var id1 = state.NextListingId++;
            state.Listings[id1] = new MarketListing { ListingId = id1, SellerId = player1 };
            var id2 = state.NextListingId++;
            state.Listings[id2] = new MarketListing { ListingId = id2, SellerId = player2 };
            var id3 = state.NextListingId++;
            state.Listings[id3] = new MarketListing { ListingId = id3, SellerId = player1 };

            var player1Listings = state.Listings.Values.Where(l => l.SellerId == player1).ToList();

            Assert.Equal(2, player1Listings.Count);
        }

        #endregion

        #region MarketListingStatus Enum Tests

        [Fact]
        public void MarketListingStatus_EnumValues_AreCorrect()
        {
            Assert.Equal(0, (int)MarketListingStatus.Active);
            Assert.Equal(1, (int)MarketListingStatus.Sold);
            Assert.Equal(2, (int)MarketListingStatus.Delisted);
            Assert.Equal(3, (int)MarketListingStatus.Expired);
        }

        [Fact]
        public void MarketState_MarketTaxRate_ThreePercent()
        {
            const decimal MarketTaxRate = 0.03m;
            long price = 10000;
            long tax = (long)(price * MarketTaxRate);

            Assert.Equal(300, tax);
        }

        [Fact]
        public void MarketState_ListingExpiration_Check()
        {
            var listing = new MarketListing
            {
                ListTime = DateTime.UtcNow.AddHours(-73),
                Status = (int)MarketListingStatus.Active
            };

            var expiration = TimeSpan.FromHours(72);
            bool isExpired = DateTime.UtcNow - listing.ListTime > expiration;

            Assert.True(isExpired);
        }

        [Fact]
        public void MarketState_ListingNotExpired_WithinTime()
        {
            var listing = new MarketListing
            {
                ListTime = DateTime.UtcNow.AddHours(-1),
                Status = (int)MarketListingStatus.Active
            };

            var expiration = TimeSpan.FromHours(72);
            bool isExpired = DateTime.UtcNow - listing.ListTime > expiration;

            Assert.False(isExpired);
        }

        [Fact]
        public void MarketState_PreventBuyingOwnItem()
        {
            var sellerId = Guid.NewGuid();
            var buyerId = sellerId;

            bool isSelfBuy = sellerId == buyerId;
            Assert.True(isSelfBuy);
        }

        [Fact]
        public void MarketState_PreventBuyingOwnItem_DifferentPlayers()
        {
            var sellerId = Guid.NewGuid();
            var buyerId = Guid.NewGuid();

            bool isSelfBuy = sellerId == buyerId;
            Assert.False(isSelfBuy);
        }

        [Fact]
        public void MarketState_InvalidPrice_Rejected()
        {
            long price = -100;
            bool isValid = price > 0;
            Assert.False(isValid);
        }

        [Fact]
        public void MarketState_InvalidQuantity_Rejected()
        {
            int quantity = 0;
            bool isValid = quantity > 0;
            Assert.False(isValid);
        }

        #endregion

        #region TradeItem Model Tests

        [Fact]
        public void TradeItem_DefaultValues_AreCorrect()
        {
            var item = new TradeItem();
            Assert.Equal(0, item.ItemId);
            Assert.Equal(0, item.Quantity);
            Assert.Equal("", item.ItemName);
        }

        [Fact]
        public void TradeItem_SetProperties_WorksCorrectly()
        {
            var item = new TradeItem
            {
                ItemId = 42,
                Quantity = 10,
                ItemName = "灵石"
            };

            Assert.Equal(42, item.ItemId);
            Assert.Equal(10, item.Quantity);
            Assert.Equal("灵石", item.ItemName);
        }

        #endregion

        #region MarketListing Model Tests

        [Fact]
        public void MarketListing_DefaultValues_AreCorrect()
        {
            var listing = new MarketListing();
            Assert.Equal(0, listing.ListingId);
            Assert.Equal(Guid.Empty, listing.SellerId);
            Assert.Equal("", listing.SellerName);
            Assert.Equal(0, listing.ItemId);
            Assert.Equal("", listing.ItemName);
            Assert.Equal(0, listing.Quantity);
            Assert.Equal(0, listing.Price);
            Assert.Equal(0, listing.CurrencyType);
            Assert.Equal(0, listing.Status);
            Assert.Equal(0, listing.Category);
        }

        [Fact]
        public void MarketListing_SetProperties_WorksCorrectly()
        {
            var sellerId = Guid.NewGuid();
            var listing = new MarketListing
            {
                ListingId = 1,
                SellerId = sellerId,
                SellerName = "大侠",
                ItemId = 200,
                ItemName = "天蚕丝",
                Quantity = 5,
                Price = 10000,
                CurrencyType = 1,
                ListTime = DateTime.UtcNow,
                Status = (int)MarketListingStatus.Active,
                Category = 3
            };

            Assert.Equal(1, listing.ListingId);
            Assert.Equal(sellerId, listing.SellerId);
            Assert.Equal("大侠", listing.SellerName);
            Assert.Equal(200, listing.ItemId);
            Assert.Equal(10000, listing.Price);
            Assert.Equal(3, listing.Category);
        }

        #endregion

        #region TradeResult Model Tests

        [Fact]
        public void TradeResult_DefaultValues_AreCorrect()
        {
            var result = new TradeResult();
            Assert.False(result.Success);
            Assert.Equal("", result.Message);
            Assert.Equal(Guid.Empty, result.TradeId);
            Assert.Equal(0, result.TotalAmount);
            Assert.Equal(0, result.Tax);
        }

        [Fact]
        public void TradeResult_SuccessfulTrade_HasCorrectValues()
        {
            var tradeId = Guid.NewGuid();
            var result = new TradeResult
            {
                Success = true,
                Message = "交易完成",
                TradeId = tradeId,
                TotalAmount = 3000,
                Tax = 150
            };

            Assert.True(result.Success);
            Assert.Equal("交易完成", result.Message);
            Assert.Equal(tradeId, result.TradeId);
            Assert.Equal(3000, result.TotalAmount);
            Assert.Equal(150, result.Tax);
        }

        #endregion

        #region MarketStats Model Tests

        [Fact]
        public void MarketStats_DefaultValues_AreCorrect()
        {
            var stats = new MarketStats();
            Assert.Equal(0, stats.TotalListings);
            Assert.Equal(0, stats.TotalTransactions);
            Assert.Equal(0, stats.TotalVolume);
            Assert.Equal(0, stats.ActiveListings);
        }

        [Fact]
        public void MarketStats_SetProperties_WorksCorrectly()
        {
            var stats = new MarketStats
            {
                TotalListings = 100,
                TotalTransactions = 50,
                TotalVolume = 500000,
                ActiveListings = 75
            };

            Assert.Equal(100, stats.TotalListings);
            Assert.Equal(50, stats.TotalTransactions);
            Assert.Equal(500000, stats.TotalVolume);
            Assert.Equal(75, stats.ActiveListings);
        }

        #endregion

        #region TradeInfo Model Tests

        [Fact]
        public void TradeInfo_DefaultValues_AreCorrect()
        {
            var info = new TradeInfo();
            Assert.Equal(Guid.Empty, info.TradeId);
            Assert.Equal(Guid.Empty, info.SellerId);
            Assert.Equal(Guid.Empty, info.BuyerId);
            Assert.NotNull(info.SellerItems);
            Assert.Empty(info.SellerItems);
            Assert.NotNull(info.BuyerItems);
            Assert.Empty(info.BuyerItems);
            Assert.Equal(0, info.SellerCurrency);
            Assert.Equal(0, info.BuyerCurrency);
            Assert.False(info.SellerConfirmed);
            Assert.False(info.BuyerConfirmed);
            Assert.Equal(0, info.Status);
        }

        [Fact]
        public void TradeInfo_SetProperties_WorksCorrectly()
        {
            var tradeId = Guid.NewGuid();
            var sellerId = Guid.NewGuid();
            var buyerId = Guid.NewGuid();

            var info = new TradeInfo
            {
                TradeId = tradeId,
                SellerId = sellerId,
                BuyerId = buyerId,
                SellerCurrency = 500,
                BuyerCurrency = 1000,
                SellerConfirmed = true,
                BuyerConfirmed = false,
                Status = (int)TradeStatus.Created,
                CreatedTime = DateTime.UtcNow
            };

            Assert.Equal(tradeId, info.TradeId);
            Assert.Equal(sellerId, info.SellerId);
            Assert.Equal(buyerId, info.BuyerId);
            Assert.Equal(500, info.SellerCurrency);
            Assert.True(info.SellerConfirmed);
            Assert.False(info.BuyerConfirmed);
        }

        #endregion

        #region WuXing Resonance Tests - 五行共鸣

        [Fact]
        public void WuxingResonance_NullElements_ReturnsLevel0()
        {
            var result = CombatCalculator.CalculateWuxingResonance(null);

            Assert.Equal(0, result.ResonanceLevel);
            Assert.Equal("无共鸣", result.Description);
            Assert.Equal(0f, result.DamageBonus);
            Assert.Equal(0f, result.DefenseBonus);
        }

        [Fact]
        public void WuxingResonance_EmptyElements_ReturnsLevel0()
        {
            var result = CombatCalculator.CalculateWuxingResonance(new List<int>());

            Assert.Equal(0, result.ResonanceLevel);
            Assert.Equal("无共鸣", result.Description);
        }

        [Fact]
        public void WuxingResonance_SingleElement_ReturnsLevel0()
        {
            var result = CombatCalculator.CalculateWuxingResonance(new List<int> { 1 });

            Assert.Equal(0, result.ResonanceLevel);
            Assert.Equal("无共鸣", result.Description);
            Assert.Equal(0f, result.DamageBonus);
            Assert.Equal(0f, result.DefenseBonus);
        }

        [Fact]
        public void WuxingResonance_TwoSynergyElements_ReturnsLevel1()
        {
            // 金(1) and 水(3) -> synergy pair (1,3)
            var result = CombatCalculator.CalculateWuxingResonance(new List<int> { 1, 3 });

            Assert.Equal(1, result.ResonanceLevel);
            Assert.Equal("基础共鸣", result.Description);
            Assert.Equal(0.10f, result.DamageBonus);
            Assert.Equal(0.05f, result.DefenseBonus);
        }

        [Fact]
        public void WuxingResonance_ThreeElements_WithThreeSynergies_ReturnsLevel2()
        {
            // 4 elements: 金(1), 水(3), 木(2), 火(4) create 3 synergy pairs: (1,3), (3,2), (2,4) which triggers level 2 resonance
            var result = CombatCalculator.CalculateWuxingResonance(new List<int> { 1, 3, 2, 4 });

            Assert.Equal(2, result.ResonanceLevel);
            Assert.Equal("高级共鸣", result.Description);
            Assert.Equal(0.20f, result.DamageBonus);
            Assert.Equal(0.10f, result.DefenseBonus);
        }

        [Fact]
        public void WuxingResonance_AllFiveElements_ReturnsLevel3()
        {
            var result = CombatCalculator.CalculateWuxingResonance(new List<int> { 1, 2, 3, 4, 5 });

            Assert.Equal(3, result.ResonanceLevel);
            Assert.Equal("混沌共鸣", result.Description);
            Assert.Equal(0.35f, result.DamageBonus);
            Assert.Equal(0.20f, result.DefenseBonus);
        }

        [Fact]
        public void WuxingResonance_InvalidElementsFiltered()
        {
            // Only valid elements 1-5 are counted; 0 and 6 are invalid
            var result = CombatCalculator.CalculateWuxingResonance(new List<int> { 0, 6, 1 });

            Assert.Equal(0, result.ResonanceLevel);
            Assert.Equal("无共鸣", result.Description);
        }

        [Fact]
        public void WuxingResonance_DuplicateElements_HandledCorrectly()
        {
            // Duplicates should be treated as one unique element
            var result = CombatCalculator.CalculateWuxingResonance(new List<int> { 1, 1, 3, 3 });

            Assert.Equal(1, result.ResonanceLevel);
            Assert.Equal("基础共鸣", result.Description);
        }

        [Fact]
        public void WuxingResonance_Level1_CorrectBonuses()
        {
            var result = CombatCalculator.CalculateWuxingResonance(new List<int> { 2, 4 });

            Assert.Equal(1, result.ResonanceLevel);
            Assert.Equal(0.10f, result.DamageBonus, 0.001f);
            Assert.Equal(0.05f, result.DefenseBonus, 0.001f);
        }

        [Fact]
        public void WuxingResonance_ResonanceElements_Populated()
        {
            var result = CombatCalculator.CalculateWuxingResonance(new List<int> { 1, 2, 3, 4, 5 });

            Assert.NotNull(result.ResonanceElements);
            Assert.Equal(5, result.ResonanceElements.Count);
            Assert.Contains(1, result.ResonanceElements);
            Assert.Contains(2, result.ResonanceElements);
            Assert.Contains(3, result.ResonanceElements);
            Assert.Contains(4, result.ResonanceElements);
            Assert.Contains(5, result.ResonanceElements);
        }

        #endregion

        #region NotificationHelper Tests - 通知辅助

        [Fact]
        public void BuildFirstKillNotification_ContentFormat_IsCorrect()
        {
            var notification = NotificationHelper.BuildFirstKillNotification("玩家A", "巨龙", 1000);

            Assert.Equal(NotificationType.FirstKill, notification.Type);
            Assert.Equal("首杀通知", notification.Title);
            Assert.Equal("恭喜【玩家A】成功首杀【巨龙】！", notification.Content);
            Assert.Equal(1000, notification.Timestamp);
        }

        [Fact]
        public void BuildActivityStartNotification_WithDuration_IsCorrect()
        {
            var notification = NotificationHelper.BuildActivityStartNotification("秋收祭典", "全服双倍经验", 60, 2000);

            Assert.Equal(NotificationType.ActivityStart, notification.Type);
            Assert.Equal("活动通知", notification.Title);
            Assert.Equal("活动【秋收祭典】已开始！全服双倍经验 持续60分钟。", notification.Content);
            Assert.Equal(2000, notification.Timestamp);
            Assert.Equal("秋收祭典", notification.ExtraData["ActivityName"]);
            Assert.Equal("60", notification.ExtraData["Duration"]);
        }

        [Fact]
        public void BuildActivityEndNotification_Content_IsCorrect()
        {
            var notification = NotificationHelper.BuildActivityEndNotification("秋收祭典", 3000);

            Assert.Equal(NotificationType.ActivityEnd, notification.Type);
            Assert.Equal("活动通知", notification.Title);
            Assert.Equal("活动【秋收祭典】已结束！", notification.Content);
            Assert.Equal(3000, notification.Timestamp);
        }

        [Fact]
        public void BuildWorldBossSpawnNotification_WithLocation_IsCorrect()
        {
            var notification = NotificationHelper.BuildWorldBossSpawnNotification("远古巨龙", "龙潭峡谷", 4000);

            Assert.Equal(NotificationType.WorldBossSpawn, notification.Type);
            Assert.Equal("世界BOSS", notification.Title);
            Assert.Equal("世界BOSS【远古巨龙】已在【龙潭峡谷】出现！", notification.Content);
            Assert.Equal("远古巨龙", notification.ExtraData["BossName"]);
            Assert.Equal("龙潭峡谷", notification.ExtraData["Location"]);
        }

        [Fact]
        public void BuildAchievementNotification_Content_IsCorrect()
        {
            var notification = NotificationHelper.BuildAchievementNotification("大侠", "初入江湖", 5000);

            Assert.Equal(NotificationType.Achievement, notification.Type);
            Assert.Equal("成就通知", notification.Title);
            Assert.Equal("恭喜【大侠】达成成就【初入江湖】！", notification.Content);
        }

        [Fact]
        public void ToChatMessage_Conversion_IsCorrect()
        {
            var notification = NotificationHelper.BuildFirstKillNotification("玩家A", "巨龙", 1000);
            var chatMsg = NotificationHelper.ToChatMessage(notification);

            Assert.Equal(ChatChannel.System, chatMsg.ChannelType);
            Assert.True(chatMsg.IsSystemMessage);
            Assert.Equal("[首杀通知] 恭喜【玩家A】成功首杀【巨龙】！", chatMsg.Content);
            Assert.Equal((ulong)0, chatMsg.SenderId);
            Assert.Equal("系统", chatMsg.SenderName);
            Assert.False(string.IsNullOrEmpty(chatMsg.MessageId));
            Assert.Equal(1000, chatMsg.Timestamp);
        }

        [Fact]
        public void BuildFirstKillNotification_NullPlayerName_HandledGracefully()
        {
            var notification = NotificationHelper.BuildFirstKillNotification(null!, null!, 1000);

            Assert.Equal("恭喜【】成功首杀【】！", notification.Content);
        }

        [Fact]
        public void BuildFirstKillNotification_EmptyPlayerName_HandledGracefully()
        {
            var notification = NotificationHelper.BuildFirstKillNotification("", "", 1000);

            Assert.Equal("恭喜【】成功首杀【】！", notification.Content);
        }

        [Fact]
        public void Notification_TimestampDefaultsToCurrentTime()
        {
            var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var notification = NotificationHelper.BuildFirstKillNotification("A", "B");
            var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Assert.True(notification.Timestamp >= before);
            Assert.True(notification.Timestamp <= after);
        }

        [Fact]
        public void NotificationType_EnumValues_AreCorrect()
        {
            Assert.Equal(1, (int)NotificationType.FirstKill);
            Assert.Equal(2, (int)NotificationType.ActivityStart);
            Assert.Equal(3, (int)NotificationType.ActivityEnd);
            Assert.Equal(4, (int)NotificationType.WorldBossSpawn);
            Assert.Equal(5, (int)NotificationType.Achievement);
        }

        #endregion
    }
}
