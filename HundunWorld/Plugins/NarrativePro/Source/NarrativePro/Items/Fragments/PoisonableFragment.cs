using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 可涂毒片段，加到任何需要支持涂毒的物品上（通常是武器，也可以是箭矢等弹药）。
    /// 适配 UE5 UPoisonableFragment。
    /// GAS 相关：毒药效果以字符串 ID 引用，待 GAS 阶段填充。
    /// </summary>
    public class PoisonableFragment : NarrativeItemFragment
    {
        /// <summary>已施加到此物品的毒药效果 ID（对应 UE5 TSubclassOf&lt;UGameplayEffect&gt;）。</summary>
        public string AppliedPoison { get; protected set; } = "";

        public PoisonableFragment()
        {
            // 默认构造
        }

        /// <summary>
        /// 判断此物品是否可被指定毒药涂毒。子类可覆盖以拒绝特定毒药。
        /// </summary>
        /// <param name="poison">尝试施加的毒药物品</param>
        /// <returns>是否可被涂毒</returns>
        public virtual bool CanBePoisonedBy(PoisonItem poison)
        {
            // 默认情况下，只要毒药非空且当前未涂毒即可
            return poison != null && string.IsNullOrEmpty(AppliedPoison);
        }

        /// <summary>
        /// 将指定毒药设置到此物品上。
        /// </summary>
        /// <param name="poisonEffectId">毒药效果 ID（对应 UE5 TSubclassOf&lt;UGameplayEffect&gt;）</param>
        public virtual void SetPoison(string poisonEffectId)
        {
            AppliedPoison = poisonEffectId ?? "";
        }

        /// <summary>
        /// 获取当前毒药效果，并清空已施加的毒药（一次性消耗）。
        /// </summary>
        /// <returns>被消耗的毒药效果 ID；若无毒药返回空字符串</returns>
        public string ConsumePoison()
        {
            string poison = AppliedPoison;
            AppliedPoison = "";
            return poison;
        }
    }
}
