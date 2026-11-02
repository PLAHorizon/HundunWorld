using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 载具可视化打包数据。对应 UE5 FVehicleVisualPackedData（VehicleVisualizationProcessor.h）。
    /// UE5 把它作为材质参数批量下发；Flax 侧由本结构承载同一份语义并写进 Actor 脚本。
    /// </summary>
    [Serializable]
    public class VehicleVisualPackedData
    {
        public VehicleVisualPackedData() { }

        /// <summary>载具材质已用索引 0 实现泥污效果，此值设为 0 即关闭。</summary>
        public float Dirt = 0f;

        /// <summary>颜色值（材质色相索引）。对应 UE5 FVehicleLocomotionFragment::Color。</summary>
        public float Color = 0f;

        /// <summary>当前速度（世界单位/秒）。UE5 由 SyncVelocityQuery 写 FMassVelocityFragment，这里同步给 Actor。</summary>
        public float Speed = 0f;

        /// <summary>油门（-1..1）。对应 UE5 FVehicleLocomotionFragment::Throttle。</summary>
        public float Throttle = 0f;

        /// <summary>是否刹车中（油门为正但实际速度低于期望速度的容差）。</summary>
        public bool bBraking;

        public override string ToString() => $"Packed(dirt={Dirt:0.##}, color={Color:0.##}, speed={Speed:0.#}, throttle={Throttle:0.###})";
    }

    /// <summary>
    /// 载具可视化处理器（Flax 侧手写实现）。对应 UE5 UVehicleVisualizationProcessor
    /// （VehicleVisualizationProcessor.h/.cpp:1-199 的 Actor 分支）。
    /// <para>
    /// UE5 是 UMassTranslator：把实体片段翻译成高 LOD Actor 的可视状态（速度、颜色打包数据、
    /// 泥污材质参数）。Flax 无 Mass 渲染器管线，可移植的部分是「把同一份数据写进载具 Actor 脚本」，
    /// 由 <see cref="TrafficVehicleActor"/> 自己决定如何呈现（材质/音效/动画蓝图参数）。
    /// </para>
    /// <para>
    /// 明确不再移植的部分：UE5 的 <c>UVehicleISMVisualizationProcessor</c>（实例化静态网格桶 +
    /// MassRendererVisibility 组件）—— 它依赖 Mass 的 representation/ISM 子系统，
    /// FlaxEngine.CSharp.xml 里没有对应物（已核对无 ISM 桶式批次接口），
    /// 因此该占位类已删除并在此记录，而不是留一个空壳。
    /// </para>
    /// </summary>
    [Serializable]
    public class VehicleVisualizationProcessor
    {
        /// <summary>判定为「在刹车」的速度容差（世界单位/秒）。</summary>
        public float BrakingSpeedTolerance = 20f;

        /// <summary>泥污系数随里程增长（对应 UE5 材质里按行驶距离累积的泥污）。</summary>
        public float DirtPerSecond = 0.02f;

        /// <summary>本帧同步过多少个 Actor。</summary>
        public int SyncedActorCount { get; private set; }

        /// <summary>
        /// 同步可视数据。对应 UE5 UVehicleVisualizationProcessor::Execute。
        /// </summary>
        public void UpdateVisualization(IReadOnlyList<TrafficVehicle> vehicles, float deltaTime)
        {
            SyncedActorCount = 0;
            if (vehicles == null) return;

            foreach (var vehicle in vehicles)
            {
                if (vehicle == null) continue;

                var actor = vehicle.ComponentWrapper.Vehicle ?? vehicle.Actor?.GetScript<TrafficVehicleActor>();
                if (actor == null) continue;

                var packed = new VehicleVisualPackedData
                {
                    Color = vehicle.Locomotion.Color,
                    Speed = vehicle.Speed,
                    Throttle = Mathf.Clamp(vehicle.Locomotion.Throttle, -1f, 1f),
                    Dirt = Mathf.Clamp(actor.VisualDirt + DirtPerSecond * (vehicle.LOD == EVehicleRepresentationLOD.High ? deltaTime : 0f), 0f, 1f),
                };
                packed.bBraking = vehicle.Locomotion.DesiredSpeed - vehicle.Speed > BrakingSpeedTolerance;

                actor.ApplyVisualPackedData(packed, vehicle.Velocity, vehicle.LOD);
                SyncedActorCount++;
            }
        }
    }
}
