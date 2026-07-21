using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 水墨页面外壳。
    /// 作为所有页面的统一挂载基础设施，承载五层子控件：
    /// <list type="bullet">
    ///   <item>背景层（<see cref="InkBackgroundLayer"/>，z-index 0，不接收鼠标事件）</item>
    ///   <item>暗角晕影层（<see cref="InkVignette"/>，z-index 1，不接收鼠标事件）</item>
    ///   <item>内容层（<see cref="ContainerControl"/>，z-index 2，页面挂载点）</item>
    ///   <item>粒子动效层（<see cref="InkParticleSystem"/>，z-index 5，全屏覆盖层，不接收鼠标事件）</item>
    ///   <item>返回按钮（<see cref="InkBackButton"/>，z-index 10，左上角，仅非战斗 HUD 页面显示）</item>
    /// </list>
    /// 子控件按添加顺序确定 z-index（后添加者在上层）。
    /// 粒子层位于内容层之上、返回按钮之下，既能在所有页面之上播放粒子动效，
    /// 又不会拦截按钮交互（<see cref="InkParticleSystem.Enabled"/>=false）。
    /// </summary>
    public class InkPageShell : ContainerControl
    {
        /// <summary>返回按钮左上角边距（像素）</summary>
        private const float BackButtonMargin = 16f;

        /// <summary>全局水墨背景层（z-index 0，最底层）</summary>
        private readonly InkBackgroundLayer _backgroundLayer;

        /// <summary>暗角晕影层（z-index 1）</summary>
        private readonly InkVignette _vignette;

        /// <summary>内容层（z-index 2，页面挂载点）</summary>
        private readonly ContainerControl _contentLayer;

        /// <summary>粒子动效层（z-index 5，全屏覆盖层，不接收鼠标事件）</summary>
        private readonly InkParticleSystem _particleSystem;

        /// <summary>左上角返回按钮（z-index 10，最顶层）</summary>
        private readonly InkBackButton _backButton;

        /// <summary>当前挂载的页面（若实现 IInkPage 则缓存引用用于尺寸变化时刷新）</summary>
        private Control _currentPage;

        /// <summary>
        /// 内容层引用，供 <see cref="InkPageRouter"/> 挂载页面。
        /// </summary>
        public ContainerControl ContentLayer => _contentLayer;

        /// <summary>
        /// 粒子动效系统引用。
        /// 供 <see cref="MainUIManager"/> 在初始化时调用
        /// <see cref="InkParticleSystem.Initialize(InkPageRouter)"/> 订阅 PanelShow 事件，
        /// 以及供按钮等交互元素调用 <see cref="InkParticleSystem.EmitGoldBurst"/> 反馈。
        /// </summary>
        public InkParticleSystem ParticleSystem => _particleSystem;

        /// <summary>
        /// 返回按钮点击事件。
        /// 由 <see cref="InkPageRouter"/> 在 <c>Initialize</c> 中订阅，
        /// 触发后调用 <c>NavigateToHud</c> 返回战斗 HUD。
        /// </summary>
        public event Action BackButtonClicked;

        /// <summary>
        /// 构造函数：初始化三层 + 返回按钮。
        /// <para>
        /// 背景层与暗角层 <see cref="Control.Visible"/> = true 且不接收鼠标事件
        /// （通过 <see cref="ContainerControl.ClipChildren"/> = false 配置）；
        /// 内容层 <see cref="ContainerControl.ClipChildren"/> = true 避免页面内容溢出；
        /// 返回按钮初始 <see cref="Control.Visible"/>=false，由 <see cref="ShowBackButton"/> 控制。
        /// </para>
        /// </summary>
        public InkPageShell()
        {
            try
            {
                // 外壳本身：全屏拉伸 + 透明背景 + 不裁剪子控件
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                // 1. 背景层（z-index 0，最底层，不接收鼠标事件）
                // 默认隐藏：HUD 页面叠加在游戏场景之上，不能遮挡；非 HUD 菜单页由路由器显式开启。
                _backgroundLayer = new InkBackgroundLayer
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Visible = false,
                    ClipChildren = false,
                    AutoFocus = false,
                };
                AddChild(_backgroundLayer);

                // 2. 暗角晕影层（z-index 1，不接收鼠标事件）
                // 默认隐藏：与背景层一致，仅非 HUD 菜单页需要暗角聚焦效果。
                _vignette = new InkVignette
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Visible = false,
                    ClipChildren = false,
                    AutoFocus = false,
                };
                AddChild(_vignette);

                // 3. 内容层（z-index 2，页面挂载点，裁剪子控件避免溢出）
                _contentLayer = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Visible = true,
                    BackgroundColor = Color.Transparent,
                    ClipChildren = true,
                    AutoFocus = false,
                };
                AddChild(_contentLayer);

                // 4. 粒子动效层（z-index 5，全屏覆盖层，不接收鼠标事件）
                //    位于内容层之上、返回按钮之下，播放金粉/涟漪/萤光/环境微粒。
                //    Enabled=false 防止拦截鼠标，Draw 仍会被引擎调用进行自定义渲染。
                //    Visible 默认 true，由 InkPageRouter 在导航时通过粒子系统自动触发涟漪。
                _particleSystem = new InkParticleSystem
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Visible = true,
                };
                AddChild(_particleSystem);

                // 5. 返回按钮（z-index 10，最顶层，初始隐藏）
                _backButton = new InkBackButton
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Visible = false,
                    Location = new Float2(BackButtonMargin, BackButtonMargin),
                };
                AddChild(_backButton);
                _backButton.Clicked += OnBackButtonClickedInternal;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 将页面控件添加到内容层（先调用 <see cref="UnloadPage"/> 清空当前页面）。
        /// 页面控件会被自动设置为全屏拉伸（<see cref="AnchorPresets.StretchAll"/>）。
        /// </summary>
        /// <param name="page">要挂载的页面控件</param>
        public void LoadPage(Control page)
        {
            try
            {
                if (page == null)
                {
                    FlaxEngine.Debug.LogWarning("[InkPageShell] LoadPage 收到 null 页面，已忽略");
                    return;
                }

                // 先清空当前页面
                UnloadPage();

                // 先挂载到内容层，再设置锚点，确保锚点偏移量基于真实父容器尺寸计算
                _contentLayer.AddChild(page);
                page.AnchorPreset = AnchorPresets.StretchAll;
                page.Offsets = Margin.Zero;

                // 缓存当前页面引用
                _currentPage = page;

                FlaxEngine.Debug.Log($"[InkPageShell] LoadPage 挂载: {page.GetType().Name}, " +
                    $"ContentLayer.Size={_contentLayer.Size}, page.Size={page.Size}, page.Width={page.Width}, page.Height={page.Height}");

                // 挂载后调用 RefreshLayout 刷新布局（页面此时有真实父容器尺寸）
                if (page is IInkPage inkPage)
                {
                    try
                    {
                        inkPage.RefreshLayout();
                        FlaxEngine.Debug.Log($"[InkPageShell] {page.GetType().Name}.RefreshLayout 完成, " +
                            $"page.Size={page.Size}, ChildrenCount={(page as ContainerControl)?.ChildrenCount ?? -1}");
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[InkPageShell] 页面 RefreshLayout 失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] LoadPage 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从内容层移除并销毁当前页面控件。
        /// 内容层只承载一个活动页面，调用后会清空所有子控件并释放其资源。
        /// </summary>
        public void UnloadPage()
        {
            try
            {
                _currentPage = null;
                // 内容层只承载当前活动页面，移除并销毁所有子控件
                while (_contentLayer.ChildrenCount > 0)
                {
                    var child = _contentLayer.GetChild(0);
                    _contentLayer.RemoveChild(child);
                    child.Dispose();
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] UnloadPage 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示/隐藏左上角返回按钮。
        /// </summary>
        /// <param name="show">true=显示返回按钮；false=隐藏返回按钮</param>
        public void ShowBackButton(bool show)
        {
            try
            {
                if (_backButton != null)
                    _backButton.Visible = show;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] ShowBackButton 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示/隐藏全局水墨背景层。
        /// 战斗 HUD 应隐藏以避免遮挡游戏场景；菜单页面通常显示。
        /// </summary>
        /// <param name="show">true=显示背景层；false=隐藏背景层</param>
        public void ShowBackgroundLayer(bool show)
        {
            try
            {
                if (_backgroundLayer != null)
                    _backgroundLayer.Visible = show;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] ShowBackgroundLayer 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示/隐藏暗角晕影层。
        /// 战斗 HUD 应隐藏；菜单页面通常显示以营造聚焦效果。
        /// </summary>
        /// <param name="show">true=显示晕影层；false=隐藏晕影层</param>
        public void ShowVignette(bool show)
        {
            try
            {
                if (_vignette != null)
                    _vignette.Visible = show;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] ShowVignette 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 返回按钮 <see cref="InkBackButton.Clicked"/> 事件的内部转发，
        /// 触发 <see cref="BackButtonClicked"/> 事件通知外部订阅者（如 <see cref="InkPageRouter"/>）。
        /// </summary>
        private void OnBackButtonClickedInternal()
        {
            try
            {
                BackButtonClicked?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] BackButtonClicked 事件转发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 父容器尺寸变化时，通知当前活动页面刷新布局。
        /// </summary>
        public override void OnParentResized()
        {
            base.OnParentResized();

            try
            {
                UILayout.UpdateScale(this);

                if (_currentPage is IInkPage inkPage)
                {
                    inkPage.RefreshLayout();
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] OnParentResized 转发失败: {ex.Message}");
            }
        }
    }
}
