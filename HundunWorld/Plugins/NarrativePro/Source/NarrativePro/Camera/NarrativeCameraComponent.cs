using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Camera
{
    /// <summary>
    /// 相机组件。管理不同相机模式之间的平滑过渡。
    /// 适配 UE5 UNarrativeCameraComponent（继承 UCameraComponent）。
    /// Flax 中 Camera 是 Actor 而非 Component，因此本组件作为 Script，
    /// 通过引用关联的 Camera Actor 控制其 FOV 和变换。
    /// </summary>
    public class NarrativeCameraComponent : Script
    {
        /// <summary>默认相机模式类 ID（对应 UE TSubclassOf）</summary>
        public string DefaultCameraModeClassId { get; set; } = "";

        /// <summary>当前相机模式</summary>
        public NarrativeCameraMode CurrentCameraMode { get; protected set; }

        /// <summary>关联的 Camera Actor（自动查找或手动指定）</summary>
        public FlaxEngine.Camera CameraActor { get; set; }

        /// <summary>关联的弹簧臂 Actor（用于目标臂长度控制）</summary>
        public Actor SpringArmActor { get; set; }

        /// <summary>跟随目标 Actor</summary>
        public Actor TargetActor { get; set; }

        // 缓存的相机模式实例（按类 ID 索引）
        private readonly Dictionary<string, NarrativeCameraMode> _modeInstances = new Dictionary<string, NarrativeCameraMode>();

        // 当前插值状态
        private float _currentFOV = 90f;
        private Vector3 _currentOffset = Vector3.Zero;
        private Vector3 _currentPivot = Vector3.Zero;

        public override void OnEnable()
        {
            base.OnEnable();
            // 自动查找相机
            if (CameraActor == null)
            {
                CameraActor = FlaxEngine.Camera.MainCamera;
            }
            // 自动查找目标 Actor
            if (TargetActor == null)
            {
                TargetActor = Actor;
            }

            // 设置默认相机模式
            if (!string.IsNullOrEmpty(DefaultCameraModeClassId))
            {
                SetCameraMode(DefaultCameraModeClassId);
            }
        }

        public override void OnDisable()
        {
            if (CurrentCameraMode != null)
            {
                CurrentCameraMode.ExitMode();
                CurrentCameraMode = null;
            }
            _modeInstances.Clear();
            base.OnDisable();
        }

        /// <summary>设置相机模式。modeClassId 对应已注册的模式类。</summary>
        public virtual void SetCameraMode(string modeClassId)
        {
            if (string.IsNullOrEmpty(modeClassId)) return;
            var newMode = FindOrCreateCameraMode(modeClassId);
            if (newMode == null) return;

            if (CurrentCameraMode != null)
            {
                CurrentCameraMode.ExitMode();
            }
            CurrentCameraMode = newMode;
            newMode.EnterMode();
        }

        /// <summary>切换回默认相机模式。</summary>
        public virtual void SetCameraModeToDefault()
        {
            if (!string.IsNullOrEmpty(DefaultCameraModeClassId))
            {
                SetCameraMode(DefaultCameraModeClassId);
            }
        }

        /// <summary>查找或创建相机模式实例。</summary>
        protected virtual NarrativeCameraMode FindOrCreateCameraMode(string modeClassId)
        {
            if (string.IsNullOrEmpty(modeClassId)) return null;

            if (_modeInstances.TryGetValue(modeClassId, out var existing))
            {
                return existing;
            }

            // 通过类型注册表创建实例
            var mode = CameraModeRegistry.CreateMode(modeClassId);
            if (mode != null)
            {
                mode.OwningCamera = this;
                _modeInstances[modeClassId] = mode;
            }
            return mode;
        }

        /// <summary>每帧更新相机视图。</summary>
        public override void OnUpdate()
        {
            if (CurrentCameraMode == null) return;

            float dt = Time.DeltaTime;

            // FOV 插值
            CurrentCameraMode.GetDesiredFOV(out float desiredFOV, out float fovBlendSpeed);
            _currentFOV = InterpolateTo(_currentFOV, desiredFOV, dt, fovBlendSpeed);
            if (CameraActor != null)
            {
                CameraActor.FieldOfView = _currentFOV;
            }

            // 偏移插值
            Vector3 desiredOffset = CurrentCameraMode.GetCameraDesiredOffset();
            _currentOffset = Vector3.Lerp(_currentOffset, desiredOffset, dt * CurrentCameraMode.OffsetInterpSpeed);

            // 枢轴插值
            Vector3 desiredPivot = CurrentCameraMode.GetCameraRootLocation();
            _currentPivot = Vector3.Lerp(_currentPivot, desiredPivot, dt * CurrentCameraMode.PivotInterpSpeed);

            // 应用相机位置
            if (CameraActor != null)
            {
                CameraActor.Position = _currentPivot + _currentOffset;
            }

            // 弹簧臂长度
            if (SpringArmActor != null)
            {
                var armScript = SpringArmActor.GetScript<NarrativeSpringArm>();
                if (armScript != null)
                {
                    armScript.TargetArmLength = InterpolateTo(armScript.TargetArmLength, CurrentCameraMode.TargetArmLength, dt, CurrentCameraMode.OffsetInterpSpeed);
                }
            }
        }

        /// <summary>获取弹簧臂组件（如有）。</summary>
        public NarrativeSpringArm GetSpringArm()
        {
            return SpringArmActor?.GetScript<NarrativeSpringArm>();
        }

        private static float InterpolateTo(float current, float target, float dt, float interpSpeed)
        {
            if (interpSpeed <= 0f) return target;
            float delta = target - current;
            float step = delta * Math.Min(1f, dt * interpSpeed);
            return current + step;
        }
    }

    /// <summary>
    /// 相机模式注册表。用于通过类 ID 创建相机模式实例。
    /// 替代 UE 的 TSubclassOf 反射机制。
    /// </summary>
    public static class CameraModeRegistry
    {
        private static readonly Dictionary<string, Func<NarrativeCameraMode>> _creators = new Dictionary<string, Func<NarrativeCameraMode>>();

        /// <summary>注册一个相机模式类。</summary>
        public static void Register(string modeClassId, Func<NarrativeCameraMode> creator)
        {
            _creators[modeClassId] = creator;
        }

        /// <summary>创建模式实例。返回 null 表示未注册。</summary>
        public static NarrativeCameraMode CreateMode(string modeClassId)
        {
            if (string.IsNullOrEmpty(modeClassId)) return null;
            if (_creators.TryGetValue(modeClassId, out var creator))
            {
                return creator();
            }
            NarrativeLog.LogWarning($"[Camera] 未注册的相机模式类 ID: {modeClassId}");
            return null;
        }
    }

    /// <summary>
    /// Narrative 弹簧臂组件（简化版）。适配 UE5 USpringArmComponent。
    /// 实际弹簧臂复杂逻辑（碰撞检测、滞后、镜头回弹）由项目现有 SpringArmCamera 实现。
    /// 此类仅作为接口桥接，由 Camera 模式控制目标臂长度。
    /// </summary>
    public class NarrativeSpringArm : Script
    {
        /// <summary>目标臂长度</summary>
        public float TargetArmLength { get; set; } = 300f;

        /// <summary>当前臂长度</summary>
        public float CurrentArmLength { get; set; } = 300f;

        /// <summary>臂插值速度</summary>
        public float ArmLengthInterpSpeed { get; set; } = 10f;

        public override void OnUpdate()
        {
            float dt = Time.DeltaTime;
            CurrentArmLength = MathCurrentArmLength(dt);
        }

        private float MathCurrentArmLength(float dt)
        {
            if (ArmLengthInterpSpeed <= 0f) return TargetArmLength;
            float delta = TargetArmLength - CurrentArmLength;
            return CurrentArmLength + delta * Math.Min(1f, dt * ArmLengthInterpSpeed);
        }
    }
}
