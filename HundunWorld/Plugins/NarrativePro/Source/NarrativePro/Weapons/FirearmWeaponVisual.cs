// NarrativePro —— 枪械武器视觉（FirearmWeaponVisual）
//
// UE5 依据（只读参考工程 C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro\Source\NarrativeArsenal）：
//   - Public/Weapons/FirearmWeaponVisual.h:12-14     AFirearmWeaponVisual : AWeaponVisual（瞄准下觜 ADS 基类）
//   - Public/Weapons/FirearmWeaponVisual.h:22-32     GetADSLocation(BlueprintNativeEvent) / GetADSRelativeLocation / ADSSightSocketName
//   - Private/Weapons/FirearmWeaponVisual.cpp:6-9     构造默认 ADSSightSocketName = "Socket_ADS"
//   - Private/Weapons/FirearmWeaponVisual.cpp:11-20   GetADSLocation_Implementation = WeaponMesh->GetSocketTransform(ADS, RTS_World).GetLocation()
//   - Private/Weapons/FirearmWeaponVisual.cpp:22-39   GetADSRelativeLocation = 把世界 ADS 点 TransformToBoneSpace(根骨骼) 得相对位
//   - Private/Weapons/WeaponVisual.cpp:396-458 / 593-671  基类近战扫描（本类不改，只做枪械专属补充）
//
// 端口差异：
//   - UE5 的 GetADSLocation 是 BlueprintNativeEvent（BP 可覆写觜点来源）；端口保留同名 virtual，
//     由 FirearmWeaponVisual 的 socket 解析实现。socket→世界位用 WeaponVisual.TryGetSocketTransform
//     （Flax 里骨骼节点即 socket），"相对根骨骼"用继承的 WorldToRootSpace，语义等价 UE5 TransformToBoneSpace。
//   - 射击消费：GAS 的 FireRangedAttack（NarrativeCombatAbility）不经过武器视觉，且该文件不在本任务可改范围。
//     因此本类用"轮询 WeaponOwner.OnAttack 写入的 LastAttackTime 是否推进"作为开火沿，
//     检测到一次推进即消费一次枪械 FX（枪口闪光 / 抛壳 / 拉栓）。这是纯 WeaponVisual 侧、无需改 GAS 的活消费方。
//   - 后坐力/散布：RangedWeaponItem 已在物品层持有 spread/recoil 数据并在 GetWeaponSpread/GetRecoilImpulse 消费
//     （Items/RangedWeaponItem.cs），视觉侧不再重复实现，只做表现层的枪口位移。

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 枪械武器视觉。继承 <see cref="WeaponVisual"/> 的近战/挂载能力，补充瞄准觜点（ADS）、
    /// 枪口位置与"每次开火消费 FX"的枪械专属表现。
    /// 使用方式与基类一致：挂在武器预制体 Actor 上，武器持握后由 WeaponVisualMount 绑定 WeaponOwner。
    /// </summary>
    public class FirearmWeaponVisual : WeaponVisual
    {
        /// <summary>瞄准觜点 socket 名。对应 UE5 ADSSightSocketName 默认 "Socket_ADS"（cpp:8）。</summary>
        public string ADSSightSocketName { get; set; } = "Socket_ADS";

        /// <summary>枪口 socket 名。UE5 由 BP 命名约定提供，端口显式化以便 FX/投射物取发射点。</summary>
        public string MuzzleSocketName { get; set; } = "Socket_Muzzle";

        /// <summary>瞄准时相机对齐觜点的额外偏移（对应 BP 侧微调，端口暴露为数据）。</summary>
        public Vector3 ADSAdditionalOffset { get; set; } = Vector3.Zero;

        /// <summary>上一次观测到的武器开火时刻，用于轮询检测"这一帧开火了"。</summary>
        private float _lastObservedAttackTime = -9999f;

        /// <summary>最近一次解析到的枪口世界位置（供瞄准/投射物起点消费者读取）。</summary>
        public Vector3 LastMuzzleLocation { get; private set; } = Vector3.Zero;

        // ===== ADS / 枪口位置（port FirearmWeaponVisual.cpp） =====

        /// <summary>
        /// 瞄准觜点世界位置。对应 UE5 GetADSLocation_Implementation（cpp:11-20）：
        /// 取 WeaponMesh 上 ADS socket 的世界位置。解析失败返回 false 且 out 为武器 Actor 位置。
        /// </summary>
        public virtual bool GetADSLocation(out Vector3 adsWorldLocation)
        {
            AnimatedModel mesh = GetRelevantWeaponMesh();
            if (mesh != null && !string.IsNullOrEmpty(ADSSightSocketName)
                && TryGetSocketTransform(mesh, ADSSightSocketName, out Transform socketWorld))
            {
                adsWorldLocation = socketWorld.Translation + ADSAdditionalOffset;
                return true;
            }
            adsWorldLocation = Actor != null ? Actor.Position : Vector3.Zero;
            return false;
        }

        /// <summary>
        /// 觜点相对根骨骼的位置。对应 UE5 GetADSRelativeLocation（cpp:22-39）：
        /// 把世界 ADS 点变换回根骨骼空间（等价 TransformToBoneSpace(RootBone, WorldLocation)）。
        /// </summary>
        public virtual Vector3 GetADSRelativeLocation()
        {
            AnimatedModel mesh = GetRelevantWeaponMesh();
            if (mesh == null) return Vector3.Zero;

            if (!GetADSLocation(out Vector3 adsWorld)) return Vector3.Zero;
            Transform rootPose = GetRootBonePose(mesh);
            Transform relative = WorldToRootSpace(new Transform(adsWorld, rootPose.Orientation, Vector3.One), rootPose);
            return relative.Translation;
        }

        /// <summary>
        /// 枪口世界位置 —— 本类的"每发瞄准位置/发射点"。对应 UE5 里枪械在 BP 通过 muzzle socket 取射点，
        /// 端口显式提供，供开火 FX 与投射物起点共用。
        /// </summary>
        public virtual bool GetMuzzleLocation(out Vector3 muzzleWorldLocation)
        {
            AnimatedModel mesh = GetRelevantWeaponMesh();
            if (mesh != null && !string.IsNullOrEmpty(MuzzleSocketName)
                && TryGetSocketTransform(mesh, MuzzleSocketName, out Transform socketWorld))
            {
                muzzleWorldLocation = socketWorld.Translation;
                return true;
            }
            muzzleWorldLocation = Actor != null ? Actor.Position : Vector3.Zero;
            return false;
        }

        // ===== 开火消费 FX =====

        /// <summary>
        /// 轮询 WeaponOwner 的开火时刻推进；推进一次即消费一次枪械 FX。
        /// Flax 无 UE5 的 GameplayEvent/委托广播，这里用"武器数据时间戳"作为开火边沿的最小可靠信号。
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();

            var weapon = WeaponOwner;
            if (weapon == null) return;

            float attackTime = weapon.LastAttackTime;
            if (attackTime <= _lastObservedAttackTime) return;
            _lastObservedAttackTime = attackTime;

            FireShot();
        }

        /// <summary>一次开火的表现：解析枪口位置（更新 LastMuzzleLocation）→ 播枪口闪光 / 抛壳。</summary>
        protected virtual void FireShot()
        {
            GetMuzzleLocation(out Vector3 muzzle);
            LastMuzzleLocation = muzzle;

            var fx = ResolveWeaponFX();
            if (fx == null) return;

            PlaySlotAtMuzzle(fx, EWeaponFXSlot.MuzzleFlash, muzzle);
            PlaySlotAtMuzzle(fx, EWeaponFXSlot.ShellEject, muzzle);
        }

        /// <summary>在枪口基准上叠加 slot 的 socket/偏移后播放一次性 FX。</summary>
        private void PlaySlotAtMuzzle(NarrativeWeaponFX fx, EWeaponFXSlot slot, Vector3 muzzle)
        {
            var entry = fx.GetSlot(slot);
            if (entry == null || entry.bLoopWhileActive) return;

            Transform base_ = Actor != null ? Actor.Transform : Transform.Identity;
            // 用枪口世界位置替换平移分量（Transform 是 struct，重新构造而非改字段，避免依赖未证实的 setter）
            base_ = new Transform(muzzle, base_.Orientation, base_.Scale);
            Transform attach = ApplySocketOffset(base_, entry);
            NarrativeWeaponFXPlayback.PlayOneShot(entry, attach);
        }
    }
}
