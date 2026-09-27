using AutoMapper;
using Horizon.Core.Abstract;
using Horizon.Core.Helper;
using Horizon.Entities;
using Horizon.Game.Core.Combat;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Orleans.Interface.World;
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
    /// 战斗Grain实现
    /// </summary>
    public class CombatGrain : Grain<CombatState>, ICombatGrain
    {
        private readonly ILogger<CombatGrain> _logger;
        private readonly IPersistentState<CombatState> _combatState;

        private readonly IDataContext<GameEntityContext, CharacterEntity, long> _characterContext;

        /// <summary>
        /// 属性表上下文（加点持久层）。可为 null（单测/降级），null 时按"未分配"处理。
        /// </summary>
        private readonly IDataContext<GameEntityContext, CharacterAttributeEntity, long>? _attributeContext;

        private readonly IMapper _mapper;

        /// <summary>
        /// 阶段 3.1 — ICharacterGrain 解析器（HP 单一权威回写目标）。
        /// 默认经 <see cref="GrainFactory"/> 解析；测试可注入 mock 验证回写契约。
        /// </summary>
        private readonly Func<ulong, ICharacterGrain> _characterGrainProvider;

        public CombatGrain(
            ILogger<CombatGrain> logger,
            [PersistentState("combat", "GameStore")] IPersistentState<CombatState> combatState,
            IDataContext<GameEntityContext, CharacterEntity, long> characterContext,
            IMapper mapper,
            Func<ulong, ICharacterGrain>? characterGrainProvider = null,
            IDataContext<GameEntityContext, CharacterAttributeEntity, long>? attributeContext = null)
        {
            _logger = logger;
            _combatState = combatState;
            _characterContext = characterContext;
            _mapper = mapper;
            _characterGrainProvider = characterGrainProvider ?? (id => GrainFactory.GetGrain<ICharacterGrain>((long)id));
            _attributeContext = attributeContext;
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

                // 阶段 3.1：HP 单一权威——战斗结算结果回写 CharacterGrain.RequestHpChangeAsync
                // （CharacterInfo.CurrentHealth 唯一持久化权威）。此前攻击只改 CombatInfo.Health，
                // 从不回写 → HP 双权威残留（角色档案 HP 与实际战斗 HP 脱节）。
                // 致死归零由 RequestHpChangeAsync 内部 clamp 到 0 并标记死亡完成，
                // ProcessDeathAsync 只管理战斗状态，避免 DeathCount 重复计数。
                // Phase 2：裁决层可能减免伤害（防御/减伤/Buff），本地 Health 以裁决结果回写。
                var hpResult = await SyncHpToCharacterAsync(
                    request.TargetId, -(int)damage, request.AttackerId, Horizon.Orleans.Interface.World.DamageType.Physical);
                targetInfo.Health = hpResult?.CurrentHp ?? CombatCalculator.ClampHealth(targetInfo.Health, damage);

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
                    Timestamp = DateTime.Now,
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

                if (request.TargetEntityId > 0)
                {
                    var targetInfo = await GetOrCreateCombatInfo(request.TargetEntityId);

                    if (CombatCalculator.RollDodge(targetInfo.DodgeRate))
                    {
                        await EnterCombatAsync(request.CasterId);
                        await EnterCombatAsync(request.TargetEntityId);
                        await _combatState.WriteStateAsync();

                        return new SkillCastMessage
                        {
                            CasterId = request.CasterId,
                            SkillId = request.SkillId,
                            Success = true,
                            Message = "技能被闪避",
                            CastTime = request.CastTime,
                            Range = request.Range,
                            TargetEntityId = request.TargetEntityId
                        };
                    }

                    float baseDamage = casterInfo.AttackPower * 0.6f;
                    float damage = await CalculateDamage(casterInfo, targetInfo, baseDamage, 0);

                    bool isCritical = Random.Shared.NextDouble() < casterInfo.CritRate;
                    damage = CombatCalculator.ApplyCriticalDamage(damage, isCritical, casterInfo.CritDamageMultiplier);

                    var (blockedDamage, isBlocked) = CombatCalculator.ApplyBlock(damage, targetInfo.BlockRate);
                    damage = blockedDamage;

                    // 阶段 3.1：HP 单一权威——技能结算回写 CharacterGrain（见 ProcessAttackAsync 注释）。
                    // Phase 2：裁决层可能减免伤害，本地 Health 以裁决结果回写。
                    var hpResult = await SyncHpToCharacterAsync(
                        request.TargetEntityId, -(int)damage, request.CasterId, Horizon.Orleans.Interface.World.DamageType.Internal);
                    targetInfo.Health = hpResult?.CurrentHp ?? CombatCalculator.ClampHealth(targetInfo.Health, damage);

                    AddCombatLogEntry(new CombatLogEntry
                    {
                        Timestamp = DateTime.Now,
                        AttackerId = request.CasterId,
                        DefenderId = request.TargetEntityId,
                        DamageDealt = damage,
                        SkillId = request.SkillId,
                        ElementType = 0,
                        IsCritical = isCritical,
                        IsDodged = false,
                        IsBlocked = isBlocked,
                        LogType = CombatLogType.SkillCast
                    });

                    if (targetInfo.Health <= 0)
                    {
                        await ProcessDeathAsync(new DeathMessage
                        {
                            DeceasedId = request.TargetEntityId,
                            KillerId = request.CasterId,
                            Cause = "技能击杀",
                            DeathPosition = request.TargetPosition
                        });
                    }
                }

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

                // 阶段 3.1：HP 单一权威——受击结算回写 CharacterGrain（见 ProcessAttackAsync 注释）。
                // Phase 2：裁决层可能减免伤害，本地 Health 以裁决结果回写。
                var hpResult = await SyncHpToCharacterAsync(
                    request.VictimId, -(int)request.Damage, request.AttackerId, Horizon.Orleans.Interface.World.DamageType.Physical);
                victimInfo.Health = hpResult?.CurrentHp ?? CombatCalculator.ClampHealth(victimInfo.Health, request.Damage);

                // 进入战斗状态
                await EnterCombatAsync(request.VictimId);

                // 检查打断施法
                if (victimInfo.IsInCombat && request.Damage > 0)
                {
                    await InterruptSkillCastAsync(request.VictimId);
                }

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

                // 阶段 3.1：HP 单一权威——复活结果同步回 CharacterGrain（权威复活流程：
                // 恢复血量 + IsAlive=true + 复活位置 + ResurrectionCount）。
                try
                {
                var characterGrain = _characterGrainProvider(request.ResurrectedId);
                await characterGrain.ResurrectAsync(new ResurrectMessage
                    {
                        ResurrectedId = request.ResurrectedId,
                        ResurrectPosition = request.ResurrectPosition,
                        ResurrectType = request.ResurrectType,
                    });
                }
                catch (Exception syncEx)
                {
                    _logger.LogError(syncEx,
                        "CombatGrain {GrainKey}: 复活结果同步 CharacterGrain 失败。ResurrectedId={ResurrectedId}",
                        this.GetPrimaryKey(), request.ResurrectedId);
                }

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

        /// <summary>
        /// 阶段 3.1 — HP 单一权威回写：把战斗结算的 HP 增量同步到 CharacterGrain
        /// （<c>CharacterInfo.CurrentHealth</c> 唯一持久化权威）。
        /// <para>
        /// 回写失败不抛异常（战斗结算已完成，日志告警即可）；权威同步失败会在下次
        /// 会话/离开空间时由 CharacterGrain 重新校准。避免把战斗热路径与权威同步强耦合。
        /// 返回裁决结果（实际 HP 变化量与变更后 HP），调用方用 <see cref="HpChangeResult.CurrentHp"/>
        /// 回写本地 <c>CombatInfo.Health</c>，使判死判断与权威一致（裁决可能减免伤害）。
        /// </para>
        /// </summary>
        private async Task<HpChangeResult?> SyncHpToCharacterAsync(ulong characterId, int hpDelta, ulong sourceId, Horizon.Orleans.Interface.World.DamageType damageType)
        {
            try
            {
                var characterGrain = _characterGrainProvider(characterId);
                return await characterGrain.RequestHpChangeAsync(hpDelta, sourceId, damageType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "CombatGrain {GrainKey}: HP 权威回写失败。CharacterId={CharacterId}, Delta={Delta}",
                    this.GetPrimaryKey(), characterId, hpDelta);
                return null;
            }
        }

        public async Task<bool> EnterCombatAsync(ulong characterId)
        {
            try
            {
                var combatInfo = await GetOrCreateCombatInfo(characterId);
                
                combatInfo.IsInCombat = true;
                combatInfo.LastActionTime = DateTime.Now;

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
                        (DateTime.Now - combatInfo.LastActionTime).TotalSeconds > 5)
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

        public async Task<bool> InterruptSkillCastAsync(ulong casterId)
        {
            try
            {
                var skillGrainKey = Guid.NewGuid();
                var skillGrain = GrainFactory.GetGrain<ISkillGrain>(skillGrainKey);

                if (_combatState.State.CombatParticipants.TryGetValue(casterId, out var combatInfo))
                {
                    if (combatInfo.Health <= 0)
                    {
                        await skillGrain.InterruptCastAsync();
                        _logger.LogInformation("角色 {CasterId} 因死亡被打断施法", casterId);
                        return true;
                    }

                    if (combatInfo.IsInCombat && Random.Shared.NextDouble() < 0.3)
                    {
                        await skillGrain.InterruptCastAsync();
                        _logger.LogInformation("角色 {CasterId} 受到攻击被打断施法", casterId);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打断施法时发生异常: {CasterId}", casterId);
                return false;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// 获取或创建战斗信息。
        /// <para>
        /// 战斗属性 = 角色基础值 + 加点派生加成（<see cref="AttributeDerivation"/>，服务端单一来源）；
        /// 暴击/闪避/格挡优先取属性表列值（未落列时保留历史默认 0.1/0.05/0.1），再叠加加点。
        /// 属性表不可用（上下文未注入/表未迁移）时降级为"未分配"，行为与接入前一致。
        /// </para>
        /// </summary>
        private async Task<CombatInfo> GetOrCreateCombatInfo(ulong characterId)
        {
            if (!_combatState.State.CombatParticipants.ContainsKey(characterId))
            {
                // 从数据库加载角色信息
                var characterEntity = await _characterContext.QueryFirstOrDefaultAsync(c => c.Id == (long)characterId);

                var attrRow = await LoadAttributeRowAsync(characterId);
                var bonuses = AttributeDerivation.Derive(ToAllocation(attrRow));

                if (characterEntity != null)
                {
                    var baseCritRate = attrRow != null && attrRow.CriticalRate > 0f ? attrRow.CriticalRate : 0.1f;
                    var baseDodgeRate = attrRow != null && attrRow.DodgeRate > 0f ? attrRow.DodgeRate : 0.05f;
                    var baseBlockRate = attrRow != null && attrRow.BlockRate > 0f ? attrRow.BlockRate : 0.1f;
                    var baseCritDamage = attrRow != null && attrRow.CriticalDamage > 0f ? attrRow.CriticalDamage : 0f;

                    var maxEnergy = 100f + bonuses.MaxEnergyBonus;
                    var combatInfo = new CombatInfo
                    {
                        CharacterId = characterId,
                        Health = characterEntity.Health,
                        MaxHealth = characterEntity.MaxHealth + bonuses.MaxHealthBonus,
                        AttackPower = characterEntity.AttackPower + bonuses.AttackBonus,
                        Defense = characterEntity.Defense + bonuses.DefenseBonus,
                        WuxingElement = characterEntity.WuxingElement,
                        Energy = maxEnergy,
                        MaxEnergy = maxEnergy,
                        DodgeRate = Clamp01(baseDodgeRate),
                        BlockRate = Clamp01(baseBlockRate),
                        CritRate = Clamp01(baseCritRate + bonuses.CritRateBonus),
                        CritDamageMultiplier = 1.5f + baseCritDamage + bonuses.CritDamageBonus,
                        IsInCombat = false,
                        LastActionTime = DateTime.Now
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
                        LastActionTime = DateTime.Now
                    };
                    
                    _combatState.State.CombatParticipants[characterId] = combatInfo;
                }
            }

            return _combatState.State.CombatParticipants[characterId];
        }

        /// <summary>读取属性表行（加点+基础战斗属性列）。上下文缺失或表未迁移时返回 null。</summary>
        private async Task<CharacterAttributeEntity?> LoadAttributeRowAsync(ulong characterId)
        {
            if (_attributeContext == null)
                return null;

            try
            {
                return await _attributeContext.QueryFirstOrDefaultAsync(a => a.CharacterId == characterId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载角色属性表失败，按未分配处理: CharacterId={CharacterId}", characterId);
                return null;
            }
        }

        private static AttributeAllocationData? ToAllocation(CharacterAttributeEntity? row)
            => row == null ? null : new AttributeAllocationData
            {
                Strength = row.Strength,
                Agility = row.Agility,
                Health = row.Constitution,
                Energy = row.InternalForce,
                Metal = row.MetalPoint,
                Wood = row.WoodPoint,
                Water = row.WaterPoint,
                Fire = row.FirePoint,
                Earth = row.EarthPoint,
            };

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>
        /// 计算伤害
        /// </summary>
        private async Task<float> CalculateDamage(CombatInfo attacker, CombatInfo defender, float baseDamage, int elementType)
        {
            // 基础伤害计算
            float damage = CombatCalculator.CalculateBaseDamage(attacker.AttackPower, baseDamage);

            // 五行克制计算（仅攻击侧加成，不含防御——防御/减伤/Buff 统一由
            // CharacterGrain.RequestHpChangeAsync 裁决，避免双重减免）。
            if (elementType > 0)
            {
                float multiplier = CombatCalculator.GetWuxingMultiplier(attacker.WuxingElement, defender.WuxingElement);
                damage *= multiplier;
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
