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
    public class IoTAlertRuleGrain : Grain, IIoTAlertRuleGrain
    {
        private readonly ILogger<IoTAlertRuleGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerAlertLog, long> _logDataContext;
        private bool _isEnabled = true;

        private static readonly Dictionary<string, Func<SensorReading, double>> _metricExtractors = new()
        {
            ["Temperature"] = r => r.Temperature,
            ["Humidity"] = r => r.Humidity,
            ["LightIntensity"] = r => r.LightIntensity,
            ["Co2Level"] = r => r.Co2Level,
            ["SoilMoisture"] = r => r.SoilMoisture
        };

        private static readonly Dictionary<string, bool> _alertAboveThreshold = new()
        {
            ["Temperature"] = true,
            ["Humidity"] = true,
            ["Co2Level"] = true,
            ["LightIntensity"] = true,
            ["SoilMoisture"] = false
        };

        public IoTAlertRuleGrain(
            ILogger<IoTAlertRuleGrain> logger,
            IDataContext<FlowerEntityContext, FlowerAlertLog, long> logDataContext)
        {
            _logger = logger;
            _logDataContext = logDataContext;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("IoTAlertRuleGrain {DeviceId} activating.", this.GetPrimaryKeyString());
            return base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> EvaluateAsync(SensorReading reading)
        {
            try
            {
                if (reading == null)
                {
                    _logger.LogWarning("评估预警无效: reading is null, DeviceId={DeviceId}", this.GetPrimaryKeyString());
                    return false;
                }

                if (!_isEnabled)
                {
                    _logger.LogDebug("规则已禁用，跳过评估: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                    return false;
                }

                var deviceCode = this.GetPrimaryKeyString();
                var deviceGrain = GrainFactory.GetGrain<IIoTDeviceGrain>(deviceCode);
                var thresholds = await deviceGrain.GetThresholdsAsync();

                if (thresholds == null || thresholds.Count == 0)
                {
                    _logger.LogDebug("设备无阈值配置，跳过评估: DeviceId={DeviceId}", deviceCode);
                    return false;
                }

                bool anyTriggered = false;

                foreach (var threshold in thresholds)
                {
                    if (!_metricExtractors.TryGetValue(threshold.Key, out var extractor))
                    {
                        _logger.LogDebug("未知指标名称，跳过: MetricName={MetricName}, DeviceId={DeviceId}", threshold.Key, deviceCode);
                        continue;
                    }

                    var metricValue = extractor(reading);

                    var isAbove = _alertAboveThreshold.GetValueOrDefault(threshold.Key, true);
                    bool exceeded = isAbove ? metricValue > threshold.Value : metricValue < threshold.Value;

                    if (exceeded)
                    {
                        anyTriggered = true;

                        var now = DateTime.Now;
                        var alertMessage = $"设备{deviceCode}的{threshold.Key}超过阈值: 当前值={metricValue:F2}, 阈值={threshold.Value:F2}";

                        var alertLog = new FlowerAlertLog
                        {
                            RuleId = 0,
                            UserId = Guid.Empty,
                            SpeciesId = 0,
                            MarketId = 0,
                            AlertType = 100,
                            AlertMessage = alertMessage,
                            TriggeredValue = (decimal)metricValue,
                            ThresholdValue = (decimal)threshold.Value,
                            IsRead = false,
                            CreatedAt = now
                        };

                        var result = await _logDataContext.AddAsync(alertLog);
                        if (result == null)
                        {
                            _logger.LogError("保存预警日志失败: DeviceId={DeviceId}, MetricName={MetricName}", deviceCode, threshold.Key);
                            continue;
                        }

                        _logger.LogInformation("预警触发: DeviceId={DeviceId}, MetricName={MetricName}, Value={Value}, Threshold={Threshold}",
                            deviceCode, threshold.Key, metricValue, threshold.Value);
                    }
                }

                return anyTriggered;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "评估预警失败: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public async Task<AlertRuleState> GetRuleStateAsync()
        {
            try
            {
                var deviceCode = this.GetPrimaryKeyString();
                var deviceGrain = GrainFactory.GetGrain<IIoTDeviceGrain>(deviceCode);
                var thresholds = await deviceGrain.GetThresholdsAsync();

                var thresholdValue = 0m;
                var conditionType = AlertConditionType.PriceAbove;

                if (thresholds != null && thresholds.Count > 0)
                {
                    var first = thresholds.First();
                    thresholdValue = (decimal)first.Value;
                    conditionType = first.Key switch
                    {
                        "Temperature" => AlertConditionType.PriceAbove,
                        "Humidity" => AlertConditionType.PriceBelow,
                        "Co2Level" => AlertConditionType.PriceChangeAbove,
                        "LightIntensity" => AlertConditionType.PriceAbove,
                        "SoilMoisture" => AlertConditionType.PriceChangeBelow,
                        _ => AlertConditionType.PriceAbove
                    };
                }

                return new AlertRuleState
                {
                    RuleId = 0,
                    UserId = Guid.Empty,
                    SpeciesId = 0,
                    ConditionType = conditionType,
                    ThresholdValue = thresholdValue,
                    IsEnabled = _isEnabled,
                    LastTriggeredAt = DateTime.MinValue,
                    PreviousPrice = 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取规则状态失败: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public async Task UpdateThresholdsAsync(AlertConditionType conditionType, decimal threshold, bool isEnabled)
        {
            try
            {
                var deviceCode = this.GetPrimaryKeyString();
                var deviceGrain = GrainFactory.GetGrain<IIoTDeviceGrain>(deviceCode);

                var metricName = conditionType switch
                {
                    AlertConditionType.PriceAbove => "Temperature",
                    AlertConditionType.PriceBelow => "Humidity",
                    AlertConditionType.PriceChangeAbove => "Co2Level",
                    AlertConditionType.PriceChangeBelow => "SoilMoisture",
                    _ => "Temperature"
                };

                await deviceGrain.SetThresholdAsync(metricName, (double)threshold);

                _isEnabled = isEnabled;

                _logger.LogInformation("更新规则: DeviceId={DeviceId}, ConditionType={ConditionType}, Threshold={Threshold}, IsEnabled={IsEnabled}",
                    deviceCode, conditionType, threshold, isEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新规则失败: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                throw;
            }
        }
    }
}
