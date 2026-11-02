using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 小型状态徽标（人数/多人/PvP/未接入）。圆角深底 + 细边 + 单行文字。
    /// 用于活动卡、列表行与地图侧栏，避免各页手写一套 Label。
    /// </summary>
    public class InkBadge : InkPanel
    {
        private readonly InkTextBlock _text;
        private Color _accent;

        public InkBadge(string text, Color accent)
        {
            Radius = 9f;
            ClipChildren = true;
            _accent = accent;
            BackgroundColor = WithAlpha(accent, 0.16f);

            _text = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = text ?? "",
                TextColor = accent,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                BackgroundColor = Color.Transparent,
            };
            AddChild(_text);
        }

        public string Text
        {
            get => _text.Text;
            set => _text.Text = value ?? "";
        }

        public void SetAccent(Color accent)
        {
            _accent = accent;
            BackgroundColor = WithAlpha(accent, 0.16f);
            _text.TextColor = accent;
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            _text.Location = Float2.Zero;
            _text.Size = Size;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.R, c.G, c.B, a);
    }

    /// <summary>
    /// 数值单元：上方说明、中间大数字、下方补充。用于四海笺/心力/周目标等
    /// “值必须带上下文”的位置，避免只显示一个裸数字。
    /// </summary>
    public class InkStatCell : InkPanel
    {
        private readonly InkTextBlock _caption;
        private readonly InkTextBlock _value;
        private readonly InkTextBlock _hint;

        public InkStatCell(string caption, string value, string hint, Color valueColor)
        {
            Radius = 8f;
            ClipChildren = true;
            BackgroundColor = new Color(InkWashTheme.Panel.R, InkWashTheme.Panel.G, InkWashTheme.Panel.B, 0.72f);

            _caption = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = caption ?? "",
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            _value = new InkTextBlock(InkTextStyle.Number)
            {
                Text = value ?? "—",
                TextColor = valueColor,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            _hint = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = hint ?? "",
                TextColor = InkWashTheme.TextFaint,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            AddChild(_caption);
            AddChild(_value);
            AddChild(_hint);
        }

        public void SetValue(string value, string hint = null)
        {
            _value.Text = value ?? "—";
            if (hint != null) _hint.Text = hint;
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            float w = Mathf.Max(0f, Width - 28f);
            _caption.Location = new Float2(14f, 8f);
            _caption.Size = new Float2(w, 16f);
            _value.Location = new Float2(14f, 24f);
            _value.Size = new Float2(w, 24f);
            _hint.Location = new Float2(14f, 48f);
            _hint.Size = new Float2(w, 14f);
        }
    }
}
