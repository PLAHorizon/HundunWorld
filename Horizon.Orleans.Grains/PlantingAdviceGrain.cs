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
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class PlantingAdviceGrain : Grain, IPlantingAdviceGrain
    {
        private readonly ILogger<PlantingAdviceGrain> _logger;
        private readonly IPersistentState<AdviceState> _adviceState;
        private readonly IDataContext<FlowerEntityContext, FlowerSensorReading, long> _sensorContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPlantingBatch, long> _batchContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPlantingAdvice, long> _adviceContext;

        public PlantingAdviceGrain(
            ILogger<PlantingAdviceGrain> logger,
            [PersistentState("advice", "FlowerStore")] IPersistentState<AdviceState> adviceState,
            IDataContext<FlowerEntityContext, FlowerSensorReading, long> sensorContext,
            IDataContext<FlowerEntityContext, FlowerPlantingBatch, long> batchContext,
            IDataContext<FlowerEntityContext, FlowerPlantingAdvice, long> adviceContext)
        {
            _logger = logger;
            _adviceState = adviceState;
            _sensorContext = sensorContext;
            _batchContext = batchContext;
            _adviceContext = adviceContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (_adviceState.State.Advices == null)
                _adviceState.State.Advices = new List<PlantingAdviceItem>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<List<PlantingAdviceItem>> GenerateAdviceAsync(long batchId)
        {
            try
            {
                var batch = await _batchContext.QueryFirstOrDefaultAsync(b => b.Id == batchId && !b.IsDeleted);
                if (batch == null) return new List<PlantingAdviceItem>();

                var readings = await _sensorContext.QueryAsync(r =>
                    r.GreenhouseId == batch.GreenhouseId &&
                    r.ReadingTime >= DateTime.Now.AddHours(-24));
                var recentReadings = readings.OrderByDescending(r => r.ReadingTime).Take(50).ToList();

                var advices = new List<PlantingAdviceItem>();

                if (recentReadings.Count > 0)
                {
                    var avgTemp = recentReadings.Average(r => r.Temperature);
                    var avgHumidity = recentReadings.Average(r => r.Humidity);
                    var avgSoil = recentReadings.Average(r => r.SoilMoisture);
                    var avgCo2 = recentReadings.Average(r => r.Co2Level);

                    if (avgSoil < 40)
                    {
                        advices.Add(CreateAdvice(batchId, "Irrigation", "灌溉建议",
                            $"土壤湿度平均{avgSoil:F1}%，低于40%阈值，建议立即灌溉。",
                            "传感器数据", "High"));
                    }

                    if (avgTemp > 30)
                    {
                        advices.Add(CreateAdvice(batchId, "Ventilation", "通风降温建议",
                            $"温度平均{avgTemp:F1}°C，超过30°C，建议开启通风设备降温。",
                            "传感器数据", "High"));
                    }
                    else if (avgTemp < 15)
                    {
                        advices.Add(CreateAdvice(batchId, "Ventilation", "保温建议",
                            $"温度平均{avgTemp:F1}°C，低于15°C，建议关闭通风并加强保温。",
                            "传感器数据", "High"));
                    }

                    if (avgHumidity > 85)
                    {
                        advices.Add(CreateAdvice(batchId, "Pest", "灰霉病风险预警",
                            $"湿度平均{avgHumidity:F1}%，持续高湿度环境灰霉病风险增大，建议加强通风。",
                            "综合分析", "High"));
                    }

                    if (avgCo2 > 500)
                    {
                        advices.Add(CreateAdvice(batchId, "Ventilation", "CO₂通风建议",
                            $"CO₂浓度平均{avgCo2:F0}ppm，高于500ppm，建议通风换气。",
                            "传感器数据", "Normal"));
                    }

                    if (batch.Status == "Growing" && batch.ExpectedHarvestDate.HasValue)
                    {
                        var daysToHarvest = (batch.ExpectedHarvestDate.Value - DateTime.Now).Days;
                        if (daysToHarvest <= 7 && daysToHarvest > 0)
                        {
                            advices.Add(CreateAdvice(batchId, "Harvest", "采收准备建议",
                                $"距预期采收日还有{daysToHarvest}天，建议做好采收准备。",
                                "综合分析", "Normal"));
                        }
                    }
                }

                if (advices.Count == 0)
                {
                    advices.Add(CreateAdvice(batchId, "General", "环境正常",
                        "当前温室环境各项指标正常，无需特别操作。",
                        "综合分析", "Low"));
                }

                _adviceState.State.Advices.RemoveAll(a =>
                    a.Status != "Pending" &&
                    a.ExecutedTime.HasValue &&
                    a.ExecutedTime.Value < DateTime.Now.AddDays(-30));

                foreach (var advice in advices)
                {
                    var exists = _adviceState.State.Advices.Any(a =>
                        a.BatchId == advice.BatchId &&
                        a.Status == "Pending" &&
                        a.AdviceType == advice.AdviceType &&
                        a.Title == advice.Title);
                    if (!exists)
                    {
                        _adviceState.State.Advices.Add(advice);

                        var dbAdvice = new FlowerPlantingAdvice
                        {
                            BatchId = advice.BatchId,
                            AdviceType = advice.AdviceType,
                            Title = advice.Title,
                            Content = advice.Content,
                            Source = advice.Source,
                            Priority = advice.Priority,
                            Status = advice.Status,
                            GeneratedTime = advice.GeneratedTime,
                            ExecutedTime = advice.ExecutedTime,
                            IsDeleted = false
                        };
                        await _adviceContext.AddAsync(dbAdvice);
                    }
                }
                await _adviceState.WriteStateAsync();

                return advices;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成种植建议失败: BatchId={BatchId}", batchId);
                throw;
            }
        }

        public Task<List<PlantingAdviceItem>> GetActiveAdviceAsync(long batchId)
        {
            var advices = _adviceState.State.Advices
                .Where(a => a.BatchId == batchId && a.Status == "Pending")
                .OrderByDescending(a => a.Priority == "High")
                .ThenByDescending(a => a.GeneratedTime)
                .ToList();
            return Task.FromResult(advices);
        }

        public async Task MarkAdviceExecutedAsync(long adviceId, string action)
        {
            var advice = _adviceState.State.Advices.FirstOrDefault(a => a.Id == adviceId);
            if (advice != null)
            {
                advice.Status = action;
                advice.ExecutedTime = DateTime.Now;
                await _adviceState.WriteStateAsync();

                var dbAdvice = await _adviceContext.QueryFirstOrDefaultAsync(a =>
                    a.BatchId == advice.BatchId &&
                    a.AdviceType == advice.AdviceType &&
                    a.Title == advice.Title &&
                    a.Status == "Pending" &&
                    !a.IsDeleted);
                if (dbAdvice != null)
                {
                    dbAdvice.Status = action;
                    dbAdvice.ExecutedTime = DateTime.Now;
                    await _adviceContext.UpdateAsync(dbAdvice, dbAdvice.Id);
                }
            }
        }

        public Task<List<PlantingAdviceItem>> GetAdviceByTypeAsync(long batchId, string adviceType)
        {
            var advices = _adviceState.State.Advices
                .Where(a => a.BatchId == batchId && a.AdviceType == adviceType)
                .OrderByDescending(a => a.GeneratedTime)
                .ToList();
            return Task.FromResult(advices);
        }

        private static PlantingAdviceItem CreateAdvice(long batchId, string type, string title, string content, string source, string priority) => new()
        {
            Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            BatchId = batchId,
            AdviceType = type,
            Title = title,
            Content = content,
            Source = source,
            Priority = priority,
            Status = "Pending",
            GeneratedTime = DateTime.Now
        };
    }

    [Serializable]
    [GenerateSerializer]
    public class AdviceState
    {
        [Id(0)]
        public List<PlantingAdviceItem> Advices { get; set; } = new();
    }
}
