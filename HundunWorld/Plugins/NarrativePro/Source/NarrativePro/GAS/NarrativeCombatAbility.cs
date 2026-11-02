using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 战斗追踪数据。对应 UE5 FCombatTraceData。
    /// 描述攻击的检测范围（距离、半径、通道）。
    /// </summary>
    [Serializable]
    public class CombatTraceData
    {
        /// <summary>追踪距离。</summary>
        public float TraceDistance = 300f;

        /// <summary>追踪半径（>0 表示 SphereCast，=0 表示 RayCast）。</summary>
        public float TraceRadius = 100f;

        /// <summary>追踪通道名称（用于碰撞过滤）。</summary>
        public string TraceChannel = "Visibility";

        /// <summary>是否忽略自身。</summary>
        public bool bIgnoreSelf = true;

        public CombatTraceData() { }

        public CombatTraceData(float distance, float radius = 0f)
        {
            TraceDistance = distance;
            TraceRadius = radius;
        }
    }

    /// <summary>
    /// 命中结果。对应 UE5 FHitResult（简化版）。
    /// </summary>
    [Serializable]
    public class CombatHitResult
    {
        /// <summary>被命中的 Actor。</summary>
        public Actor HitActor;

        /// <summary>命中点世界坐标。</summary>
        public Vector3 ImpactPoint = Vector3.Zero;

        /// <summary>命中法线。</summary>
        public Vector3 ImpactNormal = Vector3.Up;

        /// <summary>追踪起点。</summary>
        public Vector3 TraceStart = Vector3.Zero;

        /// <summary>追踪终点。</summary>
        public Vector3 TraceEnd = Vector3.Zero;

        /// <summary>是否命中。</summary>
        public bool bBlockingHit = false;

        /// <summary>
        /// 命中面的物理材质。对应 UE5 FHitResult::PhysMaterial，
        /// 伤害执行计算用它取 MaterialMultiplier（NarrativePhysicalMaterial.GetDamageMultiplier）。
        /// </summary>
        public FlaxEngine.PhysicalMaterial PhysicalMaterial;

        public CombatHitResult() { }
    }

    /// <summary>
    /// 战斗能力。对应 UE5 UNarrativeCombatAbility。
    /// 内建 hitscan/碰撞检测、伤害处理，被 melee 武器和 hitscan 武器共用。
    /// 简化点：
    /// - 移除 UE5 FGameplayAbilityTargetDataHandle，改为 CombatHitResult 列表
    /// - 移除网络复制，目标数据本地处理
    /// - 武器引用通过路径占位（GAS Phase 7 + Items Phase 1 已有 WeaponItem）
    /// </summary>
    public class NarrativeCombatAbility : NarrativeGameplayAbility
    {
        // ===== 配置字段 =====

        /// <summary>是否需要弹药（如果为 true，检查 Item 是否有 ammo class）。</summary>
        public bool bRequiresAmmo = false;

        /// <summary>默认攻击伤害值（能力激活时直接使用，可被 GetAttackDamage 覆盖）。</summary>
        public float DefaultAttackDamage = 10f;

        /// <summary>
        /// 伤害效果定义 ID/路径（对应 UE5 攻击能力上配置的 Damage GE 软引用）。
        /// 由 <c>Content/NarrativePro/GAS/Abilities/*.json</c> 的 <c>damageEffectPath</c> 写入，
        /// 命中时经 <see cref="NarrativeGASAssetRegistry.ResolveEffect"/> 解析，
        /// 使执行计算派发（ASC 按 <c>ExecuteCalcTypeId</c> 查表）真正被数据驱动走一遍。
        /// 留空或解析失败时回退 <see cref="CreateFallbackDamageEffect"/> 的程序化定义。
        /// </summary>
        public string DamageEffectPath = "";

        /// <summary>默认 Bot 攻击频率（次/秒，越大越频繁）。</summary>
        public float DefaultBotAttackFrequency = 1f;

        /// <summary>默认 Bot 攻击范围（cm）。</summary>
        public float DefaultBotAttackRange = 300f;

        /// <summary>是否主手武器（false 表示副手）。</summary>
        public bool bIsMainhand = true;

        /// <summary>武器散射角度（度，0 表示无散射）。</summary>
        public float WeaponSpread = 0f;

        // ===== 运行时状态 =====

        /// <summary>当前应用标签（每次 GenerateTargetData 时使用）。</summary>
        protected GameplayTag _currentApplicationTag = GameplayTag.None;

        /// <summary>当前追踪数据。</summary>
        protected CombatTraceData _currentTraceData;

        /// <summary>当前追踪起点。</summary>
        protected Vector3 _currentTraceStart = Vector3.Zero;

        /// <summary>最近一次最终化的目标数据（供表现层/调试读取）。</summary>
        protected List<CombatHitResult> _lastTargetData;

        // ===== 攻击执行窗口（移植 UE5 攻击流程：hitscan 开火 / melee anim-notify 扫描） =====

        /// <summary>是否近战能力（true 时走 WeaponVisual 的通知窗口 + 胶囊扫描链路）。</summary>
        public bool IsMelee = false;

        /// <summary>是否重击（提交时把 Ability.DamageType.Heavy 挂到持有者标签上，执行计算读取，对应 UE5 SourceTags 判定）。</summary>
        public bool IsHeavyAttack = false;

        /// <summary>近战扫描半径（厘米）。绑定武器视觉时用于回退球扫；近战通知窗口未配置碰撞数据时同样生效。</summary>
        public float MeleeSweepRadius = 80f;

        /// <summary>攻击动画通知：触发时间（秒）。对应 UE5 AnimNotifyState_AttackAnim 分支点的 TriggerTime。</summary>
        public float AttackNotifyTriggerTime = 0.1f;

        /// <summary>攻击动画通知：持续时长（秒）。对应 UE5 分支点的 Duration。</summary>
        public float AttackNotifyDuration = 0.35f;

        /// <summary>攻击动画播放速率（把动画时间映射到游戏时间）。</summary>
        public float AttackPlayRate = 1f;

        /// <summary>攻击连击动画集（连击索引的数据来源，可选；为空时按默认三段连击轮转）。</summary>
        public AttackComboAnimSet ComboAnimSet;

        /// <summary>远程开火的追踪起点提供者。默认相机朝向焦点（对应 UE5 UTargetingTransformProvider_CameraTowardsFocus）。</summary>
        public TargetingTransformProvider TargetingTransform = new TargetingTransformProvider_CameraTowardsFocus();

        /// <summary>近战扫描窗口是否打开（由 OnUpdate 逐帧驱动 WeaponVisual.PerformCollisionCheck）。</summary>
        private bool _bMeleeWindowOpen;

        /// <summary>近战扫描窗口结束的游戏时间。</summary>
        private float _meleeWindowEndTime;

        /// <summary>当前连击索引（0 基）。</summary>
        private int _comboIndex;

        /// <summary>本帧近战扫描命中缓冲。</summary>
        private readonly List<FlaxEngine.RayCastHit> _meleeFrameHits = new List<FlaxEngine.RayCastHit>();

        // ===== 提交与目标数据生命周期（对应 UE5 NarrativeCombatAbility.cpp:48-92） =====

        public NarrativeCombatAbility()
        {
            // 对应 UE5 NarrativeCombatAbility.cpp:95-108：只有需要弹药的武器才过弹药门槛。
            // 端口此前在构造里无条件置 true，导致徒手/近战能力在 BeginAttack 第一步就被弹药门取消。
            // 真实门槛由 IsAmmoRequiredFor(weapon) 从武器数据（RequiredAmmo / ClipSize）推导。
            bRequiresAmmo = false;
            DefaultBotAttackFrequency = 1f;
            DefaultBotAttackRange = 300f;
        }

        /// <summary>
        /// 本次攻击是否需要弹药：能力显式声明需要，或所持武器本身需要弹药
        /// （<c>RequiredAmmo</c> 非空、或有弹夹）。对应 UE5 只在带弹药的武器上检查 HasAmmo。
        /// </summary>
        protected virtual bool IsAmmoRequiredFor(NarrativePro.Items.WeaponItem weapon)
        {
            if (bRequiresAmmo) return true;
            return weapon != null && weapon.NeedsAmmo();
        }

        /// <summary>
        /// 提交执行。对应 UE5 <c>CommitExecute</c>（NarrativeCombatAbility.cpp:48-52）：
        /// 子类只转发基类，把"扣 Cost/起冷却"统一留在基类实现里。
        /// </summary>
        /// <summary>
        /// 重击标签的临时效果句柄。AddDynamicTagsGameplayEffect 建的是 Infinite 效果，
        /// 没有句柄就撤不掉——之前重击标签会永久留在持有者身上，一次重击后所有攻击都按重击结算。
        /// </summary>
        protected NarrativePro.GAS.ActiveGameplayEffectHandle _heavyTagEffectHandle = NarrativePro.GAS.ActiveGameplayEffectHandle.Invalid;

        public override void CommitExecute(GameplayAbilitySpecHandle handle, GameplayAbilitySpec spec)
        {
            base.CommitExecute(handle, spec);

            // 重击标签：对应 UE5 由能力 Tag 授予 Ability.DamageType.Heavy，
            // 伤害执行计算从来源标签聚合里读取（NarrativeDamageExecCalc.cpp:71）。
            if (IsHeavyAttack && OwningASC != null)
            {
                var heavyTag = NarrativePro.Core.NarrativeGameplayTags.Ability_DamageType_Heavy;
                if (heavyTag.IsValid() && !OwningASC.HasMatchingGameplayTag(heavyTag))
                {
                    _heavyTagEffectHandle = OwningASC.AddDynamicTagsGameplayEffect(
                        new GameplayTagContainer(new[] { heavyTag.TagName }));
                }
            }
        }

        /// <summary>
        /// 撤掉本能力挂上的重击标签（对应 UE5 ActivationOwnedTags 随能力结束一起移除）。
        /// </summary>
        protected virtual void RemoveHeavyAttackTag()
        {
            if (!_heavyTagEffectHandle.IsValid || OwningASC == null) return;
            OwningASC.RemoveActiveEffect(_heavyTagEffectHandle);
            _heavyTagEffectHandle = NarrativePro.GAS.ActiveGameplayEffectHandle.Invalid;
        }

        /// <summary>
        /// 激活能力：绑定"目标数据就绪"委托，再走基类激活。
        /// 对应 UE5 <c>ActivateAbility</c>（NarrativeCombatAbility.cpp:54-66，Lyra 的做法：
        /// 不用 Targeting Actor，直接自己投递目标数据）。
        /// </summary>
        public override void ActivateAbility(GameplayAbilitySpecHandle handle, GameplayAbilitySpec spec)
        {
            var asc = OwningASC;
            if (asc == null) return;

            // 绑定目标数据就绪回调（对应 AbilityTargetDataSetDelegate(...).AddUObject(this, &FinalizeTargetData)）
            _targetDataDelegateHandle = asc.BindTargetDataSetDelegate(handle, FinalizeTargetData);

            base.ActivateAbility(handle, spec);

            // 对应 UE5 BP 攻击能力的攻击窗口：激活即开火（hitscan）或开启近战通知扫描窗口。
            // UE5 由蓝图 Event / AnimNotify 驱动，这里把等价驱动落在能力自身。
            BeginAttack();
        }

        /// <summary>
        /// 攻击执行入口：弹药门槛 →（近战）通知扫描窗口 /（远程）开火管线。
        /// 对应 UE5 UNarrativeCombatAbility::HasAmmo 检查 + BP AttackPrimary/Secondary 的
        /// 发射与近战扫描分支（NarrativeCombatAbility.cpp:94-107）。
        /// </summary>
        protected virtual void BeginAttack()
        {
            var weapon = GetAbilityWeapon();

            // 弹药门槛（工作项 5）：CanAttack→HasAmmo→WeaponItem.HasAmmo 的实调用链。
            // 门槛只在"武器确实需要弹药"时启用（UE5 口径），近战/徒手永不在此被取消。
            if (IsAmmoRequiredFor(weapon) && !CanAttack())
            {
                if (weapon != null && weapon.RequiresAutoReload()) weapon.Reload();
                EndAbility(_currentHandle, true);
                return;
            }

            if (IsMelee) BeginMeleeAttackWindow(weapon);
            else FireRangedAttack(weapon);
        }

        /// <summary>
        /// 远程开火：消耗弹药 → 取追踪数据与起点 → 目标数据管线 → 立即收尾。
        /// 对应 UE5 hitscan 攻击的 Fire Weapon 路径（WeaponItem::ConsumeAmmo + CombatTraceData）。
        /// </summary>
        protected virtual void FireRangedAttack(NarrativePro.Items.WeaponItem weapon)
        {
            if (weapon != null && IsAmmoRequiredFor(weapon) && !weapon.ConsumeAmmo(1))
            {
                // 弹夹不足：尝试自动装填并取消本次开火
                if (weapon.RequiresAutoReload()) weapon.Reload();
                EndAbility(_currentHandle, true);
                return;
            }

            weapon?.OnAttack();

            GenerateTargetDataUsingTrace(BuildCombatTraceData(weapon), ProvideAttackStart(), DamageTypeTag());
            EndAbility(_currentHandle, false);
        }

        /// <summary>
        /// 开启近战扫描窗口（工作项 4）：构造 AnimNotifyEvent（对应 UE5
        /// AnimNotifyState_AttackAnim 分支点）→ WeaponVisual.CacheAnimationTransform →
        /// 之后由 OnUpdate 每帧 PerformCollisionCheck，把命中喂给伤害管线。
        /// 没有武器视觉（徒手/未绑定）时回退为一次前向球扫，仍走同一目标数据管线。
        /// </summary>
        protected virtual void BeginMeleeAttackWindow(NarrativePro.Items.WeaponItem weapon)
        {
            if (weapon != null && IsAmmoRequiredFor(weapon) && !weapon.ConsumeAmmo(1))
            {
                if (weapon.RequiresAutoReload()) weapon.Reload();
                EndAbility(_currentHandle, true);
                return;
            }

            weapon?.OnAttack();
            _currentApplicationTag = DamageTypeTag();

            var visual = GetAbilityWeaponVisualScript();
            if (visual != null)
            {
                visual.CacheCollisionData(false);

                var notify = CreateAttackNotifyEvent();
                visual.CacheAnimationTransform(NarrativePro.Weapons.AnimNotifyEventReference.Create(notify, this));

                float playRate = notify.PlayRate > 0f ? notify.PlayRate : 1f;
                _bMeleeWindowOpen = true;
                _meleeWindowEndTime = Time.GameTime + (notify.TriggerTime + notify.Duration) / playRate;
                return;
            }

            // 无视觉绑定的近战：用能力参数做一次扇形球扫（等价 UE5 未挂动画时的直接命中）
            GenerateTargetDataUsingTrace(BuildCombatTraceData(weapon), ProvideAttackStart(), _currentApplicationTag);
            EndAbility(_currentHandle, false);
        }

        /// <summary>
        /// 从连击数据构造攻击通知事件。移植 UE5 UAnimNotifyState_AttackAnim::BranchingPointNotifyBegin
        /// （WeaponVisual.cpp:17-25 / AnimNotifyState_AttackAnim.cpp:12-17）的等价物：
        /// Flax 无 AnimNotify 队列，通知由攻击能力按连击索引生成。
        /// <para>
        /// 连击动画集存在时（武器 <c>GetComboAnims</c> → UE5 <c>TArray&lt;UNarrativeAnimSet*&gt;</c> 口径），
        /// 通知名直接取当前连击段选中的 3P 蒙太奇路径：WeaponVisual.CacheAnimationTransform 以通知本体
        /// 为键缓存碰撞采样帧，动画层（UnrealFramework/NarrativeAnimInstance 的 TaggedAnimSets 查询链）
        /// 可按同名路径取到动画对——数据真正驱动动画选择，而不再是自造字符串。
        /// Flax 无 AnimMontage 实例注册表（已核 FlaxEngine.CSharp.xml：AnimatedModel 无 PlayAnimation/montage API），
        /// 蒙太奇在端口内保持路径字符串口径（AttackComboAnimSet.cs 头注），实际播放由动画层消费者按名取用。
        /// </para>
        /// </summary>
        protected virtual NarrativePro.Weapons.AnimNotifyEvent CreateAttackNotifyEvent()
        {
            _comboIndex = NextComboIndex();
            string kind = IsHeavyAttack ? "Heavy" : "Light";

            string notifyName = "AttackAnim_" + kind + "_" + _comboIndex;
            var comboAnim = GetActiveComboAnimation();
            if (comboAnim != null && !string.IsNullOrEmpty(comboAnim.Montage3PPath))
            {
                notifyName = comboAnim.Montage3PPath;
            }

            return new NarrativePro.Weapons.AnimNotifyEvent(
                notifyName,
                AttackNotifyTriggerTime,
                AttackNotifyDuration)
            {
                PlayRate = AttackPlayRate > 0f ? AttackPlayRate : 1f
            };
        }

        /// <summary>
        /// 当前攻击生效的连击段动画集列表：能力自带动画集优先（数据覆盖入口），
        /// 否则取所持武器的连击动画（对应 UE5 攻击流程调 <c>Weapon->GetComboAnims(bHeavy)</c>，
        /// 含主/副手与轻重变体选择）。无武器/无动画集返回 null（UE5 基类即返回空数组）。
        /// </summary>
        public virtual List<NarrativeAnimSet> GetActiveComboAnims()
        {
            if (ComboAnimSet != null)
            {
                if (IsHeavyAttack && ComboAnimSet.HeavyAttackCombos != null && ComboAnimSet.HeavyAttackCombos.Count > 0)
                    return ComboAnimSet.HeavyAttackCombos;
                return ComboAnimSet.AttackCombos;
            }

            var weapon = GetAbilityWeapon();
            return weapon != null ? weapon.GetComboAnims(IsHeavyAttack) : null;
        }

        /// <summary>
        /// 当前连击索引选中的动画对（1P/3P 蒙太奇路径）。供表现层/动画层查询本次挥击该播什么。
        /// 无连击动画集时返回 null（保持旧"仅索引轮转"行为）。
        /// </summary>
        public virtual NarrativeCharacterAnimation GetActiveComboAnimation()
        {
            var anims = GetActiveComboAnims();
            if (anims == null || _comboIndex < 0 || _comboIndex >= anims.Count) return null;
            return anims[_comboIndex] != null ? anims[_comboIndex].GetAnim(0) : null;
        }

        /// <summary>连击索引轮转：动画集存在时按其长度（武器/能力两级数据源，见 <see cref="GetActiveComboAnims"/>），否则默认三段连击（UE5 常规轻击连段）。</summary>
        protected virtual int NextComboIndex()
        {
            int comboCount = 0;
            var anims = GetActiveComboAnims();
            if (anims != null) comboCount = anims.Count;
            if (comboCount <= 0) comboCount = 3;

            int next = _comboIndex + 1;
            return next >= comboCount ? 0 : next;
        }

        /// <summary>本能力的伤害类型标签（近战/远程，对应 Narrative.Ability.DamageType.*）。</summary>
        protected virtual GameplayTag DamageTypeTag()
        {
            return IsMelee
                ? NarrativePro.Core.NarrativeGameplayTags.Ability_DamageType_Melee
                : NarrativePro.Core.NarrativeGameplayTags.Ability_DamageType_Ranged;
        }

        /// <summary>
        /// 组装本次攻击的追踪数据：远程优先用武器/弹药的 CombatTraceData（含弹药覆盖），
        /// 近战用武器攻击范围 + 扫描半径。
        /// </summary>
        protected virtual CombatTraceData BuildCombatTraceData(NarrativePro.Items.WeaponItem weapon)
        {
            if (IsMelee)
            {
                float meleeRange = weapon != null ? weapon.GetAttackRange() : GetBotAttackRange();
                if (meleeRange <= 0f) meleeRange = GetBotAttackRange();
                return new CombatTraceData(meleeRange, MeleeSweepRadius);
            }

            var ranged = weapon as NarrativePro.Items.RangedWeaponItem;
            if (ranged != null)
            {
                NarrativePro.Items.CombatTraceData weaponTrace = ranged.GetTraceData();
                if (weaponTrace != null)
                {
                    return new CombatTraceData(weaponTrace.Range, weaponTrace.Radius);
                }
            }
            return new CombatTraceData(1000f, 0f);
        }

        /// <summary>
        /// 攻击追踪起点：优先瞄准变换提供者（对应 UE5 TargetingTransformProvider），
        /// 提供者返回零变换（未配置）时回退 Actor 位置。
        /// </summary>
        protected virtual Vector3 ProvideAttackStart()
        {
            var hostActor = OwningASC != null ? OwningASC.Actor : Actor;
            if (TargetingTransform != null && hostActor != null)
            {
                Transform provided = TargetingTransform.ProvideTargetingTransform(hostActor);
                if (provided.Translation.LengthSquared > 0.0001f)
                {
                    return provided.Translation;
                }
            }
            return hostActor != null ? hostActor.Position : Vector3.Zero;
        }

        /// <summary>
        /// 结束能力：校验句柄有效性 → 作用域锁延后 → 移除委托并消费残留目标数据 → 收尾。
        /// 对应 UE5 <c>EndAbility</c>（NarrativeCombatAbility.cpp:68-92）。
        /// </summary>
        public virtual void EndAbility(GameplayAbilitySpecHandle handle, bool bWasCancelled)
        {
            var asc = OwningASC;

            // 重击标签随能力结束移除（否则一次重击后永久生效）
            RemoveHeavyAttackTag();

            // 近战窗口未走完就被结束（取消/失误）：清视觉攻击数据，防止脏 CurrentNotifyEvent
            if (_bMeleeWindowOpen)
            {
                _bMeleeWindowOpen = false;
                var visual = GetAbilityWeaponVisualScript();
                visual?.CleanupAttackData();
            }

            // 对应 UE5 IsEndAbilityValid(Handle, ActorInfo)
            if (!IsEndAbilityValid(handle, asc)) return;

            // 对应 UE5 ScopeLockCount > 0 时把 EndAbility 塞进 WaitingToExecute 并直接返回
            if (ScopeLockCount > 0)
            {
                AddWaitingToExecute(() => EndAbility(handle, bWasCancelled));
                return;
            }

            if (asc != null)
            {
                // 能力结束时消费目标数据并移除委托
                asc.UnbindTargetDataSetDelegate(handle);
                asc.ConsumeTargetData(handle);
                asc.ClearTargetDataForSpec(handle);
                asc.EndAbility(handle, bWasCancelled);
            }

            _targetDataDelegateHandle = 0;
            _currentHandle = GameplayAbilitySpecHandle.Invalid;
        }

        /// <summary>
        /// 近战扫描窗口的逐帧驱动（对应 UE5 BP 攻击能力在动画通知窗口内每帧调用
        /// WeaponVisual::PerformCollisionCheck，把命中喂给 SetTargetData/Finalize 链路）。
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();

            if (!_bMeleeWindowOpen) return;

            var visual = GetAbilityWeaponVisualScript();
            if (visual != null)
            {
                visual.PerformCollisionCheck(_meleeFrameHits);
                if (_meleeFrameHits.Count > 0)
                {
                    var hits = ConvertMeleeHits(_meleeFrameHits);
                    _meleeFrameHits.Clear();
                    if (hits.Count > 0)
                    {
                        DispatchTargetData(hits, _currentApplicationTag);
                    }
                }
            }

            if (Time.GameTime >= _meleeWindowEndTime)
            {
                _bMeleeWindowOpen = false;
                visual.CleanupAttackData();
                EndAbility(_currentHandle, false);
            }
        }

        /// <summary>把 WeaponVisual 胶囊扫描的原始命中转换为战斗命中（保留物理材质用于倍率）。</summary>
        protected virtual List<CombatHitResult> ConvertMeleeHits(List<FlaxEngine.RayCastHit> rawHits)
        {
            var result = new List<CombatHitResult>();
            if (rawHits == null) return result;

            foreach (var hit in rawHits)
            {
                if (hit.Collider == null) continue;
                // 对应 UE5 近战 CQP 的 ignored actors：射手自身链条与同阵营角色不被误伤
                if (IsOwnedBySelf(hit.Collider) || IsFriendlyChain(hit.Collider)) continue;
                result.Add(new CombatHitResult
                {
                    bBlockingHit = true,
                    HitActor = hit.Collider,
                    ImpactPoint = hit.Point,
                    ImpactNormal = hit.Normal,
                    PhysicalMaterial = hit.Material
                });
            }
            return result;
        }

        // ===== 目标数据生成 =====

        /// <summary>使用追踪生成目标数据。命中时默认调用 HitTarget Attack Event。</summary>
        /// <param name="traceData">追踪数据。</param>
        /// <param name="traceStart">追踪起点。</param>
        /// <param name="applicationTag">应用标签（可空）。</param>
        public virtual void GenerateTargetDataUsingTrace(CombatTraceData traceData, Vector3 traceStart, GameplayTag applicationTag = default)
        {
            _currentTraceData = traceData;
            _currentTraceStart = traceStart;
            _currentApplicationTag = applicationTag.IsValid() ? applicationTag : GameplayTag.None;

            // 对应 UE5 GenerateTargetDataUsingTrace → FinalizeTargetData（NarrativeCombatAbility.cpp:109-112），
            // 并经 ASC.SetTargetData 走"投递→就绪回调"的同一条链路（等价 CallServerSetReplicatedTargetData）。
            DispatchTargetData(GetTargetDataUsingTrace(traceData, traceStart), applicationTag);
        }

        /// <summary>
        /// 投递目标数据：有活跃 ASC/委托时走 <see cref="NarrativeAbilitySystemComponent.SetTargetData"/>
        /// （触发 ActivateAbility 绑定的 FinalizeTargetData 回调），否则直接最终化。
        /// </summary>
        protected virtual void DispatchTargetData(List<CombatHitResult> targetData, GameplayTag applicationTag)
        {
            if (targetData == null || targetData.Count == 0) return;

            var asc = OwningASC;
            if (asc != null && _currentHandle.IsValid && _targetDataDelegateHandle != 0)
            {
                asc.SetTargetData(_currentHandle, targetData, applicationTag);
                return;
            }

            FinalizeTargetData(targetData, applicationTag);
        }

        /// <summary>使用追踪获取目标数据（不应用伤害）。</summary>
        public virtual List<CombatHitResult> GetTargetDataUsingTrace(CombatTraceData traceData, Vector3 traceStart)
        {
            var result = new List<CombatHitResult>();
            if (traceData == null) return result;

            // UE5 的散射是作用在瞄准"朝向"上（替换 TraceStart 的旋转），
            // 这里同样先取方向、再施加散射，最后按 TraceDistance 求终点。
            Vector3 aimDirection = Actor != null ? Actor.Direction : Vector3.Forward;
            if (aimDirection.LengthSquared < 0.0001f) aimDirection = Vector3.Forward;
            aimDirection = Vector3.Normalize(aimDirection);

            float spread = GetEffectiveWeaponSpread();
            if (spread > 0f)
            {
                aimDirection = ApplySpread(aimDirection, spread);
            }

            // 对应 UE5：TraceLen 还要加上 Avatar 到瞄准起点的距离，避免近身目标被漏掉
            float traceDistance = traceData.TraceDistance;
            if (Actor != null)
            {
                traceDistance += Vector3.Distance(Actor.Position, traceStart);
            }

            Vector3 traceEnd = traceStart + aimDirection * traceDistance;
            _currentTraceStart = traceStart;

            if (traceData.TraceRadius > 0f)
            {
                // SphereCast
                var hits = PerformTraceMulti(traceStart, traceEnd, traceData.TraceRadius);
                if (hits != null)
                {
                    foreach (var h in hits)
                    {
                        // UE5 用 CQP.AddIgnoredActor(own avatar) 排除自己；Flax 无忽略列表，结果侧过滤。
                        // 同阵营角色一并过滤（对应 UE5 队友 CQP 忽略），敌对目标不受影响。
                        if (h != null && h.bBlockingHit && h.HitActor != null &&
                            !IsOwnedBySelf(h.HitActor) && !IsFriendlyChain(h.HitActor)) result.Add(h);
                    }
                }
            }
            else
            {
                // RayCast
                var hit = PerformTrace(traceStart, traceEnd, traceData.TraceRadius);
                if (hit != null && hit.bBlockingHit && hit.HitActor != null &&
                    !IsOwnedBySelf(hit.HitActor) && !IsFriendlyChain(hit.HitActor))
                {
                    result.Add(hit);
                }
            }

            return result;
        }

        /// <summary>
        /// 测试 Actor 是否属于与施法者同阵营（Friendly）的角色 —— 对应 UE5 武器 CQP
        /// 对队友加入忽略列表：默认 NPC 不会误伤/射杀友军，敌对目标与无阵营的场景不受影响。
        /// 命中体常为角色子碰撞体，沿父链回溯到挂 ASC 的角色宿主再判定。
        /// </summary>
        protected bool IsFriendlyChain(Actor testActor)
        {
            if (testActor == null) return false;
            var caster = OwningASC != null ? OwningASC.Actor : Actor;
            if (caster == null) return false;

            var current = testActor;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                if (current.GetScript<NarrativeAbilitySystemComponent>() != null)
                {
                    return NarrativePro.Core.ArsenalStatics.GetAttitude(caster, current) ==
                           NarrativePro.Core.ArsenalStatics.ETeamAttitude.Friendly;
                }
                current = current.Parent;
            }
            return false;
        }

        /// <summary>测试 Actor 是否属于施法者（沿父链回溯到宿主 Actor/角色 Actor）。</summary>
        protected bool IsOwnedBySelf(Actor testActor)
        {
            if (testActor == null) return false;
            var selfActor = OwningASC != null ? OwningASC.Actor : Actor;
            var characterActor = CharacterOwner != null ? CharacterOwner.Actor : null;

            var current = testActor;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                if (selfActor != null && ReferenceEquals(current, selfActor)) return true;
                if (characterActor != null && ReferenceEquals(current, characterActor)) return true;
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// 最终化目标数据：记录应用标签、暂存数据并应用伤害/效果。
        /// 对应 UE5 <c>FinalizeTargetData(TargetDataHandle, ApplicationTag)</c>。
        /// </summary>
        public virtual void FinalizeTargetData(List<CombatHitResult> targetData, GameplayTag applicationTag)
        {
            if (applicationTag.IsValid()) _currentApplicationTag = applicationTag;
            _lastTargetData = targetData;

            // 把数据同步到 spec，使 ConsumeTargetData 之后仍能读到（对应 UE5 的 target data 缓存）
            var spec = OwningASC?.FindAbilitySpec(_currentHandle);
            if (spec != null && targetData != null && targetData.Count > 0)
            {
                spec.PendingTargetData = targetData;
            }

            if (targetData == null || targetData.Count == 0) return;
            HitTarget(targetData);
        }

        /// <summary>最近一次最终化的目标数据。</summary>
        public List<CombatHitResult> GetLastTargetData() => _lastTargetData;

        /// <summary>执行武器追踪（RayCast）。</summary>
        public virtual CombatHitResult PerformTrace(Vector3 start, Vector3 end, float sweepRadius)
        {
            var result = new CombatHitResult
            {
                TraceStart = start,
                TraceEnd = end
            };

            // Flax Physics.RayCast 返回 bool
            if (Physics.RayCast(start, end, out RayCastHit hit))
            {
                result.bBlockingHit = true;
                result.HitActor = hit.Collider;
                result.ImpactPoint = hit.Point;
                result.ImpactNormal = hit.Normal;
                result.PhysicalMaterial = hit.Material;
            }

            return result;
        }

        /// <summary>执行武器追踪（SphereCast 多命中）。
        /// 使用 Flax 原生 Physics.SphereCastAll API 进行球体扫描，获取路径上所有命中。
        /// </summary>
        public virtual List<CombatHitResult> PerformTraceMulti(Vector3 start, Vector3 end, float sweepRadius)
        {
            var result = new List<CombatHitResult>();
            var seenActors = new HashSet<Actor>();

            Vector3 dir = end - start;
            float dist = dir.Length;
            if (dist < 1f) return result;
            Vector3 forward = dir / dist;

            // 使用 Flax Physics.SphereCastAll 获取球体扫描的所有命中
            if (Physics.SphereCastAll(start, sweepRadius, forward, out RayCastHit[] hits, dist))
            {
                foreach (var hit in hits)
                {
                    var hitActor = hit.Collider;
                    if (hitActor != null && seenActors.Add(hitActor))
                    {
                        result.Add(new CombatHitResult
                        {
                            bBlockingHit = true,
                            HitActor = hitActor,
                            ImpactPoint = hit.Point,
                            ImpactNormal = hit.Normal,
                            PhysicalMaterial = hit.Material,
                            TraceStart = start,
                            TraceEnd = end
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>命中目标后处理（应用伤害）。</summary>
        public virtual void HitTarget(List<CombatHitResult> hits)
        {
            if (hits == null || hits.Count == 0) return;

            float attackDamage = GetAttackDamage();
            foreach (var hit in hits)
            {
                if (hit?.HitActor == null) continue;

                // 命中体可能是角色的子碰撞体：沿父链找承载 ASC 的角色 Actor
                var targetASC = FindASCForActor(hit.HitActor);
                if (targetASC == null || targetASC == OwningASC) continue;

                // 创建伤害 GameplayEffect 并应用（执行计算 "Damage" 在 ASC 内派发）
                var damageEffect = CreateDamageEffect(attackDamage);
                var spec = new GameplayEffectSpec(damageEffect, OwningASC);
                spec.SourceActor = OwningASC != null ? OwningASC.Actor : Actor;

                // 命中上下文：供 DamageExecCalc 取物理材质倍率（对应 UE5 EffectContext::HitResult）
                spec.Hit = hit;

                // 伤害类型标签随 Spec 投递（对应 UE5 CapturedSourceTags 里的 DamageType）
                if (_currentApplicationTag.IsValid())
                {
                    spec.DynamicAssetTags.AddTag(_currentApplicationTag);
                }

                targetASC.ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));
            }
        }

        /// <summary>沿父链查找挂载了 <see cref="NarrativeAbilitySystemComponent"/> 的 Actor。</summary>
        protected virtual NarrativeAbilitySystemComponent FindASCForActor(Actor from)
        {
            var current = from;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                var asc = current.GetScript<NarrativeAbilitySystemComponent>();
                if (asc != null) return asc;
                current = current.Parent;
            }
            return null;
        }

        /// <summary>
        /// 创建伤害效果。优先解析 <see cref="DamageEffectPath"/> 指向的 JSON 定义
        /// （<c>Content/NarrativePro/GAS/Effects/*.json</c>，带 <c>ExecuteCalcTypeId="Damage"</c>），
        /// 这样 ASC 的执行计算派发由数据驱动；再把本次攻击算出的伤害写进 Damage 修饰器。
        /// 定义缺失时回退程序化定义，保证攻击永远有落点。
        /// </summary>
        protected virtual GameplayEffect CreateDamageEffect(float damageAmount)
        {
            var resolved = string.IsNullOrEmpty(DamageEffectPath)
                ? null
                : NarrativeGASAssetRegistry.ResolveEffect(DamageEffectPath);
            if (resolved != null)
            {
                var clone = NarrativeGASAssetRegistry.CloneEffect(resolved);
                OverrideDamageMagnitude(clone, damageAmount);
                return clone;
            }

            return CreateFallbackDamageEffect(damageAmount);
        }

        /// <summary>把定义里的 Damage 修饰器量级换成本次攻击的实际伤害（没有则补一条）。</summary>
        protected virtual void OverrideDamageMagnitude(GameplayEffect effect, float damageAmount)
        {
            if (effect == null || effect.Modifiers == null) return;

            bool bFound = false;
            for (int i = 0; i < effect.Modifiers.Count; i++)
            {
                var mod = effect.Modifiers[i];
                if (mod == null || mod.AttributeName != "Damage") continue;
                mod.Magnitude = damageAmount;
                effect.Modifiers[i] = mod;
                bFound = true;
            }
            if (!bFound)
            {
                effect.Modifiers.Add(new GameplayModifierInfo
                {
                    AttributeName = "Damage",
                    ModifierOp = EGameplayModOp.Add,
                    Magnitude = damageAmount
                });
            }
        }

        /// <summary>程序化伤害效果定义（数据缺失时的兜底，与旧实现等价）。</summary>
        protected virtual GameplayEffect CreateFallbackDamageEffect(float damageAmount)
        {
            var effect = new GameplayEffect("Damage")
            {
                DurationType = EGameplayEffectDurationType.Instant,
                ExecuteCalcTypeId = "Damage"
            };
            effect.Modifiers.Add(new GameplayModifierInfo
            {
                AttributeName = "Damage",
                ModifierOp = EGameplayModOp.Add,
                Magnitude = damageAmount
            });
            return effect;
        }

        // ===== 查询方法 =====

        /// <summary>
        /// 获取攻击伤害值。
        /// <para>
        /// 顺序对应 UE5：武器数据（含弹药伤害覆盖）优先，重击再乘武器重击倍率；
        /// 没有武器来源时才读属性集的 AttackDamage，最后落到能力自身的默认值。
        /// 之前只读属性集，未装备属性效果时近战伤害恒为 0。
        /// </para>
        /// </summary>
        public virtual float GetAttackDamage()
        {
            var weapon = GetAbilityWeapon();
            if (weapon != null)
            {
                float weaponDamage = weapon.GetBaseAttackDamage();
                if (weaponDamage > 0f)
                {
                    // 重击倍率施加走纯规则层（NarrativeHeavyAttackRules.ApplyHeavyMultiplier），
                    // 倍率取自武器配置（UE5 WeaponItem.cpp:39 CDO 1.6 / 端口 WeaponItem.cs:106 默认 1.5，
                    // item_sword.json=1.5、item_bow.json=1.8 由资产覆写），harness 断言的是同一份实现。
                    return NarrativeHeavyAttackRules.ResolveWeaponAttackDamage(
                        weaponDamage, IsHeavyAttack, weapon.HeavyAttackDamageMultiplier);
                }
            }

            if (OwningASC?.AttributeSet != null)
            {
                // 使用属性集的 AttackDamage
                return OwningASC.AttributeSet.AttackDamage.CurrentValue;
            }
            return DefaultAttackDamage;
        }

        /// <summary>
        /// 应用武器散射（对应 UE5 <c>ApplyWeaponSpread</c>：
        /// 双持时固定用主手武器的散射，不维护两份散射）。
        /// </summary>
        public virtual Vector3 ApplyWeaponSpread(Vector3 aimDirection)
        {
            return ApplySpread(aimDirection, GetEffectiveWeaponSpread());
        }

        /// <summary>取实际生效的散射角度：装备了武器则用武器散射，否则用能力自身配置。</summary>
        protected virtual float GetEffectiveWeaponSpread()
        {
            var weapon = GetAbilityWeapon();
            float weaponSpread = weapon != null ? weapon.GetWeaponSpread() : 0f;
            return weaponSpread > 0f ? weaponSpread : WeaponSpread;
        }

        /// <summary>
        /// 在锥面上随机一个方向。对应 UE5 <c>ApplySpread</c>（NarrativeCombatAbility.cpp:363-392）：
        /// 半锥角 = Spread/2，用 Lyra 的正态分布随机锥（聚拢指数 1），
        /// 随机流按角色的 CharacterRandomSeed 播种，保证同一角色前后帧一致、不同角色互不相同。
        /// </summary>
        /// <param name="aimDirection">锥轴方向（瞄准方向）。</param>
        /// <param name="spread">全散射角（度）。</param>
        public virtual Vector3 ApplySpread(Vector3 aimDirection, float spread)
        {
            if (spread <= 0.0001f) return aimDirection;
            if (aimDirection.LengthSquared < 0.0001f) return aimDirection;

            float halfSpreadDegrees = spread * 0.5f;
            float coneHalfAngleRad = halfSpreadDegrees * Mathf.DegreesToRadians;

            return NarrativeGASStatics.VRandConeNormalDistribution(
                Vector3.Normalize(aimDirection), coneHalfAngleRad, 1f, GetSpreadRandomStream());
        }

        /// <summary>
        /// 取散射用的随机流。以角色随机种子播种并复用同一个实例，
        /// 等价 UE5 的 <c>FRandomStream Rand(RandSeed)</c>（种子来自 GetCharacterRandomSeed）。
        /// </summary>
        protected virtual System.Random GetSpreadRandomStream()
        {
            if (_spreadRandom != null && _spreadRandomSeed == GetSpreadSeed()) return _spreadRandom;

            _spreadRandomSeed = GetSpreadSeed();
            _spreadRandom = new System.Random(_spreadRandomSeed);
            return _spreadRandom;
        }

        private int GetSpreadSeed()
        {
            var character = CharacterOwner;
            int seed = character != null ? character.GetCharacterRandomSeed() : 1234;
            return seed != 0 ? seed : 1234;
        }

        private System.Random _spreadRandom;
        private int _spreadRandomSeed = 0;

        /// <summary>
        /// 取实际生效的散射角度（供旧调用点使用，保持既有公共签名可用）。
        /// </summary>
        public virtual float GetWeaponSpread() => GetEffectiveWeaponSpread();

        /// <summary>是否需要弹药（对应 UE5 <c>bRequiresAmmo</c>，并按所持武器数据放宽）。</summary>
        public bool RequiresAmmo() => IsAmmoRequiredFor(GetAbilityWeapon());

        /// <summary>
        /// 是否需要弹药（对应 UE5 <c>bRequiresAmmo</c>，并按武器数据放宽）。
        /// </summary>
        public virtual bool HasAmmo()
        {
            var weapon = GetAbilityWeapon();
            if (!IsAmmoRequiredFor(weapon)) return true;
            if (weapon != null) return weapon.HasAmmo();

            // 能力声明要弹药、却没有任何武器来源（例如未持握时按默认能力触发）
            return false;
        }

        /// <summary>
        /// 能否攻击：需要弹药时必须真的有弹。
        /// 对应 UE5 <c>UWeaponItem::CanAttack</c> 的弹药半边。
        /// <para>
        /// 注意：UE5 的 <c>UNarrativeCombatAbility::CanActivateAbility</c> 只是转发基类，
        /// 并没有把弹药作为激活门槛（弹药在攻击命中路径上才消耗），
        /// 所以这里不做激活拦截，只提供查询给表现层/装弹逻辑使用。
        /// </para>
        /// </summary>
        public virtual bool CanAttack() => HasAmmo();

        /// <summary>获取 Bot 攻击频率。</summary>
        public virtual float GetBotAttackFrequency() => DefaultBotAttackFrequency;

        /// <summary>获取 Bot 攻击范围。</summary>
        public virtual float GetBotAttackRange() => DefaultBotAttackRange;

        /// <summary>是否主手武器。</summary>
        public virtual bool IsMainhand() => bIsMainhand;

        // ===== 武器引用 =====

        /// <summary>
        /// 取授予本能力的武器。
        /// 对应 UE5 <c>GetAbilityWeapon</c>：走 AbilitySpec 的 SourceObject。
        /// 返回 null 表示无关联武器（徒手能力）。
        /// </summary>
        public virtual NarrativePro.Items.WeaponItem GetAbilityWeapon()
        {
            var asc = OwningASC;
            if (asc != null && _currentHandle.IsValid)
            {
                var spec = asc.FindAbilitySpec(_currentHandle);
                if (spec?.SourceObject is NarrativePro.Items.WeaponItem weaponFromSpec)
                {
                    return weaponFromSpec;
                }
            }

            // 授予路径没带来源对象时（例如角色默认能力），退回"当前持握的对应手数武器"
            return GetOwnerWieldedWeapon();
        }

        /// <summary>
        /// 取拥有者当前持握的武器（主手优先，副手兜底）。
        /// 对应 UE5 <c>GetOwnerEquippedWeapon</c>。
        /// </summary>
        protected virtual NarrativePro.Items.WeaponItem GetOwnerWieldedWeapon()
        {
            var actor = OwningASC != null ? OwningASC.Actor : Actor;
            if (actor == null) return null;

            var equipment = actor.GetScript<NarrativePro.Items.EquipmentComponent>();
            if (equipment == null) return null;

            var preferredSlot = bIsMainhand
                ? NarrativePro.Items.NarrativeGameplayTags.Equipment.WieldSlot_Mainhand
                : NarrativePro.Items.NarrativeGameplayTags.Equipment.WieldSlot_Offhand;

            var preferred = equipment.GetWieldedWeaponAtSlot(preferredSlot);
            if (preferred != null) return preferred;

            // 双持配置不完整时取任一持握中的武器
            foreach (var weapon in equipment.WieldedWeapons.Values)
            {
                if (weapon != null) return weapon;
            }
            return null;
        }

        /// <summary>取武器的弹药对象（武器没有弹药源时返回 null）。</summary>
        public virtual NarrativePro.Items.NarrativeItem GetAbilityWeaponAmmo()
        {
            var weapon = GetAbilityWeapon();
            if (weapon == null) return null;
            return weapon.GetAmmoSource();
        }

        /// <summary>
        /// 取武器视觉 Actor（主手或副手）。
        /// 对应 UE5 通过 NarrativeCharacterVisual 查询武器视觉：
        /// Flax 中武器视觉是挂在宿主 Actor 上的 <see cref="NarrativePro.Weapons.WeaponVisual"/> 脚本，
        /// 其上记录了来源武器，因此按武器反查即可。
        /// </summary>
        public virtual FlaxEngine.Actor GetAbilityWeaponVisual()
        {
            var weapon = GetAbilityWeapon();
            var actor = OwningASC != null ? OwningASC.Actor : Actor;
            if (weapon == null || actor == null) return null;

            foreach (var visual in actor.GetScripts<NarrativePro.Weapons.WeaponVisual>())
            {
                if (visual == null) continue;
                if (ReferenceEquals(visual.WeaponOwner, weapon)) return visual.Actor;
            }

            // 双持时按手侧兜底：主手取第一个非副手视觉，副手反之
            var wantedSlot = bIsMainhand
                ? NarrativePro.Items.NarrativeGameplayTags.Equipment.WieldSlot_Mainhand
                : NarrativePro.Items.NarrativeGameplayTags.Equipment.WieldSlot_Offhand;

            foreach (var visual in actor.GetScripts<NarrativePro.Weapons.WeaponVisual>())
            {
                if (visual == null) continue;
                if (visual.AttachState != null && visual.AttachState.WieldedSlot == wantedSlot) return visual.Actor;
            }

            // 视觉挂在武器预制体 Actor 上（角色 GetScripts 扫不到）时按活动实例登记表回退
            var registered = NarrativePro.Weapons.WeaponVisual.FindVisualForWeapon(weapon, actor);
            if (registered != null) return registered.Actor;

            return null;
        }

        /// <summary>
        /// 取武器视觉组件本体（拿 Actor 之外的信息，比如蒙太奇层/碰撞数据时用）。
        /// <para>
        /// 先看宿主 Actor 自身的脚本（旧口径），再回退 <see cref="NarrativePro.Weapons.WeaponVisual"/>
        /// 的活动实例登记表：武器视觉实际挂在持握时生成的武器预制体 Actor 上，
        /// 不是角色 Actor 的脚本，只扫角色会永远找不到，近战就只能走"无动画窗口"的兜底扇扫。
        /// </para>
        /// </summary>
        public NarrativePro.Weapons.WeaponVisual GetAbilityWeaponVisualScript()
        {
            var weapon = GetAbilityWeapon();
            var actor = OwningASC != null ? OwningASC.Actor : Actor;
            if (weapon == null || actor == null) return null;

            foreach (var visual in actor.GetScripts<NarrativePro.Weapons.WeaponVisual>())
            {
                if (visual != null && ReferenceEquals(visual.WeaponOwner, weapon)) return visual;
            }

            return NarrativePro.Weapons.WeaponVisual.FindVisualForWeapon(weapon, actor);
        }
    }
}
