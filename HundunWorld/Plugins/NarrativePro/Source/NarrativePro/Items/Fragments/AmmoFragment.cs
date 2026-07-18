using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Items
{
    /// <summary>
    /// 弹药片段，可加到任何物品上使其作为弹药使用。
    /// 包含此弹药使用的特殊投射物、自定义伤害效果等信息，由调用方决定如何使用。
    /// 可通过子类化扩展更多数据（例如弹药应覆盖武器使用的能力）。
    /// 适配 UE5 UAmmoFragment。
    /// </summary>
    public class AmmoFragment : NarrativeItemFragment
    {
        /// <summary>此弹药使用的自定义伤害值（&lt;=0 表示不覆盖）</summary>
        public float AmmoDamageOverride { get; set; } = 0f;

        /// <summary>此弹药使用的自定义伤害效果 ID（对应 UE5 TSubclassOf&lt;UGameplayEffect&gt;）</summary>
        public string DamageEffectId { get; set; } = "";

        /// <summary>此弹药使用的自定义投射物资源路径（对应 UE5 TSubclassOf&lt;ANarrativeProjectile&gt;）</summary>
        public string ProjectilePath { get; set; } = "";

        /// <summary>是否使用下面的 TraceData 覆盖武器默认的追踪数据</summary>
        public bool bOverrideTraceData { get; set; } = false;

        /// <summary>此弹药的自定义追踪数据</summary>
        public CombatTraceData TraceData { get; set; } = new CombatTraceData();

        public AmmoFragment()
        {
            // 默认构造，子类可覆盖
        }
    }
}
