using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Game.GengDi.Core.Services
{
    public class IoTDeviceInfo
    {
        public long Id { get; set; }
        public string DeviceCode { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string DeviceType { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public string GroupId { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string MqttTopic { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string OnlineStatus { get; set; } = "Offline";
        public string FirmwareVersion { get; set; } = "";
        public DateTime? LastHeartbeatTime { get; set; }
        public bool IsEnabled { get; set; }
        public string BindingStatus { get; set; } = "Unbound";
        public DateTime? BoundAt { get; set; }
        public string Location { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public double? BatteryLevel { get; set; }
        public double? SignalStrength { get; set; }
        public string SensorCapabilities { get; set; } = "";
        public DateTime? InstallDate { get; set; }
        public string Remark { get; set; } = "";
    }

    public class RegisterDeviceRequest
    {
        public string DeviceName { get; set; } = "";
        public string DeviceType { get; set; } = "Sensor";
        public string GreenhouseId { get; set; } = "";
        public string GroupId { get; set; } = "";
        public string Protocol { get; set; } = "MQTT";
        public string Location { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string SensorCapabilities { get; set; }
        public string Remark { get; set; }
    }

    public class DeviceGroupInfo
    {
        public long Id { get; set; }
        public string GroupName { get; set; } = "";
        public string Description { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
    }

    public class CreateDeviceGroupRequest
    {
        public string GroupName { get; set; } = "";
        public string Description { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
    }

    public class SensorReadingInfo
    {
        public string DeviceId { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public double LightIntensity { get; set; }
        public double Co2Level { get; set; }
        public double SoilMoisture { get; set; }
        public DateTime ReadingTime { get; set; }
    }

    public class PlantingBatchInfo
    {
        public long Id { get; set; }
        public string BatchName { get; set; } = "";
        public string SpeciesId { get; set; } = "";
        public string SpeciesName { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public DateTime PlantingDate { get; set; }
        public DateTime? ExpectedHarvestDate { get; set; }
        public DateTime? ActualHarvestDate { get; set; }
        public string Status { get; set; } = "Planted";
        public int PlantingQuantity { get; set; }
        public string Remark { get; set; } = "";
    }

    public class CreateBatchRequest
    {
        public string BatchName { get; set; } = "";
        public string SpeciesId { get; set; } = "";
        public string SpeciesName { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public DateTime PlantingDate { get; set; }
        public DateTime? ExpectedHarvestDate { get; set; }
        public int PlantingQuantity { get; set; }
        public string Remark { get; set; } = "";
    }

    public class CostRecordInfo
    {
        public long Id { get; set; }
        public long BatchId { get; set; }
        public string Category { get; set; } = "Other";
        public decimal Amount { get; set; }
        public DateTime CostDate { get; set; }
        public string Remark { get; set; } = "";
    }

    public class AddCostRecordRequest
    {
        public long BatchId { get; set; }
        public string Category { get; set; } = "Other";
        public decimal Amount { get; set; }
        public DateTime CostDate { get; set; }
        public string Remark { get; set; } = "";
    }

    public class CostCategoryStatsInfo
    {
        public string Category { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public int RecordCount { get; set; }
        public double Percentage { get; set; }
    }

    public class YieldRecordInfo
    {
        public long Id { get; set; }
        public long BatchId { get; set; }
        public string SpeciesId { get; set; } = "";
        public string SpeciesName { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "Stems";
        public string Grade { get; set; } = "A";
        public DateTime HarvestDate { get; set; }
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

    public class CostMonthlyTrendInfo
    {
        public string Month { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public decimal SeedlingCost { get; set; }
        public decimal FertilizerCost { get; set; }
        public decimal PesticideCost { get; set; }
        public decimal LaborCost { get; set; }
        public decimal UtilityCost { get; set; }
    }

    public class YieldTrendInfo
    {
        public string Month { get; set; } = "";
        public decimal TotalQuantity { get; set; }
        public decimal LastYearQuantity { get; set; }
        public string SpeciesName { get; set; } = "";
    }

    public class PlantingAdviceInfo
    {
        public long Id { get; set; }
        public long BatchId { get; set; }
        public string AdviceType { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Source { get; set; } = "";
        public string Priority { get; set; } = "Normal";
        public string Status { get; set; } = "Pending";
        public DateTime GeneratedTime { get; set; }
        public DateTime? ExecutedTime { get; set; }
        public string Action { get; set; } = "";
    }

    public class BatchLifecycleInfo
    {
        public PlantingBatchInfo BatchInfo { get; set; } = new();
        public SensorDataSummaryInfo SensorDataSummary { get; set; } = new();
        public decimal TotalCost { get; set; }
        public List<CostCategoryStatsInfo> CostBreakdown { get; set; } = new();
        public List<YieldRecordInfo> YieldRecords { get; set; } = new();
        public List<HarvestListingSummaryInfo> ListedProducts { get; set; } = new();
        public List<OrderSummaryInfo> OrderSummaries { get; set; } = new();
        public decimal SettlementTotal { get; set; }
    }

    public class SensorDataSummaryInfo
    {
        public double AvgTemperature { get; set; }
        public double AvgHumidity { get; set; }
        public double AvgLightIntensity { get; set; }
        public double AvgSoilMoisture { get; set; }
        public int ReadingCount { get; set; }
        public DateTime? FirstReadingTime { get; set; }
        public DateTime? LastReadingTime { get; set; }
    }

    public class HarvestListingSummaryInfo
    {
        public long Id { get; set; }
        public long YieldRecordId { get; set; }
        public long? ProductId { get; set; }
        public string SpeciesName { get; set; } = "";
        public string Grade { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal ActualPrice { get; set; }
        public int Status { get; set; }
        public DateTime HarvestDate { get; set; }
        public DateTime? ListedDate { get; set; }
    }

    public class OrderSummaryInfo
    {
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public decimal TotalAmount { get; set; }
        public int Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<OrderItemSummaryInfo> Items { get; set; } = new();
    }

    public class OrderItemSummaryInfo
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class BatchProfitAnalysisInfo
    {
        public long BatchId { get; set; }
        public decimal TotalCost { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal NetProfit { get; set; }
        public double ROI { get; set; }
        public List<CostCategoryStatsInfo> CostBreakdown { get; set; } = new();
        public List<RevenueBreakdownItemInfo> RevenueBreakdown { get; set; } = new();
    }

    public class RevenueBreakdownItemInfo
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public decimal Revenue { get; set; }
        public int QuantitySold { get; set; }
    }

    public class HarvestListingResultInfo
    {
        public long ListingId { get; set; }
        public long ProductId { get; set; }
        public decimal SuggestedPrice { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public class HarvestListingDetailInfo
    {
        public long Id { get; set; }
        public long YieldRecordId { get; set; }
        public long? ProductId { get; set; }
        public long BatchId { get; set; }
        public long MerchantId { get; set; }
        public int SpeciesId { get; set; }
        public string SpeciesName { get; set; } = "";
        public string Grade { get; set; } = "";
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = "";
        public int Status { get; set; }
        public decimal SuggestedPrice { get; set; }
        public decimal ActualPrice { get; set; }
        public string GreenhouseId { get; set; } = "";
        public DateTime HarvestDate { get; set; }
        public DateTime? ListedDate { get; set; }
    }

    public class PresaleFulfillmentStatusInfo
    {
        public long BatchId { get; set; }
        public decimal TotalPresaleDemand { get; set; }
        public decimal TotalHarvested { get; set; }
        public bool IsFulfilled { get; set; }
        public List<PresaleOrderItemInfo> PresaleOrders { get; set; } = new();
    }

    public class PresaleOrderItemInfo
    {
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = "";
        public long ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Subtotal { get; set; }
        public bool IsPresaleReadyNotified { get; set; }
    }

    public class BindDeviceRequestInfo
    {
        public string DeviceCode { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public string GroupId { get; set; } = "";
    }

    public class TrendAnalysisInfo
    {
        public string DeviceId { get; set; } = "";
        public string Granularity { get; set; } = "";
        public List<TrendDataPointInfo> DataPoints { get; set; } = new();
        public List<SignificantChangePointInfo> SignificantChanges { get; set; } = new();
    }

    public class TrendDataPointInfo
    {
        public DateTime Time { get; set; }
        public double AvgTemperature { get; set; }
        public double AvgHumidity { get; set; }
        public double AvgLightIntensity { get; set; }
        public double AvgCo2Level { get; set; }
        public double AvgSoilMoisture { get; set; }
        public double TemperatureChangeRate { get; set; }
        public double HumidityChangeRate { get; set; }
        public double LightChangeRate { get; set; }
        public double Co2ChangeRate { get; set; }
        public double SoilMoistureChangeRate { get; set; }
    }

    public class SignificantChangePointInfo
    {
        public DateTime Time { get; set; }
        public string Metric { get; set; } = "";
        public double ChangeRate { get; set; }
        public double PreviousValue { get; set; }
        public double CurrentValue { get; set; }
    }

    public class DeviceComparisonInfo
    {
        public List<DeviceComparisonItemInfo> Devices { get; set; } = new();
        public List<MetricDifferenceInfo> Differences { get; set; } = new();
        public string MaxDifferenceMetric { get; set; } = "";
    }

    public class DeviceComparisonItemInfo
    {
        public string DeviceId { get; set; } = "";
        public double AvgTemperature { get; set; }
        public double AvgHumidity { get; set; }
        public double AvgLightIntensity { get; set; }
        public double AvgCo2Level { get; set; }
        public double AvgSoilMoisture { get; set; }
    }

    public class MetricDifferenceInfo
    {
        public string Metric { get; set; } = "";
        public double Difference { get; set; }
        public double DifferencePercentage { get; set; }
    }

    public class HealthIndexInfo
    {
        public string GreenhouseId { get; set; } = "";
        public double OverallScore { get; set; }
        public double TemperatureScore { get; set; }
        public double HumidityScore { get; set; }
        public double LightScore { get; set; }
        public double Co2Score { get; set; }
        public double SoilMoistureScore { get; set; }
        public DateTime CalculatedAt { get; set; }
    }

    public class AnomalyInfo
    {
        public long ReadingId { get; set; }
        public string DeviceId { get; set; } = "";
        public string Metric { get; set; } = "";
        public double Value { get; set; }
        public double Mean { get; set; }
        public double StdDev { get; set; }
        public DateTime ReadingTime { get; set; }
    }

    public class ManualSensorReportRequest
    {
        public string DeviceId { get; set; } = "";
        public string GreenhouseId { get; set; } = "";
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public double LightIntensity { get; set; }
        public double Co2Level { get; set; }
        public double SoilMoisture { get; set; }
    }

    public class DeviceTwinDisplayInfo
    {
        public Dictionary<string, string> DesiredProperties { get; set; } = new();
        public Dictionary<string, string> ReportedProperties { get; set; } = new();
        public List<TwinPropertyDiffInfo> Differences { get; set; } = new();
    }

    public class TwinPropertyDiffInfo
    {
        public string Key { get; set; } = "";
        public string DesiredValue { get; set; }
        public string ReportedValue { get; set; }
    }

    public class SendDeviceCommandRequest
    {
        public string GreenhouseId { get; set; } = "";
        public string DeviceCode { get; set; } = "";
        public string Action { get; set; } = "";
        public string Payload { get; set; } = "";
    }

    public class FlowerIoTService
    {
        public async Task<List<IoTDeviceInfo>?> GetIoTDevicesAsync(string greenhouseId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerIoT/devices/greenhouse/{greenhouseId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<IoTDeviceInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetIoTDevicesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<IoTDeviceInfo?> RegisterDeviceAsync(RegisterDeviceRequest request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerIoT/devices", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<IoTDeviceInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(RegisterDeviceAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> UpdateDeviceHeartbeatAsync(string deviceCode)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerIoT/devices/{deviceCode}/heartbeat", null).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(UpdateDeviceHeartbeatAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> DeleteDeviceAsync(string deviceCode)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerIoT/devices/{deviceCode}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(DeleteDeviceAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<DeviceGroupInfo>?> GetDeviceGroupsAsync(string greenhouseId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerIoT/groups/{greenhouseId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<DeviceGroupInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetDeviceGroupsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<DeviceGroupInfo?> CreateDeviceGroupAsync(CreateDeviceGroupRequest request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerIoT/groups", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<DeviceGroupInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(CreateDeviceGroupAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> DeleteDeviceGroupAsync(string groupId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.DeleteAsync($"{baseUri}FlowerIoT/groups/{groupId}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(DeleteDeviceGroupAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> RenameDeviceGroupAsync(string groupId, string newName)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(new { NewName = newName }), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerIoT/groups/{groupId}", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(RenameDeviceGroupAsync)}: {ex.Message}"); return false; }
        }

        public async Task<SensorReadingInfo?> GetLatestSensorReadingAsync(string deviceId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSensorData/latest/{deviceId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<SensorReadingInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetLatestSensorReadingAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<SensorReadingInfo>?> GetSensorHistoryAsync(string deviceId, DateTime start, DateTime end)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSensorData/history/{deviceId}?start={Uri.EscapeDataString(start.ToString("o"))}&end={Uri.EscapeDataString(end.ToString("o"))}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<SensorReadingInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetSensorHistoryAsync)}: {ex.Message}"); return null; }
        }

        public async Task<Dictionary<string, double>?> GetSensorAggregatedStatsAsync(string deviceId, DateTime start, DateTime end)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSensorData/stats/{deviceId}?start={Uri.EscapeDataString(start.ToString("o"))}&end={Uri.EscapeDataString(end.ToString("o"))}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<Dictionary<string, double>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetSensorAggregatedStatsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<long> CreatePlantingBatchAsync(CreateBatchRequest request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerPlanting/batches", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return 0;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<long>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : 0;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(CreatePlantingBatchAsync)}: {ex.Message}"); return 0; }
        }

        public async Task<List<PlantingBatchInfo>?> GetPlantingBatchesAsync(string greenhouseId, string? status = null, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var url = $"{baseUri}FlowerPlanting/batches/{greenhouseId}?page={page}&pageSize={pageSize}";
                if (!string.IsNullOrEmpty(status)) url += $"&status={Uri.EscapeDataString(status)}";
                var response = await FlowerHttpConfig.HttpClient.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<PlantingBatchInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetPlantingBatchesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> UpdateBatchStatusAsync(long batchId, string status, DateTime? actualHarvestDate = null)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var request = new { Status = status, ActualHarvestDate = actualHarvestDate };
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerPlanting/batches/{batchId}/status", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(UpdateBatchStatusAsync)}: {ex.Message}"); return false; }
        }

        public async Task<long> AddCostRecordAsync(AddCostRecordRequest request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerCost/records", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return 0;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<long>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : 0;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(AddCostRecordAsync)}: {ex.Message}"); return 0; }
        }

        public async Task<List<CostRecordInfo>?> GetCostRecordsAsync(long batchId, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerCost/records/{batchId}?page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<CostRecordInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetCostRecordsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<CostCategoryStatsInfo>?> GetCostStatsAsync(long batchId, DateTime? start = null, DateTime? end = null)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var url = $"{baseUri}FlowerCost/stats/{batchId}";
                if (start.HasValue || end.HasValue)
                    url += $"?start={Uri.EscapeDataString((start ?? DateTime.MinValue).ToString("o"))}&end={Uri.EscapeDataString((end ?? DateTime.MaxValue).ToString("o"))}";
                var response = await FlowerHttpConfig.HttpClient.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<CostCategoryStatsInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetCostStatsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<long> AddYieldRecordAsync(AddYieldRecordRequest request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerYield/records", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return 0;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<long>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : 0;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(AddYieldRecordAsync)}: {ex.Message}"); return 0; }
        }

        public async Task<List<YieldRecordInfo>?> GetYieldRecordsAsync(long batchId, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerYield/records/{batchId}?page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<YieldRecordInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetYieldRecordsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<CostMonthlyTrendInfo>?> GetCostMonthlyTrendAsync(string greenhouseId, int months = 6)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerCost/trend/{greenhouseId}?months={months}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<CostMonthlyTrendInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetCostMonthlyTrendAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<YieldTrendInfo>?> GetYieldTrendAsync(string greenhouseId, int months = 6)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerYield/trend/{greenhouseId}?months={months}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<YieldTrendInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetYieldTrendAsync)}: {ex.Message}"); return null; }
        }

        public async Task<HarvestListingResultInfo?> CreateProductFromYieldAsync(long yieldRecordId, long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { YieldRecordId = yieldRecordId, MerchantId = merchantId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerYield/list-from-yield", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<HarvestListingResultInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(CreateProductFromYieldAsync)}: {ex.Message}"); return null; }
        }

        public async Task<HarvestListingResultInfo?> BatchCreateProductsFromYieldAsync(List<long> yieldRecordIds, long merchantId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { YieldRecordIds = yieldRecordIds, MerchantId = merchantId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerYield/batch-list", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<HarvestListingResultInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(BatchCreateProductsFromYieldAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> ConfirmHarvestListingAsync(long listingId, decimal actualPrice)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { ActualPrice = actualPrice }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerYield/confirm-listing/{listingId}", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(ConfirmHarvestListingAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<HarvestListingDetailInfo>?> GetHarvestListingsAsync(long merchantId, int status = -1, int page = 1, int pageSize = 20)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerYield/listings/{merchantId}?status={status}&page={page}&pageSize={pageSize}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<HarvestListingDetailInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetHarvestListingsAsync)}: {ex.Message}"); return null; }
        }

        public async Task<BatchLifecycleInfo?> GetBatchLifecycleAsync(long batchId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerPlanting/batches/{batchId}/lifecycle").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<BatchLifecycleInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetBatchLifecycleAsync)}: {ex.Message}"); return null; }
        }

        public async Task<BatchProfitAnalysisInfo?> GetBatchProfitAnalysisAsync(long batchId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerPlanting/batches/{batchId}/profit").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<BatchProfitAnalysisInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetBatchProfitAnalysisAsync)}: {ex.Message}"); return null; }
        }

        public async Task<PresaleFulfillmentStatusInfo?> GetPresaleStatusAsync(long batchId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerPlanting/batches/{batchId}/presale-status").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<PresaleFulfillmentStatusInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetPresaleStatusAsync)}: {ex.Message}"); return null; }
        }

        public async Task<IoTDeviceInfo?> BindDeviceAsync(BindDeviceRequestInfo request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerIoT/devices/bind", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<IoTDeviceInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(BindDeviceAsync)}: {ex.Message}"); return null; }
        }

        public async Task<IoTDeviceInfo?> UnbindDeviceAsync(string deviceCode)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerIoT/devices/{deviceCode}/unbind", null).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<IoTDeviceInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(UnbindDeviceAsync)}: {ex.Message}"); return null; }
        }

        public async Task<IoTDeviceInfo?> ChangeDeviceGroupAsync(string deviceCode, string groupId)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { GroupId = groupId }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerIoT/devices/{deviceCode}/group", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<IoTDeviceInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(ChangeDeviceGroupAsync)}: {ex.Message}"); return null; }
        }

        public async Task<TrendAnalysisInfo?> GetTrendAnalysisAsync(string deviceId, DateTime start, DateTime end, string granularity = "day")
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSensorData/analysis/trend/{deviceId}?start={Uri.EscapeDataString(start.ToString("o"))}&end={Uri.EscapeDataString(end.ToString("o"))}&granularity={granularity}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<TrendAnalysisInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetTrendAnalysisAsync)}: {ex.Message}"); return null; }
        }

        public async Task<DeviceComparisonInfo?> GetMultiDeviceComparisonAsync(List<string> deviceIds, DateTime start, DateTime end)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { DeviceIds = deviceIds, Start = start, End = end }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerSensorData/analysis/comparison", content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<DeviceComparisonInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetMultiDeviceComparisonAsync)}: {ex.Message}"); return null; }
        }

        public async Task<HealthIndexInfo?> GetHealthIndexAsync(string greenhouseId, DateTime start, DateTime end)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSensorData/analysis/health-index/{greenhouseId}?start={Uri.EscapeDataString(start.ToString("o"))}&end={Uri.EscapeDataString(end.ToString("o"))}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<HealthIndexInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetHealthIndexAsync)}: {ex.Message}"); return null; }
        }

        public async Task<List<AnomalyInfo>?> GetAnomaliesAsync(string deviceId, DateTime start, DateTime end)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerSensorData/analysis/anomalies/{deviceId}?start={Uri.EscapeDataString(start.ToString("o"))}&end={Uri.EscapeDataString(end.ToString("o"))}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<AnomalyInfo>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetAnomaliesAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> ReportSensorDataAsync(ManualSensorReportRequest request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerSensorData/manual-report", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(ReportSensorDataAsync)}: {ex.Message}"); return false; }
        }

        public async Task<DeviceTwinDisplayInfo?> GetDeviceTwinAsync(string deviceCode)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerIoT/devices/{deviceCode}/twin").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<DeviceTwinDisplayInfo>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetDeviceTwinAsync)}: {ex.Message}"); return null; }
        }

        public async Task<bool> SendDeviceCommandAsync(SendDeviceCommandRequest request)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var content = new StringContent(JsonSerializer.Serialize(request, FlowerHttpConfig.JsonOptions), Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PostAsync($"{baseUri}FlowerIoT/devices/{request.DeviceCode}/command", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(SendDeviceCommandAsync)}: {ex.Message}"); return false; }
        }

        public async Task<bool> SetThresholdAsync(string deviceCode, string metricName, double threshold)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var body = JsonSerializer.Serialize(new { MetricName = metricName, Threshold = threshold }, FlowerHttpConfig.JsonOptions);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await FlowerHttpConfig.HttpClient.PutAsync($"{baseUri}FlowerIoT/devices/{deviceCode}/threshold", content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(SetThresholdAsync)}: {ex.Message}"); return false; }
        }

        public async Task<List<string>?> GetGreenhousesAsync()
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerIoT/greenhouses").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new List<string> { "default" };
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<List<string>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true && result.Data?.Count > 0 ? result.Data : new List<string> { "default" };
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetGreenhousesAsync)}: {ex.Message}"); return new List<string> { "default" }; }
        }

        public async Task<Dictionary<string, double>?> GetThresholdsAsync(string deviceCode)
        {
            try
            {
                var baseUri = FlowerHttpConfig.GetBaseUri();
                var response = await FlowerHttpConfig.HttpClient.GetAsync($"{baseUri}FlowerIoT/devices/{deviceCode}/thresholds").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<FlowerApiResult<Dictionary<string, double>>>(json, FlowerHttpConfig.JsonOptions);
                return result?.IsSuccess == true ? result.Data : null;
            }
            catch (Exception ex) { Debug.WriteLine($"[FlowerIoTService] {nameof(GetThresholdsAsync)}: {ex.Message}"); return null; }
        }
    }
}
