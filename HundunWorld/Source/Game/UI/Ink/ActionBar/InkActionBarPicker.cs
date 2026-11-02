using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.ActionBar
{
    /// <summary>
    /// 技能栏槽位的选择器面板：点空槽弹出，列出可绑定的武学技能与可消耗物品。
    /// 点选直接绑定，也可以把条目拖到技能栏任意槽位上（与背包跨页拖拽共用一条会话）。
    /// </summary>
    public class InkActionBarPicker : ContainerControl
    {
        private const float PanelW = 320f;
        private const float HeaderH = 34f;
        private const float RowH = 34f;
        private const float MaxRows = 8f;
        private const float Pad = 8f;
        private const float GapAboveBar = 8f;

        /// <summary>按下后多久才接受"点击外部关闭"，避免被打开它的那一次按下立刻关掉。</summary>
        private const float OutsideCloseDelay = 0.2f;

        private readonly InkActionBarControl _bar;

        private readonly List<ActionBarSlotContent> _options = new();

        private int _pendingSlot = -1;
        private int _hoverRow = -1;
        private int _tab;             // 0=技能 1=物品
        private int _scrollTop;
        private float _openedAt;
        private bool _optionsStale = true;

        public InkActionBarPicker(InkActionBarControl bar)
        {
            _bar = bar;
            AnchorPreset = AnchorPresets.TopLeft;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Visible = false;
            Size = new Float2(PanelW, HeaderH + MaxRows * RowH + 2f * Pad);

            // 条目按下即进入拖拽会话，"按下没移动"由会话回调折算成点击绑定。
            ActionBarDrag.Finished += OnDragFinished;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            ActionBarDrag.Finished -= OnDragFinished;
        }

        private void OnDragFinished(ActionBarSlotContent content, int sourceSlot, Float2 screenPos, bool moved)        {
            if (!Visible || sourceSlot != -1 || moved || _pendingSlot < 0) return;
            if (!TryHitRowAtScreen(screenPos, out _)) return;

            _bar.AssignToSlot(_pendingSlot, content);
            Close();
        }

        private bool TryHitRowAtScreen(Float2 screenPos, out int row)
        {
            row = -1;
            if (!ContainsScreenPoint(screenPos)) return false;
            return TryHitRow(PointFromScreen(screenPos), out row);
        }

        public void OpenFor(int slotIndex)
        {
            _pendingSlot = slotIndex;
            _scrollTop = 0;
            _optionsStale = true;
            RebuildOptions();
            Reposition();
            Visible = true;
            _openedAt = Time.GameTime;
        }

        public void Close()
        {
            Visible = false;
            _pendingSlot = -1;
            _hoverRow = -1;
        }

        private void RebuildOptions()
        {
            if (!_optionsStale) return;
            _options.Clear();
            _options.AddRange(_tab == 0
                ? ActionBarService.Instance.BuildSkillOptions()
                : ActionBarService.Instance.BuildItemOptions());
            _optionsStale = false;
        }

        /// <summary>浮在技能栏正上方，横向收进设计空间边界内。</summary>
        internal void Reposition()
        {
            Float2 design = _bar.HostDesignSize;
            if (design.X <= 0f) return;

            float x = _bar.DesignTopLeft.X + (_bar.Width - PanelW) * 0.5f;
            x = Math.Max(Pad, Math.Min(x, design.X - PanelW - Pad));
            float y = _bar.DesignTopLeft.Y - Height - GapAboveBar;
            _bar.PlaceHost(this, new Float2(x, Math.Max(Pad, y)));
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (!Visible) return;

            Reposition();

            if (Time.GameTime - _openedAt < OutsideCloseDelay) return;

            if (Input.GetKeyDown(KeyboardKeys.Escape) || Input.GetMouseButtonDown(MouseButton.Right))
            {
                Close();
                return;
            }
            if (Input.GetMouseButtonDown(MouseButton.Left) && !ContainsScreenPoint(Input.MousePosition))
                Close();
        }

        /// <summary>屏幕坐标是否落在面板内。</summary>
        private bool ContainsScreenPoint(Float2 screenPos)
        {
            var local = PointFromScreen(screenPos);
            return local.X >= 0f && local.Y >= 0f && local.X <= Width && local.Y <= Height;
        }

        private int VisibleRowCount => (int)Math.Max(0f, Math.Min(MaxRows, _options.Count - _scrollTop));

        public override bool OnMouseWheel(Float2 location, float delta)
        {
            int maxTop = Math.Max(0, _options.Count - (int)MaxRows);
            _scrollTop = Math.Max(0, Math.Min(maxTop, _scrollTop + (delta > 0f ? -1 : 1)));
            return true;
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left) return true;

            if (TryHitTab(location, out int tab))
            {
                if (_tab != tab)
                {
                    _tab = tab;
                    _scrollTop = 0;
                    _optionsStale = true;
                    RebuildOptions();
                }
                return true;
            }

            if (!TryHitRow(location, out int row)) return true;

            int optionIndex = _scrollTop + row;
            if (optionIndex < 0 || optionIndex >= _options.Count) return true;

            ActionBarDrag.Begin(_options[optionIndex], -1, PointToScreen(location));
            return true;
        }

        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);
            _hoverRow = TryHitRow(location, out int row) ? row : -1;
        }

        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _hoverRow = -1;
        }

        private bool TryHitTab(Float2 location, out int tab)
        {
            tab = -1;
            if (location.Y < Pad || location.Y > Pad + RowH) return false;
            if (location.X < PanelW * 0.5f) tab = 0;
            else if (location.X < PanelW) tab = 1;
            return tab >= 0;
        }

        private bool TryHitRow(Float2 location, out int row)
        {
            row = -1;
            float listTop = Pad + RowH + 4f;
            float y = location.Y - listTop;
            if (y < 0f || location.X < Pad || location.X > PanelW - Pad) return false;
            int index = (int)(y / RowH);
            if (index < 0 || index >= VisibleRowCount) return false;
            row = index;
            return true;
        }

        // ===================================================================
        // 绘制
        // ===================================================================

        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f) return;

            RebuildOptions();

            var rect = new Rectangle(0f, 0f, Width, Height);
            InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusMd, new Color(
                InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.96f));
            InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusMd, InkWashTheme.BorderGold, 1f);

            DrawTabs();
            DrawRows();

            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
            if (font != null && _options.Count > 0)
            {
                Render2D.DrawText(font, $"{_scrollTop + 1}-{_scrollTop + VisibleRowCount} / {_options.Count}",
                    new Rectangle(PanelW - 110f, Height - 18f, 102f, 14f), InkWashTheme.TextTertiary,
                    TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap);
            }
        }

        private void DrawTabs()
        {
            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f).GetFont();
            if (font == null) return;

            DrawTabText(font, "武学技能", new Rectangle(Pad, Pad, PanelW * 0.5f - Pad, RowH), _tab == 0);
            DrawTabText(font, "可消耗品", new Rectangle(PanelW * 0.5f, Pad, PanelW * 0.5f - Pad, RowH), _tab == 1);

            Render2D.DrawLine(new Float2(Pad, Pad * 2f + RowH), new Float2(Width - Pad, Pad * 2f + RowH),
                InkWashTheme.BorderGoldSubtle, 1f);
        }

        private void DrawTabText(Font font, string text, Rectangle box, bool active)
        {
            Render2D.DrawText(font, text, box,
                active ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            if (!active) return;

            float y = box.Bottom - 2f;
            Render2D.DrawLine(new Float2(box.X + box.Width * 0.3f, y),
                new Float2(box.Right - box.Width * 0.3f, y), InkWashTheme.GoldPrimary, 2f);
        }

        private void DrawRows()
        {
            if (_options.Count == 0)
            {
                DrawLineText(InkWashTheme.TextTertiary,
                    _tab == 0 ? "尚无已学技能" : "背包中没有可消耗的物品",
                    new Rectangle(Pad, Pad + RowH + 10f, PanelW - 2f * Pad, RowH), 12f, TextAlignment.Center);
                return;
            }

            float listTop = Pad + RowH + 4f;
            int count = VisibleRowCount;
            for (int i = 0; i < count; i++)
            {
                var content = _options[_scrollTop + i];
                var box = new Rectangle(Pad, listTop + i * RowH, PanelW - 2f * Pad, RowH - 2f);
                var display = ActionBarService.Instance.Resolve(content);

                if (_hoverRow == i)
                {
                    InkRenderHelper.FillRoundedRectangle(box, InkWashTheme.RadiusSm,
                        new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f));
                }

                // 图标块
                var iconBox = new Rectangle(box.X + 2f, box.Y + 2f, RowH - 6f, RowH - 6f);
                InkRenderHelper.FillRoundedRectangle(iconBox, InkWashTheme.RadiusSm, InkWashTheme.BaseTertiary);
                if (display != null)
                {
                    var icon = display.Icon;
                    if (icon != null && icon.IsLoaded)
                    {
                        Render2D.DrawTexture(icon, iconBox, Color.White);
                    }
                    else
                    {
                        DrawLineText(display.Tint, display.Glyph, iconBox, 15f, TextAlignment.Center);
                    }
                }

                float textX = iconBox.Right + 8f;
                float textW = box.Right - textX - 46f;
                DrawLineText(InkWashTheme.TextDefault, display?.Name ?? "未知",
                    new Rectangle(textX, box.Y, textW, box.Height * 0.62f), 12f, TextAlignment.Near);

                // 已绑定位置提示，避免玩家重复占用同一技能
                int boundIndex = ActionBarLayout.Instance.FindIndex(content);
                string sub = boundIndex >= 0
                    ? $"已绑定 第{ActionBarLayout.Instance.RowOf(boundIndex) + 1}行·{InkActionBarControl.HotkeyLabel(ActionBarLayout.Instance.ColumnOf(boundIndex))}"
                    : _tab == 0 ? "未绑定" : $"持有 {ActionBarService.Instance.GetItemCount(content.RefId)}";
                DrawLineText(InkWashTheme.TextTertiary, sub,
                    new Rectangle(textX, box.Y + box.Height * 0.55f, textW, box.Height * 0.45f), 10f, TextAlignment.Near);
            }
        }

        private static void DrawLineText(Color color, string text, Rectangle box, float size, TextAlignment horizontal)
        {
            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, size).GetFont();
            if (font == null) return;
            Render2D.DrawText(font, text, box, color, horizontal, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }
}
