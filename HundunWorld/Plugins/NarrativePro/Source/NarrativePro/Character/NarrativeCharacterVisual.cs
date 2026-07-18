using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.CharacterCreator;
using NarrativePro.Items;

namespace NarrativePro.Character
{
    /// <summary>
    /// 角色外观应用事件。</summary>
    public delegate void CharacterAppearanceEvent();

    /// <summary>
    /// 角色视觉组件。对应 UE5 ANarrativeCharacterVisual。
    /// 分离角色的外观行为，处理外观资产加载与应用、武器/衣物视觉、第一/第三人称切换。
    /// 简化点：
    /// - 异步加载改为同步（Flax 资源加载通常很快）
    /// - GroomComponent 无 Flax 等价物，保留占位
    /// - IAbilitySystemInterface 用字符串 ID 占位（GAS Phase 7 实现）
    /// - 武器视觉 Actor 简化为引用持有
    /// - Flax 中无 USceneComponent 等价物，使用 Actor 作为视觉根容器
    /// </summary>
    public class NarrativeCharacterVisual : Script
    {
        /// <summary>视觉根 Actor（所有 mesh Actor 挂在其下）。Flax 中用 Actor 替代 UE5 USceneComponent。</summary>
        public Actor CharacterVisualRoot;

        /// <summary>外观资产。</summary>
        public CharacterAppearance AppearanceAsset;

        /// <summary>外观属性集合（运行时实际使用）。</summary>
        public CharacterCreatorAttributeSet AppearanceAttributeSet;

        /// <summary>所属角色。</summary>
        public Actor OwnerCharacter;

        /// <summary>第一人称时是否隐藏上半身。</summary>
        public bool bHideUpperBodyInFirstPerson = true;

        /// <summary>上半身隐藏的起始骨骼名。</summary>
        public string UpperBodyHideBone = "Spine";

        /// <summary>各 slot 的骨骼网格组件（按 GameplayTag 索引）。</summary>
        public List<MeshComponentEntry> MeshComponents = new List<MeshComponentEntry>();

        /// <summary>各 slot 的静态网格组件。</summary>
        public List<StaticMeshComponentEntry> StaticMeshComponents = new List<StaticMeshComponentEntry>();

        /// <summary>各 slot 的 Groom 组件（Flax 无原生毛发系统，保留占位）。</summary>
        public List<GroomComponentEntry> GroomComponents = new List<GroomComponentEntry>();

        /// <summary>已生成的武器视觉 Actor（按武器 slot 索引）。</summary>
        public List<WeaponVisualEntry> SpawnedWeaponVisuals = new List<WeaponVisualEntry>();

        /// <summary>外观基础应用完成事件。</summary>
        public event CharacterAppearanceEvent OnBaseAppearanceApplied;

        /// <summary>基础外观是否已加载。</summary>
        public bool bBaseAppearanceLoaded = false;

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();
            if (CharacterVisualRoot == null)
            {
                // Flax 中无 SceneComponent，使用空 Actor 作为视觉根容器
                CharacterVisualRoot = Actor.AddChild<Actor>();
                CharacterVisualRoot.Name = "CharacterVisualRoot";
            }
        }

        public override void OnDisable()
        {
            // 清理所有动态创建的 mesh
            MeshComponents.Clear();
            StaticMeshComponents.Clear();
            GroomComponents.Clear();
            SpawnedWeaponVisuals.Clear();
            base.OnDisable();
        }

        // ===== 初始化 =====

        /// <summary>从角色和外观资产初始化。</summary>
        public virtual void InitializeFromCharacterAndAppearance(Actor narrativeCharacter, CharacterAppearance appearance)
        {
            OwnerCharacter = narrativeCharacter;
            AppearanceAsset = appearance;
            if (appearance != null)
            {
                AppearanceAttributeSet = appearance.GetAppearanceAttributes();
                ApplyAppearanceAttributeSet(AppearanceAttributeSet);
            }
            BaseAppearanceApplied();
        }

        /// <summary>从角色和原始属性集合初始化（角色创建器数据用）。</summary>
        public virtual void InitializeFromCharacterAndAttributes(Actor narrativeCharacter, CharacterCreatorAttributeSet attributes)
        {
            OwnerCharacter = narrativeCharacter;
            AppearanceAttributeSet = attributes;
            ApplyAppearanceAttributeSet(attributes);
            BaseAppearanceApplied();
        }

        /// <summary>应用外观属性集合到 mesh。</summary>
        protected virtual void ApplyAppearanceAttributeSet(CharacterCreatorAttributeSet attributes)
        {
            if (attributes == null) return;

            // 应用基础网格
            if (attributes.BaseMesh != null)
            {
                var skinnedModel = GetOrCreateMeshComponent(NarrativeGameplayTags.Equipment.Slot);
                if (skinnedModel != null)
                {
                    // Flax 中 AnimatedModel 用 SkinnedModel 属性设置资产
                    skinnedModel.SkinnedModel = attributes.BaseMesh;
                    skinnedModel.IsActive = !attributes.bHideBaseMesh;
                }
            }

            // 应用各 slot 网格
            if (attributes.MeshEntries != null)
            {
                foreach (var entry in attributes.MeshEntries)
                {
                    if (entry?.Attribute != null)
                    {
                        SetMeshAppearance(entry.Slot, entry.Attribute);
                    }
                }
            }

            // 应用 Groom
            if (attributes.GroomEntries != null)
            {
                foreach (var entry in attributes.GroomEntries)
                {
                    if (entry?.Attribute != null)
                    {
                        SetGroomAppearance(entry.Slot, entry.Attribute);
                    }
                }
            }

            // TODO [待源码]: 获取 UE5 源的 Morph 绑定逻辑并验证 Flax SkinnedModel 的 morph target API 后补全
        }

        /// <summary>基础外观应用完成。</summary>
        public virtual void BaseAppearanceApplied()
        {
            bBaseAppearanceLoaded = true;
            OnBaseAppearanceApplied?.Invoke();
        }

        // ===== Mesh 管理 =====

        /// <summary>获取指定 slot 的骨骼网格 Actor（Flax 中为 AnimatedModel）。</summary>
        public AnimatedModel GetMeshComponent(GameplayTag slot)
        {
            if (MeshComponents == null) return null;
            foreach (var e in MeshComponents)
            {
                if (e != null && e.Slot == slot) return e.Component as AnimatedModel;
            }
            return null;
        }

        /// <summary>获取或创建指定 slot 的骨骼网格 Actor。Flax 中 AnimatedModel 是 Actor 子类。</summary>
        public AnimatedModel GetOrCreateMeshComponent(GameplayTag slot)
        {
            var existing = GetMeshComponent(slot);
            if (existing != null) return existing;

            var parent = CharacterVisualRoot ?? Actor;
            var comp = parent.AddChild<AnimatedModel>();
            comp.Name = "Mesh_" + slot;
            MeshComponents.Add(new MeshComponentEntry { Slot = slot, Component = comp });
            return comp;
        }

        /// <summary>获取指定 slot 的静态网格 Actor。</summary>
        public StaticModel GetStaticMeshComponent(GameplayTag slot)
        {
            if (StaticMeshComponents == null) return null;
            foreach (var e in StaticMeshComponents)
            {
                if (e != null && e.Slot == slot) return e.Component as StaticModel;
            }
            return null;
        }

        /// <summary>获取或创建指定 slot 的静态网格 Actor。Flax 中 StaticModel 是 Actor。</summary>
        public StaticModel GetOrCreateStaticMeshComponent(GameplayTag slot)
        {
            var existing = GetStaticMeshComponent(slot);
            if (existing != null) return existing;

            var parent = CharacterVisualRoot ?? Actor;
            var comp = parent.AddChild<StaticModel>();
            comp.Name = "StaticMesh_" + slot;
            StaticMeshComponents.Add(new StaticMeshComponentEntry { Slot = slot, Component = comp });
            return comp;
        }

        /// <summary>设置 mesh 外观。</summary>
        public virtual void SetMeshAppearance(GameplayTag slot, CharacterCreatorAttribute_Mesh meshData)
        {
            if (meshData == null) return;

            if (meshData.bIsStaticMesh)
            {
                var staticMesh = GetOrCreateStaticMeshComponent(slot);
                if (staticMesh != null && meshData.StaticMesh != null)
                {
                    staticMesh.Model = meshData.StaticMesh;
                }
            }
            else
            {
                var skinnedMesh = GetOrCreateMeshComponent(slot);
                if (skinnedMesh != null && meshData.Mesh != null)
                {
                    // Flax AnimatedModel.SkinnedModel 设置资产
                    skinnedMesh.SkinnedModel = meshData.Mesh;
                }
            }

            // TODO [待源码]: 获取 UE5 源的材质参数绑定/Socket 附加逻辑后补全（MeshMaterials/Morphs/MeshAttachSocket/MeshAttachOffset）
        }

        /// <summary>重置 mesh 到基础外观。</summary>
        public virtual void ResetMeshToBaseAppearance(GameplayTag slot)
        {
            var mesh = GetMeshComponent(slot);
            if (mesh != null && AppearanceAttributeSet != null)
            {
                // 重置为基础网格
                if (AppearanceAttributeSet.BaseMesh != null)
                {
                    mesh.SkinnedModel = AppearanceAttributeSet.BaseMesh;
                }
            }
        }

        /// <summary>设置 Groom 外观（Flax 无原生毛发系统，保留占位）。</summary>
        public virtual void SetGroomAppearance(GameplayTag slot, CharacterCreatorAttribute_Groom groomData)
        {
            if (groomData == null) return;
            // Flax-不兼容: UE5 的 GroomComponent 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 GroomComponent 等价物
            NarrativePro.Core.NarrativeLog.LogWarning("Groom appearance not yet supported in Flax.");
        }

        // ===== 武器视觉 =====

        /// <summary>添加武器视觉 Actor。</summary>
        public bool AddWeaponVisual(string weaponItemPath)
        {
            if (string.IsNullOrEmpty(weaponItemPath)) return false;

            // Flax-已实现: 通过 Content.LoadAsync 加载 Prefab，由 PrefabManager.SpawnPrefab 生成武器视觉 Actor
            Prefab prefab = Content.LoadAsync<Prefab>(weaponItemPath);
            if (prefab == null)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"AddWeaponVisual: 加载 Prefab 失败：{weaponItemPath}");
                return false;
            }

            var parent = CharacterVisualRoot ?? Actor;
            Actor spawned = PrefabManager.SpawnPrefab(prefab, parent.Position, parent.Orientation);
            if (spawned == null)
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"AddWeaponVisual: 生成 Actor 失败：{weaponItemPath}");
                return false;
            }

            spawned.Parent = parent;
            return true;
        }

        /// <summary>移除武器视觉。</summary>
        public void RemoveWeaponVisual(GameplayTag weaponSlot)
        {
            if (SpawnedWeaponVisuals == null) return;
            SpawnedWeaponVisuals.RemoveAll(e => e == null || e.Slot == weaponSlot);
        }

        // ===== 视角切换 =====

        /// <summary>处理第一/第三人称视角更新。</summary>
        public virtual void HandlePerspectiveUpdate(bool bIsFirstPerson)
        {
            if (bIsFirstPerson && bHideUpperBodyInFirstPerson)
            {
                HideUpperBody(true);
            }
            else
            {
                HideUpperBody(false);
            }
        }

        /// <summary>隐藏/显示上半身。</summary>
        public virtual void HideUpperBody(bool bWantsHide)
        {
            // Flax-不兼容: UE5 的 MasterPoseComponent 骨骼隐藏 API 在 Flax 无对应物，保留占位。原文 TODO: 通过 SkinnedModel 的骨骼隐藏 API 实现
            // 简化版：仅记录日志
            if (bWantsHide)
            {
                NarrativePro.Core.NarrativeLog.Log($"HideUpperBody requested on bone {UpperBodyHideBone}");
            }
        }

        // ===== 便捷方法 =====

        /// <summary>获取主 mesh。</summary>
        public AnimatedModel GetMainMesh()
        {
            return GetMeshComponent(NarrativeGameplayTags.Equipment.Slot);
        }

        /// <summary>获取 leader mesh（驱动其他 mesh）。</summary>
        public virtual AnimatedModel GetLeaderMesh()
        {
            return GetMainMesh();
        }

        /// <summary>是否本地控制。</summary>
        public bool IsLocallyControlled()
        {
            // TODO [需接入网络/玩家控制系统]: 接入玩家控制器判断本地控制权
            return true;
        }
    }

    /// <summary>Mesh 组件条目。</summary>
    [Serializable]
    public class MeshComponentEntry
    {
        public GameplayTag Slot;
        public Actor Component;
    }

    /// <summary>静态 Mesh 组件条目。</summary>
    [Serializable]
    public class StaticMeshComponentEntry
    {
        public GameplayTag Slot;
        public Actor Component;
    }

    /// <summary>Groom 组件条目。</summary>
    [Serializable]
    public class GroomComponentEntry
    {
        public GameplayTag Slot;
        public Actor Component;
    }

    /// <summary>武器视觉条目。</summary>
    [Serializable]
    public class WeaponVisualEntry
    {
        public GameplayTag Slot;
        public Actor WeaponVisualActor;
    }
}
