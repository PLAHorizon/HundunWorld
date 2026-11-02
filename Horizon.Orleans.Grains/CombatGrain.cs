using AutoMapper;
using Horizon.Core.Abstract;
using Horizon.Core.Helper;
using Horizon.Entities;
using Horizon.Game.Core;
using Horizon.Game.Core.Combat;
using Horizon.Game.Message;
using Horizon.Game.Message.Combat;
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

        /// <summary>
        /// 技能模板上下文（技能五行与威力的权威出处）。可为 null（单测/未迁移），
        /// null 或查不到模板时按"技能不改变五行口径"处理——沿用施法者命行。
        /// </summary>
        private readonly IDataContext<GameEntityContext, SkillTemplateEntity, int>? _skillTemplateContext;

        /// <summary>
        /// 物品模板上下文（装备与宝石的属性出处）。可为 null（单测/降级），
        /// null 时装备不进入权威结算——与接入前"装备只影响客户端展示"一致，而不是给一套猜测数值。
        /// </summary>
        private readonly IDataContext<GameEntityContext, ItemTemplateEntity, int>? _itemTemplateContext;

        /// <summary>
        /// 技能模板按 Grain 激活期缓存（内容表几乎不变，每次施法查库会把热路径压到 DB 上）。
        /// 可空 + 就地建：测试用 <c>FormatterServices.GetUninitializedObject</c> 造 Grain 时
        /// 字段初始化器根本不执行，写成非空只会在单测里凭空 NRE。
        /// </summary>
        private Dictionary<int, SkillTemplateEntity?>? _skillTemplateCache;

        /// <summary>物品模板缓存，可空理由同 <see cref="_skillTemplateCache"/>。</summary>
        private Dictionary<int, ItemTemplateEntity?>? _itemTemplateCache;

        private readonly IMapper _mapper;

        /// <summary>
        /// 阶段 3.1 — ICharacterGrain 解析器（HP 单一权威回写目标）。
        /// 默认经 <see cref="GrainFactory"/> 解析；测试可注入 mock 验证回写契约。
        /// </summary>
        private readonly Func<ulong, ICharacterGrain> _characterGrainProvider;

        /// <summary>
        /// 破势夺元要碰的两个 SkillGrain。留成可注入的口子是为了能测：
        /// 测试脚手架里的 <c>GrainFactory</c> 缺位时，直接调用会被 catch 吞成 false，
        /// "两人互相打断周天"这条核心玩法就永远没有断言覆盖。
        /// </summary>
        private Func<ulong, ISkillGrain>? _skillGrainProvider;

        public CombatGrain(
            ILogger<CombatGrain> logger,
            [PersistentState("combat", "GameStore")] IPersistentState<CombatState> combatState,
            IDataContext<GameEntityContext, CharacterEntity, long> characterContext,
            IMapper mapper,
            Func<ulong, ICharacterGrain>? characterGrainProvider = null,
            IDataContext<GameEntityContext, CharacterAttributeEntity, long>? attributeContext = null,
            IDataContext<GameEntityContext, SkillTemplateEntity, int>? skillTemplateContext = null,
            IDataContext<GameEntityContext, ItemTemplateEntity, int>? itemTemplateContext = null)
        {
            _logger = logger;
            _combatState = combatState;
            _characterContext = characterContext;
            _mapper = mapper;
            _characterGrainProvider = characterGrainProvider ?? (id => GrainFactory.GetGrain<ICharacterGrain>((long)id));
            _skillGrainProvider = id => GrainFactory.GetGrain<ISkillGrain>(GrainKeys.ForEntity(id));
            _attributeContext = attributeContext;
            _skillTemplateContext = skillTemplateContext;
            _itemTemplateContext = itemTemplateContext;
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

                // 结算前把实时 Buff 并入两侧五行三行（攻方影响伤害基数，守方影响减免）
                RefreshElementVectors(attackerInfo);
                var targetBuffDefense = RefreshElementVectors(targetInfo);

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

                // 计算伤害（攻击侧五行权威口径，防御减免在 CharacterGrain 裁决）
                int attackElement = ResolveAttackElement(request.ElementType, attackerInfo);
                float damage = CalculateDamage(attackerInfo.ElementAttack, attackerInfo, targetInfo, request.Damage, attackElement);

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
                // Phase 2：裁决层可能减免伤害（通用防御 + 五行防御 - 攻方破防 / 减伤 Buff），
                // 本地 Health 以裁决结果回写，故攻方所用元素与破防必须一并下传。
                var hpResult = await SyncHpToCharacterAsync(
                    request.TargetId, -(int)damage, request.AttackerId, Horizon.Orleans.Interface.World.DamageType.Physical,
                    attackElement, attackerInfo.ElementPenetration, targetBuffDefense);
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
                    ElementType = attackElement
                };

                // 记录战斗日志
                AddCombatLogEntry(new CombatLogEntry
                {
                    Timestamp = DateTime.Now,
                    AttackerId = request.AttackerId,
                    DefenderId = request.TargetId,
                    DamageDealt = damage,
                    SkillId = 0,
                    ElementType = attackElement,
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
                RefreshElementVectors(casterInfo);
                RegenerateEnergy(casterInfo);

                // 技能模板：五行、威力与能量消耗的权威出处（客户端只报"施放了哪个技能 ID"）。
                var template = await LoadSkillTemplateAsync(request.SkillId);
                float energyCost = ResolveEnergyCost(template, request.EnergyCost);

                // 检查技能消耗
                if (casterInfo.Energy < energyCost)
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
                casterInfo.Energy -= energyCost;

                bool brokeStance = false;

                if (request.TargetEntityId > 0)
                {
                    var targetInfo = await GetOrCreateCombatInfo(request.TargetEntityId);
                    var skillTargetBuffDefense = RefreshElementVectors(targetInfo);

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

                    // 五行阶梯式：基座降到 0.25 并乘模板倍率（白·初 1.10 … 道·极 4.76），
                    // 再乘这一击的循环节系数。未入阶梯的行（旧技能/未种子）逐值退回接入前的
                    // AttackPower × 0.6 —— 接线本身不许暗砍没参与改动的老技能。
                    float ladderFactor = CombatCalculator.SkillLadderFactor(template, out float skillMultiplier);
                    float loopMultiplier = request.LoopMultiplier > 0f ? request.LoopMultiplier : 1f;
                    float baseDamage = casterInfo.AttackPower * ladderFactor * skillMultiplier * loopMultiplier;

                    // 技能的五行与技能威力一律读上面那份服务端模板：客户端只上报"施放了哪个技能 ID"，
                    // 报不出数字。模板缺失（内容表尚未编辑该技能/未迁移）时沿用施法者命行、
                    // 不给额外行攻击，即与接入前逐值一致。
                    int skillElement = template != null && template.WuxingElement > 0
                        ? template.WuxingElement
                        : ResolveAttackElement(0, casterInfo);
                    float skillAttack = template?.SkillAttack ?? 0f;
                    float skillPenetration = template?.SkillPenetration ?? 0f;

                    // 技能自身的攻击力临时加在它所依据的那一五行行上：一次施法不应改常驻画像，
                    // 破防则按"已高者计"取更高的一方而不是相加。
                    var castAttack = casterInfo.ElementAttack.WithRowAdded(skillElement, skillAttack);
                    var castPenetration = casterInfo.ElementPenetration.WithRowMax(skillElement, skillPenetration);

                    float damage = CalculateDamage(castAttack, casterInfo, targetInfo, baseDamage, skillElement);

                    bool isCritical = Random.Shared.NextDouble() < casterInfo.CritRate;
                    damage = CombatCalculator.ApplyCriticalDamage(damage, isCritical, casterInfo.CritDamageMultiplier);

                    var (blockedDamage, isBlocked) = CombatCalculator.ApplyBlock(damage, targetInfo.BlockRate);
                    damage = blockedDamage;

                    // 破势夺元（方案 §5.2）：这一式克住守方护体行时，夺它一层循环节归到自己链上。
                    brokeStance = await BreakAndStealCycleAsync(
                        skillElement, skillTargetBuffDefense, request.CasterId, request.TargetEntityId);

                    // 阶段 3.1：HP 单一权威——技能结算回写 CharacterGrain（见 ProcessAttackAsync 注释）。
                    // Phase 2：裁决层可能减免伤害，本地 Health 以裁决结果回写。
                    var hpResult = await SyncHpToCharacterAsync(
                        request.TargetEntityId, -(int)damage, request.CasterId, Horizon.Orleans.Interface.World.DamageType.Internal,
                        skillElement, castPenetration, skillTargetBuffDefense);
                    targetInfo.Health = hpResult?.CurrentHp ?? CombatCalculator.ClampHealth(targetInfo.Health, damage);

                    AddCombatLogEntry(new CombatLogEntry
                    {
                        Timestamp = DateTime.Now,
                        AttackerId = request.CasterId,
                        DefenderId = request.TargetEntityId,
                        DamageDealt = damage,
                        SkillId = request.SkillId,
                        ElementType = skillElement,
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
                    // 破势与周天是玩家看得见的战果，回执里带出来，客户端指示器与飘字才有依据
                    Message = brokeStance ? "技能施放成功（破势夺元）" : "技能施放成功",
                    LoopStack = request.LoopStack,
                    LoopMultiplier = request.LoopMultiplier,
                    CycleCompleted = request.CycleCompleted,
                    LoopCompletionCount = request.LoopCompletionCount,
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
                var victimBuffDefense = RefreshElementVectors(victimInfo);

                // 阶段 3.1：HP 单一权威——受击结算回写 CharacterGrain（见 ProcessAttackAsync 注释）。
                // Phase 2：裁决层可能减免伤害，本地 Health 以裁决结果回写。
                // 攻方破防只在其战斗信息已在本地缓存时参与——不为一次环境/外部伤害专门拉取攻方全表。
                ElementStatVector attackerPenetration =
                    _combatState.State.CombatParticipants.TryGetValue(request.AttackerId, out var attackerInfo)
                        ? attackerInfo.ElementPenetration
                        : ElementStatVector.Zero;
                var hpResult = await SyncHpToCharacterAsync(
                    request.VictimId, -(int)request.Damage, request.AttackerId, Horizon.Orleans.Interface.World.DamageType.Physical,
                    request.ElementType, attackerPenetration, victimBuffDefense);
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
        private async Task<HpChangeResult?> SyncHpToCharacterAsync(ulong characterId, int hpDelta, ulong sourceId, Horizon.Orleans.Interface.World.DamageType damageType,
            int attackElement = 0, ElementStatVector attackerPenetration = default, ElementStatVector defenderBuffDefense = default)
        {
            try
            {
                var characterGrain = _characterGrainProvider(characterId);
                return await characterGrain.RequestElementalHpChangeAsync(
                    hpDelta, sourceId, damageType, attackElement, attackerPenetration, defenderBuffDefense);
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

                // key 必须由 (目标, 效果) 决定：原先用 Guid.NewGuid() 做键，同一 Buff 每次施加都
                // 新增一条互不相干的记录——既刷不掉旧时长（同一 Buff 的 N 份拷贝同时在结算里生效），
                // 也让"移除某个 Buff"无从下手。
                var effectKey = EffectKey(request.TargetId, request.EffectId);
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
                    Type = (EffectType)request.EffectType,
                    AppliedAtUtc = DateTime.UtcNow,
                    AttackBoost = request.AttackBoost,
                    DefenseBoost = request.DefenseBoost,
                    PenetrationBoost = request.PenetrationBoost,
                };

                // 覆盖式刷新：重复施加同一效果重置时长与数值，不叠加层数
                // （叠加需要 Buff 模板表给出每层上限，那张表目前不存在，宁可少给也不要给错）。
                _combatState.State.ActiveEffects[effectKey] = effectInfo;

                var targetInfo = await GetOrCreateCombatInfo(request.TargetId);
                RefreshElementVectors(targetInfo);

                // 保存状态
                await _combatState.WriteStateAsync();

                _logger.LogInformation(
                    "效果应用完成: {EffectId} -> {TargetId}, 时长 {Duration} 秒, 五行攻击 {Attack} / 防御 {Defense} / 破防 {Penetration}",
                    request.EffectId, request.TargetId, request.Duration,
                    targetInfo.ElementAttack.Total, targetInfo.ElementDefense.Total, targetInfo.ElementPenetration.Total);

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
                    AttackBoost = request.AttackBoost,
                    DefenseBoost = request.DefenseBoost,
                    PenetrationBoost = request.PenetrationBoost,
                    Applied = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "应用效果时发生异常: {EffectId} -> {TargetId}", request.EffectId, request.TargetId);
                throw;
            }
        }

        /// <summary>同一 (目标, 效果) 恒定映射到同一条记录，重复施加即刷新而不是累积副本。</summary>
        private static ulong EffectKey(ulong targetId, int effectId)
            => unchecked(targetId * 1000003ul + (uint)effectId);

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
                // SkillGrain 按施法者寻址（原实现用 Guid.NewGuid()：拿到的是一个全新的空
                // SkillGrain，InterruptCastAsync 打断的从来不是施法者自己的读条）。
                var skillGrain = GrainFactory.GetGrain<ISkillGrain>(GrainKeys.ForEntity(casterId));

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
        /// 攻防血与五行三行一律取自 <see cref="CombatAttributeProfiles.Build"/> 的画像结果
        /// （属性表行 + 加点派生，攻=各来源求和、防与破防=已高者计），不在这里二次相加，
        /// 避免出现"通用属性一套口径、五行另一套口径"的漂移。
        /// </para>
        /// <para>
        /// 画像有 <see cref="ProfileTtl"/> 的有效期：穿戴/卸下/镶嵌与加点都发生在角色键的 Grain 上，
        /// 而 CombatGrain 按受击方寻址，没有一条能可靠定向到"这份缓存所在的那个对抗 Grain"的失效通道，
        /// 所以攻方侧靠 TTL 重读收敛陈旧度（守方减免侧由 CharacterGrain 自己即时失效）。
        /// </para>
        /// <para>
        /// 属性表不可用（上下文未注入/表未迁移）时降级为"未分配"，行为与接入前一致——
        /// 注意降级会让该角色的加点派生整体消失，排查"加点没效果"先看那两条 Warning。
        /// </para>
        /// </summary>
        /// <summary>画像有效期：穿戴/加点发生在角色键的 Grain 上，
        /// 而 CombatGrain 按受击方寻址、无法被可靠地定向失效，所以用 TTL 让攻方侧最迟 2 秒内跟上。</summary>
        private static readonly TimeSpan ProfileTtl = TimeSpan.FromSeconds(2);

        private async Task<CombatInfo> GetOrCreateCombatInfo(ulong characterId)
        {
            if (!_combatState.State.CombatParticipants.TryGetValue(characterId, out var info))
            {
                info = new CombatInfo
                {
                    CharacterId = characterId,
                    IsInCombat = false,
                    LastActionTime = DateTime.Now,
                };
                _combatState.State.CombatParticipants[characterId] = info;
                await ApplyProfileAsync(info, initializeVitals: true);
            }
            else if (info.ProfileExpiresUtc != default && DateTime.UtcNow > info.ProfileExpiresUtc)
            {
                // 旧状态（迁移前写入的记录）没有到期时刻，按"不自动重读"处理：
                // 让它们继续用已有数值，比凭空把 MaxHealth 重置成默认值安全得多。
                await ApplyProfileAsync(info, initializeVitals: false);
            }

            return info;
        }

        /// <summary>
        /// 读权威数据源并重算该参与者的画像：通用攻防、暴击/闪避/格挡、五行来源清单与三行。
        /// <para>
        /// <paramref name="initializeVitals"/> 只在首次建档时为真——后续刷新必须保留这一战的实时
        /// 血量与内力，否则装备一改就把人奶满。上限变化时把当前值压回新上限之内。
        /// </para>
        /// </summary>
        private async Task ApplyProfileAsync(CombatInfo info, bool initializeVitals)
        {
            var characterId = info.CharacterId;
            var characterEntity = await _characterContext.QueryFirstOrDefaultAsync(c => c.Id == (long)characterId);
            var attrRow = await LoadAttributeRowAsync(characterId);

            // 穿戴中的装备与宝石：模板表里的属性 JSON 是唯一被信的那一份数字。
            // 没有模板上下文就没有装备贡献——顺便省掉一次跨 Grain 往返（画像每次过期重算都会走到这里）。
            var equipment = _itemTemplateContext == null
                ? ItemAttributeContract.Combine(Array.Empty<ItemAttributeContract.ParsedItemAttributes>())
                : await CombatEquipmentSource.GatherAsync(
                    GrainFactory, _itemTemplateContext, characterId,
                _itemTemplateCache ??= new Dictionary<int, ItemTemplateEntity?>(), _logger);

            // 权威画像：通用攻防基值 + 五行三行（攻=各来源求和，防/破防=已高者计）。
            // AttackPower/Defense 直接取画像结果，避免与加点派生各算一遍产生漂移。
            var profile = CombatAttributeProfiles.Build(
                ToAllocation(attrRow), attrRow,
                baseDefense: (characterEntity?.Defense ?? AttributeDerivation.DefaultBaseDefense) + equipment.PhysicalDefense,
                baseAttack: (characterEntity?.AttackPower ?? AttributeDerivation.DefaultBaseAttack) + equipment.PhysicalAttack,
                element: characterEntity?.WuxingElement ?? 0,
                extraAffinity: equipment.Affinity,
                extraSources: equipment.Contribution);
            var bonuses = profile.Bonuses;

            info.AttackPower = profile.BaseAttack;
            info.Defense = profile.BaseDefense;
            info.WuxingElement = characterEntity?.WuxingElement ?? 0;
            // 角色表里的 max_health 是 0 时，那是"从没写过"，不是"这个角色的上限就是 0/100"：
            // 缺省必须回到与客户端同一个基值，否则服务端算出的血池永远比面板小一个数量级。
            var rowMaxHealth = characterEntity != null && characterEntity.MaxHealth > 0f
                ? characterEntity.MaxHealth
                : AttributeDerivation.HealthBaseValue;
            info.MaxHealth = rowMaxHealth + bonuses.MaxHealthBonus + equipment.MaxHealth;
            info.MaxEnergy = AttributeDerivation.EnergyBaseValue + bonuses.MaxEnergyBonus + equipment.MaxEnergy;
            info.ElementSources = profile.Sources;
            info.ElementAffinity = profile.Affinity;

            if (characterEntity != null)
            {
                var baseCritRate = attrRow != null && attrRow.CriticalRate > 0f ? attrRow.CriticalRate : 0.1f;
                var baseDodgeRate = attrRow != null && attrRow.DodgeRate > 0f ? attrRow.DodgeRate : 0.05f;
                var baseBlockRate = attrRow != null && attrRow.BlockRate > 0f ? attrRow.BlockRate : 0.1f;
                var baseCritDamage = attrRow != null && attrRow.CriticalDamage > 0f ? attrRow.CriticalDamage : 0f;

                info.DodgeRate = Clamp01(baseDodgeRate);
                info.BlockRate = Clamp01(baseBlockRate);
                info.CritRate = Clamp01(baseCritRate + bonuses.CritRateBonus + equipment.CritRate);
                info.CritDamageMultiplier = 1.5f + baseCritDamage + bonuses.CritDamageBonus;
            }
            else
            {
                // 角色表无此 id（怪物/占位实体）：保留历史默认命中参数，
                // 攻防与五行三行仍走同一份画像，不另开一套数。
                info.DodgeRate = 0.05f;
                info.BlockRate = 0.1f;
                info.CritRate = 0.1f;
                info.CritDamageMultiplier = 1.5f;
            }

            if (initializeVitals)
            {
                // 生命周期起点：角色表里 (Health=0, MaxHealth=0) 表示"这两个值从没被写过"
                // （至今没有任何路径往 max_health 落库），不是"这个角色的上限是 0 且已死亡"。
                // 把它当死亡会让服务端与客户端背道而驰：面板满血、攻击却回"施法者已死亡"。
                // 真正打过之后的血是由 HP 裁决写回的，那时 max_health 也已随之落库。
                var rowHealth = characterEntity?.Health ?? 0f;
                var rowMax = characterEntity?.MaxHealth ?? 0f;
                info.Health = characterEntity is null
                    ? AttributeDerivation.HealthBaseValue
                    : rowMax > 0f || rowHealth > 0f
                        ? rowHealth
                        : info.MaxHealth;
                info.Energy = info.MaxEnergy;
                info.LastEnergyRegenUtc = DateTime.UtcNow;
            }
            else
            {
                if (info.Health > info.MaxHealth) info.Health = info.MaxHealth;
                if (info.Energy > info.MaxEnergy) info.Energy = info.MaxEnergy;
            }

            info.ProfileExpiresUtc = DateTime.UtcNow + ProfileTtl;

            // Buff 与常驻来源一起聚合出结算用的三行
            RefreshElementVectors(info);
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

        /// <summary>
        /// 读取技能模板（技能五行与技能威力的权威出处）。上下文缺失、表未迁移或无该行都返回 null，
        /// 调用方据此退回"沿用施法者命行、技能不额外加行攻击"的旧口径。
        /// </summary>
        private async Task<SkillTemplateEntity?> LoadSkillTemplateAsync(int skillId)
        {
            if (_skillTemplateContext == null || skillId <= 0) return null;
            _skillTemplateCache ??= new Dictionary<int, SkillTemplateEntity?>();
            if (_skillTemplateCache.TryGetValue(skillId, out var cached)) return cached;

            SkillTemplateEntity? template = null;
            try
            {
                template = await _skillTemplateContext.QueryFirstOrDefaultAsync(t => t.Id == skillId);
            }
            catch (Exception ex)
            {
                // 技能表可能尚未应用迁移（三列不存在）：按无五行加成的普通技能处理，不阻断施法。
                _logger.LogWarning(ex, "加载技能模板失败，按无五行加成处理: SkillId={SkillId}", skillId);
            }

            _skillTemplateCache[skillId] = template;
            return template;
        }

        private static AttributeAllocationData? ToAllocation(CharacterAttributeEntity? row)
            => row == null ? null : new AttributeAllocationData
            {
                Strength = row.Strength,
                Agility = row.Agility,
                Health = row.Constitution,
                // 该列历史上存"内力点数"，2026-09-28 起存"智力点数"（同一列，免迁移）
                Intelligence = row.InternalForce,
                Metal = row.MetalPoint,
                Wood = row.WoodPoint,
                Water = row.WaterPoint,
                Fire = row.FirePoint,
                Earth = row.EarthPoint,
            };

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>
        /// 结算前刷新参与者的五行三行：常驻来源清单 + 当前未过期的 Buff 来源，
        /// 一起交给 <see cref="CombatProfile.Aggregate"/>（攻=求和、防与破防=已高者计）。
        /// <para>
        /// 逐击重算而不是"施加时算一次"：<c>ActiveEffects</c> 没有任何 tick 会递减
        /// <see cref="EffectInfo.RemainingDuration"/>，所以到期只能按
        /// <c>AppliedAtUtc + Duration</c> 在读侧惰性判定，顺带把已过期的效果清掉。
        /// </para>
        /// </summary>
        /// <returns>
        /// 本次并入的 Buff 防御行（已高者计）。受击裁决在 CharacterGrain 那一侧进行，
        /// 而 Buff 记录属于战斗侧，所以调用方把这份临时增量随 HP 请求下传，
        /// 由档案侧与常驻来源走同一套聚合规则。
        /// </returns>
        private ElementStatVector RefreshElementVectors(CombatInfo info)
        {
            if (info == null) return ElementStatVector.Zero;

            var sources = new List<CombatContribution>(info.ElementSources ?? new List<CombatContribution>());
            var buffSources = new List<CombatContribution>();
            List<ulong>? expired = null;

            if (_combatState.State.ActiveEffects != null)
            {
                var now = DateTime.UtcNow;
                foreach (var kv in _combatState.State.ActiveEffects)
                {
                    var effect = kv.Value;
                    if (effect == null || effect.TargetId != info.CharacterId) continue;

                    if (IsExpired(effect, now))
                    {
                        (expired ??= new List<ulong>()).Add(kv.Key);
                        continue;
                    }

                    // 一个效果 = 一份独立来源：多条防御 Buff 因此在"已高者计"下自动只取最高，
                    // 多条攻击 Buff 则在求和侧自然叠满，不需要为 Buff 另立一套聚合规则。
                    buffSources.Add(CombatContribution.FromVectors(CombatSourceType.Buff,
                        effect.AttackBoost, effect.DefenseBoost, effect.PenetrationBoost));
                }
            }

            sources.AddRange(buffSources);
            // 地板与 Buff 同批参与：每击都带着这份权威亲和重聚合，而不是把兜底结果烤进缓存。
            var vectors = CombatProfile.Aggregate(sources, info.ElementAffinity);
            info.ElementAttack = vectors.Attack;
            info.ElementDefense = vectors.Defense;
            info.ElementPenetration = vectors.Penetration;

            if (expired != null)
            {
                foreach (var key in expired) _combatState.State.ActiveEffects.Remove(key);
                _logger.LogInformation(
                    "清理 {Count} 条已到期效果（目标 {TargetId}）。", expired.Count, info.CharacterId);
            }

            return ElementalDamageResolver.MaxSources(buffSources.Select(s => s.Defense));
        }

        /// <summary>
        /// 按经过的时间补算内力回复。<see cref="CombatInfo.Energy"/> 没有任何 tick 会去加，
        /// 所以回复只能在读取点（施法检查消耗前）惰性结算，与 Buff 过期同一套做法。
        /// <para>
        /// 上限变化时（换装备/改加点）当前值只会被压回新上限，不会借机回满；
        /// 旧状态没有记录时刻，按"刚刚回复过"处理，避免重启瞬间按从 0001-01-01 起的
        /// 无穷间隔把池子刷满。
        /// </para>
        /// </summary>
        private static void RegenerateEnergy(CombatInfo info)
        {
            var now = DateTime.UtcNow;
            if (info.LastEnergyRegenUtc == default)
            {
                info.LastEnergyRegenUtc = now;
                return;
            }

            double elapsedSeconds = (now - info.LastEnergyRegenUtc).TotalSeconds;
            if (elapsedSeconds <= 0) return;

            info.LastEnergyRegenUtc = now;
            if (info.Energy >= info.MaxEnergy) return;

            float regenerated = (float)(info.Energy + elapsedSeconds * AttributeDerivation.EnergyRecoveryPerSecond);
            info.Energy = regenerated > info.MaxEnergy ? info.MaxEnergy : regenerated;
        }

        /// <summary>
        /// Duration&lt;=0 视作永久（历史数据与"无时限光环"都按此处理）；
        /// 未记录施加时刻（旧状态反序列化默认值）同样视作永久，避免重启后 Buff 凭空蒸发。
        /// </summary>
        private static bool IsExpired(EffectInfo effect, DateTime nowUtc)
            => effect.Duration > 0f
               && effect.AppliedAtUtc != default
               && (nowUtc - effect.AppliedAtUtc).TotalSeconds >= effect.Duration;

        /// <summary>
        /// 计算伤害（攻击侧权威口径）。
        /// <para>
        /// 通用伤害沿用既有 <c>CalculateBaseDamage</c>（攻力×0.5 + 上报值），加上命中行五行攻击
        /// （各来源求和），再乘五行克制倍率；防御/减伤一律留给
        /// <c>CharacterGrain.RequestElementalHpChangeAsync</c> 裁决（见类注释）。
        /// </para>
        /// <para>
        /// 与旧口径的两处有意差异：① 五行攻击首次参与结算；② 克制改用标准相克环
        /// （<see cref="ElementalDamageResolver.OvercomeBy"/>），修掉旧
        /// <c>GetWuxingMultiplier</c> 里"火攻木 / 木攻水 / 土攻金"三条非标准削弱。
        /// 没有五行数据的角色逐值不变。
        /// </para>
        /// </summary>
        private static float CalculateDamage(in ElementStatVector elementAttack, CombatInfo attacker, CombatInfo defender,
            float baseDamage, int attackElement)
        {
            var atk = ElementalDamageResolver.ResolveAttackSide(elementAttack, attackElement, defender.WuxingElement);
            float physical = CombatCalculator.CalculateBaseDamage(attacker.AttackPower, baseDamage);
            return (physical + atk.Attack) * atk.Multiplier;
        }

        /// <summary>
        /// 本次攻击所用五行：请求显式指定优先，否则退化为攻方命行（都为 0 即无属性）。
        /// </summary>
        private static int ResolveAttackElement(int requestedElement, CombatInfo attacker)
            => requestedElement > 0 ? requestedElement : attacker.WuxingElement;

        /// <summary>
        /// 技能能量消耗：优先取服务端模板的 <c>energy_cost_formula</c>（内容表目前填的是同值常数）。
        /// <para>
        /// 不能直接用客户端报的数：自上报通道打通后，报 <c>EnergyCost = 0</c> 就等于无限免费施法。
        /// 模板缺行、列未迁移或公式不是常数（真的是公式）时才退回客户端值——那时至少行为与接入前一致，
        /// 而不是把一次施法判成 0 消耗或干脆拒绝。
        /// </para>
        /// </summary>
        /// <summary>
        /// 守方护体行的兜底口径：五行防御向量最高的一行。玩家设了镇岳行时以它为准
        /// （判定在 <see cref="ISkillGrain.BreakCycleAsync"/> 内，那里才有镇岳状态）。
        /// </summary>
        private static int DominantDefenseRow(ElementStatVector defense)
        {
            int best = 0;
            float bestValue = 0f;
            for (int row = 1; row <= 5; row++)
            {
                float value = defense.At(row);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = row;
                }
            }
            return best;
        }

        /// <summary>
        /// 破势夺元。两个 SkillGrain 调用都不回身指向 CombatGrain（施放校验在 SkillGrain、
        /// 结算在 CombatGrain，由 SkillHandler 顺序串起来），所以这条边不会构成 Grain 环。
        /// "克没克到"交给守方自己的 Grain 判（镇岳在那边），这里只在确认夺到层之后才给攻方记一层，
        /// 免得双方各加一层、破势变成互赠。
        /// </summary>
        private async Task<bool> BreakAndStealCycleAsync(
            int attackElement, ElementStatVector defenderDefense, ulong casterId, ulong targetId)
        {
            try
            {
                var provider = _skillGrainProvider;
                if (provider == null) return false;

                var victim = provider(targetId);
                if (!await victim.BreakCycleAsync(attackElement, DominantDefenseRow(defenderDefense)))
                    return false;

                var taker = provider(casterId);
                await taker.GrantCycleAsync();

                _logger.LogInformation("破势夺元: {Caster} 以第 {Attack} 行破开 {Target} 的护体",
                    casterId, attackElement, targetId);
                return true;
            }
            catch (Exception ex)
            {
                // 夺元是额外战果，失败绝不能把已经算好的伤害回滚或让整次施放报错
                _logger.LogWarning(ex, "破势夺元写入失败: caster={Caster} target={Target}", casterId, targetId);
                return false;
            }
        }

        /// <summary>
        /// 技能能量消耗：优先取服务端模板的 <c>energy_cost_formula</c>（内容表目前填的是同值常数）。
        /// <para>
        /// 不能直接用客户端报的数：自上报通道打通后，报 <c>EnergyCost = 0</c> 就等于无限免费施法。
        /// 模板缺行、列未迁移或公式不是常数（真的是公式）时才退回客户端值——那时至少行为与接入前一致，
        /// 而不是把一次施法判成 0 消耗或干脆拒绝。
        /// </para>
        /// </summary>
        private float ResolveEnergyCost(SkillTemplateEntity? template, float reportedCost)
        {
            var formula = template?.EnergyCostFormula;
            if (!string.IsNullOrWhiteSpace(formula)
                && float.TryParse(formula.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var constant)
                && constant >= 0f)
            {
                return constant;
            }

            return reportedCost;
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
