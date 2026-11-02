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

        /// <summary>
        /// 相机输出所有权谓词（缺陷②(b)，单一开关）。仅当为 true 时，<see cref="OnUpdate"/>
        /// 才会写 <see cref="CameraActor"/> 的 FieldOfView / Position / Transform（FOV、机位、震动叠加）。
        /// <para>
        /// 默认 false：宿主装配模式下引导（NarrativeRuntimeBootstrap）把本组件挂到玩家宿主 Actor 上，
        /// 让模式栈 / ADS FOV 值 / 弹簧臂 / 对话 cinecam 有归属者可被使用，但本游戏的相机由游戏侧
        /// ThirdPersonCamera 独占——本组件不产生任何相机写入，因此「补挂组件」这一动作本身
        /// 不会回归已出厂的相机（无双控）。
        /// </para>
        /// <para>
        /// 宿主集成若要把相机控制权交给插件，显式调用 <see cref="ClaimCameraOwnership"/>
        /// （并让游戏侧 ThirdPersonCamera 停止写相机）；交还给游戏时调用 <see cref="ReleaseCameraOwnership"/>。
        /// </para>
        /// </summary>
        public bool bDrivesCameraOutput { get; private set; }

        // 缓存的相机模式实例（按类 ID 索引）
        private readonly Dictionary<string, NarrativeCameraMode> _modeInstances = new Dictionary<string, NarrativeCameraMode>();

        // 当前插值状态
        private float _currentFOV = 90f;
        private Vector3 _currentOffset = Vector3.Zero;
        private Vector3 _currentPivot = Vector3.Zero;

        // 程序化相机震动（Flax 无 Camera.Shake，见 NarrativeCameraShake）
        private readonly NarrativeCameraShake _shake = new NarrativeCameraShake();

        public override void OnEnable()
        {
            base.OnEnable();
            // 自动查找相机。注意：持有 MainCamera 引用 ≠ 驱动它——所有写入都受
            // bDrivesCameraOutput 谓词约束（默认 false，宿主模式下游戏相机独占）。
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

            // ADS → FOV：把跟随角色当前机瞄状态与手持远程武器的 AimFOVPct 写进 NarrativeSettingsFOV，
            // 之后所有模式的 GetDesiredFOV 会乘这个占比，让 ADS 数值真正改变相机 FOV（缺陷④的落点）。
            {
                bool aiming = NarrativeAimCameraMode.IsOwnerAiming(this);
                var ranged = NarrativeAimCameraMode.FindWieldedRangedWeapon(this);
                NarrativeSettingsFOV.ApplyAimState(aiming, ranged != null ? ranged.GetAimFOV() : 1f);
            }

            // FOV 插值
            CurrentCameraMode.GetDesiredFOV(out float desiredFOV, out float fovBlendSpeed);
            _currentFOV = InterpolateTo(_currentFOV, desiredFOV, dt, fovBlendSpeed);
            // 缺陷②(c)：对引擎相机的每一处写入都过 bDrivesCameraOutput 谓词——
            // 未声明所有权（宿主装配模式的默认）时只维护插件内部插值状态，不碰 MainCamera。
            if (bDrivesCameraOutput && CameraActor != null)
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
            if (bDrivesCameraOutput && CameraActor != null)
            {
                CameraActor.Position = _currentPivot + _currentOffset;
            }

            // 弹簧臂长度
            if (SpringArmActor != null)
            {
                var armScript = SpringArmActor.GetScript<NarrativeSpringArm>();
                if (armScript != null)
                {
                    float desiredArm = CurrentCameraMode is NarrativeAimCameraMode aimMode
                        ? aimMode.GetDesiredArmLength()
                        : CurrentCameraMode.TargetArmLength;
                    armScript.TargetArmLength = InterpolateTo(armScript.TargetArmLength, desiredArm, dt, CurrentCameraMode.OffsetInterpSpeed);
                }
            }

            // 叠加程序化震动：本帧位置已在上面重算（非累积），再把噪声位移/旋转叠加到相机上。
            // 同样只在拥有相机输出时叠加，避免把震动写到游戏侧独占的 MainCamera 上。
            if (bDrivesCameraOutput && CameraActor != null && _shake.IsActive)
            {
                Vector3 shakeRot;
                Vector3 shakeOffset = _shake.Advance(dt, out shakeRot);
                CameraActor.Position += shakeOffset;
                var camTransform = CameraActor.Transform;
                camTransform.Orientation *= Quaternion.Euler(shakeRot.X, shakeRot.Y, shakeRot.Z);
                CameraActor.Transform = camTransform;
            }
        }

        /// <summary>
        /// 触发一次相机震动。对应 UE5 对话侧的 PlayCameraShake（Dialogue.cpp:718,846）。
        /// Flax 无原生相机震动，改用确定性衰减振荡叠加到 <see cref="CameraActor"/>。
        /// </summary>
        /// <param name="amplitude">振幅（场景单位）。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <param name="frequencyHz">频率（Hz）。</param>
        public void PlayCameraShake(float amplitude, float duration, float frequencyHz = 8f)
        {
            _shake.Play(amplitude, duration, frequencyHz);
        }

        /// <summary>停止相机震动（下一帧起相机回到无噪声位姿）。</summary>
        public void StopCameraShake()
        {
            _shake.Stop();
        }

        /// <summary>
        /// 声明相机输出所有权（宿主集成交还控制权的唯一入口，见 <see cref="bDrivesCameraOutput"/>）。
        /// 调用后本组件的 OnUpdate 才会写相机的 FOV/位置/震动；
        /// 宿主必须同时让游戏侧相机（如 ThirdPersonCamera）停写，否则会形成双控。
        /// </summary>
        /// <param name="targetCamera">要驱动的相机对象；省略时回落到 <see cref="FlaxEngine.Camera.MainCamera"/>。</param>
        public virtual void ClaimCameraOwnership(FlaxEngine.Camera targetCamera = null)
        {
            if (targetCamera != null)
            {
                CameraActor = targetCamera;
            }
            CameraActor ??= FlaxEngine.Camera.MainCamera;
            bDrivesCameraOutput = CameraActor != null;
        }

        /// <summary>交还相机输出所有权（默认状态）：立即停止对相机对象的一切写入。</summary>
        public virtual void ReleaseCameraOwnership()
        {
            bDrivesCameraOutput = false;
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
    /// <para>
    /// 播种方式沿用本工程既有的两处注册表范式（<c>Tales/NarrativeLogicFactory</c> 静态构造内置 +
    /// 惰性反射扫描、<c>GAS/NarrativeExecCalc</c> 内置登记）：静态构造里注册全部内置模式，
    /// 未命中时再惰性扫描已加载程序集里的 <see cref="NarrativeCameraMode"/> 具体子类自动补登记，
    /// 因此 <see cref="NarrativeCameraComponent.SetCameraMode"/> 传入的类/ID 字符串能命中并创建实例，
    /// 不再恒为「未注册的相机模式类 ID」。键做了归一化（忽略大小写/空格，剥离 narrative / camera / mode 前后缀），
    /// 故 "ThirdPerson" / "NarrativeThirdPersonCameraMode" / "third_person_camera_mode" 视作同一个模式。
    /// </para>
    /// </summary>
    public static class CameraModeRegistry
    {
        private static readonly Dictionary<string, Func<NarrativeCameraMode>> _creators = new Dictionary<string, Func<NarrativeCameraMode>>(System.StringComparer.Ordinal);
        private static bool _bScannedAssemblies;

        static CameraModeRegistry()
        {
            RegisterBuiltinModes();
        }

        /// <summary>注册一个相机模式类。</summary>
        public static void Register(string modeClassId, Func<NarrativeCameraMode> creator)
        {
            if (string.IsNullOrEmpty(modeClassId) || creator == null) return;
            RegisterAlias(creator, modeClassId);
        }

        private static void RegisterAlias(Func<NarrativeCameraMode> creator, params string[] ids)
        {
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                string key = Normalize(id);
                if (key.Length == 0) continue;
                _creators[key] = creator;
                string core = CoreKey(key);
                if (core.Length > 0) _creators[core] = creator;
            }
        }

        /// <summary>登记全部内置相机模式（对应 UE5 由 UClass 体系自动可见的内置相机类）。</summary>
        private static void RegisterBuiltinModes()
        {
            RegisterAlias(() => new NarrativeCameraMode(), "NarrativeCameraMode", "CameraMode", "Default", "Base");
            RegisterAlias(() => new NarrativeThirdPersonCameraMode(), "NarrativeThirdPersonCameraMode", "ThirdPersonCameraMode", "ThirdPerson", "TPPC", "DefaultThirdPerson");
            RegisterAlias(() => new NarrativeFirstPersonCameraMode(), "NarrativeFirstPersonCameraMode", "FirstPersonCameraMode", "FirstPerson", "FPPC");
            RegisterAlias(() => new NarrativeAimCameraMode(), "NarrativeAimCameraMode", "AimCameraMode", "Aim", "ADS", "AimingCameraMode");
        }

        /// <summary>创建模式实例。返回 null 表示未注册。</summary>
        public static NarrativeCameraMode CreateMode(string modeClassId)
        {
            if (string.IsNullOrEmpty(modeClassId)) return null;

            var creator = ResolveCreator(modeClassId);
            if (creator != null) return creator();

            // 反射兜底：扫描已加载程序集里 NarrativeCameraMode 的具体子类（游戏自定义模式无需手写 Register）
            EnsureAssemblyScan();
            creator = ResolveCreator(modeClassId);
            if (creator != null) return creator();

            NarrativeLog.LogWarning($"[Camera] 未注册的相机模式类 ID: {modeClassId}");
            return null;
        }

        /// <summary>是否已注册（编辑器校验/诊断用）。</summary>
        public static bool IsRegistered(string modeClassId)
        {
            return ResolveCreator(modeClassId) != null;
        }

        private static Func<NarrativeCameraMode> ResolveCreator(string modeClassId)
        {
            string key = Normalize(modeClassId);
            if (key.Length > 0 && _creators.TryGetValue(key, out var c)) return c;
            string core = CoreKey(key);
            if (core.Length > 0 && _creators.TryGetValue(core, out var c2)) return c2;
            return null;
        }

        private static string Normalize(string value)
        {
            return value == null ? "" : value.Trim().Replace(" ", "").ToLowerInvariant();
        }

        /// <summary>剥离 narrative / camera / mode 前后缀得到核心键，兼容多种写法。</summary>
        private static string CoreKey(string normalized)
        {
            string s = normalized;
            const string prefix = "narrative";
            if (s.StartsWith(prefix, System.StringComparison.Ordinal)) s = s.Substring(prefix.Length);
            if (s.EndsWith("cameramode", System.StringComparison.Ordinal)) s = s.Substring(0, s.Length - "cameramode".Length);
            else if (s.EndsWith("camera", System.StringComparison.Ordinal)) s = s.Substring(0, s.Length - "camera".Length);
            else if (s.EndsWith("mode", System.StringComparison.Ordinal)) s = s.Substring(0, s.Length - "mode".Length);
            return s;
        }

        private static void EnsureAssemblyScan()
        {
            if (_bScannedAssemblies) return;
            _bScannedAssemblies = true;

            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue; // 动态/资源程序集取不到类型时跳过
                }

                foreach (var type in types)
                {
                    if (type == null || !type.IsClass || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(NarrativeCameraMode).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(System.Type.EmptyTypes) == null) continue;

                    var localType = type;
                    RegisterAlias(() => (NarrativeCameraMode)System.Activator.CreateInstance(localType), localType.Name);
                }
            }
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
