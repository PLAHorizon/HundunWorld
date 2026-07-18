using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具生成器子系统。对应 UE5 UVehicleSpawnerSubsystem（VehicleSpawnerSubsystem.h）。
    /// 继承 UMassActorSpawnerSubsystem。处理 Mass 载具的 Actor 生成。
    /// 简化点：
    /// - Flax 无 Mass Entity System，改为 [Serializable] 单例类占位（Flax 不兼容）
    /// - TMap → Dictionary
    /// - FConstStructView/FActorSpawnParameters → object 占位
    /// </summary>
    [Serializable]
    public class VehicleSpawnerSubsystem
    {
        /// <summary>单例实例。</summary>
        public static VehicleSpawnerSubsystem Instance { get; } = new VehicleSpawnerSubsystem();

        /// <summary>获取单例。</summary>
        public static VehicleSpawnerSubsystem Get() => Instance;

        /// <summary>Actor 到座位索引的映射。对应 UE5 ActorToSeatIndexMap（mutable TMap）。</summary>
        [NonSerialized]
        public Dictionary<Actor, int> ActorToSeatIndexMap = new Dictionary<Actor, int>();

        /// <summary>生成 Actor。对应 UE5 SpawnActor。
        /// Flax-不兼容: UE5 的 Mass 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass，需自定义实现。</summary>
        /// <param name="spawnRequestView">生成请求视图（占位）。</param>
        /// <param name="outSpawnedActor">生成的 Actor。</param>
        /// <param name="inOutSpawnParameters">生成参数（占位）。</param>
        /// <returns>生成请求状态。</returns>
        public virtual int SpawnActor(object spawnRequestView, ref Actor outSpawnedActor, object inOutSpawnParameters)
        {
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现
            // ESpawnRequestStatus 简化为 int
            return 0;
        }
    }
}
