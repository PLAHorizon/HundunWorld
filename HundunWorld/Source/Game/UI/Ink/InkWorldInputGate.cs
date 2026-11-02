using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.CommonUI;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 把「Ink 菜单正覆盖着世界」同步给 NarrativePro 的 CommonUI 输入路由。
    /// <para>
    /// 世界侧交互提示（<c>[T] 上车</c> 之类）的显隐条件是
    /// <see cref="NarrativeInputRouter.bGameInputBlocked"/>，而该标志只由 NarrativePro 自己的
    /// 可激活控件栈驱动；Ink 页面不在这条栈上，于是菜单铺满屏幕时提示仍会浮在上面。
    /// 这里用一个不渲染任何内容的 <see cref="InkMenuInputBlocker"/> 代表"有一层 Ink 菜单"，
    /// 开菜单压栈、回战斗 HUD 弹栈，提示与游戏侧按键自然让位。
    /// </para>
    /// </summary>
    internal sealed class InkWorldInputGate
    {
        private readonly InkMenuInputBlocker _blocker = new InkMenuInputBlocker();
        private bool _menuOpen;

        /// <summary>
        /// 按路由器当前状态同步压栈/弹栈。每帧调用是安全的：状态没变时不做任何事。
        /// </summary>
        public void Sync(InkPageRouter router)
        {
            bool menuOpen = IsMenuOpen(router);
            if (menuOpen == _menuOpen)
            {
                if (menuOpen && _blocker.IsActivated && !NarrativeInputRouter.IsInStack(_blocker))
                {
                    // 场景切换会 NarrativeInputRouter.Reset() 清空栈，但我们的标志还停在"已压栈"。
                    NarrativeInputRouter.PushWidget(_blocker, ENarrativeWidgetInputMode.Menu);
                }
                return;
            }

            _menuOpen = menuOpen;
            if (menuOpen)
                _blocker.Activate();
            else
                _blocker.Deactivate();
        }

        /// <summary>UI 销毁时交还输入栈，避免留下一个永远不会弹栈的菜单标记。</summary>
        public void Release()
        {
            _menuOpen = false;
            if (_blocker.IsActivated)
                _blocker.Deactivate();
            NarrativeInputRouter.ReleaseWidget(_blocker);
        }

        /// <summary>
        /// 菜单层存在 = 叠了弹层，或当前页不是战斗 HUD（沉浸式/自带返回/自铺舞台都算菜单）。
        /// 页面类型问路由器登记的 frame，不在客户端另立一份 dom-id 名单。
        /// </summary>
        private static bool IsMenuOpen(InkPageRouter router)
        {
            if (router == null)
                return false;
            if (router.HasOverlay)
                return true;

            string current = router.CurrentPageDomId;
            if (string.IsNullOrEmpty(current))
                return false;
            return router.GetFrame(current).Kind != InkPageKind.Hud;
        }
    }

    /// <summary>
    /// 不渲染内容、只占输入栈一格的菜单标记：输入配置固定为 Menu，
    /// 让 <see cref="NarrativeInputRouter.bGameInputBlocked"/> 与"Ink 菜单开着"保持一致。
    /// </summary>
    internal sealed class InkMenuInputBlocker : NarrativeActivatableWidget
    {
        public InkMenuInputBlocker()
        {
            InputConfig = ENarrativeWidgetInputMode.Menu;
            bFocusDesiredTargetOnActivate = false;
            AnchorPreset = AnchorPresets.TopLeft;
            Location = Float2.Zero;
            Size = Float2.Zero;
            Visible = false;
            Enabled = false;
        }
    }
}
