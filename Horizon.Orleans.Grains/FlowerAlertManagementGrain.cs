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
    /// <summary>
    /// 花卉预警管理Grain实现 - 负责用户预警规则与日志管理
    /// </summary>
    public class FlowerAlertManagementGrain : Grain, IFlowerAlertManagementGrain
    {
        private readonly ILogger<FlowerAlertManagementGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerAlertRule, long> _ruleDataContext;
        private readonly IDataContext<FlowerEntityContext, FlowerAlertLog, long> _logDataContext;

        public FlowerAlertManagementGrain(
            ILogger<FlowerAlertManagementGrain> logger,
            IDataContext<FlowerEntityContext, FlowerAlertRule, long> ruleDataContext,
            IDataContext<FlowerEntityContext, FlowerAlertLog, long> logDataContext)
        {
            _logger = logger;
            _ruleDataContext = ruleDataContext;
            _logDataContext = logDataContext;
        }

        public async Task<List<FlowerAlertRuleInfo>> GetAlertRulesAsync()
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var entities = await _ruleDataContext.QueryAsync(
                    r => r.UserId == userId && !r.IsDeleted,
                    r => new FlowerAlertRuleInfo
                    {
                        Id = r.Id,
                        UserId = r.UserId,
                        SpeciesId = r.SpeciesId,
                        MarketId = r.MarketId,
                        ConditionType = r.ConditionType,
                        ThresholdValue = r.ThresholdValue,
                        IsEnabled = r.IsEnabled,
                        LastTriggeredAt = r.LastTriggeredAt
                    });

                return entities.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户预警规则列表失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<FlowerAlertRuleInfo> CreateAlertRuleAsync(FlowerAlertRuleInfo rule)
        {
            try
            {
                var userId = this.GetPrimaryKey();

                var entity = new FlowerAlertRule
                {
                    UserId = userId,
                    SpeciesId = rule.SpeciesId,
                    MarketId = rule.MarketId,
                    ConditionType = rule.ConditionType,
                    ThresholdValue = rule.ThresholdValue,
                    IsEnabled = rule.IsEnabled,
                    IsDeleted = false
                };

                var result = await _ruleDataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("创建预警规则失败: 数据库保存返回null, UserId={UserId}", userId);
                    return null;
                }

                _logger.LogInformation("创建预警规则: UserId={UserId}, RuleId={RuleId}, SpeciesId={SpeciesId}, ConditionType={ConditionType}",
                    userId, result.Id, rule.SpeciesId, rule.ConditionType);

                return new FlowerAlertRuleInfo
                {
                    Id = result.Id,
                    UserId = result.UserId,
                    SpeciesId = result.SpeciesId,
                    MarketId = result.MarketId,
                    ConditionType = result.ConditionType,
                    ThresholdValue = result.ThresholdValue,
                    IsEnabled = result.IsEnabled,
                    LastTriggeredAt = result.LastTriggeredAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建预警规则失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<FlowerAlertRuleInfo> UpdateAlertRuleAsync(long ruleId, int conditionType, decimal thresholdValue, bool isEnabled)
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var entity = await _ruleDataContext.QueryFirstOrDefaultAsync(r => r.Id == ruleId && r.UserId == userId && !r.IsDeleted);

                if (entity == null)
                {
                    _logger.LogWarning("更新预警规则失败: 规则不存在或无权操作, RuleId={RuleId}, UserId={UserId}", ruleId, userId);
                    return null;
                }

                entity.ConditionType = conditionType;
                entity.ThresholdValue = thresholdValue;
                entity.IsEnabled = isEnabled;

                var updated = await _ruleDataContext.UpdateAsync(entity, entity.Id);
                if (!updated)
                {
                    _logger.LogError("更新预警规则失败: 数据库更新返回false, RuleId={RuleId}", ruleId);
                    return null;
                }

                _logger.LogInformation("更新预警规则: RuleId={RuleId}, UserId={UserId}, ConditionType={ConditionType}, Threshold={Threshold}, IsEnabled={IsEnabled}",
                    ruleId, userId, conditionType, thresholdValue, isEnabled);

                return new FlowerAlertRuleInfo
                {
                    Id = entity.Id,
                    UserId = entity.UserId,
                    SpeciesId = entity.SpeciesId,
                    MarketId = entity.MarketId,
                    ConditionType = entity.ConditionType,
                    ThresholdValue = entity.ThresholdValue,
                    IsEnabled = entity.IsEnabled,
                    LastTriggeredAt = entity.LastTriggeredAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新预警规则失败: RuleId={RuleId}, UserId={UserId}", ruleId, this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<bool> DeleteAlertRuleAsync(long ruleId)
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var entity = await _ruleDataContext.QueryFirstOrDefaultAsync(r => r.Id == ruleId && r.UserId == userId && !r.IsDeleted);

                if (entity == null)
                {
                    _logger.LogWarning("删除预警规则失败: 规则不存在或无权操作, RuleId={RuleId}, UserId={UserId}", ruleId, userId);
                    return false;
                }

                entity.IsDeleted = true;
                var result = await _ruleDataContext.UpdateAsync(entity, entity.Id);

                _logger.LogInformation("删除预警规则: RuleId={RuleId}, UserId={UserId}", ruleId, userId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除预警规则失败: RuleId={RuleId}, UserId={UserId}", ruleId, this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<List<FlowerAlertLogInfo>> GetAlertLogsAsync(int skip, int take)
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var entities = await _logDataContext.QueryAsync(
                    l => l.UserId == userId,
                    l => new FlowerAlertLogInfo
                    {
                        Id = l.Id,
                        RuleId = l.RuleId,
                        UserId = l.UserId,
                        SpeciesId = l.SpeciesId,
                        MarketId = l.MarketId,
                        AlertType = l.AlertType,
                        AlertMessage = l.AlertMessage ?? "",
                        TriggeredValue = l.TriggeredValue,
                        ThresholdValue = l.ThresholdValue,
                        IsRead = l.IsRead,
                        CreatedAt = l.CreatedAt
                    });

                return entities.OrderByDescending(l => l.CreatedAt).Skip(skip).Take(take).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户预警日志失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }
    }
}
