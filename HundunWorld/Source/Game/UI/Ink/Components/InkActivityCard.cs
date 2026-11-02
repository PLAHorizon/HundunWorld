using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 活动卡片：标题 + 副标题 + 徽标 + 状态 + 单个行动按钮。
    /// 对应复核报告 7.1 的“入口即状态”：人数/多人、周奖励、缺治疗等信息应出现在卡片上。
    /// 未接服务端时按钮禁用并显示“未接入”，不允许假交互。
    /// </summary>
    public class InkActivityCard : InkPanel
    {
        private readonly InkTextBlock _title;
        private readonly InkTextBlock _subtitle;
        private readonly InkTextBlock _status;
        private readonly InkBadge _badge;
        private readonly InkButton _action;
        private readonly ContainerControl _accentBar;
        private readonly Color _accent;
        private Action _onClick;

        public InkActivityCard(string title, string subtitle, string badge, string status, Color accent, bool compact = false)
        {
            Radius = 8f;
            ClipChildren = true;
            _accent = accent;
            BackgroundColor = new Color(InkWashTheme.Panel.R, InkWashTheme.Panel.G, InkWashTheme.Panel.B, 0.78f);

            _accentBar = new ContainerControl { BackgroundColor = accent };
            AddChild(_accentBar);

            _title = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = title ?? "",
                TextColor = InkWashTheme.TextDefault,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            if (compact)
                _title.Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f);
            AddChild(_title);

            _subtitle = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = subtitle ?? "",
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            AddChild(_subtitle);

            _status = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = status ?? "",
                TextColor = accent,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = Color.Transparent,
            };
            AddChild(_status);

            _badge = new InkBadge(badge ?? "", accent);
            AddChild(_badge);

            _action = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "未接入",
                Enabled = false,
            };
            _action.Clicked += () => _onClick?.Invoke();
            AddChild(_action);
        }

        public void SetAction(string text, Action onClick, bool enabled)
        {
            _action.Text = text ?? "";
            _action.Enabled = enabled;
            _onClick = enabled ? onClick : null;
        }

        public void SetStatus(string status) => _status.Text = status ?? "";

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutContent();
        }

        private void LayoutContent()
        {
            if (Width <= 0f || Height <= 0f) return;

            _accentBar.Location = Float2.Zero;
            _accentBar.Size = new Float2(3f, Height);

            float left = 16f;
            float rightPad = 16f;
            float actionW = 84f;
            float textW = Mathf.Max(0f, Width - left - rightPad - actionW - 12f);

            _badge.Location = new Float2(Width - 78f, 10f);
            _badge.Size = new Float2(64f, 20f);

            _title.Location = new Float2(left, 10f);
            _title.Size = new Float2(Mathf.Max(0f, textW), 24f);

            _subtitle.Location = new Float2(left, 34f);
            _subtitle.Size = new Float2(Mathf.Max(0f, Width - left - rightPad), 16f);

            _status.Location = new Float2(left, Mathf.Max(0f, Height - 28f));
            _status.Size = new Float2(Mathf.Max(0f, Width - left - rightPad - actionW - 8f), 18f);

            _action.Location = new Float2(Mathf.Max(0f, Width - rightPad - actionW), Mathf.Max(0f, Height - 36f));
            _action.Size = new Float2(actionW, 28f);
        }
    }
}
