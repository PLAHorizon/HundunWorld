using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Spawners
{
    /// <summary>
    /// 生成器基类。对应 UE5 ASpawnerBase。
    ///
    /// World Partition 对动态生成的 Actor 支持不佳。生成器通过常驻世界来解决此问题，
    /// 由 World Partition 管理。生成器将已生成的 Actor 保存到存档记录，从而记住需要重生成的 Actor。
    /// 例如拾取物生成器：玩家拾取后，生成器根据存档记录判断哪些拾取物已被取走，不再重生成。
    /// 在 Narrative 1.2 中，生成器取代了聚落（Settlement）用于生成 NPC。
    ///
    /// 架构说明：UE5 中继承 AActor；Flax 中 Actor 为密封类不可继承，
    /// 故改为 Script 挂载到 Actor 上，通过所在 Actor 的 Transform 提供生成位置。
    /// UE5 中的 INarrativeSavableActor 接口方法（GetActorGUID/SetActorGUID）在此直接作为虚方法提供。
    /// </summary>
    public class SpawnerBase : Script
    {
        /// <summary>
        /// 根组件占位。对应 UE5 USceneComponent* SpawnerRoot。
        /// Flax 中 Actor 自带 Transform，无需额外根组件，生成位置直接取自 Actor 的 Transform。
        /// 运行时指向所在 Actor。
        /// </summary>
        [NonSerialized]
        [HideInEditor]
        public Actor SpawnerRoot;

        /// <summary>存档系统用于标识和保存生成器的 GUID</summary>
        public Guid SpawnerSaveGUID = Guid.NewGuid();

        /// <summary>是否在 BeginPlay（OnEnable）时激活生成器</summary>
        public bool bActivateOnBeginPlay = true;

        public override void OnEnable()
        {
            base.OnEnable();
            SpawnerRoot = Actor;

            if (bActivateOnBeginPlay)
            {
                SpawnActors();
            }
        }

        public override void OnDisable()
        {
            RemoveActors();
            SpawnerRoot = null;
            base.OnDisable();
        }

        /// <summary>生成所有 Actor。子类可覆盖以实现具体生成逻辑。</summary>
        public virtual void SpawnActors()
        {
            // 默认实现：遍历所在 Actor 上的所有 SpawnComponent 并触发生成
            var spawnComponents = Actor.GetScripts<SpawnComponent>();
            if (spawnComponents == null) return;

            foreach (var comp in spawnComponents)
            {
                if (comp != null)
                {
                    comp.TrySpawnActor();
                }
            }
        }

        /// <summary>移除所有已生成的 Actor。子类可覆盖以实现具体移除逻辑。</summary>
        public virtual void RemoveActors()
        {
            var spawnComponents = Actor.GetScripts<SpawnComponent>();
            if (spawnComponents == null) return;

            foreach (var comp in spawnComponents)
            {
                if (comp != null)
                {
                    comp.RemoveActor();
                }
            }
        }

        /// <summary>获取 Actor 的存档 GUID（对应 UE5 INarrativeSavableActor::GetActorGUID）</summary>
        public virtual Guid GetActorGUID()
        {
            return SpawnerSaveGUID;
        }

        /// <summary>设置 Actor 的存档 GUID（对应 UE5 INarrativeSavableActor::SetActorGUID）</summary>
        public virtual void SetActorGUID(Guid savedGUID)
        {
            SpawnerSaveGUID = savedGUID;
        }
    }
}
