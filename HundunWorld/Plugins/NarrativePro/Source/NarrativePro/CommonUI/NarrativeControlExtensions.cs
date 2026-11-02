using FlaxEngine;
using FlaxEngine.GUI;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// Flax GUI <see cref="Control"/> 的 z 序辅助。
    /// <para>
    /// UE5 Slate 用 ZOrder（SetPanelWidgetZOrder / UCanvasPanelSlot::SetZOrder）控制叠放顺序，
    /// Flax 没有 ZOrder，绘制顺序就是父容器子控件列表的顺序（越靠后越靠上）。
    /// Flax 的 Control 也没有 BringToFront API —— 本仓库既有做法是把控件重新挂到父控件末尾
    /// （见 Source/Game/UI/Ink/Components/InkAttributeTooltip.cs:395），这里把它收敛成一个公用扩展，
    /// 供 CommonUI 的激活栈（WidgetStack / PromptStack）与字幕控件复用。
    /// </para>
    /// </summary>
    public static class NarrativeControlExtensions
    {
        /// <summary>
        /// 置顶：把控件移到父控件子列表末尾，使其绘制在最上层。对应 UE5 的 SetPanelWidgetZOrder(最大值)。
        /// </summary>
        public static void BringToFront(this Control control)
        {
            if (control == null) return;

            var parent = control.Parent;
            if (parent == null) return;

            // 已经在末尾则无需改动（避免无谓的层级变化事件）
            if (parent.ChildrenCount > 0 && parent.GetChild(parent.ChildrenCount - 1) == control) return;

            SetIndexInParent(control, parent.ChildrenCount - 1);
        }

        /// <summary>
        /// 置底：把控件移到父控件子列表开头，使其绘制在最下层。对应 UE5 的 SetPanelWidgetZOrder(最小值)。
        /// </summary>
        public static void SendToBack(this Control control)
        {
            if (control == null) return;

            var parent = control.Parent;
            if (parent == null) return;

            SetIndexInParent(control, 0);
        }

        /// <summary>
        /// 按索引重排：优先用 Flax 的 <see cref="Control.IndexInParent"/>，
        /// 不可用时退回"摘掉再挂回末尾"的写法（等价于重新加入父控件子列表）。
        /// </summary>
        private static void SetIndexInParent(Control control, int index)
        {
            var parent = control.Parent;
            if (parent == null) return;

            if (index < 0) index = 0;
            if (index > parent.ChildrenCount - 1) index = parent.ChildrenCount - 1;

            if (control.IndexInParent == index) return;

            control.IndexInParent = index;
        }
    }
}
