using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.ActionBar
{
    /// <summary>
    /// 全局拖拽会话：把"从哪拖来"和"拖到哪去"解耦。
    /// <para>
    /// 来源控件只需在按下时调用 <see cref="Begin"/>，之后由幽灵层的每帧 Update 追踪指针并检测松开，
    /// 不依赖引擎把鼠标移动/抬起事件继续投递给离开的控件（Flax 的鼠标事件按悬停投递，跨控件拖拽不可靠）。
    /// 落点控件订阅 <see cref="Finished"/>，用 <see cref="Control.PointFromScreen"/> 自己判断有没有被命中。
    /// </para>
    /// </summary>
    public static class ActionBarDrag
    {
        /// <summary>按下后移动超过该像素才算拖动，否则视为点击。</summary>
        private const float DragThreshold = 8f;

        public static bool IsActive { get; private set; }

        public static ActionBarSlotContent Payload { get; private set; }

        /// <summary>拖拽起点的技能栏槽位索引；来自背包或选择器时为 -1。</summary>
        public static int SourceSlot { get; private set; }

        /// <summary>
        /// 拖拽结束（松开或按 Esc 取消）。参数：内容、起点槽位、终点屏幕坐标、指针是否移动过。
        /// 未移动过的结束即"在起点上点了一下"，由起点自己决定点击语义。
        /// </summary>
        public static event Action<ActionBarSlotContent, int, Float2, bool> Finished;

        private static InkDragGhost _ghost;
        private static Float2 _startScreenPos;
        private static bool _moved;

        /// <summary>由 MainUIManager 在画布根建好幽灵层后调用。</summary>
        public static void AttachGhost(InkDragGhost ghost)
        {
            _ghost = ghost;
            if (_ghost != null) _ghost.Visible = false;
        }

        public static bool Begin(ActionBarSlotContent content, int sourceSlot, Float2 startScreenPos)
        {
            if (IsActive) return false;

            IsActive = true;
            Payload = content;
            SourceSlot = sourceSlot;
            _startScreenPos = startScreenPos;
            _moved = false;

            if (_ghost != null)
            {
                // 幽灵一按下就出现会让普通点击看起来像拖拽，等真的移动起来再显示。
                _ghost.SetContent(ActionBarService.Instance.Resolve(content), content);
                _ghost.Follow(startScreenPos);
                _ghost.Visible = false;
            }
            return true;
        }

        /// <summary>结束会话并广播给落点；<paramref name="cancelled"/> 为真时不改变任何绑定。</summary>
        public static void End(bool cancelled)
        {
            if (!IsActive) return;

            var content = Payload;
            int source = SourceSlot;
            bool moved = _moved;
            var endPos = _ghost != null ? _ghost.PointerScreenPos : _startScreenPos;

            IsActive = false;
            Payload = ActionBarSlotContent.EmptyContent;
            SourceSlot = -1;
            if (_ghost != null) _ghost.Visible = false;

            if (cancelled) return;

            try
            {
                Finished?.Invoke(content, source, endPos, moved);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ActionBarDrag] Finished 订阅者异常: {ex.Message}");
            }
        }

        /// <summary>每帧由幽灵层驱动：跟随指针 + 检测松开与取消。</summary>
        public static void Tick()
        {
            if (!IsActive) return;

            var pos = Input.MousePosition;
            float dx = pos.X - _startScreenPos.X;
            float dy = pos.Y - _startScreenPos.Y;
            if (!_moved && dx * dx + dy * dy > DragThreshold * DragThreshold)
            {
                _moved = true;
                if (_ghost != null) _ghost.Visible = true;
            }

            _ghost?.Follow(pos);

            if (Input.GetKeyDown(KeyboardKeys.Escape))
            {
                End(true);
                return;
            }
            if (!Input.GetMouseButton(MouseButton.Left))
                End(false);
        }
    }

    /// <summary>
    /// 拖拽幽灵：画布根上的顶层控件，只在拖拽期间可见，跟随指针画出被拖内容的缩略图。
    /// </summary>
    public class InkDragGhost : Control
    {
        private const float GhostSize = 52f;

        private ActionBarDisplay _display;
        private Float2 _pointerScreenPos;

        /// <summary>当前指针的屏幕坐标（供落点命中测试）。</summary>
        public Float2 PointerScreenPos => _pointerScreenPos;

        public InkDragGhost()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            Size = new Float2(GhostSize, GhostSize);
            BackgroundColor = Color.Transparent;
            Visible = false;
            AutoFocus = false;
        }

        public void SetContent(ActionBarDisplay display, ActionBarSlotContent content)
        {
            _display = display;
        }

        /// <summary>跟随指针（屏幕坐标），把自身中心对准指针。幽灵挂在未缩放的画布根上，自己补设计缩放。</summary>
        public void Follow(Float2 screenPos)
        {
            _pointerScreenPos = screenPos;
            if (Parent == null) return;

            float s = UILayout.Scale > 0f ? UILayout.Scale : 1f;
            Scale = new Float2(s, s);

            var local = Parent.PointFromScreen(screenPos);
            Location = new Float2(local.X - GhostSize * s * 0.5f, local.Y - GhostSize * s * 0.5f);
        }

        public override void Draw()
        {
            if (!Visible || _display == null || Width <= 0f) return;

            var rect = new Rectangle(0f, 0f, Width, Height);
            InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusMd, new Color(
                InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.92f));

            var icon = _display.Icon;
            if (icon != null && icon.IsLoaded)
            {
                Render2D.DrawTexture(icon, new Rectangle(4f, 4f, Width - 8f, Height - 8f), Color.White);
            }
            else if (!string.IsNullOrEmpty(_display.Glyph))
            {
                var fontRef = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f);
                Render2D.DrawText(fontRef.GetFont(), _display.Glyph, rect, _display.Tint,
                    TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusMd, _display.Tint, 1f);
        }
    }
}
