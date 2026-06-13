using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class SensorDataGrain : Grain, ISensorDataGrain
    {
        private readonly ILogger<SensorDataGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerSensorReading, long> _dataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPlantingBatch, long> _batchContext;
        private readonly IDataContext<FlowerEntityContext, FlowerIoTDevice, long> _deviceContext;

        public SensorDataGrain(
            ILogger<SensorDataGrain> logger,
            IDataContext<FlowerEntityContext, FlowerSensorReading, long> dataContext,
            IDataContext<FlowerEntityContext, FlowerPlantingBatch, long> batchContext,
            IDataContext<FlowerEntityContext, FlowerIoTDevice, long> deviceContext)
        {
            _logger = logger;
            _dataContext = dataContext;
            _batchContext = batchContext;
            _deviceContext = deviceContext;
        }

        public async Task ReportReadingAsync(SensorReading reading)
        {
            try
            {
                if (reading == null) return;

                var deviceCode = reading.DeviceId;
                var devices = await _deviceContext.QueryAsync(d => d.DeviceCode == deviceCode);
                var passport = devices.FirstOrDefault()?.Passport ?? reading.GreenhouseId;

                var entity = new FlowerSensorReading
                {
                    DeviceId = reading.DeviceId,
                    GreenhouseId = reading.GreenhouseId,
                    Passport = passport,
                    Temperature = reading.Temperature,
                    Humidity = reading.Humidity,
                    LightIntensity = reading.LightIntensity,
                    Co2Level = reading.Co2Level,
                    SoilMoisture = reading.SoilMoisture,
                    ReadingTime = reading.ReadingTime,
                    DataQuality = "Normal",
                    DataSource = "MQTT"
                };

                if (!string.IsNullOrEmpty(reading.GreenhouseId))
                {
                    try
                    {
                        var batches = await _batchContext.QueryAsync(b =>
                            b.GreenhouseId == reading.GreenhouseId &&
                            (b.Status == "Planted" || b.Status == "Growing") &&
                            !b.IsDeleted);
                        var batch = batches.OrderByDescending(b => b.CreateTime).FirstOrDefault();
                        if (batch != null)
                        {
                            entity.BatchId = batch.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "查询种植批次失败: GreenhouseId={GreenhouseId}", reading.GreenhouseId);
                    }
                }

                await _dataContext.AddAsync(entity);

                _logger.LogInformation("上报传感器数据: DeviceId={DeviceId}", reading.DeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "上报传感器数据失败: DeviceId={DeviceId}", reading.DeviceId);
                throw;
            }
        }

        public async Task ReportManualReadingAsync(SensorReading reading)
        {
            try
            {
                if (reading == null) return;

                var deviceGrain = GrainFactory.GetGrain<IIoTDeviceGrain>(reading.DeviceId);
                if (!await deviceGrain.IsOnlineAsync())
                {
                    throw new InvalidOperationException("Device is offline, cannot submit manual reading");
                }

                var deviceCode = reading.DeviceId;
                var devices = await _deviceContext.QueryAsync(d => d.DeviceCode == deviceCode);
                var passport = devices.FirstOrDefault()?.Passport ?? reading.GreenhouseId;

                var entity = new FlowerSensorReading
                {
                    DeviceId = reading.DeviceId,
                    GreenhouseId = reading.GreenhouseId,
                    Passport = passport,
                    Temperature = reading.Temperature,
                    Humidity = reading.Humidity,
                    LightIntensity = reading.LightIntensity,
                    Co2Level = reading.Co2Level,
                    SoilMoisture = reading.SoilMoisture,
                    ReadingTime = reading.ReadingTime,
                    DataQuality = "Normal",
                    DataSource = "Manual"
                };

                if (!string.IsNullOrEmpty(reading.GreenhouseId))
                {
                    try
                    {
                        var batches = await _batchContext.QueryAsync(b =>
                            b.GreenhouseId == reading.GreenhouseId &&
                            (b.Status == "Planted" || b.Status == "Growing") &&
                            !b.IsDeleted);
                        var batch = batches.OrderByDescending(b => b.CreateTime).FirstOrDefault();
                        if (batch != null)
                        {
                            entity.BatchId = batch.Id;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "查询种植批次失败: GreenhouseId={GreenhouseId}", reading.GreenhouseId);
                    }
                }

                await _dataContext.AddAsync(entity);

                _logger.LogInformation("手动上报传感器数据: DeviceId={DeviceId}", reading.DeviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "手动上报传感器数据失败: DeviceId={DeviceId}", reading.DeviceId);
                throw;
            }
        }

        public async Task<SensorReading> GetLatestReadingAsync(string deviceId)
        {
            try
            {
                var deviceGrain = GrainFactory.GetGrain<IIoTDeviceGrain>(deviceId);
                return await deviceGrain.GetLatestReadingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最新传感器数据失败: DeviceId={DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<List<SensorReading>> GetHistoryReadingsAsync(string deviceId, DateTime start, DateTime end)
        {
            try
            {
                var readings = await _dataContext.QueryAsync(r =>
                    r.DeviceId == deviceId &&
                    r.ReadingTime >= start &&
                    r.ReadingTime <= end);

                return readings
                    .OrderBy(r => r.ReadingTime)
                    .Select(r => new SensorReading
                    {
                        DeviceId = r.DeviceId,
                        GreenhouseId = r.GreenhouseId,
                        Passport = r.Passport,
                        Temperature = r.Temperature,
                        Humidity = r.Humidity,
                        LightIntensity = r.LightIntensity,
                        Co2Level = r.Co2Level,
                        SoilMoisture = r.SoilMoisture,
                        ReadingTime = r.ReadingTime
                    }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取历史传感器数据失败: DeviceId={DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<Dictionary<string, double>> GetAggregatedStatsAsync(string deviceId, DateTime start, DateTime end)
        {
            try
            {
                var readings = await _dataContext.QueryAsync(r =>
                    r.DeviceId == deviceId &&
                    r.ReadingTime >= start &&
                    r.ReadingTime <= end &&
                    r.DataQuality == "Normal");

                var list = readings.ToList();
                if (list.Count == 0) return new Dictionary<string, double>();

                return new Dictionary<string, double>
                {
                    ["AvgTemperature"] = list.Average(r => r.Temperature),
                    ["MinTemperature"] = list.Min(r => r.Temperature),
                    ["MaxTemperature"] = list.Max(r => r.Temperature),
                    ["AvgHumidity"] = list.Average(r => r.Humidity),
                    ["AvgLightIntensity"] = list.Average(r => r.LightIntensity),
                    ["AvgCo2Level"] = list.Average(r => r.Co2Level),
                    ["AvgSoilMoisture"] = list.Average(r => r.SoilMoisture),
                    ["ReadingCount"] = list.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取传感器聚合统计失败: DeviceId={DeviceId}", deviceId);
                throw;
            }
        }

        public async Task<List<SensorReading>> GetMultiDeviceReadingsAsync(List<string> deviceIds, DateTime start, DateTime end)
        {
            try
            {
                var allReadings = new List<SensorReading>();
                foreach (var deviceId in deviceIds)
                {
                    var readings = await GetHistoryReadingsAsync(deviceId, start, end);
                    allReadings.AddRange(readings);
                }
                return allReadings.OrderBy(r => r.ReadingTime).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取多设备传感器数据失败");
                throw;
            }
        }

        public async Task<TrendAnalysisResult> GetTrendAnalysisAsync(string deviceId, DateTime start, DateTime end, string granularity)
        {
            var readings = await _dataContext.QueryAsync(r =>
                r.DeviceId == deviceId &&
                r.ReadingTime >= start &&
                r.ReadingTime <= end &&
                r.DataQuality == "Normal");

            var list = readings.ToList();
            if (list.Count == 0) return new TrendAnalysisResult { DeviceId = deviceId, Granularity = granularity };

            IEnumerable<IGrouping<DateTime, FlowerSensorReading>> grouped = granularity switch
            {
                "week" => list.GroupBy(r => new DateTime(r.ReadingTime.Year, r.ReadingTime.Month, r.ReadingTime.Day)),
                "month" => list.GroupBy(r => new DateTime(r.ReadingTime.Year, r.ReadingTime.Month, 1)),
                _ => list.GroupBy(r => r.ReadingTime.Date)
            };

            var dataPoints = new List<TrendDataPoint>();
            var orderedGroups = grouped.OrderBy(g => g.Min(r => r.ReadingTime)).ToList();

            for (int i = 0; i < orderedGroups.Count; i++)
            {
                var g = orderedGroups[i];
                var point = new TrendDataPoint
                {
                    Time = g.Min(r => r.ReadingTime),
                    AvgTemperature = g.Average(r => r.Temperature),
                    AvgHumidity = g.Average(r => r.Humidity),
                    AvgLightIntensity = g.Average(r => r.LightIntensity),
                    AvgCo2Level = g.Average(r => r.Co2Level),
                    AvgSoilMoisture = g.Average(r => r.SoilMoisture)
                };

                if (i > 0)
                {
                    var prev = dataPoints[i - 1];
                    point.TemperatureChangeRate = prev.AvgTemperature != 0 ? (point.AvgTemperature - prev.AvgTemperature) / Math.Abs(prev.AvgTemperature) * 100 : 0;
                    point.HumidityChangeRate = prev.AvgHumidity != 0 ? (point.AvgHumidity - prev.AvgHumidity) / Math.Abs(prev.AvgHumidity) * 100 : 0;
                    point.LightChangeRate = prev.AvgLightIntensity != 0 ? (point.AvgLightIntensity - prev.AvgLightIntensity) / Math.Abs(prev.AvgLightIntensity) * 100 : 0;
                    point.Co2ChangeRate = prev.AvgCo2Level != 0 ? (point.AvgCo2Level - prev.AvgCo2Level) / Math.Abs(prev.AvgCo2Level) * 100 : 0;
                    point.SoilMoistureChangeRate = prev.AvgSoilMoisture != 0 ? (point.AvgSoilMoisture - prev.AvgSoilMoisture) / Math.Abs(prev.AvgSoilMoisture) * 100 : 0;
                }

                dataPoints.Add(point);
            }

            var significantChanges = new List<SignificantChangePoint>();
            var metrics = new[] { "Temperature", "Humidity", "LightIntensity", "Co2Level", "SoilMoisture" };
            foreach (var dp in dataPoints.Skip(1))
            {
                var rates = new Dictionary<string, double>
                {
                    ["Temperature"] = dp.TemperatureChangeRate,
                    ["Humidity"] = dp.HumidityChangeRate,
                    ["LightIntensity"] = dp.LightChangeRate,
                    ["Co2Level"] = dp.Co2ChangeRate,
                    ["SoilMoisture"] = dp.SoilMoistureChangeRate
                };
                foreach (var metric in metrics)
                {
                    if (Math.Abs(rates[metric]) > 20)
                    {
                        var prevDp = dataPoints.Last(p => p.Time < dp.Time);
                        significantChanges.Add(new SignificantChangePoint
                        {
                            Time = dp.Time,
                            Metric = metric,
                            ChangeRate = rates[metric],
                            PreviousValue = metric switch
                            {
                                "Temperature" => prevDp.AvgTemperature,
                                "Humidity" => prevDp.AvgHumidity,
                                "LightIntensity" => prevDp.AvgLightIntensity,
                                "Co2Level" => prevDp.AvgCo2Level,
                                "SoilMoisture" => prevDp.AvgSoilMoisture,
                                _ => 0
                            },
                            CurrentValue = metric switch
                            {
                                "Temperature" => dp.AvgTemperature,
                                "Humidity" => dp.AvgHumidity,
                                "LightIntensity" => dp.AvgLightIntensity,
                                "Co2Level" => dp.AvgCo2Level,
                                "SoilMoisture" => dp.AvgSoilMoisture,
                                _ => 0
                            }
                        });
                    }
                }
            }

            return new TrendAnalysisResult
            {
                DeviceId = deviceId,
                Granularity = granularity,
                DataPoints = dataPoints,
                SignificantChanges = significantChanges
            };
        }

        public async Task<MultiDeviceComparisonResult> GetMultiDeviceComparisonAsync(List<string> deviceIds, DateTime start, DateTime end)
        {
            var devices = new List<DeviceComparisonItem>();
            foreach (var deviceId in deviceIds)
            {
                var readings = await _dataContext.QueryAsync(r =>
                    r.DeviceId == deviceId &&
                    r.ReadingTime >= start &&
                    r.ReadingTime <= end &&
                    r.DataQuality == "Normal");
                var list = readings.ToList();
                if (list.Count == 0) continue;

                devices.Add(new DeviceComparisonItem
                {
                    DeviceId = deviceId,
                    AvgTemperature = list.Average(r => r.Temperature),
                    AvgHumidity = list.Average(r => r.Humidity),
                    AvgLightIntensity = list.Average(r => r.LightIntensity),
                    AvgCo2Level = list.Average(r => r.Co2Level),
                    AvgSoilMoisture = list.Average(r => r.SoilMoisture)
                });
            }

            var differences = new List<MetricDifference>();
            if (devices.Count >= 2)
            {
                var d1 = devices[0];
                var d2 = devices[1];
                var metrics = new Dictionary<string, (double v1, double v2)>
                {
                    ["Temperature"] = (d1.AvgTemperature, d2.AvgTemperature),
                    ["Humidity"] = (d1.AvgHumidity, d2.AvgHumidity),
                    ["LightIntensity"] = (d1.AvgLightIntensity, d2.AvgLightIntensity),
                    ["Co2Level"] = (d1.AvgCo2Level, d2.AvgCo2Level),
                    ["SoilMoisture"] = (d1.AvgSoilMoisture, d2.AvgSoilMoisture)
                };

                foreach (var kv in metrics)
                {
                    var diff = Math.Abs(kv.Value.v1 - kv.Value.v2);
                    var baseVal = (kv.Value.v1 + kv.Value.v2) / 2;
                    differences.Add(new MetricDifference
                    {
                        Metric = kv.Key,
                        Difference = diff,
                        DifferencePercentage = baseVal != 0 ? diff / Math.Abs(baseVal) * 100 : 0
                    });
                }
            }

            var maxDiffMetric = differences.Count > 0
                ? differences.OrderByDescending(d => d.DifferencePercentage).First().Metric
                : "";

            return new MultiDeviceComparisonResult
            {
                Devices = devices,
                Differences = differences,
                MaxDifferenceMetric = maxDiffMetric
            };
        }

        public async Task<HealthIndexResult> GetHealthIndexAsync(string greenhouseId, DateTime start, DateTime end)
        {
            var readings = await _dataContext.QueryAsync(r =>
                r.GreenhouseId == greenhouseId &&
                r.ReadingTime >= start &&
                r.ReadingTime <= end &&
                r.DataQuality == "Normal");

            var list = readings.ToList();
            if (list.Count == 0) return new HealthIndexResult { GreenhouseId = greenhouseId, CalculatedAt = DateTime.Now };

            var avgTemp = list.Average(r => r.Temperature);
            var avgHumidity = list.Average(r => r.Humidity);
            var avgLight = list.Average(r => r.LightIntensity);
            var avgCo2 = list.Average(r => r.Co2Level);
            var avgSoil = list.Average(r => r.SoilMoisture);

            double CalcScore(double value, double optLow, double optHigh, double absLow, double absHigh)
            {
                if (value >= optLow && value <= optHigh) return 100;
                if (value < optLow) return Math.Max(0, 100 - (optLow - value) / (optLow - absLow) * 100);
                return Math.Max(0, 100 - (value - optHigh) / (absHigh - optHigh) * 100);
            }

            var tempScore = CalcScore(avgTemp, 15, 25, 0, 45);
            var humidityScore = CalcScore(avgHumidity, 50, 70, 0, 100);
            var lightScore = CalcScore(avgLight, 5000, 20000, 0, 40000);
            var co2Score = CalcScore(avgCo2, 300, 500, 0, 1000);
            var soilScore = CalcScore(avgSoil, 40, 60, 0, 100);

            return new HealthIndexResult
            {
                GreenhouseId = greenhouseId,
                OverallScore = (tempScore + humidityScore + lightScore + co2Score + soilScore) / 5,
                TemperatureScore = tempScore,
                HumidityScore = humidityScore,
                LightScore = lightScore,
                Co2Score = co2Score,
                SoilMoistureScore = soilScore,
                CalculatedAt = DateTime.Now
            };
        }

        public async Task<List<AnomalyDataPoint>> GetAnomaliesAsync(string deviceId, DateTime start, DateTime end)
        {
            var readings = await _dataContext.QueryAsync(r =>
                r.DeviceId == deviceId &&
                r.ReadingTime >= start &&
                r.ReadingTime <= end);

            var list = readings.ToList();
            if (list.Count < 3) return new List<AnomalyDataPoint>();

            var normalReadings = list.Where(r => r.DataQuality == "Normal").ToList();
            if (normalReadings.Count < 3) return new List<AnomalyDataPoint>();

            var anomalies = new List<AnomalyDataPoint>();
            var metrics = new Dictionary<string, Func<FlowerSensorReading, double>>
            {
                ["Temperature"] = r => r.Temperature,
                ["Humidity"] = r => r.Humidity,
                ["LightIntensity"] = r => r.LightIntensity,
                ["Co2Level"] = r => r.Co2Level,
                ["SoilMoisture"] = r => r.SoilMoisture
            };

            foreach (var kv in metrics)
            {
                var values = normalReadings.Select(kv.Value).ToList();
                var mean = values.Average();
                var stdDev = Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / values.Count);

                if (stdDev == 0) continue;

                foreach (var reading in list)
                {
                    var val = kv.Value(reading);
                    if (Math.Abs(val - mean) > 3 * stdDev)
                    {
                        anomalies.Add(new AnomalyDataPoint
                        {
                            ReadingId = reading.Id,
                            DeviceId = reading.DeviceId,
                            Metric = kv.Key,
                            Value = val,
                            Mean = mean,
                            StdDev = stdDev,
                            ReadingTime = reading.ReadingTime
                        });
                    }
                }
            }

            return anomalies;
        }
    }
}
