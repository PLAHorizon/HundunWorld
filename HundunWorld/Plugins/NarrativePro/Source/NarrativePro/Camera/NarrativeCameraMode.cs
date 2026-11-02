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

        /// <summary>该模式是否要求第一人称渲染。基类为 false，第一人称模式覆盖为 true。</summary>
        public virtual bool WantsFirstPersonRender() => false;

        /// <summary>获取期望的 FOV 和插值速度。可覆盖以动态生成。</summary>
        public virtual void GetDesiredFOV(out float fov, out float fovBlendSpeed)
        {
            // 消费 NarrativeSettingsFOV：ADS 开镜时 FieldOfViewPct<1 会收窄 FOV（blend 路径的真实落点）。
            // 若本模式设了自定义 DefaultFOV 就以它为基础乘占比；否则用设置里的基准 FOV×占比。
            fov = DefaultFOV > 0f
                ? DefaultFOV * NarrativeSettingsFOV.FieldOfViewPct
                : NarrativeSettingsFOV.GetDesiredFOV();
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

    /// <summary>
    /// 第三人称环绕镜头模式（内置）。对应 UE5 常见的过肩/跟随相机类。
    /// 由 <see cref="CameraModeRegistry"/> 静态播种注册，使 <see cref="NarrativeCameraComponent.SetCameraMode"/>
    /// 能用类/ID 字符串命中并驱动 FOV/偏移/臂长插值（blend 路径真正生效）。
    /// </summary>
    public class NarrativeThirdPersonCameraMode : NarrativeCameraMode
    {
        public NarrativeThirdPersonCameraMode()
        {
            DefaultFOV = 90f;
            DefaultFOVBlendSpeed = 8f;
            TargetArmLength = 300f;
            OffsetInterpSpeed = 12f;
            PivotInterpSpeed = 12f;
            // 角色后上方典型过肩位（沿用工程单位口径）
            Offset = new Vector3(0f, -40f, 60f);
        }
    }

    /// <summary>
    /// 第一人称镜头模式（内置）。基准 FOV 取自 <see cref="NarrativeSettingsFOV"/>，
    /// 开镜（ADS）时随 <c>FieldOfViewPct</c> 收窄。
    /// </summary>
    public class NarrativeFirstPersonCameraMode : NarrativeCameraMode
    {
        public NarrativeFirstPersonCameraMode()
        {
            // DefaultFOV<=0 → 基类 GetDesiredFOV 走 NarrativeSettingsFOV.GetDesiredFOV()（受 ADS 占比影响）
            DefaultFOV = 0f;
            DefaultFOVBlendSpeed = 12f;
            TargetArmLength = 0f;
            OffsetInterpSpeed = 20f;
            PivotInterpSpeed = 20f;
            Offset = new Vector3(0f, 0f, 60f);
        }

        public override bool WantsFirstPersonRender() => true;
    }

    /// <summary>
    /// 机瞄（ADS）镜头模式（内置）。对应 UE5 远程武器开镜相机节点：
    /// 读取跟随角色身上手持远程武器的 <c>AimFOVPct</c>，把 FOV 收窄，并把弹簧臂拉近，
    /// 从而真正消费 <see cref="NarrativeSettingsFOV"/> 与 <see cref="NarrativePro.Items.RangedWeaponItem.GetAimFOV"/>。
    /// </summary>
    public class NarrativeAimCameraMode : NarrativeCameraMode
    {
        /// <summary>非瞄准时（腰射）的臂长。</summary>
        public float HipArmLength { get; set; } = 260f;

        /// <summary>瞄准时收紧到的臂长。</summary>
        public float AimedArmLength { get; set; } = 8f;

        public NarrativeAimCameraMode()
        {
            DefaultFOV = 0f; // 以 NarrativeSettingsFOV 为基准
            DefaultFOVBlendSpeed = 14f;
            OffsetInterpSpeed = 16f;
            PivotInterpSpeed = 16f;
            Offset = new Vector3(0f, -8f, 12f);
            TargetArmLength = HipArmLength;
        }

        /// <summary>跟随目标角色当前是否处于机瞄状态（读 ASC 的 <c>Narrative.State.Weapon.IsAiming</c>）。</summary>
        public static bool IsOwnerAiming(NarrativeCameraComponent camera)
        {
            var target = camera?.TargetActor;
            if (target == null) return false;
            var character = target.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
            var asc = character != null ? character.GetAbilitySystemComponent() : null;
            if (asc == null) return false;
            return asc.HasMatchingGameplayTag(NarrativePro.Core.NarrativeGameplayTags.State_Weapon_IsAiming);
        }

        /// <summary>取目标角色手持的远程武器（用于读 AimFOVPct）。</summary>
        public static NarrativePro.Items.RangedWeaponItem FindWieldedRangedWeapon(NarrativeCameraComponent camera)
        {
            var target = camera?.TargetActor;
            if (target == null) return null;
            var character = target.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>();
            if (character == null) return null;
            var weapons = character.GetWieldedWeapons();
            if (weapons == null) return null;
            for (int i = 0; i < weapons.Count; i++)
            {
                if (weapons[i] is NarrativePro.Items.RangedWeaponItem ranged && ranged.IsWielded())
                {
                    return ranged;
                }
            }
            return null;
        }

        public override void GetDesiredFOV(out float fov, out float fovBlendSpeed)
        {
            // ADS：收窄到武器 AimFOVPct × 基准 FOV；未开镜：回落到基类（占比 1.0）。
            var ranged = FindWieldedRangedWeapon(OwningCamera);
            if (IsOwnerAiming(OwningCamera) && ranged != null)
            {
                float baseFov = NarrativeSettingsFOV.DefaultFOV > 0f ? NarrativeSettingsFOV.DefaultFOV : 90f;
                fov = baseFov * ranged.GetAimFOV();
            }
            else
            {
                base.GetDesiredFOV(out fov, out _);
            }
            fovBlendSpeed = DefaultFOVBlendSpeed;
        }

        public override Vector3 GetCameraDesiredOffset()
        {
            // 开镜时机位贴脸，腰射时保持过肩偏移
            return IsOwnerAiming(OwningCamera) ? Vector3.Zero : base.GetCameraDesiredOffset();
        }

        /// <summary>开镜时把臂长收到近眼位。</summary>
        public float GetDesiredArmLength()
        {
            return IsOwnerAiming(OwningCamera) ? AimedArmLength : HipArmLength;
        }
    }
}
