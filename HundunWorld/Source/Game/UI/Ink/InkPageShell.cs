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

        /// <summary>叠加层（z-index 3，模态弹层挂载点，无弹层时整体不可见）</summary>
        private readonly ContainerControl _overlayLayer;

        /// <summary>设计空间缩放系数与铺满窗口后的设计尺寸（常驻部件复用同一坐标系）。</summary>
        private float _designScale = 1f;
        private Float2 _designSize = new Float2(UILayout.RefWidth, UILayout.RefHeight);

        /// <summary>
        /// 常驻部件挂载层：把控件直接添加为外壳的子节点（排在内容层之后 → 浮在页面之上）。
        /// 技能栏这类"打开任意菜单时仍要可见可拖"的部件走这里，坐标系与设计空间一致。
        /// </summary>
        public Control AddPersistentControl(Control control)
        {
            if (control == null) return null;
            AddChild(control);
            return control;
        }

        /// <summary>当前设计空间缩放系数（窗口像素 / 设计像素）。</summary>
        public float DesignScale => _designScale;

        /// <summary>按"铺满窗口"折算出的设计空间尺寸，与战斗 HUD 内容层同一坐标口径。</summary>
        public Float2 DesignSize => _designSize;

        /// <summary>粒子动效层（z-index 5，全屏覆盖层，不接收鼠标事件）</summary>
        private readonly InkParticleSystem _particleSystem;

        /// <summary>左上角返回按钮（z-index 10，最顶层）</summary>
        private readonly InkBackButton _backButton;

        /// <summary>当前挂载的页面（若实现 IInkPage 则缓存引用用于尺寸变化时刷新）</summary>
        private Control _currentPage;

        /// <summary>内容层是否顶部对齐（战斗 HUD 使用，见 <see cref="TopAnchoredContent"/>）</summary>
        private bool _topAnchoredContent;

        /// <summary>
        /// 内容层引用，供 <see cref="InkPageRouter"/> 挂载页面。
        /// </summary>
        public ContainerControl ContentLayer => _contentLayer;

        /// <summary>
        /// 叠加层引用：模态弹层的挂载点（位于内容层之上、粒子层之下）。
        /// 弹层叠在任意页面之上，关闭后底页仍在（不再像"弹窗即页面"那样把底页销毁）。
        /// </summary>
        public ContainerControl OverlayLayer => _overlayLayer;

        /// <summary>当前是否有弹层。</summary>
        public bool HasOverlay => _overlayLayer != null && _overlayLayer.ChildrenCount > 0;

        /// <summary>
        /// 内容层是否顶部对齐。
        /// <para>
        /// 设计空间固定 1920×1080，窗口比例非 16:9 时会多出上下留边：默认上下居中，
        /// 页面整体会比窗口上边缘低一段（留边高度）。战斗 HUD 需顶部对齐，
        /// 设计坐标 y=0 才贴合窗口上边缘（罗盘带 10px、边缘边距 12px 才是距屏幕的真实距离）。
        /// 16:9 及更宽的窗口下无上下留边，此开关无影响。
        /// </para>
        /// </summary>
        public bool TopAnchoredContent
        {
            get => _topAnchoredContent;
            set
            {
                if (_topAnchoredContent == value) return;
                _topAnchoredContent = value;
                UpdateContentLayout();
                if (_currentPage is IInkPage inkPage)
                {
                    inkPage.RefreshLayout();
                }
            }
        }

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
                //    设计坐标系：固定 1920×1080，运行时按窗口等比缩放并居中（随窗口尺寸自适应）——
                //    与登录页 LoginPanel.LayoutZones 采用同一机制，页面在固定设计空间中布局，
                //    缩放全部由本层承担。背景/暗角/粒子层仍铺满窗口、不参与缩放。
                _contentLayer = new ContainerControl
                {
                    Visible = true,
                    BackgroundColor = Color.Transparent,
                    ClipChildren = true,
                    AutoFocus = false,
                    Size = new Float2(UILayout.RefWidth, UILayout.RefHeight),
                    Pivot = new Float2(0.5f, 0.5f),
                };
                AddChild(_contentLayer);

                // 4. 叠加层（z-index 3，模态弹层挂载点）
                //    位于内容层之上、粒子层之下：弹层叠在任意页面之上，关闭后底页仍在。
                //    空置时整体 Visible=false，不会参与命中测试、不影响下层页面交互。
                _overlayLayer = new ContainerControl
                {
                    Visible = false,
                    BackgroundColor = Color.Transparent,
                    ClipChildren = false,
                    AutoFocus = false,
                    Size = new Float2(UILayout.RefWidth, UILayout.RefHeight),
                    Pivot = new Float2(0.5f, 0.5f),
                };
                AddChild(_overlayLayer);

                // 5. 粒子动效层（z-index 5，全屏覆盖层，不接收鼠标事件）
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

                // 6. 返回按钮（z-index 10，最顶层，初始隐藏）
                _backButton = new InkBackButton
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Visible = false,
                    Location = new Float2(BackButtonMargin, BackButtonMargin),
                };
                AddChild(_backButton);
                _backButton.Clicked += OnBackButtonClickedInternal;

                // 初始布局：构造期若已有尺寸则立即应用设计坐标系缩放（通常由 OnSizeChanged 在挂载后完成）。
                UpdateContentLayout();
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
        /// 把一个弹层控件挂到叠加层（先清空已有弹层）。弹层通常自带遮罩与面板，
        /// 底页保持挂载不被销毁。
        /// </summary>
        /// <param name="overlay">弹层根控件（会被拉伸铺满叠加层）</param>
        public void ShowOverlay(Control overlay)
        {
            try
            {
                if (_overlayLayer == null || overlay == null) return;

                HideOverlay();

                _overlayLayer.AddChild(overlay);
                overlay.AnchorPreset = AnchorPresets.StretchAll;
                overlay.Offsets = Margin.Zero;
                _overlayLayer.Visible = true;

                if (overlay is IInkPage inkPage)
                {
                    inkPage.RefreshLayout();
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] ShowOverlay 失败: {ex.Message}");
            }
        }

        /// <summary>清空并隐藏叠加层。</summary>
        public void HideOverlay()
        {
            try
            {
                if (_overlayLayer == null) return;

                while (_overlayLayer.ChildrenCount > 0)
                {
                    var child = _overlayLayer.GetChild(0);
                    _overlayLayer.RemoveChild(child);
                    child.Dispose();
                }

                _overlayLayer.Visible = false;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] HideOverlay 失败: {ex.Message}");
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
        /// 外部强制重排内容层（设计坐标系缩放 + 居中/贴边）。
        /// <para>
        /// 内容层缩放只在尺寸变化事件里重算，而"控件树建好时 Canvas 还是 0 尺寸"
        /// 这一窗口期内 <see cref="UpdateContentLayout"/> 会直接 return；
        /// 若之后尺寸没有再发生变化（画布已被预置成最终尺寸），就不会有任何事件来补算，
        /// 表现是界面缺一块/位置不对。场景切换完成时由外部调用本方法兜一次。
        /// </para>
        /// </summary>
        public void RefreshContentLayout()
        {
            UpdateContentLayout();
        }

        /// <summary>
        /// 常驻部件的挂载方式：直接作为外壳的子控件（与返回按钮同一手法），由外壳提供设计坐标系。
        /// <para>
        /// 不能再用一层铺满的容器去兜：那种整屏容器会拦在页面之前吸掉鼠标，
        /// 菜单页就点不动了。控件自带缩放变换，命中区域就等于它自己的可见区域。
        /// </para>
        /// </summary>
        public void PlaceInDesignSpace(Control target, Float2 designLocation)
        {
            if (target == null || _designScale <= 0f) return;

            float s = _designScale;
            target.AnchorPreset = AnchorPresets.TopLeft;
            target.Pivot = new Float2(0.5f, 0.5f);
            target.Scale = new Float2(s, s);
            target.Location = new Float2(
                designLocation.X * s - target.Size.X * (1f - s) * 0.5f,
                designLocation.Y * s - target.Size.Y * (1f - s) * 0.5f);
        }

        /// <summary>
        /// 依据外壳当前尺寸更新内容层缩放（设计坐标系 1920×1080 等比缩放并居中）。
        /// <para>
        /// 与登录页 <c>LoginPanel.LayoutZones</c> 同一机制：内容层固定设计分辨率、Pivot 居中、
        /// <c>Scale = min(W/1920, H/1080)</c>；页面在固定设计空间中布局（其 Width/Height 恒为
        /// 1920×1080），缩放全部由本层承担，保证 UI 随窗口尺寸变化等比缩放。
        /// 背景/暗角/粒子层保持铺满窗口、不参与缩放（任意窗口比例下全出血）。
        /// </para>
        /// </summary>
        private void UpdateContentLayout()
        {
            if (_contentLayer == null) return;
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f) return;

            float designW = UILayout.RefWidth;
            float designH = UILayout.RefHeight;
            float s = Mathf.Min(w / designW, h / designH);

            _contentLayer.Pivot = new Float2(0.5f, 0.5f);
            _contentLayer.Scale = new Float2(s, s);

            if (_topAnchoredContent)
            {
                // 战斗 HUD：内容层尺寸扩到"恰好填满窗口"（等比缩放 s 不变），使
                //   设计坐标 (0,0) = 窗口左上角、设计尺寸 = 窗口尺寸 / s。
                // 否则非 16:9 窗口下会留出边距带，导致底部/右侧锚定的 HUD 元素（聊天框/功能钮/导航栏）
                // 停在距窗口边缘一段距离处，无法真正贴边。
                var fill = new Float2(w / s, h / s);
                _contentLayer.Size = fill;
                _contentLayer.Location = new Float2(-(fill.X * (1f - s)) * 0.5f, -(fill.Y * (1f - s)) * 0.5f);
            }
            else
            {
                // 菜单页：固定设计空间 1920×1080，等比缩放后在窗口中居中（保留上下留边）。
                _contentLayer.Size = new Float2(designW, designH);
                _contentLayer.Location = new Float2((w - designW) * 0.5f, (h - designH) * 0.5f);
            }

            // 叠加层与内容层同一坐标系（同一缩放/尺寸/位置）：弹层里的控件用与页面一致的
            // 设计坐标排版，叠在任意页面上都对齐。
            if (_overlayLayer != null)
            {
                _overlayLayer.Pivot = new Float2(0.5f, 0.5f);
                _overlayLayer.Scale = new Float2(s, s);
                _overlayLayer.Size = _contentLayer.Size;
                _overlayLayer.Location = _contentLayer.Location;
            }

            // 返回按钮留在最顶层（粒子层之上）不参与内容层子树，但位置与尺寸跟随设计坐标系缩放。
            if (_backButton != null)
            {
                _backButton.Pivot = Float2.Zero;
                _backButton.Scale = new Float2(s, s);
                _backButton.Location = new Float2(
                    _contentLayer.Location.X + BackButtonMargin * s,
                    _contentLayer.Location.Y + BackButtonMargin * s);
            }

            // 同步全局缩放系数（UILayout.S/Font 供页面按需使用）。
            UILayout.UpdateScale(this);

            // 常驻部件（技能栏）恒按"铺满窗口"的 HUD 设计空间摆放，
            // 不跟随菜单页那套居中信箱，保证它在任何页面下都贴在窗口底部同一位置。
            _designScale = s;
            _designSize = new Float2(w / s, h / s);
        }

        /// <summary>
        /// 外壳尺寸变化（含窗口分辨率变化）时：重算内容层缩放并刷新当前页面布局。
        /// </summary>
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            try
            {
                UpdateContentLayout();
                if (_currentPage is IInkPage inkPage)
                {
                    inkPage.RefreshLayout();
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageShell] OnSizeChanged 处理失败: {ex.Message}");
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
                UpdateContentLayout();

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
