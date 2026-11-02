using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using Hud = global::Game.UI.HUD;

namespace HundunWorld.Game.UI.Ink.ActionBar
{
    /// <summary>
    /// 主界面底部滚轮式技能栏：每行 10 个槽位、共 6 行，滚轮上下切换、首尾无限循环，
    /// 一次只显示一行，左侧行标标明当前是第几行；当前行的 10 个槽位一一对应数字键 1~0。
    /// <para>
    /// 槽位接受拖入的技能与可直接消耗的物品：栏内槽位可互相换位，背包与选择器面板可跨页拖入。
    /// 行内容放在一个裁剪视口里的纵向条带上，靠条带位移实现滚轮感，
    /// 不依赖引擎对自定义绘制的裁剪行为。
    /// </para>
    /// </summary>
    public class InkActionBarControl : ContainerControl
    {
        // --- 设计空间尺寸（1920x1080 参考，与其余 Ink 页面同一坐标系）---
        private const float SlotSize = 50f;
        private const float SlotGap = 6f;
        private const float LabelW = 52f;
        private const float LabelGap = 10f;
        private const float Pad = 6f;
        private const float BottomMargin = 20f;
        private const float ChevronH = 14f;

        private const float SlotsW = ActionBarLayout.Columns * SlotSize + (ActionBarLayout.Columns - 1) * SlotGap;

        /// <summary>一行的高度（含上下内边距），同时也是滚轮条带的行距。</summary>
        internal const float RowPitch = SlotSize + 2f * Pad;

        internal const float SlotsLeft = Pad + LabelW + LabelGap;

        private const float ContentW = LabelW + LabelGap + SlotsW;

        /// <summary>滚轮切换一行的动画速度（行/秒）。</summary>
        private const float ScrollSpeed = 7f;

        private static readonly string[] RowNames = { "一行", "二行", "三行", "四行", "五行", "六行" };

        /// <summary>当前行 10 个槽位对应的数字键：第 1~9 槽为 Alpha1~Alpha9，第 10 槽为 Alpha0。</summary>
        private static readonly KeyboardKeys[] HotkeyKeys =
        {
            KeyboardKeys.Alpha1, KeyboardKeys.Alpha2, KeyboardKeys.Alpha3, KeyboardKeys.Alpha4,
            KeyboardKeys.Alpha5, KeyboardKeys.Alpha6, KeyboardKeys.Alpha7, KeyboardKeys.Alpha8,
            KeyboardKeys.Alpha9, KeyboardKeys.Alpha0,
        };

        private static readonly string[] HotkeyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };

        private ContainerControl _viewport;
        private ActionBarStrip _strip;
        private InkAttributeTooltip _tooltip;
        private InkActionBarPicker _picker;

        private float _scroll;
        private float _targetScroll;
        private int _hoverLabel;      // 0=无, 1=上箭头, 2=下箭头
        private bool _tooltipShown;

        /// <summary>当前停靠行（0 基）。</summary>
        public int CurrentRow => Mod((int)Math.Round(_scroll, MidpointRounding.AwayFromZero));

        /// <summary>滚轮的连续位置（小数部分即条带的滑动进度）。</summary>
        internal float Scroll => _scroll;

        internal int HoverSlot { get; private set; } = -1;

        internal int DropHoverSlot { get; private set; } = -1;

        public InkActionBarControl()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Size = new Float2(ContentW + 2f * Pad, RowPitch);

            _viewport = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(SlotsLeft, 0f),
                Size = new Float2(SlotsW, RowPitch),
                BackgroundColor = Color.Transparent,
                ClipChildren = true,
                AutoFocus = false,
            };
            _strip = new ActionBarStrip(this)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(SlotsW, RowPitch * 3f),
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
            };
            _viewport.AddChild(_strip);
            AddChild(_viewport);

            ActionBarLayout.Instance.Changed += OnLayoutChanged;
            ActionBarDrag.Finished += OnDragFinished;
            ScrollTo(ActionBarLayout.Instance.VisibleRow);
        }

        private InkPageShell _shell;
        private Float2 _designTopLeft;
        private float _relocatedScale = -1f;
        private Float2 _relocatedDesignSize;

        /// <summary>
        /// 挂到水墨外壳上（与返回按钮同一手法：外壳的子节点，排在内容层之后所以浮在页面之上）。
        /// 不用整屏覆盖层承载：铺满的容器会挡在页面前面吸掉鼠标。
        /// Tooltip 与选择器同样直接挂外壳，各自的命中区就是自己的可见区。
        /// </summary>
        public void AttachTo(InkPageShell shell)
        {
            if (shell == null || Parent != null) return;
            _shell = shell;

            shell.AddPersistentControl(this);

            _tooltip = new InkAttributeTooltip
            {
                AnchorPreset = AnchorPresets.TopLeft,
                DarkTheme = true,
            };
            shell.AddPersistentControl(_tooltip);

            _picker = new InkActionBarPicker(this)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            shell.AddPersistentControl(_picker);

            Relocate();
        }

        /// <summary>技能栏左上角在设计空间中的位置（选择器据此对齐）。</summary>
        internal Float2 DesignTopLeft => _designTopLeft;

        /// <summary>外壳的设计空间尺寸（1920 宽口径，随窗口比例变化）。</summary>
        internal Float2 HostDesignSize => _shell != null ? _shell.DesignSize : Float2.Zero;

        /// <summary>按设计坐标摆放一个常驻部件（技能栏的兄弟节点共用同一坐标系）。</summary>
        internal void PlaceHost(Control target, Float2 designLocation)
        {
            _shell?.PlaceInDesignSpace(target, designLocation);
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            Relocate();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            ActionBarLayout.Instance.Changed -= OnLayoutChanged;
            ActionBarDrag.Finished -= OnDragFinished;
        }

        /// <summary>底部居中：避开最下方的修为经验条与左右聊天框、功能列。</summary>
        private void Relocate()
        {
            if (_shell == null) return;
            Float2 design = _shell.DesignSize;
            if (design.X <= 0f || design.Y <= 0f) return;

            _designTopLeft = new Float2(design.X * 0.5f - Width * 0.5f, design.Y - BottomMargin - Height);
            _shell.PlaceInDesignSpace(this, _designTopLeft);

            _relocatedScale = _shell.DesignScale;
            _relocatedDesignSize = design;

            _picker?.Reposition();
        }

        // ===================================================================
        // 滚轮与行切换
        // ===================================================================

        /// <summary>切到相邻行；正数向下、负数向上，跨首尾无限循环。</summary>
        public void StepRow(int direction)
        {
            if (direction == 0) return;
            _targetScroll += direction < 0 ? -1f : 1f;
        }

        private void ScrollTo(int row)
        {
            row = Mod(row);
            _scroll = row;
            _targetScroll = row;
        }

        private static int Mod(int row)
        {
            int m = row % ActionBarLayout.Rows;
            return m < 0 ? m + ActionBarLayout.Rows : m;
        }

        /// <summary>动画是否已停靠；未停靠时不接受槽位操作，避免半显半隐的行被误点。</summary>
        internal bool IsSettled => Math.Abs(_scroll - _targetScroll) < 0.001f;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 拖拽会话由技能栏驱动：幽灵在空闲时是隐藏的，不能指望它的 Update 每帧被调用。
            ActionBarDrag.Tick();

            // 窗口尺寸变化时外壳只会重算内容层变换，子节点收不到尺寸事件，这里按缩放变化补排一次。
            if (_shell != null && (_shell.DesignScale != _relocatedScale || _shell.DesignSize != _relocatedDesignSize))
                Relocate();

            if (Math.Abs(_targetScroll - _scroll) > 0.0001f)
            {
                float step = ScrollSpeed * deltaTime;
                float gap = _targetScroll - _scroll;
                _scroll = Math.Abs(gap) <= step ? _targetScroll : _scroll + Math.Sign(gap) * step;
                _strip.Location = new Float2(0f, -(_scroll - (float)Math.Floor(_scroll)) * RowPitch);

                if (IsSettled)
                    ActionBarLayout.Instance.SetVisibleRow(CurrentRow);
            }

            int row = CurrentRow;
            for (int i = 0; i < HotkeyKeys.Length; i++)
            {
                if (!Input.GetKeyDown(HotkeyKeys[i])) continue;
                // 聊天框一类的文本输入正在接键时，数字属于输入内容而不是施放指令。
                if (ActionBarInputGate.IsTextEntryFocused()) break;
                ActivateSlot(ActionBarLayout.Instance.IndexOf(row, i));
                break;
            }
        }

        public override bool OnMouseWheel(Float2 location, float delta)
        {
            StepRow(delta > 0f ? -1 : 1);
            return true;
        }

        // ===================================================================
        // 槽位交互
        // ===================================================================

        /// <summary>
        /// 命中测试：把屏幕坐标折算成槽位索引，只有当前停靠行的槽位可被命中。
        /// </summary>
        internal bool TryHitTestSlot(Float2 screenPos, out int slotIndex)
        {
            slotIndex = -1;
            if (!IsSettled) return false;

            var local = PointFromScreen(screenPos);
            float x = local.X - SlotsLeft;
            float y = local.Y - Pad;
            if (x < 0f || y < 0f || y > SlotSize) return false;

            int col = (int)(x / (SlotSize + SlotGap));
            if (col < 0 || col >= ActionBarLayout.Columns) return false;
            if (x - col * (SlotSize + SlotGap) > SlotSize) return false;

            slotIndex = ActionBarLayout.Instance.IndexOf(CurrentRow, col);
            return true;
        }

        /// <summary>条带转交的槽位按下（<paramref name="screenPos"/> 为按下点屏幕坐标）。</summary>
        internal void HandleSlotMouseDown(int slotIndex, MouseButton button, Float2 screenPos)
        {
            var content = ActionBarLayout.Instance.Get(slotIndex);

            if (button == MouseButton.Right)
            {
                ActionBarLayout.Instance.Clear(slotIndex);
                return;
            }

            if (content.IsEmpty)
            {
                _picker?.OpenFor(slotIndex);
                return;
            }

            DropHoverSlot = slotIndex;
            ActionBarDrag.Begin(content, slotIndex, screenPos);
        }

        private void OnDragFinished(ActionBarSlotContent content, int sourceSlot, Float2 screenPos, bool moved)
        {
            DropHoverSlot = -1;

            // 没移动过就是"在原槽上点了一下"：按点击语义触发施放/使用。
            if (!moved)
            {
                if (sourceSlot >= 0 && TryHitTestSlot(screenPos, out int clicked) && clicked == sourceSlot)
                    ActivateSlot(clicked);
                return;
            }

            if (!TryHitTestSlot(screenPos, out int target)) return;

            // 背包里什么都能拖，但只有可直接消耗的物品值得占一个快捷键位。
            if (content.Kind == ActionBarSlotKind.Item && !global::HundunWorld.Game.Inventory.ItemCatalog.Resolve(content.RefId).IsUsable)
            {
                Hud.ShowNotification("只有丹药与可直接消耗的物品能放入技能栏", 1.8f, InkWashTheme.VermilionBright);
                return;
            }

            if (content.IsEmpty)
                ActionBarLayout.Instance.Clear(target);
            else
                ActionBarLayout.Instance.Assign(target, content);
        }

        /// <summary>从选择器面板选定内容后调用。</summary>
        internal void AssignToSlot(int slotIndex, ActionBarSlotContent content)
        {
            ActionBarLayout.Instance.Assign(slotIndex, content);
        }

        private void ActivateSlot(int slotIndex)
        {
            var content = ActionBarLayout.Instance.Get(slotIndex);
            if (content.IsEmpty)
            {
                _picker?.OpenFor(slotIndex);
                return;
            }

            if (!ActionBarService.Instance.TryActivate(content, out string reason) && !string.IsNullOrEmpty(reason))
                Hud.ShowNotification(reason, 1.6f, InkWashTheme.VermilionBright);
        }

        private void OnLayoutChanged()
        {
            // 读档后要把停靠行对齐到记录里的行号；用户正在滚动时不打断。
            if (IsSettled)
                ScrollTo(ActionBarLayout.Instance.VisibleRow);
            HideTooltip();
        }

        internal void SetHoverSlot(int slotIndex)
        {
            if (HoverSlot == slotIndex) return;
            HoverSlot = slotIndex;
        }

        internal void SetDropHoverSlot(int slotIndex)
        {
            DropHoverSlot = slotIndex;
        }

        // ===================================================================
        // 绘制：底板 + 行标
        // ===================================================================

        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f) return;

            var rect = new Rectangle(0f, 0f, Width, Height);
            InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusMd, new Color(
                InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.72f));
            InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusMd, InkWashTheme.BorderGoldSubtle, 1f);

            DrawRowLabel();

            base.Draw();
        }

        private void DrawRowLabel()
        {
            int row = CurrentRow;

            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 17f).GetFont();
            if (font != null)
            {
                Render2D.DrawText(font, RowNames[row], new Rectangle(Pad, 12f, LabelW, 20f),
                    InkWashTheme.GoldBright, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            var small = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 11f).GetFont();
            if (small != null)
            {
                Render2D.DrawText(small, $"{row + 1}/{ActionBarLayout.Rows}", new Rectangle(Pad, 32f, LabelW, 14f),
                    InkWashTheme.TextTertiary, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            Render2D.DrawLine(new Float2(SlotsLeft - LabelGap * 0.5f, Pad),
                new Float2(SlotsLeft - LabelGap * 0.5f, Height - Pad), InkWashTheme.BorderGoldSubtle, 1f);

            DrawChevron(0f, true);
            DrawChevron(Height - ChevronH, false);
        }

        /// <summary>行首尾的上下箭头：点击切换行，与滚轮等价。</summary>
        private void DrawChevron(float y, bool up)
        {
            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
            if (font == null) return;

            bool hovered = _hoverLabel == (up ? 1 : 2);
            Render2D.DrawText(font, up ? "▲" : "▼", new Rectangle(Pad, y, LabelW, ChevronH),
                hovered ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (TryHitLabelZone(location, out int zone))
            {
                StepRow(zone == 2 ? 1 : -1);
                return true;
            }
            return base.OnMouseDown(location, button);
        }

        /// <summary>行标区上下箭头命中（1=上、2=下、0=无）。</summary>
        private bool TryHitLabelZone(Float2 location, out int zone)
        {
            zone = 0;
            if (location.X > Pad + LabelW) return false;
            if (location.Y < ChevronH) zone = 1;
            else if (location.Y > Height - ChevronH) zone = 2;
            return zone != 0;
        }

        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);
            TryHitLabelZone(location, out _hoverLabel);
        }

        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _hoverLabel = 0;
            SetHoverSlot(-1);
            HideTooltip();
        }

        // ===================================================================
        // 悬浮说明
        // ===================================================================

        internal void ShowTooltipFor(int slotIndex, Float2 screenAnchor)
        {
            if (_tooltip == null) return;

            var content = ActionBarLayout.Instance.Get(slotIndex);
            var display = ActionBarService.Instance.Resolve(content);
            if (display == null)
            {
                HideTooltip();
                return;
            }

            string kind = content.Kind == ActionBarSlotKind.Skill ? "武学技能" : "消耗品";
            string core = display.DetailLines.Count > 0 ? string.Join("\n", display.DetailLines) : kind;
            _tooltip.SetData(display.Icon, display.Name, core, KindHint(content), null);
            _tooltip.Show(screenAnchor);
            _tooltipShown = true;
        }

        private static string KindHint(ActionBarSlotContent content)
        {
            if (content.Kind == ActionBarSlotKind.Item)
            {
                int count = ActionBarService.Instance.GetItemCount(content.RefId);
                return count > 0 ? $"持有 {count}" : "已用完";
            }
            return "左键使用 · 右键清空 · 拖动换位";
        }

        internal void HideTooltip()
        {
            if (_tooltip == null || !_tooltipShown) return;
            _tooltip.Hide();
            _tooltipShown = false;
        }

        internal static string HotkeyLabel(int column) =>
            column >= 0 && column < HotkeyLabels.Length ? HotkeyLabels[column] : string.Empty;

        internal static float SlotPixelSize => SlotSize;

        internal static float SlotStride => SlotSize + SlotGap;

        // ===================================================================
        // 嵌套控件：纵向条带（绘制 2~3 行槽位，随滚轮位移滑动）
        // ===================================================================

        /// <summary>
        /// 技能栏条带：在裁剪视口内按滚轮位置绘制相邻行槽位，并承担槽位的悬停与按下事件。
        /// </summary>
        private class ActionBarStrip : Control
        {
            private readonly InkActionBarControl _bar;

            public ActionBarStrip(InkActionBarControl bar)
            {
                _bar = bar;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);

                if (ActionBarDrag.IsActive)
                {
                    var local = PointFromScreen(Input.MousePosition);
                    _bar.SetDropHoverSlot(TryHitSlot(local, out int index) ? index : -1);
                }
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f) return;

                float scroll = _bar.Scroll;
                int baseRow = (int)Math.Floor(scroll);
                float frac = scroll - baseRow;

                // frac>0.5 时下一条已是主角，把再下一条也带上，滑入滑出都不留空档。
                int rowCount = frac > 0.001f ? 3 : 2;
                for (int i = 0; i < rowCount; i++)
                {
                    float rowY = i * InkActionBarControl.RowPitch;
                    float visible = RowVisibility(rowY, frac);
                    if (visible <= 0.01f) continue;
                    DrawRow(Mod(baseRow + i), rowY, visible);
                }
            }

            /// <summary>该行在视口内的可见比例（0=完全在视口外），用于淡入淡出避免硬切。</summary>
            private float RowVisibility(float rowY, float frac)
            {
                float top = rowY - frac * InkActionBarControl.RowPitch;
                float bottom = top + InkActionBarControl.RowPitch;
                float visibleH = Math.Min(bottom, InkActionBarControl.RowPitch) - Math.Max(top, 0f);
                return Math.Max(0f, visibleH / InkActionBarControl.RowPitch);
            }

            private void DrawRow(int row, float rowY, float alpha)
            {
                bool isCurrent = row == _bar.CurrentRow;
                for (int col = 0; col < ActionBarLayout.Columns; col++)
                {
                    var rect = new Rectangle(
                        col * InkActionBarControl.SlotStride,
                        rowY + InkActionBarControl.Pad,
                        InkActionBarControl.SlotPixelSize,
                        InkActionBarControl.SlotPixelSize);
                    DrawSlot(ActionBarLayout.Instance.IndexOf(row, col), rect, alpha, isCurrent);
                }
            }

            private void DrawSlot(int slotIndex, Rectangle rect, float alpha, bool isCurrent)
            {
                var layout = ActionBarLayout.Instance;
                var content = layout.Get(slotIndex);
                var service = ActionBarService.Instance;

                bool hovered = isCurrent && _bar.HoverSlot == slotIndex;
                bool dropTarget = isCurrent && _bar.DropHoverSlot == slotIndex;

                var bg = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G,
                    InkWashTheme.BaseTertiary.B, 0.9f * alpha);
                InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusSm, bg);

                if (content.IsEmpty)
                {
                    InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusSm,
                        Fade(InkWashTheme.BorderGoldSubtle, alpha), 1f);
                    if (hovered) DrawCenteredGlyph("+", rect, Fade(InkWashTheme.TextTertiary, alpha), 18f);
                    return;
                }

                var display = service.Resolve(content);
                float padding = 4f;
                var iconRect = new Rectangle(rect.X + padding, rect.Y + padding,
                    rect.Width - padding * 2f, rect.Height - padding * 2f);

                if (display != null)
                {
                    var icon = display.Icon;
                    if (icon != null && icon.IsLoaded)
                    {
                        Render2D.DrawTexture(icon, iconRect, new Color(1f, 1f, 1f, alpha));
                    }
                    else if (!string.IsNullOrEmpty(display.Glyph))
                    {
                        DrawCenteredGlyph(display.Glyph, rect, Fade(display.Tint, alpha), 20f);
                    }
                }

                DrawCooldown(rect, slotIndex, content, alpha);

                var border = dropTarget ? InkWashTheme.JadeBright
                    : hovered ? InkWashTheme.GoldBright : InkWashTheme.BorderGold;
                InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusSm,
                    Fade(border, alpha), dropTarget || hovered ? 2f : 1f);

                DrawCornerText(HotkeyLabelOfSlot(slotIndex), rect, new Rectangle(
                    rect.X + 3f, rect.Bottom - 13f, 12f, 12f), alpha);
                if (!string.IsNullOrEmpty(display?.Corner))
                {
                    DrawCornerText(display.Corner, rect, new Rectangle(
                        rect.Right - 16f, rect.Bottom - 13f, 13f, 12f), alpha);
                }
            }

            private static string HotkeyLabelOfSlot(int slotIndex) =>
                InkActionBarControl.HotkeyLabel(ActionBarLayout.Instance.ColumnOf(slotIndex));

            private void DrawCooldown(Rectangle rect, int slotIndex, ActionBarSlotContent content, float alpha)
            {
                float progress = ActionBarService.Instance.GetCooldownProgress(content);
                var center = new Float2(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
                float radius = rect.Width * 0.5f;

                if (progress > 0f)
                {
                    var veil = new Color(InkWashTheme.VermilionDeep.R, InkWashTheme.VermilionDeep.G,
                        InkWashTheme.VermilionDeep.B, 0.55f * alpha);
                    FillSector(center, radius, progress, veil);

                    string text = ActionBarService.Instance.GetCooldownText(content);
                    if (!string.IsNullOrEmpty(text))
                        DrawCenteredGlyph(text, rect, Fade(InkWashTheme.PaperBright, alpha), 14f);
                }
                else if (!content.IsEmpty && content.Kind == ActionBarSlotKind.Item
                         && ActionBarService.Instance.GetItemCount(content.RefId) <= 0)
                {
                    // 已用完的丹药仍留在栏上，用半透明压暗提示玩家，不静默清槽。
                    InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusSm,
                        new Color(0f, 0f, 0f, 0.45f * alpha));
                }
            }

            private static void FillSector(Float2 center, float radius, float progress, Color color)
            {
                const int segments = 32;
                int count = Math.Max(1, (int)Math.Ceiling(progress * segments));
                float start = -(float)Math.PI * 0.5f;
                float total = progress * (float)Math.PI * 2f;

                var vertices = new Float2[count * 3];
                for (int i = 0; i < count; i++)
                {
                    float a1 = start + total * i / count;
                    float a2 = start + total * (i + 1) / count;
                    vertices[i * 3] = center;
                    vertices[i * 3 + 1] = center + new Float2((float)Math.Cos(a1) * radius, (float)Math.Sin(a1) * radius);
                    vertices[i * 3 + 2] = center + new Float2((float)Math.Cos(a2) * radius, (float)Math.Sin(a2) * radius);
                }
                Render2D.FillTriangles(vertices, color);
            }

            private static void DrawCenteredGlyph(string text, Rectangle rect, Color color, float size)
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, size).GetFont();
                if (font == null) return;
                Render2D.DrawText(font, text, rect, color,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            private static void DrawCornerText(string text, Rectangle slot, Rectangle box, float alpha)
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f).GetFont();
                if (font == null) return;
                Render2D.DrawText(font, text, box, Fade(InkWashTheme.TextDefault, alpha),
                    TextAlignment.Near, TextAlignment.Far, TextWrapping.NoWrap);
            }

            private static Color Fade(Color color, float alpha) =>
                new Color(color.R, color.G, color.B, color.A * alpha);

            private static int Mod(int row)
            {
                int m = row % ActionBarLayout.Rows;
                return m < 0 ? m + ActionBarLayout.Rows : m;
            }

            /// <summary>条带局部坐标 → 槽位索引（仅停靠行可命中）。</summary>
            private bool TryHitSlot(Float2 local, out int slotIndex)
            {
                slotIndex = -1;
                if (!_bar.IsSettled) return false;

                float x = local.X;
                float y = local.Y - InkActionBarControl.Pad;
                if (x < 0f || y < 0f || y > InkActionBarControl.SlotPixelSize) return false;

                int col = (int)(x / InkActionBarControl.SlotStride);
                if (col < 0 || col >= ActionBarLayout.Columns) return false;
                if (x - col * InkActionBarControl.SlotStride > InkActionBarControl.SlotPixelSize) return false;

                slotIndex = ActionBarLayout.Instance.IndexOf(_bar.CurrentRow, col);
                return true;
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (TryHitSlot(location, out int index))
                {
                    _bar.HandleSlotMouseDown(index, button, PointToScreen(location));
                    return true;
                }
                return base.OnMouseDown(location, button);
            }

            public override bool OnMouseWheel(Float2 location, float delta) => _bar.OnMouseWheel(location, delta);

            public override void OnMouseMove(Float2 location)
            {
                base.OnMouseMove(location);
                if (TryHitSlot(location, out int index))
                {
                    _bar.SetHoverSlot(index);
                    if (!ActionBarDrag.IsActive)
                        _bar.ShowTooltipFor(index, PointToScreen(location));
                }
                else
                {
                    _bar.SetHoverSlot(-1);
                    _bar.HideTooltip();
                }
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _bar.SetHoverSlot(-1);
                _bar.HideTooltip();
            }
        }
    }
}
