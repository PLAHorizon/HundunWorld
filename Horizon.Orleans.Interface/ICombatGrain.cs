using Orleans;
using System;
using System.Threading.Tasks;
using Horizon.Game.Message;
using System.Collections.Generic;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 战斗Grain接口 - 负责处理战斗相关逻辑
    /// </summary>
    public interface ICombatGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 处理攻击请求
        /// </summary>
        /// <param name="request">攻击请求</param>
        /// <returns>伤害响应</returns>
        Task<DamageMessage> ProcessAttackAsync(AttackMessage request);

        /// <summary>
        /// 处理技能施放请求
        /// </summary>
        /// <param name="request">技能施放请求</param>
        /// <returns>技能施放响应</returns>
        Task<SkillCastMessage> ProcessSkillCastAsync(SkillCastMessage request);

        /// <summary>
        /// 处理伤害
        /// </summary>
        /// <param name="request">伤害消息</param>
        /// <returns>处理结果</returns>
        Task<DamageMessage> TakeDamageAsync(DamageMessage request);

        /// <summary>
        /// 处理死亡
        /// </summary>
        /// <param name="request">死亡消息</param>
        /// <returns>死亡响应</returns>
        Task<DeathMessage> ProcessDeathAsync(DeathMessage request);

        /// <summary>
        /// 处理复活
        /// </summary>
        /// <param name="request">复活请求</param>
        /// <returns>复活响应</returns>
        Task<ResurrectMessage> ProcessResurrectAsync(ResurrectMessage request);

        /// <summary>
        /// 进入战斗状态
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>是否成功</returns>
        Task<bool> EnterCombatAsync(ulong characterId);

        /// <summary>
        /// 退出战斗状态
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>是否成功</returns>
        Task<bool> ExitCombatAsync(ulong characterId);

        /// <summary>
        /// 检查是否在战斗中
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>是否在战斗中</returns>
        Task<bool> IsInCombatAsync(ulong characterId);

        /// <summary>
        /// 应用战斗效果
        /// </summary>
        /// <param name="request">效果应用请求</param>
        /// <returns>效果响应</returns>
        Task<EffectMessage> ApplyEffectAsync(EffectMessage request);

        /// <summary>
        /// 计算五行相克伤害
        /// </summary>
        /// <param name="attackerId">攻击者ID</param>
        /// <param name="defenderId">防御者ID</param>
        /// <param name="baseDamage">基础伤害</param>
        /// <param name="elementType">元素类型</param>
        /// <returns>最终伤害</returns>
        Task<float> CalculateWuxingDamageAsync(ulong attackerId, ulong defenderId, float baseDamage, int elementType);
    }
}