using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 武器动画姿态快照。移植自 UE5 NarrativeArsenal: Weapons/WeaponAnimPose.h（FWeaponAnimPose）。
    /// 记录骨骼/曲线/Socket 在某一时刻的姿态数据，用于武器碰撞检测与动画对齐。
    /// 简化点：FName → string；FTransform → FlaxEngine.Transform。
    /// </summary>
    [Serializable]
    public class WeaponAnimPose
    {
        /// <summary>骨骼名称列表</summary>
        public List<string> BoneNames { get; set; } = new List<string>();

        /// <summary>骨骼索引列表</summary>
        public List<int> BoneIndices { get; set; } = new List<int>();

        /// <summary>父骨骼索引列表</summary>
        public List<int> ParentBoneIndices { get; set; } = new List<int>();

        /// <summary>本地空间姿态列表</summary>
        public List<Transform> LocalSpacePoses { get; set; } = new List<Transform>();

        /// <summary>世界空间姿态列表</summary>
        public List<Transform> WorldSpacePoses { get; set; } = new List<Transform>();

        /// <summary>参考本地空间姿态列表</summary>
        public List<Transform> RefLocalSpacePoses { get; set; } = new List<Transform>();

        /// <summary>参考世界空间姿态列表</summary>
        public List<Transform> RefWorldSpacePoses { get; set; } = new List<Transform>();

        /// <summary>曲线名称列表</summary>
        public List<string> CurveNames { get; set; } = new List<string>();

        /// <summary>曲线数值列表</summary>
        public List<float> CurveValues { get; set; } = new List<float>();

        /// <summary>Socket 名称列表</summary>
        public List<string> SocketNames { get; set; } = new List<string>();

        /// <summary>Socket 父骨骼名称列表</summary>
        public List<string> SocketParentBoneNames { get; set; } = new List<string>();

        /// <summary>Socket 变换列表</summary>
        public List<Transform> SocketTransforms { get; set; } = new List<Transform>();
    }

    /// <summary>
    /// 动画通知事件引用占位结构。移植自 UE5 FAnimNotifyEventReference。
    /// Flax 无 UE5 AnimNotify 系统，此为占位类型，供 WeaponVisual / AnimNotifyRefObject 引用。
    /// Flax-不兼容: UE5 的 AnimNotifyEventReference 在 Flax 无对应物，保留占位。原文 TODO: 接入 Flax 动画通知系统后填充实际字段。
    /// </summary>
    [Serializable]
    public struct AnimNotifyEventReference
    {
        /// <summary>占位：通知名称</summary>
        public string NotifyName;
    }

    /// <summary>
    /// 动画通知引用对象。移植自 UE5 UAnimNotifyRefObject（UObject）。
    /// 持有一个 AnimNotifyEventReference。Flax 无对应物，按 [Serializable] 占位类实现。
    /// </summary>
    [Serializable]
    public class AnimNotifyRefObject
    {
        /// <summary>所持有的动画通知事件引用</summary>
        public AnimNotifyEventReference NotifyEventReference;
    }

    /// <summary>
    /// 伤害状态数据。移植自 UE5 FDamageStateData。
    /// 源 .h 未随包提供，具体字段未知，此处为占位。
    /// Flax-待源码: 获取 UE5 源 FDamageStateData 字段后补全。
    /// </summary>
    [Serializable]
    public class DamageStateData
    {
        // TODO [待源码]: 获取 UE5 源 FDamageStateData 字段后补全实现
    }

    /// <summary>
    /// 伤害状态数据容器。移植自 UE5 FDamageStateDataContainer。
    /// 源 .h 未随包提供，具体字段未知，此处为占位。
    /// Flax-待源码: 获取 UE5 源 FDamageStateDataContainer 字段后补全。
    /// </summary>
    [Serializable]
    public class DamageStateDataContainer
    {
        // TODO [待源码]: 获取 UE5 源 FDamageStateDataContainer 字段后补全实现
    }

    /// <summary>
    /// 武器碰撞数据。移植自 UE5 FWeaponCollisionData。
    /// 被 WeaponVisual.CollisionData（TArray&lt;FWeaponCollisionData&gt;）引用。
    /// 源 .h 未随包提供，具体字段未知，此处为占位。
    /// Flax-待源码: 获取 UE5 源 FWeaponCollisionData 字段后补全。
    /// </summary>
    [Serializable]
    public class WeaponCollisionData
    {
        // TODO [待源码]: 获取 UE5 源 FWeaponCollisionData 字段后补全实现
    }
}
