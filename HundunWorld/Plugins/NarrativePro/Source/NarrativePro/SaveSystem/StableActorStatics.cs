// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系详见 NarrativeStableActor.cs 顶部说明。

using System;
using FlaxEngine;
using NarrativePro.SaveSystem.Subsystems;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 稳定 Actor 工具函数库。对应 UE5 UStableActorStatics（继承 UBlueprintFunctionLibrary）。
    /// Flax 中改为静态类。
    /// </summary>
    public static class StableActorStatics
    {
        /// <summary>
        /// 从 Actor 引用构造稳定 Actor 引用。对应 UE5 MakeStableActor。
        /// 仅当 Actor 实现了 INarrativeStableActor 且返回有效 GUID 时才构造成功。
        /// </summary>
        public static StableActor MakeStableActor(Actor actor)
        {
            if (actor is INarrativeStableActor stable)
            {
                Guid actorGuid = stable.GetActorGUID();
                if (actorGuid != Guid.Empty)
                {
                    return new StableActor(actorGuid, actor);
                }
            }
            return new StableActor();
        }

        /// <summary>
        /// 从 GUID 构造稳定 Actor 引用。对应 UE5 MakeStableActorFromGUID。
        /// 会通过存档子系统按 GUID 查找当前场景中的 Actor 并缓存引用。
        /// </summary>
        public static StableActor MakeStableActorFromGUID(Guid stableActorGuid)
        {
            if (stableActorGuid != Guid.Empty)
            {
                var saveSub = NarrativeSaveSubsystem.Instance;
                if (saveSub != null)
                {
                    Actor actor = saveSub.LookupActorByGUID(stableActorGuid);
                    if (stableActorGuid != Guid.Empty)
                    {
                        return new StableActor(stableActorGuid, actor);
                    }
                }
            }
            return new StableActor();
        }

        /// <summary>
        /// 获取稳定 Actor 引用对应的 Actor。对应 UE5 GetStableActor。
        /// 优先返回缓存引用，否则通过存档子系统按 GUID 查询。
        /// </summary>
        /// <param name="stableActor">稳定 Actor 引用。</param>
        /// <param name="outSucceeded">输出是否成功找到 Actor。</param>
        public static Actor GetStableActor(StableActor stableActor, out bool outSucceeded)
        {
            outSucceeded = false;
            if (stableActor == null) return null;

            Actor actor = stableActor.GetActor();
            if (actor != null)
            {
                outSucceeded = true;
                return actor;
            }

            Guid stableGuid = stableActor.GetStableGUID();
            if (stableGuid != Guid.Empty)
            {
                var saveSub = NarrativeSaveSubsystem.Instance;
                if (saveSub != null)
                {
                    Actor found = saveSub.LookupActorByGUID(stableGuid);
                    if (found != null)
                    {
                        outSucceeded = true;
                        return found;
                    }
                }
            }

            return null;
        }
    }
}
