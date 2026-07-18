using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// Narrative 物理材质。对应 UE5 UNarrativePhysicalMaterial。
    /// UE5 中继承 UPhysicalMaterial；Flax 中 PhysicalMaterial 是引擎资产类不易直接派生，
    /// 改为 [Serializable] 包装类，持有 FlaxEngine PhysicalMaterial 引用并附加 DamageMultiplier。
    /// 简化点：
    /// - 持有 FlaxEngine.PhysicalMaterial 引用（替代 TObjectPtr&lt;UPhysicalMaterial&gt;）
    /// - 暴露 DamageMultiplier 供伤害计算读取
    /// </summary>
    [Serializable]
    public class NarrativePhysicalMaterial
    {
        /// <summary>构造默认实例。</summary>
        public NarrativePhysicalMaterial()
        {
            DamageMultiplier = 1f;
        }

        /// <summary>关联的 FlaxEngine 物理材质资产（直接引用，替代 UE5 父类的物理材质数据）。</summary>
        [NonSerialized]
        public PhysicalMaterial PhysicalMaterialAsset;

        /// <summary>
        /// 物理材质资产路径（用于序列化持久化，对应 UE5 资产引用）。
        /// </summary>
        public string PhysicalMaterialPath = "";

        /// <summary>
        /// 当伤害计算检测到命中此材质时应用的特殊伤害倍数。
        /// 对应 UE5 UPROPERTY(EditDefaultsOnly, BlueprintReadOnly) float DamageMultiplier。
        /// </summary>
        public float DamageMultiplier;

        /// <summary>获取关联的 FlaxEngine 物理材质（若未加载则按路径同步加载）。</summary>
        public virtual PhysicalMaterial GetPhysicalMaterial()
        {
            if (PhysicalMaterialAsset != null) return PhysicalMaterialAsset;
            // Flax-不兼容: Flax 中 PhysicalMaterial 不是 Asset，无法通过 Content.Load 加载，需通过直接引用或物理系统查询。原文 TODO: Flax 中 PhysicalMaterial 不是 Asset，无法通过 Content.Load 加载
            return PhysicalMaterialAsset;
        }
    }
}
