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

            // 应用视角旋转
            CameraActor.Orientation = Quaternion.Euler(ViewPitch, ViewYaw, 0);

            // 第一人称模式下的特殊处理
            if (bWantsFirstPersonRender && OwningCharacter != null)
            {
                CameraActor.Position = OwningCharacter.Position + Vector3.Up * 60f * FirstPersonRenderScale;
            }
        }

        /// <summary>是否需要第一人称渲染。</summary>
        public virtual bool WantsFirstPersonRender() => bWantsFirstPersonRender;

        /// <summary>初始化。控制器准备好时调用。</summary>
        public virtual void InitializeFor(Actor controller)
        {
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
            DoUpdateCamera(Time.DeltaTime);
        }
    }
}
