using System;
using System.Collections.Generic;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 3D 世界鼠标输入的统一门控：鼠标归 UI 时，世界侧（点击移动、实体选中、技能指示器、
    /// 相机右键旋转、滚轮缩放）一律不响应鼠标；鼠标不在 UI 上时世界照常响应。
    /// <para>
    /// 取代 NarrativePro 原先的鼠标锁定模式（<c>NarrativeInputRouter</c> 隐藏并捕获光标）：
    /// 光标现在始终自由，"谁该收这个鼠标事件"由这里按焦点/悬停裁决。
    /// </para>
    /// <para>
    /// 判定为"鼠标归 UI"的三种情形：整屏菜单/弹窗页；界面输入框持有键盘焦点（正在打字）；
    /// 光标悬停在会自行处理鼠标的控件上。第三种必须只认**可交互**控件——HUD 是全屏透明容器，
    /// 按"命中任何控件"判定会让场景里一次点击都选不中东西。
    /// </para>
    /// </summary>
    public static class UIMouseCapture
    {
        private const BindingFlags DeclaredOnlyInstance =
            BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Assembly EngineGuiAssembly = typeof(Control).Assembly;
        private static readonly Dictionary<Type, bool> InteractiveByType = new Dictionary<Type, bool>();

        /// <summary>鼠标此刻是否归 UI（世界侧应忽略本轮所有鼠标事件）。</summary>
        public static bool Captured
        {
            get
            {
                // CommonUI（NarrativePro）栈顶是 Menu 模式：它自己的面板接管鼠标。
                if (NarrativePro.CommonUI.NarrativeInputRouter.bGameInputBlocked)
                    return true;

                var ui = MainUIManager.Instance;
                if (ui == null) return false;

                if (!ui.IsOnCombatHudPage) return true;

                // 有模态弹层时世界侧不接鼠标（弹层遮罩本身不在可交互控件白名单内）。
                if (ui.IsOverlayOpen) return true;

                var root = ui.InkUiRoot;
                if (root == null) return false;

                // 正在输入框里打字时，鼠标事件不该被世界抢走。
                if (IsTextInputFocused) return true;

                var mouse = Input.MousePosition;
                var hit = root.GetChildAtRecursive(new Float2(mouse.X, mouse.Y));
                while (hit != null && hit != root)
                {
                    if (IsInteractive(hit))
                        return true;
                    hit = hit.Parent;
                }

                return false;
            }
        }

        /// <summary>
        /// 界面输入框是否持有键盘焦点（正在打字）。
        /// <para>
        /// 刻意不用 <c>root.ContainsFocus</c>：Flax 的 <c>Button</c>/<c>CheckBox</c>/<c>Slider</c> 在
        /// <c>OnMouseDown</c> 里会 <c>Focus()</c>，只要点过一次任意 HUD 按钮，ContainsFocus 就永远为真，
        /// 会把世界鼠标与 Esc 一起锁死。
        /// </para>
        /// </summary>
        public static bool IsTextInputFocused
        {
            get
            {
                var root = MainUIManager.Instance?.InkUiRoot;

                // 引擎的 `RootControl.FocusedControl` 读的是 `Parent?.Root.FocusedControl`：控件还没挂到窗口根上时
                // `Parent.Root` 是 null，读它直接抛 NRE。PIE 里跑没有建 UI 的场景（例如 World）就是这样，而这条
                // 属性是**每帧**从 `ThirdPersonCamera.OnUpdate` 里读的——一帧一条异常、穿过原生 interop thunk，
                // 几百条之后整个编辑器崩掉（WER: FlaxEditor.exe / 0xc0000409 / 栈 cookie）。
                // 没挂上就没有"谁在打字"可言，所以先判这一条再读焦点。
                if (!(root is RootControl canvasRoot) || canvasRoot.Parent?.Root == null)
                    return false;

                return canvasRoot.FocusedControl is TextBoxBase;
            }
        }

        /// <summary>把键盘焦点从输入框上摘掉（ESC 的第一层语义：先退出打字，不顺手关页面）。</summary>
        public static void ReleaseTextInputFocus()
        {
            if (MainUIManager.Instance?.InkUiRoot is RootControl canvasRoot && canvasRoot.Parent?.Root != null)
                canvasRoot.FocusedControl?.Defocus();
        }

        /// <summary>
        /// 键盘操控是否归 UI：正在打字、盖着模态弹层、或停在整屏页面上时，
        /// 世界侧的 WASD/跳跃一类操控输入应当清零。
        /// <para>
        /// 与 <see cref="Captured"/>（鼠标）成对。ESC 系统菜单的"暂停"就落在这里——
        /// 没有消费者的暂停态只是换个标题的假动作。
        /// </para>
        /// </summary>
        public static bool KeyboardCaptured
        {
            get
            {
                var ui = MainUIManager.Instance;
                if (ui == null) return false;
                if (IsTextInputFocused) return true;
                if (ui.IsOverlayOpen) return true;
                if (!ui.IsOnCombatHudPage) return true;
                return false;
            }
        }

        private static bool IsInteractive(Control control)        {
            var type = control.GetType();
            if (InteractiveByType.TryGetValue(type, out var cached))
                return cached;

            bool interactive = IsEngineInteractive(type) || DeclaresMouseHandling(type);
            InteractiveByType[type] = interactive;
            return interactive;
        }

        private static bool IsEngineInteractive(Type type)
        {
            return typeof(Button).IsAssignableFrom(type)
                   || typeof(TextBoxBase).IsAssignableFrom(type)
                   || typeof(Slider).IsAssignableFrom(type)
                   || typeof(CheckBox).IsAssignableFrom(type)
                   || typeof(Dropdown).IsAssignableFrom(type)
                   || typeof(ScrollBar).IsAssignableFrom(type);
        }

        /// <summary>
        /// 工程自己的控件只要重写过鼠标回调就算可交互（技能格、滚轮列表、3D 预览拖拽等），
        /// 不必再维护一份类型白名单。引擎侧的 <see cref="ContainerControl"/> / Panel 也重写了
        /// 这些方法，但那只是向子节点分发，不算接管鼠标，故遇到引擎类型即停。
        /// </summary>
        private static bool DeclaresMouseHandling(Type type)
        {
            for (var t = type; t != null && t.Assembly != EngineGuiAssembly; t = t.BaseType)
            {
                if (t.GetMethod("OnMouseDown", DeclaredOnlyInstance) != null ||
                    t.GetMethod("OnMouseWheel", DeclaredOnlyInstance) != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
