using System;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 水墨页面接口。
    /// 所有 Ink 页面应实现此接口，供 <see cref="InkPageShell"/> 在页面挂载后
    /// 与父容器尺寸变化时统一调用 <see cref="RefreshLayout"/> 刷新布局。
    /// </summary>
    public interface IInkPage
    {
        /// <summary>
        /// 基于当前控件实际尺寸刷新子控件布局。
        /// 应在页面挂载到父容器后调用，以及父容器尺寸变化时调用。
        /// 实现应使用 <c>this.Width</c>/<c>this.Height</c> 而非 <c>Screen.Size</c> 计算布局。
        /// </summary>
        void RefreshLayout();
    }
}
