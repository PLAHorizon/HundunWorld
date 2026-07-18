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
        }

        // ===== 初始化 =====

        /// <summary>游戏初始化时调用。对应 UE5 InitGame。</summary>
        public virtual void InitGame()
        {
            NarrativeLog.Log("[NarrativeGameMode] InitGame");
            // TODO [需接入存档系统]: 初始化子系统、加载存档等
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

            // TODO [需接入 Prefab 实例化系统]: 从 Prefab 路径加载并实例化玩家角色 Actor
            // 简化版：在场景中查找已有的玩家角色
            var existing = Level.GetScene(0).GetScript<NarrativePlayerCharacter>();
            if (existing != null)
            {
                existing.Actor.Position = spawnTransform.Translation;
                existing.Actor.Orientation = spawnTransform.Orientation;
                return existing;
            }

            NarrativeLog.LogWarning("[NarrativeGameMode] 玩家角色生成未实现，需要 Prefab 系统");
            return null;
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
