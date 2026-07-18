using FlaxEngine;

namespace NarrativePro.GAS.AbilityTasks
{
    /// <summary>
    /// 生成投射物的能力任务。对应 UE5 UAbilityTask_SpawnProjectile。
    /// 简化点：
    /// - TSubclassOf&lt;AActor&gt; 替换为字符串 Prefab 路径
    /// - 投射物生成后通过 Actor.AddChild 添加到场景
    /// - 投射物的物理由其自身 Script 处理（不在本任务中）
    /// </summary>
    public class AbilityTask_SpawnProjectile : AbilityTask
    {
        /// <summary>投射物 Prefab 路径。</summary>
        public string ProjectilePrefabPath = "";

        /// <summary>生成位置。</summary>
        public Vector3 SpawnLocation = Vector3.Zero;

        /// <summary>生成旋转。</summary>
        public Quaternion SpawnRotation = Quaternion.Identity;

        /// <summary>初始速度向量。</summary>
        public Vector3 Velocity = Vector3.Zero;

        /// <summary>生命周期（秒，0 = 永久）。</summary>
        public float LifeSpan = 5f;

        /// <summary>是否在世界坐标生成（false = 相对 Owner）。</summary>
        public bool bSpawnInWorld = true;

        /// <summary>是否使用 Owner 的 Transform 作为生成基础。</summary>
        public bool bUseOwnerTransform = false;

        /// <summary>是否使用 SpawnLocation 字段。</summary>
        public bool bUseSpawnLocation = true;

        /// <summary>是否使用 SpawnRotation 字段。</summary>
        public bool bUseSpawnRotation = true;

        /// <summary>是否使用 Velocity 字段。</summary>
        public bool bUseVelocity = true;

        /// <summary>是否使用 LifeSpan 字段。</summary>
        public bool bUseLifeSpan = true;

        /// <summary>已生成的投射物 Actor。</summary>
        public Actor SpawnedProjectile;

        /// <summary>创建任务实例。</summary>
        public static AbilityTask_SpawnProjectile Create(NarrativeGameplayAbility ability, string prefabPath)
        {
            var task = new AbilityTask_SpawnProjectile
            {
                OwningAbility = ability,
                ProjectilePrefabPath = prefabPath
            };
            return task;
        }

        public override void Activate()
        {
            base.Activate();

            if (OwningAbility?.Actor == null || string.IsNullOrEmpty(ProjectilePrefabPath))
            {
                Complete();
                return;
            }

            // 计算生成位置
            Vector3 location = bUseOwnerTransform ? OwningAbility.Actor.Position : (bUseSpawnLocation ? SpawnLocation : OwningAbility.Actor.Position);
            Quaternion rotation = bUseOwnerTransform ? OwningAbility.Actor.Orientation : (bUseSpawnRotation ? SpawnRotation : OwningAbility.Actor.Orientation);

            // 加载 Prefab 并生成（参考 NarrativeCharacterSubsystem.SpawnNPC_Internal 的同步加载模式）
            Prefab prefab = Content.LoadAsync<Prefab>(ProjectilePrefabPath);
            if (prefab == null)
            {
                NarrativePro.Core.NarrativeLog.LogError($"[AbilityTask_SpawnProjectile] 加载 Prefab 失败：{ProjectilePrefabPath}");
                Complete();
                return;
            }

            SpawnedProjectile = PrefabManager.SpawnPrefab(prefab, location, rotation);
            if (SpawnedProjectile == null)
            {
                NarrativePro.Core.NarrativeLog.LogError($"[AbilityTask_SpawnProjectile] 生成投射物 Actor 失败：{ProjectilePrefabPath}");
                Complete();
                return;
            }

            // 应用初始速度（若投射物带有 RigidBody 则设置线速度）
            if (bUseVelocity && Velocity.LengthSquared > 0f)
            {
                var rigidBody = SpawnedProjectile.GetScript<RigidBody>();
                if (rigidBody != null)
                {
                    rigidBody.LinearVelocity = Velocity;
                }
            }

            // 设置生命周期：Flax Actor 无 SetLifeSpan API（UE5 移植遗留 TODO）
            // TODO: 实现延迟销毁逻辑（可用自定义 Script 或 Object.Destroy 延迟调用）
            // if (bUseLifeSpan && LifeSpan > 0f)
            // {
            //     SpawnedProjectile.SetLifeSpan(LifeSpan);
            // }

            NarrativePro.Core.NarrativeLog.Log($"[AbilityTask_SpawnProjectile] 已生成投射物 {ProjectilePrefabPath}，位置={location}");

            // 生成完成后立即完成
            Complete();
        }
    }
}
