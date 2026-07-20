using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Layout;
using System;

namespace HundunWorld.Game.UI.StyleSystem
{
    /// <summary>
    /// 中国古典美学风格主题系统
    /// 基于《浮云十八声》的设计理念
    /// </summary>
    public static class ChineseClassicalTheme
    {
        #region 色彩方案

        /// <summary>
        /// 主色调 - 黛青色
        /// </summary>
        public static readonly Color PrimaryColor = new Color(45f / 255f, 85f / 255f, 105f / 255f, 1f);

        /// <summary>
        /// 辅助色 - 古典金
        /// </summary>
        public static readonly Color SecondaryColor = new Color(205f / 255f, 165f / 255f, 85f / 255f, 1f);

        public static Color SecondaryColorWithAlpha(float alpha)
        {
            return new Color(205f / 255f, 165f / 255f, 85f / 255f, alpha);
        }

        /// <summary>
        /// 背景色 - 雅致灰白
        /// </summary>
        public static readonly Color BackgroundColor = new Color(35f / 255f, 40f / 255f, 45f / 255f, 0.95f);

        /// <summary>
        /// 面板色 - 青石色
        /// </summary>
        public static readonly Color PanelColor = new Color(55f / 255f, 65f / 255f, 75f / 255f, 0.9f);

        /// <summary>
        /// 文字色 - 清雅白
        /// </summary>
        public static readonly Color TextColor = new Color(245f / 255f, 245f / 255f, 245f / 255f, 1f);

        /// <summary>
        /// 强调色 - 朱砂红
        /// </summary>
        public static readonly Color AccentColor = new Color(185f / 255f, 65f / 255f, 65f / 255f, 1f);

        /// <summary>
        /// 成功色 - 竹叶青
        /// </summary>
        public static readonly Color SuccessColor = new Color(85f / 255f, 165f / 255f, 85f / 255f, 1f);

        /// <summary>
        /// 输入框色 - 深青色
        /// </summary>
        public static readonly Color InputColor = new Color(65f / 255f, 75f / 255f, 85f / 255f, 1f);

        /// <summary>
        /// 边框色 - 古典金边框
        /// </summary>
        public static readonly Color BorderColor = new Color(205f / 255f, 165f / 255f, 85f / 255f, 0.6f);

        /// <summary>
        /// 输入框背景色 - 更深的青色
        /// </summary>
        public static readonly Color InputBackgroundColor = new Color(45f / 255f, 55f / 255f, 65f / 255f, 0.9f);

        #endregion

        #region 魔兽世界风格 - 石质与金属颜色

        /// <summary>深色石质背景</summary>
        public static readonly Color DarkStoneBackgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.97f);

        /// <summary>石质面板色</summary>
        public static readonly Color DarkStonePanelColor = new Color(0.12f, 0.13f, 0.16f, 0.92f);

        /// <summary>面板高亮层（金属高光底色）</summary>
        public static readonly Color DarkStonePanelHighlight = new Color(0.18f, 0.18f, 0.22f, 0.9f);

        /// <summary>深凹面板色</summary>
        public static readonly Color DarkStoneInsetColor = new Color(0.04f, 0.05f, 0.07f, 0.98f);

        #endregion

        #region 魔兽世界风格 - 金属渐变与装饰色

        /// <summary>金属边框色（暗铜）</summary>
        public static readonly Color MetalBorderColor = new Color(0.38f, 0.32f, 0.22f, 0.95f);

        /// <summary>金属边框高亮色（亮金）</summary>
        public static readonly Color MetalBorderHighlightColor = new Color(0.85f, 0.72f, 0.42f, 1.0f);

        /// <summary>金属边框柔光色（用于高光内部）</summary>
        public static readonly Color MetalBorderSoftHighlightColor = new Color(1.0f, 0.92f, 0.62f, 0.9f);

        /// <summary>金属深暗面（暗铜深色阴影）</summary>
        public static readonly Color MetalDarkShade = new Color(0.22f, 0.18f, 0.12f, 0.98f);

        /// <summary>金属亮面（高反射）</summary>
        public static readonly Color MetalBrightShade = new Color(0.65f, 0.55f, 0.32f, 1.0f);

        /// <summary>符文装饰色（暗红铜，用于复古装饰图案）</summary>
        public static readonly Color RuneDecorationColor = new Color(0.55f, 0.35f, 0.18f, 0.55f);

        /// <summary>暗纹背景色（用于面板内的纹理底色）</summary>
        public static readonly Color DarkPatternColor = new Color(0.08f, 0.08f, 0.1f, 0.95f);

        #endregion

        #region 品质色阶 — 统一为设计规范 5 阶（ui-design-guidelines.md §1.3 / --ink-quality-*）

        /// <summary>普通品质（灰白 #8A8275）</summary>
        public static readonly Color QualityCommon = UIStyleTokens.QualityCommon;

        /// <summary>优秀品质（青绿 #6B8E5A）</summary>
        public static readonly Color QualityUncommon = UIStyleTokens.QualityUncommon;

        /// <summary>稀有品质（蓝紫 #4A7EA8）</summary>
        public static readonly Color QualityRare = UIStyleTokens.QualityRare;

        /// <summary>史诗品质（紫 #8B5E9E）</summary>
        public static readonly Color QualityEpic = UIStyleTokens.QualityEpic;

        /// <summary>传说品质（赤金 #C8A858，复用鎏金主色）</summary>
        public static readonly Color QualityLegendary = UIStyleTokens.QualityLegendary;

        /// <summary>神器品质（血色 #B85450，设计品质色阶之外的第 6 档，沿用危险色）</summary>
        public static readonly Color QualityArtifact = UIStyleTokens.BloodPrimary;

        /// <summary>
        /// 根据品质等级获取颜色（0=灰, 1=绿, 2=蓝, 3=紫, 4=橙, 5=红）
        /// </summary>
        public static Color GetQualityColor(int quality)
        {
            return quality switch
            {
                0 => QualityCommon,
                1 => QualityUncommon,
                2 => QualityRare,
                3 => QualityEpic,
                4 => QualityLegendary,
                5 => QualityArtifact,
                _ => QualityCommon
            };
        }

        /// <summary>
        /// 获取品质发光颜色（低透明度用于外发光效果）
        /// </summary>
        public static Color GetQualityGlowColor(int quality)
        {
            var baseColor = GetQualityColor(quality);
            return new Color(baseColor.R, baseColor.G, baseColor.B, 0.35f);
        }

        /// <summary>
        /// 获取品质文字颜色（略暗，保证可读性）
        /// </summary>
        public static Color GetQualityTextColor(int quality)
        {
            var c = GetQualityColor(quality);
            return new Color(c.R * 0.92f + 0.08f, c.G * 0.92f + 0.08f, c.B * 0.92f + 0.08f, 1f);
        }

        #endregion

        #region 五行元素颜色 — 统一为设计规范五行色（ui-design-guidelines.md §1.4 / --ink-element-*）

        /// <summary>金元素色（白 #D4C4A0）</summary>
        public static readonly Color ElementMetalColor = UIStyleTokens.ElementMetal;

        /// <summary>木元素色（青 #6B8E5A）</summary>
        public static readonly Color ElementWoodColor = UIStyleTokens.ElementWood;

        /// <summary>水元素色（黑 #4A6E8A）</summary>
        public static readonly Color ElementWaterColor = UIStyleTokens.ElementWater;

        /// <summary>火元素色（红 #B85638）</summary>
        public static readonly Color ElementFireColor = UIStyleTokens.ElementFire;

        /// <summary>土元素色（黄 #8A7B5A）</summary>
        public static readonly Color ElementEarthColor = UIStyleTokens.ElementEarth;

        #endregion

        #region 魔兽世界风格 - 统一边框/分隔线/阴影色

        /// <summary>魔兽风格金色标题</summary>
        public static readonly Color WowTitleColor = new Color(1.0f, 0.86f, 0.36f, 1.0f);

        /// <summary>金色标题外发光</summary>
        public static readonly Color WowTitleGlowColor = new Color(1.0f, 0.72f, 0.18f, 0.35f);

        /// <summary>魔兽风格属性文本（灰白色）</summary>
        public static readonly Color WowAttributeTextColor = new Color(0.88f, 0.86f, 0.78f, 1.0f);

        /// <summary>次级属性文本色</summary>
        public static readonly Color WowSubTextColor = new Color(0.62f, 0.60f, 0.56f, 1.0f);

        /// <summary>提示文字色（更淡）</summary>
        public static readonly Color WowHintTextColor = new Color(0.48f, 0.46f, 0.42f, 1.0f);

        /// <summary>数字文字色（亮金）</summary>
        public static readonly Color WowNumberTextColor = new Color(1.0f, 0.92f, 0.62f, 1.0f);

        /// <summary>分组标题色（暗金）</summary>
        public static readonly Color WowSectionHeaderColor = new Color(0.78f, 0.62f, 0.36f, 1.0f);

        /// <summary>分割线色</summary>
        public static readonly Color WowDividerColor = new Color(0.30f, 0.26f, 0.18f, 0.9f);

        /// <summary>内描边线色</summary>
        public static readonly Color WowInnerBorderColor = new Color(0.22f, 0.18f, 0.12f, 0.9f);

        /// <summary>发光阴影色（暗）</summary>
        public static readonly Color WowShadowDark = new Color(0.0f, 0.0f, 0.0f, 0.65f);

        /// <summary>发光阴影色（淡）</summary>
        public static readonly Color WowShadowSoft = new Color(0.0f, 0.0f, 0.0f, 0.35f);

        #endregion

        #region 黄金比例设计系统

        public static class GoldenRatioLayout
        {
            public static Float2 CalculateLoginPanelSize(float baseWidth = 480)
            {
                var height = baseWidth / ResponsiveLayoutCalculator.GoldenRatio;
                return ResponsiveLayoutCalculator.EnsureSafeSize(new Float2(baseWidth, height));
            }

            public static Float2 CalculateRegisterPanelSize(float baseWidth = 680)
            {
                var height = baseWidth * 0.52f;
                return ResponsiveLayoutCalculator.EnsureSafeSize(new Float2(baseWidth, height));
            }

            public static Float2 CalculateInputSize(float containerWidth)
            {
                var width = containerWidth * 0.65f;
                var height = 36f;
                return new Float2(width, height);
            }

            public static Float2 CalculateButtonSize(ButtonType buttonType)
            {
                return buttonType switch
                {
                    ButtonType.Primary => new Float2(120, 40),
                    ButtonType.Secondary => new Float2(100, 36),
                    ButtonType.Small => new Float2(80, 32),
                    ButtonType.Large => new Float2(160, 48),
                    _ => new Float2(120, 40)
                };
            }

            public static float CalculateSpacing(SpacingType elementType)
            {
                return elementType switch
                {
                    SpacingType.Small => 8f,
                    SpacingType.Medium => 16f,
                    SpacingType.Large => 24f,
                    SpacingType.ExtraLarge => 32f,
                    SpacingType.Big => 64f,
                    _ => 16f
                };
            }
        }

        #endregion

        #region 视觉层次设计

        // 视觉层次样式统一映射到设计 Token（出处：game-ui-system/colors_and_type.css --ink-* 系列，
        // ds-btn §4.1 变体规范）。仅改视觉数值，层次语义保持不变。

        public static void ApplyVisualHierarchy(Control control, VisualHierarchy hierarchy)
        {
            switch (hierarchy)
            {
                case VisualHierarchy.Primary:
                    ApplyPrimaryStyle(control);
                    break;
                case VisualHierarchy.Secondary:
                    ApplySecondaryStyle(control);
                    break;
                case VisualHierarchy.Tertiary:
                    ApplyTertiaryStyle(control);
                    break;
                case VisualHierarchy.Auxiliary:
                    ApplyAuxiliaryStyle(control);
                    break;
            }
        }

        private static void ApplyPrimaryStyle(Control control)
        {
            if (control is Button button)
            {
                // ds-btn--brand/primary：鎏金底 + 墨黑反白字（--ink-gold-primary / --ink-text-inverse）
                button.BackgroundColor = UIStyleTokens.GoldPrimary;
                button.TextColor = UIStyleTokens.TextInverse;
            }
            else if (control is Label label)
            {
                label.TextColor = UIStyleTokens.TextGold; // --ink-text-gold
            }
        }

        private static void ApplySecondaryStyle(Control control)
        {
            if (control is Button button)
            {
                // 次操作：水墨青底 + 反白字（--ink-jade-primary / --ink-text-inverse）
                button.BackgroundColor = UIStyleTokens.JadePrimary;
                button.TextColor = UIStyleTokens.TextInverse;
            }
            else if (control is Label label)
            {
                label.TextColor = UIStyleTokens.JadePrimary;
            }
        }

        private static void ApplyTertiaryStyle(Control control)
        {
            if (control is TextBox textBox)
            {
                // ds-input 默认态：墨水深背景 + 主文本（--ink-bg-ink / --ink-text-primary）
                textBox.BackgroundColor = UIStyleTokens.BgInk;
                textBox.TextColor = UIStyleTokens.TextPrimary;
            }
            else if (control is Panel panel)
            {
                panel.BackgroundColor = UIStyleTokens.BgInk;
            }
        }

        private static void ApplyAuxiliaryStyle(Control control)
        {
            if (control is Label label)
            {
                // 辅助文本：宣纸白 0.7 透明度（--ink-text-primary 派生）
                label.TextColor = new Color(UIStyleTokens.TextPrimary.R, UIStyleTokens.TextPrimary.G, UIStyleTokens.TextPrimary.B, 0.7f);
            }
        }

        #endregion

        #region 中式装饰元素

        public static void ApplyChineseBorder(Panel panel, ChineseBorderStyle borderStyle)
        {
            // 边框风格仅改背景层次，统一映射设计 Token 墨色背景（--ink-bg-*）
            switch (borderStyle)
            {
                case ChineseBorderStyle.Elegant:
                    panel.BackgroundColor = UIStyleTokens.BgInk;
                    break;
                case ChineseBorderStyle.Traditional:
                    panel.BackgroundColor = UIStyleTokens.BgVoid;
                    break;
                case ChineseBorderStyle.Ornate:
                    panel.BackgroundColor = UIStyleTokens.BgElevated;
                    break;
            }
        }

        public static void ApplyChineseBorder(ContainerControl panel, ChineseBorderStyle borderStyle)
        {
            switch (borderStyle)
            {
                case ChineseBorderStyle.Elegant:
                    panel.BackgroundColor = UIStyleTokens.BgInk;
                    break;
                case ChineseBorderStyle.Traditional:
                    panel.BackgroundColor = UIStyleTokens.BgVoid;
                    break;
                case ChineseBorderStyle.Ornate:
                    panel.BackgroundColor = UIStyleTokens.BgElevated;
                    break;
            }
        }

        public static void ApplyWowPanelStyle(Panel panel, bool isInset = false)
        {
            if (panel == null) return;
            panel.BackgroundColor = isInset ? DarkStoneInsetColor : DarkStonePanelColor;
        }

        #endregion
    }


}
