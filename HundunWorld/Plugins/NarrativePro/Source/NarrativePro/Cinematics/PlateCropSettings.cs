using System;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 取景遮幅模式。对应 UE5 引擎 <c>ECineCameraCropMode</c>（CineCameraComponent.h），
    /// <see cref="PlateCropSettings"/> 的字段之一。Overscan/Off 表示不裁切。
    /// </summary>
    public enum ECineCameraCropMode
    {
        /// <summary>不裁切（插件回退为默认银幕 2.35，保持既有对话黑边行为不减弱）。</summary>
        Off,
        /// <summary>默认银幕 2.35:1。</summary>
        Default,
        /// <summary>DCPsi 1.90:1。</summary>
        DCPsi,
        /// <summary>Academy Flat 1.85:1。</summary>
        AcademyFlat,
        /// <summary>Academy 1.37:1。</summary>
        Academy,
        /// <summary>自定义：用 <see cref="PlateCropSettings.AspectRatio"/>。</summary>
        Custom,
        /// <summary>过扫描：显示全画幅，不裁切。</summary>
        Overscan,
        /// <summary>手动：直接按 <see cref="PlateCropSettings.CropAmount"/> 裁切。</summary>
        Manual,
    }

    /// <summary>
    /// 电影遮幅/裁切设置。对应 UE5 引擎 <c>FPlateCropSettings</c>（CineCameraSettings.h），
    /// 在 UE5 对话镜头上以 <c>UNarrativeDialogueSequence::CropSettings</c> 携带
    /// （NarrativeDialogueSequence.h:131），并在 BeginPlaySequence 里赋给生成出的 CineCameraComponent
    /// （NarrativeDialogueSequence.cpp:209 <c>CinecamComp-&gt;CropSettings = CropSettings;</c>），
    /// 构造函数默认宽高比 2.35（NarrativeDialogueSequence.cpp:27）。
    /// <para>
    /// Flax 1.12 相机无 letterbox/遮幅 API（已在 FlaxEngine.CSharp.xml 核实），
    /// 因此本端口把「遮幅」落到屏幕空间黑边覆盖层 <see cref="NarrativeCinematicBars"/> 上：
    /// 由 <see cref="ComputeBarHeightPct"/> 把模式/宽高比/裁切量换算成上下黑条高度占屏比。
    /// 属性均带 setter，保证 JSON 资产（大小写不敏感绑定）可写入这些字段。
    /// </para>
    /// </summary>
    [Serializable]
    public class PlateCropSettings
    {
        /// <summary>裁切模式。对应 UE5 <c>CropMode</c>。</summary>
        public ECineCameraCropMode CropMode { get; set; } = ECineCameraCropMode.Default;

        /// <summary>自定义宽高比（仅 <see cref="ECineCameraCropMode.Custom"/> 生效）。对应 UE5 <c>AspectRatio</c>，默认 2.35。</summary>
        public float AspectRatio { get; set; } = 2.35f;

        /// <summary>手动裁切量（0~1，画幅高度占比，仅 <see cref="ECineCameraCropMode.Manual"/> 生效）。对应 UE5 <c>CropAmount</c>。</summary>
        public float CropAmount { get; set; } = 0f;

        /// <summary>
        /// 取当前模式生效的目标银幕宽高比；返回 &lt;=0 表示不裁切（Off/Overscan/Manual 无比例语义）。
        /// </summary>
        public float GetEffectiveAspectRatio()
        {
            switch (CropMode)
            {
                case ECineCameraCropMode.Default: return 2.35f;
                case ECineCameraCropMode.DCPsi: return 1.90f;
                case ECineCameraCropMode.AcademyFlat: return 1.85f;
                case ECineCameraCropMode.Academy: return 1.37f;
                case ECineCameraCropMode.Custom: return AspectRatio > 0f ? AspectRatio : 0f;
                case ECineCameraCropMode.Off:
                case ECineCameraCropMode.Overscan:
                case ECineCameraCropMode.Manual:
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 换算上下黑条各自的高度占屏比（0~0.45）。
        /// <para>
        /// 银幕比 target 比屏幕宽时，黑边占比 = 0.5 * (1 - screenAspect / target)。
        /// 例：16:9 屏幕 + 2.35 银幕 → 0.5*(1-1.7778/2.35) ≈ 0.122，与端口此前固定值 0.12 一致，
        /// 保证默认数据不改变现有黑边表现。Manual 模式直接按 CropAmount 对半分到上下。
        /// Off/Overscan 回退 2.35 银幕（调用方已明确请求黑边，不给无裁切留空档）。
        /// </para>
        /// </summary>
        public float ComputeBarHeightPct(float screenAspect)
        {
            if (CropMode == ECineCameraCropMode.Manual)
            {
                float amount = Math.Min(Math.Max(CropAmount, 0f), 0.9f);
                return Math.Min(amount * 0.5f, 0.45f);
            }

            float target = GetEffectiveAspectRatio();
            if (target <= 0f || screenAspect <= 0f)
            {
                // Off/Overscan 或尺寸未就绪：沿用默认银幕观感（等价旧固定值）
                target = 2.35f;
            }

            float ratio = screenAspect / target;
            float pct = ratio >= 1f ? 0f : 0.5f * (1f - ratio);
            if (pct < 0f) pct = 0f;
            if (pct > 0.45f) pct = 0.45f;
            return pct;
        }
    }
}
