using Horizon.Core.Abstract;
using Horizon.Core.Options;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using System.Linq;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers
{
    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerSensorDataController : OrleansControllerBase
    {
        private readonly ILogger<FlowerSensorDataController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerSensorDataController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerSensorDataController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        private Guid GetAuthenticatedUserId()
        {
            Guid.TryParse(_passportCurrent?.UserId, out Guid id);
            return id;
        }

        private async Task<bool> IsGreenhouseOwnerAsync(IClusterClient client, string greenhouseId, Guid userId)
        {
            if (string.IsNullOrEmpty(greenhouseId) || userId == Guid.Empty) return false;
            var poolGrain = client.GetGrain<IFlowerDataPoolGrain>(0);
            var batches = await poolGrain.ListPlantingBatchesAsync(greenhouseId, "", 0, 1);
            return batches.Any(b => b.UserId == userId);
        }

        private async Task<bool> IsDeviceOwnerAsync(IClusterClient client, string deviceId, Guid userId)
        {
            if (string.IsNullOrEmpty(deviceId) || userId == Guid.Empty) return false;
            var sensorGrain = client.GetGrain<ISensorDataGrain>(deviceId);
            var latest = await sensorGrain.GetLatestReadingAsync(deviceId);
            if (latest == null || string.IsNullOrEmpty(latest.GreenhouseId)) return false;
            return await IsGreenhouseOwnerAsync(client, latest.GreenhouseId, userId);
        }

        [HttpPost("report")]
        public async Task<ResultVM<object>> ReportReadingAsync([FromBody] SensorReading reading)
        {
            var result = new ResultVM<object>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!string.IsNullOrEmpty(reading.GreenhouseId) && !await IsGreenhouseOwnerAsync(client, reading.GreenhouseId, userId))
                {
                    _logger.LogWarning("传感器数据上报归属权校验失败: GreenhouseId={GreenhouseId}, UserId={UserId}", reading.GreenhouseId, userId);
                    result.ErrorMessage = "无权操作此温室";
                    return result;
                }

                var grain = client.GetGrain<ISensorDataGrain>(reading.DeviceId ?? "unknown");
                await grain.ReportReadingAsync(reading);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "上报传感器数据失败: DeviceId={DeviceId}", reading.DeviceId);
                result.ErrorMessage = "上报传感器数据失败";
            }
            return result;
        }

        [HttpGet("latest/{deviceId}")]
        public async Task<ResultVM<SensorReading>> GetLatestReadingAsync(string deviceId)
        {
            var result = new ResultVM<SensorReading>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsDeviceOwnerAsync(client, deviceId, userId))
                {
                    _logger.LogWarning("获取最新传感器数据归属权校验失败: DeviceId={DeviceId}, UserId={UserId}", deviceId, userId);
                    result.ErrorMessage = "无权查看此设备数据";
                    return result;
                }

                var grain = client.GetGrain<ISensorDataGrain>(deviceId);
                result.Data = await grain.GetLatestReadingAsync(deviceId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最新传感器数据失败: DeviceId={DeviceId}", deviceId);
                result.ErrorMessage = "获取最新传感器数据失败";
            }
            return result;
        }

        [HttpGet("history/{deviceId}")]
        public async Task<ResultVM<List<SensorReading>>> GetHistoryReadingsAsync(string deviceId, [FromQuery] DateTime start, [FromQuery] DateTime end)
        {
            var result = new ResultVM<List<SensorReading>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsDeviceOwnerAsync(client, deviceId, userId))
                {
                    _logger.LogWarning("获取历史传感器数据归属权校验失败: DeviceId={DeviceId}, UserId={UserId}", deviceId, userId);
                    result.ErrorMessage = "无权查看此设备数据";
                    return result;
                }

                var grain = client.GetGrain<ISensorDataGrain>(deviceId);
                result.Data = await grain.GetHistoryReadingsAsync(deviceId, start, end);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取历史传感器数据失败: DeviceId={DeviceId}", deviceId);
                result.ErrorMessage = "获取历史传感器数据失败";
            }
            return result;
        }

        [HttpGet("stats/{deviceId}")]
        public async Task<ResultVM<Dictionary<string, double>>> GetAggregatedStatsAsync(string deviceId, [FromQuery] DateTime start, [FromQuery] DateTime end)
        {
            var result = new ResultVM<Dictionary<string, double>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsDeviceOwnerAsync(client, deviceId, userId))
                {
                    _logger.LogWarning("获取传感器聚合统计归属权校验失败: DeviceId={DeviceId}, UserId={UserId}", deviceId, userId);
                    result.ErrorMessage = "无权查看此设备数据";
                    return result;
                }

                var grain = client.GetGrain<ISensorDataGrain>(deviceId);
                result.Data = await grain.GetAggregatedStatsAsync(deviceId, start, end);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取传感器聚合统计失败: DeviceId={DeviceId}", deviceId);
                result.ErrorMessage = "获取传感器聚合统计失败";
            }
            return result;
        }

        [HttpGet("analysis/trend/{deviceId}")]
        public async Task<ResultVM<TrendAnalysisResult>> GetTrendAnalysisAsync(string deviceId, [FromQuery] DateTime start, [FromQuery] DateTime end, [FromQuery] string granularity = "day")
        {
            var result = new ResultVM<TrendAnalysisResult>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsDeviceOwnerAsync(client, deviceId, userId))
                {
                    _logger.LogWarning("获取趋势分析归属权校验失败: DeviceId={DeviceId}, UserId={UserId}", deviceId, userId);
                    result.ErrorMessage = "无权查看此设备数据";
                    return result;
                }

                var grain = client.GetGrain<ISensorDataGrain>(deviceId);
                result.Data = await grain.GetTrendAnalysisAsync(deviceId, start, end, granularity);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取趋势分析失败: DeviceId={DeviceId}", deviceId);
                result.ErrorMessage = "获取趋势分析失败";
            }
            return result;
        }

        [HttpPost("analysis/comparison")]
        public async Task<ResultVM<MultiDeviceComparisonResult>> GetMultiDeviceComparisonAsync([FromBody] DeviceComparisonRequest request)
        {
            var result = new ResultVM<MultiDeviceComparisonResult>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                foreach (var did in request.DeviceIds)
                {
                    if (!await IsDeviceOwnerAsync(client, did, userId))
                    {
                        _logger.LogWarning("多设备对比分析归属权校验失败: DeviceId={DeviceId}, UserId={UserId}", did, userId);
                        result.ErrorMessage = "无权查看此设备数据";
                        return result;
                    }
                }

                var grain = client.GetGrain<ISensorDataGrain>(request.DeviceIds.FirstOrDefault() ?? _passportCurrent.PassportId);
                result.Data = await grain.GetMultiDeviceComparisonAsync(request.DeviceIds, request.Start, request.End);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取多设备对比分析失败");
                result.ErrorMessage = "获取多设备对比分析失败";
            }
            return result;
        }

        [HttpGet("analysis/health-index/{greenhouseId}")]
        public async Task<ResultVM<HealthIndexResult>> GetHealthIndexAsync(string greenhouseId, [FromQuery] DateTime start, [FromQuery] DateTime end)
        {
            var result = new ResultVM<HealthIndexResult>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsGreenhouseOwnerAsync(client, greenhouseId, userId))
                {
                    _logger.LogWarning("环境健康指数查询归属权校验失败: GreenhouseId={GreenhouseId}, UserId={UserId}", greenhouseId, userId);
                    result.ErrorMessage = "无权查看此温室数据";
                    return result;
                }

                var grain = client.GetGrain<ISensorDataGrain>(greenhouseId);
                result.Data = await grain.GetHealthIndexAsync(greenhouseId, start, end);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取环境健康指数失败: GreenhouseId={GreenhouseId}", greenhouseId);
                result.ErrorMessage = "获取环境健康指数失败";
            }
            return result;
        }

        [HttpGet("analysis/anomalies/{deviceId}")]
        public async Task<ResultVM<List<AnomalyDataPoint>>> GetAnomaliesAsync(string deviceId, [FromQuery] DateTime start, [FromQuery] DateTime end)
        {
            var result = new ResultVM<List<AnomalyDataPoint>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsDeviceOwnerAsync(client, deviceId, userId))
                {
                    _logger.LogWarning("获取异常数据归属权校验失败: DeviceId={DeviceId}, UserId={UserId}", deviceId, userId);
                    result.ErrorMessage = "无权查看此设备数据";
                    return result;
                }

                var grain = client.GetGrain<ISensorDataGrain>(deviceId);
                result.Data = await grain.GetAnomaliesAsync(deviceId, start, end);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取异常数据失败: DeviceId={DeviceId}", deviceId);
                result.ErrorMessage = "获取异常数据失败";
            }
            return result;
        }

        [HttpPost("manual-report")]
        public async Task<ResultVM<object>> ReportManualReadingAsync([FromBody] ManualSensorReportApiRequest request)
        {
            var result = new ResultVM<object>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!string.IsNullOrEmpty(request.GreenhouseId) && !await IsGreenhouseOwnerAsync(client, request.GreenhouseId, userId))
                {
                    _logger.LogWarning("手动传感器上报归属权校验失败: GreenhouseId={GreenhouseId}, UserId={UserId}", request.GreenhouseId, userId);
                    result.ErrorMessage = "无权操作此温室";
                    return result;
                }

                var grain = client.GetGrain<ISensorDataGrain>(request.DeviceId);
                await grain.ReportManualReadingAsync(new SensorReading
                {
                    DeviceId = request.DeviceId,
                    GreenhouseId = request.GreenhouseId,
                    Temperature = request.Temperature,
                    Humidity = request.Humidity,
                    LightIntensity = request.LightIntensity,
                    Co2Level = request.Co2Level,
                    SoilMoisture = request.SoilMoisture,
                    ReadingTime = DateTime.UtcNow
                });
                result.IsSuccess = true;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "手动上报传感器数据失败: DeviceId={DeviceId}", request.DeviceId);
                result.ErrorMessage = "手动上报传感器数据失败";
            }
            return result;
        }
    }

    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("FlowerPlanting")]
    [Authorize]
    public class FlowerPlantingController : OrleansControllerBase
    {
        private readonly ILogger<FlowerPlantingController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerPlantingController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerPlantingController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        private Guid GetAuthenticatedUserId()
        {
            Guid.TryParse(_passportCurrent?.UserId, out Guid id);
            return id;
        }

        private async Task<bool> IsBatchOwnerAsync(IClusterClient client, long batchId, Guid userId)
        {
            if (batchId <= 0 || userId == Guid.Empty) return false;
            var poolGrain = client.GetGrain<IFlowerDataPoolGrain>(0);
            var lifecycle = await poolGrain.GetBatchLifecycleAsync(batchId);
            return lifecycle?.BatchInfo != null && lifecycle.BatchInfo.UserId == userId;
        }

        [HttpPost("batches")]
        public async Task<ResultVM<long>> CreateBatchAsync([FromBody] CreateBatchRequest request)
        {
            var result = new ResultVM<long>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.CreatePlantingBatchAsync(new PlantingBatchState
                {
                    BatchName = request.BatchName ?? "",
                    SpeciesId = request.SpeciesId ?? "",
                    SpeciesName = request.SpeciesName ?? "",
                    GreenhouseId = _passportCurrent.PassportId,
                    PlantingDate = request.PlantingDate,
                    ExpectedHarvestDate = request.ExpectedHarvestDate,
                    PlantingQuantity = request.PlantingQuantity,
                    Status = "Planted",
                    Remark = request.Remark ?? "",
                    UserId = userId,
                    Passport = _passportCurrent.PassportId
                });
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建种植批次失败: BatchName={BatchName}", request.BatchName);
                result.ErrorMessage = "创建种植批次失败";
            }
            return result;
        }

        [HttpGet("batches/{greenhouseId}")]
        public async Task<ResultVM<List<PlantingBatchState>>> ListBatchesAsync(string greenhouseId, [FromQuery] string status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<PlantingBatchState>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();
                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                var allBatches = await grain.ListPlantingBatchesAsync(_passportCurrent.PassportId, status ?? "", (page - 1) * pageSize, pageSize);
                result.Data = allBatches.Where(b => b.UserId == userId).ToList();
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取种植批次列表失败: GreenhouseId={GreenhouseId}", greenhouseId);
                result.ErrorMessage = "获取种植批次列表失败";
            }
            return result;
        }

        [HttpPut("batches/{batchId}/status")]
        public async Task<ResultVM<object>> UpdateBatchStatusAsync(long batchId, [FromBody] UpdateBatchStatusRequest request)
        {
            var result = new ResultVM<object>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("更新批次状态归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权操作此批次";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                await grain.UpdatePlantingBatchStatusAsync(batchId, request.Status ?? "", request.ActualHarvestDate);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新种植批次状态失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "更新种植批次状态失败";
            }
            return result;
        }

        [HttpGet("batches/{batchId}/lifecycle")]
        public async Task<ResultVM<BatchLifecycle>> GetBatchLifecycleAsync(long batchId)
        {
            var result = new ResultVM<BatchLifecycle>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("获取批次生命周期归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权查看此批次";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.GetBatchLifecycleAsync(batchId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次生命周期失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "获取批次生命周期失败";
            }
            return result;
        }

        [HttpGet("batches/{batchId}/profit")]
        public async Task<ResultVM<BatchProfitAnalysis>> GetBatchProfitAsync(long batchId)
        {
            var result = new ResultVM<BatchProfitAnalysis>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("获取批次利润分析归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权查看此批次";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.GetBatchProfitAnalysisAsync(batchId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取批次利润分析失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "获取批次利润分析失败";
            }
            return result;
        }

        [HttpGet("batches/{batchId}/presale-status")]
        public async Task<ResultVM<PresaleFulfillmentStatus>> GetPresaleStatusAsync(long batchId)
        {
            var result = new ResultVM<PresaleFulfillmentStatus>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("获取预售履行状态归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权查看此批次";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.CheckPresaleFulfillmentAsync(batchId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取预售履行状态失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "获取预售履行状态失败";
            }
            return result;
        }
    }

    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("FlowerCost")]
    [Authorize]
    public class FlowerCostController : OrleansControllerBase
    {
        private readonly ILogger<FlowerCostController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerCostController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerCostController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        private Guid GetAuthenticatedUserId()
        {
            Guid.TryParse(_passportCurrent?.UserId, out Guid id);
            return id;
        }

        private async Task<bool> IsBatchOwnerAsync(IClusterClient client, long batchId, Guid userId)
        {
            if (batchId <= 0 || userId == Guid.Empty) return false;
            var poolGrain = client.GetGrain<IFlowerDataPoolGrain>(0);
            var lifecycle = await poolGrain.GetBatchLifecycleAsync(batchId);
            return lifecycle?.BatchInfo != null && lifecycle.BatchInfo.UserId == userId;
        }

        [HttpPost("records")]
        public async Task<ResultVM<long>> AddCostRecordAsync([FromBody] AddCostRecordRequest request)
        {
            var result = new ResultVM<long>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, request.BatchId, userId))
                {
                    _logger.LogWarning("添加成本记录归属权校验失败: BatchId={BatchId}, UserId={UserId}", request.BatchId, userId);
                    result.ErrorMessage = "无权操作此批次";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.AddCostRecordAsync(new CostRecordState
                {
                    BatchId = request.BatchId,
                    Category = request.Category ?? "Other",
                    Amount = request.Amount,
                    CostDate = request.CostDate,
                    Remark = request.Remark ?? ""
                });
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加成本记录失败: BatchId={BatchId}", request.BatchId);
                result.ErrorMessage = "添加成本记录失败";
            }
            return result;
        }

        [HttpGet("records/{batchId}")]
        public async Task<ResultVM<List<CostRecordState>>> GetCostRecordsAsync(long batchId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<CostRecordState>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("获取成本记录归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权查看此批次成本记录";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.GetCostRecordsAsync(batchId, (page - 1) * pageSize, pageSize);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成本记录失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "获取成本记录失败";
            }
            return result;
        }

        [HttpGet("stats/{batchId}")]
        public async Task<ResultVM<List<CostCategoryStats>>> GetCostStatsAsync(long batchId, [FromQuery] DateTime? start, [FromQuery] DateTime? end)
        {
            var result = new ResultVM<List<CostCategoryStats>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("获取成本统计归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权查看此批次成本统计";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.GetCostCategoryStatsAsync(batchId, start ?? DateTime.MinValue, end ?? DateTime.MaxValue);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成本统计失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "获取成本统计失败";
            }
            return result;
        }

        [HttpGet("trend/{greenhouseId}")]
        public async Task<ResultVM<List<CostMonthlyTrendInfo>>> GetCostTrendAsync(string greenhouseId, [FromQuery] int months = 6)
        {
            var result = new ResultVM<List<CostMonthlyTrendInfo>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();
                var poolGrain = client.GetGrain<IFlowerDataPoolGrain>(0);
                var batches = await poolGrain.ListPlantingBatchesAsync(greenhouseId, "", 0, 1);
                if (!batches.Any(b => b.UserId == userId))
                {
                    _logger.LogWarning("获取成本趋势归属权校验失败: GreenhouseId={GreenhouseId}, UserId={UserId}", greenhouseId, userId);
                    result.ErrorMessage = "无权查看此温室成本趋势";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.GetCostTrendAsync(greenhouseId, months);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成本趋势失败: GreenhouseId={GreenhouseId}", greenhouseId);
                result.ErrorMessage = "获取成本趋势失败";
            }
            return result;
        }
    }

    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerYieldController : OrleansControllerBase
    {
        private readonly ILogger<FlowerYieldController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerYieldController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerYieldController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        private Guid GetAuthenticatedUserId()
        {
            Guid.TryParse(_passportCurrent?.UserId, out Guid id);
            return id;
        }

        private async Task<bool> IsBatchOwnerAsync(IClusterClient client, long batchId, Guid userId)
        {
            if (batchId <= 0 || userId == Guid.Empty) return false;
            var poolGrain = client.GetGrain<IFlowerDataPoolGrain>(0);
            var lifecycle = await poolGrain.GetBatchLifecycleAsync(batchId);
            return lifecycle?.BatchInfo != null && lifecycle.BatchInfo.UserId == userId;
        }

        private async Task<bool> IsMerchantOwnerAsync(IClusterClient client, long merchantId, Guid userId)
        {
            if (merchantId <= 0 || userId == Guid.Empty) return false;
            var merchantGrain = client.GetGrain<IMerchantGrain>(merchantId);
            var merchant = await merchantGrain.GetMerchantAsync();
            return merchant != null && merchant.UserId == userId;
        }

        [HttpPost("records")]
        public async Task<ResultVM<long>> AddYieldRecordAsync([FromBody] AddYieldRecordRequest request)
        {
            var result = new ResultVM<long>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, request.BatchId, userId))
                {
                    _logger.LogWarning("添加产量记录归属权校验失败: BatchId={BatchId}, UserId={UserId}", request.BatchId, userId);
                    result.ErrorMessage = "无权操作此批次";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.AddYieldRecordAsync(new YieldRecordState
                {
                    BatchId = request.BatchId,
                    SpeciesId = request.SpeciesId ?? "",
                    SpeciesName = request.SpeciesName ?? "",
                    Quantity = request.Quantity,
                    Unit = request.Unit ?? "Stems",
                    Grade = request.Grade ?? "A",
                    HarvestDate = request.HarvestDate,
                    Remark = request.Remark ?? ""
                });
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加产量记录失败: BatchId={BatchId}", request.BatchId);
                result.ErrorMessage = "添加产量记录失败";
            }
            return result;
        }

        [HttpGet("records/{batchId}")]
        public async Task<ResultVM<List<YieldRecordState>>> GetYieldRecordsAsync(long batchId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<YieldRecordState>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("获取产量记录归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权查看此批次产量记录";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.GetYieldRecordsAsync(batchId, (page - 1) * pageSize, pageSize);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取产量记录失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "获取产量记录失败";
            }
            return result;
        }

        [HttpGet("trend/{greenhouseId}")]
        public async Task<ResultVM<List<YieldTrendItem>>> GetYieldTrendAsync(string greenhouseId, [FromQuery] int months = 6)
        {
            var result = new ResultVM<List<YieldTrendItem>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();
                var poolGrain = client.GetGrain<IFlowerDataPoolGrain>(0);
                var batches = await poolGrain.ListPlantingBatchesAsync(greenhouseId, "", 0, 1);
                if (!batches.Any(b => b.UserId == userId))
                {
                    _logger.LogWarning("获取产量趋势归属权校验失败: GreenhouseId={GreenhouseId}, UserId={UserId}", greenhouseId, userId);
                    result.ErrorMessage = "无权查看此温室产量趋势";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.GetYieldTrendAsync(greenhouseId, months);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取产量趋势失败: GreenhouseId={GreenhouseId}", greenhouseId);
                result.ErrorMessage = "获取产量趋势失败";
            }
            return result;
        }

        [HttpPost("list-from-yield")]
        public async Task<ResultVM<HarvestListingResult>> ListFromYieldAsync([FromBody] ListFromYieldRequest request)
        {
            var result = new ResultVM<HarvestListingResult>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsMerchantOwnerAsync(client, request.MerchantId, userId))
                {
                    _logger.LogWarning("创建上架单归属权校验失败: MerchantId={MerchantId}, UserId={UserId}", request.MerchantId, userId);
                    result.ErrorMessage = "无权操作此商户";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.CreateProductFromYieldAsync(request.YieldRecordId, request.MerchantId);
                result.IsSuccess = result.Data.Success;
                if (!result.Data.Success)
                    result.ErrorMessage = result.Data.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从采收记录创建上架单失败: YieldRecordId={YieldRecordId}", request.YieldRecordId);
                result.ErrorMessage = "从采收记录创建上架单失败";
            }
            return result;
        }

        [HttpPost("batch-list")]
        public async Task<ResultVM<HarvestListingResult>> BatchListAsync([FromBody] BatchListFromYieldRequest request)
        {
            var result = new ResultVM<HarvestListingResult>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsMerchantOwnerAsync(client, request.MerchantId, userId))
                {
                    _logger.LogWarning("批量创建上架单归属权校验失败: MerchantId={MerchantId}, UserId={UserId}", request.MerchantId, userId);
                    result.ErrorMessage = "无权操作此商户";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.BatchCreateProductsFromYieldAsync(request.YieldRecordIds, request.MerchantId);
                result.IsSuccess = result.Data.Success;
                if (!result.Data.Success)
                    result.ErrorMessage = result.Data.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建上架单失败");
                result.ErrorMessage = "批量创建上架单失败";
            }
            return result;
        }

        [HttpPut("confirm-listing/{listingId}")]
        public async Task<ResultVM<object>> ConfirmListingAsync(long listingId, [FromBody] ConfirmListingRequest request)
        {
            var result = new ResultVM<object>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();
                var poolGrain = client.GetGrain<IFlowerDataPoolGrain>(0);
                var allListings = await poolGrain.GetHarvestListingsAsync(0, -1, 0, int.MaxValue);
                var listing = allListings.FirstOrDefault(l => l.Id == listingId);
                if (listing == null || !await IsMerchantOwnerAsync(client, listing.MerchantId, userId))
                {
                    _logger.LogWarning("确认上架归属权校验失败: ListingId={ListingId}, UserId={UserId}", listingId, userId);
                    result.ErrorMessage = "无权操作此上架单";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                await grain.ConfirmHarvestListingAsync(listingId, request.ActualPrice);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确认上架失败: ListingId={ListingId}", listingId);
                result.ErrorMessage = "确认上架失败";
            }
            return result;
        }

        [HttpGet("listings/{merchantId}")]
        public async Task<ResultVM<List<HarvestListingInfo>>> GetListingsAsync(long merchantId, [FromQuery] int status = -1, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = new ResultVM<List<HarvestListingInfo>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsMerchantOwnerAsync(client, merchantId, userId))
                {
                    _logger.LogWarning("获取上架列表归属权校验失败: MerchantId={MerchantId}, UserId={UserId}", merchantId, userId);
                    result.ErrorMessage = "只能查询自己店铺的上架单";
                    return result;
                }

                var grain = client.GetGrain<IFlowerDataPoolGrain>(0);
                result.Data = await grain.GetHarvestListingsAsync(merchantId, status, (page - 1) * pageSize, pageSize);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取上架列表失败: MerchantId={MerchantId}", merchantId);
                result.ErrorMessage = "获取上架列表失败";
            }
            return result;
        }
    }

    [ApiGroup(ApiGroupName.Flower)]
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class FlowerAdviceController : OrleansControllerBase
    {
        private readonly ILogger<FlowerAdviceController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;

        public FlowerAdviceController(
            IOptions<AdoNetOptions> options,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<FlowerAdviceController> logger,
            IClusterClient clusterClient,
            IPassportCurrentUser passportCurrent)
            : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        private Guid GetAuthenticatedUserId()
        {
            Guid.TryParse(_passportCurrent?.UserId, out Guid id);
            return id;
        }

        private async Task<bool> IsBatchOwnerAsync(IClusterClient client, long batchId, Guid userId)
        {
            if (batchId <= 0 || userId == Guid.Empty) return false;
            var poolGrain = client.GetGrain<IFlowerDataPoolGrain>(0);
            var lifecycle = await poolGrain.GetBatchLifecycleAsync(batchId);
            return lifecycle?.BatchInfo != null && lifecycle.BatchInfo.UserId == userId;
        }

        [HttpPost("generate/{batchId}")]
        public async Task<ResultVM<List<PlantingAdviceItem>>> GenerateAdviceAsync(long batchId)
        {
            var result = new ResultVM<List<PlantingAdviceItem>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("生成种植建议归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权操作此批次";
                    return result;
                }

                var grain = client.GetGrain<IPlantingAdviceGrain>(batchId);
                result.Data = await grain.GenerateAdviceAsync(batchId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成种植建议失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "生成种植建议失败";
            }
            return result;
        }

        [HttpGet("active/{batchId}")]
        public async Task<ResultVM<List<PlantingAdviceItem>>> GetActiveAdviceAsync(long batchId)
        {
            var result = new ResultVM<List<PlantingAdviceItem>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("获取活跃建议归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权查看此批次建议";
                    return result;
                }

                var grain = client.GetGrain<IPlantingAdviceGrain>(batchId);
                result.Data = await grain.GetActiveAdviceAsync(batchId);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃建议失败: BatchId={BatchId}", batchId);
                result.ErrorMessage = "获取活跃建议失败";
            }
            return result;
        }

        [HttpPut("{adviceId}/execute")]
        public async Task<ResultVM<object>> MarkAdviceExecutedAsync(long adviceId, [FromBody] ExecuteAdviceRequest request)
        {
            var result = new ResultVM<object>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, request.BatchId, userId))
                {
                    _logger.LogWarning("标记建议已执行归属权校验失败: BatchId={BatchId}, UserId={UserId}", request.BatchId, userId);
                    result.ErrorMessage = "无权操作此批次建议";
                    return result;
                }

                var grain = client.GetGrain<IPlantingAdviceGrain>(request.BatchId);
                await grain.MarkAdviceExecutedAsync(adviceId, request.Action ?? "Executed");
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "标记建议已执行失败: AdviceId={AdviceId}", adviceId);
                result.ErrorMessage = "标记建议已执行失败";
            }
            return result;
        }

        [HttpGet("type/{batchId}/{adviceType}")]
        public async Task<ResultVM<List<PlantingAdviceItem>>> GetAdviceByTypeAsync(long batchId, string adviceType)
        {
            var result = new ResultVM<List<PlantingAdviceItem>>();
            try
            {
                var userId = GetAuthenticatedUserId();
                if (userId == Guid.Empty)
                {
                    result.ErrorMessage = "用户未认证";
                    return result;
                }

                var client = await OrleansConnectClient();

                if (!await IsBatchOwnerAsync(client, batchId, userId))
                {
                    _logger.LogWarning("获取类型建议归属权校验失败: BatchId={BatchId}, UserId={UserId}", batchId, userId);
                    result.ErrorMessage = "无权查看此批次建议";
                    return result;
                }

                var grain = client.GetGrain<IPlantingAdviceGrain>(batchId);
                result.Data = await grain.GetAdviceByTypeAsync(batchId, adviceType);
                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取类型建议失败: BatchId={BatchId}, Type={Type}", batchId, adviceType);
                result.ErrorMessage = "获取类型建议失败";
            }
            return result;
        }
    }

    public class CreateBatchRequest
    {
        public string BatchName { get; set; } = "";
        public string SpeciesId { get; set; } = "";
        public string SpeciesName { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public Guid UserId { get; set; }
        public string Passport { get; set; } = "";
        public DateTime PlantingDate { get; set; }
        public DateTime? ExpectedHarvestDate { get; set; }
        public int PlantingQuantity { get; set; }
        public string Remark { get; set; } = "";
    }

    public class UpdateBatchStatusRequest
    {
        public string Status { get; set; } = "";
        public DateTime? ActualHarvestDate { get; set; }
    }

    public class AddCostRecordRequest
    {
        public long BatchId { get; set; }
        public string Category { get; set; } = "Other";
        public decimal Amount { get; set; }
        public DateTime CostDate { get; set; }
        public string Remark { get; set; } = "";
    }

    public class AddYieldRecordRequest
    {
        public long BatchId { get; set; }
        public string SpeciesId { get; set; } = "";
        public string SpeciesName { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "Stems";
        public string Grade { get; set; } = "A";
        public DateTime HarvestDate { get; set; }
        public string Remark { get; set; } = "";
    }

    public class ExecuteAdviceRequest
    {
        public long BatchId { get; set; }
        public string Action { get; set; } = "Executed";
    }

    public class ListFromYieldRequest
    {
        public long YieldRecordId { get; set; }
        public long MerchantId { get; set; }
    }

    public class BatchListFromYieldRequest
    {
        public List<long> YieldRecordIds { get; set; } = new();
        public long MerchantId { get; set; }
    }

    public class ConfirmListingRequest
    {
        public decimal ActualPrice { get; set; }
    }

    public class DeviceComparisonRequest
    {
        public List<string> DeviceIds { get; set; } = new();
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
    }

    public class ManualSensorReportApiRequest
    {
        public string DeviceId { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public double LightIntensity { get; set; }
        public double Co2Level { get; set; }
        public double SoilMoisture { get; set; }
    }
}
