using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    /// <summary>
    /// MetaHuman编辑器UI样式常量
    /// 统一管理颜色、尺寸、字体等样式定义
    /// </summary>
    public static class MetaHumanStyles
    {
        #region 颜色定义

        public static class Colors
        {
            public static readonly Color BackgroundDark = new Color(0.08f, 0.08f, 0.10f, 1.0f);
            public static readonly Color BackgroundMedium = new Color(0.12f, 0.12f, 0.14f, 1.0f);
            public static readonly Color BackgroundLight = new Color(0.16f, 0.16f, 0.18f, 1.0f);
            public static readonly Color BackgroundElevated = new Color(0.18f, 0.18f, 0.20f, 1.0f);

            public static readonly Color Primary = new Color(0.25f, 0.50f, 0.75f, 1.0f);
            public static readonly Color PrimaryHover = new Color(0.30f, 0.58f, 0.85f, 1.0f);
            public static readonly Color PrimaryPressed = new Color(0.20f, 0.42f, 0.65f, 1.0f);
            
            public static readonly Color Accent = new Color(0.40f, 0.65f, 0.45f, 1.0f);
            public static readonly Color AccentHover = new Color(0.45f, 0.72f, 0.50f, 1.0f);
            public static readonly Color AccentPressed = new Color(0.35f, 0.55f, 0.40f, 1.0f);

            public static readonly Color Success = new Color(0.30f, 0.65f, 0.40f, 1.0f);
            public static readonly Color Warning = new Color(0.85f, 0.60f, 0.20f, 1.0f);
            public static readonly Color Error = new Color(0.75f, 0.30f, 0.30f, 1.0f);

            public static readonly Color TextPrimary = new Color(0.95f, 0.95f, 0.97f, 1.0f);
            public static readonly Color TextSecondary = new Color(0.75f, 0.75f, 0.78f, 1.0f);
            public static readonly Color TextMuted = new Color(0.55f, 0.55f, 0.58f, 1.0f);
            public static readonly Color TextDisabled = new Color(0.40f, 0.40f, 0.42f, 1.0f);

            public static readonly Color Border = new Color(0.25f, 0.25f, 0.28f, 1.0f);
            public static readonly Color BorderLight = new Color(0.30f, 0.30f, 0.33f, 1.0f);
            public static readonly Color BorderFocus = new Color(0.25f, 0.50f, 0.75f, 1.0f);

            public static readonly Color Separator = new Color(0.22f, 0.22f, 0.25f, 1.0f);
            public static readonly Color SeparatorHighlight = new Color(0.35f, 0.35f, 0.40f, 1.0f);

            public static readonly Color SliderTrack = new Color(0.20f, 0.20f, 0.22f, 1.0f);
            public static readonly Color SliderFill = new Color(0.25f, 0.50f, 0.75f, 1.0f);
            public static readonly Color SliderThumb = new Color(0.85f, 0.85f, 0.88f, 1.0f);
            public static readonly Color SliderThumbHover = new Color(0.95f, 0.95f, 0.97f, 1.0f);

            public static readonly Color SectionHeader = new Color(0.70f, 0.75f, 0.85f, 1.0f);
            public static readonly Color SectionHeaderBackground = new Color(0.14f, 0.14f, 0.16f, 1.0f);

            public static readonly Color TabActive = new Color(0.25f, 0.50f, 0.75f, 1.0f);
            public static readonly Color TabInactive = new Color(0.16f, 0.16f, 0.18f, 1.0f);
            public static readonly Color TabHover = new Color(0.20f, 0.20f, 0.24f, 1.0f);
        }

        #endregion

        #region 尺寸定义

        public static class Sizes
        {
            public const float PaddingSmall = 6f;
            public const float Padding = 12f;
            public const float PaddingLarge = 18f;
            public const float PaddingXL = 24f;

            public const float RowHeight = 36f;
            public const float RowHeightCompact = 28f;
            public const float RowHeightLarge = 44f;

            public const float LabelWidth = 130f;
            public const float SliderWidth = 180f;
            public const float ColorPickerWidth = 70f;
            public const float ValueLabelWidth = 55f;

            public const float ButtonHeight = 32f;
            public const float ButtonHeightLarge = 40f;
            public const float ButtonWidth = 80f;
            public const float ButtonWidthSmall = 60f;

            public const float SectionSpacing = 18f;
            public const float ItemSpacing = 6f;
            public const float GroupSpacing = 12f;

            public const float CornerRadius = 4f;
            public const float CornerRadiusLarge = 6f;
            public const float BorderThickness = 1f;

            public const float PresetBarHeight = 54f;
            public const float TabBarHeight = 48f;
            public const float ControlBarHeight = 56f;

            public const float LeftPanelWidthRatio = 0.38f;
        }

        #endregion

        #region 字体定义

        public static class Fonts
        {
            public static FontReference Title => new FontReference { Size = 18 };
            public static FontReference Header => new FontReference { Size = 14 };
            public static FontReference Normal => new FontReference { Size = 12 };
            public static FontReference Small => new FontReference { Size = 10 };
        }

        #endregion

        #region 辅助方法

        public static Button CreateStyledButton(string text, float width = Sizes.ButtonWidth, float height = Sizes.ButtonHeight, ButtonStyle style = ButtonStyle.Default)
        {
            var button = new Button
            {
                Text = text,
                Width = width,
                Height = height
            };

            ApplyButtonStyle(button, style);
            return button;
        }

        public static void ApplyButtonStyle(Button button, ButtonStyle style = ButtonStyle.Default)
        {
            switch (style)
            {
                case ButtonStyle.Primary:
                    button.BackgroundColor = Colors.Primary;
                    button.TextColor = Colors.TextPrimary;
                    break;
                case ButtonStyle.Accent:
                    button.BackgroundColor = Colors.Accent;
                    button.TextColor = Colors.TextPrimary;
                    break;
                case ButtonStyle.Success:
                    button.BackgroundColor = Colors.Success;
                    button.TextColor = Colors.TextPrimary;
                    break;
                case ButtonStyle.Warning:
                    button.BackgroundColor = Colors.Warning;
                    button.TextColor = Colors.TextPrimary;
                    break;
                case ButtonStyle.Error:
                    button.BackgroundColor = Colors.Error;
                    button.TextColor = Colors.TextPrimary;
                    break;
                case ButtonStyle.Ghost:
                    button.BackgroundColor = Color.Transparent;
                    button.TextColor = Colors.TextSecondary;
                    break;
                default:
                    button.BackgroundColor = Colors.BackgroundElevated;
                    button.TextColor = Colors.TextSecondary;
                    break;
            }
        }

        public static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                TextColor = Colors.SectionHeader,
                Font = Fonts.Header,
                Height = 28
            };
        }

        public static Panel CreateSeparator(float width)
        {
            return new Panel
            {
                Width = width,
                Height = 1,
                BackgroundColor = Colors.Separator
            };
        }

        public static Label CreateValueLabel(string text = "0.00")
        {
            return new Label
            {
                Text = text,
                TextColor = Colors.TextMuted,
                Width = Sizes.ValueLabelWidth,
                HorizontalAlignment = TextAlignment.Far
            };
        }

        #endregion
    }

    public enum ButtonStyle
    {
        Default,
        Primary,
        Accent,
        Success,
        Warning,
        Error,
        Ghost
    }
}
