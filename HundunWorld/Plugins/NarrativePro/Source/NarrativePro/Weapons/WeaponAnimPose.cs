using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Weapons
{
    /// <summary>
    /// 武器动画姿态快照。移植自 UE5 NarrativeArsenal: Weapons/WeaponAnimPose.h（FWeaponAnimPose）。
    /// UE5 用 FBoneContainer/CompactPose 承载骨骼姿态；Flax 无对应容器，
    /// 这里以 SkinnedModel.Nodes（骨骼名 + 父索引 + 参考变换）为骨容器，
    /// 以 AnimatedModel.GetNodeTransformation 为逐帧姿态来源，实现同名能力：
    /// Init/GetPose/SetPose/SetToRefPose/GenerateWorldSpaceTransforms/Reset。
    /// 简化点：FName → string；FTransform → FlaxEngine.Transform；FBoneContainer → SkinnedModel 骨骼表。
    /// </summary>
    [Serializable]
    public class WeaponAnimPose
    {
        /// <summary>骨骼名称列表（对应 UE5 BoneNames，来自 SkinnedModel.Nodes）</summary>
        public List<string> BoneNames { get; set; } = new List<string>();

        /// <summary>骨骼索引列表（自身在容器中的索引）</summary>
        public List<int> BoneIndices { get; set; } = new List<int>();

        /// <summary>父骨骼索引列表（-1 表示根）</summary>
        public List<int> ParentBoneIndices { get; set; } = new List<int>();

        /// <summary>本地空间姿态列表</summary>
        public List<Transform> LocalSpacePoses { get; set; } = new List<Transform>();

        /// <summary>世界空间姿态列表</summary>
        public List<Transform> WorldSpacePoses { get; set; } = new List<Transform>();

        /// <summary>参考（Rest）本地空间姿态列表</summary>
        public List<Transform> RefLocalSpacePoses { get; set; } = new List<Transform>();

        /// <summary>参考（Rest）世界空间姿态列表</summary>
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

        /// <summary>是否已初始化（等价 UE5 IsInitialized，即 BoneNames 非空）。</summary>
        public bool IsInitialized() => BoneNames != null && BoneNames.Count != 0;

        /// <summary>本地空间姿态是否已填充（等价 UE5 IsPopulated）。</summary>
        public bool IsPopulated() => LocalSpacePoses != null && LocalSpacePoses.Count != 0;

        /// <summary>姿态数据是否有效（已初始化且已填充）。</summary>
        public bool IsValid() => IsInitialized() && IsPopulated();

        /// <summary>
        /// 用骨骼容器（Flax SkinnedModel）初始化各数组。对应 UE5 Init(const FBoneContainer&amp;)。
        /// </summary>
        public void Init(SkinnedModel model)
        {
            Reset();
            if (model == null) return;

            SkeletonNode[] nodes = model.Nodes;
            if (nodes == null) return;

            for (int i = 0; i < nodes.Length; i++)
            {
                BoneNames.Add(nodes[i].Name ?? string.Empty);
                BoneIndices.Add(i);
                ParentBoneIndices.Add(nodes[i].ParentIndex);

                Transform refLocal = nodes[i].LocalTransform;
                RefLocalSpacePoses.Add(refLocal);
                LocalSpacePoses.Add(refLocal);
            }

            GenerateWorldSpaceTransforms(RefWorldSpacePoses, RefLocalSpacePoses);
            // 世界空间默认以参考姿态起算，SetPose 后会重算
            for (int i = 0; i < RefWorldSpacePoses.Count; i++)
            {
                WorldSpacePoses.Add(RefWorldSpacePoses[i]);
            }
        }

        /// <summary>用组件当前激活的骨骼网格初始化容器。便捷重载。</summary>
        public void Init(AnimatedModel component)
        {
            Init(component != null ? component.SkinnedModel : null);
        }

        /// <summary>把参考姿态拷贝到动画姿态。对应 UE5 SetToRefPose。</summary>
        public void SetToRefPose()
        {
            LocalSpacePoses.Clear();
            for (int i = 0; i < RefLocalSpacePoses.Count; i++)
            {
                LocalSpacePoses.Add(RefLocalSpacePoses[i]);
            }
            GenerateWorldSpaceTransforms();
        }

        /// <summary>
        /// 由骨骼组件的实时节点变换填充本地空间姿态。对应 UE5 SetPose(USkeletalMeshComponent*)。
        /// Flax 的 GetNodeTransformation(localSpace=false) 直接给世界空间；这里取世界空间后
        /// 再相对组件世界变换还原为本地空间，语义与 UE5 一致。
        /// </summary>
        public void SetPose(AnimatedModel component)
        {
            if (component == null || !IsInitialized()) return;

            // 组件世界变换：用于把节点世界变换还原为组件本地空间
            Transform componentWorld = component.Transform;

            LocalSpacePoses.Clear();
            for (int i = 0; i < BoneNames.Count; i++)
            {
                Matrix world;
                component.GetNodeTransformation(i, out world, true);
                Transform worldPose = new Transform(world);
                LocalSpacePoses.Add(componentWorld.WorldToLocal(worldPose));
            }

            GenerateWorldSpaceTransforms();
        }

        /// <summary>由外部提供的本地姿态列表填充（对应 UE5 SetPose(const FAnimationPoseData&amp;)）。</summary>
        public void SetPose(List<Transform> localSpacePoses)
        {
            if (localSpacePoses == null || !IsInitialized()) return;
            LocalSpacePoses.Clear();
            for (int i = 0; i < BoneNames.Count; i++)
            {
                LocalSpacePoses.Add(i < localSpacePoses.Count ? localSpacePoses[i] : Transform.Identity);
            }
            GenerateWorldSpaceTransforms();
        }

        /// <summary>导出本地空间姿态。对应 UE5 GetPose(FCompactPose&amp;)。</summary>
        public void GetPose(List<Transform> outLocalPoses)
        {
            if (outLocalPoses == null) return;
            outLocalPoses.Clear();
            outLocalPoses.AddRange(LocalSpacePoses);
        }

        /// <summary>按父子层级重算世界（根相对）空间变换。对应 UE5 GenerateWorldSpaceTransforms。</summary>
        public void GenerateWorldSpaceTransforms()
        {
            GenerateWorldSpaceTransforms(WorldSpacePoses, LocalSpacePoses);
        }

        private void GenerateWorldSpaceTransforms(List<Transform> outWorld, List<Transform> inLocal)
        {
            outWorld.Clear();
            if (inLocal == null || inLocal.Count == 0) return;

            for (int i = 0; i < inLocal.Count; i++)
            {
                Transform local = inLocal[i];
                int parent = (i < ParentBoneIndices.Count) ? ParentBoneIndices[i] : -1;
                if (parent >= 0 && parent < outWorld.Count)
                {
                    outWorld.Add(Compose(outWorld[parent], local));
                }
                else
                {
                    outWorld.Add(local);
                }
            }
        }

        /// <summary>返回指定骨骼名的索引，未找到返回 -1。对应 UE5 GetBoneIndex(FName)。</summary>
        public int GetBoneIndex(string boneName)
        {
            if (string.IsNullOrEmpty(boneName) || BoneNames == null) return -1;
            return BoneNames.IndexOf(boneName);
        }

        /// <summary>返回指定骨骼的世界空间姿态，越界返回 Identity。</summary>
        public Transform GetWorldSpacePose(int boneIndex)
        {
            if (WorldSpacePoses != null && boneIndex >= 0 && boneIndex < WorldSpacePoses.Count)
            {
                return WorldSpacePoses[boneIndex];
            }
            return Transform.Identity;
        }

        /// <summary>注册一个 Socket（父骨骼 + 相对变换），供 GetSocketTransform 使用。</summary>
        public void AddSocket(string socketName, string parentBoneName, Transform localOffset)
        {
            if (string.IsNullOrEmpty(socketName)) return;
            SocketNames.Add(socketName);
            SocketParentBoneNames.Add(parentBoneName ?? string.Empty);
            SocketTransforms.Add(localOffset);
        }

        /// <summary>取 Socket 的世界（根相对）变换。对应 UE5 GetSocketTransform(SocketName, RTS_World)。未找到返回 Identity。</summary>
        public Transform GetSocketTransform(string socketName)
        {
            int idx = SocketNames != null ? SocketNames.IndexOf(socketName) : -1;
            if (idx < 0 || idx >= SocketTransforms.Count) return Transform.Identity;

            int boneIndex = GetBoneIndex(SocketParentBoneNames[idx]);
            if (boneIndex < 0) return SocketTransforms[idx];

            return Compose(GetWorldSpacePose(boneIndex), SocketTransforms[idx]);
        }

        /// <summary>清空全部数据，使实例失效。对应 UE5 Reset。</summary>
        public void Reset()
        {
            BoneNames.Clear();
            BoneIndices.Clear();
            ParentBoneIndices.Clear();
            LocalSpacePoses.Clear();
            WorldSpacePoses.Clear();
            RefLocalSpacePoses.Clear();
            RefWorldSpacePoses.Clear();
            CurveNames.Clear();
            CurveValues.Clear();
            SocketNames.Clear();
            SocketParentBoneNames.Clear();
            SocketTransforms.Clear();
        }

        /// <summary>TRS 复合：把 child 变换挂到 parent 之下（等价 parent * child）。对应 UE5 FTransform 相乘。</summary>
        public static Transform Compose(Transform parent, Transform child)
        {
            return parent.LocalToWorld(child);
        }
    }

    /// <summary>
    /// 动画通知事件。对应 UE5 FAnimNotifyEvent（NotifyState/TriggerTime/Duration/SourceAnimation）。
    /// Flax 无 UE5 的 AnimNotify 队列，攻击动画通过 AnimNotifyState_AttackAnim 等价的
    /// 分支事件产生本结构，交由 WeaponVisual 做命中缓存。
    /// </summary>
    [Serializable]
    public class AnimNotifyEvent
    {
        /// <summary>通知名（用于调试与匹配）。</summary>
        public string NotifyName;

        /// <summary>触发时间（动画起始时间，秒）。对应 UE5 GetTriggerTime()。</summary>
        public float TriggerTime;

        /// <summary>通知持续时长（秒）。对应 UE5 Duration。</summary>
        public float Duration;

        /// <summary>播放速率（蒙太奇播放倍速，用于把动画时间映射到游戏时间）。</summary>
        public float PlayRate = 1f;

        /// <summary>宿主骨骼网格组件（UE5 中由 NotifyBegin 的 SkelMeshComponent 提供）。</summary>
        [NonSerialized]
        public AnimatedModel MeshComponent;

        public AnimNotifyEvent() { }

        public AnimNotifyEvent(string notifyName, float triggerTime, float duration, AnimatedModel mesh = null)
        {
            NotifyName = notifyName;
            TriggerTime = triggerTime;
            Duration = duration;
            MeshComponent = mesh;
        }
    }

    /// <summary>
    /// 动画通知事件引用。移植自 UE5 FAnimNotifyEventReference。
    /// 持有通知事件本体与其来源对象（攻击蒙太奇/动画资产），作为 WeaponVisual 命中缓存的键。
    /// </summary>
    [Serializable]
    public struct AnimNotifyEventReference
    {
        /// <summary>占位/兼容：通知名称。</summary>
        public string NotifyName;

        /// <summary>被引用的通知事件（UE5 GetNotify() 的等价物）。</summary>
        [NonSerialized]
        public AnimNotifyEvent Notify;

        /// <summary>通知来源对象（UE5 GetSourceObject()，通常是蒙太奇/动画资产）。</summary>
        [NonSerialized]
        public object SourceObject;

        public AnimNotifyEvent GetNotify() => Notify;
        public object GetSourceObject() => SourceObject;

        /// <summary>是否为空引用（UE5 FAnimNotifyEventReference 默认构造等价）。</summary>
        public bool IsValid => Notify != null;

        /// <summary>创建一个引用。</summary>
        public static AnimNotifyEventReference Create(AnimNotifyEvent notify, object sourceObject = null)
        {
            return new AnimNotifyEventReference
            {
                NotifyName = notify != null ? notify.NotifyName : null,
                Notify = notify,
                SourceObject = sourceObject
            };
        }
    }

    /// <summary>
    /// 动画通知引用对象。移植自 UE5 UAnimNotifyRefObject（UObject）。
    /// Flax 无 UObject，按 [Serializable] 类实现，用于把 AnimNotifyEventReference 作为 gameplay event 载荷传递。
    /// </summary>
    [Serializable]
    public class AnimNotifyRefObject
    {
        /// <summary>所持有的动画通知事件引用</summary>
        public AnimNotifyEventReference NotifyEventReference;

        /// <summary>便捷工厂：把通知事件包装成可传递的载荷对象。</summary>
        public static AnimNotifyRefObject From(AnimNotifyEvent notify, object sourceObject = null)
        {
            return new AnimNotifyRefObject { NotifyEventReference = AnimNotifyEventReference.Create(notify, sourceObject) };
        }
    }

    /// <summary>
    /// 伤害状态数据。移植自 UE5 FDamageStateData：记录攻击动画某一帧时骨骼的相对变换。
    /// </summary>
    [Serializable]
    public class DamageStateData
    {
        /// <summary>该帧在动画中的时间（秒）。对应 UE5 FrameTime。</summary>
        public float FrameTime;

        /// <summary>该帧是否已经做过碰撞扫描。对应 UE5 bTriggered。</summary>
        public bool bTriggered;

        /// <summary>该帧的相对变换是否已从骨骼姿态捕获（Flax 无法离线采样任意时刻，故增量捕获）。</summary>
        public bool bCaptured;

        /// <summary>相对（角色网格根骨骼空间）位置。对应 UE5 RelativeLocation。</summary>
        public Vector3 RelativeLocation = Vector3.Zero;

        /// <summary>相对旋转。对应 UE5 RelativeRotation。</summary>
        public Quaternion RelativeRotation = Quaternion.Identity;

        public DamageStateData() { }

        public DamageStateData(float inFrameTime)
        {
            FrameTime = inFrameTime;
        }
    }

    /// <summary>
    /// 伤害状态数据容器。移植自 UE5 FDamageStateDataContainer，可在字典中高效检索。
    /// </summary>
    [Serializable]
    public class DamageStateDataContainer
    {
        /// <summary>逐帧采样数据。对应 UE5 DamageStateDatas。</summary>
        public List<DamageStateData> DamageStateDatas = new List<DamageStateData>();

        public DamageStateDataContainer() { }

        public DamageStateDataContainer(List<DamageStateData> inDamageStateData)
        {
            if (inDamageStateData != null) DamageStateDatas.AddRange(inDamageStateData);
        }
    }

    /// <summary>
    /// 武器碰撞体形状。对应 UE5 FCollisionShape 中本插件实际使用的胶囊体分支
    /// （UE5 注释亦说明"目前只支持 capsule 形状"）。
    /// </summary>
    [Serializable]
    public struct WeaponCollisionShape
    {
        /// <summary>胶囊半径。</summary>
        public float Radius;

        /// <summary>胶囊半高（含两端半球），与 UE5 FCollisionShape::MakeCapsule(Radius, HalfHeight) 的 HalfHeight 同义。</summary>
        public float HalfHeight;

        public WeaponCollisionShape(float radius, float halfHeight)
        {
            Radius = radius;
            HalfHeight = halfHeight;
        }

        /// <summary>UE5 FCollisionShape::GetExtent() 等价：X/Y = 半径，Z = 半高。</summary>
        public Vector3 GetExtent()
        {
            float half = System.Math.Max(HalfHeight, Radius);
            return new Vector3(Radius, Radius, half);
        }

        /// <summary>
        /// Flax Physics.Capsule* 系列要求的 height 语义：两端半球圆心之间的距离（不含球帽）。
        /// 见 Flax Engine/Physics/Physics.h 的 CapsuleCast 注释。
        /// </summary>
        public float GetFlaxCylinderHeight()
        {
            float h = (HalfHeight - Radius) * 2f;
            return h > 0f ? h : 0f;
        }

        public bool IsCapsule() => Radius > 0f && HalfHeight >= Radius;
    }

    /// <summary>
    /// 武器碰撞数据。移植自 UE5 FWeaponCollisionData：
    /// 保存某个武器碰撞胶囊相对武器根骨骼的位置/旋转，以及上一帧的世界位置（用于连续扫描）。
    /// </summary>
    [Serializable]
    public class WeaponCollisionData
    {
        /// <summary>相对位置（相对武器挂载骨骼）。</summary>
        public Vector3 RelativeLocation = Vector3.Zero;

        /// <summary>相对旋转。</summary>
        public Quaternion RelativeRotation = Quaternion.Identity;

        /// <summary>上一帧世界位置，未初始化时为 (0,0,0)。对应 UE5 LastLocation。</summary>
        public Vector3 LastLocation = Vector3.Zero;

        /// <summary>碰撞形状。对应 UE5 Shape。</summary>
        public WeaponCollisionShape Shape;

        public WeaponCollisionData() { }

        public WeaponCollisionData(Vector3 inRelativeLocation, Quaternion inRelativeRotation, Vector3 inLastLocation, WeaponCollisionShape inShape)
        {
            RelativeLocation = inRelativeLocation;
            RelativeRotation = inRelativeRotation;
            LastLocation = inLastLocation;
            Shape = inShape;
        }

        /// <summary>UE5 FVector::IsNearlyZero 等价。</summary>
        public bool LastLocationNearlyZero() => LastLocation.LengthSquared < 1e-6f;
    }
}
