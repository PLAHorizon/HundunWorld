using System;
using FlaxEngine;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// Narrative SpinBox 占位。对应 UE5 UNarrativeSpinBox（继承 USpinBox）。
    /// 会从插件设置的 UIPrimary 和 UIInvert 颜色中拉取样式。
    ///
    /// 移植简化点：
    /// 1. Flax 完全没有 UMG / Slate 控件系统。USpinBox 是 UE5 Slate 数值微调控件，
    ///    这里以 [Serializable] plain class 占位，保留 UE5 的类名、字段定义、方法签名。
    /// 2. UE5 中 RebuildWidget 返回 TSharedRef&lt;SWidget&gt; 并通过 FSpinBoxStyle 配置多个画刷的颜色；
    ///    Flax 中以颜色字段占位，UI 渲染需用 Flax UIControl（如自定义 SpinBox）重新实现。
    /// 3. 颜色值从 NarrativeUIDeveloperSettings 拉取（与 UE5 行为一致）。
    /// </summary>
    [Serializable]
    public class NarrativeSpinBox
    {
        /// <summary>前景色（对应 UE5 SetForegroundColor）。</summary>
        public Color ForegroundColor = Color.White;

        /// <summary>背景画刷颜色（对应 UE5 BackgroundBrush.TintColor）。</summary>
        public Color BackgroundColor = Color.Black;

        /// <summary>激活时背景画刷颜色（对应 UE5 ActiveBackgroundBrush.TintColor）。</summary>
        public Color ActiveBackgroundColor = Color.Black;

        /// <summary>悬停时背景画刷颜色（对应 UE5 HoveredBackgroundBrush.TintColor）。</summary>
        public Color HoveredBackgroundColor = Color.Black;

        /// <summary>激活时填充画刷颜色（对应 UE5 ActiveFillBrush.TintColor）。</summary>
        public Color ActiveFillColor = Color.Black;

        /// <summary>悬停时填充画刷颜色（对应 UE5 HoveredFillBrush.TintColor）。</summary>
        public Color HoveredFillColor = Color.Black;

        /// <summary>非激活时填充画刷颜色（对应 UE5 InactiveFillBrush.TintColor）。</summary>
        public Color InactiveFillColor = Color.Black;

        /// <summary>当前数值。对应 UE5 USpinBox 的 Value。</summary>
        public float Value = 0f;

        /// <summary>最小值。对应 UE5 USpinBox 的 MinValue。</summary>
        public float MinValue = 0f;

        /// <summary>最大值。对应 UE5 USpinBox 的 MaxValue。</summary>
        public float MaxValue = 100f;

        /// <summary>构造函数。</summary>
        public NarrativeSpinBox()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax SpinBox 控件重新实现构造逻辑。
        }

        /// <summary>
        /// 重建控件。对应 UE5 RebuildWidget（UWidget 接口）。
        /// UE5 中从 NarrativeUIDeveloperSettings 拉取主色与反色，应用到 FSpinBoxStyle 的各个画刷。
        /// </summary>
        public virtual void RebuildWidget()
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax UIControl（自定义 SpinBox）重新实现控件构建。
            var settings = NarrativePro.CommonUI.NarrativeUIDeveloperSettings.Instance;
            if (settings != null)
            {
                SetForegroundColor(settings.UIPrimaryColor);

                // 应用反色到背景与填充画刷（与 UE5 行为一致）。
                BackgroundColor = settings.UIInvertColor;
                ActiveBackgroundColor = settings.UIInvertAccentColor;
                HoveredBackgroundColor = settings.UIInvertColor;
                ActiveFillColor = settings.UIInvertAccentColor;
                HoveredFillColor = settings.UIInvertColor;
                InactiveFillColor = settings.UIInvertAccentColor;
            }
        }

        /// <summary>设置前景色。对应 UE5 SetForegroundColor。</summary>
        public void SetForegroundColor(Color color)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，前景色应用需用 Flax UIControl 的 Color 属性重新实现。
            ForegroundColor = color;
        }

        /// <summary>设置当前数值（钳制到 [MinValue, MaxValue]）。</summary>
        public void SetValue(float newValue)
        {
            // Flax-不兼容: UE5 的 UMG 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 UMG，需用 Flax SpinBox 控件重新实现。
            Value = Math.Max(MinValue, Math.Min(MaxValue, newValue));
        }
    }
}
