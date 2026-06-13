using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.StyleSystem
{
    public static class KungfuTheme
    {
        public static class Colors
        {
            public static readonly Color BackgroundPrimary = new Color(0.047f, 0.047f, 0.047f);
            public static readonly Color BackgroundSecondary = new Color(0.102f, 0.102f, 0.102f);
            public static readonly Color Accent = new Color(0.831f, 0.686f, 0.216f);
            public static readonly Color TextPrimary = Color.White;
            public static readonly Color TextSecondary = new Color(0.702f, 0.702f, 0.702f);
            public static readonly Color Border = new Color(0.235f, 0.235f, 0.235f);
            public static readonly Color Divider = new Color(0.165f, 0.165f, 0.165f);
            public static readonly Color ButtonBackground = new Color(0.165f, 0.165f, 0.165f);
            public static readonly Color ProgressBackground = new Color(0.2f, 0.2f, 0.2f);
            public static readonly Color Success = new Color(0.153f, 0.682f, 0.376f);
            public static readonly Color Error = new Color(0.753f, 0.224f, 0.169f);
            public static readonly Color Overlay = new Color(0, 0, 0, 0.7f);
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
