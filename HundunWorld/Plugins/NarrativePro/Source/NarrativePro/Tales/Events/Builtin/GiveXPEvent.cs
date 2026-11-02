using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 发放经验事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_GiveXP.uasset 符号表）：
    /// CallFunc 含 <c>GetAbilitySystemComponent</c>（ArsenalStatics）、
    /// <c>MakeEffectContext</c> + <c>MakeOutgoingSpec</c> +
    /// <c>AssignTagSetByCallerMagnitude</c>（含 _Magnitude_ImplicitCast，double 转 float）+
    /// <c>BP_ApplyGameplayEffectSpecToSelf</c>（AbilitySystemBlueprintLibrary），
    /// BP 变量 <c>GameplayEffectClass</c>/<c>Tag</c>/<c>XPToGive</c>/<c>Level</c>——
    /// 即「用 SetByCaller 幅度把 XPToGive 写进 XP 专属 GE 再应用到目标 ASC」。
    /// </para>
    /// <para>
    /// 端口通路核对：端口 ASC 属性集含 XP（GAS/NarrativeAttributeSetBase.cs:43-44，
    /// 且在 AttributesToSave 名单里），GE 应用链为
    /// NarrativeGASAssetRegistry.ResolveEffect → new GameplayEffectSpec(effect, asc, level)
    /// → spec.SetSetByCallerMagnitude → asc.ApplyGameplayEffectSpecToSelf
    /// （Items/GameplayEffectItem.cs:55-87 已在实时使用同一链）。
    /// </para>
    /// <para>
    /// 端口差异：UE5 的 MakeOutgoingSpec 走 GameplayEffectContextHandle，端口
    /// GameplayEffectSpec 构造函数直接吃源 ASC，无 Context 概念，语义等价（来源即自身）。
    /// 一次性奖励默认不随读档重放（bRefireOnLoad=false）。
    /// </para>
    /// </summary>
    public class GiveXPEvent : NarrativeEvent
    {
        /// <summary>经验 GE 资产标识。对应 UE5 <c>GameplayEffectClass</c>。</summary>
        public string GameplayEffectId { get; set; } = "";

        /// <summary>SetByCaller 标签名。对应 UE5 <c>Tag</c>；默认用端口内建 XP 幅度标签。</summary>
        public string XPTag { get; set; } = NarrativeGameplayTagsDefaults.XPTagName;

        /// <summary>经验数值。对应 UE5 <c>XPToGive</c>（double，应用时转 float）。</summary>
        public float XPToGive { get; set; } = 0f;

        /// <summary>效果等级。对应 UE5 MakeOutgoingSpec 的 <c>Level</c>。</summary>
        public float Level { get; set; } = 1f;

        public GiveXPEvent()
        {
            bRefireOnLoad = false;
        }

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(GameplayEffectId))
            {
                NarrativeLog.LogWarning("GiveXPEvent: GameplayEffectId 为空，跳过");
                return;
            }

            var asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(target as Actor)
                      ?? ArsenalStatics.GetNarrativeAbilitySystemComponent(controller as Actor);
            if (asc == null)
            {
                NarrativeLog.LogWarning($"GiveXPEvent: 目标没有 ASC，无法发放 {XPToGive} XP");
                return;
            }

            var effect = NarrativeGASAssetRegistry.ResolveEffect(GameplayEffectId);
            if (effect == null)
            {
                NarrativeLog.LogWarning($"GiveXPEvent: 无法解析 GameplayEffect '{GameplayEffectId}'");
                return;
            }

            // 对应 UE5 MakeOutgoingSpec + AssignTagSetByCallerMagnitude + ApplyGameplayEffectSpecToSelf
            var spec = new GameplayEffectSpec(effect, asc, Level);
            var magnitudeTag = new GameplayTag(string.IsNullOrEmpty(XPTag) ? NarrativeGameplayTagsDefaults.XPTagName : XPTag);
            if (magnitudeTag.IsValid())
                spec.SetSetByCallerMagnitude(magnitudeTag, XPToGive);
            else
                NarrativeLog.LogWarning($"GiveXPEvent: SetByCaller 标签 '{XPTag}' 无效，GE 将按默认幅度应用");

            asc.ApplyGameplayEffectSpecToSelf(new GameplayEffectSpecHandle(spec));
            NarrativeLog.Log($"GiveXPEvent: 已发放 {XPToGive} XP（效果 '{GameplayEffectId}'）");
        }

        public override string GetGraphDisplayText() => $"Give XP: {XPToGive}";
    }

    /// <summary>
    /// 端口内建 SetByCaller XP 标签名常量（Core/NarrativeGameplayTags.cs:514 注册同名标签）。
    /// 独立成类是因为属性默认值必须是常量表达式。
    /// </summary>
    public static class NarrativeGameplayTagsDefaults
    {
        public const string XPTagName = "Narrative.SetByCaller.XP";
    }
}
