using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 应用 GameplayEffect 的物品（药水/食物/绷带等"消耗品"）。
    /// 使用时将所选效果施加到使用者身上。可通过取消 bConsumeOnUse 实现可重复使用的物品。
    /// 适配 UE5 UGameplayEffectItem。
    /// GAS 相关：效果以字符串 ID 走 <see cref="NarrativePro.GAS.NarrativeGASAssetRegistry"/> 解析，
    /// SetByCaller 标签值通过 <see cref="SetByCallerValues"/> 传递到 Spec 上。
    /// </summary>
    public class GameplayEffectItem : NarrativeItem
    {
        /// <summary>使用时应用的效果 ID（对应 UE5 TSubclassOf&lt;UGameplayEffect&gt;）</summary>
        public string GameplayEffectId { get; set; } = "";

        /// <summary>
        /// 使用时随效果一起应用的 SetByCaller 标签值映射。
        /// 对应 UE5 TMap&lt;FGameplayTag, float&gt; SetByCallerValues。
        /// 键为 GameplayTag.TagName，值为待设置的数值。
        /// </summary>
        public Dictionary<string, float> SetByCallerValues { get; set; } = new Dictionary<string, float>();

        /// <summary>效果等级</summary>
        public float EffectLevel { get; set; } = 1f;

        public override bool bConsumeOnUse { get => true; set => base.bConsumeOnUse = value; }

        public override bool ShouldUseOnAdd() => false;

        /// <summary>
        /// 使用物品：把效果应用到拥有者 ASC。
        /// 对应 UE5 <c>UGameplayEffectItem::Use_Implementation</c>。
        /// 返回 true 表示效果确实被应用（拥有者无 ASC 或效果解析失败则为 false）。
        /// </summary>
        public override void Use(NarrativeItem otherItem = null)
        {
            if (!ApplyEffectToOwner())
            {
                // 应用失败不消耗物品（否则玩家白白损失一瓶药水）
                NarrativeLog.LogWarning($"[Item] 使用效果物品 '{DisplayName}' 未生效，物品未被消耗");
                return;
            }

            base.Use(otherItem);
        }

        /// <summary>
        /// 解析并应用效果到拥有者。对应 UE5 MakeEffectSpecHandle + ApplyGameplayEffectSpecToOwnActor。
        /// </summary>
        protected virtual bool ApplyEffectToOwner()
        {
            if (string.IsNullOrEmpty(GameplayEffectId))
            {
                NarrativeLog.LogWarning($"[Item] '{DisplayName}' 未配置 GameplayEffectId");
                return false;
            }

            var asc = GetOwningASC();
            if (asc == null)
            {
                NarrativeLog.LogWarning($"[Item] '{DisplayName}' 的拥有者没有能力系统组件，效果无法应用");
                return false;
            }

            var effect = NarrativePro.GAS.NarrativeGASAssetRegistry.ResolveEffect(GameplayEffectId);
            if (effect == null) return false;

            var spec = new NarrativePro.GAS.GameplayEffectSpec(effect, asc, EffectLevel);

            // SetByCaller 值（键为标签名）
            if (SetByCallerValues != null)
            {
                foreach (var kvp in SetByCallerValues)
                {
                    if (string.IsNullOrEmpty(kvp.Key)) continue;
                    spec.SetSetByCallerMagnitude(new GameplayTag(kvp.Key), kvp.Value);
                }
            }

            var handle = asc.ApplyGameplayEffectSpecToSelf(new NarrativePro.GAS.GameplayEffectSpecHandle(spec));
            NarrativeLog.Log($"[Item] 使用效果物品 '{DisplayName}'，效果 {GameplayEffectId} 等级 {EffectLevel}");
            return handle.IsValid || effect.DurationType == NarrativePro.GAS.EGameplayEffectDurationType.Instant;
        }

        /// <summary>取拥有者的 ASC（通过背包→Pawn）。</summary>
        protected NarrativePro.GAS.NarrativeAbilitySystemComponent GetOwningASC()
        {
            var pawn = GetOwningPawn();
            return pawn != null ? pawn.GetScript<NarrativePro.GAS.NarrativeAbilitySystemComponent>() : null;
        }

        public override string GetStringVariable(string variableName)
        {
            switch (variableName)
            {
                case "EffectId": return GameplayEffectId;
                case "EffectLevel": return EffectLevel.ToString("F0");
                default: return base.GetStringVariable(variableName);
            }
        }
    }

    /// <summary>
    /// 毒药物品。特殊的 GameplayEffectItem，可施加到任何带 PoisonableFragment 的物品上，
    /// 不是供玩家直接消耗的物品。
    /// 适配 UE5 UPoisonItem。
    /// </summary>
    public class PoisonItem : GameplayEffectItem
    {
        public override bool bConsumeOnUse { get => false; set => base.bConsumeOnUse = value; }

        public override bool bUsedWithOtherItem { get => true; set => base.bUsedWithOtherItem = value; }

        public override void Use(NarrativeItem otherItem = null)
        {
            if (otherItem == null)
            {
                NarrativeLog.LogWarning($"[PoisonItem] '{DisplayName}' 必须配合目标物品使用");
                return;
            }

            // 获取目标物品的 PoisonableFragment
            var poisonable = otherItem.GetFragment<PoisonableFragment>();
            if (poisonable == null)
            {
                NarrativeLog.LogWarning($"[PoisonItem] 目标物品 '{otherItem.DisplayName}' 不支持涂毒");
                return;
            }

            if (!poisonable.CanBePoisonedBy(this))
            {
                NarrativeLog.LogWarning($"[PoisonItem] 目标物品 '{otherItem.DisplayName}' 拒绝此毒药 '{DisplayName}'");
                return;
            }

            poisonable.SetPoison(GameplayEffectId);
            NarrativeLog.Log($"[PoisonItem] 已将 '{DisplayName}' 涂抹到 '{otherItem.DisplayName}'");
        }

        public override bool CanUseItemWith(NarrativeItem testItem)
        {
            if (testItem == null) return false;
            var poisonable = testItem.GetFragment<PoisonableFragment>();
            return poisonable != null && poisonable.CanBePoisonedBy(this);
        }
    }
}
