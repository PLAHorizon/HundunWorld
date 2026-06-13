using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 数据池Grain实现 - 负责花卉市场数据采集与管理
    /// </summary>
    public class FlowerDataPoolGrain : Grain, IFlowerDataPoolGrain
    {
        private readonly ILogger<FlowerDataPoolGrain> _logger;
        private readonly IPersistentState<DataPoolState> _dataPoolState;
        private readonly IDataContext<FlowerEntityContext, FlowerDataPool, long> _dataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPlantingBatch, long> _batchContext;
        private readonly IDataContext<FlowerEntityContext, FlowerCostRecord, long> _costContext;
        private readonly IDataContext<FlowerEntityContext, FlowerYieldRecord, long> _yieldContext;
        private readonly IDataContext<FlowerEntityContext, FlowerHarvestListing, long> _listingContext;
        private readonly IDataContext<FlowerEntityContext, FlowerProduct, long> _productContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrderItem, long> _orderItemContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPendingSettlement, long> _pendingSettlementContext;
        private readonly IDataContext<FlowerEntityContext, FlowerSensorReading, long> _sensorReadingContext;

        public FlowerDataPoolGrain(
            ILogger<FlowerDataPoolGrain> logger,
            [PersistentState("datapool", "FlowerStore")] IPersistentState<DataPoolState> dataPoolState,
            IDataContext<FlowerEntityContext, FlowerDataPool, long> dataContext,
            IDataContext<FlowerEntityContext, FlowerPlantingBatch, long> batchContext,
            IDataContext<FlowerEntityContext, FlowerCostRecord, long> costContext,
            IDataContext<FlowerEntityContext, FlowerYieldRecord, long> yieldContext,
            IDataContext<FlowerEntityContext, FlowerHarvestListing, long> listingContext,
            IDataContext<FlowerEntityContext, FlowerProduct, long> productContext,
            IDataContext<FlowerEntityContext, FlowerOrderItem, long> orderItemContext,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IDataContext<FlowerEntityContext, FlowerPendingSettlement, long> pendingSettlementContext,
            IDataContext<FlowerEntityContext, FlowerSensorReading, long> sensorReadingContext)
        {
            _logger = logger;
            _dataPoolState = dataPoolState;
            _dataContext = dataContext;
            _batchContext = batchContext;
            _costContext = costContext;
            _yieldContext = yieldContext;
            _listingContext = listingContext;
            _productContext = productContext;
            _orderItemContext = orderItemContext;
            _orderContext = orderContext;
            _pendingSettlementContext = pendingSettlementContext;
            _sensorReadingContext = sensorReadingContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerDataPoolGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            if (_dataPoolState.State.EntriesByType == null)
                _dataPoolState.State.EntriesByType = new Dictionary<int, int>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<long> WriteAsync(DataPoolEntry entry)
        {
            try
            {
                if (entry == null)
                {
                    _logger.LogWarning("写入数据池条目无效: entry is null");
                    return 0;
                }

                var state = _dataPoolState.State;
                var nextId = state.LastEntryId + 1;

                var entity = new FlowerDataPool
                {
                    DataType = (int)entry.DataType,
                    DataSource = entry.DataSource,
                    RawPayload = entry.RawPayload,
                    Timestamp = entry.Timestamp,
                    RelatedEntityId = entry.RelatedEntityId,
                    ModelVersion = entry.ModelVersion,
                    Confidence = entry.Confidence,
                    Passport = this.GetPrimaryKeyLong().ToString(),
                    CreateTime = DateTime.Now,
                    IsValid = true
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("写入数据池失败: 数据库保存返回null");
                    return 0;
                }

                state.LastEntryId = result.Id;
                state.LastWriteTime = DateTime.Now;
                state.TotalEntries++;

                var typeKey = (int)entry.DataType;
                if (!state.EntriesByType.ContainsKey(typeKey))
                    state.EntriesByType[typeKey] = 0;
                state.EntriesByType[typeKey]++;

                await _dataPoolState.WriteStateAsync();

                _logger.LogInformation("写入数据池: Id={Id}, DataType={DataType}, Timestamp={Timestamp}",
                    result.Id, entry.DataType, entry.Timestamp);

                return result.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入数据池失败: DataType={DataType}", entry?.DataType);
                throw;
            }
        }

        public async Task<List<DataPoolEntry>> QueryByTypeAsync(DataPoolDataType dataType, DateTime? startTime, DateTime? endTime, int skip, int take)
        {
            try
            {
                var typeValue = (int)dataType;
                var query = await _dataContext.QueryAsync(e => e.DataType == typeValue);

                if (startTime.HasValue)
                    query = query.Where(e => e.Timestamp >= startTime.Value);

                if (endTime.HasValue)
                    query = query.Where(e => e.Timestamp <= endTime.Value);

                var results = query
                    .OrderByDescending(e => e.Timestamp)
                    .Skip(skip)
                    .Take(take)
                    .Select(e => new DataPoolEntry
                    {
                        Id = e.Id,
                        DataType = (DataPoolDataType)e.DataType,
                        DataSource = e.DataSource,
                        RawPayload = e.RawPayload ?? string.Empty,
                        Timestamp = e.Timestamp,
                        RelatedEntityId = e.RelatedEntityId ?? string.Empty,
                        ModelVersion = e.ModelVersion ?? string.Empty,
                        Confidence = e.Confidence
                    })
                    .ToList();

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询数据池失败: DataType={DataType}", dataType);
                throw;
            }
        }

        public async Task<byte[]> ExportAsync(DataPoolDataType dataType, DateTime startTime, DateTime endTime, string format)
        {
            try
            {
                var entries = await QueryByTypeAsync(dataType, startTime, endTime, 0, int.MaxValue);

                if (format?.Equals("csv", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Id,DataType,DataSource,Timestamp,RelatedEntityId,ModelVersion,Confidence");

                    foreach (var entry in entries)
                    {
                        sb.AppendLine($"{entry.Id},{entry.DataType},{entry.DataSource},{entry.Timestamp:O},{entry.RelatedEntityId},{entry.ModelVersion},{entry.Confidence}");
                    }

                    return Encoding.UTF8.GetBytes(sb.ToString());
                }

                var json = MemoryPackSerializer.Serialize(entries);
                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出数据池失败: DataType={DataType}, Format={Format}", dataType, format);
                throw;
            }
        }

        public async Task<int> GetTrainingDataCountAsync(DataPoolDataType dataType, DateTime startTime, DateTime endTime)
        {
            try
            {
                var typeValue = (int)dataType;
                var count = await _dataContext.CountAsync(e =>
                    e.DataType == typeValue &&
                    e.Timestamp >= startTime &&
                    e.Timestamp <= endTime);

                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取训练数据数量失败: DataType={DataType}", dataType);
                throw;
            }
        }

        public async Task<List<DataPoolEntry>> GetTrainingDataAsync(DataPoolDataType dataType, DateTime startTime, DateTime endTime, int skip, int take)
        {
            try
            {
                return await QueryByTypeAsync(dataType, startTime, endTime, skip, take);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取训练数据失败: DataType={DataType}", dataType);
                throw;
            }
        }

        public async Task<long> CreatePlantingBatchAsync(PlantingBatchState batch)
        {
            try
            {
                var entity = new FlowerPlantingBatch
                {
                    BatchName = batch.BatchName,
                    SpeciesId = batch.SpeciesId,
                    SpeciesName = batch.SpeciesName,
                    GreenhouseId = batch.GreenhouseId,
                    PlantingDate = batch.PlantingDate,
                    ExpectedHarvestDate = batch.ExpectedHarvestDate,
                    Status = batch.Status ?? "Planted",
                    PlantingQuantity = batch.PlantingQuantity,
                    Remark = batch.Remark,
                    UserId = batch.UserId,
                    Passport = batch.Passport,
                    IsDeleted = false
                };

                var result = await _batchContext.AddAsync(entity);
                return result?.Id ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建种植批次失败: BatchName={BatchName}", batch.BatchName);
                throw;
            }
        }

        public async Task<List<PlantingBatchState>> ListPlantingBatchesAsync(string greenhouseId, string status, int skip, int take)
        {
            try
            {
                var query = await _batchContext.QueryAsync(b => b.GreenhouseId == greenhouseId && !b.IsDeleted);
                if (!string.IsNullOrEmpty(status))
                    query = query.Where(b => b.Status == status);

                return query
                    .OrderByDescending(b => b.PlantingDate)
                    .Skip(skip)
                    .Take(take)
                    .Select(b => new PlantingBatchState
                    {
                        Id = b.Id,
                        BatchName = b.BatchName,
                        SpeciesId = b.SpeciesId,
                        SpeciesName = b.SpeciesName,
                        GreenhouseId = b.GreenhouseId,
                        PlantingDate = b.PlantingDate,
                        ExpectedHarvestDate = b.ExpectedHarvestDate,
                        ActualHarvestDate = b.ActualHarvestDate,
                        Status = b.Status,
                        PlantingQuantity = b.PlantingQuantity,
                        Remark = b.Remark,
                        UserId = b.UserId,
                        Passport = b.Passport
                    }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取种植批次列表失败: GreenhouseId={GreenhouseId}", greenhouseId);
                throw;
            }
        }

        public async Task UpdatePlantingBatchStatusAsync(long batchId, string status, DateTime? actualHarvestDate)
        {
            try
            {
                var batch = await _batchContext.QueryFirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
                if (batch != null)
                {
                    batch.Status = status;
                    if (actualHarvestDate.HasValue)
                        batch.ActualHarvestDate = actualHarvestDate;
                    await _batchContext.UpdateAsync(batch, batch.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新种植批次状态失败: BatchId={BatchId}", batchId);
                throw;
            }
        }

        public async Task<long> AddCostRecordAsync(CostRecordState record)
        {
            try
            {
                var entity = new FlowerCostRecord
                {
                    BatchId = record.BatchId,
                    Category = record.Category,
                    Amount = record.Amount,
                    CostDate = record.CostDate,
                    Remark = record.Remark,
                    IsDeleted = false
                };

                var result = await _costContext.AddAsync(entity);
                return result?.Id ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加成本记录失败: BatchId={BatchId}", record.BatchId);
                throw;
            }
        }

        public async Task<List<CostRecordState>> GetCostRecordsAsync(long batchId, int skip, int take)
        {
            try
            {
                var records = await _costContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted);
                return records
                    .OrderByDescending(r => r.CostDate)
                    .Skip(skip)
                    .Take(take)
                    .Select(r => new CostRecordState
                    {
                        Id = r.Id,
                        BatchId = r.BatchId,
                        Category = r.Category,
                        Amount = r.Amount,
                        CostDate = r.CostDate,
                        Remark = r.Remark
                    }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成本记录失败: BatchId={BatchId}", batchId);
                throw;
            }
        }

        public async Task<List<CostCategoryStats>> GetCostCategoryStatsAsync(long batchId, DateTime start, DateTime end)
        {
            try
            {
                var records = await _costContext.QueryAsync(r =>
                    r.BatchId == batchId && !r.IsDeleted &&
                    r.CostDate >= start && r.CostDate <= end);

                var list = records.ToList();
                var totalAmount = list.Sum(r => r.Amount);

                return list
                    .GroupBy(r => r.Category)
                    .Select(g => new CostCategoryStats
                    {
                        Category = g.Key,
                        TotalAmount = g.Sum(r => r.Amount),
                        RecordCount = g.Count(),
                        Percentage = totalAmount > 0 ? (double)(g.Sum(r => r.Amount) / totalAmount) * 100.0 : 0
                    })
                    .OrderByDescending(s => s.TotalAmount)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成本统计失败: BatchId={BatchId}", batchId);
                throw;
            }
        }

        public async Task<long> AddYieldRecordAsync(YieldRecordState record)
        {
            try
            {
                var entity = new FlowerYieldRecord
                {
                    BatchId = record.BatchId,
                    SpeciesId = record.SpeciesId,
                    SpeciesName = record.SpeciesName,
                    Quantity = record.Quantity,
                    Unit = record.Unit,
                    Grade = record.Grade,
                    HarvestDate = record.HarvestDate,
                    Remark = record.Remark,
                    IsDeleted = false
                };

                var result = await _yieldContext.AddAsync(entity);

                try
                {
                    var fulfillment = await CheckPresaleFulfillmentAsync(record.BatchId);
                    if (fulfillment.IsFulfilled)
                    {
                        foreach (var presaleOrder in fulfillment.PresaleOrders)
                        {
                            if (!presaleOrder.IsPresaleReadyNotified)
                            {
                                var orderGrain = GrainFactory.GetGrain<IOrderGrain>(presaleOrder.OrderId);
                                await orderGrain.NotifyPresaleReadyAsync(presaleOrder.OrderId);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "预售履行检查失败: BatchId={BatchId}", record.BatchId);
                }

                return result?.Id ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加产量记录失败: BatchId={BatchId}", record.BatchId);
                throw;
            }
        }

        public async Task<List<YieldRecordState>> GetYieldRecordsAsync(long batchId, int skip, int take)
        {
            try
            {
                var records = await _yieldContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted);
                return records
                    .OrderByDescending(r => r.HarvestDate)
                    .Skip(skip)
                    .Take(take)
                    .Select(r => new YieldRecordState
                    {
                        Id = r.Id,
                        BatchId = r.BatchId,
                        SpeciesId = r.SpeciesId,
                        SpeciesName = r.SpeciesName,
                        Quantity = r.Quantity,
                        Unit = r.Unit,
                        Grade = r.Grade,
                        HarvestDate = r.HarvestDate,
                        Remark = r.Remark
                    }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取产量记录失败: BatchId={BatchId}", batchId);
                throw;
            }
        }

        public async Task<List<YieldTrendItem>> GetYieldTrendAsync(string greenhouseId, int months)
        {
            try
            {
                var startDate = DateTime.Now.AddMonths(-months);
                var batches = await _batchContext.QueryAsync(b => b.GreenhouseId == greenhouseId && !b.IsDeleted);
                var batchIds = batches.Select(b => b.Id).ToList();

                var allRecords = new List<FlowerYieldRecord>();
                foreach (var batchId in batchIds)
                {
                    var records = await _yieldContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted && r.HarvestDate >= startDate);
                    allRecords.AddRange(records);
                }

                var lastYearStart = startDate.AddYears(-1);
                var lastYearEnd = DateTime.Now.AddYears(-1);
                var lastYearRecords = new List<FlowerYieldRecord>();
                foreach (var batchId in batchIds)
                {
                    var records = await _yieldContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted && r.HarvestDate >= lastYearStart && r.HarvestDate <= lastYearEnd);
                    lastYearRecords.AddRange(records);
                }

                var lastYearGroups = lastYearRecords
                    .GroupBy(r => new { Month = r.HarvestDate.ToString("yyyy-MM"), r.SpeciesName })
                    .ToDictionary(g => $"{g.Key.Month}_{g.Key.SpeciesName}", g => g.Sum(r => r.Quantity));

                return allRecords
                    .GroupBy(r => new { Month = r.HarvestDate.ToString("yyyy-MM"), r.SpeciesName })
                    .Select(g =>
                    {
                        var lastYearMonth = (int.Parse(g.Key.Month.Substring(0, 4)) - 1) + g.Key.Month.Substring(4);
                        var lastYearKey = $"{lastYearMonth}_{g.Key.SpeciesName}";
                        var lastYearQty = lastYearGroups.ContainsKey(lastYearKey) ? lastYearGroups[lastYearKey] : 0m;
                        return new YieldTrendItem
                        {
                            Month = g.Key.Month,
                            SpeciesName = g.Key.SpeciesName,
                            TotalQuantity = g.Sum(r => r.Quantity),
                            LastYearQuantity = lastYearQty
                        };
                    })
                    .OrderBy(t => t.Month)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取产量趋势失败: GreenhouseId={GreenhouseId}", greenhouseId);
                throw;
            }
        }

        public async Task<List<CostMonthlyTrendInfo>> GetCostTrendAsync(string greenhouseId, int months)
        {
            try
            {
                var startDate = DateTime.Now.AddMonths(-months);
                var batches = await _batchContext.QueryAsync(b => b.GreenhouseId == greenhouseId && !b.IsDeleted);
                var batchIds = batches.Select(b => b.Id).ToList();

                var allRecords = new List<FlowerCostRecord>();
                foreach (var batchId in batchIds)
                {
                    var records = await _costContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted && r.CostDate >= startDate);
                    allRecords.AddRange(records);
                }

                return allRecords
                    .GroupBy(r => r.CostDate.ToString("yyyy-MM"))
                    .Select(g => new CostMonthlyTrendInfo
                    {
                        Month = g.Key,
                        TotalAmount = g.Sum(r => r.Amount),
                        SeedlingCost = g.Where(r => r.Category == "Seedling").Sum(r => r.Amount),
                        FertilizerCost = g.Where(r => r.Category == "Fertilizer").Sum(r => r.Amount),
                        PesticideCost = g.Where(r => r.Category == "Pesticide").Sum(r => r.Amount),
                        LaborCost = g.Where(r => r.Category == "Labor").Sum(r => r.Amount),
                        UtilityCost = g.Where(r => r.Category == "Utility").Sum(r => r.Amount)
                    })
                    .OrderBy(t => t.Month)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成本趋势失败: GreenhouseId={GreenhouseId}", greenhouseId);
                throw;
            }
        }

        public async Task<HarvestListingResult> CreateProductFromYieldAsync(long yieldRecordId, long merchantId)
        {
            try
            {
                var yieldRecord = await _yieldContext.QueryFirstOrDefaultAsync(r => r.Id == yieldRecordId && !r.IsDeleted);
                if (yieldRecord == null)
                {
                    return new HarvestListingResult { Success = false, Message = "采收记录不存在" };
                }

                var existingListing = await _listingContext.QueryFirstOrDefaultAsync(l => l.YieldRecordId == yieldRecordId && !l.IsDeleted);
                if (existingListing != null)
                {
                    return new HarvestListingResult { Success = false, Message = "该采收记录已创建上架单" };
                }

                decimal suggestedPrice = 0;
                try
                {
                    var speciesGrain = GrainFactory.GetGrain<IFlowerSpeciesGrain>(int.TryParse(yieldRecord.SpeciesId, out var sid) ? sid : 0);
                    var forecast = await speciesGrain.PredictPriceAsync(ForecastTimeScale.ShortTerm, 7);
                    var avgPrice = forecast?.PredictedPrices?.Count > 0 ? forecast.PredictedPrices.Average(p => p.PredictedPrice) : 0m;
                    if (avgPrice > 0)
                    {
                        suggestedPrice = yieldRecord.Grade == "A" ? avgPrice
                            : yieldRecord.Grade == "B" ? avgPrice * 0.8m
                            : avgPrice * 0.6m;
                    }
                }
                catch
                {
                    suggestedPrice = yieldRecord.Grade == "A" ? 10m : yieldRecord.Grade == "B" ? 8m : 5m;
                }

                var batch = await _batchContext.QueryFirstOrDefaultAsync(b => b.Id == yieldRecord.BatchId && !b.IsDeleted);

                var product = new FlowerProduct
                {
                    MerchantId = merchantId,
                    SpeciesId = int.TryParse(yieldRecord.SpeciesId, out var spId) ? spId : 0,
                    ProductName = $"{yieldRecord.SpeciesName} {yieldRecord.Grade}级",
                    Description = $"采收自{(batch?.GreenhouseId ?? "未知温室")}，品种：{yieldRecord.SpeciesName}，等级：{yieldRecord.Grade}",
                    Price = suggestedPrice,
                    Stock = (int)yieldRecord.Quantity,
                    Unit = yieldRecord.Unit ?? "Stems",
                    IsActive = false,
                    IsDeleted = false
                };

                var productResult = await _productContext.AddAsync(product);
                if (productResult == null)
                {
                    return new HarvestListingResult { Success = false, Message = "创建商品失败" };
                }

                var listing = new FlowerHarvestListing
                {
                    YieldRecordId = yieldRecordId,
                    ProductId = productResult.Id,
                    BatchId = yieldRecord.BatchId,
                    MerchantId = merchantId,
                    SpeciesId = int.TryParse(yieldRecord.SpeciesId, out var sId) ? sId : 0,
                    SpeciesName = yieldRecord.SpeciesName ?? "",
                    Grade = yieldRecord.Grade ?? "A",
                    Quantity = yieldRecord.Quantity,
                    Unit = yieldRecord.Unit ?? "Stems",
                    Status = 0,
                    SuggestedPrice = suggestedPrice,
                    ActualPrice = 0,
                    GreenhouseId = batch?.GreenhouseId ?? "",
                    HarvestDate = yieldRecord.HarvestDate,
                    IsDeleted = false
                };

                var listingResult = await _listingContext.AddAsync(listing);
                if (listingResult == null)
                {
                    return new HarvestListingResult { Success = false, Message = "创建上架记录失败" };
                }

                _logger.LogInformation("从采收记录创建上架单: YieldRecordId={YieldRecordId}, ListingId={ListingId}, ProductId={ProductId}, SuggestedPrice={SuggestedPrice}",
                    yieldRecordId, listingResult.Id, productResult.Id, suggestedPrice);

                return new HarvestListingResult
                {
                    ListingId = listingResult.Id,
                    ProductId = productResult.Id,
                    SuggestedPrice = suggestedPrice,
                    Success = true,
                    Message = "创建成功"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从采收记录创建上架单失败: YieldRecordId={YieldRecordId}", yieldRecordId);
                throw;
            }
        }

        public async Task<HarvestListingResult> BatchCreateProductsFromYieldAsync(List<long> yieldRecordIds, long merchantId)
        {
            try
            {
                if (yieldRecordIds == null || yieldRecordIds.Count == 0)
                {
                    return new HarvestListingResult { Success = false, Message = "采收记录列表为空" };
                }

                var yieldRecords = new List<FlowerYieldRecord>();
                foreach (var id in yieldRecordIds)
                {
                    var record = await _yieldContext.QueryFirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);
                    if (record != null)
                        yieldRecords.Add(record);
                }

                if (yieldRecords.Count == 0)
                {
                    return new HarvestListingResult { Success = false, Message = "未找到有效的采收记录" };
                }

                var groups = yieldRecords.GroupBy(r => new { SpeciesId = r.SpeciesId, Grade = r.Grade });
                var firstListingId = 0L;
                var firstProductId = 0L;
                var suggestedPrice = 0m;
                var result = new HarvestListingResult { Success = true };

                foreach (var group in groups)
                {
                    var totalQuantity = group.Sum(r => r.Quantity);
                    var firstRecord = group.First();

                    decimal groupSuggestedPrice = 0;
                    try
                    {
                        var speciesGrain = GrainFactory.GetGrain<IFlowerSpeciesGrain>(int.TryParse(firstRecord.SpeciesId, out var sid) ? sid : 0);
                        var forecast = await speciesGrain.PredictPriceAsync(ForecastTimeScale.ShortTerm, 7);
                        var avgPrice = forecast?.PredictedPrices?.Count > 0 ? forecast.PredictedPrices.Average(p => p.PredictedPrice) : 0m;
                        if (avgPrice > 0)
                        {
                            groupSuggestedPrice = firstRecord.Grade == "A" ? avgPrice
                                : firstRecord.Grade == "B" ? avgPrice * 0.8m
                                : avgPrice * 0.6m;
                        }
                    }
                    catch
                    {
                        groupSuggestedPrice = firstRecord.Grade == "A" ? 10m : firstRecord.Grade == "B" ? 8m : 5m;
                    }

                    var batch = await _batchContext.QueryFirstOrDefaultAsync(b => b.Id == firstRecord.BatchId && !b.IsDeleted);

                    var product = new FlowerProduct
                    {
                        MerchantId = merchantId,
                        SpeciesId = int.TryParse(firstRecord.SpeciesId, out var spId) ? spId : 0,
                        ProductName = $"{firstRecord.SpeciesName} {firstRecord.Grade}级",
                        Description = $"批量采收自{(batch?.GreenhouseId ?? "未知温室")}，品种：{firstRecord.SpeciesName}，等级：{firstRecord.Grade}，共{group.Count()}条记录",
                        Price = groupSuggestedPrice,
                        Stock = (int)totalQuantity,
                        Unit = firstRecord.Unit ?? "Stems",
                        IsActive = false,
                        IsDeleted = false
                    };

                    var productResult = await _productContext.AddAsync(product);
                    if (productResult == null)
                    {
                        _logger.LogWarning("批量创建商品失败: SpeciesId={SpeciesId}, Grade={Grade}", firstRecord.SpeciesId, firstRecord.Grade);
                        result.FailedGroups.Add(new HarvestListingGroupFailure
                        {
                            SpeciesId = firstRecord.SpeciesId ?? "",
                            Grade = firstRecord.Grade ?? "",
                            Reason = "创建商品失败"
                        });
                        continue;
                    }

                    foreach (var record in group)
                    {
                        var listing = new FlowerHarvestListing
                        {
                            YieldRecordId = record.Id,
                            ProductId = productResult?.Id,
                            BatchId = record.BatchId,
                            MerchantId = merchantId,
                            SpeciesId = int.TryParse(record.SpeciesId, out var sId) ? sId : 0,
                            SpeciesName = record.SpeciesName ?? "",
                            Grade = record.Grade ?? "A",
                            Quantity = record.Quantity,
                            Unit = record.Unit ?? "Stems",
                            Status = 0,
                            SuggestedPrice = groupSuggestedPrice,
                            ActualPrice = 0,
                            GreenhouseId = batch?.GreenhouseId ?? "",
                            HarvestDate = record.HarvestDate,
                            IsDeleted = false
                        };

                        var listingResult = await _listingContext.AddAsync(listing);
                        if (listingResult != null && firstListingId == 0)
                        {
                            firstListingId = listingResult.Id;
                            firstProductId = productResult?.Id ?? 0;
                            suggestedPrice = groupSuggestedPrice;
                        }
                    }
                }

                _logger.LogInformation("批量创建上架单: {Count}条记录, {GroupCount}个分组", yieldRecords.Count, groups.Count());

                result.ListingId = firstListingId;
                result.ProductId = firstProductId;
                result.SuggestedPrice = suggestedPrice;
                result.Message = result.FailedGroups.Count > 0
                    ? $"批量创建完成，{result.FailedGroups.Count}个分组失败"
                    : $"批量创建成功，共{groups.Count()}个商品分组";

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建上架单失败");
                throw;
            }
        }

        public async Task ConfirmHarvestListingAsync(long listingId, decimal actualPrice)
        {
            try
            {
                var listing = await _listingContext.QueryFirstOrDefaultAsync(l => l.Id == listingId && !l.IsDeleted);
                if (listing == null)
                {
                    _logger.LogWarning("上架记录不存在: ListingId={ListingId}", listingId);
                    return;
                }

                listing.Status = 1;
                listing.ActualPrice = actualPrice;
                listing.ListedDate = DateTime.Now;
                await _listingContext.UpdateAsync(listing, listing.Id);

                if (listing.ProductId.HasValue && listing.ProductId.Value > 0)
                {
                    var product = await _productContext.QueryFirstOrDefaultAsync(p => p.Id == listing.ProductId.Value && !p.IsDeleted);
                    if (product != null)
                    {
                        product.IsActive = true;
                        product.Price = actualPrice;
                        await _productContext.UpdateAsync(product, product.Id);
                    }
                }

                _logger.LogInformation("确认上架: ListingId={ListingId}, ActualPrice={ActualPrice}", listingId, actualPrice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确认上架失败: ListingId={ListingId}", listingId);
                throw;
            }
        }

        public async Task<List<HarvestListingInfo>> GetHarvestListingsAsync(long merchantId, int status, int skip, int take)
        {
            try
            {
                var listings = await _listingContext.QueryAsync(l => l.MerchantId == merchantId && !l.IsDeleted);
                if (status >= 0)
                    listings = listings.Where(l => l.Status == status);

                return listings
                    .OrderByDescending(l => l.HarvestDate)
                    .Skip(skip)
                    .Take(take)
                    .Select(l => new HarvestListingInfo
                    {
                        Id = l.Id,
                        YieldRecordId = l.YieldRecordId,
                        ProductId = l.ProductId,
                        BatchId = l.BatchId,
                        MerchantId = l.MerchantId,
                        SpeciesId = l.SpeciesId,
                        SpeciesName = l.SpeciesName ?? "",
                        Grade = l.Grade ?? "",
                        Quantity = l.Quantity,
                        Unit = l.Unit ?? "",
                        Status = l.Status,
                        SuggestedPrice = l.SuggestedPrice,
                        ActualPrice = l.ActualPrice,
                        GreenhouseId = l.GreenhouseId ?? "",
                        HarvestDate = l.HarvestDate,
                        ListedDate = l.ListedDate
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取上架列表失败: MerchantId={MerchantId}", merchantId);
                throw;
            }
        }

        public async Task<BatchLifecycle> GetBatchLifecycleAsync(long batchId)
        {
            try
            {
                var batch = await _batchContext.QueryFirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
                if (batch == null)
                    return new BatchLifecycle();

                var batchInfo = new PlantingBatchState
                {
                    Id = batch.Id,
                    BatchName = batch.BatchName,
                    SpeciesId = batch.SpeciesId,
                    SpeciesName = batch.SpeciesName,
                    GreenhouseId = batch.GreenhouseId,
                    PlantingDate = batch.PlantingDate,
                    ExpectedHarvestDate = batch.ExpectedHarvestDate,
                    ActualHarvestDate = batch.ActualHarvestDate,
                    Status = batch.Status,
                    PlantingQuantity = batch.PlantingQuantity,
                    Remark = batch.Remark,
                    UserId = batch.UserId,
                    Passport = batch.Passport
                };

                var sensorSummary = new SensorDataSummary();
                try
                {
                    var sensorReadings = await _sensorReadingContext.QueryAsync(r =>
                        r.GreenhouseId == batch.GreenhouseId && r.BatchId == batchId);
                    var readingList = sensorReadings.ToList();
                    if (readingList.Count > 0)
                    {
                        sensorSummary = new SensorDataSummary
                        {
                            AvgTemperature = readingList.Average(r => r.Temperature),
                            AvgHumidity = readingList.Average(r => r.Humidity),
                            AvgLightIntensity = readingList.Average(r => r.LightIntensity),
                            AvgSoilMoisture = readingList.Average(r => r.SoilMoisture),
                            ReadingCount = readingList.Count,
                            FirstReadingTime = readingList.Min(r => r.ReadingTime),
                            LastReadingTime = readingList.Max(r => r.ReadingTime)
                        };
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to load sensor data for batch {BatchId}", batchId); }

                var costRecords = await _costContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted);
                var costList = costRecords.ToList();
                var totalCost = costList.Sum(r => r.Amount);
                var costBreakdown = costList
                    .GroupBy(r => r.Category)
                    .Select(g => new CostCategoryStats
                    {
                        Category = g.Key,
                        TotalAmount = g.Sum(r => r.Amount),
                        RecordCount = g.Count(),
                        Percentage = totalCost > 0 ? (double)(g.Sum(r => r.Amount) / totalCost) * 100.0 : 0
                    })
                    .OrderByDescending(s => s.TotalAmount)
                    .ToList();

                var yieldRecords = await _yieldContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted);
                var yieldList = yieldRecords
                    .OrderByDescending(r => r.HarvestDate)
                    .Select(r => new YieldRecordState
                    {
                        Id = r.Id,
                        BatchId = r.BatchId,
                        SpeciesId = r.SpeciesId,
                        SpeciesName = r.SpeciesName,
                        Quantity = r.Quantity,
                        Unit = r.Unit,
                        Grade = r.Grade,
                        HarvestDate = r.HarvestDate,
                        Remark = r.Remark
                    }).ToList();

                var listings = await _listingContext.QueryAsync(l => l.BatchId == batchId && !l.IsDeleted);
                var listedProducts = listings
                    .OrderByDescending(l => l.HarvestDate)
                    .Select(l => new HarvestListingSummary
                    {
                        Id = l.Id,
                        YieldRecordId = l.YieldRecordId,
                        ProductId = l.ProductId,
                        SpeciesName = l.SpeciesName ?? "",
                        Grade = l.Grade ?? "",
                        Quantity = l.Quantity,
                        ActualPrice = l.ActualPrice,
                        Status = l.Status,
                        HarvestDate = l.HarvestDate,
                        ListedDate = l.ListedDate
                    }).ToList();

                var productIds = listedProducts.Where(l => l.ProductId.HasValue).Select(l => l.ProductId.Value).Distinct().ToList();
                var orderSummaries = new List<OrderSummary>();
                var settlementTotal = 0m;

                if (productIds.Count > 0)
                {
                    var allOrderItems = await _orderItemContext.QueryAsync(oi => productIds.Contains(oi.ProductId));
                    var orderIds = allOrderItems.Select(oi => oi.OrderId).Distinct().ToList();

                    foreach (var orderId in orderIds)
                    {
                        var order = await _orderContext.QueryFirstOrDefaultAsync(o => o.Id == orderId);
                        if (order == null) continue;

                        var items = allOrderItems.Where(oi => oi.OrderId == orderId).ToList();
                        orderSummaries.Add(new OrderSummary
                        {
                            OrderId = order.Id,
                            OrderNo = order.OrderNo ?? "",
                            TotalAmount = order.TotalAmount,
                            Status = order.Status,
                            CreatedAt = order.CreateTime,
                            Items = items.Select(i => new OrderItemSummary
                            {
                                ProductId = i.ProductId,
                                ProductName = i.ProductName ?? "",
                                Quantity = i.Quantity,
                                Subtotal = i.Subtotal
                            }).ToList()
                        });
                    }

                    var merchantId = listings.First().MerchantId;
                    var pendingSettlements = await _pendingSettlementContext.QueryAsync(ps => ps.ShopId == merchantId && ps.Status >= 0);
                    var batchProductOrderIds = orderSummaries.Select(o => o.OrderId).ToHashSet();
                    settlementTotal = pendingSettlements
                        .Where(ps => batchProductOrderIds.Contains(ps.OrderId))
                        .Sum(ps => ps.SettleableAmount);
                }

                return new BatchLifecycle
                {
                    BatchInfo = batchInfo,
                    SensorDataSummary = sensorSummary,
                    TotalCost = totalCost,
                    CostBreakdown = costBreakdown,
                    YieldRecords = yieldList,
                    ListedProducts = listedProducts,
                    OrderSummaries = orderSummaries,
                    SettlementTotal = settlementTotal
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次生命周期数据失败: BatchId={BatchId}", batchId);
                throw;
            }
        }

        public async Task<BatchProfitAnalysis> GetBatchProfitAnalysisAsync(long batchId)
        {
            try
            {
                var costRecords = await _costContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted);
                var costList = costRecords.ToList();
                var totalCost = costList.Sum(r => r.Amount);

                var costBreakdown = costList
                    .GroupBy(r => r.Category)
                    .Select(g => new CostCategoryStats
                    {
                        Category = g.Key,
                        TotalAmount = g.Sum(r => r.Amount),
                        RecordCount = g.Count(),
                        Percentage = totalCost > 0 ? (double)(g.Sum(r => r.Amount) / totalCost) * 100.0 : 0
                    })
                    .OrderByDescending(s => s.TotalAmount)
                    .ToList();

                var listings = await _listingContext.QueryAsync(l => l.BatchId == batchId && !l.IsDeleted);
                var productIds = listings.Where(l => l.ProductId.HasValue).Select(l => l.ProductId.Value).Distinct().ToList();

                var totalRevenue = 0m;
                var revenueBreakdown = new List<RevenueBreakdownItem>();

                if (productIds.Count > 0)
                {
                    var allOrderItems = await _orderItemContext.QueryAsync(oi => productIds.Contains(oi.ProductId));
                    var completedOrderIds = new HashSet<long>();

                    foreach (var pid in productIds)
                    {
                        var items = allOrderItems.Where(oi => oi.ProductId == pid).ToList();
                        var orderIds = items.Select(i => i.OrderId).Distinct().ToList();

                        decimal productRevenue = 0;
                        int quantitySold = 0;
                        foreach (var oid in orderIds)
                        {
                            if (!completedOrderIds.Contains(oid))
                            {
                                var order = await _orderContext.QueryFirstOrDefaultAsync(o => o.Id == oid);
                                if (order != null && order.Status >= 3)
                                    completedOrderIds.Add(oid);
                            }
                            if (completedOrderIds.Contains(oid))
                            {
                                var orderItems = items.Where(i => i.OrderId == oid).ToList();
                                productRevenue += orderItems.Sum(i => i.Subtotal);
                                quantitySold += orderItems.Sum(i => i.Quantity);
                            }
                        }

                        if (productRevenue > 0)
                        {
                            var product = await _productContext.QueryFirstOrDefaultAsync(p => p.Id == pid);
                            revenueBreakdown.Add(new RevenueBreakdownItem
                            {
                                ProductId = pid,
                                ProductName = product?.ProductName ?? "",
                                Revenue = productRevenue,
                                QuantitySold = quantitySold
                            });
                        }
                    }

                    totalRevenue = revenueBreakdown.Sum(r => r.Revenue);
                }

                var netProfit = totalRevenue - totalCost;
                var roi = totalCost > 0 ? (double)(netProfit / totalCost) * 100.0 : 0;

                return new BatchProfitAnalysis
                {
                    BatchId = batchId,
                    TotalCost = totalCost,
                    TotalRevenue = totalRevenue,
                    NetProfit = netProfit,
                    ROI = roi,
                    CostBreakdown = costBreakdown,
                    RevenueBreakdown = revenueBreakdown
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次利润分析失败: BatchId={BatchId}", batchId);
                throw;
            }
        }

        public async Task<bool> CheckBatchCompletionAsync(long batchId)
        {
            try
            {
                var batch = await _batchContext.QueryFirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
                if (batch == null || batch.Status == "Completed")
                    return batch?.Status == "Completed";

                var yieldRecords = await _yieldContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted);
                var yieldList = yieldRecords.ToList();
                if (yieldList.Count == 0)
                    return false;

                var yieldRecordIds = yieldList.Select(r => r.Id).ToHashSet();
                var listings = await _listingContext.QueryAsync(l => l.BatchId == batchId && !l.IsDeleted);
                var listingYieldIds = listings.Select(l => l.YieldRecordId).ToHashSet();

                var allYieldHaveListing = yieldRecordIds.All(yid => listingYieldIds.Contains(yid));
                if (!allYieldHaveListing)
                    return false;

                var productIds = listings.Where(l => l.ProductId.HasValue).Select(l => l.ProductId.Value).Distinct().ToList();
                if (productIds.Count == 0)
                    return false;

                var allSoldOut = true;
                foreach (var pid in productIds)
                {
                    var product = await _productContext.QueryFirstOrDefaultAsync(p => p.Id == pid && !p.IsDeleted);
                    if (product != null && product.Stock > 0)
                    {
                        allSoldOut = false;
                        break;
                    }
                }

                if (allSoldOut)
                {
                    batch.Status = "Completed";
                    await _batchContext.UpdateAsync(batch, batch.Id);
                    _logger.LogInformation("批次已标记为完成: BatchId={BatchId}", batchId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查批次完成状态失败: BatchId={BatchId}", batchId);
                throw;
            }
        }

        public async Task<PresaleFulfillmentStatus> CheckPresaleFulfillmentAsync(long batchId)
        {
            try
            {
                var result = new PresaleFulfillmentStatus { BatchId = batchId };

                var presaleProducts = await _productContext.QueryAsync(p =>
                    p.RelatedBatchId == batchId && p.IsPresale && !p.IsDeleted);
                var presaleProductList = presaleProducts.ToList();
                var presaleProductIds = presaleProductList.Select(p => p.Id).ToList();

                var totalDemand = 0m;
                var presaleOrders = new List<PresaleOrderItem>();

                if (presaleProductIds.Count > 0)
                {
                    var allOrderItems = await _orderItemContext.QueryAsync(oi => presaleProductIds.Contains(oi.ProductId));
                    var orderItemGroups = allOrderItems.GroupBy(oi => oi.OrderId);

                    foreach (var group in orderItemGroups)
                    {
                        var order = await _orderContext.QueryFirstOrDefaultAsync(o => o.Id == group.Key && o.IsPresale);
                        if (order == null) continue;

                        foreach (var item in group)
                        {
                            totalDemand += item.Quantity;
                            presaleOrders.Add(new PresaleOrderItem
                            {
                                OrderId = order.Id,
                                OrderNo = order.OrderNo ?? "",
                                ProductId = item.ProductId,
                                ProductName = item.ProductName ?? "",
                                Quantity = item.Quantity,
                                Subtotal = item.Subtotal,
                                IsPresaleReadyNotified = order.PresaleReadyNotifiedAt.HasValue
                            });
                        }
                    }
                }

                var yieldRecords = await _yieldContext.QueryAsync(r => r.BatchId == batchId && !r.IsDeleted);
                var totalHarvested = yieldRecords.Sum(r => r.Quantity);

                result.TotalPresaleDemand = totalDemand;
                result.TotalHarvested = totalHarvested;
                result.IsFulfilled = totalHarvested >= totalDemand && totalDemand > 0;
                result.PresaleOrders = presaleOrders;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查预售履行状态失败: BatchId={BatchId}", batchId);
                throw;
            }
        }
    }
}
