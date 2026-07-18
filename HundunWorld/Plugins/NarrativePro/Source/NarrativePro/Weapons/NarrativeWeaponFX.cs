using System;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 武器特效基类。移植自 UE5 NarrativeArsenal: Weapons/NarrativeWeaponFX.h（UNarrativeWeaponFX，抽象 UObject）。
    /// 源 .h/.cpp 未随包提供，仅可从反射头确认其为抽象 UObject 派生类，无反射 UFUNCTION。
    /// 简化点：UObject → [Serializable] 抽象占位类；具体特效播放方法待获取源码后补全。
    /// Flax-待源码: 获取 UE5 源 UNarrativeWeaponFX 的虚函数后补全方法签名与实现。
    /// </summary>
    [Serializable]
    public abstract class NarrativeWeaponFX
    {
        // TODO [待源码]: 获取 UE5 源 UNarrativeWeaponFX 的虚方法后补全实现（无反射 UFUNCTION）
    }

    /// <summary>
    /// 实例化武器特效。移植自 UE5 FInstancedWeaponFX。
    /// 持有一个 NarrativeWeaponFX 引用。
    /// </summary>
    [Serializable]
    public class InstancedWeaponFX
    {
        /// <summary>所引用的武器特效实例</summary>
        public NarrativeWeaponFX WeaponFX { get; set; }
    }
}
