using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 死亡事件委托。对应 UE5 FOnDied。
    /// </summary>
    /// <param name="killedActor">死亡的 Actor。</param>
    /// <param name="killedASC">死亡的 ASC。</param>
    public delegate void OnDiedDelegate(FlaxEngine.Actor killedActor, NarrativeAbilitySystemComponent killedASC);

    /// <summary>
    /// 治疗事件委托。对应 UE5 FOnHealedBy。
    /// </summary>
    /// <param name="healer">治疗者 ASC。</param>
    /// <param name="amount">治疗量。</param>
    public delegate void OnHealedByDelegate(NarrativeAbilitySystemComponent healer, float amount, GameplayEffectSpec spec);

    /// <summary>
    /// 受到伤害事件委托。对应 UE5 FOnDamagedBy。
    /// </summary>
    /// <param name="damagerASC">伤害源 ASC。</param>
    /// <param name="damage">伤害量。</param>
    /// <param name="spec">效果规格。</param>
    public delegate void OnDamagedByDelegate(NarrativeAbilitySystemComponent damagerASC, float damage, GameplayEffectSpec spec);

    /// <summary>
    /// 造成伤害事件委托。对应 UE5 FOnDealtDamage。
    /// </summary>
    /// <param name="damagedASC">被伤害 ASC。</param>
    /// <param name="damage">伤害量。</param>
    /// <param name="spec">效果规格。</param>
    public delegate void OnDealtDamageDelegate(NarrativeAbilitySystemComponent damagedASC, float damage, GameplayEffectSpec spec);

    /// <summary>
    /// 能力规格。对应 UE5 FGameplayAbilitySpec。
    /// 一个能力在 ASC 上的实例化数据。
    /// </summary>
    [Serializable]
    public class GameplayAbilitySpec
    {
        /// <summary>能力实例。</summary>
        public NarrativeGameplayAbility Ability;

        /// <summary>能力等级。</summary>
        public float Level = 1f;

        /// <summary>输入标签（绑定到具体输入）。</summary>
        public GameplayTag InputTag = GameplayTag.None;

        /// <summary>句柄 ID（每个 spec 唯一）。</summary>
        public int HandleId = 0;

        /// <summary>是否已激活。</summary>
        public bool bIsActive = false;

        /// <summary>
        /// 授予来源对象（通常是授予此能力的物品，如 WeaponItem）。
        /// 对应 UE5 FGameplayAbilitySpec::SourceObject。
        /// </summary>
        [NonSerialized]
        public object SourceObject;

        /// <summary>本规格是否已完成提交（Commit）。对应 UE5 bIsCommitted。</summary>
        public bool bIsCommitted = false;

        /// <summary>
        /// 最近一次已设置的目标数据（等待能力消费）。
        /// 对应 UE5 的 ClientReplicatedTargetData / AbilityTargetDataSetDelegate 载体。
        /// </summary>
        [NonSerialized]
        public List<CombatHitResult> PendingTargetData;

        public GameplayAbilitySpec() { }

        public GameplayAbilitySpec(NarrativeGameplayAbility ability, float level = 1f)
        {
            Ability = ability;
            Level = level;
        }
    }

    /// <summary>
    /// 能力规格句柄。对应 UE5 FGameplayAbilitySpecHandle。
    /// </summary>
    [Serializable]
    public struct GameplayAbilitySpecHandle
    {
        public int HandleId;

        public bool IsValid => HandleId != 0;

        public GameplayAbilitySpecHandle(int id) { HandleId = id; }

        public static readonly GameplayAbilitySpecHandle Invalid = new GameplayAbilitySpecHandle(0);
    }

    /// <summary>
    /// 攻击令牌。对应 UE5 FAttackToken。
    /// 令牌不做句柄化，直接指向持有者 NPC 控制器，简化管理。
    /// </summary>
    public class AttackToken
    {
        /// <summary>持有此令牌的 NPC 控制器。</summary>
        public NarrativePro.AI.NarrativeNPCController Owner;

        /// <summary>令牌被授予时的游戏时间（秒）。</summary>
        public float TokenGrantedTime;

        public AttackToken() { }

        public AttackToken(NarrativePro.AI.NarrativeNPCController inAttacker, float inTokenGrantedTime)
        {
            Owner = inAttacker;
            TokenGrantedTime = inTokenGrantedTime;
        }
    }

    /// <summary>
    /// 存档用的属性快照项。对应 UE5 FSavedAttribute。
    /// <para>成员必须带 setter：Flax/JSON 存档只还原有写入通道的成员。</para>
    /// </summary>
    [Serializable]
    public class SavedAttribute
    {
        /// <summary>属性名（如 "Health"）。</summary>
        public string AttributeName { get; set; } = "";

        /// <summary>属性值。</summary>
        public float Value { get; set; } = 0f;
    }

    /// <summary>
    /// Gameplay 事件负载。对应 UE5 FGameplayEventData。
    /// 简化点：移除 ContextHandle / 预测窗口，保留触发能力与回调所需的信息。
    /// </summary>
    public class GameplayEventData
    {
        /// <summary>事件标签。</summary>
        public GameplayTag EventTag = GameplayTag.None;

        /// <summary>发起者 Actor。</summary>
        public FlaxEngine.Actor Instigator;

        /// <summary>目标 Actor。</summary>
        public FlaxEngine.Actor Target;

        /// <summary>可选对象（对应 UE5 OptionalObject，例如造成伤害的效果定义）。</summary>
        public object OptionalObject;

        /// <summary>事件数值（对应 EventMagnitude，例如伤害量）。</summary>
        public float EventMagnitude;

        /// <summary>发起者标签集。</summary>
        public GameplayTagContainer InstigatorTags = new GameplayTagContainer();

        /// <summary>目标标签集。</summary>
        public GameplayTagContainer TargetTags = new GameplayTagContainer();

        /// <summary>造成伤害/治疗的效果规格（可能为 null）。</summary>
        [NonSerialized]
        public GameplayEffectSpec EffectSpec;
    }

    /// <summary>
    /// Narrativo Pro 能力系统组件。对应 UE5 UNarrativeAbilitySystemComponent。
    /// 管理角色能力、属性、效果、标签，是 GAS 的核心。
    /// 简化点：
    /// - 移除 UE5 复制/RPC（改为本地逻辑）
    /// - 移除网络预测/Local/Remote 区分
    /// - INarrativeSavableComponent 接口通过 PrepareForSave/Load 实现，
    ///   由 <c>NarrativeSaveSubsystem</c> 统一驱动（它会遍历 Actor 上实现该接口的脚本）
    /// - FGameplayAbilityTargetData 简化为基础结构
    /// - 伤害/治疗元属性通过 NarrativeAttributeSetBase.PostGameplayEffectExecute 应用
    /// </summary>
    public class NarrativeAbilitySystemComponent : Script, NarrativePro.SaveSystem.INarrativeSavableComponent
    {
        // ===== 配置字段 =====

        /// <summary>默认属性初始化效果路径（Instant 类型，设置 BaseValue）。</summary>
        public string DefaultAttributesEffectPath = "";

        /// <summary>启动时一次性应用的效果路径列表。</summary>
        public List<string> StartupEffectPaths = new List<string>();

        /// <summary>默认授予的能力路径列表。</summary>
        public List<string> DefaultAbilityPaths = new List<string>();

        /// <summary>能力等级。</summary>
        public int Level = 1;

        // ===== 运行时状态 =====

        /// <summary>属性集合（运行时实例）。</summary>
        public NarrativeAttributeSetBase AttributeSet;

        /// <summary>所有已授予的能力规格。</summary>
        public List<GameplayAbilitySpec> GrantedAbilities = new List<GameplayAbilitySpec>();

        /// <summary>所有激活的游戏效果。</summary>
        public List<ActiveGameplayEffect> ActiveEffects = new List<ActiveGameplayEffect>();

        /// <summary>拥有的标签容器（含效果授予的 + 基础）。</summary>
        public GameplayTagContainer GameplayTags = new GameplayTagContainer();

        /// <summary>
        /// GrantedTags 的挂/撤账本（纯层，见 NarrativeGrantedTagLedger.cs）。
        /// 授予记在激活效果句柄上，撤销按句柄取回同一批标签——重击标签"挂了撤不掉、
        /// 之后每次攻击都算重击"的缺陷就是靠这条记账堵住的，harness 驱动同一个实现。
        /// </summary>
        private readonly NarrativeGrantedTagLedger _grantedTagLedger = new NarrativeGrantedTagLedger();

        // ===== 存档（对应 UE5 AttributesToSave / SavedAttributes） =====

        /// <summary>
        /// 需要写入存档的属性名列表。
        /// 对应 UE5 <c>TArray&lt;FGameplayAttribute&gt; AttributesToSave</c>，
        /// Flax 中 FGameplayAttribute 以属性名字符串标识（见 NarrativeAttributeSetBase.GetAttribute）。
        /// </summary>
        public List<string> AttributesToSave = new List<string> { "XP", "Health", "MaxHealth", "Stamina", "MaxStamina" };

        /// <summary>
        /// PrepareForSave 产出的属性快照，由存档子系统随本脚本一起归档，Load 时还原。
        /// 对应 UE5 <c>TArray&lt;FSavedAttribute&gt; SavedAttributes</c>。
        /// </summary>
        public List<SavedAttribute> SavedAttributes = new List<SavedAttribute>();

        // ===== 攻击令牌（对应 UE5 Attack Tokens） =====

        /// <summary>本 ASC 作为被攻击目标时允许同时存在的攻击者数量上限（UE5 构造默认 10）。</summary>
        public int NumAttackTokens = 10;

        /// <summary>NPC 用来决定优先攻击谁；优先级高者先被攻击。</summary>
        public float AttackPriority = 1f;

        /// <summary>已授予的令牌。不做句柄化，令牌即指向持有者的指针 + 授予时间。</summary>
        public List<AttackToken> GrantedAttackTokens = new List<AttackToken>();

        // ===== 事件 =====

        /// <summary>死亡事件。在 server 和所有客户端触发。</summary>
        public event OnDiedDelegate OnDied;

        /// <summary>被治疗事件。</summary>
        public event OnHealedByDelegate OnHealedBy;

        /// <summary>受到伤害事件。</summary>
        public event OnDamagedByDelegate OnDamagedBy;

        /// <summary>造成伤害事件。</summary>
        public event OnDealtDamageDelegate OnDealtDamage;

        /// <summary>
        /// 死亡状态变化事件（对应 UE5 FOnDeathStateChanged）。
        /// 与 OnDied 的区别：本事件在复活时也会触发且带 bIsDead 状态，参数为（被杀 Actor, ASC, 是否死亡）。
        /// </summary>
        public event Action<FlaxEngine.Actor, NarrativeAbilitySystemComponent, bool> OnDeathStateChanged;

        // ===== 内部 =====

        private int _nextEffectHandleId = 1;
        private int _nextAbilityHandleId = 1;
        private bool _bIsDead = false;

        /// <summary>启动/默认属性效果是否已应用。用于忽略 BeginPlay 期 Health=0 触发的假死亡（对应 UE5 bStartupEffectsApplied）。</summary>
        protected bool bStartupEffectsApplied = false;

        /// <summary>
        /// 能力列表作用域锁计数。对应 UE5 ABILITYLIST_SCOPE_LOCK()。
        /// 大于 0 时结构性的能力增删（Give/Clear）会被排队到锁释放后执行，
        /// 避免遍历 ActivatableAbilities 时列表被改动。
        /// </summary>
        private int _abilityListScopeLockCount = 0;
        private readonly List<Action> _deferredAbilityListChanges = new List<Action>();

        /// <summary>事件标签 → 以该事件为触发源的能力句柄列表。对应 UE5 GameplayEventTriggeredAbilities。</summary>
        private readonly Dictionary<GameplayTag, List<GameplayAbilitySpecHandle>> _gameplayEventTriggeredAbilities
            = new Dictionary<GameplayTag, List<GameplayAbilitySpecHandle>>();

        /// <summary>事件标签 → 通用回调。对应 UE5 GenericGameplayEventCallbacks。</summary>
        private readonly Dictionary<GameplayTag, Action<GameplayEventData>> _genericGameplayEventCallbacks
            = new Dictionary<GameplayTag, Action<GameplayEventData>>();

        /// <summary>
        /// 能力规格 + 预测键 → 已设置的目标数据委托。
        /// 对应 UE5 AbilityTargetDataSetDelegate(SpecHandle, PredictionKey)。
        /// C# 无预测键，用 (句柄, 输入标签) 组合作为等价的键。
        /// </summary>
        private readonly Dictionary<string, List<Action<List<CombatHitResult>, GameplayTag>>> _targetDataSetCallbacks
            = new Dictionary<string, List<Action<List<CombatHitResult>, GameplayTag>>>();

        /// <summary>能力规格 + 预测键 → 最近一次已设置的目标数据（供 Consume 读取后清空）。</summary>
        private readonly Dictionary<string, List<CombatHitResult>> _storedTargetData
            = new Dictionary<string, List<CombatHitResult>>();

        /// <summary>目标数据委托句柄自增 id，用于 Unbind。</summary>
        private int _nextTargetDataDelegateId = 1;

        /// <summary>是否已死亡。</summary>
        public bool IsDead => _bIsDead;

        /// <summary>获取 Avatar Actor（UE 不暴露到 BP）。</summary>
        public FlaxEngine.Actor GetAvatarOwner() => Actor;

        /// <summary>获取属性集合。</summary>
        public NarrativeAttributeSetBase GetAttributeSet() => AttributeSet;

        /// <summary>
        /// 获取拥有本 ASC 的 Narrative 角色。对应 UE5 <c>GetCharacterOwner</c>。
        /// Flax 无 ACharacter 基类，这里以 Actor 上是否挂 <c>NarrativeCharacter</c> 脚本做等价判定；
        /// 非角色（如可破坏桶）返回 null。
        /// </summary>
        public virtual NarrativePro.UnrealFramework.NarrativeCharacter GetCharacterOwner()
        {
            return Actor?.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
        }

        /// <summary>启动/默认属性效果是否已应用。对应 UE5 <c>bStartupEffectsApplied</c>。</summary>
        public bool AreStartupEffectsApplied => bStartupEffectsApplied;

        /// <summary>
        /// 清掉启动标记并按当前配置重跑一次初始化。
        /// 用于宿主（如 <see cref="NarrativeASCActor"/>）在 AddScript 之后才把配置写进来的时序：
        /// 脚本 OnEnable 已经用空配置跑过一次，不重刷就会丢掉配置。
        /// </summary>
        public virtual void ReapplyStartupEffects()
        {
            bStartupEffectsApplied = false;
            InitializeAttributes();
        }

        /// <summary>
        /// 初始化属性集合，应用默认属性效果与启动效果，并授予默认能力。
        /// 对应 UE5 <c>InitializeAttributes</c> + <c>InitStartUpEffects</c>。
        /// 幂等：重复调用不会二次应用启动效果。
        /// </summary>
        public virtual void InitializeAttributes()
        {
            if (AttributeSet == null)
            {
                AttributeSet = Actor.GetScript<NarrativeAttributeSetBase>();
                if (AttributeSet == null)
                {
                    // Flax 中 Script 不能 AddChild，需用 AddScript 添加到 Actor
                    AttributeSet = Actor.AddScript<NarrativeAttributeSetBase>();
                }
            }
            if (AttributeSet != null)
            {
                AttributeSet.AttachToComponent(this);
            }

            if (bStartupEffectsApplied) return;
            bStartupEffectsApplied = true;
            // 应用默认属性效果（设置 BaseValue）
            if (!string.IsNullOrEmpty(DefaultAttributesEffectPath))
            {
                ApplyEffectByPath(DefaultAttributesEffectPath, "DefaultAttributesEffect");
            }

            // 应用启动效果
            if (StartupEffectPaths != null)
            {
                foreach (var path in StartupEffectPaths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        ApplyEffectByPath(path, "StartupEffect");
                    }
                }
            }

            // 初始化基础属性：未配置启动效果时按 Max 满值起步
            if (AttributeSet != null)
            {
                AttributeSet.Health.SetCurrentValue(AttributeSet.MaxHealth.CurrentValue);
                AttributeSet.Stamina.SetCurrentValue(AttributeSet.MaxStamina.CurrentValue);
                AttributeSet.NotifyAttributeChanged("Health", AttributeSet.Health.CurrentValue, 0f);
                AttributeSet.NotifyAttributeChanged("Stamina", AttributeSet.Stamina.CurrentValue, 0f);
            }

            // 授予默认能力（对应 UE5 GrantCharacterAbilities 走 AbilityConfig->DefaultAbilities）
            GrantDefaultAbilities();
        }

        /// <summary>
        /// 应用能力配置资产。对应 UE5 角色定义上的 <c>UAbilityConfiguration</c>：
        /// 把 <c>DefaultAttributesEffectPath / StartupEffectPaths / DefaultAbilityPaths</c> 合并进本 ASC 的
        /// 同名字段，并在启动效果已经应用过的场合立即补应用（幂等，重复调用不会二次扣属性）。
        /// </summary>
        public virtual bool ApplyAbilityConfiguration(AbilityConfiguration config)
        {
            if (config == null) return false;

            if (!string.IsNullOrEmpty(config.DefaultAttributesEffectPath) &&
                string.IsNullOrEmpty(DefaultAttributesEffectPath))
            {
                DefaultAttributesEffectPath = config.DefaultAttributesEffectPath;
            }

            if (config.StartupEffectPaths != null)
            {
                foreach (var path in config.StartupEffectPaths)
                {
                    if (string.IsNullOrEmpty(path) || (StartupEffectPaths != null && StartupEffectPaths.Contains(path))) continue;
                    (StartupEffectPaths ??= new List<string>()).Add(path);
                }
            }

            if (config.DefaultAbilityPaths != null)
            {
                foreach (var path in config.DefaultAbilityPaths)
                {
                    if (string.IsNullOrEmpty(path) || (DefaultAbilityPaths != null && DefaultAbilityPaths.Contains(path))) continue;
                    (DefaultAbilityPaths ??= new List<string>()).Add(path);
                }
            }

            if (bStartupEffectsApplied)
            {
                // 已经过了 BeginPlay 的启动窗口（例如输入绑定/换装后才拿到配置）：立即补应用
                if (!string.IsNullOrEmpty(config.DefaultAttributesEffectPath))
                {
                    ApplyEffectByPath(config.DefaultAttributesEffectPath, "DefaultAttributesEffect");
                }
                if (config.StartupEffectPaths != null)
                {
                    foreach (var path in config.StartupEffectPaths)
                    {
                        if (!string.IsNullOrEmpty(path)) ApplyEffectByPath(path, "StartupEffect");
                    }
                }
                GrantDefaultAbilities();
            }

            return true;
        }

        /// <summary>
        /// 按路径/ID 加载并应用能力配置（走 <see cref="NarrativeGASAssetRegistry.LoadAbilityConfiguration"/>）。
        /// </summary>
        public virtual bool ApplyAbilityConfiguration(string configurationPath)
        {
            if (string.IsNullOrEmpty(configurationPath)) return false;
            var config = NarrativeGASAssetRegistry.LoadAbilityConfiguration(configurationPath);
            if (config == null)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"{logTag} 能力配置未生效: {configurationPath}");
                return false;
            }
            return ApplyAbilityConfiguration(config);
        }

        private string logTag => "ApplyAbilityConfiguration";

        /// <summary>
        /// 按路径/ID 解析并应用一个效果到自身。
        /// 解析失败只记录警告，不抛异常——保持与 UE5 缺失软引用时的容错一致。
        /// </summary>
        protected virtual bool ApplyEffectByPath(string effectPath, string logLabel)
        {
            var effect = NarrativeGASAssetRegistry.ResolveEffect(effectPath);
            if (effect == null)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"{logLabel} 解析失败: {effectPath}");
                return false;
            }
            var spec = new GameplayEffectSpec(effect, this);
            ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));
            return true;
        }

        /// <summary>按 <see cref="DefaultAbilityPaths"/> 授予能力（已在列表中的能力不重复授予）。</summary>
        protected virtual void GrantDefaultAbilities()
        {
            if (DefaultAbilityPaths == null || DefaultAbilityPaths.Count == 0) return;

            if (_grantedDefaultAbilityPaths == null)
            {
                _grantedDefaultAbilityPaths = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var path in DefaultAbilityPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (!_grantedDefaultAbilityPaths.Add(path)) continue;
                GrantAbilityByPath(path);
            }
        }

        private HashSet<string> _grantedDefaultAbilityPaths;

        /// <summary>
        /// 按路径/ID 授予一个能力。对应 UE5 <c>AddAbility(TSubclassOf&lt;...&gt;, SourceObject)</c>。
        /// 返回无效句柄表示能力类型无法解析。
        /// <para>
        /// 输入标签解析顺序：调用方显式传入 → 能力实例自带（来自 <c>Abilities/*.json</c> 的 <c>inputTag</c>）
        /// → <see cref="NarrativeGASAssetRegistry.ResolveAbilityInputTag"/> 按路径再取一次。
        /// 三者都拿不到时保持 None，此时该能力只能由代码/事件激活，不会被按键触发。
        /// </para>
        /// </summary>
        public virtual GameplayAbilitySpecHandle GrantAbilityByPath(
            string abilityPath,
            object sourceObject = null,
            float level = 1f,
            GameplayTag inputTag = default)
        {
            if (string.IsNullOrEmpty(abilityPath)) return GameplayAbilitySpecHandle.Invalid;

            var ability = NarrativeGASAssetRegistry.CreateAbilityInstance(Actor, abilityPath);
            if (ability == null) return GameplayAbilitySpecHandle.Invalid;

            if (!inputTag.IsValid()) inputTag = ability.InputTag;
            if (!inputTag.IsValid()) inputTag = NarrativeGASAssetRegistry.ResolveAbilityInputTag(abilityPath);

            var handle = GiveAbility(ability, level, inputTag);
            if (handle.IsValid)
            {
                var spec = FindAbilitySpec(handle);
                if (spec != null) spec.SourceObject = sourceObject;
            }
            return handle;
        }

        /// <summary>
        /// 批量授予能力，返回句柄列表。对应 UE5 <c>ANarrativeCharacter::GrantAbilities</c>。
        /// <para>
        /// <paramref name="inputTags"/> 与 <paramref name="abilityPaths"/> 按下标对齐（可更长可更短，
        /// 缺位或 None 时按能力数据定义回退）。武器授予攻击能力就靠这条把
        /// "玩家按下的那个输入标签"写进 <c>spec.InputTag</c>，使 <see cref="AbilityInputTagPressed"/> 能命中。
        /// </para>
        /// </summary>
        public virtual List<GameplayAbilitySpecHandle> GrantAbilities(
            List<string> abilityPaths,
            object sourceObject = null,
            float level = 1f,
            List<GameplayTag> inputTags = null)
        {
            var handles = new List<GameplayAbilitySpecHandle>();
            if (abilityPaths == null) return handles;
            for (int i = 0; i < abilityPaths.Count; i++)
            {
                GameplayTag inputTag = GameplayTag.None;
                if (inputTags != null && i < inputTags.Count) inputTag = inputTags[i];
                var handle = GrantAbilityByPath(abilityPaths[i], sourceObject, level, inputTag);
                // 保持索引对齐：解析失败也占位（无效句柄），调用方可按位置回查
                handles.Add(handle);
            }
            return handles;
        }

        /// <summary>批量移除已授予能力。对应 UE5 <c>ANarrativeCharacter::RemoveAbilities</c>。</summary>
        public virtual void RemoveAbilities(List<GameplayAbilitySpecHandle> handles)
        {
            if (handles == null) return;
            foreach (var handle in handles)
            {
                ClearAbility(handle);
            }
        }

        /// <summary>授予能力。</summary>
        /// <param name="ability">能力实例。</param>
        /// <param name="level">等级。</param>
        /// <param name="inputTag">输入标签（可空）。</param>
        /// <returns>能力规格句柄。</returns>
        public virtual GameplayAbilitySpecHandle GiveAbility(NarrativeGameplayAbility ability, float level = 1f, GameplayTag inputTag = default)
        {
            if (ability == null) return GameplayAbilitySpecHandle.Invalid;

            // 同一 Actor 上同类型能力只保留一个规格，避免反复穿戴装备造成 spec 泄漏
            foreach (var existing in GrantedAbilities)
            {
                if (ReferenceEquals(existing.Ability, ability))
                {
                    existing.Level = level;
                    if (inputTag.IsValid()) existing.InputTag = inputTag;
                    return new GameplayAbilitySpecHandle(existing.HandleId);
                }
            }

            var spec = new GameplayAbilitySpec(ability, level)
            {
                HandleId = _nextAbilityHandleId++,
                InputTag = inputTag.IsValid() ? inputTag : GameplayTag.None
            };
            GrantedAbilities.Add(spec);
            ability.OwningASC = this;

            // 以事件为触发源的能力登记到事件表（对应 UE5 GameplayEventTriggeredAbilities）
            RegisterGameplayEventTriggers(spec);

            ability.OnAbilityGranted();

            // 对应 UE5 FGameplayAbilitySpec::ActiveActiveHandle → OnAvatarSet：
            // 授予即设定 Avatar，装备驱动的能力可据此在授予时自动激活
            ability.OnAvatarSet(this, spec);

            return new GameplayAbilitySpecHandle(spec.HandleId);
        }

        /// <summary>移除一个能力规格（先取消运行中的实例）。对应 UE5 ClearAbility。</summary>
        public virtual void ClearAbility(GameplayAbilitySpecHandle handle)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null) return;

            if (spec.bIsActive && spec.Ability != null)
            {
                spec.Ability.CancelAbility();
            }
            spec.bIsActive = false;
            spec.bIsCommitted = false;

            UnregisterGameplayEventTriggers(spec);
            ClearTargetDataForSpec(handle);
            if (spec.Ability != null) spec.Ability.OnAvatarClear();

            // 作用域锁期间不能真的改动列表（对应 ABILITYLIST_SCOPE_LOCK 语义）
            DeferAbilityListChange(() => GrantedAbilities.Remove(spec));
        }

        /// <summary>移除全部能力。对应 UE5 ClearAllAbilities。</summary>
        public virtual void ClearAllAbilities()
        {
            var snapshot = new List<GameplayAbilitySpec>(GrantedAbilities);
            foreach (var spec in snapshot)
            {
                ClearAbility(new GameplayAbilitySpecHandle(spec.HandleId));
            }
        }

        /// <summary>
        /// 移除绑定到指定输入/标签的全部能力。
        /// 对应 UE5 <c>ClearAbilitiesWithTag</c>（武器收起时按输入标签清掉其能力）。
        /// </summary>
        public virtual void ClearAbilitiesWithTag(GameplayTag inputTag)
        {
            if (!inputTag.IsValid()) return;

            var outSpecs = new List<GameplayAbilitySpecHandle>();
            FindAbilitiesWithTag(inputTag, outSpecs);
            foreach (var handle in outSpecs)
            {
                ClearAbility(handle);
            }

            // UE5 按 DynamicSpecSourceTags 匹配；这里额外支持能力自身声明的标签，
            // 使武器只配了 AbilityTags 时也能被正确清掉。
            var byAbilityTag = new List<GameplayAbilitySpecHandle>();
            foreach (var spec in GrantedAbilities)
            {
                if (spec?.Ability == null) continue;
                if (spec.Ability.AbilityTags != null && spec.Ability.AbilityTags.HasTag(inputTag))
                {
                    byAbilityTag.Add(new GameplayAbilitySpecHandle(spec.HandleId));
                }
            }
            foreach (var handle in byAbilityTag)
            {
                ClearAbility(handle);
            }
        }

        /// <summary>
        /// 按授予来源对象移除能力。对应 UE5 武器用 WeaponAbilityHandles 精确回滚的等价实现，
        /// 供 WeaponItem.RemoveWeaponAbilities 在句柄丢失时兜底。
        /// </summary>
        public virtual int ClearAbilitiesFromSource(object sourceObject)
        {
            if (sourceObject == null) return 0;
            var handles = new List<GameplayAbilitySpecHandle>();
            foreach (var spec in GrantedAbilities)
            {
                if (spec != null && ReferenceEquals(spec.SourceObject, sourceObject))
                {
                    handles.Add(new GameplayAbilitySpecHandle(spec.HandleId));
                }
            }
            foreach (var handle in handles)
            {
                ClearAbility(handle);
            }
            return handles.Count;
        }

        /// <summary>通过句柄查找能力规格。</summary>
        public virtual GameplayAbilitySpec FindAbilitySpec(GameplayAbilitySpecHandle handle)
        {
            if (!handle.IsValid) return null;
            foreach (var spec in GrantedAbilities)
            {
                if (spec.HandleId == handle.HandleId) return spec;
            }
            return null;
        }

        /// <summary>查找绑定到指定输入标签的所有能力规格。</summary>
        public virtual void FindAbilitiesWithTag(GameplayTag inputTag, List<GameplayAbilitySpecHandle> outAbilitySpecs)
        {
            if (outAbilitySpecs == null) return;
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag)
                {
                    outAbilitySpecs.Add(new GameplayAbilitySpecHandle(spec.HandleId));
                }
            }
        }

        /// <summary>通过句柄激活能力。对应 UE5 TryActivateAbilityHandle：先 CheckCost，再 Commit，再 Activate。</summary>
        public virtual bool TryActivateAbility(GameplayAbilitySpecHandle handle)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null || spec.Ability == null) return false;
            if (spec.bIsActive) return false;

            // 检查能力是否可激活（Cost/Cooldown/Tag 检查，Cost 通过 Ability.CanAffordCost 验证）
            if (!spec.Ability.CanActivateAbility(this))
            {
                return false;
            }

            // 提交（Commit）：扣除 Cost、启动 Cooldown，之后才允许真正激活。
            // 对应 UE5 TryActivateAbilityHandle 内的 CommitAbility(CheckCost=true, CheckCooldown=true)。
            if (!CommitAbility(handle, spec))
            {
                return false;
            }

            spec.bIsActive = true;
            spec.Ability.OwningASC = this;
            spec.Ability.ActivateAbility(handle, spec);
            return true;
        }

        /// <summary>
        /// 提交能力：校验并扣除消耗、应用冷却。
        /// 对应 UE5 <c>UGameplayAbility::CommitAbility</c> + <c>CommitExecute</c>。
        /// 成功后 spec 标记为已提交，EndAbility 时复位。
        /// </summary>
        public virtual bool CommitAbility(GameplayAbilitySpecHandle handle, GameplayAbilitySpec spec)
        {
            if (spec == null) spec = FindAbilitySpec(handle);
            if (spec == null || spec.Ability == null) return false;
            if (spec.bIsCommitted) return true;

            // Cost 校验（不足则提交失败，能力不激活）
            if (!spec.Ability.CanAffordCostPublic(this)) return false;

            spec.bIsCommitted = true;
            spec.Ability.CommitExecute(handle, spec);
            return true;
        }

        /// <summary>撤销提交（回滚 Cost/Cooldown）。对应 UE5 CancelAbility 的 bReplicateCancelAbility 分支。</summary>
        public virtual void UncommitAbility(GameplayAbilitySpecHandle handle)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null || !spec.bIsCommitted) return;
            spec.bIsCommitted = false;
            spec.Ability?.TryUncommit();
        }

        // （GameplayAbilitySpecHandle.IsValid 是属性，不是方法，下文使用时不应加括号）

        /// <summary>通过输入标签激活能力。</summary>
        public virtual void AbilityInputTagPressed(GameplayTag inputTag)
        {
            if (!inputTag.IsValid()) return;
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag && !spec.bIsActive)
                {
                    var handle = new GameplayAbilitySpecHandle(spec.HandleId);
                    if (handle.IsValid)
                    {
                        TryActivateAbility(handle);
                    }
                }
            }
        }

        /// <summary>通过输入标签释放能力。</summary>
        public virtual void AbilityInputTagReleased(GameplayTag inputTag)
        {
            if (!inputTag.IsValid()) return;
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag && spec.bIsActive && spec.Ability != null)
                {
                    spec.Ability.InputTagReleased(inputTag);
                }
            }
        }

        /// <summary>取消能力。</summary>
        public virtual void CancelAbility(GameplayAbilitySpecHandle handle)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null || spec.Ability == null) return;
            if (!spec.bIsActive) return;

            spec.bIsActive = false;
            spec.bIsCommitted = false;
            spec.Ability.CancelAbility();
        }

        /// <summary>
        /// 结束能力（由能力本身调用）。
        /// 对应 UE5 EndAbility 的收尾：复位激活/提交标记、清掉残留目标数据。
        /// </summary>
        public virtual void EndAbility(GameplayAbilitySpecHandle handle, bool bWasCancelled = false)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null) return;
            spec.bIsActive = false;
            spec.bIsCommitted = false;
            spec.PendingTargetData = null;
        }

        // ===== 能力列表作用域锁（对应 UE5 ABILITYLIST_SCOPE_LOCK） =====

        /// <summary>
        /// 进入能力列表作用域锁。锁期间的 Give/Clear 结构性改动会被延后到锁释放。
        /// 返回 IDisposable，配合 using 使用（等价 UE5 的宏）。
        /// </summary>
        public IDisposable BeginAbilityListScopeLock()
        {
            return new AbilityListScopeLock(this);
        }

        /// <summary>当前能力列表锁深度。&gt;0 表示结构性改动会被延后。</summary>
        public int AbilityListScopeLockCount => _abilityListScopeLockCount;

        private void AcquireAbilityListScopeLock()
        {
            _abilityListScopeLockCount++;
        }

        private void ReleaseAbilityListScopeLock()
        {
            if (_abilityListScopeLockCount > 0) _abilityListScopeLockCount--;

            if (_abilityListScopeLockCount == 0 && _deferredAbilityListChanges.Count > 0)
            {
                // 先取快照再执行：延后项自身可能再次进入锁
                var pending = new List<Action>(_deferredAbilityListChanges);
                _deferredAbilityListChanges.Clear();
                foreach (var action in pending)
                {
                    action?.Invoke();
                }
            }
        }

        /// <summary>登记一个需要在锁释放后执行的列表改动。</summary>
        private void DeferAbilityListChange(Action change)
        {
            if (change == null) return;
            if (_abilityListScopeLockCount > 0)
            {
                _deferredAbilityListChanges.Add(change);
                return;
            }
            change.Invoke();
        }

        private sealed class AbilityListScopeLock : IDisposable
        {
            private NarrativeAbilitySystemComponent _asc;

            public AbilityListScopeLock(NarrativeAbilitySystemComponent asc)
            {
                _asc = asc;
                _asc?.AcquireAbilityListScopeLock();
            }

            public void Dispose()
            {
                var asc = _asc;
                _asc = null;
                asc?.ReleaseAbilityListScopeLock();
            }
        }

        // ===== Gameplay 事件（对应 UE5 HandleGameplayEvent） =====

        /// <summary>
        /// 注册一个「以该 gameplay 事件为触发源」的能力。
        /// 对应 UE5 能力配置 TriggerSource = GameplayEvent 时的注册表写入。
        /// </summary>
        public virtual void RegisterAbilityGameplayEventTrigger(GameplayTag eventTag, GameplayAbilitySpecHandle handle)
        {
            if (!eventTag.IsValid() || !handle.IsValid) return;
            if (!_gameplayEventTriggeredAbilities.TryGetValue(eventTag, out var list))
            {
                list = new List<GameplayAbilitySpecHandle>();
                _gameplayEventTriggeredAbilities[eventTag] = list;
            }
            if (!list.Contains(handle)) list.Add(handle);
        }

        /// <summary>注销通用事件回调。对应 UE5 GenericGameplayEventCallbacks 的反注册。</summary>
        public virtual bool UnregisterGenericGameplayEventCallback(GameplayTag eventTag)
        {
            if (!eventTag.IsValid()) return false;
            return _genericGameplayEventCallbacks.Remove(eventTag);
        }

        /// <summary>注册通用事件回调。同一标签重复注册会替换旧回调（保持 UE5 单播语义的简化）。</summary>
        public virtual void RegisterGenericGameplayEventCallback(GameplayTag eventTag, Action<GameplayEventData> callback)
        {
            if (!eventTag.IsValid() || callback == null) return;
            _genericGameplayEventCallbacks[eventTag] = callback;
        }

        private void RegisterGameplayEventTriggers(GameplayAbilitySpec spec)
        {
            if (spec?.Ability == null) return;
            var triggers = spec.Ability.TriggerOnGameplayEventTags;
            if (triggers == null) return;
            foreach (var tag in triggers.GetTags())
            {
                RegisterAbilityGameplayEventTrigger(new GameplayTag(tag), new GameplayAbilitySpecHandle(spec.HandleId));
            }
        }

        private void UnregisterGameplayEventTriggers(GameplayAbilitySpec spec)
        {
            if (spec?.Ability == null) return;
            var triggers = spec.Ability.TriggerOnGameplayEventTags;
            if (triggers == null) return;
            foreach (var tag in triggers.GetTags())
            {
                var gameplayTag = new GameplayTag(tag);
                if (_gameplayEventTriggeredAbilities.TryGetValue(gameplayTag, out var list))
                {
                    list.RemoveAll(h => h.HandleId == spec.HandleId);
                    if (list.Count == 0) _gameplayEventTriggeredAbilities.Remove(gameplayTag);
                }
            }
        }

        /// <summary>
        /// 广播一个 gameplay 事件：触发以该事件为触发源的能力，并调用注册的事件回调。
        /// 对应 UE5 <c>HandleGameplayEvent</c>。
        /// <para>
        /// UE5 基类会把整个「事件委托 + 能力触发」包在 ABILITYLIST_SCOPE_LOCK 里，
        /// 导致委托回调内收/放武器时改能力列表被阻塞。Narrative 的重写把锁收窄到只覆盖能力触发段，
        /// 委托广播放在锁外——这里保持同一语义。
        /// </para>
        /// </summary>
        /// <returns>被成功触发的能力数量。</returns>
        public virtual int HandleGameplayEvent(GameplayTag eventTag, GameplayEventData payload)
        {
            if (!eventTag.IsValid()) return 0;

            int triggeredCount = 0;
            GameplayTag currentTag = eventTag;

            // 锁只覆盖能力触发段（对应 UE5 重写里 ABILITYLIST_SCOPE_LOCK 的位置）
            using (BeginAbilityListScopeLock())
            {
                while (currentTag.IsValid())
                {
                    if (_gameplayEventTriggeredAbilities.TryGetValue(currentTag, out var triggeredHandles) && triggeredHandles.Count > 0)
                    {
                        // 复制一份：触发过程中能力可能被清掉
                        var snapshot = new List<GameplayAbilitySpecHandle>(triggeredHandles);
                        foreach (var handle in snapshot)
                        {
                            if (TriggerAbilityFromGameplayEvent(handle, eventTag, payload))
                            {
                                triggeredCount++;
                            }
                        }
                    }

                    currentTag = RequestDirectParent(currentTag);
                }
            }

            // 事件回调在锁外广播，允许其自由增删能力（这正是 Narrative 重写要解决的问题）
            if (payload == null) payload = new GameplayEventData { EventTag = eventTag };
            payload.EventTag = eventTag;

            if (_genericGameplayEventCallbacks.TryGetValue(eventTag, out var callback) && callback != null)
            {
                // 复制再广播，防止回调内部反注册把正在遍历的委托改坏
                var delegateCopy = callback;
                delegateCopy.Invoke(payload);
            }

            return triggeredCount;
        }

        /// <summary>
        /// 由 gameplay 事件触发一个能力。对应 UE5 TriggerAbilityFromGameplayEvent。
        /// 触发前把负载写入能力，让能力内可以读到事件源/目标/数值。
        /// </summary>
        public virtual bool TriggerAbilityFromGameplayEvent(GameplayAbilitySpecHandle handle, GameplayTag eventTag, GameplayEventData payload)
        {
            var spec = FindAbilitySpec(handle);
            if (spec == null || spec.Ability == null) return false;
            if (spec.bIsActive) return false;
            if (IsDead) return false;

            if (!spec.Ability.CanActivateAbility(this)) return false;

            spec.Ability.SetTriggerEventData(payload);

            if (!CommitAbility(handle, spec)) return false;

            spec.bIsActive = true;
            spec.Ability.OwningASC = this;
            spec.Ability.ActivateAbility(handle, spec);
            return true;
        }

        /// <summary>
        /// 取父标签（去掉最后一段）。对应 UE5 <c>FGameplayTag::RequestDirectParent</c>。
        /// 事件沿层级向上冒泡，父级注册的能力能被子级事件触发。
        /// </summary>
        public static GameplayTag RequestDirectParent(GameplayTag tag)
        {
            if (!tag.IsValid()) return GameplayTag.None;
            int lastDot = tag.TagName.LastIndexOf('.');
            if (lastDot <= 0) return GameplayTag.None;
            return new GameplayTag(tag.TagName.Substring(0, lastDot));
        }

        // ===== 目标数据（对应 UE5 AbilityTargetDataSetDelegate / ConsumeClientReplicatedTargetData） =====

        /// <summary>能力规格与预测键的组合键。C# 无预测键，用句柄即可。</summary>
        private static string TargetDataKey(GameplayAbilitySpecHandle handle) => "spec_" + handle.HandleId;

        /// <summary>
        /// 绑定「目标数据就绪」回调。对应 UE5
        /// <c>AbilityTargetDataSetDelegate(SpecHandle, PredictionKey).AddUObject(...)</c>。
        /// 返回句柄用于解绑（0 表示无效）。
        /// </summary>
        public virtual int BindTargetDataSetDelegate(GameplayAbilitySpecHandle handle, Action<List<CombatHitResult>, GameplayTag> callback)
        {
            if (!handle.IsValid || callback == null) return 0;
            string key = TargetDataKey(handle);
            if (!_targetDataSetCallbacks.TryGetValue(key, out var list))
            {
                list = new List<Action<List<CombatHitResult>, GameplayTag>>();
                _targetDataSetCallbacks[key] = list;
            }
            list.Add(callback);

            // 已有暂存数据立即派发（等价 UE5 数据先到、绑定后补发的竞态处理）
            if (_storedTargetData.TryGetValue(key, out var stored) && stored != null)
            {
                callback(stored, GameplayTag.None);
            }

            return _nextTargetDataDelegateId++;
        }

        /// <summary>解绑目标数据回调。对应 UE5 <c>.Remove(DelegateHandle)</c>。</summary>
        public virtual void UnbindTargetDataSetDelegate(GameplayAbilitySpecHandle handle)
        {
            if (!handle.IsValid) return;
            _targetDataSetCallbacks.Remove(TargetDataKey(handle));
        }

        /// <summary>
        /// 设置目标数据并派发绑定的回调。对应 UE5 <c>SetClientInputTargetData</c> 的服务端等价物。
        /// </summary>
        public virtual void SetTargetData(GameplayAbilitySpecHandle handle, List<CombatHitResult> targetData, GameplayTag applicationTag = default)
        {
            if (!handle.IsValid) return;
            string key = TargetDataKey(handle);

            if (targetData != null && targetData.Count > 0)
            {
                _storedTargetData[key] = targetData;
            }

            if (_targetDataSetCallbacks.TryGetValue(key, out var callbacks) && callbacks != null && callbacks.Count > 0)
            {
                var snapshot = new List<Action<List<CombatHitResult>, GameplayTag>>(callbacks);
                foreach (var cb in snapshot)
                {
                    cb?.Invoke(targetData, applicationTag);
                }
            }
        }

        /// <summary>
        /// 消费已复制的目标数据：取出并清空暂存。
        /// 对应 UE5 <c>ConsumeClientReplicatedTargetData</c>。
        /// </summary>
        public virtual List<CombatHitResult> ConsumeTargetData(GameplayAbilitySpecHandle handle)
        {
            if (!handle.IsValid) return null;
            string key = TargetDataKey(handle);
            if (_storedTargetData.TryGetValue(key, out var data))
            {
                _storedTargetData.Remove(key);
                return data;
            }
            return null;
        }

        /// <summary>能力结束时清掉其目标数据与委托。对应 UE5 EndAbility 内的清理。</summary>
        public virtual void ClearTargetDataForSpec(GameplayAbilitySpecHandle handle)
        {
            if (!handle.IsValid) return;
            string key = TargetDataKey(handle);
            _storedTargetData.Remove(key);
            _targetDataSetCallbacks.Remove(key);
        }

        // ===== 攻击令牌（对应 UE5 Attack Tokens） =====

        /// <summary>
        /// NPC 控制器想要攻击本 ASC 拥有者时调用，尝试占用一个攻击槽。
        /// 满槽时会尝试从现有持有者手里抢夺一个。
        /// 裁决本体在纯层 <see cref="NarrativeAttackTokenLedger.Decide"/>（对应 UE5
        /// <c>TryClaimToken</c>，NarrativeAbilitySystemComponent.cpp:400-462），本方法负责
        /// 取引擎侧事实（距离/时间/持有者存活）与执行裁决（通知被抢者 + 归还 + 授予）。
        /// </summary>
        public virtual bool TryClaimToken(NarrativePro.AI.NarrativeNPCController claimer)
        {
            var claimerPawn = claimer != null ? claimer.GetControlledNPC() : null;
            FlaxEngine.Actor ourActor = GetAvatarOwner();
            var settings = NarrativePro.Settings.NarrativeCombatDeveloperSettings.Instance;

            float distSqFromClaimer = claimerPawn != null && ourActor != null
                ? Vector3.DistanceSquared(claimerPawn.Position, ourActor.Position)
                : 0f;

            var holders = SnapshotHolders();
            var decision = NarrativeAttackTokenLedger.Decide(
                GetNumAttackTokens(),
                holders,
                claimer != null,
                claimerPawn != null,
                ourActor != null,
                distSqFromClaimer,
                Time.GameTime,
                ToStealSettings(settings),
                settings != null);

            if (decision.Result != EAttackTokenClaim.Granted) return false;

            if (decision.StealIndex != NarrativeAttackTokenLedger.NoToken)
            {
                var stolen = GrantedAttackTokens[decision.StealIndex];
                // 通知被抢者：其 AI 需要放弃当前攻击并重新选目标（UE5:446 在归还之前调用）。
                stolen?.Owner?.TokenStolen();
                ReturnTokenAtIndex(decision.StealIndex);
            }

            // 再校验一次槽位，然后授予令牌（UE5:452-458）。
            if (GrantedAttackTokens.Count < GetNumAttackTokens())
            {
                GrantedAttackTokens.Add(new AttackToken(claimer, Time.GameTime));
                claimer.GrantedToken = this;
                return true;
            }

            return false;
        }

        /// <summary>把当前账本折成纯层视图（含每个持有者到本 ASC 拥有者的距离平方）。</summary>
        private List<AttackTokenHolderView> SnapshotHolders()
        {
            var ourActor = GetAvatarOwner();
            var result = new List<AttackTokenHolderView>(GrantedAttackTokens.Count);
            for (int i = 0; i < GrantedAttackTokens.Count; i++)
            {
                result.Add(ToHolderView(GrantedAttackTokens[i], ourActor));
            }
            return result;
        }

        /// <summary>单个令牌 → 纯层持有者视图。</summary>
        private AttackTokenHolderView ToHolderView(AttackToken token, FlaxEngine.Actor ourActor)
        {
            var view = new AttackTokenHolderView();
            if (token == null)
            {
                view.bOwnerMissing = true;
                return view;
            }

            view.OwnerKey = token.Owner;
            view.GrantedTime = token.TokenGrantedTime;

            var owner = token.Owner;
            if (owner == null)
            {
                view.bOwnerMissing = true;
                return view;
            }

            view.bOwnerDead = !owner.IsAlive();
            var pawn = owner.GetControlledNPC();
            view.bOwnerHasNoPawn = pawn == null;
            view.DistanceSqToVictim = pawn != null && ourActor != null
                ? Vector3.DistanceSquared(pawn.Position, ourActor.Position)
                : 0f;
            return view;
        }

        /// <summary>战斗设置 → 纯层抢夺参数（设置为空时返回零值，调用方须禁用抢夺扫描）。</summary>
        internal static AttackTokenStealSettings ToStealSettings(NarrativePro.Settings.NarrativeCombatDeveloperSettings settings)
        {
            if (settings == null) return new AttackTokenStealSettings();
            return new AttackTokenStealSettings
            {
                TokenStealableAgeSeconds = settings.TokenStealableAgeSeconds,
                StealTokenProximity = settings.StealTokenProximity,
            };
        }

        /// <summary>按下标归还令牌，永不失败。对应 UE5 <c>ReturnTokenAtIndex</c>。</summary>
        public virtual void ReturnTokenAtIndex(int index)
        {
            if (index < 0 || index >= GrantedAttackTokens.Count) return;

            var token = GrantedAttackTokens[index];
            if (token != null && token.Owner != null)
            {
                token.Owner.GrantedToken = null;
            }

            GrantedAttackTokens.RemoveAt(index);
        }

        /// <summary>归还指定持有者的令牌（下标由纯层 <see cref="NarrativeAttackTokenLedger.FindIndexForOwner"/> 给出）。对应 UE5 <c>ReturnToken</c>。</summary>
        public virtual void ReturnToken(NarrativePro.AI.NarrativeNPCController returner)
        {
            if (returner == null) return;

            int foundIdx = NarrativeAttackTokenLedger.FindIndexForOwner(SnapshotHolders(), returner);
            if (foundIdx >= 0)
            {
                ReturnTokenAtIndex(foundIdx);
            }
        }

        /// <summary>
        /// 是否应无条件立即抢夺该令牌（持有者无效/已死/已失去 Pawn）。
        /// 判定本体在 <see cref="NarrativeAttackTokenLedger.ShouldImmediatelyStealToken"/>。
        /// </summary>
        public virtual bool ShouldImmediatelyStealToken(AttackToken token)
        {
            return NarrativeAttackTokenLedger.ShouldImmediatelyStealToken(ToHolderView(token, GetAvatarOwner()));
        }

        /// <summary>
        /// 判定抢夺者能否抢走现有持有者的令牌，并给出抢夺评分（评分高者优先被抢）。
        /// 判定本体在 <see cref="NarrativeAttackTokenLedger.CanStealToken"/>：令牌「够老」或抢夺者明显更近时成立。
        /// </summary>
        public virtual bool CanStealToken(
            NarrativePro.AI.NarrativeNPCController stealer,
            AttackToken existingToken,
            out float stealScore)
        {
            stealScore = 0f;

            var settings = NarrativePro.Settings.NarrativeCombatDeveloperSettings.Instance;
            if (settings == null) return false;

            if (stealer == null || existingToken == null || existingToken.Owner == null) return false;

            var stealerPawn = stealer.GetControlledNPC();
            FlaxEngine.Actor ourActor = GetAvatarOwner();
            if (stealerPawn == null || ourActor == null) return false;

            float distFromStealer = Vector3.DistanceSquared(stealerPawn.Position, ourActor.Position);
            return NarrativeAttackTokenLedger.CanStealToken(
                ToHolderView(existingToken, ourActor),
                distFromStealer,
                Time.GameTime,
                ToStealSettings(settings),
                out stealScore);
        }

        /// <summary>
        /// 本 ASC 允许的攻击令牌总数（按当前难度从战斗设置取）。
        /// 对应 UE5 <c>GetNumAttackTokens</c>；无配置时返回 int.MaxValue（不设限）。
        /// </summary>
        public virtual int GetNumAttackTokens()
        {
            var settings = NarrativePro.Settings.NarrativeCombatDeveloperSettings.Instance;
            if (settings == null) return int.MaxValue;
            return settings.GetAttackTokensForDifficulty(GetCurrentDifficultyForTokens());
        }

        /// <summary>剩余可用令牌数（算术在纯层 <see cref="NarrativeAttackTokenLedger.AvailableTokens"/>）。对应 UE5 <c>GetAvailableAttackTokens</c>。</summary>
        public virtual int GetAvailableAttackTokens()
        {
            return NarrativeAttackTokenLedger.AvailableTokens(GetNumAttackTokens(), GrantedAttackTokens.Count);
        }

        /// <summary>已授予的令牌数。对应 UE5 <c>GetNumGrantedAttackTokens</c>。</summary>
        public virtual int GetNumGrantedAttackTokens()
        {
            return GrantedAttackTokens.Count;
        }

        /// <summary>
        /// 查找第一个当前空闲的战斗能力句柄（供 NPC 攻击回路使用，
        /// 对应 UE5 行为树 AbilityService 选取攻击能力的等价物）。
        /// </summary>
        public GameplayAbilitySpecHandle FindFirstCombatAbilityHandle()
        {
            foreach (var spec in GrantedAbilities)
            {
                if (spec == null || spec.bIsActive || spec.Ability == null) continue;
                if (spec.Ability is NarrativeCombatAbility)
                {
                    return new GameplayAbilitySpecHandle(spec.HandleId);
                }
            }
            return GameplayAbilitySpecHandle.Invalid;
        }

        /// <summary>取句柄上的战斗能力实例（非战斗能力返回 null）。</summary>
        public NarrativeCombatAbility FindCombatAbility(GameplayAbilitySpecHandle handle)
        {
            return FindAbilitySpec(handle)?.Ability as NarrativeCombatAbility;
        }

        /// <summary>取攻击优先级（供 EQS / NPC 选目标用）。对应 UE5 <c>GetAttackPriority</c>。</summary>
        public virtual float GetAttackPriority()
        {
            return AttackPriority;
        }

        /// <summary>某控制器当前是否持有本 ASC 的令牌。</summary>
        public virtual bool HoldsToken(NarrativePro.AI.NarrativeNPCController claimer)
        {
            if (claimer == null) return false;
            foreach (var token in GrantedAttackTokens)
            {
                if (token != null && ReferenceEquals(token.Owner, claimer)) return true;
            }
            return false;
        }

        /// <summary>
        /// 读取当前游戏难度并映射到战斗设置所用的枚举。
        /// 两处 ENarrativeGameplayDifficulty 同名不同命名空间，按底层整数值对齐映射。
        /// </summary>
        private static NarrativePro.Settings.ENarrativeGameplayDifficulty GetCurrentDifficultyForTokens()
        {
            var userSettings = NarrativePro.UnrealFramework.NarrativeGameUserSettings.Get();
            if (userSettings == null) return NarrativePro.Settings.ENarrativeGameplayDifficulty.Medium;

            var difficulty = userSettings.GetGameplayDifficulty();
            int raw = (int)difficulty;
            if (raw < 0) raw = 0;
            if (raw > (int)NarrativePro.Settings.ENarrativeGameplayDifficulty.Insane)
            {
                raw = (int)NarrativePro.Settings.ENarrativeGameplayDifficulty.Insane;
            }
            return (NarrativePro.Settings.ENarrativeGameplayDifficulty)raw;
        }

        // ===== 死亡 / 复活 =====

        /// <summary>
        /// 死亡状态置位并广播。对应 UE5 <c>OnRep_bIsDead</c>：
        /// 状态变化时广播 OnDeathStateChanged，并增删 State_IsDead 松散标签。
        /// </summary>
        protected virtual void SetDeadState(bool bNewIsDead)
        {
            if (_bIsDead == bNewIsDead) return;

            bool bOldIsDead = _bIsDead;
            _bIsDead = bNewIsDead;

            if (_bIsDead != bOldIsDead)
            {
                OnDeathStateChanged?.Invoke(Actor, this, _bIsDead);
            }

            // 给能力/效果系统一个统一的死亡标签
            var deadTag = NarrativePro.Core.NarrativeGameplayTags.State_IsDead;
            if (_bIsDead)
            {
                if (!HasMatchingGameplayTag(deadTag)) GameplayTags.AddTag(deadTag);
            }
            else
            {
                if (HasMatchingGameplayTag(deadTag)) GameplayTags.RemoveTag(deadTag);
            }
        }

        /// <summary>
        /// 复活：清除死亡状态并恢复满血。对应 UE5 <c>Revive</c>。
        /// UE5 仅重置状态，血量由效果/调用方决定；这里补上「复活即满血」的常见语义，
        /// 由 <see cref="ReviveRestoresFullHealth"/> 控制。
        /// </summary>
        public virtual void Revive()
        {
            if (!_bIsDead) return;

            SetDeadState(false);

            if (ReviveRestoresFullHealth && AttributeSet != null)
            {
                float oldHealth = AttributeSet.Health.CurrentValue;
                AttributeSet.Health.SetCurrentValue(AttributeSet.MaxHealth.CurrentValue);
                AttributeSet.NotifyAttributeChanged("Health", AttributeSet.Health.CurrentValue, oldHealth);

                float oldStamina = AttributeSet.Stamina.CurrentValue;
                AttributeSet.Stamina.SetCurrentValue(AttributeSet.MaxStamina.CurrentValue);
                AttributeSet.NotifyAttributeChanged("Stamina", AttributeSet.Stamina.CurrentValue, oldStamina);
            }
        }

        /// <summary>Revive 是否把血量/耐力恢复到满值。</summary>
        public bool ReviveRestoresFullHealth = true;

        // ===== 属性存档（对应 UE5 PrepareForSave / Load） =====

        /// <summary>本 ASC 拥有的全部可存档属性名。对应 UE5 GetAllAttributes。</summary>
        public virtual List<string> GetAllAttributeNames()
        {
            var names = new List<string>();
            if (AttributeSet == null) return names;
            names.AddRange(new[]
            {
                "XP", "Health", "MaxHealth", "Stamina", "MaxStamina", "StaminaRegenRate",
                "AttackRating", "Armor", "AttackDamage", "StealthRating"
            });
            return names;
        }

        /// <summary>
        /// 存档前把 <see cref="AttributesToSave"/> 中的属性值快照进 <see cref="SavedAttributes"/>。
        /// 对应 UE5 <c>PrepareForSave_Implementation</c>（NarrativeAbilitySystemComponent.cpp:702-735）。
        /// 由 NarrativeSaveSubsystem 遍历 INarrativeSavableComponent 自动调用。
        /// </summary>
        public virtual void PrepareForSave()
        {
            if (AttributeSet == null) return;

            var allAttributes = GetAllAttributeNames();
            if (SavedAttributes == null) SavedAttributes = new List<SavedAttribute>();
            SavedAttributes.Clear();

            foreach (var attributeName in allAttributes)
            {
                if (AttributesToSave == null || !AttributesToSave.Contains(attributeName)) continue;

                var attr = AttributeSet.GetAttribute(attributeName);
                if (attr == null) continue;

                SavedAttributes.Add(new SavedAttribute
                {
                    AttributeName = attributeName,
                    Value = attr.CurrentValue
                });
            }
        }

        /// <summary>
        /// 读档后把 <see cref="SavedAttributes"/> 里的值以 Override 方式还原回属性。
        /// 对应 UE5 <c>Load_Implementation</c>（NarrativeAbilitySystemComponent.cpp:677-700）。
        /// 由 NarrativeSaveSubsystem 在应用完归档字节后自动调用。
        /// </summary>
        public virtual void Load()
        {
            if (AttributeSet == null || SavedAttributes == null || SavedAttributes.Count == 0) return;

            var allAttributes = GetAllAttributeNames();

            foreach (var attributeName in allAttributes)
            {
                if (AttributesToSave == null || !AttributesToSave.Contains(attributeName)) continue;

                foreach (var saved in SavedAttributes)
                {
                    if (saved == null || saved.AttributeName != attributeName) continue;

                    var attr = AttributeSet.GetAttribute(attributeName);
                    if (attr == null) break;

                    float oldValue = attr.CurrentValue;
                    float restoredValue = saved.Value;

                    // Health/Stamina 不能越过其 Max
                    if (attributeName == "Health")
                    {
                        restoredValue = Mathf.Clamp(restoredValue, 0f, AttributeSet.MaxHealth.CurrentValue);
                    }
                    else if (attributeName == "Stamina")
                    {
                        restoredValue = Mathf.Clamp(restoredValue, 0f, AttributeSet.MaxStamina.CurrentValue);
                    }

                    attr.BaseValue = restoredValue;
                    attr.SetCurrentValue(restoredValue);

                    if (Mathf.Abs(attr.CurrentValue - oldValue) > 0.0001f)
                    {
                        AttributeSet.NotifyAttributeChanged(attributeName, attr.CurrentValue, oldValue);
                    }
                    break;
                }
            }

            // 存档里就是一个死掉的角色时，读档后应保持死亡状态
            if (AttributeSet.Health.CurrentValue <= 0f && bStartupEffectsApplied)
            {
                SetDeadState(true);
            }
        }

        /// <summary>应用 GameplayEffect 到自身。</summary>
        /// <param name="specHandle">效果规格句柄。</param>
        /// <returns>激活效果句柄（Instant 类型返回 Invalid）。</returns>
        public virtual ActiveGameplayEffectHandle ApplyGameplayEffectSpecToSelf(GameplayEffectSpecHandle specHandle)
        {
            if (!specHandle.IsValid) return ActiveGameplayEffectHandle.Invalid;
            var spec = specHandle.Spec;
            var effect = spec.Effect;
            if (effect == null) return ActiveGameplayEffectHandle.Invalid;

            // 检查 RequiredTags
            if (effect.RequiredTags != null)
            {
                foreach (var tag in effect.RequiredTags.GetTags())
                {
                    if (!GameplayTags.HasTag(tag))
                    {
                        // 缺少必需标签，不应用
                        return ActiveGameplayEffectHandle.Invalid;
                    }
                }
            }

            // 执行计算派发（对应 UE5 ExecutionCalculation）：效果带 ExecuteCalcTypeId 且注册表能解析时，
            // 本次执行的输出全部由 exec calc 产生（自行写入 Damage/Heal 元属性），平坦修饰器列表不再直接应用。
            // 伤害与治疗都经此路由（"Damage"/"Heal"，见 ExecutionCalculationRegistry）。
            GameplayEffectExecutionCalculation execCalc = null;
            if (!string.IsNullOrEmpty(effect.ExecuteCalcTypeId))
            {
                execCalc = ExecutionCalculationRegistry.Find(effect.ExecuteCalcTypeId);
            }

            if (execCalc != null)
            {
                execCalc.Execute(this, spec);
            }
            else if (AttributeSet != null && effect.Modifiers != null)
            {
                // 执行前判定（对应 UE5 AttributeSet::PreGameplayEffectExecute）：
                // 被否决的修饰器不应用。免疫状态下的伤害在这里整体丢弃
                // （见 NarrativeAttributeSetBase.PreGameplayEffectExecute）。
                // 应用修饰器
                foreach (var mod in effect.Modifiers)
                {
                    if (mod == null) continue;

                    float magnitude = mod.ResolveMagnitude(spec);
                    if (!AttributeSet.PreGameplayEffectExecute(mod.AttributeName, magnitude, spec))
                    {
                        continue;
                    }

                    ApplyModifier(AttributeSet, mod, spec);
                }
            }

            // 记录本次执行投递到元属性上的量：PostGameplayEffectExecute 会把它消耗进 Health 并归零，
            // 因此必须在 Post 之前取，否则事件永远读不到值。
            float damageThisExecution = AttributeSet != null ? AttributeSet.Damage.CurrentValue : 0f;
            float healThisExecution = AttributeSet != null ? AttributeSet.Heal.CurrentValue : 0f;

            // 应用元属性处理
            if (AttributeSet != null)
            {
                AttributeSet.PostGameplayEffectExecute(spec);
            }

            // 应用授予标签（纯层：与抽取前的内联 foreach 同一口径，容器 HashSet 去重故幂等）
            NarrativeGrantedTagLedger.AddGrantedTags(GameplayTags, effect.GrantedTags);

            // 触发伤害/治疗事件（对应 UE5 DamagedBy / HealedBy）
            if (damageThisExecution > 0f)
            {
                OnDamagedBy?.Invoke(spec.SourceASC, damageThisExecution, spec);
                spec.SourceASC?.DealtDamage(this, damageThisExecution, spec);
                // 受击反馈音效：按命中表面 + 部位标签查 foley 库播放（UE5 在伤害执行里直接调 PlaySoundAtLocation）
                NarrativePro.Character.NarrativeCharacterFoleyExtensions.PlayFoleyFromCombatHit(
                    this, NarrativePro.Audio.NarrativeFoleyTags.Feet_Land, spec.Hit, damageThisExecution);
            }
            if (healThisExecution > 0f)
            {
                OnHealedBy?.Invoke(spec.SourceASC, healThisExecution, spec);
            }

            // 检查死亡：Health 归零即走 HandleOutOfHealth（对应 UE5 AttributeSet → ASC 绑定）
            if (AttributeSet != null && AttributeSet.Health.CurrentValue <= 0f)
            {
                HandleOutOfHealth(spec.SourceActor, spec.SourceActor, spec, damageThisExecution);
            }

            // 持续型效果加入 ActiveEffects
            if (effect.DurationType == EGameplayEffectDurationType.Duration ||
                effect.DurationType == EGameplayEffectDurationType.Infinite)
            {
                var active = new ActiveGameplayEffect(effect)
                {
                    HandleId = _nextEffectHandleId++,
                    OwnerASC = this,
                    StartTime = Time.GameTime,
                    RemainingDuration = effect.DurationType == EGameplayEffectDurationType.Duration ? effect.Duration : float.MaxValue,
                    NextPeriodTime = Time.GameTime + (effect.Period > 0f ? effect.Period : float.MaxValue)
                };

                // 周期效果：每到 Period 重新应用一次修饰器（对应 UE5 周期执行）
                if (effect.Period > 0f)
                {
                    var specForPeriod = spec;
                    active.OnPeriodTriggered = (a) => ApplyPeriodicModifiers(a, specForPeriod);
                }

                ActiveEffects.Add(active);

                // 记账：这个句柄往持有者身上挂了哪些 GrantedTags（RemoveActiveEffect / 过期都按句柄取回同一批）
                _grantedTagLedger.TrackGrant(active.HandleId, effect.GrantedTags);

                return new ActiveGameplayEffectHandle(active.HandleId);
            }

            // Instant 类型不进入 ActiveEffects
            return ActiveGameplayEffectHandle.Invalid;
        }

        /// <summary>
        /// 周期效果的一次触发：重新把该效果的修饰器按 SetByCaller/等级解析后应用一次。
        /// 对应 UE5 周期 GameplayEffect 的 Execute 重复执行。
        /// </summary>
        protected virtual void ApplyPeriodicModifiers(ActiveGameplayEffect active, GameplayEffectSpec sourceSpec)
        {
            if (active == null) return;

            // 无限/持续效果在周期 tick 期间可能被移除，句柄有效性检查
            if (!active.bIsActive) return;
            if (AttributeSet == null || sourceSpec == null || sourceSpec.Effect == null) return;

            foreach (var mod in sourceSpec.Effect.Modifiers)
            {
                if (mod == null) continue;

                float magnitude = mod.ResolveMagnitude(sourceSpec);

                // 周期治疗不应该越过 MaxHealth
                if (sourceSpec.Effect.DurationType == EGameplayEffectDurationType.Infinite &&
                    mod.AttributeName == "Heal")
                {
                    float overflow = Mathf.Max(0f, AttributeSet.Health.CurrentValue + magnitude - AttributeSet.MaxHealth.CurrentValue);
                    magnitude -= overflow;
                    if (magnitude <= 0f) continue;
                }

                if (!AttributeSet.PreGameplayEffectExecute(mod.AttributeName, magnitude, sourceSpec)) continue;

                ApplyModifier(AttributeSet, mod, sourceSpec);
            }

            AttributeSet.PostGameplayEffectExecute(sourceSpec);

            if (AttributeSet.Health.CurrentValue <= 0f)
            {
                HandleOutOfHealth(sourceSpec.SourceActor, sourceSpec.SourceActor, sourceSpec, sourceSpec.Effect.GetPeriodDamageMagnitude());
            }
        }

        /// <summary>
        /// 目标受到伤害的回执（对应 UE5 <c>DealtDamage</c>）。
        /// 用于让伤害来源侧知道自己打出了多少伤害（伤害数字、任务计数等）。
        /// </summary>
        public virtual void DealtDamage(NarrativeAbilitySystemComponent damagedASC, float damage, GameplayEffectSpec spec)
        {
            if (damagedASC == null || damage <= 0f) return;
            OnDealtDamage?.Invoke(damagedASC, damage, spec);
        }

        /// <summary>
        /// 血量耗尽处理。对应 UE5 <c>HandleOutOfHealth</c>。
        /// <para>
        /// 用 <see cref="bStartupEffectsApplied"/> 挡掉 BeginPlay 阶段 Health 尚为 0 的伪触发；
        /// 触发死亡能力（GameplayEvent_Death）后置死亡状态并广播。
        /// </para>
        /// </summary>
        public virtual void HandleOutOfHealth(
            FlaxEngine.Actor damageInstigator,
            FlaxEngine.Actor damageCauser,
            GameplayEffectSpec damageEffectSpec,
            float damageMagnitude)
        {
            if (!bStartupEffectsApplied) return;
            if (IsDead) return;

            var payload = new GameplayEventData
            {
                EventTag = NarrativePro.Core.NarrativeGameplayTags.GameplayEvent_Death,
                Instigator = damageInstigator,
                Target = GetAvatarOwner(),
                OptionalObject = damageEffectSpec?.Effect,
                EffectSpec = damageEffectSpec,
                EventMagnitude = damageMagnitude
            };

            if (damageEffectSpec != null && damageEffectSpec.SourceASC != null)
            {
                damageEffectSpec.SourceASC.GetOwnedGameplayTags(payload.InstigatorTags);
            }
            GetOwnedGameplayTags(payload.TargetTags);

            // 先广播事件，让死亡能力/监听者还能读到"活着"的状态（对应 UE5 顺序）
            HandleGameplayEvent(payload.EventTag, payload);

            // 事件里可能已被复活，重新判定一次
            if (IsDead) return;

            SetDeadState(true);
            OnDied?.Invoke(Actor, this);

            // 击杀掉落（工作项 7）：死亡即把掉落表掷给击杀者。
            // 对应 UE5 的尸体 LootSource 拾取链路（NPCInteractable.cpp:42-44 把死者背包设为 LootSource），
            // 单机移植里等价实现为“死亡时向击杀者背包掷表”，表数据由 NarrativeNPCController 初始化注入。
            // 击杀归因兜底：部分路径（周期 DOT / 施毒 / 投射物等仅带 SourceASC 的效果 Spec）SourceActor 可能为空，
            // 用来源 ASC 的 Actor 补齐，保证每条伤害路径都能给出非空击杀者；仍无来源（环境伤害）时
            // RollDeathLootFor 静默跳过，无 loot 表的 NPC 也直接早退，不产生噪音日志。
            var lootKiller = damageInstigator ?? damageEffectSpec?.SourceASC?.Actor;
            RollDeathLootFor(lootKiller);
        }

        /// <summary>
        /// 死亡时掷给击杀者的战利品表。由 NPC 控制器在初始化的
        /// <see cref="NarrativePro.UnrealFramework.NarrativeNPCCharacter.GetDefaultItemLoadout"/> 灌入。
        /// </summary>
        public readonly List<NarrativePro.Items.LootTableRoll> DeathLootTables = new List<NarrativePro.Items.LootTableRoll>();

        /// <summary>
        /// 死亡掉落：把 <see cref="DeathLootTables"/> 逐表掷入击杀者（或宿主角色）的背包。
        /// 击杀者没有背包时安全跳过（例如死于环境伤害）。
        /// </summary>
        public virtual void RollDeathLootFor(FlaxEngine.Actor killer)
        {
            if (DeathLootTables.Count == 0 || killer == null) return;

            // 自伤致死（跌落/中毒自己扣血）不产生"击杀者掉落"，否则死者会把 loot 塞回自己背包
            if (ReferenceEquals(killer, GetAvatarOwner()) || ReferenceEquals(killer, Actor)) return;

            var looter = ResolveInventoryForActor(killer);
            if (looter == null) return;

            var results = new List<NarrativePro.Items.ItemAddResult>();
            foreach (var roll in DeathLootTables)
            {
                if (roll == null) continue;
                looter.TryAddFromLootTable(roll, results);
            }

            if (results.Count > 0)
            {
                NarrativePro.Core.NarrativeLog.Log(
                    $"[GAS] 死亡掉落：{Actor?.Name} → {killer.Name}，授予 {results.Count} 件");
            }
        }

        /// <summary>沿父链查找背包组件（命中体可能是角色的子碰撞体）。</summary>
        private NarrativePro.Items.NarrativeInventoryComponent ResolveInventoryForActor(FlaxEngine.Actor actor)
        {
            var current = actor;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                var inv = current.GetScript<NarrativePro.Items.NarrativeInventoryComponent>();
                if (inv != null) return inv;
                current = current.Parent;
            }
            return null;
        }

        /// <summary>应用 GameplayEffect Spec 到指定目标 ASC。</summary>
        public virtual List<ActiveGameplayEffectHandle> ApplyGameplayEffectSpecToTargetData(GameplayEffectSpecHandle specHandle, List<NarrativeAbilitySystemComponent> targetData)
        {
            var result = new List<ActiveGameplayEffectHandle>();
            if (targetData == null) return result;
            foreach (var target in targetData)
            {
                if (target == null) continue;
                var handle = target.ApplyGameplayEffectSpecToSelf(specHandle);
                if (handle.IsValid) result.Add(handle);

                // 触发 DealtDamage 事件（来自本 ASC 造成的伤害）
                if (AttributeSet != null && target.AttributeSet != null && target.AttributeSet.Damage.CurrentValue > 0f)
                {
                    OnDealtDamage?.Invoke(target, target.AttributeSet.Damage.CurrentValue, specHandle.Spec);
                }
            }
            return result;
        }

        /// <summary>应用单个修饰器到属性集。</summary>
        protected virtual void ApplyModifier(NarrativeAttributeSetBase attrSet, GameplayModifierInfo mod, GameplayEffectSpec spec)
        {
            var attr = attrSet.GetAttribute(mod.AttributeName);
            if (attr == null) return;

            float oldValue = attr.CurrentValue;
            float magnitude = mod.ResolveMagnitude(spec);
            float newMax = oldValue;

            // 元属性（Heal/Damage）是"一次性投递量"：只累加 CurrentValue，绝不写 BaseValue。
            // 否则 PostGameplayEffectExecute 归零 CurrentValue 后 BaseValue 仍残留，
            // 下一次重算会把旧伤害重新算进来（历史缺陷）。
            bool bIsMeta = mod.AttributeName == "Heal" || mod.AttributeName == "Damage";

            if (bIsMeta)
            {
                // 元属性只承载"本次投递的量"，不参与 BaseValue 累积
                switch (mod.ModifierOp)
                {
                    case EGameplayModOp.Add: newMax = oldValue + magnitude; break;
                    case EGameplayModOp.Multiply: newMax = oldValue * magnitude; break;
                    case EGameplayModOp.Divide: newMax = magnitude != 0f ? oldValue / magnitude : oldValue; break;
                    case EGameplayModOp.Override: newMax = magnitude; break;
                }
                attr.SetCurrentValue(newMax);
            }
            else
            {
                switch (mod.ModifierOp)
                {
                    case EGameplayModOp.Add:
                        // 加法修改 BaseValue（Instant）或当前值（持续）
                        attr.BaseValue += magnitude;
                        newMax = RecalcAttributeWithModifiers(attrSet, mod.AttributeName);
                        break;
                    case EGameplayModOp.Multiply:
                        attr.BaseValue *= magnitude;
                        newMax = RecalcAttributeWithModifiers(attrSet, mod.AttributeName);
                        break;
                    case EGameplayModOp.Divide:
                        if (magnitude != 0f)
                        {
                            attr.BaseValue /= magnitude;
                            newMax = RecalcAttributeWithModifiers(attrSet, mod.AttributeName);
                        }
                        break;
                    case EGameplayModOp.Override:
                        attr.SetBaseValue(magnitude);
                        newMax = magnitude;
                        break;
                }

                // 处理 Health/Stamina（被 Max 限制）
                if (mod.AttributeName == "Health")
                {
                    newMax = Mathf.Clamp(newMax, 0f, attrSet.MaxHealth.CurrentValue);
                }
                else if (mod.AttributeName == "Stamina")
                {
                    newMax = Mathf.Clamp(newMax, 0f, attrSet.MaxStamina.CurrentValue);
                }

                // Max 属性变化时按比例重标定其派生属性（对应 UE5 AdjustAttributeForMaxChange）。
                // 必须在将新 Max 写入之前调用：此时 MaxHealth.CurrentValue 仍是旧 Max。
                attrSet.OnMaxAttributeChanged(mod.AttributeName, newMax);

                attr.SetCurrentValue(newMax);
            }

            if (Mathf.Abs(newMax - oldValue) > 0.0001f)
            {
                attrSet.NotifyAttributeChanged(mod.AttributeName, newMax, oldValue);
            }
        }

        /// <summary>重算属性当前值（基于 BaseValue + 激活效果修饰器，简化版仅返回 BaseValue）。</summary>
        protected virtual float RecalcAttributeWithModifiers(NarrativeAttributeSetBase attrSet, string attributeName)
        {
            var attr = attrSet.GetAttribute(attributeName);
            if (attr == null) return 0f;

            // 简化版：直接用 BaseValue 作为当前值
            // 完整实现需要遍历所有 ActiveEffects 中的修饰器
            float result = attr.BaseValue;
            foreach (var active in ActiveEffects)
            {
                if (!active.bIsActive || active.Effect == null || active.Effect.Modifiers == null) continue;
                foreach (var mod in active.Effect.Modifiers)
                {
                    if (mod.AttributeName != attributeName) continue;
                    switch (mod.ModifierOp)
                    {
                        case EGameplayModOp.Add: result += mod.Magnitude; break;
                        case EGameplayModOp.Multiply: result *= mod.Magnitude; break;
                        case EGameplayModOp.Divide:
                            if (mod.Magnitude != 0f) result /= mod.Magnitude;
                            break;
                        case EGameplayModOp.Override: result = mod.Magnitude; break;
                    }
                }
            }
            return result;
        }

        /// <summary>移除激活效果。</summary>
        public virtual void RemoveActiveEffect(ActiveGameplayEffectHandle handle)
        {
            if (!handle.IsValid) return;
            for (int i = ActiveEffects.Count - 1; i >= 0; i--)
            {
                var active = ActiveEffects[i];
                if (active.HandleId == handle.HandleId)
                {
                    // 移除授予的标签：按句柄从账本取回当初挂上的同一批（重击标签因此能被撤掉）
                    _grantedTagLedger.RevokeGrant(active.HandleId, GameplayTags);

                    active.bIsActive = false;
                    ActiveEffects.RemoveAt(i);

                    // 重算属性
                    if (AttributeSet != null)
                    {
                        RecalculateAllAttributes();
                    }
                    return;
                }
            }
        }

        /// <summary>重算所有属性。</summary>
        public virtual void RecalculateAllAttributes()
        {
            if (AttributeSet == null) return;

            // 先重算 Max 类，再重算其派生类，保证 clamp 与比例重标定用的是新 Max
            var attrNames = new[] { "XP", "MaxHealth", "MaxStamina", "StaminaRegenRate",
                                     "AttackRating", "Armor", "AttackDamage", "StealthRating",
                                     "Health", "Stamina" };
            foreach (var name in attrNames)
            {
                // 元属性不参与重算（一次性投递量，由 PostGameplayEffectExecute 消耗）
                if (name == "Heal" || name == "Damage") continue;

                var attr = AttributeSet.GetAttribute(name);
                if (attr == null) continue;
                float oldValue = attr.CurrentValue;
                float newValue = RecalcAttributeWithModifiers(AttributeSet, name);

                // clamp Health/Stamina
                if (name == "Health") newValue = Mathf.Clamp(newValue, 0f, AttributeSet.MaxHealth.CurrentValue);
                else if (name == "Stamina") newValue = Mathf.Clamp(newValue, 0f, AttributeSet.MaxStamina.CurrentValue);

                // Max 属性写入前按比例重标定派生属性
                AttributeSet.OnMaxAttributeChanged(name, newValue);

                attr.SetCurrentValue(newValue);
                if (Mathf.Abs(newValue - oldValue) > 0.0001f)
                {
                    AttributeSet.NotifyAttributeChanged(name, newValue, oldValue);
                }
            }
        }

        /// <summary>添加动态标签（通过临时 GameplayEffect）。</summary>
        public virtual ActiveGameplayEffectHandle AddDynamicTagsGameplayEffect(GameplayTagContainer tagsToAdd)
        {
            if (tagsToAdd == null || !tagsToAdd.GetTags().Any()) return ActiveGameplayEffectHandle.Invalid;

            // 创建 Infinite 类型的效果，仅包含 GrantedTags
            var effect = new GameplayEffect("DynamicTags")
            {
                DurationType = EGameplayEffectDurationType.Infinite,
                GrantedTags = new GameplayTagContainer(tagsToAdd.GetTags())
            };
            var spec = new GameplayEffectSpec(effect, this);
            return ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));
        }

        /// <summary>
        /// 直接造成伤害（不通过 GameplayEffect，用于掉落伤害等）。
        /// <para>
        /// <paramref name="instigator"/> 是真正的施害者：死亡归因（<see cref="RollDeathLootFor"/>）
        /// 与击杀能力都读它。旧调用点不传时退化为"自己"（等价环境伤害），
        /// 但不再像之前那样把受害者当成击杀者写死进死亡流程。
        /// </para>
        /// </summary>
        public virtual void DealDamage(float damage, FlaxEngine.Actor instigator = null, FlaxEngine.Actor damageCauser = null)
        {
            if (AttributeSet == null) return;
            if (damage <= 0f) return;

            // 免疫状态下直接丢弃（与 PreGameplayEffectExecute 的判定保持一致）
            if (HasMatchingGameplayTag(NarrativePro.Core.NarrativeGameplayTags.State_Invulnerable)) return;

            float oldDamage = AttributeSet.Damage.CurrentValue;
            AttributeSet.Damage.SetCurrentValue(oldDamage + damage);
            AttributeSet.PostGameplayEffectExecute(null);

            // 检查死亡
            if (AttributeSet.Health.CurrentValue <= 0f)
            {
                HandleOutOfHealth(instigator ?? Actor, damageCauser ?? instigator ?? Actor, null, damage);
            }
        }

        /// <summary>直接治疗（不通过 GameplayEffect）。</summary>
        public virtual void Heal(float amount)
        {
            if (AttributeSet == null) return;
            if (amount <= 0f) return;

            float oldHeal = AttributeSet.Heal.CurrentValue;
            AttributeSet.Heal.SetCurrentValue(oldHeal + amount);
            AttributeSet.PostGameplayEffectExecute(null);
        }

        /// <summary>立即击杀。<paramref name="killer"/> 为真实击杀者，缺失时按自杀处理（不给掉落）。</summary>
        public virtual void Instakill(FlaxEngine.Actor killer = null)
        {
            if (AttributeSet == null) return;
            if (_bIsDead) return;

            // 免疫状态下无法被瞬时击杀
            if (HasMatchingGameplayTag(NarrativePro.Core.NarrativeGameplayTags.State_Invulnerable)) return;

            float oldHealth = AttributeSet.Health.CurrentValue;
            AttributeSet.Health.SetCurrentValue(0f);
            AttributeSet.NotifyAttributeChanged("Health", 0f, oldHealth);

            var trueKiller = killer ?? Actor;
            HandleOutOfHealth(trueKiller, trueKiller, null, oldHealth);
        }

        /// <summary>获取 Bot 攻击频率（指定 InputTag）。</summary>
        public virtual float GetBotAttackFrequency(GameplayTag inputTag)
        {
            // 查找绑定到 inputTag 的能力，返回其 DefaultBotAttackFrequency
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag && spec.Ability is NarrativeCombatAbility combat)
                {
                    return combat.DefaultBotAttackFrequency;
                }
            }
            return 0f;
        }

        /// <summary>获取 Bot 攻击范围（指定 InputTag）。</summary>
        public virtual float GetBotAttackRange(GameplayTag inputTag)
        {
            foreach (var spec in GrantedAbilities)
            {
                if (spec.InputTag == inputTag && spec.Ability is NarrativeCombatAbility combat)
                {
                    return combat.DefaultBotAttackRange;
                }
            }
            return 0f;
        }

        // ===== 标签查询（IGameplayTagAssetInterface 等价） =====

        public virtual void GetOwnedGameplayTags(GameplayTagContainer tagContainer)
        {
            if (tagContainer == null) return;
            foreach (var tag in GameplayTags.GetTags())
            {
                tagContainer.AddTag(new GameplayTag(tag));
            }
        }

        public virtual bool HasMatchingGameplayTag(GameplayTag tagToCheck)
        {
            return GameplayTags.HasTag(tagToCheck);
        }

        public virtual bool HasAllMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return GameplayTags.HasAll(tagsToCheck);
        }

        public virtual bool HasAnyMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            return GameplayTags.HasAny(tagsToCheck);
        }

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();

            // 宿主碰撞体兜底（幂等）：战斗命中全部走物理查询，玩家宿主走预制体路径时
            // 游戏侧不一定给它挂碰撞体（HundunWorldGame.cs:2772 只在兜底分支挂），
            // 没有碰撞体的角色对 NPC 的 OverlapSphere/hitscan 隐形，劫匪会直接索敌失败。
            NarrativePro.AI.NarrativeCharacterSubsystem.EnsureHostCollider(Actor);

            if (AttributeSet == null)
            {
                AttributeSet = Actor.GetScript<NarrativeAttributeSetBase>();
            }
            // 初始化属性（含默认属性效果、启动效果、默认能力授予）
            InitializeAttributes();

            // 绑定属性集的血量耗尽事件 → HandleOutOfHealth
            // 对应 UE5 InitAbilityActorInfo 里的 MutableAttributeSet->OnOutOfHealth.AddUObject(...)
            if (AttributeSet != null)
            {
                AttributeSet.AttachToComponent(this);
                AttributeSet.BindOutOfHealth(this);
            }

            // 绑定死亡事件以处理 HandleDeath
            OnDied += HandleDeathInternal;
        }

        public override void OnDisable()
        {
            OnDied -= HandleDeathInternal;

            if (AttributeSet != null)
            {
                AttributeSet.UnbindOutOfHealth(this);
            }

            // 本体下线：把手里的攻击令牌全部归还，避免其它 NPC 的令牌表里留悬空引用
            ReleaseAllAttackTokens();

            base.OnDisable();
        }

        /// <summary>归还本 ASC 发出的全部攻击令牌（持有者下线/销毁时调用）。</summary>
        public virtual void ReleaseAllAttackTokens()
        {
            for (int i = GrantedAttackTokens.Count - 1; i >= 0; i--)
            {
                ReturnTokenAtIndex(i);
            }
        }

        /// <summary>每帧 Tick：更新激活效果的持续期，应用耐力恢复。</summary>
        public override void OnUpdate()
        {
            // 更新激活效果
            float dt = Time.DeltaTime;
            float gameTime = Time.GameTime;
            for (int i = ActiveEffects.Count - 1; i >= 0; i--)
            {
                var active = ActiveEffects[i];
                if (!active.Tick(dt, gameTime))
                {
                    // 过期：与 RemoveActiveEffect 走同一笔账（按句柄取回当初挂上的标签并撤销）
                    _grantedTagLedger.RevokeGrant(active.HandleId, GameplayTags);
                    ActiveEffects.RemoveAt(i);
                }
            }

            // 耐力恢复
            if (AttributeSet != null)
            {
                AttributeSet.TickStaminaRegen(dt);
            }
        }

        /// <summary>内部死亡处理。</summary>
        protected virtual void HandleDeathInternal(FlaxEngine.Actor killedActor, NarrativeAbilitySystemComponent killedASC)
        {
            NarrativePro.Core.NarrativeLog.Log($"[ASC] Actor {killedActor?.Name} died.");
        }
    }
}
