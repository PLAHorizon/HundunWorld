using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 武器视觉 Actor。移植自 UE5 NarrativeArsenal: Weapons/WeaponVisual.h（AWeaponVisual : AActor）。
    /// 负责武器网格、动画层、配件网格、碰撞检测与命中扫描的可视化与逻辑承载。
    ///
    /// 简化点：
    /// - AActor → Flax Script 挂载到武器视觉 Actor。
    /// - 移除 UE5 复制（WeaponOwner 的 OnRep 改为本地方法 OnRep_WeaponOwner）。
    /// - USkeletalMeshComponent → FlaxEngine.AnimatedModel；UStaticMeshComponent → StaticModel；UStaticMesh → StaticMesh。
    /// - UPrimitiveComponent（无直接对应）→ 用 Actor 占位。
    /// - TSubclassOf&lt;UAnimInstance&gt; → string 路径占位。
    /// - FGameplayTag → NarrativePro.Items.GameplayTag；FHitResult → FlaxEngine.RayCastHit。
    /// - FAnimNotifyEventReference / FWeaponCollisionData 见 WeaponAnimPose.cs。
    /// 方法体待获取源 .cpp 后补全（源 .cpp 未随包提供）。
    /// Flax-待源码: 获取 UE5 源 WeaponVisual.cpp 后补全实现。
    /// </summary>
    public class WeaponVisual : Script
    {
        /// <summary>武器骨骼网格组件（对应 UE5 WeaponMesh）</summary>
        public AnimatedModel WeaponMesh { get; set; }

        /// <summary>本地空间武器骨骼网格组件（对应 UE5 LocalWeaponMesh）</summary>
        public AnimatedModel LocalWeaponMesh { get; set; }

        /// <summary>默认武器动画层（TSubclassOf&lt;UAnimInstance&gt; → string 路径）</summary>
        public string DefaultWeaponAnimLayer { get; set; } = "";

        /// <summary>双持武器动画层</summary>
        public string DualWieldWeaponAnimLayer { get; set; } = "";

        /// <summary>第一人称武器动画层</summary>
        public string Weapon1PAnimLayer { get; set; } = "";

        /// <summary>第一人称双持武器动画层</summary>
        public string DualWieldWeapon1PAnimLayer { get; set; } = "";

        /// <summary>角色形态特定的动画层（形态标签 → 动画层路径）</summary>
        public Dictionary<GameplayTag, string> FormSpecificLayers { get; set; } = new Dictionary<GameplayTag, string>();

        /// <summary>本次攻击已命中的 Actor 缓存（对应 UE5 CachedHitActors）</summary>
        public List<Actor> CachedHitActors { get; set; } = new List<Actor>();

        /// <summary>配件槽位 → 配件网格组件（UStaticMeshComponent → StaticModel）</summary>
        public Dictionary<GameplayTag, StaticModel> AttachmentMeshComps { get; set; } = new Dictionary<GameplayTag, StaticModel>();

        /// <summary>本地空间配件网格组件映射</summary>
        public Dictionary<GameplayTag, StaticModel> LocalAttachmentMeshComps { get; set; } = new Dictionary<GameplayTag, StaticModel>();

        /// <summary>配件槽位 → 默认静态网格资源（UStaticMesh → FlaxEngine.Model）</summary>
        public Dictionary<GameplayTag, Model> AttachmentMeshDefaultMeshes { get; set; } = new Dictionary<GameplayTag, Model>();

        /// <summary>当前动画通知事件引用（对应 UE5 CurrentNotifyEvent）</summary>
        public AnimNotifyEventReference CurrentNotifyEvent { get; set; }

        /// <summary>武器碰撞数据列表（对应 UE5 CollisionData）</summary>
        public List<WeaponCollisionData> CollisionData { get; set; } = new List<WeaponCollisionData>();

        /// <summary>拥有此武器视觉的角色（对应 UE5 CharacterOwner，ANarrativeCharacter* → Actor）</summary>
        public Actor CharacterOwner { get; set; }

        /// <summary>拥有此视觉的武器物品（对应 UE5 WeaponOwner，原为复制属性）</summary>
        public WeaponItem WeaponOwner { get; set; }

        /// <summary>返回所有参与碰撞的图元组件（UE5 UPrimitiveComponent* → Actor）。</summary>
        public virtual List<Actor> GetCollidingPrimitives_Implementation()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
            return new List<Actor>();
        }

        /// <summary>返回所有参与碰撞的图元组件。</summary>
        public List<Actor> GetCollidingPrimitives()
        {
            return GetCollidingPrimitives_Implementation();
        }

        /// <summary>返回所有武器骨骼网格组件。</summary>
        public virtual List<AnimatedModel> GetWeaponMeshes()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现。默认返回包含 WeaponMesh 的列表。
            var meshes = new List<AnimatedModel>();
            if (WeaponMesh != null) meshes.Add(WeaponMesh);
            return meshes;
        }

        /// <summary>返回当前视角下相关的武器骨骼网格组件。</summary>
        public virtual AnimatedModel GetRelevantWeaponMesh()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现。默认返回 WeaponMesh。
            return WeaponMesh;
        }

        /// <summary>返回武器覆盖动画层（TSubclassOf&lt;UAnimInstance&gt; → string 路径）。</summary>
        public virtual string GetWeaponOverlayLayer()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现。默认返回 DefaultWeaponAnimLayer。
            return DefaultWeaponAnimLayer;
        }

        /// <summary>处理添加配件（实现）。</summary>
        public virtual void HandleAddAttachment_Implementation(WeaponAttachmentItem attachment, WeaponAttachmentSlotConfig weaponSlotConfig)
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
        }

        /// <summary>处理添加配件。</summary>
        public void HandleAddAttachment(WeaponAttachmentItem attachment, WeaponAttachmentSlotConfig weaponSlotConfig)
        {
            HandleAddAttachment_Implementation(attachment, weaponSlotConfig);
        }

        /// <summary>处理移除配件（实现）。</summary>
        public virtual void HandleRemoveAttachment_Implementation(WeaponAttachmentItem attachment)
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
        }

        /// <summary>处理移除配件。</summary>
        public void HandleRemoveAttachment(WeaponAttachmentItem attachment)
        {
            HandleRemoveAttachment_Implementation(attachment);
        }

        /// <summary>缓存碰撞数据。</summary>
        public virtual void CacheCollisionData()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
        }

        /// <summary>清理攻击数据。</summary>
        public virtual void CleanupAttackData()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
            CachedHitActors.Clear();
        }

        /// <summary>执行碰撞检测，结果写入 outHits。</summary>
        public virtual void PerformCollisionCheck(List<RayCastHit> outHits)
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
        }

        /// <summary>缓存动画变换。</summary>
        public virtual void CacheAnimationTransform()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
        }

        /// <summary>扫描命中。沿胶囊体从 start 到 end 扫描，结果写入 outHits。</summary>
        public virtual void SweepForHits(Vector3 start, Vector3 end, Quaternion rot, Vector3 capsuleSize, List<RayCastHit> outHits)
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现。Flax 中可使用 Physics.Sweep 等 API。
        }

        /// <summary>处理视角更新（第一/第三人称切换等）。</summary>
        public virtual void HandlePerspectiveUpdate()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
        }

        /// <summary>注册默认配件。</summary>
        public virtual void RegisterDefaultAttachment()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现
        }

        /// <summary>WeaponOwner 复制回调（UE5 OnRep_WeaponOwner）。本地改为普通方法。</summary>
        public virtual void OnRep_WeaponOwner()
        {
            // TODO [待源码]: 获取 UE5 源 WeaponVisual.cpp 后补全实现（原为复制属性变更回调）。
        }
    }
}
