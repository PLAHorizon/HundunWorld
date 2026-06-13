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
    /// 花卉订阅Grain实现 - 负责用户订阅管理
    /// </summary>
    public class FlowerSubscriptionGrain : Grain, IFlowerSubscriptionGrain
    {
        private readonly ILogger<FlowerSubscriptionGrain> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerSubscription, long> _subscriptionContext;
        private readonly IDataContext<FlowerEntityContext, FlowerUser, long> _flowerUserContext;

        public FlowerSubscriptionGrain(
            ILogger<FlowerSubscriptionGrain> logger,
            IDataContext<FlowerEntityContext, FlowerSubscription, long> subscriptionContext,
            IDataContext<FlowerEntityContext, FlowerUser, long> flowerUserContext)
        {
            _logger = logger;
            _subscriptionContext = subscriptionContext;
            _flowerUserContext = flowerUserContext;
        }

        public async Task<List<FlowerSubscriptionInfo>> GetSubscriptionsAsync()
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var entities = await _subscriptionContext.QueryAsync(
                    s => s.UserId == userId && !s.IsDeleted,
                    s => new FlowerSubscriptionInfo
                    {
                        Id = s.Id,
                        UserId = s.UserId,
                        Level = s.Level,
                        StartDate = s.StartDate,
                        EndDate = s.EndDate,
                        AutoRenew = s.AutoRenew,
                        PaymentMethod = s.PaymentMethod ?? ""
                    });

                return entities.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户订阅列表失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<FlowerSubscriptionInfo> CreateSubscriptionAsync(FlowerSubscriptionInfo subscription)
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var now = DateTime.Now;

                var flowerUser = await _flowerUserContext.QueryFirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted);
                if (flowerUser == null)
                {
                    _logger.LogWarning("创建订阅失败: Flower_User 中未找到用户, UserId={UserId}", userId);
                    return null;
                }

                var entity = new FlowerSubscription
                {
                    UserId = flowerUser.UserId,
                    Level = subscription.Level,
                    StartDate = subscription.StartDate != default ? subscription.StartDate : now,
                    EndDate = subscription.EndDate != default ? subscription.EndDate : now.AddYears(1),
                    AutoRenew = subscription.AutoRenew,
                    PaymentMethod = subscription.PaymentMethod,
                    IsDeleted = false,
                    Passport = flowerUser.Passport,
                    CreateTime = now
                };

                var result = await _subscriptionContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("创建订阅失败: 数据库保存返回null, UserId={UserId}", flowerUser.UserId);
                    return null;
                }

                _logger.LogInformation("创建订阅: UserId={UserId}, Level={Level}, StartDate={StartDate}, EndDate={EndDate}",
                    flowerUser.UserId, subscription.Level, entity.StartDate, entity.EndDate);

                return new FlowerSubscriptionInfo
                {
                    Id = result.Id,
                    UserId = result.UserId,
                    Level = result.Level,
                    StartDate = result.StartDate,
                    EndDate = result.EndDate,
                    AutoRenew = result.AutoRenew,
                    PaymentMethod = result.PaymentMethod ?? ""
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建订阅失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<bool> CancelSubscriptionAsync(long subscriptionId)
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var entity = await _subscriptionContext.QueryFirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId && !s.IsDeleted);

                if (entity == null)
                {
                    _logger.LogWarning("取消订阅失败: 订阅不存在或无权操作, SubscriptionId={SubscriptionId}, UserId={UserId}", subscriptionId, userId);
                    return false;
                }

                entity.IsDeleted = true;
                var result = await _subscriptionContext.UpdateAsync(entity, entity.Id);

                _logger.LogInformation("取消订阅: SubscriptionId={SubscriptionId}, UserId={UserId}", subscriptionId, userId);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消订阅失败: SubscriptionId={SubscriptionId}, UserId={UserId}", subscriptionId, this.GetPrimaryKey());
                throw;
            }
        }

        public async Task<FlowerSubscriptionInfo?> GetActiveSubscriptionAsync()
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var now = DateTime.Now;
                var entities = await _subscriptionContext.QueryAsync(
                    s => s.UserId == userId && !s.IsDeleted && s.EndDate > now,
                    s => new FlowerSubscriptionInfo
                    {
                        Id = s.Id,
                        UserId = s.UserId,
                        Level = s.Level,
                        StartDate = s.StartDate,
                        EndDate = s.EndDate,
                        AutoRenew = s.AutoRenew,
                        PaymentMethod = s.PaymentMethod ?? ""
                    });

                return entities.OrderByDescending(s => s.EndDate).FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取活跃订阅失败: UserId={UserId}", this.GetPrimaryKey());
                return null;
            }
        }

        public async Task<FlowerSubscriptionInfo> UpgradeSubscriptionAsync(int newLevel, string paymentMethod)
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var now = DateTime.Now;

                var currentActive = await GetActiveSubscriptionAsync();
                if (currentActive != null)
                {
                    currentActive.AutoRenew = false;
                    var entity = await _subscriptionContext.QueryFirstOrDefaultAsync(s => s.Id == currentActive.Id && !s.IsDeleted);
                    if (entity != null)
                    {
                        entity.AutoRenew = false;
                        await _subscriptionContext.UpdateAsync(entity, entity.Id);
                    }
                }

                var newSubscription = new FlowerSubscriptionInfo
                {
                    UserId = userId,
                    Level = newLevel,
                    StartDate = now,
                    EndDate = now.AddYears(1),
                    AutoRenew = true,
                    PaymentMethod = paymentMethod ?? ""
                };

                return await CreateSubscriptionAsync(newSubscription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "升级订阅失败: UserId={UserId}, NewLevel={NewLevel}", this.GetPrimaryKey(), newLevel);
                throw;
            }
        }

        public async Task<bool> UpdateAutoRenewAsync(bool autoRenew)
        {
            try
            {
                var userId = this.GetPrimaryKey();
                var active = await GetActiveSubscriptionAsync();
                if (active == null)
                {
                    _logger.LogWarning("更新自动续费失败: 无活跃订阅, UserId={UserId}", userId);
                    return false;
                }

                var entity = await _subscriptionContext.QueryFirstOrDefaultAsync(s => s.Id == active.Id && !s.IsDeleted);
                if (entity == null)
                {
                    _logger.LogWarning("更新自动续费失败: 订阅记录不存在, SubscriptionId={SubscriptionId}", active.Id);
                    return false;
                }

                entity.AutoRenew = autoRenew;
                var result = await _subscriptionContext.UpdateAsync(entity, entity.Id);

                _logger.LogInformation("更新自动续费: UserId={UserId}, SubscriptionId={SubscriptionId}, AutoRenew={AutoRenew}", userId, active.Id, autoRenew);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新自动续费失败: UserId={UserId}", this.GetPrimaryKey());
                throw;
            }
        }
    }
}
