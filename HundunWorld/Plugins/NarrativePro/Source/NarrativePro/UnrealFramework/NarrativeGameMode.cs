using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Character;
using NarrativePro.Core;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// Narrative 游戏模式。对应 UE5 ANarrativeGameMode。
    /// UE5 中继承 AGameModeBase；Flax 无 GameMode 基类，改为 Script。
    /// 每个场景（或全局）应挂载一个此 Script，负责玩家生成、定义分配等。
    /// 简化点：
    /// - 移除 UE5 复制/RPC
    /// - 移除 AGameModeBase 的 SpawnDefaultPawn 等流程（Flax 无 Pawn 概念，由 GameMode 直接创建角色 Actor）
    /// - TObjectPtr&lt;UPlayerDefinition&gt; 转为直接引用
    /// </summary>
    public class NarrativeGameMode : Script
    {
        // ===== 配置字段 =====

        /// <summary>玩家定义列表。对应 UE5 PlayerDefinitions。
        /// 默认情况下，Narrative 按加入顺序为每位玩家分配此列表中的定义。
        /// 如需不同行为，重写 GetPlayerDefinitionForController。
        /// </summary>
        public List<PlayerDefinition> PlayerDefinitions = new List<PlayerDefinition>();

        /// <summary>默认玩家角色 Prefab 路径（替代 UE5 DefaultPawnClass）。</summary>
        public string DefaultPlayerCharacterPrefabPath = "";

        // ===== 运行时状态 =====

        /// <summary>当前已分配的玩家索引（用于从 PlayerDefinitions 顺序取值）。</summary>
        [NonSerialized]
        protected int _nextPlayerIndex = 0;

        // ===== 生命周期 =====

        public override void OnEnable()
        {
            base.OnEnable();
            InitGame();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            // Flax-已实现：与 OnEnable 的 InitGame 对称，走游戏实例的显式关闭链路
            NarrativeGameInstance.Get()?.Shutdown();
        }

        // ===== 初始化 =====

        /// <summary>游戏初始化时调用。对应 UE5 InitGame。</summary>
        public virtual void InitGame()
        {
            NarrativeLog.Log("[NarrativeGameMode] InitGame");
            // Flax-已实现：本地等价中 GameMode 是场景入口，由它驱动游戏实例生命周期
            //（UE5 中对应 World 就绪后 GameInstance 的 Init/Start 链路）。
            // 存档数据恢复由 NarrativeSaveSubsystem（SaveSystem）在其自有入口驱动，此处不重复。
            var gameInstance = NarrativeGameInstance.Get();
            gameInstance.Init();
            gameInstance.OnStart();
        }

        // ===== 玩家生成 =====

        /// <summary>在指定变换处生成默认玩家角色。对应 UE5 SpawnDefaultPawnAtTransform_Implementation。</summary>
        /// <param name="spawnTransform">生成变换。</param>
        /// <returns>生成的玩家角色 Script，失败返回 null。</returns>
        public virtual NarrativePlayerCharacter SpawnDefaultPawnAtTransform(Transform spawnTransform)
        {
            if (string.IsNullOrEmpty(DefaultPlayerCharacterPrefabPath))
            {
                NarrativeLog.LogWarning("[NarrativeGameMode] DefaultPlayerCharacterPrefabPath 未设置");
                return null;
            }

            // Flax-已实现：与 ActorSpawnComponent/NarrativeCharacterSubsystem 相同的 Prefab 生成链路：
            // Content.Load<Prefab>(路径) + PrefabManager.SpawnPrefab（位置+朝向），再取角色脚本。
            var prefab = FlaxEngine.Content.Load<Prefab>(DefaultPlayerCharacterPrefabPath);
            if (prefab == null)
            {
                NarrativeLog.LogWarning($"[NarrativeGameMode] 玩家 Prefab 加载失败：{DefaultPlayerCharacterPrefabPath}");
                return null;
            }

            Actor spawned = PrefabManager.SpawnPrefab(prefab, spawnTransform.Translation, spawnTransform.Orientation);
            // SpawnPrefab 默认落 Scenes[0]（RootScene），玩家角色属玩法世界对象，搬入玩法场景
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.MoveToGameplayScene(spawned, "PlayerCharacter");
            var character = spawned != null ? spawned.GetScript<NarrativePlayerCharacter>() : null;
            if (character == null)
            {
                NarrativeLog.LogWarning("[NarrativeGameMode] 生成的 Prefab 上未找到 NarrativePlayerCharacter 脚本");
                return null;
            }

            return character;
        }

        /// <summary>为加入的控制器返回玩家定义。对应 UE5 GetPlayerDefinitionForController。</summary>
        /// <param name="controller">加入的玩家控制器（null 表示首个玩家）。</param>
        /// <returns>分配的玩家定义；列表为空或超出范围时返回 null。</returns>
        public virtual PlayerDefinition GetPlayerDefinitionForController(NarrativePlayerController controller)
        {
            if (PlayerDefinitions == null || PlayerDefinitions.Count == 0) return null;

            int index = _nextPlayerIndex;
            _nextPlayerIndex = (_nextPlayerIndex + 1) % PlayerDefinitions.Count;
            return PlayerDefinitions[index];
        }

        /// <summary>重置玩家分配索引（用于重新开始游戏时）。</summary>
        public virtual void ResetPlayerAssignment()
        {
            _nextPlayerIndex = 0;
        }

        // ===== 静态访问 =====

        /// <summary>获取当前场景的 NarrativeGameMode。</summary>
        public static NarrativeGameMode Get(Scene scene)
        {
            if (scene == null) return null;
            return scene.GetScript<NarrativeGameMode>();
        }

        /// <summary>获取当前激活场景的 NarrativeGameMode。</summary>
        public static NarrativeGameMode GetCurrent()
        {
            return Get(Level.GetScene(0));
        }
    }
}
