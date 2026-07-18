using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// 姿势快照。对应 UE5 FPoseSnapshot。
    /// 用于 Sequencer 混出、Ragdoll 起身等需要预存骨骼姿势的场景。
    /// Flax 无直接等价类型，这里作为 [Serializable] 占位类，待后续接入 Flax 动画系统。
    /// </summary>
    [Serializable]
    public class FPoseSnapshot
    {
        /// <summary>骨骼位置数据（占位）。</summary>
        public List<Vector3> BonePositions = new List<Vector3>();

        /// <summary>骨骼旋转数据（占位）。</summary>
        public List<Quaternion> BoneRotations = new List<Quaternion>();

        /// <summary>骨骼缩放数据（占位）。</summary>
        public List<Vector3> BoneScales = new List<Vector3>();

        /// <summary>骨骼名称列表（占位）。</summary>
        public List<string> BoneNames = new List<string>();

        /// <summary>是否已捕获有效快照。</summary>
        public bool bIsValid = false;
    }

    /// <summary>
    /// GameplayTag 到蓝图属性的映射。对应 UE5 FGameplayTagBlueprintPropertyMap。
    /// GAS 提供的容器类型，允许将变量直接绑定到 GameplayTag。
    /// Flax 中作为占位类（Flax-不兼容: UE5 FGameplayTagBlueprintPropertyMap 在 Flax 无对应物，保留占位）。
    /// </summary>
    [Serializable]
    public class FGameplayTagBlueprintPropertyMap
    {
        /// <summary>映射条目（占位）。</summary>
        public Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// 活跃 GameplayEffect 句柄。对应 UE5 FActiveGameplayEffectHandle。
    /// 用于引用正在应用中的 GameplayEffect。
    /// </summary>
    [Serializable]
    public struct FActiveGameplayEffectHandle
    {
        /// <summary>句柄值（占位）。</summary>
        public int Handle;

        public FActiveGameplayEffectHandle(int handle) { Handle = handle; }

        public bool IsValid() => Handle != 0;
        public static readonly FActiveGameplayEffectHandle Invalid = new FActiveGameplayEffectHandle(0);
    }

    /// <summary>
    /// Narrative 动画实例。对应 UE5 UNarrativeAnimInstance。
    /// UE5 中继承 UAnimInstance；Flax 无 AnimInstance 基类，改为普通 [Serializable] class 占位 + TODO。
    /// 简化点：
    /// - 移除 UE5 复制/RPC，改为本地逻辑 + 事件回调
    /// - FVector → Vector3，FQuat → Quaternion，FTransform → Transform
    /// - FGameplayTag → GameplayTag，FGameplayTagContainer → GameplayTagContainer
    /// - TMap → Dictionary，TObjectPtr → 直接引用，TSubclassOf → string 路径
    /// - USkeletalMeshComponent* 用 AnimatedModel 等价（Flax-已实现: 接入 Flax AnimatedModel）
    /// - NativeUpdateAnimation/NativeInitializeAnimation 用 virtual 方法模拟
    /// </summary>
    [Serializable]
    public class NarrativeAnimInstance
    {
        /// <summary>构造默认实例。</summary>
        public NarrativeAnimInstance()
        {
            TraversalLedgeLocation = Vector3.Zero;
            LocalLedgeLocation = Vector3.Zero;
            TraversalLedgeRotation = Quaternion.Identity;
            TraversalLedgeTransform = Transform.Identity;
            DirectionToLedge = Quaternion.Identity;
            OverrideLayerBlendInTime = 0.2f;
            OverrideLayerBlendOutTime = 0.2f;
            bIsThirdPersonABP = true;
            bHasOverrideLayer = false;
            bWantsBlendOutOfSequencer = false;
            ApplyTags = new GameplayTagContainer();
            GameplayTagPropertyMap = new FGameplayTagBlueprintPropertyMap();
            TaggedAnimSets = new Dictionary<GameplayTag, NarrativeAnimSet>();
            TaggedOverrideLayers = new Dictionary<GameplayTag, string>();
        }

        // ===== 遍历（Traversal）相关 =====

        /// <summary>遍历 ledge 的位置。对应 UE5 TraversalLedgeLocation。</summary>
        public Vector3 TraversalLedgeLocation;

        /// <summary>本地 ledge 位置。对应 UE5 LocalLedgeLocation。</summary>
        public Vector3 LocalLedgeLocation;

        /// <summary>遍历 ledge 的旋转。对应 UE5 TraversalLedgeRotation。</summary>
        public Quaternion TraversalLedgeRotation;

        /// <summary>遍历 ledge 的变换。对应 UE5 TraversalLedgeTransform。</summary>
        public Transform TraversalLedgeTransform;

        /// <summary>朝向 ledge 的方向。对应 UE5 DirectionToLedge。</summary>
        public Quaternion DirectionToLedge;

        // ===== 覆盖层（Override Layer）相关 =====

        /// <summary>覆盖层淡入时间。对应 UE5 OverrideLayerBlendInTime。</summary>
        public float OverrideLayerBlendInTime;

        /// <summary>覆盖层淡出时间。对应 UE5 OverrideLayerBlendOutTime。</summary>
        public float OverrideLayerBlendOutTime;

        /// <summary>覆盖层淡出计时器句柄（替代 UE5 FTimerHandle）。</summary>
        [NonSerialized]
        public float TimerHandle_OverrideLayerBlendedOut;

        /// <summary>是否已应用覆盖层。对应 UE5 bHasOverrideLayer。</summary>
        public bool bHasOverrideLayer;

        /// <summary>上一次的覆盖层标签。对应 UE5 LastOverrideLayer。</summary>
        public GameplayTag LastOverrideLayer;

        /// <summary>当前覆盖层标签。对应 UE5 CurrentOverrideLayer。</summary>
        public GameplayTag CurrentOverrideLayer;

        // ===== 标签 =====

        /// <summary>
        /// 这些标签将在动画蓝图运行期间应用到角色上。
        /// 对应 UE5 UPROPERTY(EditDefaultsOnly) FGameplayTagContainer ApplyTags。
        /// </summary>
        public GameplayTagContainer ApplyTags;

        /// <summary>应用标签的 GameplayEffect 句柄。对应 UE5 ApplyTagsHandle。</summary>
        [NonSerialized]
        public FActiveGameplayEffectHandle ApplyTagsHandle;

        // ===== 引用 =====

        /// <summary>所属 Narrative 角色（直接引用，替代 UE5 TObjectPtr&lt;ANarrativeCharacter&gt;）。</summary>
        [NonSerialized]
        public NarrativeCharacter NarrativeCharacterRef;

        /// <summary>当前覆盖层动画实例（直接引用，替代 UE5 TObjectPtr&lt;UNarrativeAnimInstance&gt;）。</summary>
        [NonSerialized]
        public NarrativeAnimInstance OverrideLayerAnimInstance;

        /// <summary>此 AnimInstance 是否应用于 3P（第三人称）或 1P（第一人称）网格设置——而非是否当前为第一人称。对应 UE5 bIsThirdPersonABP。</summary>
        public bool bIsThirdPersonABP;

        // ===== 标签化数据 =====

        /// <summary>
        /// GAS 中极佳的容器类型，允许将变量直接绑定到 GameplayTag。
        /// 对应 UE5 UPROPERTY(EditDefaultsOnly) FGameplayTagBlueprintPropertyMap。
        /// </summary>
        public FGameplayTagBlueprintPropertyMap GameplayTagPropertyMap;

        /// <summary>
        /// 标签化的动画集——以通用、可扩展、蓝图友好的方式将标签映射到 Combo Set。
        /// 对应 UE5 TMap&lt;FGameplayTag, TObjectPtr&lt;UNarrativeAnimSet&gt;&gt; TaggedAnimSets。
        /// </summary>
        public Dictionary<GameplayTag, NarrativeAnimSet> TaggedAnimSets;

        /// <summary>
        /// 标签化的覆盖层——以通用、可扩展、蓝图友好的方式将标签映射到覆盖层。
        /// 对应 UE5 TMap&lt;FGameplayTag, TSubclassOf&lt;UAnimInstance&gt;&gt; TaggedOverrideLayers。
        /// </summary>
        public Dictionary<GameplayTag, string> TaggedOverrideLayers;

        // ===== Sequencer 混出 =====

        /// <summary>为使用此 AnimInstance 的对象提供从 Sequencer 混出的简易框架。
        /// 由于 Sequencer 无法即时混出（只能使用预制关键帧），需要快照。
        /// 对应 UE5 注释说明。</summary>

        /// <summary>需要从 Sequencer 混出时设为 true。对应 UE5 bWantsBlendOutOfSequencer。</summary>
        public bool bWantsBlendOutOfSequencer;

        /// <summary>角色在 Sequencer 中的最后一帧姿势。当 bWantsBlendOutOfSequencer 为 true 时，由 ABP 从此快照混回。对应 UE5 SequencerPoseSnapshot。</summary>
        [NonSerialized]
        public FPoseSnapshot SequencerPoseSnapshot;

        // ===== 方法 =====

        /// <summary>初始化属性映射。对应 UE5 InitializePropertyMap(ASC)。</summary>
        /// <param name="asc">能力系统组件。</param>
        public virtual void InitializePropertyMap(NarrativeAbilitySystemComponent asc)
        {
            // TODO [需接入 ASC 系统]: 通过 ASC 绑定 GameplayTag 到属性
            NarrativeLog.Log("[NarrativeAnimInstance] InitializePropertyMap");
        }

        /// <summary>通过标签在动画实例上查找动画集，返回 AnimSet 与是否找到的布尔值。对应 UE5 GetAnimSet。</summary>
        /// <param name="animSetTag">动画集标签（属于 Narrative.Anim.AnimSets 类别）。</param>
        /// <param name="bOutFoundAnimSet">输出：是否找到动画集。</param>
        /// <returns>动画集实例；未找到返回 null。</returns>
        public virtual NarrativeAnimSet GetAnimSet(GameplayTag animSetTag, out bool bOutFoundAnimSet)
        {
            bOutFoundAnimSet = false;
            if (animSetTag.IsValid() && TaggedAnimSets != null)
            {
                if (TaggedAnimSets.TryGetValue(animSetTag, out var animSet))
                {
                    bOutFoundAnimSet = true;
                    return animSet;
                }
            }
            return null;
        }

        /// <summary>返回主角色网格。即使 AnimInstance 应用于角色视觉（CharacterVisual）而非角色本身也能正常工作。对应 UE5 GetCharacterMesh。</summary>
        /// <returns>骨骼网格组件（Flax-已实现: 返回 AnimatedModel 实例，可用 GetParameter(name).Value 访问动画参数）。</returns>
        public virtual object GetCharacterMesh()
        {
            // Flax-已实现: 通过 NarrativeCharacterVisual 或 NarrativeCharacterRef 获取 AnimatedModel
            var visual = GetCharacterVisualRef();
            if (visual != null && visual.Actor != null)
            {
                var am = visual.Actor.GetScript<AnimatedModel>();
                if (am != null) return am;
            }
            if (NarrativeCharacterRef != null && NarrativeCharacterRef.Actor != null)
            {
                return NarrativeCharacterRef.Actor.GetScript<AnimatedModel>();
            }
            return null;
        }

        /// <summary>返回所属角色。对应 UE5 GetCharacterRef。</summary>
        public virtual NarrativeCharacter GetCharacterRef() => NarrativeCharacterRef;

        /// <summary>返回所属角色视觉。对应 UE5 GetCharacterVisualRef。</summary>
        public virtual NarrativeCharacterVisual GetCharacterVisualRef()
        {
            return NarrativeCharacterRef?.GetCharacterVisual();
        }

        /// <summary>返回所属角色的主 ABP——挂载在角色网格上的那个。对应 UE5 GetMainABPRef。</summary>
        public virtual NarrativeAnimInstance GetMainABPRef()
        {
            // TODO [需接入 NarrativeCharacter 系统]: 通过角色获取主 ABP
            return null;
        }

        /// <summary>当前是否存在覆盖层。对应 UE5 HasOverrideLayer。</summary>
        public virtual bool HasOverrideLayer() => bHasOverrideLayer;

        /// <summary>以给定淡入时间应用覆盖层。对应 UE5 ApplyOverrideLayer。</summary>
        /// <param name="layerTag">覆盖层标签（属于 Narrative.Anim.OverrideLayer 类别）。</param>
        /// <param name="blendInTime">淡入时间。</param>
        /// <returns>是否成功应用。</returns>
        public virtual bool ApplyOverrideLayer(GameplayTag layerTag, float blendInTime)
        {
            if (!layerTag.IsValid()) return false;
            if (TaggedOverrideLayers == null) return false;
            if (!TaggedOverrideLayers.ContainsKey(layerTag)) return false;

            LastOverrideLayer = CurrentOverrideLayer;
            CurrentOverrideLayer = layerTag;
            bHasOverrideLayer = true;
            OverrideLayerBlendInTime = blendInTime;
            // TODO [需接入 AnimInstance 加载系统]: 实际应用覆盖层（加载并切换 AnimInstance）
            NarrativeLog.Log($"[NarrativeAnimInstance] ApplyOverrideLayer: {layerTag} (blend in {blendInTime}s)");
            return true;
        }

        /// <summary>在设定时间内移除当前覆盖层，混回正常 ABP 逻辑。对应 UE5 RemoveOverrideLayer。</summary>
        /// <param name="blendOutTime">淡出时间。</param>
        public virtual void RemoveOverrideLayer(float blendOutTime)
        {
            OverrideLayerBlendOutTime = blendOutTime;
            // TODO [需接入计时器系统]: 启动淡出计时器，完成后调用 OverrideLayerBlendedOut
            NarrativeLog.Log($"[NarrativeAnimInstance] RemoveOverrideLayer (blend out {blendOutTime}s)");
            OverrideLayerBlendedOut();
        }

        /// <summary>停止所有蒙太奇。对应 UE5 BPStopAllMontages。</summary>
        /// <param name="blendOutTime">淡出时间。</param>
        public virtual void BPStopAllMontages(float blendOutTime)
        {
            // Flax-不兼容: UE5 的 AnimMontage 在 Flax 无对应物，保留占位。原文 TODO: 接入 Flax 动画系统停止所有蒙太奇
            NarrativeLog.Log($"[NarrativeAnimInstance] BPStopAllMontages (blend out {blendOutTime}s)");
        }

        /// <summary>返回覆盖层的动画实例。对应 UE5 GetOverrideLayerAnimInstance。</summary>
        public virtual NarrativeAnimInstance GetOverrideLayerAnimInstance() => OverrideLayerAnimInstance;

        /// <summary>返回覆盖层的标签。对应 UE5 GetOverrideLayerTag。</summary>
        public virtual GameplayTag GetOverrideLayerTag() => CurrentOverrideLayer;

        /// <summary>覆盖层混出完成时调用。对应 UE5 OverrideLayerBlendedOut（protected virtual）。</summary>
        protected virtual void OverrideLayerBlendedOut()
        {
            bHasOverrideLayer = false;
            OverrideLayerAnimInstance = null;
        }

        /// <summary>每帧更新动画。对应 UE5 NativeUpdateAnimation(DeltaSeconds)。</summary>
        /// <param name="deltaSeconds">帧间隔（秒）。</param>
        public virtual void NativeUpdateAnimation(float deltaSeconds)
        {
            // TODO [需接入 ASC 系统]: 更新动画状态（根据角色标签/状态通过 AnimatedModel.GetParameter(name).Value 更新动画参数）
        }

        /// <summary>初始化动画。对应 UE5 NativeInitializeAnimation。</summary>
        public virtual void NativeInitializeAnimation()
        {
            // TODO [需接入 ASC 系统]: 缓存角色引用、初始化属性映射
            NarrativeLog.Log("[NarrativeAnimInstance] NativeInitializeAnimation");
        }

        /// <summary>反初始化动画。对应 UE5 NativeUninitializeAnimation。</summary>
        public virtual void NativeUninitializeAnimation()
        {
            // TODO [需接入 ASC 系统]: 清理状态
        }

        /// <summary>从 Sequencer 混出。对应 UE5 BlendOutOfSequencer。</summary>
        public virtual void BlendOutOfSequencer()
        {
            bWantsBlendOutOfSequencer = true;
            // Flax-不兼容: UE5 的 PoseSnapshot 在 Flax 无对应物，保留占位。原文 TODO: 捕获当前姿势快照
            NarrativeLog.Log("[NarrativeAnimInstance] BlendOutOfSequencer");
        }
    }

    /// <summary>
    /// ABP_Biped、ABP_Quadruped 等的基类，对应 UE5 UNarrativeCharacterAnimInstance。
    /// 角色基础动画蓝图类。
    /// </summary>
    [Serializable]
    public class NarrativeCharacterAnimInstance : NarrativeAnimInstance
    {
    }

    /// <summary>
    /// Ragdoll（布娃娃）专用覆盖层，包含姿势快照。对应 UE5 URagdollAnimInstance。
    /// </summary>
    [Serializable]
    public class RagdollAnimInstance : NarrativeAnimInstance
    {
        /// <summary>构造默认实例。</summary>
        public RagdollAnimInstance() : base() { }

        /// <summary>Ragdoll 起身姿势快照。对应 UE5 RagdollGetUpSnapshot。</summary>
        [NonSerialized]
        public FPoseSnapshot RagdollGetUpSnapshot;

        /// <summary>创建 Ragdoll 快照。对应 UE5 CreateRagdollSnapshot。</summary>
        /// <returns>姿势快照引用。</returns>
        public virtual FPoseSnapshot CreateRagdollSnapshot()
        {
            // Flax-不兼容: UE5 的 PoseSnapshot 在 Flax 无对应物，保留占位。原文 TODO: 捕获当前骨骼姿势
            RagdollGetUpSnapshot = new FPoseSnapshot();
            RagdollGetUpSnapshot.bIsValid = true;
            return RagdollGetUpSnapshot;
        }
    }
}
