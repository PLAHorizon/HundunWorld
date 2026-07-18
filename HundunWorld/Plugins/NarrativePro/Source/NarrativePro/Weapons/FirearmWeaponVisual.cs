using FlaxEngine;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 枪械武器视觉。移植自 UE5 NarrativeArsenal: Weapons/FirearmWeaponVisual.h（AFirearmWeaponVisual : AWeaponVisual）。
    /// 在 WeaponVisual 基础上增加瞄准（ADS）相关位置查询。
    /// 简化点：AActor → Script（继承 WeaponVisual）；方法体待获取源 .cpp 后补全。
    /// Flax-待源码: 获取 UE5 源 FirearmWeaponVisual.cpp 后补全方法实现。
    /// </summary>
    public class FirearmWeaponVisual : WeaponVisual
    {
        /// <summary>返回 ADS（瞄准）的世界空间位置。</summary>
        public virtual Vector3 GetADSLocation()
        {
            // TODO [待源码]: 获取 UE5 源 FirearmWeaponVisual.cpp 后补全实现。默认返回零向量。
            return Vector3.Zero;
        }

        /// <summary>返回 ADS（瞄准）相对于武器网格的本地空间位置。</summary>
        public virtual Vector3 GetADSRelativeLocation()
        {
            // TODO [待源码]: 获取 UE5 源 FirearmWeaponVisual.cpp 后补全实现。默认返回零向量。
            return Vector3.Zero;
        }
    }
}
