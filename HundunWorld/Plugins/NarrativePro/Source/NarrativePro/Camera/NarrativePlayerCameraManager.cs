using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Camera
{
    /// <summary>
    /// 玩家相机管理器。处理视角旋转、第一人称渲染切换。
    /// 适配 UE5 ANarrativePlayerCameraManager。
    /// Flax 中无 PlayerCameraManager 等价物，简化为 Script，
    /// 由 PlayerController 持有，每帧更新相机旋转。
    /// </summary>
    public class NarrativePlayerCameraManager : Script
    {
        /// <summary>是否使用第一人称渲染</summary>
        public bool bWantsFirstPersonRender { get; set; } = false;

        /// <summary>第一人称渲染缩放</summary>
        public float FirstPersonRenderScale { get; set; } = 1.0f;

        /// <summary>关联的相机 Actor</summary>
        public FlaxEngine.Camera CameraActor { get; set; }

        /// <summary>关联的角色 Actor</summary>
        public Actor OwningCharacter { get; set; }

        /// <summary>关联的控制器 Actor</summary>
        public Actor OwningController { get; set; }

        /// <summary>视角偏航角（度）</summary>
        public float ViewYaw { get; set; } = 0f;

        /// <summary>视角俯仰角（度）</summary>
        public float ViewPitch { get; set; } = 0f;

        /// <summary>偏航角速度（度/秒）</summary>
        public float YawSpeed { get; set; } = 200f;

        /// <summary>俯仰角速度（度/秒）</summary>
        public float PitchSpeed { get; set; } = 200f;

        /// <summary>最小俯仰角（度）</summary>
        public float MinPitch { get; set; } = -89f;

        /// <summary>最大俯仰角（度）</summary>
        public float MaxPitch { get; set; } = 89f;

        /// <summary>
        /// 是否由本管理器接管相机朝向。默认 false——本游戏运行期是「宿主装配」模式，
        /// 相机朝向由游戏自己的 PlayerController/ThirdPersonCamera 负责；只有当插件真正拥有相机控制权
        /// （角色模式且插件相机模式处于激活态）时，控制器才会把本位翻成 true，避免逐帧覆盖第三方相机朝向导致回归。
        /// </summary>
        public bool bDrivesCameraOrientation { get; private set; } = false;

        /// <summary>由控制器设定是否接管相机朝向（幂等，值不变时无副作用）。</summary>
        public void SetDrivesCameraOrientation(bool bDrive, FlaxEngine.Camera cameraToDrive = null)
        {
            bDrivesCameraOrientation = bDrive;
            if (cameraToDrive != null) CameraActor = cameraToDrive;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (CameraActor == null)
            {
                CameraActor = FlaxEngine.Camera.MainCamera;
            }
        }

        /// <summary>处理视角旋转。每帧调用以应用输入增量。</summary>
        public virtual void ProcessViewRotation(float deltaTime, ref float outYaw, ref float outPitch, float deltaYaw, float deltaPitch)
        {
            outYaw += deltaYaw * YawSpeed * deltaTime;
            outPitch += deltaPitch * PitchSpeed * deltaTime;

            // 限制俯仰角
            outPitch = Mathf.Clamp(outPitch, MinPitch, MaxPitch);
        }

        /// <summary>每帧更新相机。</summary>
        public virtual void DoUpdateCamera(float deltaTime)
        {
            if (CameraActor == null) return;

            // 朝向仅在插件真正拥有相机控制权时才写，否则会把游戏第三方相机的朝向覆盖掉（回归）。
            if (bDrivesCameraOrientation)
            {
                CameraActor.Orientation = Quaternion.Euler(ViewPitch, ViewYaw, 0);
            }

            // 第一人称模式下的特殊处理
            if (bWantsFirstPersonRender && OwningCharacter != null)
            {
                CameraActor.Position = OwningCharacter.Position + Vector3.Up * 60f * FirstPersonRenderScale;
            }
        }

        /// <summary>是否需要第一人称渲染。</summary>
        public virtual bool WantsFirstPersonRender() => bWantsFirstPersonRender;

        /// <summary>
        /// 初始化。控制器准备好时调用（幂等：同一控制器重复调用不重复处理）。
        /// 缺陷③的接线点——此前本方法零调用方，常驻 RootScene 的管理器永远 self-disable。
        /// </summary>
        public virtual void InitializeFor(Actor controller)
        {
            if (controller == null) return;
            if (OwningController == controller) return; // 幂等

            OwningController = controller;
            NarrativeLog.Log($"[PlayerCameraManager] 初始化完成，控制器: {controller?.Name}");
        }

        /// <summary>添加视角偏航输入。</summary>
        public void AddYawInput(float value)
        {
            ViewYaw += value * YawSpeed * Time.DeltaTime;
        }

        /// <summary>添加视角俯仰输入。</summary>
        public void AddPitchInput(float value)
        {
            ViewPitch = Mathf.Clamp(ViewPitch + value * PitchSpeed * Time.DeltaTime, MinPitch, MaxPitch);
        }

        public override void OnUpdate()
        {
            // 仅在通过 InitializeFor 绑定控制器后才接管相机。
            // 该脚本实例常驻 RootScene 且没有任何调用方：一旦 OnEnable 时 Camera.MainCamera 非空，
            // 它就会逐帧用 ViewYaw/ViewPitch 覆盖第三方相机（ThirdPersonCamera）写好的朝向。
            if (OwningController == null) return;

            DoUpdateCamera(Time.DeltaTime);
        }
    }
}
