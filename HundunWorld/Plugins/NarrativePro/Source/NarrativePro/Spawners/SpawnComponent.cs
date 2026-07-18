using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Spawners
{
    /// <summary>
    /// 生成组件。对应 UE5 USpawnComponent。
    /// 挂载到生成器上，当被请求时在其 Transform 处生成一个 Actor。
    ///
    /// 架构说明：UE5 中继承 USceneComponent（拥有独立 Transform）；
    /// Flax 中 Script 无独立 Transform，故以 SpawnRelativeTransform（相对所在 Actor 的偏移）
    /// 来表达组件的局部变换，生成时通过 GetSpawnTransform() 计算世界变换。
    /// UE5 中的 INarrativeSavableComponent 接口方法在子类中按需实现。
    /// </summary>
    public class SpawnComponent : Script
    {
        /// <summary>
        /// 生成位置的相对偏移变换。对应 UE5 USceneComponent 的 RelativeTransform。
        /// 生成时世界变换 = 所在 Actor 的 Transform * SpawnRelativeTransform。
        /// </summary>
        public Transform SpawnRelativeTransform = Transform.Identity;

        /// <summary>已生成的 Actor 引用。对应 UE5 TWeakObjectPtr&lt;AActor&gt; SpawnedActor。Flax 中改为直接引用。</summary>
        [NonSerialized]
        [HideInEditor]
        public Actor SpawnedActor;

        /// <summary>计算生成用的世界变换。</summary>
        /// <returns>世界空间的 Transform</returns>
        public virtual Transform GetSpawnTransform()
        {
            if (Actor == null) return SpawnRelativeTransform;
            // 将相对变换叠加到所在 Actor 的世界变换上
            var worldT = Actor.Transform;
            var localT = SpawnRelativeTransform;
            // 合并变换：先平移+旋转到 Actor 空间，再应用局部偏移
            var combined = new Transform(
                worldT.Translation + Vector3.Transform(localT.Translation, worldT.Orientation) * worldT.Scale,
                worldT.Orientation * localT.Orientation,
                worldT.Scale * localT.Scale
            );
            return combined;
        }

        /// <summary>是否应当生成 Actor。子类可覆盖以加入生成条件判断。</summary>
        public virtual bool ShouldSpawnActor()
        {
            return SpawnedActor == null;
        }

        /// <summary>是否应当反生成（移除）Actor。子类可覆盖。</summary>
        public virtual bool ShouldDespawnActor()
        {
            return SpawnedActor != null;
        }

        /// <summary>尝试生成 Actor。返回是否成功。</summary>
        public virtual bool TrySpawnActor()
        {
            if (!ShouldSpawnActor()) return false;

            Actor spawned = SpawnActor();
            if (spawned != null)
            {
                SpawnedActor = spawned;
                return true;
            }
            return false;
        }

        /// <summary>尝试反生成 Actor。返回是否成功。</summary>
        public virtual bool TryDespawnActor()
        {
            if (!ShouldDespawnActor()) return false;
            return RemoveActor();
        }

        /// <summary>
        /// 生成 Actor。对应 UE5 BlueprintNativeEvent SpawnActor。
        /// 子类覆盖此方法以实现具体的生成逻辑，返回生成的 Actor。
        /// </summary>
        public virtual Actor SpawnActor()
        {
            // 基类默认不生成具体对象，由子类（如 ActorSpawnComponent、NPCSpawnComponent）覆盖
            return null;
        }

        /// <summary>移除已生成的 Actor。返回是否成功。对应 UE5 RemoveActor。</summary>
        public virtual bool RemoveActor()
        {
            if (SpawnedActor != null)
            {
                Actor toRemove = SpawnedActor;
                SpawnedActor = null;
                if (toRemove != null)
                {
                    Destroy(toRemove, 0.1f);
                }
                return true;
            }
            return false;
        }

        /// <summary>获取编辑器显示标签。对应 UE5 BlueprintNativeEvent GetEditorLabel。</summary>
        public virtual string GetEditorLabel()
        {
            return GetType().Name;
        }
    }
}
