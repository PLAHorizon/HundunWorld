namespace NarrativePro.Camera
{
    /// <summary>
    /// FOV 设置节点。适配 UE5 UNarrativeSettingsFOVCameraNode。
    /// UE 中作为 CameraNode 使用，Flax 无对应 API，简化为静态配置。
    /// 允许通过 PlayerController 的 GetDesiredFOV 控制相机 FOV。
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
    }
}
