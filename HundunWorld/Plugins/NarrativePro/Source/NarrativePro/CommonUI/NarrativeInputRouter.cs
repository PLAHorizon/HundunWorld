using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// CommonUI 输入路由与激活栈。对应 UE5 中 UCommonUIGameViewExtension +
    /// UCommonInputSubsystem + CommonActivatableWidget 的输入配置栈合起来做的事：
    /// 谁在栈顶决定游戏输入是否被吃掉、以及是否显示按键图标。
    /// <para>
    /// Flax 没有对应的引擎子系统，因此把这套策略收敛在此静态路由器：
    /// 路由器只管键盘输入的屏蔽标志，其他系统（角色/相机）查询
    /// <see cref="bGameInputBlocked"/> 决定是否消费玩家输入。
    /// </para>
    /// <para>
    /// 光标不再由本路由器隐藏或捕获：鼠标锁定模式已取消，改为"焦点/悬停在 UI 上时
    /// 世界侧不响应鼠标"（判定在客户端 <c>HundunWorld.Game.UI.UIMouseCapture</c>）。
    /// </para>
    /// </summary>
    public static class NarrativeInputRouter
    {
        /// <summary>当前激活栈（末尾为栈顶）。对应 UE5 的 activatable widget 栈序。</summary>
        private static readonly List<NarrativeActivatableWidget> _stack = new List<NarrativeActivatableWidget>();

        /// <summary>是否有 UI 处于 Menu 模式（游戏输入应被屏蔽）。</summary>
        public static bool bGameInputBlocked { get; private set; }

        /// <summary>当前生效的输入模式（栈顶控件决定的结果）。</summary>
        public static ENarrativeWidgetInputMode CurrentInputMode { get; private set; } = ENarrativeWidgetInputMode.Default;

        /// <summary>当前栈顶控件。</summary>
        public static NarrativeActivatableWidget TopWidget => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>激活栈变化时触发（供 HUD 更新可见性/按键提示）。</summary>
        public static event Action<NarrativeActivatableWidget> OnStackChanged;

        /// <summary>把一个已激活控件压入栈。对应 UE5 CommonUI 的输入配置压栈。</summary>
        public static void PushWidget(NarrativeActivatableWidget widget, ENarrativeWidgetInputMode? desiredConfig)
        {
            if (widget == null) return;

            _stack.Remove(widget);
            _stack.Add(widget);

            // 记住本控件希望的模式，重算栈顶生效配置
            _desiredConfigs[widget] = desiredConfig;
            Recalculate();
            OnStackChanged?.Invoke(widget);
        }

        /// <summary>控件停用/销毁时从栈中移除并重算输入配置。对应 UE5 的输入配置弹栈。</summary>
        public static void ReleaseWidget(NarrativeActivatableWidget widget)
        {
            if (widget == null) return;

            int index = _stack.IndexOf(widget);
            if (index < 0)
            {
                _desiredConfigs.Remove(widget);
                return;
            }

            _stack.RemoveAt(index);
            _desiredConfigs.Remove(widget);
            Recalculate();
            OnStackChanged?.Invoke(_stack.Count > 0 ? _stack[_stack.Count - 1] : null);
        }

        /// <summary>控件是否仍在激活栈上。对应 UE5 IsActivated。</summary>
        public static bool IsInStack(NarrativeActivatableWidget widget) => widget != null && _stack.Contains(widget);

        /// <summary>栈深度。</summary>
        public static int StackDepth => _stack.Count;

        private static readonly Dictionary<NarrativeActivatableWidget, ENarrativeWidgetInputMode?> _desiredConfigs
            = new Dictionary<NarrativeActivatableWidget, ENarrativeWidgetInputMode?>();

        /// <summary>
        /// 是否应显示按键图标。对应 UE5 CommonInputSubsystem::ShouldShowInputKeys：
        /// 手柄输入时显示手柄按钮提示，键鼠输入时显示键位；这里以"是否连接手柄"作为等价判据，
        /// 并且只要有任何 UI 激活就一律显示（菜单需要给出操作提示）。
        /// </summary>
        public static bool ShouldShowInputKeys()
        {
            if (_stack.Count > 0) return true;

            try
            {
                return Input.GamepadsCount > 0;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>当前是否为手柄输入（供 action widget 选择键位还是手柄图标）。</summary>
        public static bool IsUsingGamepad()
        {
            try
            {
                return Input.GamepadsCount > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 依据栈顶控件的期望配置计算游戏输入屏蔽标志。
        /// UE5 中由 CommonUI 在每个 widget 激活/停用切换 Slate input mode 完成。
        /// </summary>
        private static void Recalculate()
        {
            ENarrativeWidgetInputMode effectiveMode = ENarrativeWidgetInputMode.Default;
            bool bFound = false;

            // 从栈顶往下找第一个显式声明了输入配置的控件
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (!_desiredConfigs.TryGetValue(_stack[i], out var config) || config == null) continue;

                effectiveMode = config.Value;
                bFound = true;
                break;
            }

            CurrentInputMode = bFound ? effectiveMode : ENarrativeWidgetInputMode.Default;
            bGameInputBlocked = bFound && effectiveMode == ENarrativeWidgetInputMode.Menu;
        }

        /// <summary>清空激活栈（切场景 / HUD 销毁时调用）。</summary>
        public static void Reset()
        {
            _stack.Clear();
            _desiredConfigs.Clear();
            CurrentInputMode = ENarrativeWidgetInputMode.Default;
            bGameInputBlocked = false;
            Recalculate();
        }
    }
}
