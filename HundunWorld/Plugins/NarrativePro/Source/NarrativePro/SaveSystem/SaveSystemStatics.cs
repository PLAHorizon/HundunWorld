// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系详见 NarrativeStableActor.cs 顶部说明。

using System;
using FlaxEngine;
using NarrativePro.SaveSystem.Subsystems;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 存档系统静态工具库。对应 UE5 USaveSystemStatics（继承 UBlueprintFunctionLibrary）。
    /// Flax 中改为静态类。
    /// </summary>
    public static class SaveSystemStatics
    {
        /// <summary>
        /// 若 OutGuid 尚未生成有效 GUID，则为其赋一个新 GUID。
        /// 适用于构造函数等可能被多次调用的场景。
        /// 对应 UE5 CreateSaveGuid。
        /// </summary>
        public static void CreateSaveGuid(ref Guid outGuid)
        {
            if (outGuid == Guid.Empty)
            {
                outGuid = Guid.NewGuid();
            }
        }

        /// <summary>
        /// 加载单个 Actor，使其状态匹配其存档记录。对应 UE5 LoadSingleActor。
        /// </summary>
        public static bool LoadSingleActor(Actor actor)
        {
            if (actor != null)
            {
                var subsystem = NarrativeSaveSubsystem.Instance;
                if (subsystem != null)
                {
                    return subsystem.LoadSingleActor(actor);
                }
            }
            return false;
        }

        /// <summary>
        /// 保存单个 Actor，更新其存档记录。对应 UE5 SaveSingleActor。
        /// </summary>
        public static bool SaveSingleActor(Actor actor)
        {
            if (actor != null)
            {
                var subsystem = NarrativeSaveSubsystem.Instance;
                if (subsystem != null)
                {
                    return subsystem.SaveSingleActor(actor);
                }
            }
            return false;
        }

        /// <summary>
        /// 从存档文件中移除单个 Actor 的记录。对应 UE5 RemoveSingleActor。
        /// </summary>
        public static bool RemoveSingleActor(Actor actor)
        {
            if (actor != null)
            {
                var subsystem = NarrativeSaveSubsystem.Instance;
                if (subsystem != null)
                {
                    return subsystem.RemoveSingleActor(actor);
                }
            }
            return false;
        }
    }
}
