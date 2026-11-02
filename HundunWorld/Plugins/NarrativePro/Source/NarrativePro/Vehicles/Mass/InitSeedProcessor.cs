using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 种子初始化处理器。对应 UE5 UInitSeedProcessor（InitSeedProcessor.h/.cpp:11-38）。
    /// <para>
    /// UE5 是观察者处理器：实体创建时若 <c>FSeedFragment::Seed == 0</c>，用全局随机流写入种子，
    /// 并创建该实体专属的 <c>FRandomStream</c>。
    /// Flax 侧由 <see cref="VehicleTrait.BuildVehicle"/> 显式调用 <see cref="InitializeSeed"/>，
    /// 语义一致（0 → 随机），并且同一颗种子会同时决定乘客数量与后续车道选择，
    /// 所以重复调用不会改变已定种子（幂等）。
    /// </para>
    /// </summary>
    [Serializable]
    public class InitSeedProcessor
    {
        /// <summary>UE5 用「无效种子」0 表示需要重新随机。</summary>
        public const int InvalidSeed = 0;

        private static readonly Random GlobalRandom = new Random();

        /// <summary>已写入种子的实体数（诊断自证）。</summary>
        public int InitializedCount { get; private set; }

        /// <summary>
        /// 为实体初始化种子。对应 UE5 UInitSeedProcessor::Execute。
        /// </summary>
        /// <param name="vehicle">目标实体。</param>
        /// <param name="requestedSeed">请求种子；&lt;=0 表示随机（对应 UE5 Seed==0 分支）。</param>
        /// <returns>实体最终使用的种子。</returns>
        public int InitializeSeed(TrafficVehicle vehicle, int requestedSeed)
        {
            if (vehicle == null) return InvalidSeed;

            int seed = vehicle.Seed != null ? vehicle.Seed.Seed : InvalidSeed;
            if (requestedSeed > 0)
            {
                seed = requestedSeed;
            }
            else if (seed <= InvalidSeed)
            {
                // 对应 UE5 FRandomStream(GlobalRandomSeed) 取值
                lock (GlobalRandom)
                {
                    seed = GlobalRandom.Next(1, int.MaxValue);
                }
            }

            vehicle.Seed = new SeedFragment(seed);
            vehicle.Seed.Stream = new Random(seed);
            InitializedCount++;
            return seed;
        }
    }
}
