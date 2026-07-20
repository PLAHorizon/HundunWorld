using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.StyleSystem
{
    public static class KungfuTheme
    {
        public static class Colors
        {
            // 统一映射设计 Token（出处：game-ui-system/colors_and_type.css --ink-* 水墨古风）
            public static readonly Color BackgroundPrimary = UIStyleTokens.BgVoid; // --ink-bg-void
            public static readonly Color BackgroundSecondary = UIStyleTokens.BgInk; // --ink-bg-ink
            public static readonly Color Accent = UIStyleTokens.GoldPrimary; // --ink-gold-primary
            public static readonly Color TextPrimary = UIStyleTokens.TextPrimary; // --ink-text-primary（禁纯白）
            public static readonly Color TextSecondary = UIStyleTokens.TextSecondary; // --ink-text-secondary
            public static readonly Color Border = UIStyleTokens.BorderGold; // --ink-border-gold
            public static readonly Color Divider = UIStyleTokens.Divider; // --ink-divider
            public static readonly Color ButtonBackground = UIStyleTokens.BgElevated; // --ink-bg-elevated
            public static readonly Color ProgressBackground = UIStyleTokens.BgElevated; // 进度条轨道（ds-progress §4.6）
            public static readonly Color Success = UIStyleTokens.StatusSuccess; // --status-success-default
            public static readonly Color Error = UIStyleTokens.StatusError; // --status-error-default
            public static readonly Color Overlay = UIStyleTokens.Scrim; // 墨黑遮罩
        }
        
        public static class Sizes
        {
            public const float TopBarHeight = 50f;
            public const float TabBarHeight = 44f;
            public const float BottomBarHeight = 70f;
            public const float CardHeight = 140f;
            public const float CardSpacing = 12f;
            public const float Padding = 16f;
            public const float PaddingSmall = 8f;
            public const float IconSize = 60f;
            public const float ButtonIconSize = 44f;
            public const float BorderRadius = 8f;
            public const float BorderWidth = 1f;
        }
        
        public static class Fonts
        {
            public static FontReference GetTitleFont()
            {
                return UIHelper.SetFont(size: 18);
            }
            
            public static FontReference GetBodyFont()
            {
                return UIHelper.SetFont(size: 14);
            }
            
            public static FontReference GetSmallFont()
            {
                return UIHelper.SetFont(size: 12);
            }
        }
        
        public static Button CreateIconButton(string text, float size = Sizes.ButtonIconSize)
        {
            var button = new Button
            {
                Size = new Float2(size, size),
                BackgroundColor = Colors.ButtonBackground,
                Text = text,
                TextColor = Colors.TextSecondary,
                Font = Fonts.GetBodyFont()
            };
            return button;
        }
        
        public static Button CreateTextButton(string text, float width = 80f, float height = 32f)
        {
            var button = new Button
            {
                Size = new Float2(width, height),
                BackgroundColor = Color.Transparent,
                Text = text,
                TextColor = Colors.Accent,
                Font = Fonts.GetBodyFont()
            };
            return button;
        }
        
        public static Label CreateTitleLabel(string text)
        {
            return new Label
            {
                Text = text,
                TextColor = Colors.TextPrimary,
                Font = Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
        }
        
        public static Label CreateBodyLabel(string text)
        {
            return new Label
            {
                Text = text,
                TextColor = Colors.TextSecondary,
                Font = Fonts.GetBodyFont(),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
        }
        
        public static Label CreateAccentLabel(string text)
        {
            return new Label
            {
                Text = text,
                TextColor = Colors.Accent,
                Font = Fonts.GetBodyFont(),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
        }
        
        public static Panel CreateCard()
        {
            return new Panel
            {
                BackgroundColor = Colors.BackgroundSecondary
            };
        }
        
        public static ProgressBar CreateProgressBar(float width, float height = 6f)
        {
            return new ProgressBar
            {
                Size = new Float2(width, height),
                BackgroundColor = Colors.ProgressBackground,
                Value = 0f,
                Minimum = 0f,
                Maximum = 100f
            };
        }
    }
}
