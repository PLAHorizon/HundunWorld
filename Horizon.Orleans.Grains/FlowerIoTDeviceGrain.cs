using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.IoT.MQTT;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using Orleans;
using Orleans.Runtime;
using MemoryPack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// IoT设备Grain实现 - 负责传感器数据与阈值管理
    /// </summary>
    public class FlowerIoTDeviceGrain : Grain, IIoTDeviceGrain
    {
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingCommands = new();

        private readonly ILogger<FlowerIoTDeviceGrain> _logger;
        private readonly IPersistentState<IoTDeviceState> _deviceState;
        private readonly IDataContext<FlowerEntityContext, FlowerSensorReading, long> _dataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerPlantingBatch, long> _batchContext;

        public FlowerIoTDeviceGrain(
            ILogger<FlowerIoTDeviceGrain> logger,
            [PersistentState("iotdevice", "FlowerStore")] IPersistentState<IoTDeviceState> deviceState,
            IDataContext<FlowerEntityContext, FlowerSensorReading, long> dataContext,
            IDataContext<FlowerEntityContext, FlowerPlantingBatch, long> batchContext)
        {
            _logger = logger;
            _deviceState = deviceState;
            _dataContext = dataContext;
            _batchContext = batchContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerIoTDeviceGrain {GrainKey} activating.", this.GetPrimaryKeyString());

            var deviceId = this.GetPrimaryKeyString();
            if (_deviceState.State.Thresholds == null)
                _deviceState.State.Thresholds = new Dictionary<string, double>();
            if (string.IsNullOrEmpty(_deviceState.State.DeviceId))
                _deviceState.State.DeviceId = deviceId;

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task UpdateReadingAsync(SensorReading reading)
        {
            try
            {
                if (reading == null)
                {
                    _logger.LogWarning("更新传感器读数无效: reading is null");
                    return;
                }

                var entity = new FlowerSensorReading
                {
                    DeviceId = reading.DeviceId,
                    GreenhouseId = reading.GreenhouseId,
                    Temperature = reading.Temperature,
                    Humidity = reading.Humidity,
                    LightIntensity = reading.LightIntensity,
                    Co2Level = reading.Co2Level,
                    SoilMoisture = reading.SoilMoisture,
                    ReadingTime = reading.ReadingTime
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

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("保存传感器读数失败: 数据库保存返回null, DeviceId={DeviceId}", reading.DeviceId);
                    return;
                }

                var state = _deviceState.State;
                state.LatestReading = reading;
                state.IsOnline = true;
                state.LastHeartbeat = DateTime.Now;
                if (string.IsNullOrEmpty(state.GreenhouseId) && !string.IsNullOrEmpty(reading.GreenhouseId))
                    state.GreenhouseId = reading.GreenhouseId;

                await _deviceState.WriteStateAsync();

                var dataPoolGrain = GrainFactory.GetGrain<IFlowerDataPoolGrain>(0);
                var dataPoolEntry = new DataPoolEntry
                {
                    DataType = DataPoolDataType.SensorData,
                    DataSource = 0,
                    RawPayload = Convert.ToBase64String(MemoryPackSerializer.Serialize(reading)),
                    Timestamp = reading.ReadingTime,
                    RelatedEntityId = result.Id.ToString(),
                    ModelVersion = "",
                    Confidence = null
                };
                await dataPoolGrain.WriteAsync(dataPoolEntry);

                _logger.LogInformation("更新传感器读数: DeviceId={DeviceId}, Temperature={Temperature}, Humidity={Humidity}",
                    reading.DeviceId, reading.Temperature, reading.Humidity);

                try
                {
                    var alertGrain = GrainFactory.GetGrain<IIoTAlertRuleGrain>(this.GetPrimaryKeyString());
                    await alertGrain.EvaluateAsync(reading);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "预警评估失败: DeviceId={DeviceId}", reading.DeviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新传感器读数失败: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public Task<SensorReading> GetLatestReadingAsync()
        {
            try
            {
                var state = _deviceState.State;
                return Task.FromResult(state.LatestReading);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最新读数失败: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public async Task SetThresholdAsync(string metricName, double threshold)
        {
            try
            {
                var state = _deviceState.State;

                if (string.IsNullOrWhiteSpace(metricName))
                {
                    _logger.LogWarning("设置阈值无效: metricName is empty");
                    return;
                }

                state.Thresholds[metricName] = threshold;
                await _deviceState.WriteStateAsync();

                _logger.LogInformation("设置阈值: DeviceId={DeviceId}, MetricName={MetricName}, Threshold={Threshold}",
                    state.DeviceId, metricName, threshold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置阈值失败: DeviceId={DeviceId}, MetricName={MetricName}", this.GetPrimaryKeyString(), metricName);
                throw;
            }
        }

        public Task<Dictionary<string, double>> GetThresholdsAsync()
        {
            try
            {
                var state = _deviceState.State;
                return Task.FromResult(state.Thresholds ?? new Dictionary<string, double>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取阈值配置失败: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public Task<bool> IsOnlineAsync()
        {
            try
            {
                var state = _deviceState.State;
                return Task.FromResult(state.IsOnline);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查设备在线状态失败: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public async Task SendCommandAsync(string action, string payload)
        {
            try
            {
                var state = _deviceState.State;
                var deviceCode = this.GetPrimaryKeyString();
                var greenhouseId = state.GreenhouseId;

                if (string.IsNullOrEmpty(greenhouseId))
                {
                    _logger.LogWarning("发送命令失败: 设备未绑定温室, DeviceId={DeviceId}", deviceCode);
                    return;
                }

                var commandId = Guid.NewGuid().ToString();
                var commandPayload = new DeviceCommandPayload
                {
                    CommandId = commandId,
                    Action = action,
                    Payload = payload,
                    Timestamp = DateTime.Now
                };

                var json = JsonSerializer.Serialize(commandPayload);
                var topic = MqttTopicHelper.BuildCommandTopic(greenhouseId, deviceCode, action);

                var mqttProvider = ServiceProvider.GetRequiredService<IMqttClientProvider>();
                var client = await mqttProvider.GetClientAsync();

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(json)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .Build();

                await client.PublishAsync(message);
                _logger.LogInformation("发送命令: DeviceId={DeviceId}, Action={Action}, CommandId={CommandId}", deviceCode, action, commandId);

                var tcs = new TaskCompletionSource<string>();
                _pendingCommands[commandId] = tcs;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                cts.Token.Register(() =>
                {
                    _pendingCommands.TryRemove(commandId, out _);
                    tcs.TrySetResult(null);
                });

                var response = await tcs.Task;
                _pendingCommands.TryRemove(commandId, out _);

                if (response == null)
                {
                    _logger.LogWarning("命令超时: DeviceId={DeviceId}, Action={Action}, CommandId={CommandId}", deviceCode, action, commandId);
                }
                else
                {
                    _logger.LogInformation("命令响应: DeviceId={DeviceId}, Action={Action}, CommandId={CommandId}, Response={Response}", deviceCode, action, commandId, response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送命令失败: DeviceId={DeviceId}, Action={Action}", this.GetPrimaryKeyString(), action);
                throw;
            }
        }

        public Task<DeviceTwinInfo> GetDeviceTwinAsync()
        {
            try
            {
                var state = _deviceState.State;
                var desired = state.DesiredProperties ?? new Dictionary<string, string>();
                var reported = state.ReportedProperties ?? new Dictionary<string, string>();

                var diffs = new List<TwinPropertyDiff>();
                var allKeys = desired.Keys.Union(reported.Keys);

                foreach (var key in allKeys)
                {
                    var hasDesired = desired.TryGetValue(key, out var desiredValue);
                    var hasReported = reported.TryGetValue(key, out var reportedValue);

                    if (!hasDesired || !hasReported || desiredValue != reportedValue)
                    {
                        diffs.Add(new TwinPropertyDiff
                        {
                            Key = key,
                            DesiredValue = hasDesired ? desiredValue : null,
                            ReportedValue = hasReported ? reportedValue : null
                        });
                    }
                }

                return Task.FromResult(new DeviceTwinInfo
                {
                    DesiredProperties = new Dictionary<string, string>(desired),
                    ReportedProperties = new Dictionary<string, string>(reported),
                    Differences = diffs
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取设备孪生失败: DeviceId={DeviceId}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public async Task SetDesiredPropertyAsync(string key, string value)
        {
            try
            {
                var state = _deviceState.State;
                var deviceCode = this.GetPrimaryKeyString();
                var greenhouseId = state.GreenhouseId;

                state.DesiredProperties[key] = value;
                await _deviceState.WriteStateAsync();

                _logger.LogInformation("设置期望属性: DeviceId={DeviceId}, Key={Key}, Value={Value}", deviceCode, key, value);

                if (!string.IsNullOrEmpty(greenhouseId))
                {
                    try
                    {
                        var mqttProvider = ServiceProvider.GetRequiredService<IMqttClientProvider>();
                        var client = await mqttProvider.GetClientAsync();
                        var configTopic = MqttTopicHelper.BuildConfigTopic(greenhouseId, deviceCode, "threshold");

                        var configPayload = JsonSerializer.Serialize(new Dictionary<string, string> { { key, value } });
                        var message = new MqttApplicationMessageBuilder()
                            .WithTopic(configTopic)
                            .WithPayload(configPayload)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                            .WithRetainFlag(true)
                            .Build();

                        await client.PublishAsync(message);
                        _logger.LogInformation("发布配置到MQTT: DeviceId={DeviceId}, Key={Key}", deviceCode, key);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "发布配置到MQTT失败: DeviceId={DeviceId}, Key={Key}", deviceCode, key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置期望属性失败: DeviceId={DeviceId}, Key={Key}", this.GetPrimaryKeyString(), key);
                throw;
            }
        }

        public async Task UpdateReportedPropertyAsync(string key, string value)
        {
            try
            {
                var state = _deviceState.State;
                var deviceCode = this.GetPrimaryKeyString();

                state.ReportedProperties[key] = value;
                await _deviceState.WriteStateAsync();

                var hasDesired = state.DesiredProperties.TryGetValue(key, out var desiredValue);
                if (hasDesired && desiredValue != value)
                {
                    _logger.LogInformation("报告属性与期望不一致: DeviceId={DeviceId}, Key={Key}, Desired={DesiredValue}, Reported={ReportedValue}",
                        deviceCode, key, desiredValue, value);
                }
                else if (!hasDesired)
                {
                    _logger.LogInformation("报告属性无对应期望: DeviceId={DeviceId}, Key={Key}, Reported={ReportedValue}",
                        deviceCode, key, value);
                }
                else
                {
                    _logger.LogInformation("更新报告属性: DeviceId={DeviceId}, Key={Key}, Value={Value}", deviceCode, key, value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新报告属性失败: DeviceId={DeviceId}, Key={Key}", this.GetPrimaryKeyString(), key);
                throw;
            }
        }
        public Task CompleteCommandAsync(string commandId, string responsePayload)
        {
            if (!string.IsNullOrEmpty(commandId) && _pendingCommands.TryRemove(commandId, out var tcs))
            {
                tcs.TrySetResult(responsePayload);
                _logger.LogInformation("完成命令响应: DeviceId={DeviceId}, CommandId={CommandId}", this.GetPrimaryKeyString(), commandId);
            }
            return Task.CompletedTask;
        }
    }
}
