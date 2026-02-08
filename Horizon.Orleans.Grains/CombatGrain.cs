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
using System.Linq;
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

        [MemoryPackOrder(2)]
        [Id(2)]
        public List<CombatLogEntry> CombatLog { get; set; } = new List<CombatLogEntry>();
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

        [MemoryPackOrder(11)]
        [Id(11)]
        public float DodgeRate { get; set; }

        [MemoryPackOrder(12)]
        [Id(12)]
        public float BlockRate { get; set; }

        [MemoryPackOrder(13)]
        [Id(13)]
        public float CritRate { get; set; } = 0.1f;

        [MemoryPackOrder(14)]
        [Id(14)]
        public float CritDamageMultiplier { get; set; } = 1.5f;

        /// <summary>
        /// 技能冷却记录（技能ID -> 上次施放时间）
        /// </summary>
        [MemoryPackOrder(15)]
        [Id(15)]
        public Dictionary<int, DateTime> SkillCooldowns { get; set; } = new();
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

            if (_combatState.State.CombatLog == null)
                _combatState.State.CombatLog = new List<CombatLogEntry>();

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

                // 闪避判定
                if (CombatCalculator.RollDodge(targetInfo.DodgeRate))
                {
                    _logger.LogInformation("攻击被闪避: {AttackerId} -> {TargetId}", request.AttackerId, request.TargetId);
                    return new DamageMessage
                    {
                        AttackerId = request.AttackerId,
                        VictimId = request.TargetId,
                        Damage = 0,
                        RemainingHealth = (int)targetInfo.Health,
                        IsCritical = false,
                        IsDodged = true,
                        IsBlocked = false,
                        ElementType = request.ElementType
                    };
                }

                // 计算伤害
                float damage = await CalculateDamage(attackerInfo, targetInfo, request.Damage, request.ElementType);

                // 格挡判定
                var (blockedDamage, isBlocked) = CombatCalculator.ApplyBlock(damage, targetInfo.BlockRate);
                damage = blockedDamage;

                // 判断是否暴击（使用角色暴击率属性）
                bool isCritical = Random.Shared.NextDouble() < attackerInfo.CritRate;
                damage = CombatCalculator.ApplyCriticalDamage(damage, isCritical, attackerInfo.CritDamageMultiplier);

                // 更新目标血量
                targetInfo.Health = CombatCalculator.ClampHealth(targetInfo.Health, damage);

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
                    IsBlocked = isBlocked,
                    ElementType = request.ElementType
                };

                // 记录战斗日志
                AddCombatLogEntry(new CombatLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    AttackerId = request.AttackerId,
                    DefenderId = request.TargetId,
                    DamageDealt = damage,
                    SkillId = 0,
                    ElementType = request.ElementType,
                    IsCritical = isCritical,
                    IsDodged = false,
                    IsBlocked = isBlocked,
                    LogType = CombatLogType.Attack
                });

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
                victimInfo.Health = CombatCalculator.ClampHealth(victimInfo.Health, request.Damage);

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
                resurrectedInfo.Health = CombatCalculator.CalculateResurrectHealth(resurrectedInfo.MaxHealth, request.ResurrectType);

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

                return CombatCalculator.CalculateWuxingDamage(
                    baseDamage, attackerInfo.WuxingElement, defenderInfo.WuxingElement, defenderInfo.Defense);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "计算五行伤害时发生异常: {AttackerId} -> {DefenderId}", attackerId, defenderId);
                return baseDamage; // 返回基础伤害作为备用
            }
        }

        public Task<List<CombatLogEntry>> GetCombatLogAsync(int count = 50)
        {
            try
            {
                var log = _combatState.State.CombatLog;
                int skip = Math.Max(0, log.Count - count);
                var result = log.Skip(skip).Take(count).ToList();
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取战斗日志失败");
                throw;
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
                        DodgeRate = 0.05f,
                        BlockRate = 0.1f,
                        CritRate = 0.1f,
                        CritDamageMultiplier = 1.5f,
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
                        DodgeRate = 0.05f,
                        BlockRate = 0.1f,
                        CritRate = 0.1f,
                        CritDamageMultiplier = 1.5f,
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
            float damage = CombatCalculator.CalculateBaseDamage(attacker.AttackPower, baseDamage);

            // 五行伤害计算
            if (elementType > 0)
            {
                damage = await CalculateWuxingDamageAsync(attacker.CharacterId, defender.CharacterId, damage, elementType);
            }
            else
            {
                // 普通物理伤害计算
                float defenseReduction = CombatCalculator.CalculateDefenseReduction(defender.Defense);
                damage *= (1 - defenseReduction);
            }

            return damage;
        }

        /// <summary>
        /// 记录战斗日志
        /// </summary>
        private void AddCombatLogEntry(CombatLogEntry entry)
        {
            _combatState.State.CombatLog.Add(entry);

            // Limit log to last 200 entries
            if (_combatState.State.CombatLog.Count > 200)
            {
                _combatState.State.CombatLog.RemoveRange(0, _combatState.State.CombatLog.Count - 200);
            }
        }

        #endregion
    }
}
