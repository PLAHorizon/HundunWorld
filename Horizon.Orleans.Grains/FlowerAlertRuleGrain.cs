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
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerAlertRuleGrain : Grain, IPriceAlertRuleGrain
    {
        private readonly ILogger<FlowerAlertRuleGrain> _logger;
        private readonly IPersistentState<AlertRuleState> _ruleState;
        private readonly IDataContext<FlowerEntityContext, FlowerAlertLog, long> _dataContext;

        public FlowerAlertRuleGrain(
            ILogger<FlowerAlertRuleGrain> logger,
            [PersistentState("alertrule", "FlowerStore")] IPersistentState<AlertRuleState> ruleState,
            IDataContext<FlowerEntityContext, FlowerAlertLog, long> dataContext)
        {
            _logger = logger;
            _ruleState = ruleState;
            _dataContext = dataContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FlowerAlertRuleGrain {GrainKey} activating.", this.GetPrimaryKeyString());

            var grainKey = this.GetPrimaryKeyString();
            if (long.TryParse(grainKey, out var ruleId) && _ruleState.State.RuleId == 0)
                _ruleState.State.RuleId = ruleId;
            if (_ruleState.State.IsEnabled == false && _ruleState.State.ThresholdValue == 0)
                _ruleState.State.IsEnabled = true;

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<bool> EvaluateAsync(SensorReading reading)
        {
            try
            {
                if (reading == null)
                {
                    _logger.LogWarning("评估预警无效: reading is null");
                    return false;
                }

                var state = _ruleState.State;

                if (!state.IsEnabled)
                {
                    _logger.LogDebug("规则已禁用，跳过评估: RuleId={RuleId}", state.RuleId);
                    return false;
                }

                var metricValue = GetMetricValue(reading, state.ConditionType);
                if (metricValue == null)
                    return false;

                bool triggered = state.ConditionType switch
                {
                    AlertConditionType.PriceAbove => metricValue.Value > state.ThresholdValue,
                    AlertConditionType.PriceBelow => metricValue.Value < state.ThresholdValue,
                    AlertConditionType.PriceChangeAbove => state.PreviousPrice > 0 && (metricValue.Value - state.PreviousPrice) / state.PreviousPrice > state.ThresholdValue,
                    AlertConditionType.PriceChangeBelow => state.PreviousPrice > 0 && (state.PreviousPrice - metricValue.Value) / state.PreviousPrice > state.ThresholdValue,
                    _ => false
                };

                if (!triggered)
                    return false;

                var now = DateTime.Now;

                var alertLog = new FlowerAlertLog
                {
                    RuleId = state.RuleId,
                    UserId = state.UserId,
                    SpeciesId = state.SpeciesId,
                    MarketId = 0,
                    AlertType = (int)state.ConditionType,
                    AlertMessage = BuildAlertMessage(metricValue.Value, state),
                    TriggeredValue = metricValue.Value,
                    ThresholdValue = state.ThresholdValue,
                    IsRead = false,
                    CreatedAt = now
                };

                var result = await _dataContext.AddAsync(alertLog);
                if (result == null)
                {
                    _logger.LogError("保存预警日志失败: 数据库保存返回null, RuleId={RuleId}", state.RuleId);
                    return false;
                }

                state.PreviousPrice = metricValue.Value;
                state.LastTriggeredAt = now;
                await _ruleState.WriteStateAsync();

                var dataPoolGrain = GrainFactory.GetGrain<IFlowerDataPoolGrain>(0);
                var alertMessage = new AlertMessage
                {
                    RuleId = state.RuleId,
                    UserId = state.UserId,
                    SpeciesId = state.SpeciesId,
                    MarketId = 0,
                    AlertType = state.ConditionType,
                    Message = alertLog.AlertMessage,
                    TriggeredValue = metricValue.Value,
                    ThresholdValue = state.ThresholdValue,
                    CreatedAt = now
                };

                var dataPoolEntry = new DataPoolEntry
                {
                    DataType = DataPoolDataType.AlertEvent,
                    DataSource = 0,
                    RawPayload = Convert.ToBase64String(MemoryPackSerializer.Serialize(alertMessage)),
                    Timestamp = now,
                    RelatedEntityId = result.Id.ToString(),
                    ModelVersion = "",
                    Confidence = null
                };
                await dataPoolGrain.WriteAsync(dataPoolEntry);

                if (state.UserId != Guid.Empty)
                {
                    var notificationGrain = GrainFactory.GetGrain<INotificationGrain>(state.UserId);
                    await notificationGrain.PushAlertAsync(alertMessage);
                }

                _logger.LogInformation("预警触发: RuleId={RuleId}, ConditionType={ConditionType}, TriggeredValue={TriggeredValue}",
                    state.RuleId, state.ConditionType, metricValue.Value);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "评估预警失败: GrainKey={GrainKey}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public Task<AlertRuleState> GetRuleStateAsync()
        {
            try
            {
                return Task.FromResult(_ruleState.State);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取规则状态失败: GrainKey={GrainKey}", this.GetPrimaryKeyString());
                throw;
            }
        }

        public async Task UpdateRuleAsync(AlertConditionType conditionType, decimal threshold, bool isEnabled)
        {
            try
            {
                var state = _ruleState.State;

                state.ConditionType = conditionType;
                state.ThresholdValue = threshold;
                state.IsEnabled = isEnabled;

                await _ruleState.WriteStateAsync();

                _logger.LogInformation("更新规则: RuleId={RuleId}, ConditionType={ConditionType}, Threshold={Threshold}, IsEnabled={IsEnabled}",
                    state.RuleId, conditionType, threshold, isEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新规则失败: GrainKey={GrainKey}", this.GetPrimaryKeyString());
                throw;
            }
        }

        private decimal? GetMetricValue(SensorReading reading, AlertConditionType conditionType)
        {
            return conditionType switch
            {
                AlertConditionType.PriceAbove => (decimal)reading.Temperature,
                AlertConditionType.PriceBelow => (decimal)reading.Temperature,
                AlertConditionType.PriceChangeAbove => (decimal)reading.Temperature,
                AlertConditionType.PriceChangeBelow => (decimal)reading.Temperature,
                _ => null
            };
        }

        private string BuildAlertMessage(decimal value, AlertRuleState state)
        {
            return $"设备预警触发: 条件类型={state.ConditionType}, 当前值={value}, 阈值={state.ThresholdValue}";
        }

        public async Task CreateForecastAlertAsync(string speciesCode, string suggestionType, decimal priceChangePercent)
        {
            try
            {
                var state = _ruleState.State;
                var now = DateTime.Now;

                var conditionType = suggestionType switch
                {
                    "ExpandPlanting" => AlertConditionType.PriceChangeAbove,
                    "EarlyHarvest" => AlertConditionType.PriceChangeBelow,
                    "ReducePlanting" => AlertConditionType.PriceChangeBelow,
                    _ => AlertConditionType.PriceChangeAbove
                };

                state.ConditionType = conditionType;
                state.ThresholdValue = 0.1m;
                state.IsEnabled = true;
                await _ruleState.WriteStateAsync();

                var alertMessage = suggestionType switch
                {
                    "ExpandPlanting" => $"【种植建议】品种{speciesCode}预测价格上涨{priceChangePercent:F1}%，建议扩大种植规模",
                    "EarlyHarvest" => $"【种植建议】品种{speciesCode}预测价格下跌{Math.Abs(priceChangePercent):F1}%，建议提前采收",
                    "ReducePlanting" => $"【种植建议】品种{speciesCode}预测价格下跌{Math.Abs(priceChangePercent):F1}%，建议减少种植",
                    _ => $"【种植建议】品种{speciesCode}价格变化{priceChangePercent:F1}%，维持当前计划"
                };

                var alertLog = new FlowerAlertLog
                {
                    RuleId = state.RuleId,
                    UserId = state.UserId,
                    SpeciesId = state.SpeciesId,
                    MarketId = 0,
                    AlertType = (int)conditionType,
                    AlertMessage = alertMessage,
                    TriggeredValue = priceChangePercent,
                    ThresholdValue = 0.1m,
                    IsRead = false,
                    CreatedAt = now
                };

                var result = await _dataContext.AddAsync(alertLog);
                if (result == null)
                {
                    _logger.LogError("保存预测预警日志失败: SpeciesCode={SpeciesCode}", speciesCode);
                    return;
                }

                state.LastTriggeredAt = now;
                await _ruleState.WriteStateAsync();

                var dataPoolGrain = GrainFactory.GetGrain<IFlowerDataPoolGrain>(0);
                var alert = new AlertMessage
                {
                    RuleId = state.RuleId,
                    UserId = state.UserId,
                    SpeciesId = state.SpeciesId,
                    MarketId = 0,
                    AlertType = conditionType,
                    Message = alertMessage,
                    TriggeredValue = priceChangePercent,
                    ThresholdValue = 0.1m,
                    CreatedAt = now
                };

                var dataPoolEntry = new DataPoolEntry
                {
                    DataType = DataPoolDataType.AlertEvent,
                    DataSource = 0,
                    RawPayload = Convert.ToBase64String(MemoryPackSerializer.Serialize(alert)),
                    Timestamp = now,
                    RelatedEntityId = result.Id.ToString(),
                    ModelVersion = "",
                    Confidence = null
                };
                await dataPoolGrain.WriteAsync(dataPoolEntry);

                if (state.UserId != Guid.Empty)
                {
                    var notificationGrain = GrainFactory.GetGrain<INotificationGrain>(state.UserId);
                    await notificationGrain.PushAlertAsync(alert);
                }

                _logger.LogInformation("创建预测预警: SpeciesCode={SpeciesCode}, SuggestionType={SuggestionType}, PriceChangePercent={PriceChangePercent}%",
                    speciesCode, suggestionType, priceChangePercent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建预测预警失败: SpeciesCode={SpeciesCode}", speciesCode);
                throw;
            }
        }
    }
}
