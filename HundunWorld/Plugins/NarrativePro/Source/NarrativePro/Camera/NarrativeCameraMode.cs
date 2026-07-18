using FlaxEngine;

namespace NarrativePro.Camera
{
    /// <summary>
    /// 相机模式基类。由 NarrativeCameraComponent 管理，支持在不同相机行为间平滑切换。
    /// 适配 UE5 UNarrativeCameraMode。
    /// 模式只指定相机期望的 FOV 和位置，相机组件根据 blendSpeed 平滑过渡。
    /// </summary>
    public class NarrativeCameraMode
    {
        /// <summary>默认 FOV（度）</summary>
        public float DefaultFOV { get; set; } = 90f;

        /// <summary>FOV 插值速度（单位/秒）</summary>
        public float DefaultFOVBlendSpeed { get; set; } = 10f;

        /// <summary>目标臂长度（弹簧臂）</summary>
        public float TargetArmLength { get; set; } = 300f;

        /// <summary>相对角色的偏移</summary>
        public Vector3 Offset { get; set; } = Vector3.Zero;

        /// <summary>偏移插值速度（单位/秒）</summary>
        public float OffsetInterpSpeed { get; set; } = 10f;

        /// <summary>枢轴点插值速度（单位/秒）</summary>
        public float PivotInterpSpeed { get; set; } = 10f;

        /// <summary>拥有此模式的相机组件</summary>
        public NarrativeCameraComponent OwningCamera { get; set; }

        /// <summary>进入模式时调用。可覆盖。</summary>
        public virtual void EnterMode() { }

        /// <summary>退出模式时调用。可覆盖。</summary>
        public virtual void ExitMode() { }

        /// <summary>获取期望的 FOV 和插值速度。可覆盖以动态生成。</summary>
        public virtual void GetDesiredFOV(out float fov, out float fovBlendSpeed)
        {
            fov = DefaultFOV;
            fovBlendSpeed = DefaultFOVBlendSpeed;
        }

        /// <summary>获取期望的相机偏移。可覆盖以动态生成。</summary>
        public virtual Vector3 GetCameraDesiredOffset()
        {
            return Offset;
        }

        /// <summary>获取相机根位置（弹簧臂根部）。</summary>
        public virtual Vector3 GetCameraRootLocation()
        {
            if (OwningCamera?.TargetActor != null)
            {
                return OwningCamera.TargetActor.Position;
            }
            return Vector3.Zero;
        }
    }
}
