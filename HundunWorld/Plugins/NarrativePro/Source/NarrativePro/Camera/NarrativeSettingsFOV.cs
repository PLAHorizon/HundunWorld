namespace NarrativePro.Camera
{
    /// <summary>
    /// FOV 设置节点。适配 UE5 UNarrativeSettingsFOVCameraNode。
    /// UE 中作为 CameraNode 使用，Flax 无对应 API，简化为静态配置。
    /// 允许通过 PlayerController 的 GetDesiredFOV 控制相机 FOV。
    /// <para>
    /// 移植核对（NarrativeSettingsFOV.h/.cpp 全量已读）：UE5 的
    /// <c>UNarrativeSettingsFOVCameraNode</c> 本体是死壳——<c>FieldOfViewPct</c> UPROPERTY、
    /// 构造函数赋值与 <c>FNarrativeFOVCameraNodeEvaluator::OnRun</c> 里的
    /// <c>CameraPose.SetFieldOfView(PC-&gt;GetDesiredFOV())</c> 在 UE5 源码中全部被注释掉，
    /// 该节点类不携带任何生效行为，属于 UE 相机系统的接线壳（plumbing）。
    /// 其意图（「FOV 由 PlayerController 的 DesiredFOV 驱动而非相机模式混合」）由本静态类的
    /// <see cref="GetDesiredFOV"/> + <see cref="ApplyAimState"/> 真实落地，
    /// 无需也无法再移植一个「节点」类型——故此类即该节点端口的最终形态。
    /// </para>
    /// </summary>
    public static class NarrativeSettingsFOV
    {
        /// <summary>FOV 占比（相对于玩家游戏用户设置中的 FOV 度数）</summary>
        public static float FieldOfViewPct { get; set; } = 1.0f;

        /// <summary>默认 FOV（度）</summary>
        public static float DefaultFOV { get; set; } = 90f;

        /// <summary>获取应用百分比后的目标 FOV。</summary>
        public static float GetDesiredFOV()
        {
            return DefaultFOV * FieldOfViewPct;
        }

        /// <summary>设置 FOV 百分比。</summary>
        public static void SetFOVPct(float pct)
        {
            FieldOfViewPct = pct;
        }

        /// <summary>设置默认 FOV（度）。</summary>
        public static void SetDefaultFOV(float fov)
        {
            DefaultFOV = fov;
        }

        /// <summary>
        /// 应用瞄准（ADS）状态到 FOV 占比。对应 UE5 武器/ADS 相机节点：
        /// 开镜时把 FOV 占比收窄到武器的 AimFOVPct（UE5 ClampMin=0.1/ClampMax=1.0），收镜恢复 1.0。
        /// <para>
        /// 这是 <see cref="NarrativeCameraMode.GetDesiredFOV"/> / <see cref="GetDesiredFOV"/> 的实际写入方，
        /// 让 <see cref="Items.RangedWeaponItem.GetAimFOV"/> 的 ADS 数值真正改变相机 FOV（此前该设置 0 引用）。
        /// </para>
        /// </summary>
        /// <param name="bAiming">当前是否处于机瞄状态。</param>
        /// <param name="aimFOVPct">武器瞄准 FOV 占比（来自 <c>RangedWeaponItem.GetAimFOV()</c>）。</param>
        public static void ApplyAimState(bool bAiming, float aimFOVPct)
        {
            if (!bAiming)
            {
                FieldOfViewPct = 1.0f;
                return;
            }

            // 与 UE5 口径一致：夹在 [0.1, 1.0]，越小说明放大越多
            float pct = aimFOVPct;
            if (pct <= 0f) pct = 1.0f;
            if (pct < 0.1f) pct = 0.1f;
            if (pct > 1.0f) pct = 1.0f;
            FieldOfViewPct = pct;
        }
    }
}
