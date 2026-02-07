using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// Toast消息类
    /// 表示单个Toast消息的UI元素
    /// </summary>
    public class ToastMessage
    {
        public Panel Panel { get; }
        public string Message { get; }
        public Color Color { get; }
        public string Icon { get; }
        public float Duration { get; }

        public ToastMessage(string message, Color color, string icon, float duration)
        {
            Message = message;
            Color = color;
            Icon = icon;
            Duration = duration;

            Panel = new Panel
            {
                Size = new Float2(280, 60),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f),
                ScrollbarThumbSelectedColor = color,
                ScrollMargin = new Margin(2)
            };

            // 图标标签
            var iconLabel = new Label
            {
                Text = icon,
                Location = new Float2(10, 10),
                Size = new Float2(30, 40),
                TextColor = color,
                HorizontalAlignment = TextAlignment.Center,
                Font = UIHelper.SetFont(size: 20)
            };
            Panel.AddChild(iconLabel);

            // 消息标签
            var messageLabel = new Label
            {
                Text = message,
                Location = new Float2(50, 10),
                Size = new Float2(220, 40),
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            Panel.AddChild(messageLabel);
        }
    }
}