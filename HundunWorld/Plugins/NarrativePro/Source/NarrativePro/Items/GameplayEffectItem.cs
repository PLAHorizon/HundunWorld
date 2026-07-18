using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 应用 GameplayEffect 的物品（药水/食物/绷带等"消耗品"）。
    /// 使用时将所选效果施加到使用者身上。可通过取消 bConsumeOnUse 实现可重复使用的物品。
    /// 适配 UE5 UGameplayEffectItem。
    /// GAS 相关：效果以字符串 ID 引用，SetByCaller 标签值通过字典传递，待 GAS 阶段填充。
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

        public override void Use(NarrativeItem otherItem = null)
        {
            // TODO [需接入 GAS 资产加载机制]: 将 GameplayEffectId 应用到拥有者的能力系统组件
            // 同时根据 SetByCallerValues 设置 SetByCaller 标签数值
            NarrativeLog.Log($"[Item] 使用效果物品 '{DisplayName}'，效果 {GameplayEffectId} 等级 {EffectLevel}");
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
