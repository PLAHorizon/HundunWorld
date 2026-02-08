using Arch.Core;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// 为Scene添加别名以避免命名冲突
using FlaxScene = FlaxEngine.Scene;

namespace ManagedHundunWorld.Network.Handlers
{
    /// <summary>
    /// 攻击响应消息处理器
    /// 处理服务端返回的攻击结果
    /// </summary>
    public class AttackResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.Attack };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<AttackMessage> AttackProcessed;
        public event Action<DamageMessage> DamageApplied;

        public AttackResponseHandler() : base(MessageType.Attack)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is AttackMessage attackMessage)
                {
                    FlaxEngine.Debug.Log($"收到攻击响应: {attackMessage.AttackerId} -> {attackMessage.TargetId}, 伤害: {attackMessage.Damage}");

                    // 触发事件通知订阅者
                    AttackProcessed?.Invoke(attackMessage);

                    // 在UI线程更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        // 可能需要更新UI或触发特效
                        ProcessAttackEffects(attackMessage);
                    });
                }
                else if (message.Body is DamageMessage damageMessage)
                {
                    FlaxEngine.Debug.Log($"收到伤害消息: {damageMessage.VictimId} 受到伤害 {damageMessage.Damage}");

                    // 触发事件通知订阅者
                    DamageApplied?.Invoke(damageMessage);

                    // 在UI线程更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        // 更新受击实体的状态
                        ApplyDamageToEntity(damageMessage);
                        
                        // 触发伤害数字显示
                        ShowDamageNumber(damageMessage);
                        
                        // 触发受击特效
                        PlayHitEffect(damageMessage);
                    });
                }
                else
                {
                    FlaxEngine.Debug.LogError($"收到无效的攻击响应消息，Body类型: {message.Body?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogException(ex);
                FlaxEngine.Debug.LogError($"处理攻击响应消息时发生异常: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 处理攻击效果
        /// </summary>
        private void ProcessAttackEffects(AttackMessage attackMessage)
        {
            // 播放攻击特效
            PlayAttackEffect(attackMessage);

            // 播放攻击音效
            PlayAttackSound(attackMessage);
        }

        /// <summary>
        /// 应用伤害到实体
        /// </summary>
        private void ApplyDamageToEntity(DamageMessage damageMessage)
        {
            try
            {
                // 在ECS系统中查找目标实体并应用伤害
                // 这里需要根据damageMessage.VictimId找到对应的实体
                // 由于我们无法直接访问World实例，可能需要通过单例或事件系统来处理
                
                // 临时实现：更新健康组件（如果存在的话）
                // 在实际实现中，这应该通过ECS系统来处理
                FlaxEngine.Debug.Log($"对实体 {damageMessage.VictimId} 应用伤害 {damageMessage.Damage}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"应用伤害到实体时发生异常: {ex.Message}");
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
                    damageNumberSystem.ShowDamageNumber(damageMessage.Damage, 
                        new Vector3(damageMessage.ImpactPosition.X, damageMessage.ImpactPosition.Y, damageMessage.ImpactPosition.Z), 
                        damageMessage.IsCritical);
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
        /// 播放攻击特效
        /// </summary>
        private void PlayAttackEffect(AttackMessage attackMessage)
        {
            try
            {
                // 通知特效系统播放攻击特效
                var effectManager = Game.Combat.Effects.SkillEffectManager.Instance;
                if (effectManager != null)
                {
                    // 使用现有字段替代缺失的字段
                    var startPosition = new Vector3(0, 0, 0); // 临时起始位置
                    var impactPosition = new Vector3(0, 0, 0); // 临时冲击位置
                    var skillId = attackMessage.AttackType; // 临时使用AttackType作为SkillId
                    
                    effectManager.PlayAttackEffect(startPosition, impactPosition, skillId);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放攻击特效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放攻击音效
        /// </summary>
        private void PlayAttackSound(AttackMessage attackMessage)
        {
            try
            {
                // 播放攻击音效
                // 可能需要根据技能ID选择不同的音效
                var soundPath = GetAttackSoundPath(attackMessage.SkillId);
                if (!string.IsNullOrEmpty(soundPath))
                {
                    // TODO: 实现正确的音频播放系统
                    // AudioListener.Play(soundPath);
                    FlaxEngine.Debug.Log($"[TODO] 播放攻击音效: {soundPath}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放攻击音效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取攻击音效路径
        /// </summary>
        private string GetAttackSoundPath(int skillId)
        {
            // 根据技能ID返回对应的音效路径
            // 这只是一个示例实现
            return $"/Game/Audio/Skills/Skill_{skillId}_Attack";
        }
    }
}