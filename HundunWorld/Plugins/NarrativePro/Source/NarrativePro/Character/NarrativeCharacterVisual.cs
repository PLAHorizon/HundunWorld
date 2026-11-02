using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.CharacterCreator;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

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

        /// <summary>各 slot 当前施加的隐藏（slot → 被其网格 HideSlots 隐藏的 slot 列表）。对应 UE5 CurrentHides。</summary>
        protected readonly Dictionary<GameplayTag, List<GameplayTag>> CurrentHides =
            new Dictionary<GameplayTag, List<GameplayTag>>();

        /// <summary>骨骼跟随绑定列表（Socket 附着等价实现）。</summary>
        protected readonly List<BoneFollowerBinding> BoneFollowers = new List<BoneFollowerBinding>();

        /// <summary>本地（1P）动画图（由 BaseLocalMeshAnimBPPath 加载，供游戏侧 1P 网格使用）。</summary>
        public AnimationGraph BaseLocalAnimGraph;

        /// <summary>徒手动画图（由 UnarmedAnimLayerPath 加载）。</summary>
        public AnimationGraph UnarmedAnimGraph;

        /// <summary>上半身是否已隐藏（骨骼塌缩模式）。</summary>
        private bool _bUpperBodyHidden = false;

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
            // 解除骨骼塌缩钩子
            if (_bUpperBodyHidden)
            {
                var mesh = GetMainMesh();
                if (mesh != null) mesh.AnimationUpdated -= ApplyUpperBodyCollapse;
                _bUpperBodyHidden = false;
            }

            // 清理所有动态创建的 mesh
            BoneFollowers.Clear();
            CurrentHides.Clear();
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
                // 对应 UE5 GetAppearanceAttributes(Requester)：以请求角色的随机种子驱动变化，保证确定性
                int seed = 0;
                if (narrativeCharacter != null)
                {
                    var character = narrativeCharacter.GetScript<NarrativeCharacter>();
                    if (character != null) seed = character.GetCharacterRandomSeed();
                }
                AppearanceAttributeSet = appearance.GetAppearanceAttributes(seed);
                ApplyAppearanceAttributeSet(AppearanceAttributeSet);
            }
            BaseAppearanceApplied();
        }

        /// <summary>从角色和原始属性集合初始化（角色创建器数据用）。</summary>
        public virtual void InitializeFromCharacterAndAttributes(Actor narrativeCharacter, CharacterCreatorAttributeSet attributes)
        {
            OwnerCharacter = narrativeCharacter;
            AppearanceAttributeSet = attributes;

            // 重置此前可能已设置的 slot 网格（对应 UE5 InitializeFromCharacterAndAttributes 中 SetSkeletalMesh(nullptr)）
            if (attributes != null && attributes.BaseMesh != null && MeshComponents != null)
            {
                foreach (var e in MeshComponents)
                {
                    if (e?.Component is AnimatedModel am && e.Slot != NarrativeGameplayTags.Equipment.Slot)
                    {
                        am.SkinnedModel = null;
                    }
                }
            }

            // 将表单标签登记为角色的宽松 gameplay tag（对应 UE5 ASC->SetLooseGameplayTagCount(FormTag, 1)）
            if (attributes != null && attributes.FormTag.IsValid() && narrativeCharacter != null)
            {
                var asc = Core.ArsenalStatics.GetNarrativeAbilitySystemComponent(narrativeCharacter);
                asc?.GameplayTags.AddTag(attributes.FormTag);
            }

            ApplyAppearanceAttributeSet(attributes);
            BaseAppearanceApplied();
        }

        /// <summary>应用外观属性集合到 mesh。</summary>
        protected virtual void ApplyAppearanceAttributeSet(CharacterCreatorAttributeSet attributes)
        {
            if (attributes == null) return;

            CurrentHides.Clear();

            // 应用基础网格
            if (attributes.BaseMesh != null)
            {
                var skinnedModel = GetOrCreateMeshComponent(NarrativeGameplayTags.Equipment.Slot);
                if (skinnedModel != null)
                {
                    // Flax 中 AnimatedModel 用 SkinnedModel 属性设置资产
                    skinnedModel.SkinnedModel = attributes.BaseMesh;
                    skinnedModel.IsActive = !attributes.bHideBaseMesh;

                    // 基础网格动画图（对应 UE5 BaseMeshAnimBP）：Flax 中为 AnimationGraph 资产
                    ApplyAnimationGraph(skinnedModel, attributes.BaseMeshAnimBPPath);
                }

                // 本地（1P）动画图路径记录：Flax 无独立“第一人称本地网格”体系，
                // 由游戏侧以 1P slot 网格 + BaseLocalMeshAnimBPPath 自行创建（见 ApplyAnimationGraph 用法）。
                if (!string.IsNullOrEmpty(attributes.BaseLocalMeshAnimBPPath))
                {
                    BaseLocalAnimGraph = Content.Load<AnimationGraph>(attributes.BaseLocalMeshAnimBPPath);
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

            // 应用全局 Morph 列表（对应 UE5 FCharacterCreatorAttributeSet.Morphs）：
            // Flax 中 morph target 等价物为 AnimatedModel 的 BlendShape，按名设置权重。
            if (attributes.Morphs != null)
            {
                var mainMesh = GetMainMesh();
                if (mainMesh != null)
                {
                    foreach (var morph in attributes.Morphs)
                    {
                        if (morph == null || string.IsNullOrEmpty(morph.MorphName)) continue;
                        mainMesh.SetBlendShapeWeight(morph.MorphName, Mathf.Clamp(morph.MorphValue, 0f, 1f));
                    }
                }
            }

            // 徒手动画层：Flax 1.12 无运行时动画层混合 API（动画层只能在 AnimationGraph 资产内图化），
            // 此处加载并挂载为兜底动画图，游戏侧可在图内用同一资产实现层混合。
            if (!string.IsNullOrEmpty(attributes.UnarmedAnimLayerPath))
            {
                UnarmedAnimGraph = Content.Load<AnimationGraph>(attributes.UnarmedAnimLayerPath);
                var mainMeshForLayer = GetMainMesh();
                if (UnarmedAnimGraph != null && mainMeshForLayer != null && mainMeshForLayer.AnimationGraph == null)
                {
                    mainMeshForLayer.AnimationGraph = UnarmedAnimGraph;
                }
            }
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

        /// <summary>设置 mesh 外观。对应 UE5 SetMeshAppearance（NarrativeCharacterVisual.cpp:1188-1402）。</summary>
        public virtual void SetMeshAppearance(GameplayTag slot, CharacterCreatorAttribute_Mesh meshData)
        {
            if (meshData == null) return;

            // 处理隐藏 slot（对应 UE5 CurrentHides）：先恢复本 slot 此前的隐藏，再应用新的隐藏要求
            ApplySlotHides(slot, meshData);

            if (meshData.bIsStaticMesh)
            {
                var staticMesh = GetOrCreateStaticMeshComponent(slot);
                if (staticMesh != null && meshData.StaticMesh != null)
                {
                    staticMesh.Model = meshData.StaticMesh;

                    // Socket 附着：Flax 无 Socket，按骨骼名每帧跟随主网格骨骼（见 BoneFollowerUpdate）
                    if (!string.IsNullOrEmpty(meshData.MeshAttachSocket))
                    {
                        RegisterBoneFollower(staticMesh, meshData.MeshAttachSocket, meshData.MeshAttachOffset);
                    }
                    else
                    {
                        UnregisterBoneFollower(staticMesh);
                        staticMesh.LocalTransform = meshData.MeshAttachOffset;
                    }

                    ApplyMeshMaterials(staticMesh, meshData.MeshMaterials);
                }
            }
            else
            {
                var skinnedMesh = GetOrCreateMeshComponent(slot);
                if (skinnedMesh != null)
                {
                    // 1P slot 优先使用 Mesh1P（对应 UE5 Is1PMeshTag 分支）
                    SkinnedModel meshAsset = meshData.Mesh;
                    if (Is1PMeshTag(slot) && meshData.Mesh1P != null)
                    {
                        meshAsset = meshData.Mesh1P;
                    }

                    if (meshAsset != null)
                    {
                        skinnedMesh.SkinnedModel = meshAsset;
                    }

                    // Socket 附着
                    if (!string.IsNullOrEmpty(meshData.MeshAttachSocket))
                    {
                        RegisterBoneFollower(skinnedMesh, meshData.MeshAttachSocket, meshData.MeshAttachOffset);
                    }
                    else
                    {
                        UnregisterBoneFollower(skinnedMesh);
                        skinnedMesh.LocalTransform = meshData.MeshAttachOffset;
                    }

                    // Leader Pose（对应 UE5 SetLeaderPoseComponent）：Flax 1.12 提供 SetMasterPoseModel
                    if (meshData.bUseLeaderPose)
                    {
                        skinnedMesh.SetMasterPoseModel(GetLeaderMesh());
                    }
                    else
                    {
                        skinnedMesh.SetMasterPoseModel(null);
                        ApplyAnimationGraph(skinnedMesh, meshData.MeshAnimBPPath);
                    }

                    ApplyMeshMaterials(skinnedMesh, meshData.MeshMaterials);

                    // Morph：Flax 等价物为 BlendShape（对应 UE5 SetMorphTarget）
                    if (meshAsset != null && meshData.Morphs != null)
                    {
                        foreach (var morph in meshData.Morphs)
                        {
                            if (morph == null || !morph.ScalarTag.IsValid() || morph.MorphNames == null) continue;
                            float morphVal = AppearanceAttributeSet != null
                                ? AppearanceAttributeSet.GetScalarValue(morph.ScalarTag)
                                : 0f;
                            foreach (string morphName in morph.MorphNames)
                            {
                                if (!string.IsNullOrEmpty(morphName))
                                {
                                    skinnedMesh.SetBlendShapeWeight(morphName, Mathf.Clamp(morphVal, 0f, 1f));
                                }
                            }
                        }
                    }

                    // 保持 1P 手/躯干与 3P 网格同步（对应 UE5 OnMeshAppearanceReady(Torso_1P/Hands_1P)）
                    if (IsLocallyControlled() &&
                        (slot == NarrativeGameplayTags.Equipment.Slot_Torso || slot == NarrativeGameplayTags.Equipment.Slot_Hands))
                    {
                        var sync1P = new GameplayTag(slot.TagName + ".1P");
                        if (sync1P != slot && GetMeshComponent(sync1P) != null)
                        {
                            SetMeshAppearance(sync1P, meshData);
                        }
                    }
                }
            }
        }

        /// <summary>判断 slot 是否为 1P 网格 slot。对应 UE5 Is1PMeshTag。</summary>
        protected static bool Is1PMeshTag(GameplayTag slot)
        {
            return slot.TagName.EndsWith(".1P", StringComparison.Ordinal);
        }

        /// <summary>
        /// 应用网格材质与参数。对应 UE5 SetMeshAppearance 中对 MeshMaterials 的遍历：
        /// 无参数时直接 SetMaterial；有参数时创建动态材质实例并写入 向量/标量 参数
        /// （参数值来自 AppearanceAttributeSet 的 GetVectorValue/GetScalarValue）。
        /// </summary>
        protected virtual void ApplyMeshMaterials(ModelInstanceActor meshActor, List<CreatorMeshMaterial> materials)
        {
            if (meshActor == null || materials == null || materials.Count == 0) return;

            int entryCount = meshActor.Entries != null ? meshActor.Entries.Length : 0;
            for (int i = 0; i < materials.Count && i < entryCount; i++)
            {
                var meshMat = materials[i];
                if (meshMat == null) continue;

                MaterialBase material = ResolveMaterial(meshMat);
                bool bHasParams = (meshMat.VectorParams?.Count ?? 0) > 0 || (meshMat.ScalarParams?.Count ?? 0) > 0;

                if (!bHasParams)
                {
                    if (material != null) meshActor.SetMaterial(i, material);
                    continue;
                }

                // 对应 UE5 CreateDynamicMaterialInstance：Flax 用虚拟材质实例
                MaterialInstance instance = meshActor.CreateAndSetVirtualMaterialInstance(i);
                if (instance == null)
                {
                    if (material != null) meshActor.SetMaterial(i, material);
                    continue;
                }
                if (material != null && instance.BaseMaterial != material)
                {
                    instance.BaseMaterial = material;
                }

                if (meshMat.VectorParams != null && AppearanceAttributeSet != null)
                {
                    foreach (var vParam in meshMat.VectorParams)
                    {
                        if (vParam == null || !vParam.VectorTagID.IsValid() || vParam.ParameterNames == null) continue;
                        Color parameterValue = AppearanceAttributeSet.GetVectorValue(vParam.VectorTagID);
                        foreach (string parameterName in vParam.ParameterNames)
                        {
                            if (!string.IsNullOrEmpty(parameterName))
                            {
                                instance.SetParameterValue(parameterName, parameterValue, false);
                            }
                        }
                    }
                }

                if (meshMat.ScalarParams != null && AppearanceAttributeSet != null)
                {
                    foreach (var sParam in meshMat.ScalarParams)
                    {
                        if (sParam == null || !sParam.ScalarTagID.IsValid() || sParam.ParameterNames == null) continue;
                        float parameterValue = AppearanceAttributeSet.GetScalarValue(sParam.ScalarTagID);
                        foreach (string parameterName in sParam.ParameterNames)
                        {
                            if (!string.IsNullOrEmpty(parameterName))
                            {
                                instance.SetParameterValue(parameterName, parameterValue, false);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>解析材质：优先直接引用，其次按路径 Content.Load。</summary>
        protected virtual MaterialBase ResolveMaterial(CreatorMeshMaterial meshMat)
        {
            if (meshMat == null) return null;
            if (meshMat.Material != null) return meshMat.Material;
            if (!string.IsNullOrEmpty(meshMat.MaterialPath))
            {
                return Content.Load<Material>(meshMat.MaterialPath);
            }
            return null;
        }

        /// <summary>为网格应用动画图（对应 UE5 SetAnimInstanceClass；Flax 中为 AnimationGraph 资产）。</summary>
        protected virtual void ApplyAnimationGraph(AnimatedModel mesh, string animationGraphPath)
        {
            if (mesh == null || string.IsNullOrEmpty(animationGraphPath)) return;
            var graph = Content.Load<AnimationGraph>(animationGraphPath);
            if (graph != null)
            {
                mesh.AnimationGraph = graph;
            }
            else
            {
                NarrativePro.Core.NarrativeLog.LogWarning($"[CharacterVisual] AnimationGraph 加载失败: {animationGraphPath}");
            }
        }

        /// <summary>
        /// 应用某 slot 的隐藏需求。对应 UE5 SetMeshAppearance 中 CurrentHides 的维护
        /// （cpp:1176-1202）：切换网格时先恢复此 slot 此前隐藏的组件，再隐藏新网格要求的 slot。
        /// </summary>
        protected virtual void ApplySlotHides(GameplayTag slot, CharacterCreatorAttribute_Mesh meshData)
        {
            // 恢复此 slot 此前的隐藏
            if (CurrentHides.TryGetValue(slot, out List<GameplayTag> previousHides))
            {
                foreach (GameplayTag hiddenSlot in previousHides)
                {
                    bool bStillHiddenByOther = false;
                    foreach (var kvp in CurrentHides)
                    {
                        if (kvp.Key != slot && kvp.Value != null && kvp.Value.Contains(hiddenSlot))
                        {
                            bStillHiddenByOther = true;
                            break;
                        }
                    }

                    if (!bStillHiddenByOther)
                    {
                        var meshToUnhide = GetMeshComponent(hiddenSlot);
                        if (meshToUnhide != null)
                        {
                            meshToUnhide.IsActive = true;
                        }
                        else
                        {
                            var staticToUnhide = GetStaticMeshComponent(hiddenSlot);
                            if (staticToUnhide != null) staticToUnhide.IsActive = true;
                        }
                    }
                }

                CurrentHides.Remove(slot);
            }

            // 应用新网格的隐藏要求
            if (meshData.HideSlots != null && meshData.HideSlots.Count > 0)
            {
                var newHides = new List<GameplayTag>();
                foreach (GameplayTag hideSlot in meshData.HideSlots)
                {
                    if (!hideSlot.IsValid()) continue;
                    var meshToHide = GetMeshComponent(hideSlot);
                    if (meshToHide != null)
                    {
                        meshToHide.IsActive = false;
                        newHides.Add(hideSlot);
                        continue;
                    }

                    var staticToHide = GetStaticMeshComponent(hideSlot);
                    if (staticToHide != null)
                    {
                        staticToHide.IsActive = false;
                        newHides.Add(hideSlot);
                    }
                }

                if (newHides.Count > 0)
                {
                    CurrentHides[slot] = newHides;
                }
            }
        }

        // ===== 骨骼附着（Flax 无 Socket，用骨骼世界变换每帧驱动实现 SnapToTarget + Offset） =====

        /// <summary>注册子 Actor 对主网格指定骨骼的跟随。</summary>
        protected virtual void RegisterBoneFollower(Actor child, string boneName, Transform offset)
        {
            if (child == null || string.IsNullOrEmpty(boneName)) return;

            BoneFollowers.RemoveAll(b => b.Child == child);
            BoneFollowers.Add(new BoneFollowerBinding
            {
                Child = child,
                BoneName = boneName,
                Offset = offset
            });
        }

        /// <summary>注销子 Actor 的骨骼跟随。</summary>
        protected virtual void UnregisterBoneFollower(Actor child)
        {
            if (child == null) return;
            BoneFollowers.RemoveAll(b => b.Child == child);
        }

        /// <summary>每帧更新所有骨骼跟随项（在动画求值后执行，使用最新骨骼姿态）。</summary>
        public override void OnUpdate()
        {
            base.OnUpdate();

            if (BoneFollowers.Count == 0) return;

            var mainMesh = GetMainMesh();
            if (mainMesh == null) return;

            for (int i = BoneFollowers.Count - 1; i >= 0; i--)
            {
                var binding = BoneFollowers[i];
                // Flax 中 Actor 被销毁后 ==null 即为失效（无 Actor.IsValid）
                if (binding == null || binding.Child == null)
                {
                    BoneFollowers.RemoveAt(i);
                    continue;
                }

                mainMesh.GetNodeTransformation(binding.BoneName, out Matrix boneWorld, true);
                Transform boneTransform = new Transform(boneWorld);
                binding.Child.Transform = boneTransform.LocalToWorld(binding.Offset);
            }
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

        /// <summary>
        /// 设置 Groom 外观。UE5 GroomComponent 在 Flax 无对应物（已核对 Flax 1.12 引擎源码：
        /// Source/Engine 中无 groom/hair 渲染类），按最佳可行方案实现：
        /// 若 GroomAssetPath 指向网格资产（SkinnedModel/Model，常见于导出为卡片头发的 groom 资源），
        /// 则作为跟随主网格骨骼的网格渲染并应用 groom 材质；否则记录告警。
        /// </summary>
        public virtual void SetGroomAppearance(GameplayTag slot, CharacterCreatorAttribute_Groom groomData)
        {
            if (groomData == null) return;

            if (string.IsNullOrEmpty(groomData.GroomAssetPath))
            {
                NarrativePro.Core.NarrativeLog.LogWarning("[CharacterVisual] Groom 资产路径为空，跳过。Flax 无原生毛发系统。");
                return;
            }

            // 尝试按骨骼网格加载（卡片头发）
            var skinned = Content.Load<SkinnedModel>(groomData.GroomAssetPath);
            if (skinned != null)
            {
                var mesh = GetOrCreateMeshComponent(slot);
                mesh.SkinnedModel = skinned;
                mesh.SetMasterPoseModel(GetLeaderMesh());
                ApplyMeshMaterials(mesh, groomData.GroomMaterials);
                GroomComponents.RemoveAll(e => e != null && e.Slot == slot);
                GroomComponents.Add(new GroomComponentEntry { Slot = slot, Component = mesh });
                return;
            }

            var staticModel = Content.Load<Model>(groomData.GroomAssetPath);
            if (staticModel != null)
            {
                var mesh = GetOrCreateStaticMeshComponent(slot);
                mesh.Model = staticModel;
                var leader = GetLeaderMesh();
                if (leader != null && !string.IsNullOrEmpty(groomData.GroomBindingAssetPath))
                {
                    // GroomBindingAssetPath 在 Flax 无骨骼绑定语义，仅作为附着骨骼名使用
                    RegisterBoneFollower(mesh, groomData.GroomBindingAssetPath, Transform.Identity);
                }
                ApplyMeshMaterials(mesh, groomData.GroomMaterials);
                GroomComponents.RemoveAll(e => e != null && e.Slot == slot);
                GroomComponents.Add(new GroomComponentEntry { Slot = slot, Component = mesh });
                return;
            }

            NarrativePro.Core.NarrativeLog.LogWarning(
                $"[CharacterVisual] Groom '{groomData.GroomAssetPath}' 无法作为网格解析；Flax 1.12 无 Groom 系统，已跳过。");
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

            // 1P 网格仅在第一人称可见、3P 躯干/手部在第三人称可见
            // （对应 UE5 HandlePerspectiveUpdate 中 LocalMesh->SetVisibility(bIsFirstPerson) 分支）
            foreach (var entry in MeshComponents)
            {
                if (entry?.Component == null) continue;
                if (Is1PMeshTag(entry.Slot))
                {
                    entry.Component.IsActive = bIsFirstPerson;
                }
            }
        }

        /// <summary>
        /// 隐藏/显示上半身。对应 UE5 HideUpperBody（cpp:404-418，HideBoneByName）。
        /// Flax 1.12 无按骨骼隐藏渲染的 API（已核对 AnimatedModel 托管 API：仅
        /// SetNodeTransformation/GetNodeTransformation/SetBlendShapeWeight/SetMasterPoseModel 可用），
        /// 采用骨骼塌缩方案：在动画求值后将目标骨骼及其子链的世界变换收缩到该骨骼位置，
        /// 达到“上半身不可见”的效果；游戏侧可重写本方法实现更精细的方案（如自定义顶点剔除）。
        /// </summary>
        public virtual void HideUpperBody(bool bWantsHide)
        {
            var mesh = GetMainMesh();
            if (mesh == null || string.IsNullOrEmpty(UpperBodyHideBone))
            {
                if (bWantsHide)
                {
                    NarrativePro.Core.NarrativeLog.LogWarning("[CharacterVisual] HideUpperBody：主网格或目标骨骼未就绪。");
                }
                return;
            }

            if (bWantsHide && !_bUpperBodyHidden)
            {
                _bUpperBodyHidden = true;
                mesh.AnimationUpdated += ApplyUpperBodyCollapse;
                ApplyUpperBodyCollapse();
                NarrativePro.Core.NarrativeLog.Log($"[CharacterVisual] 上半身已隐藏（骨骼塌缩 @{UpperBodyHideBone}）。");
            }
            else if (!bWantsHide && _bUpperBodyHidden)
            {
                _bUpperBodyHidden = false;
                mesh.AnimationUpdated -= ApplyUpperBodyCollapse;
                NarrativePro.Core.NarrativeLog.Log("[CharacterVisual] 上半身已恢复显示。");
            }
        }

        /// <summary>动画求值后把上半身目标骨骼缩放为 0，使其子链（头/臂）塌缩至该骨骼位置。</summary>
        protected virtual void ApplyUpperBodyCollapse()
        {
            var mesh = GetMainMesh();
            if (mesh == null || !_bUpperBodyHidden) return;

            mesh.GetNodeTransformation(UpperBodyHideBone, out Matrix boneLocal, false);
            // 保留平移分量，清零旋转/缩放行：子骨骼世界位置全部收敛到该骨骼点
            var collapsed = new Matrix(
                0f, 0f, 0f, 0f,
                0f, 0f, 0f, 0f,
                0f, 0f, 0f, 0f,
                boneLocal.M41, boneLocal.M42, boneLocal.M43, 1f);
            mesh.SetNodeTransformation(UpperBodyHideBone, collapsed, false);
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

        /// <summary>
        /// 是否本地控制。Flax 无 UE5 的 LocalRole/NetRole 概念，改用本地玩家交互组件的宿主 Actor 比对：
        /// 取 <c>PlayerInteractionComponent.LocalPlayerInteraction</c>（已核对：Interaction/PlayerInteractionComponent.cs:65），
        /// 未接入本地交互组件时（纯单机/未初始化）保持本地可控，与原行为一致。
        /// </summary>
        public bool IsLocallyControlled()
        {
            var owner = Actor;
            if (owner == null) return false;

            var local = NarrativePro.Interaction.PlayerInteractionComponent.LocalPlayerInteraction;
            if (local == null) return true;
            return local.Actor == owner;
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

    /// <summary>
    /// 骨骼跟随绑定（Flax 无 Socket 的等价实现）：子 Actor 每帧按主网格指定骨骼的世界变换定位。
    /// 对应 UE5 AttachToComponent(Socket) + SetRelativeTransform(Offset)。
    /// </summary>
    [Serializable]
    public class BoneFollowerBinding
    {
        /// <summary>被定位的子 Actor。</summary>
        public Actor Child;

        /// <summary>跟随的骨骼名（对应 UE5 Socket 名）。</summary>
        public string BoneName = "";

        /// <summary>相对骨骼的偏移变换（对应 UE5 MeshAttachOffset）。</summary>
        public Transform Offset = Transform.Identity;
    }

    /// <summary>武器视觉条目。</summary>
    [Serializable]
    public class WeaponVisualEntry
    {
        public GameplayTag Slot;
        public Actor WeaponVisualActor;
    }
}
