using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 数据池Grain接口 - 负责花卉市场数据采集与管理
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IFlowerDataPoolGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 写入数据到数据池
        /// </summary>
        Task<long> WriteAsync(DataPoolEntry entry);

        /// <summary>
        /// 按类型和时间范围查询数据
        /// </summary>
        Task<List<DataPoolEntry>> QueryByTypeAsync(DataPoolDataType dataType, DateTime? startTime, DateTime? endTime, int skip, int take);

        /// <summary>
        /// 导出数据为CSV/Parquet格式
        /// </summary>
        Task<byte[]> ExportAsync(DataPoolDataType dataType, DateTime startTime, DateTime endTime, string format);

        /// <summary>
        /// 获取训练数据数量
        /// </summary>
        Task<int> GetTrainingDataCountAsync(DataPoolDataType dataType, DateTime startTime, DateTime endTime);

        /// <summary>
        /// 获取训练数据
        /// </summary>
        Task<List<DataPoolEntry>> GetTrainingDataAsync(DataPoolDataType dataType, DateTime startTime, DateTime endTime, int skip, int take);

        Task<long> CreatePlantingBatchAsync(PlantingBatchState batch);
        Task<List<PlantingBatchState>> ListPlantingBatchesAsync(string greenhouseId, string status, int skip, int take);
        Task UpdatePlantingBatchStatusAsync(long batchId, string status, DateTime? actualHarvestDate);

        Task<long> AddCostRecordAsync(CostRecordState record);
        Task<List<CostRecordState>> GetCostRecordsAsync(long batchId, int skip, int take);
        Task<List<CostCategoryStats>> GetCostCategoryStatsAsync(long batchId, DateTime start, DateTime end);
        Task<List<CostMonthlyTrendInfo>> GetCostTrendAsync(string greenhouseId, int months);

        Task<long> AddYieldRecordAsync(YieldRecordState record);
        Task<List<YieldRecordState>> GetYieldRecordsAsync(long batchId, int skip, int take);
        Task<List<YieldTrendItem>> GetYieldTrendAsync(string greenhouseId, int months);

        Task<BatchLifecycle> GetBatchLifecycleAsync(long batchId);
        Task<BatchProfitAnalysis> GetBatchProfitAnalysisAsync(long batchId);
        Task<bool> CheckBatchCompletionAsync(long batchId);

        Task<HarvestListingResult> CreateProductFromYieldAsync(long yieldRecordId, long merchantId);
        Task<HarvestListingResult> BatchCreateProductsFromYieldAsync(List<long> yieldRecordIds, long merchantId);
        Task ConfirmHarvestListingAsync(long listingId, decimal actualPrice);
        Task<List<HarvestListingInfo>> GetHarvestListingsAsync(long merchantId, int status, int skip, int take);

        Task<PresaleFulfillmentStatus> CheckPresaleFulfillmentAsync(long batchId);
    }

    [Serializable]
    [GenerateSerializer]
    public class PresaleFulfillmentStatus
    {
        [Id(0)] public long BatchId { get; set; }
        [Id(1)] public decimal TotalPresaleDemand { get; set; }
        [Id(2)] public decimal TotalHarvested { get; set; }
        [Id(3)] public bool IsFulfilled { get; set; }
        [Id(4)] public List<PresaleOrderItem> PresaleOrders { get; set; } = new();
    }

    [Serializable]
    [GenerateSerializer]
    public class PresaleOrderItem
    {
        [Id(0)] public long OrderId { get; set; }
        [Id(1)] public string OrderNo { get; set; } = "";
        [Id(2)] public long ProductId { get; set; }
        [Id(3)] public string ProductName { get; set; } = "";
        [Id(4)] public int Quantity { get; set; }
        [Id(5)] public decimal Subtotal { get; set; }
        [Id(6)] public bool IsPresaleReadyNotified { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class HarvestListingResult
    {
        [Id(0)] public long ListingId { get; set; }
        [Id(1)] public long ProductId { get; set; }
        [Id(2)] public decimal SuggestedPrice { get; set; }
        [Id(3)] public bool Success { get; set; }
        [Id(4)] public string Message { get; set; } = "";
        [Id(5)] public List<HarvestListingGroupFailure> FailedGroups { get; set; } = new();
    }

    [Serializable]
    [GenerateSerializer]
    public class HarvestListingGroupFailure
    {
        [Id(0)] public string SpeciesId { get; set; } = "";
        [Id(1)] public string Grade { get; set; } = "";
        [Id(2)] public string Reason { get; set; } = "";
    }

    [Serializable]
    [GenerateSerializer]
    public class HarvestListingInfo
    {
        [Id(0)] public long Id { get; set; }
        [Id(1)] public long YieldRecordId { get; set; }
        [Id(2)] public long? ProductId { get; set; }
        [Id(3)] public long BatchId { get; set; }
        [Id(4)] public long MerchantId { get; set; }
        [Id(5)] public int SpeciesId { get; set; }
        [Id(6)] public string SpeciesName { get; set; } = "";
        [Id(7)] public string Grade { get; set; } = "";
        [Id(8)] public decimal Quantity { get; set; }
        [Id(9)] public string Unit { get; set; } = "";
        [Id(10)] public int Status { get; set; }
        [Id(11)] public decimal SuggestedPrice { get; set; }
        [Id(12)] public decimal ActualPrice { get; set; }
        [Id(13)] public string GreenhouseId { get; set; } = "";
        [Id(14)] public DateTime HarvestDate { get; set; }
        [Id(15)] public DateTime? ListedDate { get; set; }
    }
}
