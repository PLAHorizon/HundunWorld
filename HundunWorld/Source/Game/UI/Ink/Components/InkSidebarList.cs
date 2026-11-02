using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 通用侧边栏导航列表：按 <see cref="InkSidebarNav"/> 的分组渲染全部功能页入口，超出高度可滚轮滚动。
    /// <para>
    /// 取代各页面各自硬编码一份（且还带"暂无路由"灰条）的侧边栏实现——六个有侧边栏的页面共用这一份，
    /// 因此"侧边栏能到达除弹框/事件 UI 之外的所有 UI"只需在 <see cref="InkSidebarNav"/> 维护一次。
    /// </para>
    /// </summary>
    public class InkSidebarList : ContainerControl
    {
        /// <summary>条目点击：参数为目标 dom-id，页面据此发 <c>NavigationRequested</c>。</summary>
        public event Action<string> NavigationRequested;

        private const float RowHeight = 34f;
        private const float HeaderHeight = 24f;
        private const float RowGap = 2f;
        private const float SectionGap = 8f;
        private const float ScrollStep = 44f;
        private const float ActiveBarWidth = 2f;

        private readonly ContainerControl _content;
        private readonly List<Control> _blocks = new List<Control>();
        private readonly List<SidebarRow> _rows = new List<SidebarRow>();

        private float _contentHeight;
        private float _scroll;

        public InkSidebarList(string activeDomId)
        {
            AutoFocus = false;
            // 只有 ContainerControl 有 ClipChildren：滚动时必须真裁剪，否则行会画到页脚上。
            ClipChildren = true;
            BackgroundColor = Color.Transparent;

            _content = new ContainerControl
            {
                AutoFocus = false,
                BackgroundColor = Color.Transparent,
            };
            AddChild(_content);

            Build(activeDomId);
        }

        /// <summary>当前激活页（用于高亮）。别名页传入其规范 id。</summary>
        public string ActiveDomId { get; private set; }

        /// <summary>切换激活项（页面复用同一个列表实例时调用）。</summary>
        public void SetActive(string domId)
        {
            ActiveDomId = domId;
            foreach (var row in _rows)
            {
                row.SetActive(row.DomId == domId);
            }
        }

        /// <summary>由页面在布局阶段调用：给出列表区域，内部完成分组排布与滚动钳制。</summary>
        public void Layout(float x, float y, float width, float height)
        {
            Location = new Float2(x, y);
            Size = new Float2(Mathf.Max(0f, width), Mathf.Max(0f, height));

            float cy = SectionGap;
            foreach (var block in _blocks)
            {
                if (block is SidebarRow row)
                {
                    row.Location = new Float2(0f, cy);
                    row.Size = new Float2(Width, RowHeight);
                    row.LayoutBadge();
                    cy += RowHeight + RowGap;
                }
                else if (block is Label header)
                {
                    header.Location = new Float2(16f, cy);
                    header.Size = new Float2(Mathf.Max(0f, Width - 32f), HeaderHeight);
                    cy += HeaderHeight + RowGap;
                }
            }

            _contentHeight = cy + SectionGap;
            _content.Size = new Float2(Width, Mathf.Max(Height, _contentHeight));
            ApplyScroll();
        }

        public override bool OnMouseWheel(Float2 location, float delta)
        {
            if (_contentHeight > Height)
            {
                _scroll = Mathf.Clamp(_scroll - delta * ScrollStep, 0f, _contentHeight - Height);
                ApplyScroll();
                return true;
            }
            return base.OnMouseWheel(location, delta);
        }

        public override void Draw()
        {
            base.Draw();

            // 溢出时右侧画一条细滚动条（不用引擎 ScrollBar，避免与页脚/侧栏宽度打架）。
            if (_contentHeight <= Height || Height <= 0f) return;

            var gold = InkWashTheme.GoldPrimary;
            float trackTop = 4f;
            float trackH = Mathf.Max(1f, Height - 8f);
            InkRenderHelper.FillRoundedRectangle(
                new Rectangle(Width - 4f, trackTop, 3f, trackH), 1.5f, new Color(1f, 1f, 1f, 0.06f));

            float barH = Mathf.Max(24f, trackH * (Height / _contentHeight));
            float range = Mathf.Max(1f, _contentHeight - Height);
            float barY = trackTop + (trackH - barH) * (_scroll / range);
            InkRenderHelper.FillRoundedRectangle(
                new Rectangle(Width - 4f, barY, 3f, barH), 1.5f, new Color(gold.R, gold.G, gold.B, 0.45f));
        }

        private void ApplyScroll()
        {
            _content.Location = new Float2(0f, -_scroll);
        }

        private void Build(string activeDomId)
        {
            ActiveDomId = activeDomId;

            foreach (var section in InkSidebarNav.Sections)
            {
                var header = new Label
                {
                    Text = section.Title,
                    AnchorPreset = AnchorPresets.TopLeft,
                    TextColor = InkWashTheme.TextTertiary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AutoFocus = false,
                };
                _content.AddChild(header);
                _blocks.Add(header);

                foreach (var item in section.Items)
                {
                    var row = new SidebarRow(item)
                    {
                        Parent = _content,
                    };
                    row.SetActive(item.DomId == activeDomId);
                    string target = item.DomId;
                    row.Clicked += () => NavigationRequested?.Invoke(target);

                    _rows.Add(row);
                    _blocks.Add(row);
                }
            }
        }

        /// <summary>单条目：左激活竖条 + 文案；悬停/按下有底色反馈。</summary>
        private sealed class SidebarRow : ContainerControl
        {
            public readonly string DomId;

            public event Action Clicked;

            private readonly Label _label;
            private bool _active;
            private bool _hovered;
            private bool _pressed;

            public SidebarRow(InkNavItem item)
            {
                DomId = item.DomId;
                AutoFocus = false;
                BackgroundColor = Color.Transparent;

                _label = new Label
                {
                    Text = item.Label,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(18f, 0f),
                    TextColor = InkWashTheme.TextSecondary,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    AutoFocus = false,
                };
                AddChild(_label);

                if (!string.IsNullOrEmpty(item.Badge))
                {
                    // 条目右侧角标（如战令的"热"）。
                    _badge = new InkTag
                    {
                        Text = item.Badge,
                        TagVariant = InkTagVariant.Vermilion,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Parent = this,
                    };
                }
            }

            private readonly InkTag _badge;

            /// <summary>角标跟随行内右侧（行尺寸在 Layout 后才确定，故此处重排）。</summary>
            public void LayoutBadge()
            {
                if (_badge == null) return;
                _badge.Location = new Float2(Mathf.Max(0f, Width - 34f), (Height - 16f) * 0.5f);
                _badge.Size = new Float2(26f, 16f);
            }

            public void SetActive(bool active)
            {
                _active = active;
                ApplyStyle();
            }

            private void ApplyStyle()
            {
                _label.TextColor = _active ? InkWashTheme.TextGold : InkWashTheme.TextSecondary;
                _label.Font = InkRenderHelper.GetFontRef(
                    InkWashTheme.FontRole.Body, _active ? 14f : 13f);
            }

            public override void Draw()
            {
                if (_hovered || _pressed)
                {
                    var tint = InkWashTheme.BaseTertiary;
                    InkRenderHelper.FillRoundedRectangle(
                        new Rectangle(Float2.Zero, Size), 4f,
                        new Color(tint.R, tint.G, tint.B, _pressed ? 0.55f : 0.35f));
                }
                if (_active)
                {
                    InkRenderHelper.FillRoundedRectangle(
                        new Rectangle(0f, (Height - 18f) * 0.5f, ActiveBarWidth, 18f),
                        ActiveBarWidth * 0.5f, InkWashTheme.GoldPrimary);
                }
                base.Draw();
            }

            public override void OnMouseEnter(Float2 location)
            {
                _hovered = true;
                base.OnMouseEnter(location);
            }

            public override void OnMouseLeave()
            {
                _hovered = false;
                _pressed = false;
                base.OnMouseLeave();
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    _pressed = true;
                }
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && _pressed)
                {
                    _pressed = false;
                    if (ContainsPoint(ref location))
                    {
                        Clicked?.Invoke();
                    }
                    return true;
                }
                return base.OnMouseUp(location, button);
            }
        }
    }
}
