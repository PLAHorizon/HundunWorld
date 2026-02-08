using AutoMapper;
using Horizon.Core.Abstract;
using Horizon.Core.Helper;
using Horizon.Entities;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryPack;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 战斗状态
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CombatState
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public Dictionary<ulong, CombatInfo> CombatParticipants { get; set; } = new Dictionary<ulong, CombatInfo>();

        [MemoryPackOrder(1)]
        [Id(1)]
        public Dictionary<ulong, EffectInfo> ActiveEffects { get; set; } = new Dictionary<ulong, EffectInfo>();
    }

    /// <summary>
    /// 战斗信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class CombatInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public ulong CharacterId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsInCombat { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public DateTime LastActionTime { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public float Health { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public float MaxHealth { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public float AttackPower { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public float Defense { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public int WuxingElement { get; set; } // 0=无, 1=金, 2=木, 3=水, 4=火, 5=土

        [MemoryPackOrder(9)]
        [Id(9)]
        public float Energy { get; set; }

        [MemoryPackOrder(10)]
        [Id(10)]
        public float MaxEnergy { get; set; }
    }

    /// <summary>
    /// 效果信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class EffectInfo
    {
        [MemoryPackOrder(0)]
        [Id(0)]
        public int EffectId { get; set; }

        [MemoryPackOrder(1)]
        [Id(1)]
        public string EffectName { get; set; }

        [MemoryPackOrder(2)]
        [Id(2)]
        public ulong TargetId { get; set; }

        [MemoryPackOrder(3)]
        [Id(3)]
        public ulong SourceId { get; set; }

        [MemoryPackOrder(4)]
        [Id(4)]
        public float Duration { get; set; }

        [MemoryPackOrder(5)]
        [Id(5)]
        public float RemainingDuration { get; set; }

        [MemoryPackOrder(6)]
        [Id(6)]
        public float Intensity { get; set; }

        [MemoryPackOrder(7)]
        [Id(7)]
        public int StackCount { get; set; }

        [MemoryPackOrder(8)]
        [Id(8)]
        public EffectType Type { get; set; }
    }

    /// <summary>
    /// 效果类型
    /// </summary>
    public enum EffectType
    {
        Buff,
        Debuff,
        DamageOverTime,
        HealOverTime,
        Control
    }

    /// <summary>
    /// 战斗Grain实现
    /// </summary>
    public class CombatGrain : Grain<CombatState>, ICombatGrain
    {
        private readonly ILogger<CombatGrain> _logger;
        private readonly IPersistentState<CombatState> _combatState;

        private readonly IDataContext<GameEntityContext, CharacterEntity, long> _characterContext;
        private readonly IMapper _mapper;

        public CombatGrain(
            ILogger<CombatGrain> logger,
            [PersistentState("combat", "GameStore")] IPersistentState<CombatState> combatState,
            IDataContext<GameEntityContext, CharacterEntity, long> characterContext,
            IMapper mapper)
        {
            _logger = logger;
            _combatState = combatState;
            _characterContext = characterContext;
            _mapper = mapper;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("CombatGrain {GrainKey} activating.", this.GetPrimaryKey());

            // 初始化状态
            if (_combatState.State.CombatParticipants == null)
                _combatState.State.CombatParticipants = new Dictionary<ulong, CombatInfo>();

            if (_combatState.State.ActiveEffects == null)
                _combatState.State.ActiveEffects = new Dictionary<ulong, EffectInfo>();

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<DamageMessage> ProcessAttackAsync(AttackMessage request)
        {
            try
            {
                _logger.LogInformation("处理攻击请求: {AttackerId} 攻击 {TargetId}", request.AttackerId, request.TargetId);

                // 获取攻击者和目标信息
                var attackerInfo = await GetOrCreateCombatInfo(request.AttackerId);
                var targetInfo = await GetOrCreateCombatInfo(request.TargetId);

                // 计算伤害
                float damage = await CalculateDamage(attackerInfo, targetInfo, request.Damage, request.ElementType);

                // 判断是否暴击
                bool isCritical = request.IsCritical || Random.Shared.NextDouble() < 0.1; // 10%基础暴击率
                if (isCritical)
                {
                    damage *= 1.5f; // 暴击伤害1.5倍
                }

                // 更新目标血量
                targetInfo.Health = Math.Max(0, targetInfo.Health - damage);

                // 进入战斗状态
                await EnterCombatAsync(request.AttackerId);
                await EnterCombatAsync(request.TargetId);

                // 创建伤害响应
                var response = new DamageMessage
                {
                    AttackerId = request.AttackerId,
                    VictimId = request.TargetId,
                    Damage = (int)damage,
                    RemainingHealth = (int)targetInfo.Health,
                    IsCritical = isCritical,
                    IsDodged = false,
                    IsBlocked = false,
                    ElementType = request.ElementType
                };

                // 保存状态
                await _combatState.WriteStateAsync();

                _logger.LogInformation("攻击处理完成: 造成伤害 {Damage}, 目标剩余血量 {RemainingHealth}", damage, targetInfo.Health);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理攻击请求时发生异常: {AttackerId} -> {TargetId}", request.AttackerId, request.TargetId);
                throw;
            }
        }

        public async Task<SkillCastMessage> ProcessSkillCastAsync(SkillCastMessage request)
        {
            try
            {
                _logger.LogInformation("处理技能施放请求: {CasterId} 施放技能 {SkillId}", request.CasterId, request.SkillId);

                var casterInfo = await GetOrCreateCombatInfo(request.CasterId);

                // 检查技能消耗
                if (casterInfo.Energy < request.EnergyCost)
                {
                    _logger.LogWarning("技能施放失败: {CasterId} 能量不足", request.CasterId);
                    return new SkillCastMessage
                    {
                        CasterId = request.CasterId,
                        SkillId = request.SkillId,
                        Success = false,
                        Message = "能量不足"
                    };
                }

                // 消耗能量
                casterInfo.Energy -= request.EnergyCost;

                // 根据技能类型执行不同逻辑
                var response = new SkillCastMessage
                {
                    CasterId = request.CasterId,
                    SkillId = request.SkillId,
                    Success = true,
                    Message = "技能施放成功",
                    CastTime = request.CastTime,
                    Range = request.Range,
                    TargetEntityId = request.TargetEntityId
                };

                // 进入战斗状态
                await EnterCombatAsync(request.CasterId);

                // 保存状态
                await _combatState.WriteStateAsync();

                _logger.LogInformation("技能施放处理完成: {CasterId} -> {SkillId}", request.CasterId, request.SkillId);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理技能施放请求时发生异常: {CasterId} -> {SkillId}", request.CasterId, request.SkillId);
                throw;
            }
        }

        public async Task<DamageMessage> TakeDamageAsync(DamageMessage request)
        {
            try
            {
                _logger.LogInformation("处理受伤请求: {VictimId} 受到伤害 {Damage}", request.VictimId, request.Damage);

                var victimInfo = await GetOrCreateCombatInfo(request.VictimId);

                // 更新血量
                victimInfo.Health = Math.Max(0, victimInfo.Health - request.Damage);

                // 进入战斗状态
                await EnterCombatAsync(request.VictimId);

                // 检查是否死亡
                if (victimInfo.Health <= 0)
                {
                    await ProcessDeathAsync(new DeathMessage
                    {
                        DeceasedId = request.VictimId,
                        KillerId = request.AttackerId,
                        Cause = "战斗死亡",
                        DeathPosition = request.ImpactPosition
                    });
                }

                // 创建响应
                var response = new DamageMessage
                {
                    AttackerId = request.AttackerId,
                    VictimId = request.VictimId,
                    Damage = (int)request.Damage,
                    RemainingHealth = (int)victimInfo.Health,
                    IsCritical = request.IsCritical,
                    IsDodged = request.IsDodged,
                    IsBlocked = request.IsBlocked,
                    ImpactPosition = request.ImpactPosition,
                    ElementType = request.ElementType
                };

                // 保存状态
                await _combatState.WriteStateAsync();

                _logger.LogInformation("受伤处理完成: {VictimId} 剩余血量 {RemainingHealth}", request.VictimId, victimInfo.Health);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理受伤请求时发生异常: {VictimId}", request.VictimId);
                throw;
            }
        }

        public async Task<DeathMessage> ProcessDeathAsync(DeathMessage request)
        {
            try
            {
                _logger.LogInformation("处理死亡请求: {DeceasedId} 被 {KillerId} 杀死", request.DeceasedId, request.KillerId);

                var deceasedInfo = await GetOrCreateCombatInfo(request.DeceasedId);

                // 设置血量为0
                deceasedInfo.Health = 0;

                // 退出战斗状态
                await ExitCombatAsync(request.DeceasedId);

                // 创建响应
                var response = new DeathMessage
                {
                    DeceasedId = request.DeceasedId,
                    KillerId = request.KillerId,
                    Cause = request.Cause,
                    DeathPosition = request.DeathPosition
                };

                // 保存状态
                await _combatState.WriteStateAsync();

                _logger.LogInformation("死亡处理完成: {DeceasedId}", request.DeceasedId);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理死亡请求时发生异常: {DeceasedId}", request.DeceasedId);
                throw;
            }
        }

        public async Task<ResurrectMessage> ProcessResurrectAsync(ResurrectMessage request)
        {
            try
            {
                _logger.LogInformation("处理复活请求: {ResurrectedId}", request.ResurrectedId);

                var resurrectedInfo = await GetOrCreateCombatInfo(request.ResurrectedId);

                // 恢复血量（根据复活类型决定恢复比例）
                float restoreRatio = request.ResurrectType == 1 ? 1.0f : 0.5f; // 1=完全复活，其他=半血复活
                resurrectedInfo.Health = resurrectedInfo.MaxHealth * restoreRatio;

                // 创建响应
                var response = new ResurrectMessage
                {
                    ResurrectedId = request.ResurrectedId,
                    ResurrectPosition = request.ResurrectPosition,
                    ResurrectType = request.ResurrectType,
                    RemainingHealth = resurrectedInfo.Health,
                    MaxHealth = resurrectedInfo.MaxHealth
                };

                // 保存状态
                await _combatState.WriteStateAsync();

                _logger.LogInformation("复活处理完成: {ResurrectedId} 恢复血量至 {Health}", request.ResurrectedId, resurrectedInfo.Health);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理复活请求时发生异常: {ResurrectedId}", request.ResurrectedId);
                throw;
            }
        }

        public async Task<bool> EnterCombatAsync(ulong characterId)
        {
            try
            {
                var combatInfo = await GetOrCreateCombatInfo(characterId);
                
                combatInfo.IsInCombat = true;
                combatInfo.LastActionTime = DateTime.UtcNow;

                await _combatState.WriteStateAsync();

                _logger.LogInformation("角色 {CharacterId} 进入战斗状态", characterId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "角色 {CharacterId} 进入战斗状态时发生异常", characterId);
                return false;
            }
        }

        public async Task<bool> ExitCombatAsync(ulong characterId)
        {
            try
            {
                if (_combatState.State.CombatParticipants.ContainsKey(characterId))
                {
                    var combatInfo = _combatState.State.CombatParticipants[characterId];
                    combatInfo.IsInCombat = false;

                    await _combatState.WriteStateAsync();

                    _logger.LogInformation("角色 {CharacterId} 退出战斗状态", characterId);

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "角色 {CharacterId} 退出战斗状态时发生异常", characterId);
                return false;
            }
        }

        public async Task<bool> IsInCombatAsync(ulong characterId)
        {
            try
            {
                if (_combatState.State.CombatParticipants.ContainsKey(characterId))
                {
                    var combatInfo = _combatState.State.CombatParticipants[characterId];
                    
                    // 检查是否超过5秒无动作，自动退出战斗
                    if (combatInfo.IsInCombat && 
                        (DateTime.UtcNow - combatInfo.LastActionTime).TotalSeconds > 5)
                    {
                        combatInfo.IsInCombat = false;
                        await _combatState.WriteStateAsync();
                    }

                    return combatInfo.IsInCombat;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查角色 {CharacterId} 战斗状态时发生异常", characterId);
                return false;
            }
        }

        public async Task<EffectMessage> ApplyEffectAsync(EffectMessage request)
        {
            try
            {
                _logger.LogInformation("应用效果: {EffectId} 到 {TargetId}", request.EffectId, request.TargetId);

                var effectId = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
                var effectInfo = new EffectInfo
                {
                    EffectId = request.EffectId,
                    EffectName = request.EffectName,
                    TargetId = request.TargetId,
                    SourceId = request.SourceId,
                    Duration = request.Duration,
                    RemainingDuration = request.Duration,
                    Intensity = request.Intensity,
                    StackCount = request.StackCount,
                    Type = (EffectType)request.EffectType
                };

                // 添加效果
                _combatState.State.ActiveEffects[effectId] = effectInfo;

                // 保存状态
                await _combatState.WriteStateAsync();

                _logger.LogInformation("效果应用完成: {EffectId} -> {TargetId}", request.EffectId, request.TargetId);

                return new EffectMessage
                {
                    EffectId = request.EffectId,
                    EffectName = request.EffectName,
                    TargetId = request.TargetId,
                    SourceId = request.SourceId,
                    Duration = request.Duration,
                    Intensity = request.Intensity,
                    StackCount = request.StackCount,
                    EffectType = request.EffectType,
                    Applied = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "应用效果时发生异常: {EffectId} -> {TargetId}", request.EffectId, request.TargetId);
                throw;
            }
        }

        public async Task<float> CalculateWuxingDamageAsync(ulong attackerId, ulong defenderId, float baseDamage, int elementType)
        {
            try
            {
                var attackerInfo = await GetOrCreateCombatInfo(attackerId);
                var defenderInfo = await GetOrCreateCombatInfo(defenderId);

                // 五行相克计算
                float multiplier = 1.0f;

                // 简化五行相克逻辑：金克木、木克土、土克水、水克火、火克金
                // 1=金, 2=木, 3=水, 4=火, 5=土
                if (attackerInfo.WuxingElement != 0 && defenderInfo.WuxingElement != 0)
                {
                    if ((attackerInfo.WuxingElement == 1 && defenderInfo.WuxingElement == 2) || // 金克木
                        (attackerInfo.WuxingElement == 2 && defenderInfo.WuxingElement == 5) || // 木克土
                        (attackerInfo.WuxingElement == 5 && defenderInfo.WuxingElement == 3) || // 土克水
                        (attackerInfo.WuxingElement == 3 && defenderInfo.WuxingElement == 4) || // 水克火
                        (attackerInfo.WuxingElement == 4 && defenderInfo.WuxingElement == 1))   // 火克金
                    {
                        multiplier = 1.25f; // 相克伤害增加25%
                    }
                    else if ((attackerInfo.WuxingElement == 1 && defenderInfo.WuxingElement == 4) || // 金被火克
                             (attackerInfo.WuxingElement == 4 && defenderInfo.WuxingElement == 2) || // 火被木克
                             (attackerInfo.WuxingElement == 2 && defenderInfo.WuxingElement == 3) || // 木被水克
                             (attackerInfo.WuxingElement == 3 && defenderInfo.WuxingElement == 5) || // 水被土克
                             (attackerInfo.WuxingElement == 5 && defenderInfo.WuxingElement == 1))   // 土被金克
                    {
                        multiplier = 0.8f; // 被克伤害减少20%
                    }
                }

                float finalDamage = baseDamage * multiplier;

                // 应用防御减免
                float defenseReduction = defenderInfo.Defense / (defenderInfo.Defense + 100);
                finalDamage *= (1 - defenseReduction);

                return finalDamage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "计算五行伤害时发生异常: {AttackerId} -> {DefenderId}", attackerId, defenderId);
                return baseDamage; // 返回基础伤害作为备用
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// 获取或创建战斗信息
        /// </summary>
        private async Task<CombatInfo> GetOrCreateCombatInfo(ulong characterId)
        {
            if (!_combatState.State.CombatParticipants.ContainsKey(characterId))
            {
                // 从数据库加载角色信息
                var characterEntity = await _characterContext.QueryFirstOrDefaultAsync(c => c.Id == (long)characterId);
                
                if (characterEntity != null)
                {
                    var combatInfo = new CombatInfo
                    {
                        CharacterId = characterId,
                        Health = characterEntity.Health,
                        MaxHealth = characterEntity.MaxHealth,
                        AttackPower = characterEntity.AttackPower,
                        Defense = characterEntity.Defense,
                        WuxingElement = characterEntity.WuxingElement,
                        Energy = 100,
                        MaxEnergy = 100,
                        IsInCombat = false,
                        LastActionTime = DateTime.UtcNow
                    };
                    
                    _combatState.State.CombatParticipants[characterId] = combatInfo;
                }
                else
                {
                    // 默认值
                    var combatInfo = new CombatInfo
                    {
                        CharacterId = characterId,
                        Health = 100,
                        MaxHealth = 100,
                        AttackPower = 50,
                        Defense = 20,
                        WuxingElement = 0,
                        Energy = 100,
                        MaxEnergy = 100,
                        IsInCombat = false,
                        LastActionTime = DateTime.UtcNow
                    };
                    
                    _combatState.State.CombatParticipants[characterId] = combatInfo;
                }
            }

            return _combatState.State.CombatParticipants[characterId];
        }

        /// <summary>
        /// 计算伤害
        /// </summary>
        private async Task<float> CalculateDamage(CombatInfo attacker, CombatInfo defender, float baseDamage, int elementType)
        {
            // 基础伤害计算
            float damage = attacker.AttackPower * 0.5f + baseDamage;

            // 五行伤害计算
            if (elementType > 0)
            {
                damage = await CalculateWuxingDamageAsync(attacker.CharacterId, defender.CharacterId, damage, elementType);
            }
            else
            {
                // 普通物理伤害计算
                float defenseReduction = defender.Defense / (defender.Defense + 100);
                damage *= (1 - defenseReduction);
            }

            return damage;
        }

        #endregion
    }
}
