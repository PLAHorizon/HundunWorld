using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model.Flower;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;

namespace Horizon.Entities
{

    /// <summary>
    /// 仅限用于数据库设计生成或修改数据库结构使用
    /// 不要在生产环境中使用
    /// </summary>
    public class FlowerEntityContextDes : DbContext, IDesignTimeDbContextFactory<FlowerEntityContextDes>
    {
        DbContextOptions ContextOptions { get; }

        private static readonly ILoggerFactory _loggerFactory
     = LoggerFactory.Create(builder => { builder.AddConsole(); });
        #region 设计
        public FlowerEntityContextDes()
        {
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (DesignTimeContextChecker.IsDesignTime())
            {
                var config = FlowerRepositoryConfigurationLocator.Build();
                var connStr = config.GetConnectionString("FlowerSqlServer");
                if (string.IsNullOrEmpty(connStr))
                    throw new InvalidOperationException("未能在 repository.json 中找到 FlowerSqlServer 连接字符串。");
                optionsBuilder.UseSqlServer(connStr);
            }
        }
        public FlowerEntityContextDes CreateDbContext(string[] args)
        {

            var config = FlowerRepositoryConfigurationLocator.Build();
            var connStr = config.GetConnectionString("FlowerSqlServer");
            if (string.IsNullOrEmpty(connStr))
                throw new InvalidOperationException("未能在 repository.json 中找到 FlowerSqlServer 连接字符串。");
            var optionsBuilder = new DbContextOptionsBuilder<FlowerEntityContextDes>();
            optionsBuilder.UseSqlServer(connStr).UseLoggerFactory(_loggerFactory).UseRootApplicationServiceProvider();
            return FastActivator.Create<FlowerEntityContextDes>(isnewInstance: true, args: optionsBuilder.Options);
        }
        #endregion
        public FlowerEntityContextDes(DbContextOptions options) : base(options)
        {
            ContextOptions = options;
            Database.AutoSavepointsEnabled = true;
            Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
            ChangeTracker.AutoDetectChangesEnabled = true;
            ChangeTracker.AcceptAllChanges();

        }

        #region  实体类
        public DbSet<FlowerSpecies> FlowerSpecies { get; set; }
        public DbSet<FlowerMarket> FlowerMarkets { get; set; }
        public DbSet<FlowerMarketSnapshot> FlowerMarketSnapshots { get; set; }
        public DbSet<FlowerDailyPriceStats> FlowerDailyPriceStats { get; set; }
        public DbSet<FlowerSensorReading> FlowerSensorReadings { get; set; }
        public DbSet<FlowerPredictionModel> FlowerPredictionModels { get; set; }
        public DbSet<FlowerPricePrediction> FlowerPricePredictions { get; set; }
        public DbSet<FlowerAlertRule> FlowerAlertRules { get; set; }
        public DbSet<FlowerAlertLog> FlowerAlertLogs { get; set; }
        public DbSet<FlowerUser> FlowerUsers { get; set; }
        public DbSet<FlowerDataPool> FlowerDataPools { get; set; }
        public DbSet<FlowerSubscription> FlowerSubscriptions { get; set; }
        public DbSet<FlowerMerchant> FlowerMerchants { get; set; }
        public DbSet<FlowerProduct> FlowerProducts { get; set; }
        public DbSet<FlowerOrder> FlowerOrders { get; set; }
        public DbSet<FlowerOrderItem> FlowerOrderItems { get; set; }
        public DbSet<FlowerPaymentTransaction> FlowerPaymentTransactions { get; set; }
        public DbSet<FlowerRefundOrder> FlowerRefundOrders { get; set; }
        public DbSet<FlowerSettlementBill> FlowerSettlementBills { get; set; }
        public DbSet<FlowerOrderLog> FlowerOrderLogs { get; set; }
        public DbSet<FlowerPaymentStatusChangeLog> FlowerPaymentStatusChangeLogs { get; set; }
        public DbSet<FlowerInventoryChangeLog> FlowerInventoryChangeLogs { get; set; }
        public DbSet<FlowerTradeArchive> FlowerTradeArchives { get; set; }
        public DbSet<FlowerDocument> FlowerDocuments { get; set; }
        public DbSet<FlowerChatHistory> FlowerChatHistories { get; set; }
        public DbSet<FlowerGeneratedReport> FlowerGeneratedReports { get; set; }
        public DbSet<FlowerShopGrade> FlowerShopGrades { get; set; }
        public DbSet<FlowerProductSKU> FlowerProductSKUs { get; set; }
        public DbSet<FlowerProductCategory> FlowerProductCategories { get; set; }
        public DbSet<FlowerShopCategory> FlowerShopCategories { get; set; }
        public DbSet<FlowerFreightTemplate> FlowerFreightTemplates { get; set; }
        public DbSet<FlowerProductLadderPrice> FlowerProductLadderPrices { get; set; }
        public DbSet<FlowerOrderRefund> FlowerOrderRefunds { get; set; }
        public DbSet<FlowerProductComment> FlowerProductComments { get; set; }
        public DbSet<FlowerShopShipper> FlowerShopShippers { get; set; }
        public DbSet<FlowerSettledConfig> FlowerSettledConfigs { get; set; }
        public DbSet<FlowerBrand> FlowerBrands { get; set; }
        public DbSet<FlowerShopBrandApply> FlowerShopBrandApplies { get; set; }
        public DbSet<FlowerCoupon> FlowerCoupons { get; set; }
        public DbSet<FlowerCouponRecord> FlowerCouponRecords { get; set; }
        public DbSet<FlowerFullDiscountRule> FlowerFullDiscountRules { get; set; }
        public DbSet<FlowerCashDeposit> FlowerCashDeposits { get; set; }
        public DbSet<FlowerBusinessCategory> FlowerBusinessCategories { get; set; }
        public DbSet<FlowerProductDescriptionTemplate> FlowerProductDescriptionTemplates { get; set; }
        public DbSet<FlowerProductRelation> FlowerProductRelations { get; set; }
        public DbSet<FlowerOrderComplaint> FlowerOrderComplaints { get; set; }
        public DbSet<FlowerTradeComment> FlowerTradeComments { get; set; }
        public DbSet<FlowerPendingSettlement> FlowerPendingSettlements { get; set; }
        public DbSet<FlowerShopWithdraw> FlowerShopWithdraws { get; set; }
        public DbSet<FlowerShopAccountItem> FlowerShopAccountItems { get; set; }
        public DbSet<FlowerShippingAddress> FlowerShippingAddresses { get; set; }
        public DbSet<FlowerDocumentChunk> FlowerDocumentChunks { get; set; }
        public DbSet<FlowerApiKey> FlowerApiKeys { get; set; }
        public DbSet<FlowerMerchantSettlementAccount> FlowerMerchantSettlementAccounts { get; set; }
        public DbSet<FlowerShoppingCart> FlowerShoppingCarts { get; set; }
        public DbSet<FlowerIoTDevice> FlowerIoTDevices { get; set; }
        public DbSet<FlowerDeviceGroup> FlowerDeviceGroups { get; set; }
        public DbSet<FlowerPlantingBatch> FlowerPlantingBatches { get; set; }
        public DbSet<FlowerCostRecord> FlowerCostRecords { get; set; }
        public DbSet<FlowerYieldRecord> FlowerYieldRecords { get; set; }
        public DbSet<FlowerPlantingAdvice> FlowerPlantingAdvices { get; set; }
        public DbSet<FlowerHarvestListing> FlowerHarvestListings { get; set; }
        public DbSet<FlowerReturnShipment> FlowerReturnShipments { get; set; }
        public DbSet<FlowerLogisticsTrack> FlowerLogisticsTracks { get; set; }
        public DbSet<FlowerSettlementDetail> FlowerSettlementDetails { get; set; }
        public DbSet<FlowerRepurchaseRecord> FlowerRepurchaseRecords { get; set; }
        #endregion



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            FlowerEntityIndexConfiguration.ConfigureIndexes(modelBuilder);
        }


    }


    public class FlowerEntityContext : DbContext
    {
        DbContextOptions ContextOptions { get; }

        private static readonly ILoggerFactory _loggerFactory
    = LoggerFactory.Create(builder => { builder.AddConsole(); });
        #region 设计
        public FlowerEntityContext()
        {
        }

        public FlowerEntityContext CreateDbContext(string[] args)
        {

            var config = FlowerRepositoryConfigurationLocator.Build();
            var connStr = config.GetConnectionString("FlowerSqlServer");
            if (string.IsNullOrEmpty(connStr))
                throw new InvalidOperationException("未能在 repository.json 中找到 FlowerSqlServer 连接字符串。");
            var optionsBuilder = new DbContextOptionsBuilder<FlowerEntityContext>();
            optionsBuilder.UseSqlServer(connStr).UseLoggerFactory(_loggerFactory).UseRootApplicationServiceProvider();
            return FastActivator.Create<FlowerEntityContext>(isnewInstance: true, args: optionsBuilder.Options);
        }
        #endregion
        public FlowerEntityContext(DbContextOptions options) : base(options)
        {
            ContextOptions = options;
            Database.AutoSavepointsEnabled = true;
            Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
            ChangeTracker.AutoDetectChangesEnabled = true;
            ChangeTracker.AcceptAllChanges();

        }

        #region  实体类
        public DbSet<FlowerSpecies> FlowerSpecies { get; set; }
        public DbSet<FlowerMarket> FlowerMarkets { get; set; }
        public DbSet<FlowerMarketSnapshot> FlowerMarketSnapshots { get; set; }
        public DbSet<FlowerDailyPriceStats> FlowerDailyPriceStats { get; set; }
        public DbSet<FlowerSensorReading> FlowerSensorReadings { get; set; }
        public DbSet<FlowerPredictionModel> FlowerPredictionModels { get; set; }
        public DbSet<FlowerPricePrediction> FlowerPricePredictions { get; set; }
        public DbSet<FlowerAlertRule> FlowerAlertRules { get; set; }
        public DbSet<FlowerAlertLog> FlowerAlertLogs { get; set; }
        public DbSet<FlowerUser> FlowerUsers { get; set; }
        public DbSet<FlowerDataPool> FlowerDataPools { get; set; }
        public DbSet<FlowerSubscription> FlowerSubscriptions { get; set; }
        public DbSet<FlowerMerchant> FlowerMerchants { get; set; }
        public DbSet<FlowerProduct> FlowerProducts { get; set; }
        public DbSet<FlowerOrder> FlowerOrders { get; set; }
        public DbSet<FlowerOrderItem> FlowerOrderItems { get; set; }
        public DbSet<FlowerPaymentTransaction> FlowerPaymentTransactions { get; set; }
        public DbSet<FlowerRefundOrder> FlowerRefundOrders { get; set; }
        public DbSet<FlowerSettlementBill> FlowerSettlementBills { get; set; }
        public DbSet<FlowerOrderLog> FlowerOrderLogs { get; set; }
        public DbSet<FlowerPaymentStatusChangeLog> FlowerPaymentStatusChangeLogs { get; set; }
        public DbSet<FlowerInventoryChangeLog> FlowerInventoryChangeLogs { get; set; }
        public DbSet<FlowerTradeArchive> FlowerTradeArchives { get; set; }
        public DbSet<FlowerMerchantSettlementAccount> FlowerMerchantSettlementAccounts { get; set; }
        public DbSet<FlowerDocument> FlowerDocuments { get; set; }
        public DbSet<FlowerChatHistory> FlowerChatHistories { get; set; }
        public DbSet<FlowerGeneratedReport> FlowerGeneratedReports { get; set; }
        public DbSet<FlowerShopGrade> FlowerShopGrades { get; set; }
        public DbSet<FlowerProductSKU> FlowerProductSKUs { get; set; }
        public DbSet<FlowerProductCategory> FlowerProductCategories { get; set; }
        public DbSet<FlowerShopCategory> FlowerShopCategories { get; set; }
        public DbSet<FlowerFreightTemplate> FlowerFreightTemplates { get; set; }
        public DbSet<FlowerProductLadderPrice> FlowerProductLadderPrices { get; set; }
        public DbSet<FlowerOrderRefund> FlowerOrderRefunds { get; set; }
        public DbSet<FlowerProductComment> FlowerProductComments { get; set; }
        public DbSet<FlowerShopShipper> FlowerShopShippers { get; set; }
        public DbSet<FlowerSettledConfig> FlowerSettledConfigs { get; set; }
        public DbSet<FlowerBrand> FlowerBrands { get; set; }
        public DbSet<FlowerShopBrandApply> FlowerShopBrandApplies { get; set; }
        public DbSet<FlowerCoupon> FlowerCoupons { get; set; }
        public DbSet<FlowerCouponRecord> FlowerCouponRecords { get; set; }
        public DbSet<FlowerFullDiscountRule> FlowerFullDiscountRules { get; set; }
        public DbSet<FlowerCashDeposit> FlowerCashDeposits { get; set; }
        public DbSet<FlowerBusinessCategory> FlowerBusinessCategories { get; set; }
        public DbSet<FlowerProductDescriptionTemplate> FlowerProductDescriptionTemplates { get; set; }
        public DbSet<FlowerProductRelation> FlowerProductRelations { get; set; }
        public DbSet<FlowerOrderComplaint> FlowerOrderComplaints { get; set; }
        public DbSet<FlowerTradeComment> FlowerTradeComments { get; set; }
        public DbSet<FlowerPendingSettlement> FlowerPendingSettlements { get; set; }
        public DbSet<FlowerShopWithdraw> FlowerShopWithdraws { get; set; }
        public DbSet<FlowerShopAccountItem> FlowerShopAccountItems { get; set; }
        public DbSet<FlowerShippingAddress> FlowerShippingAddresses { get; set; }
        public DbSet<FlowerDocumentChunk> FlowerDocumentChunks { get; set; }
        public DbSet<FlowerApiKey> FlowerApiKeys { get; set; }
        public DbSet<FlowerShoppingCart> FlowerShoppingCarts { get; set; }
        public DbSet<FlowerIoTDevice> FlowerIoTDevices { get; set; }
        public DbSet<FlowerDeviceGroup> FlowerDeviceGroups { get; set; }
        public DbSet<FlowerPlantingBatch> FlowerPlantingBatches { get; set; }
        public DbSet<FlowerCostRecord> FlowerCostRecords { get; set; }
        public DbSet<FlowerYieldRecord> FlowerYieldRecords { get; set; }
        public DbSet<FlowerPlantingAdvice> FlowerPlantingAdvices { get; set; }
        public DbSet<FlowerHarvestListing> FlowerHarvestListings { get; set; }
        public DbSet<FlowerReturnShipment> FlowerReturnShipments { get; set; }
        public DbSet<FlowerLogisticsTrack> FlowerLogisticsTracks { get; set; }
        public DbSet<FlowerSettlementDetail> FlowerSettlementDetails { get; set; }
        public DbSet<FlowerRepurchaseRecord> FlowerRepurchaseRecords { get; set; }
        #endregion



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            FlowerEntityIndexConfiguration.ConfigureIndexes(modelBuilder);
        }


    }

    /// <summary>
    /// 数据库索引配置（共享方法）
    /// </summary>
    internal static class FlowerEntityIndexConfiguration
    {
        /// <summary>
        /// 配置数据库索引以优化查询性能
        /// </summary>
        public static void ConfigureIndexes(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FlowerSpecies>(entity =>
            {
                entity.HasIndex(e => e.SpeciesCode).HasDatabaseName("IX_FlowerSpecies_SpeciesCode");
                entity.HasIndex(e => e.Category).HasDatabaseName("IX_FlowerSpecies_Category");
            });

            modelBuilder.Entity<FlowerMarket>(entity =>
            {
                entity.HasIndex(e => e.MarketCode).HasDatabaseName("IX_FlowerMarket_MarketCode");
                entity.HasIndex(e => e.Region).HasDatabaseName("IX_FlowerMarket_Region");
            });

            modelBuilder.Entity<FlowerMarketSnapshot>(entity =>
            {
                entity.HasIndex(e => new { e.SpeciesId, e.MarketId, e.SnapshotTime }).HasDatabaseName("IX_FlowerMarketSnapshot_SpeciesId_MarketId_SnapshotTime");
                entity.HasIndex(e => e.SnapshotTime).HasDatabaseName("IX_FlowerMarketSnapshot_SnapshotTime");
            });

            modelBuilder.Entity<FlowerDailyPriceStats>(entity =>
            {
                entity.HasIndex(e => new { e.SpeciesId, e.MarketId, e.StatDate }).HasDatabaseName("IX_FlowerDailyPriceStats_SpeciesId_MarketId_StatDate");
                entity.HasIndex(e => e.StatDate).HasDatabaseName("IX_FlowerDailyPriceStats_StatDate");
            });

            modelBuilder.Entity<FlowerSensorReading>(entity =>
            {
                entity.HasIndex(e => e.DeviceId).HasDatabaseName("IX_FlowerSensorReading_DeviceId");
                entity.HasIndex(e => e.GreenhouseId).HasDatabaseName("IX_FlowerSensorReading_GreenhouseId");
                entity.HasIndex(e => e.ReadingTime).HasDatabaseName("IX_FlowerSensorReading_ReadingTime");
                entity.HasIndex(e => e.BatchId).HasDatabaseName("IX_FlowerSensorReading_BatchId");
            });

            modelBuilder.Entity<FlowerPredictionModel>(entity =>
            {
                entity.HasIndex(e => e.SpeciesId).HasDatabaseName("IX_FlowerPredictionModel_SpeciesId");
                entity.HasIndex(e => new { e.SpeciesId, e.ModelType, e.ModelVersion }).HasDatabaseName("IX_FlowerPredictionModel_SpeciesId_ModelType_ModelVersion");
            });

            modelBuilder.Entity<FlowerPricePrediction>(entity =>
            {
                entity.HasIndex(e => new { e.SpeciesId, e.MarketId, e.PredictDate }).HasDatabaseName("IX_FlowerPricePrediction_SpeciesId_MarketId_PredictDate");
                entity.HasIndex(e => e.ModelId).HasDatabaseName("IX_FlowerPricePrediction_ModelId");
            });

            modelBuilder.Entity<FlowerAlertRule>(entity =>
            {
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerAlertRule_UserId");
                entity.HasIndex(e => new { e.SpeciesId, e.MarketId }).HasDatabaseName("IX_FlowerAlertRule_SpeciesId_MarketId");
            });

            modelBuilder.Entity<FlowerAlertLog>(entity =>
            {
                entity.HasIndex(e => e.RuleId).HasDatabaseName("IX_FlowerAlertLog_RuleId");
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerAlertLog_UserId");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("IX_FlowerAlertLog_CreatedAt");
            });

            modelBuilder.Entity<FlowerUser>(entity =>
            {
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerUser_UserId");
                entity.HasIndex(e => e.MerchantId).HasDatabaseName("IX_FlowerUser_MerchantId");
            });

            modelBuilder.Entity<FlowerDataPool>(entity =>
            {
                entity.HasIndex(e => e.DataType).HasDatabaseName("IX_FlowerDataPool_DataType");
                entity.HasIndex(e => e.Timestamp).HasDatabaseName("IX_FlowerDataPool_Timestamp");
            });

            modelBuilder.Entity<FlowerSubscription>(entity =>
            {
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerSubscription_UserId");
                entity.HasIndex(e => e.EndDate).HasDatabaseName("IX_FlowerSubscription_EndDate");
            });

            modelBuilder.Entity<FlowerMerchant>(entity =>
            {
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerMerchant_UserId");
                entity.HasIndex(e => e.ShopName).HasDatabaseName("IX_FlowerMerchant_ShopName");
            });

            modelBuilder.Entity<FlowerProduct>(entity =>
            {
                entity.HasIndex(e => e.MerchantId).HasDatabaseName("IX_FlowerProduct_MerchantId");
                entity.HasIndex(e => e.SpeciesId).HasDatabaseName("IX_FlowerProduct_SpeciesId");
                entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_FlowerProduct_IsActive");
                entity.HasIndex(e => e.RelatedBatchId).HasDatabaseName("IX_FlowerProduct_RelatedBatchId");
            });

            modelBuilder.Entity<FlowerOrder>(entity =>
            {
                entity.HasIndex(e => e.OrderNo).HasDatabaseName("IX_FlowerOrder_OrderNo");
                entity.HasIndex(e => e.BuyerId).HasDatabaseName("IX_FlowerOrder_BuyerId");
                entity.HasIndex(e => e.MerchantId).HasDatabaseName("IX_FlowerOrder_MerchantId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerOrder_Status");
                entity.HasIndex(e => e.RelatedBatchId).HasDatabaseName("IX_FlowerOrder_RelatedBatchId");
            });

            modelBuilder.Entity<FlowerOrderItem>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerOrderItem_OrderId");
                entity.HasIndex(e => e.ProductId).HasDatabaseName("IX_FlowerOrderItem_ProductId");
            });

            modelBuilder.Entity<FlowerPaymentTransaction>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerPaymentTransaction_OrderId");
                entity.HasIndex(e => e.TransactionNo).HasDatabaseName("IX_FlowerPaymentTransaction_TransactionNo");
                entity.HasIndex(e => e.Channel).HasDatabaseName("IX_FlowerPaymentTransaction_Channel");
            });

            modelBuilder.Entity<FlowerRefundOrder>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerRefundOrder_OrderId");
                entity.HasIndex(e => e.RefundNo).HasDatabaseName("IX_FlowerRefundOrder_RefundNo");
            });

            modelBuilder.Entity<FlowerSettlementBill>(entity =>
            {
                entity.HasIndex(e => e.MerchantId).HasDatabaseName("IX_FlowerSettlementBill_MerchantId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerSettlementBill_Status");
            });

            modelBuilder.Entity<FlowerOrderLog>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerOrderLog_OrderId");
                entity.HasIndex(e => e.OperatedAt).HasDatabaseName("IX_FlowerOrderLog_OperatedAt");
            });

            modelBuilder.Entity<FlowerPaymentStatusChangeLog>(entity =>
            {
                entity.HasIndex(e => e.TransactionId).HasDatabaseName("IX_FlowerPaymentStatusChangeLog_TransactionId");
                entity.HasIndex(e => e.NotifyId).HasDatabaseName("IX_FlowerPaymentStatusChangeLog_NotifyId");
            });

            modelBuilder.Entity<FlowerInventoryChangeLog>(entity =>
            {
                entity.HasIndex(e => e.ProductId).HasDatabaseName("IX_FlowerInventoryChangeLog_ProductId");
                entity.HasIndex(e => e.ChangedAt).HasDatabaseName("IX_FlowerInventoryChangeLog_ChangedAt");
            });

            modelBuilder.Entity<FlowerTradeArchive>(entity =>
            {
                entity.HasIndex(e => e.ArchiveType).HasDatabaseName("IX_FlowerTradeArchive_ArchiveType");
                entity.HasIndex(e => e.ArchivedAt).HasDatabaseName("IX_FlowerTradeArchive_ArchivedAt");
            });

            modelBuilder.Entity<FlowerMerchantSettlementAccount>(entity =>
            {
                entity.HasIndex(e => e.MerchantId).HasDatabaseName("IX_FlowerMerchantSettlementAccount_MerchantId");
            });

            modelBuilder.Entity<FlowerDocument>(entity =>
            {
                entity.HasIndex(e => e.IsIndexed).HasDatabaseName("IX_FlowerDocument_IsIndexed");
                entity.HasIndex(e => e.Source).HasDatabaseName("IX_FlowerDocument_Source");
            });

            modelBuilder.Entity<FlowerChatHistory>(entity =>
            {
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerChatHistory_UserId");
                entity.HasIndex(e => e.ConversationId).HasDatabaseName("IX_FlowerChatHistory_ConversationId");
            });

            modelBuilder.Entity<FlowerGeneratedReport>(entity =>
            {
                entity.HasIndex(e => e.ReportType).HasDatabaseName("IX_FlowerGeneratedReport_ReportType");
                entity.HasIndex(e => e.ReportDate).HasDatabaseName("IX_FlowerGeneratedReport_ReportDate");
            });

            modelBuilder.Entity<FlowerShopGrade>(entity =>
            {
                entity.HasIndex(e => e.Name).HasDatabaseName("IX_FlowerShopGrade_Name");
            });

            modelBuilder.Entity<FlowerProductSKU>(entity =>
            {
                entity.HasIndex(e => e.ProductId).HasDatabaseName("IX_FlowerProductSKU_ProductId");
                entity.HasIndex(e => e.SkuCode).HasDatabaseName("IX_FlowerProductSKU_SkuCode");
            });

            modelBuilder.Entity<FlowerProductCategory>(entity =>
            {
                entity.HasIndex(e => e.ParentCategoryId).HasDatabaseName("IX_FlowerProductCategory_ParentCategoryId");
                entity.HasIndex(e => e.Depth).HasDatabaseName("IX_FlowerProductCategory_Depth");
            });

            modelBuilder.Entity<FlowerShopCategory>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerShopCategory_ShopId");
            });

            modelBuilder.Entity<FlowerFreightTemplate>(entity =>
            {
                entity.HasIndex(e => e.MerchantId).HasDatabaseName("IX_FlowerFreightTemplate_MerchantId");
            });

            modelBuilder.Entity<FlowerProductLadderPrice>(entity =>
            {
                entity.HasIndex(e => e.ProductId).HasDatabaseName("IX_FlowerProductLadderPrice_ProductId");
            });

            modelBuilder.Entity<FlowerOrderRefund>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerOrderRefund_OrderId");
                entity.HasIndex(e => e.RefundNo).HasDatabaseName("IX_FlowerOrderRefund_RefundNo");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerOrderRefund_Status");
                entity.HasIndex(e => e.MerchantId).HasDatabaseName("IX_FlowerOrderRefund_MerchantId");
            });

            modelBuilder.Entity<FlowerProductComment>(entity =>
            {
                entity.HasIndex(e => e.ProductId).HasDatabaseName("IX_FlowerProductComment_ProductId");
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerProductComment_OrderId");
            });

            modelBuilder.Entity<FlowerShopShipper>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerShopShipper_ShopId");
            });

            modelBuilder.Entity<FlowerBrand>(entity =>
            {
                entity.HasIndex(e => e.Name).HasDatabaseName("IX_FlowerBrand_Name");
                entity.HasIndex(e => e.AuditStatus).HasDatabaseName("IX_FlowerBrand_AuditStatus");
            });

            modelBuilder.Entity<FlowerShopBrandApply>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerShopBrandApply_ShopId");
                entity.HasIndex(e => e.AuditStatus).HasDatabaseName("IX_FlowerShopBrandApply_AuditStatus");
            });

            modelBuilder.Entity<FlowerCoupon>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerCoupon_ShopId");
                entity.HasIndex(e => e.CouponType).HasDatabaseName("IX_FlowerCoupon_CouponType");
                entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_FlowerCoupon_IsActive");
            });

            modelBuilder.Entity<FlowerCouponRecord>(entity =>
            {
                entity.HasIndex(e => e.CouponId).HasDatabaseName("IX_FlowerCouponRecord_CouponId");
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerCouponRecord_UserId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerCouponRecord_Status");
            });

            modelBuilder.Entity<FlowerFullDiscountRule>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerFullDiscountRule_ShopId");
                entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_FlowerFullDiscountRule_IsActive");
            });

            modelBuilder.Entity<FlowerCashDeposit>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerCashDeposit_ShopId");
                entity.HasIndex(e => e.CategoryId).HasDatabaseName("IX_FlowerCashDeposit_CategoryId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerCashDeposit_Status");
            });

            modelBuilder.Entity<FlowerBusinessCategory>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerBusinessCategory_ShopId");
                entity.HasIndex(e => e.CategoryId).HasDatabaseName("IX_FlowerBusinessCategory_CategoryId");
                entity.HasIndex(e => e.AuditStatus).HasDatabaseName("IX_FlowerBusinessCategory_AuditStatus");
            });

            modelBuilder.Entity<FlowerProductDescriptionTemplate>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerProductDescriptionTemplate_ShopId");
            });

            modelBuilder.Entity<FlowerProductRelation>(entity =>
            {
                entity.HasIndex(e => e.ProductId).HasDatabaseName("IX_FlowerProductRelation_ProductId");
            });

            modelBuilder.Entity<FlowerOrderComplaint>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerOrderComplaint_OrderId");
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerOrderComplaint_UserId");
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerOrderComplaint_ShopId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerOrderComplaint_Status");
            });

            modelBuilder.Entity<FlowerTradeComment>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerTradeComment_OrderId");
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerTradeComment_ShopId");
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerTradeComment_UserId");
            });

            modelBuilder.Entity<FlowerPendingSettlement>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerPendingSettlement_OrderId");
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerPendingSettlement_ShopId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerPendingSettlement_Status");
            });

            modelBuilder.Entity<FlowerShopWithdraw>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerShopWithdraw_ShopId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerShopWithdraw_Status");
            });

            modelBuilder.Entity<FlowerShopAccountItem>(entity =>
            {
                entity.HasIndex(e => e.ShopId).HasDatabaseName("IX_FlowerShopAccountItem_ShopId");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("IX_FlowerShopAccountItem_CreatedAt");
            });

            modelBuilder.Entity<FlowerDocumentChunk>(entity =>
            {
                entity.HasIndex(e => e.DocumentId).HasDatabaseName("IX_FlowerDocumentChunk_DocumentId");
                entity.HasIndex(e => e.IsIndexed).HasDatabaseName("IX_FlowerDocumentChunk_IsIndexed");
                entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex }).HasDatabaseName("IX_FlowerDocumentChunk_DocumentId_ChunkIndex");
            });

            modelBuilder.Entity<FlowerApiKey>(entity =>
            {
                entity.HasIndex(e => e.ApiKey).HasDatabaseName("IX_FlowerApiKey_ApiKey");
                entity.HasIndex(e => e.OwnerPassportId).HasDatabaseName("IX_FlowerApiKey_OwnerPassportId");
            });

            modelBuilder.Entity<FlowerShoppingCart>(entity =>
            {
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_FlowerShoppingCart_UserId");
                entity.HasIndex(e => new { e.UserId, e.ProductId }).HasDatabaseName("IX_FlowerShoppingCart_UserId_ProductId");
            });

            modelBuilder.Entity<FlowerIoTDevice>(entity =>
            {
                entity.HasIndex(e => e.DeviceCode).HasDatabaseName("IX_FlowerIoTDevice_DeviceCode").IsUnique();
                entity.HasIndex(e => e.GreenhouseId).HasDatabaseName("IX_FlowerIoTDevice_GreenhouseId");
                entity.HasIndex(e => e.GroupId).HasDatabaseName("IX_FlowerIoTDevice_GroupId");
                entity.HasIndex(e => e.OnlineStatus).HasDatabaseName("IX_FlowerIoTDevice_OnlineStatus");
                entity.HasIndex(e => e.BindingStatus).HasDatabaseName("IX_FlowerIoTDevice_BindingStatus");
            });

            modelBuilder.Entity<FlowerDeviceGroup>(entity =>
            {
                entity.HasIndex(e => e.GreenhouseId).HasDatabaseName("IX_FlowerDeviceGroup_GreenhouseId");
            });

            modelBuilder.Entity<FlowerPlantingBatch>(entity =>
            {
                entity.HasIndex(e => e.SpeciesId).HasDatabaseName("IX_FlowerPlantingBatch_SpeciesId");
                entity.HasIndex(e => e.GreenhouseId).HasDatabaseName("IX_FlowerPlantingBatch_GreenhouseId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerPlantingBatch_Status");
                entity.HasIndex(e => e.PlantingDate).HasDatabaseName("IX_FlowerPlantingBatch_PlantingDate");
            });

            modelBuilder.Entity<FlowerCostRecord>(entity =>
            {
                entity.HasIndex(e => e.BatchId).HasDatabaseName("IX_FlowerCostRecord_BatchId");
                entity.HasIndex(e => e.Category).HasDatabaseName("IX_FlowerCostRecord_Category");
                entity.HasIndex(e => e.CostDate).HasDatabaseName("IX_FlowerCostRecord_CostDate");
            });

            modelBuilder.Entity<FlowerYieldRecord>(entity =>
            {
                entity.HasIndex(e => e.BatchId).HasDatabaseName("IX_FlowerYieldRecord_BatchId");
                entity.HasIndex(e => e.SpeciesId).HasDatabaseName("IX_FlowerYieldRecord_SpeciesId");
                entity.HasIndex(e => e.HarvestDate).HasDatabaseName("IX_FlowerYieldRecord_HarvestDate");
            });

            modelBuilder.Entity<FlowerPlantingAdvice>(entity =>
            {
                entity.HasIndex(e => e.BatchId).HasDatabaseName("IX_FlowerPlantingAdvice_BatchId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerPlantingAdvice_Status");
                entity.HasIndex(e => e.AdviceType).HasDatabaseName("IX_FlowerPlantingAdvice_AdviceType");
            });

            modelBuilder.Entity<FlowerHarvestListing>(entity =>
            {
                entity.HasIndex(e => e.YieldRecordId).HasDatabaseName("IX_FlowerHarvestListing_YieldRecordId");
                entity.HasIndex(e => e.ProductId).HasDatabaseName("IX_FlowerHarvestListing_ProductId");
                entity.HasIndex(e => e.BatchId).HasDatabaseName("IX_FlowerHarvestListing_BatchId");
                entity.HasIndex(e => e.MerchantId).HasDatabaseName("IX_FlowerHarvestListing_MerchantId");
                entity.HasIndex(e => e.SpeciesId).HasDatabaseName("IX_FlowerHarvestListing_SpeciesId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerHarvestListing_Status");
                entity.HasIndex(e => e.HarvestDate).HasDatabaseName("IX_FlowerHarvestListing_HarvestDate");
            });

            modelBuilder.Entity<FlowerReturnShipment>(entity =>
            {
                entity.HasIndex(e => e.RefundId).HasDatabaseName("IX_FlowerReturnShipment_RefundId");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_FlowerReturnShipment_Status");
            });

            modelBuilder.Entity<FlowerLogisticsTrack>(entity =>
            {
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerLogisticsTrack_OrderId");
                entity.HasIndex(e => new { e.ExpressCompanyName, e.ShipOrderNumber }).HasDatabaseName("IX_FlowerLogisticsTrack_Express_ShipNo");
                entity.HasIndex(e => e.LogisticsStatus).HasDatabaseName("IX_FlowerLogisticsTrack_LogisticsStatus");
            });

            modelBuilder.Entity<FlowerSettlementDetail>(entity =>
            {
                entity.HasIndex(e => e.SettlementBillId).HasDatabaseName("IX_FlowerSettlementDetail_SettlementBillId");
                entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_FlowerSettlementDetail_OrderId");
            });

            modelBuilder.Entity<FlowerRepurchaseRecord>(entity =>
            {
                entity.HasIndex(e => e.BuyerId).HasDatabaseName("IX_FlowerRepurchaseRecord_BuyerId");
                entity.HasIndex(e => e.OriginalOrderId).HasDatabaseName("IX_FlowerRepurchaseRecord_OriginalOrderId");
            });
        }
    }

    internal static class FlowerRepositoryConfigurationLocator
    {
        public static IConfiguration Build()
        {
            var basePath = ResolveBasePath();
            var config = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("repository.json", optional: false, reloadOnChange: false)
                .Build();

            var allKeys = config.AsEnumerable().Select(kvp => kvp.Key).ToArray();
            var connStrSection = config["ConnectionStrings:FlowerSqlServer"];

            return config;
        }

        private static string ResolveBasePath()
        {
            var assemblyFile = typeof(FlowerEntityContext).Assembly.Location;
            var assemblyDir = string.IsNullOrEmpty(assemblyFile) ? "" : Path.GetDirectoryName(assemblyFile);

            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(assemblyDir))
            {
                candidates.Add(assemblyDir);
                var parent = Directory.GetParent(assemblyDir);
                for (int i = 0; i < 6 && parent != null; i++)
                {
                    candidates.Add(parent.FullName);
                    parent = parent.Parent;
                }
            }

            var currentDir = Directory.GetCurrentDirectory();
            candidates.Add(currentDir);

            foreach (var candidate in candidates.Distinct())
            {
                var fullPath = Path.GetFullPath(candidate);
                var jsonPath = Path.Combine(fullPath, "repository.json");
                if (File.Exists(jsonPath))
                {
                    return fullPath;
                }
            }

            var dirs = string.Join("\n", candidates.Distinct().Select(c => "  - " + Path.GetFullPath(c)));
            throw new FileNotFoundException($"未找到 repository.json。已搜索以下目录:\n{dirs}");
        }
    }

}
