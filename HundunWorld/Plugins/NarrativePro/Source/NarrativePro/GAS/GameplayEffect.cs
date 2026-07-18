using System;
using System.Collections.Generic;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 游戏效果持续期类型。对应 UE5 EGameplayEffectDurationType。
    /// </summary>
    public enum EGameplayEffectDurationType : byte
    {
        /// <summary>瞬时（应用后立即结束，仅修改属性）。</summary>
        Instant = 0,
        /// <summary>持续指定时长。</summary>
        Duration = 1,
        /// <summary>无限持续（直到主动移除）。</summary>
        Infinite = 2
    }

    /// <summary>
    /// 游戏效果定义。对应 UE5 UGameplayEffect。
    /// 简化点：移除 UE5 的 ModifierMagnitudeCalculation/ExecutionCalculation 复杂体系，
    /// 仅保留直接的属性修饰器列表 + 标签授予/移除。
    /// 执行计算通过 ExecuteCalcType 字符串引用，由 ASC 调用对应计算类。
    /// </summary>
    [Serializable]
    public class GameplayEffect
    {
        /// <summary>效果名称（便于调试）。</summary>
        public string EffectName = "";

        /// <summary>效果持续时间类型。</summary>
        public EGameplayEffectDurationType DurationType = EGameplayEffectDurationType.Instant;

        /// <summary>持续时间（秒），仅 Duration 类型有效。</summary>
        public float Duration = 0f;

        /// <summary>周期（秒），0 表示无周期。</summary>
        public float Period = 0f;

        /// <summary>属性修饰器列表。</summary>
        public List<GameplayModifierInfo> Modifiers = new List<GameplayModifierInfo>();

        /// <summary>授予的标签（应用到目标）。</summary>
        public GameplayTagContainer GrantedTags = new GameplayTagContainer();

        /// <summary>资产标签（用于查询，不应用到目标）。</summary>
        public GameplayTagContainer AssetTags = new GameplayTagContainer();

        /// <summary>所需标签（目标必须具有才能应用）。</summary>
        public GameplayTagContainer RequiredTags = new GameplayTagContainer();

        /// <summary>执行计算类型 ID（如 "Damage"、"Heal"），由 ASC 解析调用对应 ExecCalc。</summary>
        public string ExecuteCalcTypeId = "";

        /// <summary>效果堆叠策略。对应 UE5 EGameplayEffectStackingType。</summary>
        public EGameplayEffectStackingType StackingType = EGameplayEffectStackingType.None;

        /// <summary>最大堆叠数。</summary>
        public int MaxStacks = 1;

        public GameplayEffect() { }

        public GameplayEffect(string name)
        {
            EffectName = name;
        }
    }

    /// <summary>
    /// 游戏效果堆叠策略。对应 UE5 EGameplayEffectStackingType。
    /// </summary>
    public enum EGameplayEffectStackingType : byte
    {
        /// <summary>不堆叠（每次应用创建新实例）。</summary>
        None = 0,
        /// <summary>替换（刷新持续期）。</summary>
        Replace = 1,
        /// <summary>叠加（增加堆叠数）。</summary>
        Aggregate = 2
    }

    /// <summary>
    /// 激活的游戏效果实例。对应 UE5 FActiveGameplayEffect。
    /// 一个 GameplayEffect 应用到 ASC 后创建此实例，记录剩余时间、堆叠数等运行时状态。
    /// </summary>
    [Serializable]
    public class ActiveGameplayEffect
    {
        /// <summary>所属 ASC（运行时设置，不序列化）。</summary>
        [NonSerialized]
        public NarrativeAbilitySystemComponent OwnerASC;

        /// <summary>效果定义。</summary>
        public GameplayEffect Effect;

        /// <summary>已激活的时间（秒，从 Time.GameTime 开始计算）。</summary>
        public float StartTime = 0f;

        /// <summary>剩余持续时间（秒）。</summary>
        public float RemainingDuration = 0f;

        /// <summary>下次周期执行时间（秒）。</summary>
        public float NextPeriodTime = 0f;

        /// <summary>当前堆叠数。</summary>
        public int Stacks = 1;

        /// <summary>句柄 ID（每个激活效果唯一）。</summary>
        public int HandleId = 0;

        /// <summary>是否仍有效（未被移除）。</summary>
        public bool bIsActive = true;

        public ActiveGameplayEffect() { }

        public ActiveGameplayEffect(GameplayEffect effect)
        {
            Effect = effect;
        }

        /// <summary>更新效果：处理持续期与周期。</summary>
        /// <param name="deltaTime">帧间隔（秒）。</param>
        /// <param name="gameTime">当前游戏时间（秒）。</param>
        /// <returns>是否仍有效（true=仍激活，false=已过期可移除）。</returns>
        public bool Tick(float deltaTime, float gameTime)
        {
            if (Effect == null) return false;

            // Duration 类型：递减剩余时间
            if (Effect.DurationType == EGameplayEffectDurationType.Duration)
            {
                RemainingDuration -= deltaTime;
                if (RemainingDuration <= 0f)
                {
                    return false;
                }
            }

            // Infinite 类型：永不过期
            if (Effect.DurationType == EGameplayEffectDurationType.Infinite)
            {
                return true;
            }

            // Instant 类型：应用后立即移除（不应在 Active 列表中）
            if (Effect.DurationType == EGameplayEffectDurationType.Instant)
            {
                return false;
            }

            // 周期执行（Period > 0）
            if (Effect.Period > 0f && gameTime >= NextPeriodTime)
            {
                NextPeriodTime = gameTime + Effect.Period;
                // TODO [需接入 ASC 周期应用机制]: 周期应用修饰器（由 ASC 处理）
            }

            return true;
        }
    }

    /// <summary>
    /// 激活效果句柄。对应 UE5 FActiveGameplayEffectHandle。
    /// 用于引用 ASC 中的某个激活效果，便于移除或查询。
    /// </summary>
    [Serializable]
    public struct ActiveGameplayEffectHandle
    {
        public int HandleId;

        public bool IsValid => HandleId != 0;

        public ActiveGameplayEffectHandle(int id)
        {
            HandleId = id;
        }

        public static readonly ActiveGameplayEffectHandle Invalid = new ActiveGameplayEffectHandle(0);
    }

    /// <summary>
    /// 游戏效果规格。对应 UE5 FGameplayEffectSpec。
    /// 一个 GameplayEffect 应用前的实例化数据，包含来源、等级、动态标签等。
    /// </summary>
    [Serializable]
    public class GameplayEffectSpec
    {
        /// <summary>效果定义。</summary>
        public GameplayEffect Effect;

        /// <summary>来源 ASC（应用者，可能为 null）。</summary>
        [NonSerialized]
        public NarrativeAbilitySystemComponent SourceASC;

        /// <summary>来源 Actor。</summary>
        [NonSerialized]
        public FlaxEngine.Actor SourceActor;

        /// <summary>效果等级（修饰器可参考）。</summary>
        public float Level = 1f;

        /// <summary>动态授予标签（应用时追加到目标的 GrantedTags）。</summary>
        public GameplayTagContainer DynamicGrantedTags = new GameplayTagContainer();

        /// <summary>动态资产标签（应用到 Spec 上，用于查询）。</summary>
        public GameplayTagContainer DynamicAssetTags = new GameplayTagContainer();

        public GameplayEffectSpec() { }

        public GameplayEffectSpec(GameplayEffect effect, NarrativeAbilitySystemComponent source = null, float level = 1f)
        {
            Effect = effect;
            SourceASC = source;
            Level = level;
        }
    }

    /// <summary>
    /// 效果规格句柄。对应 UE5 FGameplayEffectSpecHandle。
    /// </summary>
    [Serializable]
    public struct GameplayEffectSpecHandle
    {
        public GameplayEffectSpec Spec;

        public bool IsValid => Spec != null && Spec.Effect != null;

        public GameplayEffectSpecHandle(GameplayEffectSpec spec)
        {
            Spec = spec;
        }
    }
}
