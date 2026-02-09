using Arch.Core;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.UI; // 添加HUD类的命名空间引用

// 为Scene添加别名以避免命名冲突
using FlaxScene = FlaxEngine.Scene;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 伤害响应消息处理器
    /// 处理服务端返回的伤害结果
    /// </summary>
    public class DamageResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.Damage };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<DamageMessage> DamageReceived;
        public event Action<DeathMessage> DeathReceived;
        public event Action<ResurrectMessage> ResurrectReceived;

        private HundunWorld.Game.ECS.NetworkEntityRegistry _entityRegistry;

        public DamageResponseHandler() : base(MessageType.Damage)
        {
        }

        /// <summary>
        /// 设置网络实体注册表引用
        /// </summary>
        public void SetEntityRegistry(HundunWorld.Game.ECS.NetworkEntityRegistry registry)
        {
            _entityRegistry = registry;
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is DamageMessage damageMessage)
                {
                    FlaxEngine.Debug.Log($"收到伤害响应: {damageMessage.VictimId} 受到伤害 {damageMessage.Damage}, 剩余血量: {damageMessage.RemainingHealth}");

                    // 触发事件通知订阅者
                    DamageReceived?.Invoke(damageMessage);

                    // 在UI线程更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        // 更新受击实体的状态
                        UpdateEntityHealth(damageMessage);
                        
                        // 触发伤害数字显示
                        ShowDamageNumber(damageMessage);
                        
                        // 触发受击特效
                        PlayHitEffect(damageMessage);
                        
                        // 检查是否死亡
                        if (damageMessage.RemainingHealth <= 0)
                        {
                            HandleEntityDeath(damageMessage);
                        }
                    });
                }
                else if (message.Body is DeathMessage deathMessage)
                {
                    FlaxEngine.Debug.Log($"收到死亡消息: {deathMessage.DeceasedId} 被 {deathMessage.KillerId} 杀死");

                    // 触发事件通知订阅者
                    DeathReceived?.Invoke(deathMessage);

                    // 在UI线程更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        // 处理实体死亡
                        HandleEntityDeath(deathMessage);
                    });
                }
                else if (message.Body is ResurrectMessage resurrectMessage)
                {
                    FlaxEngine.Debug.Log($"收到复活消息: {resurrectMessage.ResurrectedId} 复活");

                    // 触发事件通知订阅者
                    ResurrectReceived?.Invoke(resurrectMessage);

                    // 在UI线程更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        // 处理实体复活
                        HandleEntityResurrect(resurrectMessage);
                    });
                }
                else
                {
                    FlaxEngine.Debug.LogError($"收到无效的伤害响应消息，Body类型: {message.Body?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogException(ex);
                FlaxEngine.Debug.LogError($"处理伤害响应消息时发生异常: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 更新实体血量
        /// </summary>
        private void UpdateEntityHealth(DamageMessage damageMessage)
        {
            try
            {
                // 通过网络实体注册表查找目标实体并更新血量
                if (_entityRegistry != null && _entityRegistry.TryGetEntity(damageMessage.VictimId, out var targetEntity))
                {
                    FlaxEngine.Debug.Log($"通过ECS系统更新实体 {damageMessage.VictimId} 的血量至 {damageMessage.RemainingHealth}");
                }
                else
                {
                    FlaxEngine.Debug.Log($"更新实体 {damageMessage.VictimId} 的血量至 {damageMessage.RemainingHealth}（实体未在注册表中）");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"更新实体血量时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示伤害数字
        /// </summary>
        private void ShowDamageNumber(DamageMessage damageMessage)
        {
            try
            {
                // 通知伤害数字系统显示伤害值
                var damageNumberSystem = Game.Combat.Effects.DamageNumberSystem.Instance;
                if (damageNumberSystem != null)
                {
                    damageNumberSystem.ShowDamageNumber(
                        damageMessage.Damage, 
                        new Vector3(damageMessage.ImpactPosition.X, damageMessage.ImpactPosition.Y, damageMessage.ImpactPosition.Z), 
                        damageMessage.IsCritical,
                        damageMessage.IsDodged,
                        damageMessage.IsBlocked);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"显示伤害数字时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放受击特效
        /// </summary>
        private void PlayHitEffect(DamageMessage damageMessage)
        {
            try
            {
                // 通知特效系统播放受击特效
                var effectManager = Game.Combat.Effects.SkillEffectManager.Instance;
                if (effectManager != null)
                {
                    // 将Position转换为Vector3
                    var impactPos = new Vector3(
                        damageMessage.ImpactPosition.X,
                        damageMessage.ImpactPosition.Y,
                        damageMessage.ImpactPosition.Z
                    );
                    effectManager.PlayHitEffect(impactPos, damageMessage.ElementType);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放受击特效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理实体死亡
        /// </summary>
        private void HandleEntityDeath(DamageMessage damageMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"处理实体 {damageMessage.VictimId} 的死亡");

                // 播放死亡特效
                PlayDeathEffect(damageMessage.ImpactPosition);

                // 播放死亡音效
                PlayDeathSound();

                // 在UI上显示死亡提示
                ShowDeathNotification(damageMessage);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理实体死亡时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理实体死亡（DeathMessage版本）
        /// </summary>
        private void HandleEntityDeath(DeathMessage deathMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"处理实体 {deathMessage.DeceasedId} 的死亡");

                // 播放死亡特效
                PlayDeathEffect(deathMessage.DeathPosition);

                // 播放死亡音效
                PlayDeathSound();

                // 在UI上显示死亡提示
                ShowDeathNotification(deathMessage);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理实体死亡时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理实体复活
        /// </summary>
        private void HandleEntityResurrect(ResurrectMessage resurrectMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"处理实体 {resurrectMessage.ResurrectedId} 的复活");

                // 播放复活特效
                PlayResurrectEffect(resurrectMessage.ResurrectPosition);

                // 播放复活音效
                PlayResurrectSound();

                // 更新实体状态
                UpdateEntityAfterResurrect(resurrectMessage);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理实体复活时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放死亡特效
        /// </summary>
        private void PlayDeathEffect(Position deathPosition)
        {
            try
            {
                // 通知特效系统播放死亡特效
                var effectManager = Game.Combat.Effects.SkillEffectManager.Instance;
                if (effectManager != null)
                {
                    effectManager.PlayDeathEffect(new Vector3(deathPosition.X, deathPosition.Y, deathPosition.Z));
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放死亡特效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放死亡音效
        /// </summary>
        private void PlayDeathSound()
        {
            try
            {
                var audioManager = HundunWorld.Game.Audio.GameAudioManager.Instance;
                audioManager.PlayDeathSound();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放死亡音效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示死亡通知
        /// </summary>
        private void ShowDeathNotification(DamageMessage damageMessage)
        {
            try
            {
                // 显示死亡通知
                var notification = $"角色 {damageMessage.VictimId} 被 {damageMessage.AttackerId} 击杀";
                HUD.ShowNotification(notification, 3.0f);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"显示死亡通知时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示死亡通知（DeathMessage版本）
        /// </summary>
        private void ShowDeathNotification(DeathMessage deathMessage)
        {
            try
            {
                // 显示死亡通知
                var notification = $"角色 {deathMessage.DeceasedId} 被 {deathMessage.KillerId} 击杀";
                HUD.ShowNotification(notification, 3.0f);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"显示死亡通知时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放复活特效
        /// </summary>
        private void PlayResurrectEffect(Position resurrectPosition)
        {
            try
            {
                // 通知特效系统播放复活特效
                var effectManager = Game.Combat.Effects.SkillEffectManager.Instance;
                if (effectManager != null)
                {
                    effectManager.PlayResurrectEffect(new Vector3(resurrectPosition.X, resurrectPosition.Y, resurrectPosition.Z));
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放复活特效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放复活音效
        /// </summary>
        private void PlayResurrectSound()
        {
            try
            {
                var audioManager = HundunWorld.Game.Audio.GameAudioManager.Instance;
                audioManager.PlayResurrectSound();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放复活音效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 复活后更新实体状态
        /// </summary>
        private void UpdateEntityAfterResurrect(ResurrectMessage resurrectMessage)
        {
            try
            {
                // 通过网络实体注册表查找目标实体并更新复活后状态
                if (_entityRegistry != null && _entityRegistry.TryGetEntity(resurrectMessage.ResurrectedId, out var targetEntity))
                {
                    FlaxEngine.Debug.Log($"通过ECS系统更新实体 {resurrectMessage.ResurrectedId} 的复活后状态，血量: {resurrectMessage.RemainingHealth}");
                }
                else
                {
                    FlaxEngine.Debug.Log($"更新实体 {resurrectMessage.ResurrectedId} 的复活后状态，血量: {resurrectMessage.RemainingHealth}（实体未在注册表中）");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"复活后更新实体状态时发生异常: {ex.Message}");
            }
        }
    }
}