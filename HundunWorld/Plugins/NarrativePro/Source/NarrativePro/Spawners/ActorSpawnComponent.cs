using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Spawners
{
    /// <summary>
    /// Actor 生成碰撞处理方式。对应 UE5 ESpawnActorCollisionHandlingMethod。
    /// </summary>
    public enum ESpawnActorCollisionHandlingMethod
    {
        /// <summary>未定义，使用默认行为</summary>
        Undefined = 0,

        /// <summary>无碰撞</summary>
        NoCollision = 1,

        /// <summary>总是生成，忽略碰撞</summary>
        AlwaysSpawn = 2,

        /// <summary>尽可能调整位置，但总是生成</summary>
        AdjustIfPossibleButAlwaysSpawn = 3,

        /// <summary>尽可能调整位置，若碰撞则不生成</summary>
        AdjustIfPossibleButDontSpawnIfColliding = 4,

        /// <summary>若碰撞则不生成</summary>
        DontSpawnIfColliding = 5
    }

    /// <summary>
    /// Actor 生成组件。对应 UE5 UActorSpawnComponent。
    /// 继承自 SpawnComponent，按指定的 Actor 类（Prefab 路径占位）生成 Actor。
    /// </summary>
    public class ActorSpawnComponent : SpawnComponent
    {
        /// <summary>
        /// 要生成的 Actor 类。对应 UE5 TSubclassOf&lt;AActor&gt; ActorClass。
        /// Flax 中以 Prefab 路径占位，运行时通过 PrefabManager.SpawnPrefab 生成。
        /// </summary>
        public string ActorClass = "";

        /// <summary>生成时对碰撞的处理方式。对应 UE5 SpawnCollisionHandlingOverride。</summary>
        public ESpawnActorCollisionHandlingMethod SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod.AdjustIfPossibleButAlwaysSpawn;

        /// <summary>生成 Actor。对应 UE5 SpawnActor_Implementation。</summary>
        public override Actor SpawnActor()
        {
            if (string.IsNullOrEmpty(ActorClass))
            {
                NarrativeLog.LogWarning("ActorSpawnComponent: ActorClass 路径为空，无法生成");
                return null;
            }

            Prefab prefab = Content.LoadAsync<Prefab>(ActorClass);
            if (prefab == null)
            {
                NarrativeLog.LogError($"ActorSpawnComponent: 加载 Prefab 失败：{ActorClass}");
                return null;
            }

            Transform spawnTransform = GetSpawnTransform();

            // 注：碰撞处理在 Flax 中无直接对应 API，此处先按 AlwaysSpawn 处理，
            // 碰撞调整需在生成后通过物理查询手动处理。
            Actor spawned = PrefabManager.SpawnPrefab(prefab, spawnTransform.Translation, spawnTransform.Orientation);
            if (spawned == null)
            {
                NarrativeLog.LogError($"ActorSpawnComponent: 生成 Actor 失败：{ActorClass}");
                return null;
            }

            // 应用缩放
            spawned.Scale = spawnTransform.Scale;

            return spawned;
        }

        /// <summary>获取编辑器显示标签。</summary>
        public override string GetEditorLabel()
        {
            if (!string.IsNullOrEmpty(ActorClass))
            {
                return $"Actor Spawn ({ActorClass})";
            }
            return "Actor Spawn (未配置)";
        }
    }
}
