using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles
{
    /// <summary>
    /// 运动学载具移动模型。对应 UE5 <c>UChaosWheeledVehicleMovementComponent</c>
    /// （NarrativeWheeledVehicle.h 的 VehicleMovementComponent、VehicleSpawnerSubsystem.cpp:46-53 的 SetBaseSnapshot）。
    /// <para>
    /// 为什么不是「Chaos 直译」：Flax 1.12 完全没有载具物理 API
    /// （已核对 FlaxEngine.CSharp.xml：<c>Vehicle</c> / <c>WheeledVehicle</c> 0 命中），
    /// 所以车轮受力、轮胎摩擦、悬挂都不可能存在。本类实现的是 UE5 侧被 gameplay 读取的那一面：
    /// 油门/转向输入值、当前速度/速度向量、Snapshot 初始化、以及 <c>bReverseAsBrake</c> 语义
    /// （玩家驾驶时倒挡当刹车，NarrativeWheeledVehicle.cpp 的 PossessedBy 分支）。
    /// 速度的唯一积分点在这里（<see cref="Integrate"/>），由
    /// <c>VehicleMovementProcessor</c> 每帧调用一次；高/低 LOD 共用同一实例，避免两处真相。
    /// </para>
    /// </summary>
    public class KinematicVehicleMovement
    {
        /// <summary>油门（-1..1，负值即刹车）。对应 UE5 FVehicleLocomotionFragment::Throttle。</summary>
        public float Throttle;

        /// <summary>转向输入（-1..1）。对应 UE5 FVehicleLocomotionFragment::Steering。</summary>
        public float Steering;

        /// <summary>当前沿车道方向的标量速度。</summary>
        public float Speed;

        /// <summary>当前速度向量。对应 UE5 GetVelocity()。</summary>
        public Vector3 Velocity;

        /// <summary>玩家驾驶时倒挡当刹车。对应 UE5 bReverseAsBrake。</summary>
        public bool bReverseAsBrake;

        /// <summary>初速度（Snapshot 注入用）。对应 UE5 FBaseSnapshotData::LinearVelocity。</summary>
        private Vector3 _snapshotVelocity;

        /// <summary>读取当前速度。对应 UE5 GetVelocity().Size()</summary>
        public float GetSpeed() => Speed;

        /// <summary>读取速度向量。对应 UE5 GetVelocity()。</summary>
        public Vector3 GetVelocity() => Velocity;

        /// <summary>
        /// 用 Mass 侧状态做初始化。对应 UE5 <c>VehicleComponent->SetBaseSnapshot(BaseSnapshotData)</c>
        /// （VehicleSpawnerSubsystem.cpp:46-53）—— 高 LOD Actor 接手时不该从零速起步。
        /// </summary>
        public void SetBaseSnapshot(Transform transform, Vector3 linearVelocity)
        {
            _snapshotVelocity = linearVelocity;
            Velocity = linearVelocity;
            Speed = linearVelocity.Length;
        }

        /// <summary>
        /// 油门积分（本帧唯一的标量速度推进）。
        /// </summary>
        /// <param name="desiredSpeed">期望速度（避让/红绿灯算出的上限）。</param>
        /// <param name="throttleOutput">油门 PID 输出。</param>
        /// <param name="accelerationAuthority">油门输出到加速度的换算上限（Flax 无质量模型，取固定上限）。</param>
        /// <param name="maxSpeed">绝对上限。</param>
        /// <param name="deltaTime">帧时长。</param>
        /// <returns>积分后的速度。</returns>
        public float Integrate(float desiredSpeed, float throttleOutput, float accelerationAuthority, float maxSpeed, float deltaTime)
        {
            float authority = Mathf.Max(1f, accelerationAuthority);
            float next = Speed + throttleOutput * authority * deltaTime;

            // bReverseAsBrake：倒挡输入（负油门）只用于减速，不产生倒车（对应 UE5 玩家驾驶的同一个开关）
            if (bReverseAsBrake && throttleOutput < 0f)
            {
                next = Mathf.Max(0f, next);
            }

            // 期望速度是硬上限（UE5 cpp:205 的 DesiredSpeed = clamp(Speed, 0, MaxSpeed) 之后由其驱动位移）
            next = Mathf.Clamp(next, 0f, Mathf.Min(desiredSpeed, maxSpeed));
            Speed = next;
            return Speed;
        }
    }

    /// <summary>
    /// Narrative 轮式载具基类。对应 UE5 ANarrativeWheeledVehicle（NarrativeWheeledVehicle.h/.cpp）。
    /// <para>
    /// UE5 中它继承 ANarrativeVehicleBase 并持有 <c>UChaosWheeledVehicleMovementComponent</c>；
    /// Flax 无 Chaos，故移动组件替换为 <see cref="KinematicVehicleMovement"/>（同名字段、同名读取接口、
    /// 同一 SetBaseSnapshot/PossessedBy 语义），使 <c>TrafficVehicleActor</c> 与将来可能的玩家驾驶载具
    /// 都能有一个可 gameplay 查询的「移动组件」。
    /// </summary>
    /// </para>
    /// </summary>
    public class NarrativeWheeledVehicle : NarrativeVehicleBase
    {
        /// <summary>载具移动组件名称。对应 UE5 VehicleMovementComponentName。</summary>
        public const string VehicleMovementComponentName = "VehicleMovementComp";

        /// <summary>载具模拟组件。对应 UE5 VehicleMovementComponent（UChaosWheeledVehicleMovementComponent）。</summary>
        [NonSerialized]
        protected KinematicVehicleMovement VehicleMovementComponent;

        public override void OnEnable()
        {
            base.OnEnable();
            // 对应 UE5 构造函数里 CreateDefaultSubobject<UChaosWheeledVehicleMovementComponent>
            VehicleMovementComponent ??= new KinematicVehicleMovement();
        }

        /// <summary>获取轮式载具移动组件。对应 UE5 GetVehicleMovementComponent。</summary>
        public virtual KinematicVehicleMovement GetVehicleMovementComponent()
        {
            VehicleMovementComponent ??= new KinematicVehicleMovement();
            return VehicleMovementComponent;
        }

        /// <summary>
        /// 被控制器拥有时调用。对应 UE5 PossessedBy：玩家控制器强制 <c>bReverseAsBrake = true</c>。
        /// Flax 无 Controller 类，用「传入的 Actor 是否带玩家角色脚本」判定玩家驾驶。
        /// </summary>
        public override void PossessedBy(Actor newController)
        {
            base.PossessedBy(newController);

            var movement = GetVehicleMovementComponent();
            if (newController == null)
            {
                movement.bReverseAsBrake = false;
                return;
            }

            bool bIsPlayer = newController.GetScript<NarrativePro.UnrealFramework.NarrativePlayerCharacter>() != null;
            movement.bReverseAsBrake = bIsPlayer;
            NarrativeLog.Log($"[NarrativeWheeledVehicle] {Actor?.Name} 拥有者切换为 {newController.Name}，bReverseAsBrake={movement.bReverseAsBrake}");
        }

        /// <summary>取消拥有。对应 UE5 UnPossessed：复位倒挡刹车。</summary>
        public override void UnPossessed()
        {
            base.UnPossessed();
            if (VehicleMovementComponent != null)
            {
                VehicleMovementComponent.bReverseAsBrake = false;
                VehicleMovementComponent.Throttle = 0f;
                VehicleMovementComponent.Steering = 0f;
            }
        }
    }
}
