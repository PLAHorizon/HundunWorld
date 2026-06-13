using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 花卉市场Grain接口 - 负责市场价格快照管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IFlowerMarketGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 更新价格快照
        /// </summary>
        Task UpdateSnapshotAsync(FlowerPriceSnapshot snapshot);

        /// <summary>
        /// 获取最新价格快照
        /// </summary>
        Task<FlowerPriceSnapshot> GetLatestSnapshotAsync(int speciesId);

        /// <summary>
        /// 获取市场概览
        /// </summary>
        Task<List<FlowerPriceSnapshot>> GetMarketOverviewAsync();

        /// <summary>
        /// 获取平均价格
        /// </summary>
        Task<decimal> GetAveragePriceAsync(int speciesId);
    }

    /// <summary>
    /// 花卉品种Grain接口 - 负责价格预测与历史管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IFlowerSpeciesGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 预测价格
        /// </summary>
        Task<FlowerPriceForecast> PredictPriceAsync(ForecastTimeScale timeScale, int horizonDays);

        /// <summary>
        /// 更新价格历史
        /// </summary>
        Task UpdatePriceHistoryAsync(decimal price, DateTime timestamp);

        /// <summary>
        /// 获取价格历史
        /// </summary>
        Task<List<FlowerPriceSnapshot>> GetPriceHistoryAsync(DateTime startTime, DateTime endTime);

        /// <summary>
        /// 获取种植建议（基于预测数据）
        /// </summary>
        Task<PlantingSuggestion> GetPlantingSuggestionAsync();
    }

    /// <summary>
    /// 区域需求Grain接口 - 负责区域搜索指数与热度管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IRegionDemandGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 更新需求指数
        /// </summary>
        Task UpdateDemandAsync(int speciesId, double searchIndex, DateTime timestamp);

        /// <summary>
        /// 获取区域需求指数
        /// </summary>
        Task<Dictionary<int, double>> GetRegionalDemandAsync(int speciesId);

        /// <summary>
        /// 获取热门品种
        /// </summary>
        Task<List<int>> GetHotSpeciesAsync(int topN);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IIoTAlertRuleGrain : IGrainWithStringKey
    {
        Task<bool> EvaluateAsync(SensorReading reading);

        Task<AlertRuleState> GetRuleStateAsync();

        Task UpdateThresholdsAsync(AlertConditionType conditionType, decimal threshold, bool isEnabled);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IPriceAlertRuleGrain : IGrainWithStringKey
    {
        Task<bool> EvaluateAsync(SensorReading reading);

        Task<AlertRuleState> GetRuleStateAsync();

        Task UpdateRuleAsync(AlertConditionType conditionType, decimal threshold, bool isEnabled);

        Task CreateForecastAlertAsync(string speciesCode, string suggestionType, decimal priceChangePercent);
    }

    /// <summary>
    /// 通知Grain接口 - 负责用户订阅与推送管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface INotificationGrain : IGrainWithGuidKey
    {
        Task SubscribeAsync(int speciesId, NotifyChannel channel);

        Task UnsubscribeAsync(int speciesId, NotifyChannel channel);

        Task PushAlertAsync(AlertMessage alert);

        Task<List<AlertMessage>> GetPendingAlertsAsync();

        Task MarkAlertsAsReadAsync(List<long> alertRuleIds);

        Task MarkAllAlertsAsReadAsync();

        Task SetSilencePeriodAsync(int minutes);

        Task<NotificationChannelSettings> GetChannelSettingsAsync();

        Task SetChannelSettingsAsync(NotificationChannelSettings settings);
    }

    /// <summary>
    /// IoT设备Grain接口 - 负责传感器数据与阈值管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IIoTDeviceGrain : IGrainWithStringKey
    {
        /// <summary>
        /// 更新传感器读数
        /// </summary>
        Task UpdateReadingAsync(SensorReading reading);

        /// <summary>
        /// 获取最新读数
        /// </summary>
        Task<SensorReading> GetLatestReadingAsync();

        /// <summary>
        /// 设置阈值
        /// </summary>
        Task SetThresholdAsync(string metricName, double threshold);

        /// <summary>
        /// 获取所有阈值配置
        /// </summary>
        Task<Dictionary<string, double>> GetThresholdsAsync();

        /// <summary>
        /// 检查设备是否在线
        /// </summary>
        Task<bool> IsOnlineAsync();

        Task SendCommandAsync(string action, string payload);

        Task<DeviceTwinInfo> GetDeviceTwinAsync();

        Task SetDesiredPropertyAsync(string key, string value);

        Task UpdateReportedPropertyAsync(string key, string value);

        Task CompleteCommandAsync(string commandId, string responsePayload);
    }

    /// <summary>
    /// 预测调度Grain接口 - 负责定时预测与聚合任务
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IForecastSchedulerGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 触发每日预测
        /// </summary>
        Task TriggerDailyForecastAsync();

        /// <summary>
        /// 触发每小时聚合
        /// </summary>
        Task TriggerHourlyAggregationAsync();

        /// <summary>
        /// 获取上次运行时间
        /// </summary>
        Task<DateTime> GetLastRunTimeAsync(string taskName);
    }

    /// <summary>
    /// 花卉订阅Grain接口 - 负责用户订阅管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IFlowerSubscriptionGrain : IGrainWithGuidKey
    {
        Task<List<FlowerSubscriptionInfo>> GetSubscriptionsAsync();

        Task<FlowerSubscriptionInfo> CreateSubscriptionAsync(FlowerSubscriptionInfo subscription);

        Task<bool> CancelSubscriptionAsync(long subscriptionId);

        Task<FlowerSubscriptionInfo?> GetActiveSubscriptionAsync();

        Task<FlowerSubscriptionInfo> UpgradeSubscriptionAsync(int newLevel, string paymentMethod);

        Task<bool> UpdateAutoRenewAsync(bool autoRenew);
    }

    /// <summary>
    /// 花卉预警管理Grain接口 - 负责用户预警规则与日志管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IFlowerAlertManagementGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 获取用户预警规则列表
        /// </summary>
        Task<List<FlowerAlertRuleInfo>> GetAlertRulesAsync();

        /// <summary>
        /// 创建预警规则
        /// </summary>
        Task<FlowerAlertRuleInfo> CreateAlertRuleAsync(FlowerAlertRuleInfo rule);

        /// <summary>
        /// 更新预警规则
        /// </summary>
        Task<FlowerAlertRuleInfo> UpdateAlertRuleAsync(long ruleId, int conditionType, decimal thresholdValue, bool isEnabled);

        /// <summary>
        /// 删除预警规则
        /// </summary>
        Task<bool> DeleteAlertRuleAsync(long ruleId);

        /// <summary>
        /// 获取用户预警日志
        /// </summary>
        Task<List<FlowerAlertLogInfo>> GetAlertLogsAsync(int skip, int take);
    }

    /// <summary>
    /// 商品Grain接口 - 负责商品管理与库存控制
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IProductGrain : IGrainWithIntegerKey
    {
        Task<ProductState> GetProductAsync();
        Task<ProductState> CreateProductAsync(ProductState product);
        Task<ProductState> UpdateProductAsync(ProductState product);
        Task<bool> SetProductActiveAsync(long productId, bool isActive);
        Task<bool> DeductStockAsync(int quantity, long orderId);
        Task<bool> AddStockAsync(int quantity, string reason);
        Task<bool> DeleteProductAsync(long productId);
        Task<ProductState> AuditProductAsync(long productId, bool approved, string reason);
        Task<List<ProductSKUState>> GetProductSKUsAsync(long productId);
        Task<ProductSKUState> AddProductSKUAsync(ProductSKUState sku);
        Task<ProductSKUState> UpdateProductSKUAsync(ProductSKUState sku);
        Task<bool> DeleteProductSKUAsync(long skuId);
        Task<List<ProductLadderPriceState>> GetLadderPricesAsync(long productId);
        Task SetLadderPricesAsync(long productId, List<ProductLadderPriceState> prices);
        Task<SuggestedPriceRange> GetSuggestedPriceAsync(int speciesId);
        Task<List<PriceAdjustmentSuggestion>> GetPriceAdjustmentSuggestionsAsync(long merchantId);
    }

    /// <summary>
    /// 订单Grain接口 - 负责订单状态机与生命周期管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IOrderGrain : IGrainWithIntegerKey
    {
        Task<OrderState> GetOrderAsync();
        Task<OrderState> CreateOrderAsync(OrderState order);
        Task<bool> PayOrderAsync(string paymentMethod);
        Task<bool> ShipOrderAsync();
        Task<bool> DeliverOrderAsync();
        Task<bool> CompleteOrderAsync();
        Task<bool> CancelOrderAsync(string reason);
        Task<bool> RequestRefundAsync(string reason);
        Task<OrderState> ShipOrderAsync(long orderId, string expressCompany, string shipOrderNumber, long shipperId = 0);
        Task<List<OrderState>> GetMerchantOrdersByStatusAsync(long merchantId, int? status, int page, int pageSize);
        Task<bool> NotifyPresaleReadyAsync(long orderId);
        Task<bool> RepurchaseAsync(Guid buyerId, long originalOrderId);
        Task<List<RepurchaseState>> GetFrequentProductsAsync(Guid buyerId, int topN);
        Task<List<OrderState>> BatchShipOrdersAsync(BatchShipRequest request);
    }

    /// <summary>
    /// 支付交易Grain接口 - 负责支付流程控制
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IPaymentTransactionGrain : IGrainWithIntegerKey
    {
        Task<PaymentState> GetTransactionAsync();
        Task<PaymentState> CreatePrepayAsync(long orderId, PaymentChannel channel, decimal amount, Guid buyerId, string idempotencyKey, PaymentScene scene = PaymentScene.Native);
        Task<bool> HandlePaymentCallbackAsync(string channelTransactionNo, string channelResponse, PaymentChannel channel);
        Task<bool> ExpireTransactionAsync();
        Task<bool> RefundAsync(decimal refundAmount, string reason);
        Task<bool> TryLockForCallbackAsync(string lockKey);
        Task ReleaseCallbackLockAsync();
        Task ClearNeedsOrderSyncAsync();
    }

    /// <summary>
    /// 商户Grain接口 - 负责商户管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IMerchantGrain : IGrainWithIntegerKey
    {
        Task<MerchantState> GetMerchantAsync();
        Task<MerchantState> GetMerchantByPassportAsync(string passport);
        Task<MerchantState> RegisterMerchantAsync(MerchantState merchant);
        Task<MerchantState> UpdateMerchantAsync(MerchantState merchant);
        Task<bool> VerifyMerchantAsync();
        Task<MerchantState> UpdateMerchantStageAsync(long merchantId, int stage, MerchantState merchant);
        Task<MerchantState> AuditMerchantAsync(long merchantId, bool approved, string refuseReason);
        Task<bool> FreezeMerchantAsync(long merchantId);
        Task<bool> UnfreezeMerchantAsync(long merchantId);
        Task<List<ShopShipperState>> GetShippersAsync(long merchantId);
        Task<ShopShipperState> AddShipperAsync(long merchantId, ShopShipperState shipper);
        Task<ShopShipperState> UpdateShipperAsync(ShopShipperState shipper);
        Task<bool> DeleteShipperAsync(long merchantId, long shipperId);
    }

    /// <summary>
    /// 购物车Grain接口 - 负责购物车操作
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IShoppingCartGrain : IGrainWithGuidKey
    {
        Task<CartState> GetCartAsync();
        Task<CartState> AddItemAsync(long productId, int quantity);
        Task<CartState> UpdateItemQuantityAsync(long productId, int quantity);
        Task<CartState> RemoveItemAsync(long productId);
        Task ClearCartAsync();
    }

    /// <summary>
    /// 结算Grain接口 - 负责商户结算管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface ISettlementGrain : IGrainWithIntegerKey
    {
        Task<SettlementState> GetSettlementAsync();
        Task<SettlementState> CreateSettlementAsync(DateTime periodStart, DateTime periodEnd);
        Task<bool> CompleteSettlementAsync();
        Task<SettlementAccountState> GetSettlementAccountAsync(long merchantId);
        Task<SettlementAccountState> SaveSettlementAccountAsync(long merchantId, SettlementAccountState account);
        Task<List<SettlementState>> GetSettlementBillsAsync(long merchantId, int skip, int take);
        Task<List<SettlementDetailState>> GetSettlementDetailsAsync(long settlementBillId);
        Task<SettlementAccountSummaryState> GetAccountSummaryAsync(long merchantId);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface ITradeArchiveGrain : IGrainWithIntegerKey
    {
        Task<bool> ArchiveOrderAsync(long orderId, byte[] archiveData);
        Task<bool> ArchivePaymentAsync(long transactionId, byte[] archiveData);
        Task<bool> ArchiveRefundAsync(long refundId, byte[] archiveData);
        Task<bool> ArchiveSettlementAsync(long settlementId, byte[] archiveData);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IPaymentCallbackProcessorGrain : IGrainWithStringKey
    {
        Task<bool> ProcessAlipayCallbackAsync(Dictionary<string, string> callbackData);
        Task<bool> ProcessWechatCallbackAsync(string callbackData);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IReconciliationGrain : IGrainWithIntegerKey
    {
        Task<ReconciliationResult> RunReconciliationAsync();
        Task<DateTime> GetLastRunTimeAsync();
        Task<int> GetLastInconsistencyCountAsync();
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IDashboardGrain : IGrainWithIntegerKey
    {
        Task<DashboardOverview> GetOverviewAsync();
        Task<List<RegionalHeatmapEntry>> GetRegionalHeatmapAsync();
        Task<List<SupplyDemandEntry>> GetSupplyDemandAsync();
        Task<List<PriceTrendEntry>> GetPriceTrendAsync(int speciesId, int days);
        Task<string> GetAIMarketSummaryAsync();
        Task<DashboardStats> GetDashboardStatsAsync();
        Task<List<RegionalTradeData>> GetRegionalTradeDataAsync();
        Task<List<SupplyDemandData>> GetSupplyDemandDataAsync();
        Task<List<RecentTransaction>> GetRecentTransactionsAsync();
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IApiKeyManagementGrain : IGrainWithIntegerKey
    {
        Task<ApiKeyInfo> CreateApiKeyAsync(long ownerPassportId, string name, string plan);
        Task<List<ApiKeyInfo>> ListApiKeysAsync(long ownerPassportId);
        Task<bool> RevokeApiKeyAsync(long keyId, long ownerPassportId);
        Task<bool> RecordUsageAsync(string apiKey);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IFlowerQueryGrain : IGrainWithIntegerKey
    {
        Task<List<OrderState>> QueryOrdersByBuyerAsync(Guid buyerId, int skip, int take);
        Task<List<OrderState>> QueryOrdersByMerchantAsync(long merchantId, int skip, int take);
        Task<List<ProductState>> QueryProductsByMerchantAsync(long merchantId, int skip, int take);
        Task<List<ProductState>> QueryActiveProductsAsync(int speciesId, int skip, int take);
        Task<int> CountOrdersByBuyerAsync(Guid buyerId);
        Task<int> CountOrdersByMerchantAsync(long merchantId);
        Task<int> CountProductsByMerchantAsync(long merchantId);
        Task<Guid> GetUserIdAsync(string passportId);
    }

    /// <summary>
    /// 复购提醒Grain接口 - 负责定期扫描买家购买周期并推送复购提醒通知
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IFlowerRepurchaseReminderGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 手动触发复购提醒扫描
        /// </summary>
        Task TriggerRepurchaseScanAsync();

        /// <summary>
        /// 获取上次扫描时间
        /// </summary>
        Task<DateTime> GetLastScanTimeAsync();

        /// <summary>
        /// 获取待推送的复购提醒列表
        /// </summary>
        Task<List<RepurchaseReminderInfo>> GetPendingRemindersAsync();
    }

    [Serializable]
    [GenerateSerializer]
    public class RepurchaseReminderInfo
    {
        [Id(0)]
        public Guid BuyerId { get; set; }

        [Id(1)]
        public int SpeciesId { get; set; }

        [Id(2)]
        public long LastOrderId { get; set; }

        [Id(3)]
        public DateTime LastPurchaseTime { get; set; }

        [Id(4)]
        public double AverageCycleDays { get; set; }

        [Id(5)]
        public int DaysSinceLastPurchase { get; set; }

        [Id(6)]
        public string ReminderMessage { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class ApiKeyInfo
    {
        [Id(0)]
        public long KeyId { get; set; }
        [Id(1)]
        public string ApiKey { get; set; } = "";
        [Id(2)]
        public string Name { get; set; } = "";
        [Id(3)]
        public string Plan { get; set; } = "";
        [Id(4)]
        public bool IsEnabled { get; set; }
        [Id(5)]
        public long TotalCallCount { get; set; }
        [Id(6)]
        public DateTime? LastCallTime { get; set; }
        [Id(7)]
        public DateTime? ExpiresAt { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class ReconciliationResult
    {
        [Id(0)]
        public DateTime RunTime { get; set; }
        [Id(1)]
        public bool IsSuccess { get; set; }
        [Id(2)]
        public string ErrorMessage { get; set; } = "";
        [Id(3)]
        public List<ReconciliationInconsistency> Inconsistencies { get; set; } = new();
    }

    [Serializable]
    [GenerateSerializer]
    public class ReconciliationInconsistency
    {
        [Id(0)]
        public string EntityType { get; set; } = "";
        [Id(1)]
        public long EntityId { get; set; }
        [Id(2)]
        public string IssueType { get; set; } = "";
        [Id(3)]
        public string Description { get; set; } = "";
        [Id(4)]
        public string CurrentValue { get; set; } = "";
        [Id(5)]
        public string ExpectedValue { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class DashboardOverview
    {
        [Id(0)]
        public decimal TotalTransactionAmount { get; set; }
        [Id(1)]
        public int TotalOrderCount { get; set; }
        [Id(2)]
        public int CompletedOrderCount { get; set; }
        [Id(3)]
        public int PendingOrderCount { get; set; }
        [Id(4)]
        public int TodayAlertCount { get; set; }
        [Id(5)]
        public int UnreadAlertCount { get; set; }
        [Id(6)]
        public DateTime LastRefreshTime { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class RegionalHeatmapEntry
    {
        [Id(0)]
        public int RegionId { get; set; }
        [Id(1)]
        public int SpeciesId { get; set; }
        [Id(2)]
        public double DemandIndex { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class SupplyDemandEntry
    {
        [Id(0)]
        public long SpeciesId { get; set; }
        [Id(1)]
        public decimal AvgPrice { get; set; }
        [Id(2)]
        public int TotalVolume { get; set; }
        [Id(3)]
        public double PriceVolatility { get; set; }
        [Id(4)]
        public int TradeFrequency { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class PriceTrendEntry
    {
        [Id(0)]
        public DateTime Date { get; set; }
        [Id(1)]
        public decimal AvgPrice { get; set; }
        [Id(2)]
        public decimal MinPrice { get; set; }
        [Id(3)]
        public decimal MaxPrice { get; set; }
        [Id(4)]
        public int Volume { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class DashboardStats
    {
        [Id(0)]
        public decimal TodayTradeAmount { get; set; }
        [Id(1)]
        public int TradeCount { get; set; }
        [Id(2)]
        public int ActiveSpeciesCount { get; set; }
        [Id(3)]
        public int OnlineMerchantCount { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class RegionalTradeData
    {
        [Id(0)]
        public string RegionName { get; set; } = "";
        [Id(1)]
        public double DemandIndex { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class SupplyDemandData
    {
        [Id(0)]
        public string SpeciesName { get; set; } = "";
        [Id(1)]
        public int Supply { get; set; }
        [Id(2)]
        public int Demand { get; set; }
        [Id(3)]
        public decimal SupplyDemandRatio { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class RecentTransaction
    {
        [Id(0)]
        public string TradeTime { get; set; } = "";
        [Id(1)]
        public string SpeciesName { get; set; } = "";
        [Id(2)]
        public decimal Price { get; set; }
        [Id(3)]
        public int Quantity { get; set; }
        [Id(4)]
        public string Market { get; set; } = "";
    }

    public interface IShopGradeGrain : IGrainWithIntegerKey
    {
        Task<ShopGradeState> GetShopGradeAsync(long gradeId);
        Task<List<ShopGradeState>> GetAllShopGradesAsync();
        Task<ShopGradeState> AddShopGradeAsync(ShopGradeState grade);
        Task<ShopGradeState> UpdateShopGradeAsync(ShopGradeState grade);
        Task<bool> DeleteShopGradeAsync(long gradeId);
    }

    public interface IProductCategoryGrain : IGrainWithIntegerKey
    {
        Task<ProductCategoryState> GetCategoryAsync(long categoryId);
        Task<List<ProductCategoryState>> GetCategoryTreeAsync();
        Task<List<ProductCategoryState>> GetSubCategoriesAsync(long parentCategoryId);
        Task<ProductCategoryState> AddCategoryAsync(ProductCategoryState category);
        Task<ProductCategoryState> UpdateCategoryAsync(ProductCategoryState category);
        Task<bool> DeleteCategoryAsync(long categoryId);
    }

    public interface IFreightTemplateGrain : IGrainWithIntegerKey
    {
        Task<FreightTemplateState> GetTemplateAsync(long templateId);
        Task<List<FreightTemplateState>> GetMerchantTemplatesAsync(long merchantId);
        Task<FreightTemplateState> AddTemplateAsync(FreightTemplateState template);
        Task<FreightTemplateState> UpdateTemplateAsync(FreightTemplateState template);
        Task<bool> DeleteTemplateAsync(long templateId);
        Task<decimal> CalculateFreightAsync(long templateId, decimal quantity, string regionId);
    }

    public interface IOrderRefundGrain : IGrainWithIntegerKey
    {
        Task<OrderRefundState> GetRefundAsync(long refundId);
        Task<OrderRefundState> RequestRefundAsync(OrderRefundState refund);
        Task<OrderRefundState> SellerAuditRefundAsync(long refundId, bool approved, string remark);
        Task<OrderRefundState> PlatformAuditRefundAsync(long refundId, bool approved, string remark);
        Task<List<OrderRefundState>> GetMerchantRefundsAsync(long merchantId, int? status);
        Task<List<OrderRefundState>> GetBuyerRefundsAsync(Guid buyerId);
        Task<OrderRefundState> SubmitReturnShipmentAsync(long refundId, string expressCompanyName, string shipOrderNumber);
        Task<OrderRefundState> ConfirmReturnReceivedAsync(long refundId);
        Task<OrderRefundState> AutoConfirmReturnAsync(long refundId);
        Task<OrderRefundState> AutoCloseReturnAsync(long refundId);
        Task<OrderRefundState> OnRefundCompletedAsync(long refundId, long orderId, decimal refundAmount, decimal orderTotalAmount);
    }

    public interface IProductCommentGrain : IGrainWithIntegerKey
    {
        Task<ProductCommentState> SubmitCommentAsync(ProductCommentState comment);
        Task<ProductCommentState> ReplyCommentAsync(long commentId, string replyContent);
        Task<List<ProductCommentState>> GetProductCommentsAsync(long productId, int page, int pageSize);
        Task<List<ProductCommentState>> GetMerchantCommentsAsync(long merchantId, int page, int pageSize);
    }

    public interface ISettledConfigGrain : IGrainWithIntegerKey
    {
        Task<SettledConfigState> GetSettledConfigAsync();
        Task<SettledConfigState> UpdateSettledConfigAsync(SettledConfigState config);
    }

    public interface IBrandGrain : IGrainWithIntegerKey
    {
        Task<BrandState> GetBrandAsync(long brandId);
        Task<List<BrandState>> GetAllBrandsAsync();
        Task<BrandState> AddBrandAsync(BrandState brand);
        Task<BrandState> UpdateBrandAsync(BrandState brand);
        Task<bool> DeleteBrandAsync(long brandId);
        Task<ShopBrandApplyState> ApplyBrandAsync(ShopBrandApplyState apply);
        Task<ShopBrandApplyState> AuditBrandApplyAsync(long applyId, bool approved, string remark);
        Task<List<ShopBrandApplyState>> GetShopBrandAppliesAsync(long shopId);
    }

    public interface ICouponGrain : IGrainWithIntegerKey
    {
        Task<CouponState> CreateCouponAsync(CouponState coupon);
        Task<CouponState> GetCouponAsync(long couponId);
        Task<List<CouponState>> GetShopCouponsAsync(long shopId);
        Task<CouponRecordState> ReceiveCouponAsync(long couponId, Guid userId);
        Task<bool> UseCouponAsync(long recordId, long orderId);
        Task<List<CouponRecordState>> GetUserCouponsAsync(Guid userId);
        Task<int> ExpireCouponsAsync();
    }

    public interface IFullDiscountGrain : IGrainWithIntegerKey
    {
        Task<FullDiscountRuleState> GetRuleAsync(long ruleId);
        Task<List<FullDiscountRuleState>> GetShopRulesAsync(long shopId);
        Task<FullDiscountRuleState> AddRuleAsync(FullDiscountRuleState rule);
        Task<FullDiscountRuleState> UpdateRuleAsync(FullDiscountRuleState rule);
        Task<bool> DeleteRuleAsync(long ruleId);
        Task<decimal> CalculateDiscountAsync(long shopId, decimal orderAmount);
    }

    public interface ICashDepositGrain : IGrainWithIntegerKey
    {
        Task<CashDepositState> GetCashDepositAsync(long depositId);
        Task<List<CashDepositState>> GetShopCashDepositsAsync(long shopId);
        Task<CashDepositState> PayCashDepositAsync(CashDepositState deposit);
        Task<CashDepositState> DeductCashDepositAsync(long depositId, decimal amount);
    }

    public interface IBusinessCategoryGrain : IGrainWithIntegerKey
    {
        Task<BusinessCategoryState> GetBusinessCategoryAsync(long id);
        Task<List<BusinessCategoryState>> GetShopBusinessCategoriesAsync(long shopId);
        Task<BusinessCategoryState> ApplyBusinessCategoryAsync(BusinessCategoryState category);
        Task<BusinessCategoryState> AuditBusinessCategoryAsync(long id, bool approved, string remark);
    }

    public interface IProductDescriptionTemplateGrain : IGrainWithIntegerKey
    {
        Task<ProductDescriptionTemplateState> GetTemplateAsync(long templateId);
        Task<List<ProductDescriptionTemplateState>> GetShopTemplatesAsync(long shopId);
        Task<ProductDescriptionTemplateState> AddTemplateAsync(ProductDescriptionTemplateState template);
        Task<ProductDescriptionTemplateState> UpdateTemplateAsync(ProductDescriptionTemplateState template);
        Task<bool> DeleteTemplateAsync(long templateId);
    }

    public interface IProductRelationGrain : IGrainWithIntegerKey
    {
        Task<List<ProductRelationState>> GetProductRelationsAsync(long productId);
        Task<bool> SetProductRelationsAsync(long productId, List<ProductRelationState> relations);
    }

    public interface IOrderComplaintGrain : IGrainWithIntegerKey
    {
        Task<OrderComplaintState> SubmitComplaintAsync(OrderComplaintState complaint);
        Task<OrderComplaintState> GetComplaintAsync(long complaintId);
        Task<OrderComplaintState> GetOrderComplaintAsync(long orderId);
        Task<OrderComplaintState> HandleComplaintAsync(long complaintId, string replyContent);
        Task<List<OrderComplaintState>> GetShopComplaintsAsync(long shopId);
        Task<List<OrderComplaintState>> GetUserComplaintsAsync(Guid userId);
    }

    public interface ITradeCommentGrain : IGrainWithIntegerKey
    {
        Task<TradeCommentState> SubmitTradeCommentAsync(TradeCommentState comment);
        Task<TradeCommentState> GetOrderTradeCommentAsync(long orderId);
        Task<List<TradeCommentState>> GetShopTradeCommentsAsync(long shopId);
        Task<TradeCommentState> GetShopAverageScoreAsync(long shopId);
    }

    public interface IShopBillingGrain : IGrainWithIntegerKey
    {
        Task<PendingSettlementState> WritePendingSettlementAsync(PendingSettlementState pending);
        Task<List<PendingSettlementState>> GetPendingSettlementsAsync(long shopId);
        Task<SettlementState> SettleAsync(long shopId, DateTime periodStart, DateTime periodEnd);
        Task<ShopWithdrawState> RequestWithdrawAsync(ShopWithdrawState withdraw);
        Task<ShopWithdrawState> AuditWithdrawAsync(long withdrawId, bool approved, string remark);
        Task<List<ShopAccountItemState>> GetShopAccountItemsAsync(long shopId);
        Task<bool> RefundDeductFromPendingAsync(long orderId, decimal refundAmount);
        Task<SettlementAccountSummaryState> GetSettlementAccountSummaryAsync(long shopId);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IShippingAddressGrain : IGrainWithIntegerKey
    {
        Task<List<ShippingAddressState>> GetUserAddressesAsync(Guid userId);
        Task<ShippingAddressState> GetAddressAsync(long addressId);
        Task<ShippingAddressState> AddAddressAsync(ShippingAddressState address);
        Task<ShippingAddressState> UpdateAddressAsync(ShippingAddressState address);
        Task<bool> DeleteAddressAsync(Guid userId, long addressId);
        Task<bool> SetDefaultAddressAsync(Guid userId, long addressId);
        Task<ShippingAddressState> GetDefaultAddressAsync(Guid userId);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface ILogisticsGrain : IGrainWithIntegerKey
    {
        Task<LogisticsTrackState> QueryTrackAsync(long orderId, string expressCompanyName, string shipOrderNumber);
        Task<LogisticsTrackState> QueryReturnTrackAsync(long refundId, string expressCompanyName, string shipOrderNumber);
        Task<List<LogisticsTrackState>> GetTrackHistoryAsync(long orderId);
        Task CheckAndUpdateTrackAsync(long orderId);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IIoTDeviceManagementGrain : IGrainWithStringKey
    {
        Task<FlowerIoTDeviceInfo> RegisterDeviceAsync(FlowerIoTDeviceRegisterRequest request);
        Task<FlowerIoTDeviceInfo> GetDeviceAsync(string deviceCode, string passportId);
        Task<List<FlowerIoTDeviceInfo>> ListDevicesByGreenhouseAsync(string greenhouseId);
        Task<List<FlowerIoTDeviceInfo>> ListDevicesByGroupAsync(string groupId);
        Task UpdateOnlineStatusAsync(string deviceCode, string status);
        Task UpdateHeartbeatAsync(string deviceCode);
        Task DeleteDeviceAsync(string deviceCode);
        Task<FlowerDeviceGroupInfo> CreateGroupAsync(FlowerDeviceGroupCreateRequest request);
        Task<List<FlowerDeviceGroupInfo>> ListGroupsAsync(string Passport);
        Task DeleteGroupAsync(string groupId, string passportId);
        Task<FlowerDeviceGroupInfo> RenameGroupAsync(string groupId, string newName, string passportId);
        Task<FlowerIoTDeviceInfo> BindDeviceAsync(BindDeviceRequest request);
        Task<FlowerIoTDeviceInfo> UnbindDeviceAsync(string deviceCode);
        Task<FlowerIoTDeviceInfo> ChangeDeviceGroupAsync(string deviceCode, string groupId);
        Task<List<string>> GetAllGreenhouseIdsAsync();
        Task<List<FlowerIoTDeviceInfo>> ListAllDevicesAsync();
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface ISensorDataGrain : IGrainWithStringKey
    {
        Task ReportReadingAsync(SensorReading reading);
        Task ReportManualReadingAsync(SensorReading reading);
        Task<SensorReading> GetLatestReadingAsync(string deviceId);
        Task<List<SensorReading>> GetHistoryReadingsAsync(string deviceId, DateTime start, DateTime end);
        Task<Dictionary<string, double>> GetAggregatedStatsAsync(string deviceId, DateTime start, DateTime end);
        Task<List<SensorReading>> GetMultiDeviceReadingsAsync(List<string> deviceIds, DateTime start, DateTime end);
        Task<TrendAnalysisResult> GetTrendAnalysisAsync(string deviceId, DateTime start, DateTime end, string granularity);
        Task<MultiDeviceComparisonResult> GetMultiDeviceComparisonAsync(List<string> deviceIds, DateTime start, DateTime end);
        Task<HealthIndexResult> GetHealthIndexAsync(string greenhouseId, DateTime start, DateTime end);
        Task<List<AnomalyDataPoint>> GetAnomaliesAsync(string deviceId, DateTime start, DateTime end);
    }

    [global::Orleans.CodeGeneration.Version(1)]
    public interface IPlantingAdviceGrain : IGrainWithIntegerKey
    {
        Task<List<PlantingAdviceItem>> GenerateAdviceAsync(long batchId);
        Task<List<PlantingAdviceItem>> GetActiveAdviceAsync(long batchId);
        Task MarkAdviceExecutedAsync(long adviceId, string action);
        Task<List<PlantingAdviceItem>> GetAdviceByTypeAsync(long batchId, string adviceType);
    }

    [Serializable]
    [GenerateSerializer]
    public class FlowerIoTDeviceInfo
    {
        [Id(0)] public long Id { get; set; }
        [Id(1)] public string DeviceCode { get; set; } = "";
        [Id(2)] public string DeviceName { get; set; } = "";
        [Id(3)] public string DeviceType { get; set; } = "";
        [Id(4)] public string GreenhouseId { get; set; } = "";
        [Id(5)] public string GroupId { get; set; } = "";
        [Id(6)] public string Protocol { get; set; } = "";
        [Id(7)] public string MqttTopic { get; set; } = "";
        [Id(8)] public string ApiKey { get; set; } = "";
        [Id(9)] public string OnlineStatus { get; set; } = "Offline";
        [Id(10)] public string FirmwareVersion { get; set; } = "";
        [Id(11)] public DateTime? LastHeartbeatTime { get; set; }
        [Id(12)] public bool IsEnabled { get; set; }
        [Id(13)] public string BindingStatus { get; set; } = "Unbound";
        [Id(14)] public DateTime? BoundAt { get; set; }
        [Id(15)] public string Location { get; set; } = "";
        [Id(16)] public string Manufacturer { get; set; } = "";
        [Id(17)] public string Model { get; set; } = "";
        [Id(18)] public string SerialNumber { get; set; } = "";
        [Id(19)] public double? BatteryLevel { get; set; }
        [Id(20)] public double? SignalStrength { get; set; }
        [Id(21)] public string SensorCapabilities { get; set; } = "";
        [Id(22)] public DateTime? InstallDate { get; set; }
        [Id(23)] public string Remark { get; set; } = "";
        [Id(24)] public string Passport { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class FlowerIoTDeviceRegisterRequest
    {
        [Id(0)] public string DeviceName { get; set; } = "";
        [Id(1)] public string DeviceType { get; set; } = "";
        [Id(2)] public string GreenhouseId { get; set; } = "";
        [Id(3)] public string GroupId { get; set; } = "";
        [Id(4)] public string Protocol { get; set; } = "";
        [Id(5)] public string Location { get; set; } = "";
        [Id(6)] public string Manufacturer { get; set; } = "";
        [Id(7)] public string Model { get; set; } = "";
        [Id(8)] public string SensorCapabilities { get; set; } = "";
        [Id(9)] public string Remark { get; set; } = "";
        [Id(10)] public string Passport { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class FlowerDeviceGroupInfo
    {
        [Id(0)] public long Id { get; set; }
        [Id(1)] public string GroupName { get; set; } = "";
        [Id(2)] public string Description { get; set; } = "";
        [Id(3)] public string GreenhouseId { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class FlowerDeviceGroupCreateRequest
    {
        [Id(0)] public string GroupName { get; set; } = "";
        [Id(1)] public string Description { get; set; } = "";
        [Id(2)] public string GreenhouseId { get; set; } = "";
        [Id(3)] public string Passport { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class BindDeviceRequest
    {
        [Id(0)] public string DeviceCode { get; set; } = "";
        [Id(1)] public string GreenhouseId { get; set; } = "";
        [Id(2)] public string GroupId { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class TrendAnalysisResult
    {
        [Id(0)] public string DeviceId { get; set; } = "";
        [Id(1)] public string Granularity { get; set; } = "";
        [Id(2)] public List<TrendDataPoint> DataPoints { get; set; } = new();
        [Id(3)] public List<SignificantChangePoint> SignificantChanges { get; set; } = new();
    }

    [Serializable]
    [GenerateSerializer]
    public class TrendDataPoint
    {
        [Id(0)] public DateTime Time { get; set; }
        [Id(1)] public double AvgTemperature { get; set; }
        [Id(2)] public double AvgHumidity { get; set; }
        [Id(3)] public double AvgLightIntensity { get; set; }
        [Id(4)] public double AvgCo2Level { get; set; }
        [Id(5)] public double AvgSoilMoisture { get; set; }
        [Id(6)] public double TemperatureChangeRate { get; set; }
        [Id(7)] public double HumidityChangeRate { get; set; }
        [Id(8)] public double LightChangeRate { get; set; }
        [Id(9)] public double Co2ChangeRate { get; set; }
        [Id(10)] public double SoilMoistureChangeRate { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class SignificantChangePoint
    {
        [Id(0)] public DateTime Time { get; set; }
        [Id(1)] public string Metric { get; set; } = "";
        [Id(2)] public double ChangeRate { get; set; }
        [Id(3)] public double PreviousValue { get; set; }
        [Id(4)] public double CurrentValue { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class MultiDeviceComparisonResult
    {
        [Id(0)] public List<DeviceComparisonItem> Devices { get; set; } = new();
        [Id(1)] public List<MetricDifference> Differences { get; set; } = new();
        [Id(2)] public string MaxDifferenceMetric { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class DeviceComparisonItem
    {
        [Id(0)] public string DeviceId { get; set; } = "";
        [Id(1)] public double AvgTemperature { get; set; }
        [Id(2)] public double AvgHumidity { get; set; }
        [Id(3)] public double AvgLightIntensity { get; set; }
        [Id(4)] public double AvgCo2Level { get; set; }
        [Id(5)] public double AvgSoilMoisture { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class MetricDifference
    {
        [Id(0)] public string Metric { get; set; } = "";
        [Id(1)] public double Difference { get; set; }
        [Id(2)] public double DifferencePercentage { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class HealthIndexResult
    {
        [Id(0)] public string GreenhouseId { get; set; } = "";
        [Id(1)] public double OverallScore { get; set; }
        [Id(2)] public double TemperatureScore { get; set; }
        [Id(3)] public double HumidityScore { get; set; }
        [Id(4)] public double LightScore { get; set; }
        [Id(5)] public double Co2Score { get; set; }
        [Id(6)] public double SoilMoistureScore { get; set; }
        [Id(7)] public DateTime CalculatedAt { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class AnomalyDataPoint
    {
        [Id(0)] public long ReadingId { get; set; }
        [Id(1)] public string DeviceId { get; set; } = "";
        [Id(2)] public string Metric { get; set; } = "";
        [Id(3)] public double Value { get; set; }
        [Id(4)] public double Mean { get; set; }
        [Id(5)] public double StdDev { get; set; }
        [Id(6)] public DateTime ReadingTime { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class PlantingAdviceItem
    {
        [Id(0)] public long Id { get; set; }
        [Id(1)] public long BatchId { get; set; }
        [Id(2)] public string AdviceType { get; set; } = "";
        [Id(3)] public string Title { get; set; } = "";
        [Id(4)] public string Content { get; set; } = "";
        [Id(5)] public string Source { get; set; } = "";
        [Id(6)] public string Priority { get; set; } = "Normal";
        [Id(7)] public string Status { get; set; } = "Pending";
        [Id(8)] public DateTime GeneratedTime { get; set; }
        [Id(9)] public DateTime? ExecutedTime { get; set; }
        [Id(10)] public string Action { get; set; } = "";
    }

    [GenerateSerializer]
    public enum PlantingSuggestionType
    {
        [Id(0)] Normal = 0,
        [Id(1)] ExpandPlanting = 1,
        [Id(2)] ReducePlanting = 2,
        [Id(3)] EarlyHarvest = 3
    }

    [Serializable]
    [GenerateSerializer]
    public class PlantingSuggestion
    {
        [Id(0)] public string SpeciesCode { get; set; } = "";
        [Id(1)] public PlantingSuggestionType SuggestionType { get; set; }
        [Id(2)] public string Reason { get; set; } = "";
        [Id(3)] public decimal PriceChangePercent { get; set; }
        [Id(4)] public decimal ForecastPrice { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class SuggestedPriceRange
    {
        [Id(0)] public int SpeciesId { get; set; }
        [Id(1)] public decimal MinPrice { get; set; }
        [Id(2)] public decimal MaxPrice { get; set; }
        [Id(3)] public decimal AvgForecastPrice { get; set; }
        [Id(4)] public string Reason { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class PriceAdjustmentSuggestion
    {
        [Id(0)] public long ProductId { get; set; }
        [Id(1)] public string ProductName { get; set; } = "";
        [Id(2)] public decimal CurrentPrice { get; set; }
        [Id(3)] public decimal SuggestedPrice { get; set; }
        [Id(4)] public decimal ChangePercent { get; set; }
        [Id(5)] public string Reason { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class DeviceTwinInfo
    {
        [Id(0)] public Dictionary<string, string> DesiredProperties { get; set; } = new();
        [Id(1)] public Dictionary<string, string> ReportedProperties { get; set; } = new();
        [Id(2)] public List<TwinPropertyDiff> Differences { get; set; } = new();
    }

    [Serializable]
    [GenerateSerializer]
    public class TwinPropertyDiff
    {
        [Id(0)] public string Key { get; set; } = "";
        [Id(1)] public string DesiredValue { get; set; }
        [Id(2)] public string ReportedValue { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class DeviceCommandPayload
    {
        [Id(0)] public string CommandId { get; set; } = "";
        [Id(1)] public string Action { get; set; } = "";
        [Id(2)] public string Payload { get; set; } = "";
        [Id(3)] public DateTime Timestamp { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class DeviceCommandResponse
    {
        [Id(0)] public string CommandId { get; set; } = "";
        [Id(1)] public string Action { get; set; } = "";
        [Id(2)] public bool Success { get; set; }
        [Id(3)] public string Result { get; set; } = "";
        [Id(4)] public DateTime Timestamp { get; set; }
    }
}
