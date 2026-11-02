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

        /// <summary>
        /// 初始化存档系统（对应 UE5 ANarrativeGameMode::InitGame → InitializeSaveSystem）。
        /// 插件已在 Initialize 中自动接线，游戏侧需要显式指定存档槽时调用此入口。
        /// </summary>
        /// <param name="optionString">形如 "LevelName?SaveGameName=Slot&LevelTransition=1"。</param>
        public static bool InitializeSaveSystem(string optionString = null)
        {
            if (!string.IsNullOrEmpty(optionString))
            {
                NarrativeSaveBootstrap.SetOptionString(optionString);
            }

            NarrativeSaveBootstrap.Install();

            var subsystem = NarrativeSaveSubsystem.Instance;
            if (subsystem == null) return false;

            if (!string.IsNullOrEmpty(optionString))
            {
                subsystem.SetOptionString(optionString);
            }
            subsystem.InitializeSaveSystem();
            return true;
        }

        /// <summary>保存当前世界。对应 UE5 Save。存档名/槽为空时取开发者设置默认值。</summary>
        public static bool Save(string saveName = null, int slot = 0)
        {
            return NarrativeSaveBootstrap.SaveCurrentGame(saveName, slot);
        }

        /// <summary>读取存档。对应 UE5 Load。</summary>
        public static bool Load(string saveName = null, int slot = 0)
        {
            return NarrativeSaveBootstrap.LoadCurrentGame(saveName, slot);
        }

        /// <summary>删除磁盘存档。对应 UE5 DeleteSave。</summary>
        public static bool DeleteSave(string saveName = null, int slot = 0)
        {
            var subsystem = NarrativeSaveSubsystem.Instance;
            if (subsystem == null) return false;

            var settings = SaveSystemDeveloperSettings.Instance;
            string name = string.IsNullOrEmpty(saveName) ? (settings?.DefaultSaveName ?? "NarrativeSave") : saveName;
            return subsystem.DeleteSave(name, slot);
        }

        /// <summary>
        /// 登记本地玩家（Pawn / Controller / PlayerState）。
        /// 对应 UE5 UpdateSaveObject 中遍历 PlayerController 的部分；游戏侧在生成玩家后调用一行即可让玩家数据入档。
        /// </summary>
        public static void RegisterLocalPlayer(Actor pawn, System.Object controller = null, System.Object playerState = null)
        {
            NarrativePlayerDataRegistry.RegisterLocalPlayer(pawn, controller, playerState);
        }

        /// <summary>请求延迟还原玩家数据。对应 UE5 角色就绪后调用的 DeferredLoadPlayerData。</summary>
        public static void RequestDeferredPlayerDataLoad()
        {
            NarrativeSaveSubsystem.Instance?.DeferredLoadPlayerData();
        }

        /// <summary>是否为新游戏（尚无玩家数据）。对应 UE5 IsNewGame。</summary>
        public static bool IsNewGame()
        {
            var subsystem = NarrativeSaveSubsystem.Instance;
            return subsystem == null || subsystem.IsNewGame();
        }

        /// <summary>
        /// 通知存档系统某个 Actor 为运行期动态生成（非关卡内置）。
        /// 生成器/NPC 子系统在 Spawn 后调用一行，即可保证它走动态重生分支并记住 Prefab 路径。
        /// </summary>
        public static void NotifyActorDynamicallySpawned(Actor actor, string prefabPath = null)
        {
            NarrativeSaveSubsystem.Instance?.NotifyActorDynamicallySpawned(actor, prefabPath);
        }

        /// <summary>按 GUID 查找场景中的 Actor。对应 UE5 LookupActorByGUID。</summary>
        public static Actor LookupActorByGUID(Guid actorGUID)
        {
            var subsystem = NarrativeSaveSubsystem.Instance;
            return subsystem != null ? subsystem.LookupActorByGUID(actorGUID) : null;
        }

        /// <summary>当前世界的存档对象（可能为空）。</summary>
        public static NarrativeSave GetSaveObject()
        {
            var subsystem = NarrativeSaveSubsystem.Instance;
            return subsystem != null ? subsystem.GetSaveObject() : null;
        }
    }
}
