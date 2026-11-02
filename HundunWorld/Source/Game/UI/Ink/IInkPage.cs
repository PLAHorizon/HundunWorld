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

    /// <summary>
    /// 会发起页面跳转的 Ink 页面。页面声明 <see cref="NavigationRequested"/> 后必须实现本接口，
    /// <c>MainUIManager</c> 才能以**编译期**订阅把它接到路由器上。
    /// <para>
    /// 取代此前用反射按名字找 <c>"NavigationRequested"</c> 事件的写法：那种做法在事件改名、
    /// 页面漏订时会**静默**失效（按钮点了没反应，日志里什么都没有），编译器管不住。
    /// </para>
    /// </summary>
    public interface IInkNavigationSource
    {
        /// <summary>页面请求跳转（参数为目标 dom-id，取值见 <c>InkPageDomIds</c>）。</summary>
        event Action<string> NavigationRequested;
    }
}
