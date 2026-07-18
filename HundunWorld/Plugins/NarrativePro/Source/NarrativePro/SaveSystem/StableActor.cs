// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系详见 NarrativeStableActor.cs 顶部说明。

using System;
using FlaxEngine;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 稳定 Actor 引用。对应 UE5 FStableActor（USTRUCT）。
    /// 可像普通 Actor 引用一样使用，但通过稳定 GUID 跨会话保持引用。
    /// 仅适用于实现了 INarrativeStableActor 并通过 GetActorGUID() 返回 GUID 的 Actor。
    /// </summary>
    [Serializable]
    public class StableActor
    {
        /// <summary>稳定 GUID。对应 UE5 StableActorGUID。</summary>
        public Guid StableActorGUID = Guid.Empty;

        /// <summary>
        /// 运行时缓存的 Actor 引用。对应 UE5 TWeakObjectPtr&lt;AActor&gt; StableActorRef。
        /// Flax 中使用 [NonSerialized] 避免序列化；运行时通过 GUID 查询恢复。
        /// </summary>
        [NonSerialized]
        public Actor StableActorRef;

        /// <summary>默认构造。</summary>
        public StableActor() { }

        /// <summary>通过 GUID 构造。</summary>
        public StableActor(Guid inStableActorGUID)
        {
            StableActorGUID = inStableActorGUID;
        }

        /// <summary>通过 GUID 与运行时引用构造。</summary>
        public StableActor(Guid inStableActorGUID, Actor inActor)
        {
            StableActorGUID = inStableActorGUID;
            StableActorRef = inActor;
        }

        /// <summary>获取缓存的 Actor 引用（可能为空）。</summary>
        public Actor GetActor()
        {
            return StableActorRef;
        }

        /// <summary>获取稳定 GUID。</summary>
        public Guid GetStableGUID()
        {
            return StableActorGUID;
        }
    }
}
