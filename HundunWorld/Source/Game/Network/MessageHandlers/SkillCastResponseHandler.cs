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
    /// 技能施放响应消息处理器
    /// 处理服务端返回的技能施放结果
    /// </summary>
    public class SkillCastResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes => new List<MessageType> { MessageType.SkillCast };

        public override ServiceType ServiceType => ServiceType.Game;

        public event Action<SkillCastMessage> SkillCastProcessed;
        public event Action<EffectMessage> EffectApplied;

        public SkillCastResponseHandler() : base(MessageType.SkillCast)
        {
        }

        public override async Task HandleAsync(HorizonMessagePacket message)
        {
            try
            {
                if (message.Body is SkillCastMessage skillCastMessage)
                {
                    FlaxEngine.Debug.Log($"收到技能施放响应: {skillCastMessage.CasterId} 施放技能 {skillCastMessage.SkillId}, 成功: {skillCastMessage.Success}");

                    // 触发事件通知订阅者
                    SkillCastProcessed?.Invoke(skillCastMessage);

                    // 在UI线程更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        if (skillCastMessage.Success)
                        {
                            // 播放技能特效
                            PlaySkillEffect(skillCastMessage);
                            
                            // 播放技能音效
                            PlaySkillSound(skillCastMessage);
                            
                            // 更新施法者状态
                            UpdateCasterState(skillCastMessage);
                        }
                        else
                        {
                            // 显示施法失败提示
                            ShowSkillFailureMessage(skillCastMessage);
                        }
                    });
                }
                else if (message.Body is EffectMessage effectMessage)
                {
                    FlaxEngine.Debug.Log($"收到效果应用消息: {effectMessage.EffectId} 应用到 {effectMessage.TargetId}");

                    // 触发事件通知订阅者
                    EffectApplied?.Invoke(effectMessage);

                    // 在UI线程更新
                    FlaxEngine.Scripting.InvokeOnUpdate(() =>
                    {
                        // 应用效果到目标实体
                        ApplyEffectToEntity(effectMessage);
                        
                        // 播放效果特效
                        PlayEffectVisual(effectMessage);
                        
                        // 显示效果UI
                        ShowEffectUI(effectMessage);
                    });
                }
                else
                {
                    FlaxEngine.Debug.LogError($"收到无效的技能施放响应消息，Body类型: {message.Body?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogException(ex);
                FlaxEngine.Debug.LogError($"处理技能施放响应消息时发生异常: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 播放技能特效
        /// </summary>
        private void PlaySkillEffect(SkillCastMessage skillCastMessage)
        {
            try
            {
                var effectManager = Game.Combat.Effects.SkillEffectManager.Instance;
                if (effectManager != null)
                {
                    var startPos = new Vector3(
                        skillCastMessage.StartPosition.X,
                        skillCastMessage.StartPosition.Y,
                        skillCastMessage.StartPosition.Z
                    );
                    
                    var targetPos = new Vector3(
                        skillCastMessage.TargetPosition.X,
                        skillCastMessage.TargetPosition.Y,
                        skillCastMessage.TargetPosition.Z
                    );

                    // 播放技能发射特效
                    effectManager.PlaySkillLaunchEffect(startPos, skillCastMessage.SkillId);

                    // 如果是范围技能，播放范围特效
                    if (skillCastMessage.IsAreaSkill)
                    {
                        effectManager.PlayAreaSkillEffect(targetPos, skillCastMessage.SkillId, skillCastMessage.Range);
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放技能特效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放技能音效
        /// </summary>
        private void PlaySkillSound(SkillCastMessage skillCastMessage)
        {
            try
            {
                // 根据技能ID获取对应的音效
                var soundPath = GetSkillSoundPath(skillCastMessage.SkillId);
                if (!string.IsNullOrEmpty(soundPath))
                {
                    // TODO: 实现正确的音频播放系统
                    // AudioListener.Play(soundPath);
                    FlaxEngine.Debug.Log($"[TODO] 播放技能音效: {soundPath}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放技能音效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新施法者状态
        /// </summary>
        private void UpdateCasterState(SkillCastMessage skillCastMessage)
        {
            try
            {
                // 更新施法者的能量/法力值
                // 在实际实现中，这可能需要通过ECS系统或角色管理器来处理
                FlaxEngine.Debug.Log($"更新施法者 {skillCastMessage.CasterId} 的状态，消耗能量: {skillCastMessage.EnergyCost}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"更新施法者状态时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示施法失败消息
        /// </summary>
        private void ShowSkillFailureMessage(SkillCastMessage skillCastMessage)
        {
            try
            {
                var errorMessage = skillCastMessage.Message ?? "技能施放失败";
                HUD.ShowNotification($"技能失败: {errorMessage}", 2.0f, Color.Red);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"显示技能失败消息时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用效果到实体
        /// </summary>
        private void ApplyEffectToEntity(EffectMessage effectMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"对实体 {effectMessage.TargetId} 应用效果 {effectMessage.EffectId}");

                // 根据效果类型更新目标实体的状态
                switch ((EffectType)effectMessage.EffectType)
                {
                    case EffectType.Buff:
                        ApplyBuffToEntity(effectMessage);
                        break;
                    case EffectType.Debuff:
                        ApplyDebuffToEntity(effectMessage);
                        break;
                    case EffectType.DamageOverTime:
                        ApplyDoTToEntity(effectMessage);
                        break;
                    case EffectType.HealOverTime:
                        ApplyHoTToEntity(effectMessage);
                        break;
                    case EffectType.Control:
                        ApplyControlToEntity(effectMessage);
                        break;
                    default:
                        FlaxEngine.Debug.LogWarning($"未知的效果类型: {effectMessage.EffectType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"应用效果到实体时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用增益效果到实体
        /// </summary>
        private void ApplyBuffToEntity(EffectMessage effectMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"对实体 {effectMessage.TargetId} 应用增益效果 {effectMessage.EffectId}");
                
                // 在实际实现中，这将更新实体的相关属性
                // 例如：增加攻击力、防御力、移动速度等
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"应用增益效果到实体时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用减益效果到实体
        /// </summary>
        private void ApplyDebuffToEntity(EffectMessage effectMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"对实体 {effectMessage.TargetId} 应用减益效果 {effectMessage.EffectId}");
                
                // 在实际实现中，这将更新实体的相关属性
                // 例如：减少攻击力、防御力、移动速度等
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"应用减益效果到实体时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用持续伤害效果到实体
        /// </summary>
        private void ApplyDoTToEntity(EffectMessage effectMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"对实体 {effectMessage.TargetId} 应用持续伤害效果 {effectMessage.EffectId}");
                
                // 在实际实现中，这将定期对实体造成伤害
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"应用持续伤害效果到实体时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用持续治疗效果到实体
        /// </summary>
        private void ApplyHoTToEntity(EffectMessage effectMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"对实体 {effectMessage.TargetId} 应用持续治疗效果 {effectMessage.EffectId}");
                
                // 在实际实现中，这将定期对实体进行治疗
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"应用持续治疗效果到实体时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用控制效果到实体
        /// </summary>
        private void ApplyControlToEntity(EffectMessage effectMessage)
        {
            try
            {
                FlaxEngine.Debug.Log($"对实体 {effectMessage.TargetId} 应用控制效果 {effectMessage.EffectId}");
                
                // 在实际实现中，这将控制实体的行为
                // 例如：眩晕、沉默、减速等
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"应用控制效果到实体时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放效果视觉特效
        /// </summary>
        private void PlayEffectVisual(EffectMessage effectMessage)
        {
            try
            {
                var effectManager = Game.Combat.Effects.SkillEffectManager.Instance;
                if (effectManager != null)
                {
                    // 由于EffectMessage没有TargetPosition字段，使用默认位置或从TargetId获取位置
                    // TODO: 从实体系统获取目标实体的世界位置
                    var targetPos = Vector3.Zero; // 临时使用原点
                    
                    effectManager.PlayEffectVisual(
                        targetPos,
                        effectMessage.EffectId,
                        (Game.Combat.Effects.SkillEffectManager.EffectType)effectMessage.EffectType
                    );
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"播放效果视觉特效时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示效果UI
        /// </summary>
        private void ShowEffectUI(EffectMessage effectMessage)
        {
            try
            {
                // 显示效果的UI提示，如增益/减益图标
                var uiManager = HundunWorld.Game.UI.MainUIManager.Instance;
                if (uiManager != null)
                {
                    uiManager.ShowEffectIcon(
                        effectMessage.TargetId,
                        effectMessage.EffectId,
                        effectMessage.EffectName,
                        effectMessage.RemainingDuration
                    );
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"显示效果UI时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取技能音效路径
        /// </summary>
        private string GetSkillSoundPath(int skillId)
        {
            // 根据技能ID返回对应的音效路径
            // 这是一个示例实现
            return $"/Game/Audio/Skills/Skill_{skillId}_Cast";
        }
    }
}