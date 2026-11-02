using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Ink.ActionBar
{
    /// <summary>
    /// 数字键施放的输入闸门：只要有文本框正持有焦点，1~0 就属于输入内容而不是施放指令。
    /// 只在按下数字键的那一帧做一次树遍历，平时零成本。
    /// </summary>
    public static class ActionBarInputGate
    {
        /// <summary>水墨 UI 的画布根控件，由 MainUIManager 在建好 Canvas 后注入。</summary>
        public static Control UiRoot { get; set; }

        public static bool IsTextEntryFocused() => HasFocusedTextBox(UiRoot);

        private static bool HasFocusedTextBox(Control control)
        {
            if (control == null || !control.Visible) return false;
            if (control is TextBox textBox && textBox.IsFocused) return true;

            // 只有容器才有子控件；TextBox 自身不承载界面，不必下钻。
            if (control is not ContainerControl container) return false;

            int children = container.ChildrenCount;
            for (int i = 0; i < children; i++)
            {
                if (HasFocusedTextBox(container.GetChild(i))) return true;
            }
            return false;
        }
    }
}
