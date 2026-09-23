using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 加载页面 1（首屏加载）。
    /// 对应设计稿 <c>hundun-yy-ui/pages/loading-1.html</c>「混沌世界 · 加载·山水」：
    /// 全屏山水水墨背景 + 右上角品牌水印「混沌世界」+ 鎏金晕染装饰 +
    /// 左侧竖排书法诗句「千山鸟飞绝 / 万径人踪灭」与落款「江雪·柳宗元」、朱红印章「墨」+
    /// 底部鎏金进度区（旋转图标 + 「正在踏入江湖」+ 进度条 + 进度数值 + 轮播江湖提示 + 三点脉动 + 版本号）。
    /// 进度满时触发 <see cref="ProgressComplete"/> 事件，
    /// 由 <see cref="InkPageRouter"/> 订阅后自动推进到下一页（LoadingPage2 或 ChapterTransitionPage）。
    /// </summary>
    public class LoadingPage1 : ContainerControl, IInkPage
    {
        /// <summary>自动递增进度的速率（每秒 +0.1，对应 10 秒满）</summary>
        private const float AutoProgressRate = 0.1f;

        /// <summary>设计稿文档标题（作为水印控件的悬停提示）</summary>
        private const string PageTitle = "混沌世界 · 加载·山水";

        /// <summary>右上角品牌水印文案</summary>
        private const string WatermarkLabel = "混沌世界";

        /// <summary>竖排诗句第一列</summary>
        private const string PoemLineOne = "千山鸟飞绝";

        /// <summary>竖排诗句第二列</summary>
        private const string PoemLineTwo = "万径人踪灭";

        /// <summary>诗句落款</summary>
        private const string PoemAuthorLabel = "江雪·柳宗元";

        /// <summary>朱红印章文字</summary>
        private const string PoemSealLabel = "墨";

        /// <summary>进度区左侧说明文案</summary>
        private const string ProgressLabelText = "正在踏入江湖";

        /// <summary>右下角版本号</summary>
        private const string VersionLabelText = "VER 1.0.0 · CN";

        /// <summary>江湖提示轮播文案（对应设计稿脚本中的 tips 数组，按进度分段切换）</summary>
        private static readonly string[] LoadingTips =
        {
            "江湖路远，且行且珍惜",
            "刀光剑影中，方显英雄本色",
            "一壶浊酒喜相逢，古今多少事",
            "天下风云出我辈，一入江湖岁月催",
            "仗剑天涯路，何处不相逢",
        };

        /// <summary>水印距屏幕右边缘偏移</summary>
        private const float WatermarkRightOffset = 48f;

        /// <summary>水印距屏幕顶部偏移</summary>
        private const float WatermarkTopOffset = 32f;

        /// <summary>晕染装饰尺寸</summary>
        private const float GlowSize = 200f;

        /// <summary>晕染装饰水平位置比例（right: 8%）</summary>
        private const float GlowRightRatio = 0.92f;

        /// <summary>晕染装饰垂直位置比例（top: 30%）</summary>
        private const float GlowTopRatio = 0.30f;

        /// <summary>诗句距屏幕左边缘偏移</summary>
        private const float PoemLeftOffset = 80f;

        /// <summary>每 20% 进度切换一条提示</summary>
        private const float TipProgressStep = 0.2f;

        /// <summary>水印整体不透明度（对应设计稿 opacity: 0.5）</summary>
        private const float WatermarkOpacity = 0.5f;

        /// <summary>诗句字号（对应设计稿 font-size: 28px）</summary>
        private const float PoemTitleFontSize = 28f;

        /// <summary>印章字号（对应设计稿 font-size: 16px）</summary>
        private const float PoemSealFontSize = 16f;

        /// <summary>印章边长（对应设计稿 36x36）</summary>
        private const float PoemSealSize = 36f;

        /// <summary>印章距诗句行的上边距（对应设计稿 margin-top: 16px）</summary>
        private const float PoemSealTop = 176f;

        /// <summary>暗角晕影边缘不透明度（对应设计稿 .ink-vignette rgba(0,0,0,0.55)）</summary>
        private const float VignetteEdgeOpacity = 0.55f;

        /// <summary>屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>全屏背景图控件</summary>
        private readonly Image _backgroundImage;

        /// <summary>暗角晕影层</summary>
        private readonly InkVignette _vignette;

        /// <summary>鎏金晕染装饰</summary>
        private readonly LoadingGlow _splashGlow;

        /// <summary>右上角品牌水印</summary>
        private readonly LoadingWatermark _watermark;

        /// <summary>左侧竖排诗句组</summary>
        private readonly LoadingPoemGroup _poemGroup;

        /// <summary>底部加载进度区</summary>
        private readonly LoadingBottomBar _bottomBar;

        /// <summary>当前进度值（0.0~1.0）</summary>
        private float _progress;

        /// <summary>是否已触发完成事件（避免重复触发）</summary>
        private bool _completed;

        /// <summary>当前提示文案下标</summary>
        private int _tipIndex;

        /// <summary>
        /// 进度满时触发的事件。
        /// 由 <see cref="InkPageRouter"/> 订阅后自动推进到下一页。
        /// 仅在进度首次到达 1.0 时触发一次。
        /// </summary>
        public event Action ProgressComplete;

        /// <summary>
        /// 构造函数：初始化加载页面 1。
        /// 读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// 异步加载 <see cref="InkWashTheme.TexAssetPathLoadingLandscape"/> 纹理作为背景，
        /// 加载失败时记录日志但不抛异常，页面背景回退为 <see cref="InkWashTheme.BaseDefault"/>。
        /// </summary>
        public LoadingPage1()
        {
            _screenSize = LoadingPageHelper.ResolveScreenSize();

            // 外壳本身：全屏拉伸 + 深墨黑兜底背景（纹理加载失败时仍可见）
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            // 1. 全屏背景图（山水）
            _backgroundImage = LoadingPageHelper.CreateBackgroundImage(
                InkWashTheme.TexAssetPathLoadingLandscape, nameof(LoadingPage1), _screenSize);
            AddChild(_backgroundImage);

            // 2. 暗角晕影（中心透明、边缘压暗）
            _vignette = new InkVignette
            {
                AnchorPreset = AnchorPresets.TopLeft,
                EdgeOpacity = VignetteEdgeOpacity,
            };
            AddChild(_vignette);

            // 3. 鎏金晕染装饰（柔和光斑）
            _splashGlow = new LoadingGlow
            {
                GlowColor = InkWashTheme.GoldPrimary,
                GlowAlpha = 0.08f,
                AnchorPreset = AnchorPresets.TopLeft,
                Enabled = false,
            };
            AddChild(_splashGlow);

            // 4. 右上角品牌水印：登山图标 + 「混沌世界」
            _watermark = new LoadingWatermark(
                WatermarkLabel, LoadingGlyphKind.MountainSnow, PageTitle,
                InkWashTheme.TextBrand, WatermarkOpacity)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_watermark);

            // 5. 左侧竖排书法诗句 + 落款 + 朱红印章
            _poemGroup = new LoadingPoemGroup(
                PoemLineOne, PoemLineTwo, PoemAuthorLabel, PoemSealLabel,
                PoemTitleFontSize, PoemSealFontSize, PoemSealSize, PoemSealTop)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_poemGroup);

            // 6. 底部加载进度区（旋转 loader 图标 + 说明 + 进度条 + 数值 + 提示 + 脉动点 + 版本号）
            _bottomBar = new LoadingBottomBar(
                LoadingGlyphKind.Loader, LoadingGlyphKind.BookOpen, true, false)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _bottomBar.ProgressLabelText = ProgressLabelText;
            _bottomBar.TipText = LoadingTips[0];
            _bottomBar.VersionText = VersionLabelText;
            AddChild(_bottomBar);

            // 应用初始布局
            ApplyLayout();
        }

        /// <summary>
        /// 更新进度条与数值。
        /// value 会被钳制到 0-1 范围；value &gt;= 1 时触发 <see cref="ProgressComplete"/> 事件（仅触发一次）。
        /// 同时按进度分段（每 20%）切换底部江湖提示文案。
        /// </summary>
        /// <param name="value">进度值（0-1）</param>
        public void SetProgress(float value)
        {
            LoadingPageHelper.SetProgressInternal(
                _bottomBar != null ? _bottomBar.ProgressBar : null,
                _bottomBar != null ? _bottomBar.ProgressPctLabel : null,
                ref _progress, ref _completed,
                value, ProgressComplete, nameof(LoadingPage1));

            UpdateTipByProgress();
        }

        /// <summary>
        /// 根据当前进度切换提示文案（对应设计稿 newTipIndex = progress / 20）。
        /// </summary>
        private void UpdateTipByProgress()
        {
            if (_bottomBar == null)
                return;

            int index = Mathf.Clamp((int)(_progress / TipProgressStep), 0, LoadingTips.Length - 1);
            if (index == _tipIndex)
                return;

            _tipIndex = index;
            _bottomBar.TipText = LoadingTips[index];
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 外部（如 <see cref="InkPageShell"/> 或屏幕大小变更监听器）应调用此方法。
        /// </summary>
        public void RefreshLayout()
        {
            _screenSize = LoadingPageHelper.ResolveLayoutSize(Width, Height);
            ApplyLayout();
        }

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置与尺寸。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_backgroundImage != null)
            {
                _backgroundImage.Location = Float2.Zero;
                _backgroundImage.Size = _screenSize;
            }

            if (_vignette != null)
            {
                _vignette.Location = Float2.Zero;
                _vignette.Size = _screenSize;
            }

            // 晕染装饰：top 30% / right 8%
            if (_splashGlow != null)
            {
                _splashGlow.Size = new Float2(GlowSize, GlowSize);
                _splashGlow.Location = new Float2(sw * GlowRightRatio - GlowSize, sh * GlowTopRatio);
            }

            // 品牌水印：右上角
            if (_watermark != null)
            {
                _watermark.Location = new Float2(
                    sw - WatermarkRightOffset - _watermark.Width, WatermarkTopOffset);
            }

            // 诗句组：左侧垂直居中
            if (_poemGroup != null)
            {
                _poemGroup.Location = new Float2(
                    PoemLeftOffset, sh * 0.5f - _poemGroup.Height * 0.5f);
            }

            // 底部加载进度区：贴底通栏
            if (_bottomBar != null)
            {
                _bottomBar.Location = new Float2(0f, sh - LoadingBottomBar.BarAreaHeight);
                _bottomBar.Size = new Float2(sw, LoadingBottomBar.BarAreaHeight);
                _bottomBar.ApplyLayout();
            }
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            // 已完成后停止自动递增，避免重复触发事件
            if (_completed)
                return;
            // 模拟加载：每秒 +0.1
            SetProgress(_progress + AutoProgressRate * deltaTime);
        }
    }

    // =======================================================================

    /// <summary>
    /// 加载页面 2（场景切换加载）。
    /// 对应设计稿 <c>hundun-yy-ui/pages/loading-2.html</c>「混沌世界 · 加载·远峰」：
    /// 全屏远峰水墨背景 + 左上角品牌水印「混沌世界」+ 古铜晕染装饰 +
    /// 居中双层旋转金环（环心「峰」）+ 副标题「远峰入云 · 破境中」+
    /// 右侧竖排书法诗句「会当凌绝顶 / 一览众山小」与落款「望岳·杜甫」、朱红印章「岳」+
    /// 底部鎏金进度区（旋转图标 + 「正在凝聚内力」+ 进度条 + 进度数值 + 提示 + 版本号 + 加载完成后的「点击继续」）。
    /// 进度满时触发 <see cref="ProgressComplete"/> 事件，
    /// 由 <see cref="InkPageRouter"/> 订阅后自动推进到下一页。
    /// </summary>
    public class LoadingPage2 : ContainerControl, IInkPage
    {
        /// <summary>自动递增进度的速率（每秒 +0.1，对应 10 秒满）</summary>
        private const float AutoProgressRate = 0.1f;

        /// <summary>设计稿文档标题（作为水印控件的悬停提示）</summary>
        private const string PageTitle = "混沌世界 · 加载·远峰";

        /// <summary>左上角品牌水印文案</summary>
        private const string WatermarkLabel = "混沌世界";

        /// <summary>竖排诗句第一列</summary>
        private const string PoemLineOne = "会当凌绝顶";

        /// <summary>竖排诗句第二列</summary>
        private const string PoemLineTwo = "一览众山小";

        /// <summary>诗句落款</summary>
        private const string PoemAuthorLabel = "望岳·杜甫";

        /// <summary>朱红印章文字</summary>
        private const string PoemSealLabel = "岳";

        /// <summary>旋转金环中心字</summary>
        private const string RingCenterLabel = "峰";

        /// <summary>金环下方副标题</summary>
        private const string RingSubtitleLabel = "远峰入云 · 破境中";

        /// <summary>进度区左侧说明文案</summary>
        private const string ProgressLabelText = "正在凝聚内力";

        /// <summary>右下角版本号</summary>
        private const string VersionLabelText = "VER 1.0.0 · CN";

        /// <summary>底部「点击继续」提示文案</summary>
        private const string ContinueHintLabel = "点击继续";

        /// <summary>进度满且延迟结束后的提示文案</summary>
        private const string CompletedTipLabel = "内力凝聚完毕，江湖已为你敞开";

        /// <summary>点击「点击继续」后的提示文案</summary>
        private const string EnteringWorldTipLabel = "正在进入江湖...";

        /// <summary>登高主题提示轮播文案（对应设计稿脚本中的 tips 数组，按进度分段切换）</summary>
        private static readonly string[] LoadingTips =
        {
            "山高水长，江湖路远",
            "登高望远，方知天地之阔",
            "云深不知处，只在此山中",
            "剑气纵横三万里，一剑光寒十九州",
            "前路漫漫，侠义长存",
        };

        /// <summary>水印距屏幕左边缘偏移</summary>
        private const float WatermarkLeftOffset = 48f;

        /// <summary>水印距屏幕顶部偏移</summary>
        private const float WatermarkTopOffset = 32f;

        /// <summary>水印整体不透明度（对应设计稿 opacity: 0.5）</summary>
        private const float WatermarkOpacity = 0.5f;

        /// <summary>晕染装饰尺寸</summary>
        private const float GlowSize = 220f;

        /// <summary>晕染装饰水平位置比例（left: 8%）</summary>
        private const float GlowLeftRatio = 0.08f;

        /// <summary>晕染装饰垂直位置比例（top: 20%）</summary>
        private const float GlowTopRatio = 0.20f;

        /// <summary>旋转金环尺寸</summary>
        private const float RingSize = 120f;

        /// <summary>金环中心的垂直位置比例（top: 42%）</summary>
        private const float RingCenterTopRatio = 0.42f;

        /// <summary>金环与副标题间距</summary>
        private const float RingSubtitleGap = 24f;

        /// <summary>副标题宽度</summary>
        private const float RingSubtitleWidth = 320f;

        /// <summary>副标题高度</summary>
        private const float RingSubtitleHeight = 24f;

        /// <summary>诗句距屏幕右边缘偏移</summary>
        private const float PoemRightOffset = 80f;

        /// <summary>诗句字号</summary>
        private const float PoemTitleFontSize = 26f;

        /// <summary>印章字号</summary>
        private const float PoemSealFontSize = 15f;

        /// <summary>印章边长</summary>
        private const float PoemSealSize = 34f;

        /// <summary>印章距诗句行的上边距</summary>
        private const float PoemSealTop = 174f;

        /// <summary>每 20% 进度切换一条提示</summary>
        private const float TipProgressStep = 0.2f;

        /// <summary>进度满后显示「点击继续」的延迟（对应设计稿 setTimeout 400ms）</summary>
        private const float CompletedHintDelay = 0.4f;

        /// <summary>暗角晕影边缘不透明度（对应设计稿 .ink-vignette rgba(0,0,0,0.55)）</summary>
        private const float VignetteEdgeOpacity = 0.55f;

        /// <summary>屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>全屏背景图控件</summary>
        private readonly Image _backgroundImage;

        /// <summary>暗角晕影层</summary>
        private readonly InkVignette _vignette;

        /// <summary>古铜晕染装饰</summary>
        private readonly LoadingGlow _splashGlow;

        /// <summary>左上角品牌水印</summary>
        private readonly LoadingWatermark _watermark;

        /// <summary>居中双层旋转金环</summary>
        private readonly LoadingRingSpinner _ringSpinner;

        /// <summary>金环副标题</summary>
        private readonly Label _ringSubtitle;

        /// <summary>右侧竖排诗句组</summary>
        private readonly LoadingPoemGroup _poemGroup;

        /// <summary>底部加载进度区</summary>
        private readonly LoadingBottomBar _bottomBar;

        /// <summary>当前进度值（0.0~1.0）</summary>
        private float _progress;

        /// <summary>是否已触发完成事件（避免重复触发）</summary>
        private bool _completed;

        /// <summary>当前提示文案下标</summary>
        private int _tipIndex;

        /// <summary>进度满后的延迟计时</summary>
        private float _completedHintTimer;

        /// <summary>是否已显示「点击继续」提示</summary>
        private bool _continueHintShown;

        /// <summary>
        /// 进度满时触发的事件。
        /// 由 <see cref="InkPageRouter"/> 订阅后自动推进到下一页。
        /// 仅在进度首次到达 1.0 时触发一次。
        /// </summary>
        public event Action ProgressComplete;

        /// <summary>
        /// 用户点击底部「点击继续」时触发的事件。
        /// 供外部（如页面路由）在需要玩家确认后再推进页面时订阅。
        /// </summary>
        public event Action ContinueRequested;

        /// <summary>
        /// 构造函数：初始化加载页面 2。
        /// 读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// 异步加载 <see cref="InkWashTheme.TexAssetPathLoadingMountain"/> 纹理作为背景，
        /// 加载失败时记录日志但不抛异常，页面背景回退为 <see cref="InkWashTheme.BaseDefault"/>。
        /// </summary>
        public LoadingPage2()
        {
            _screenSize = LoadingPageHelper.ResolveScreenSize();

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            // 1. 全屏背景图（远峰）
            _backgroundImage = LoadingPageHelper.CreateBackgroundImage(
                InkWashTheme.TexAssetPathLoadingMountain, nameof(LoadingPage2), _screenSize);
            AddChild(_backgroundImage);

            // 2. 暗角晕影
            _vignette = new InkVignette
            {
                AnchorPreset = AnchorPresets.TopLeft,
                EdgeOpacity = VignetteEdgeOpacity,
            };
            AddChild(_vignette);

            // 3. 古铜晕染装饰
            _splashGlow = new LoadingGlow
            {
                GlowColor = InkWashTheme.BronzePrimary,
                GlowAlpha = 0.06f,
                AnchorPreset = AnchorPresets.TopLeft,
                Enabled = false,
            };
            AddChild(_splashGlow);

            // 4. 左上角品牌水印：山峦图标 + 「混沌世界」
            _watermark = new LoadingWatermark(
                WatermarkLabel, LoadingGlyphKind.Mountain, PageTitle,
                InkWashTheme.TextBrand, WatermarkOpacity)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_watermark);

            // 5. 居中双层旋转金环 + 环心「峰」
            _ringSpinner = new LoadingRingSpinner
            {
                CenterText = RingCenterLabel,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_ringSpinner);

            // 6. 金环副标题「远峰入云 · 破境中」
            _ringSubtitle = new Label
            {
                Text = RingSubtitleLabel,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Enabled = false,
            };
            AddChild(_ringSubtitle);

            // 7. 右侧竖排书法诗句 + 落款 + 朱红印章
            _poemGroup = new LoadingPoemGroup(
                PoemLineOne, PoemLineTwo, PoemAuthorLabel, PoemSealLabel,
                PoemTitleFontSize, PoemSealFontSize, PoemSealSize, PoemSealTop)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_poemGroup);

            // 8. 底部加载进度区（含完成后的「点击继续」出口）
            _bottomBar = new LoadingBottomBar(
                LoadingGlyphKind.LoaderCircle, LoadingGlyphKind.Sparkles, false, true)
            {
                AnchorPreset = AnchorPresets.TopLeft,
            };
            _bottomBar.ProgressLabelText = ProgressLabelText;
            _bottomBar.TipText = LoadingTips[0];
            _bottomBar.VersionText = VersionLabelText;
            if (_bottomBar.ContinueHint != null)
            {
                _bottomBar.ContinueHint.HintText = ContinueHintLabel;
                _bottomBar.ContinueHint.Clicked += OnContinueHintClicked;
            }
            AddChild(_bottomBar);

            // 应用初始布局
            ApplyLayout();
        }

        /// <summary>
        /// 更新进度条与数值。
        /// value 会被钳制到 0-1 范围；value &gt;= 1 时触发 <see cref="ProgressComplete"/> 事件（仅触发一次）。
        /// 同时按进度分段（每 20%）切换底部提示文案。
        /// </summary>
        /// <param name="value">进度值（0-1）</param>
        public void SetProgress(float value)
        {
            LoadingPageHelper.SetProgressInternal(
                _bottomBar != null ? _bottomBar.ProgressBar : null,
                _bottomBar != null ? _bottomBar.ProgressPctLabel : null,
                ref _progress, ref _completed,
                value, ProgressComplete, nameof(LoadingPage2));

            UpdateTipByProgress();
        }

        /// <summary>
        /// 根据当前进度切换提示文案（对应设计稿 newTipIndex = progress / 20）。
        /// </summary>
        private void UpdateTipByProgress()
        {
            if (_bottomBar == null)
                return;

            int index = Mathf.Clamp((int)(_progress / TipProgressStep), 0, LoadingTips.Length - 1);
            if (index == _tipIndex)
                return;

            _tipIndex = index;
            _bottomBar.TipText = LoadingTips[index];
        }

        /// <summary>
        /// 「点击继续」点击处理：改写提示文案并触发 <see cref="ContinueRequested"/> 事件。
        /// </summary>
        private void OnContinueHintClicked()
        {
            if (_bottomBar != null)
                _bottomBar.TipText = EnteringWorldTipLabel;

            try
            {
                ContinueRequested?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[LoadingPage2] ContinueRequested 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 外部（如 <see cref="InkPageShell"/> 或屏幕大小变更监听器）应调用此方法。
        /// </summary>
        public void RefreshLayout()
        {
            _screenSize = LoadingPageHelper.ResolveLayoutSize(Width, Height);
            ApplyLayout();
        }

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置与尺寸。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;
            float centerX = sw * 0.5f;

            if (_backgroundImage != null)
            {
                _backgroundImage.Location = Float2.Zero;
                _backgroundImage.Size = _screenSize;
            }

            if (_vignette != null)
            {
                _vignette.Location = Float2.Zero;
                _vignette.Size = _screenSize;
            }

            // 晕染装饰：top 20% / left 8%
            if (_splashGlow != null)
            {
                _splashGlow.Size = new Float2(GlowSize, GlowSize);
                _splashGlow.Location = new Float2(sw * GlowLeftRatio, sh * GlowTopRatio);
            }

            // 品牌水印：左上角
            if (_watermark != null)
                _watermark.Location = new Float2(WatermarkLeftOffset, WatermarkTopOffset);

            // 旋转金环：屏幕水平居中，垂直位于 42% 处
            float ringCenterY = sh * RingCenterTopRatio;
            if (_ringSpinner != null)
            {
                _ringSpinner.Size = new Float2(RingSize, RingSize);
                _ringSpinner.Location = new Float2(centerX - RingSize * 0.5f, ringCenterY - RingSize * 0.5f);
            }

            // 副标题：金环下方 24px 居中
            if (_ringSubtitle != null)
            {
                _ringSubtitle.Size = new Float2(RingSubtitleWidth, RingSubtitleHeight);
                _ringSubtitle.Location = new Float2(
                    centerX - RingSubtitleWidth * 0.5f,
                    ringCenterY + RingSize * 0.5f + RingSubtitleGap);
            }

            // 诗句组：右侧垂直居中
            if (_poemGroup != null)
            {
                _poemGroup.Location = new Float2(
                    sw - PoemRightOffset - _poemGroup.Width,
                    sh * 0.5f - _poemGroup.Height * 0.5f);
            }

            // 底部加载进度区：贴底通栏
            if (_bottomBar != null)
            {
                _bottomBar.Location = new Float2(0f, sh - LoadingBottomBar.BarAreaHeight);
                _bottomBar.Size = new Float2(sw, LoadingBottomBar.BarAreaHeight);
                _bottomBar.ApplyLayout();
            }
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (_completed)
            {
                // 进度满后延迟 400ms 显示「点击继续」并改写提示文案（对应设计稿脚本行为）
                if (!_continueHintShown)
                {
                    _completedHintTimer += deltaTime;
                    if (_completedHintTimer >= CompletedHintDelay)
                    {
                        _continueHintShown = true;
                        if (_bottomBar != null)
                        {
                            _bottomBar.TipText = CompletedTipLabel;
                            _bottomBar.ShowContinueHint = true;
                        }
                    }
                }
                return;
            }

            // 模拟加载：每秒 +0.1
            SetProgress(_progress + AutoProgressRate * deltaTime);
        }
    }

    // =======================================================================

    /// <summary>
    /// 章节过场页面。
    /// 全屏水墨背景 + 居中毛笔书法章节名 + 章节副标题 + "进入世界"按钮。
    /// 按钮点击时触发 <see cref="EnterWorldClicked"/> 事件，
    /// 由 <see cref="InkPageRouter"/> 订阅后调用 <c>NavigateTo("combat-hud")</c> 进入战斗 HUD。
    /// </summary>
    public class ChapterTransitionPage : ContainerControl, IInkPage
    {
        /// <summary>章节名标签尺寸</summary>
        private static readonly Float2 ChapterTitleSize = new Float2(400f, 80f);

        /// <summary>章节副标题尺寸</summary>
        private static readonly Float2 SubtitleSize = new Float2(400f, 30f);

        /// <summary>"进入世界"按钮尺寸</summary>
        private static readonly Float2 EnterButtonSize = new Float2(160f, 44f);

        /// <summary>屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>全屏背景图控件</summary>
        private readonly Image _backgroundImage;

        /// <summary>章节名（毛笔书法 Display 样式）</summary>
        private readonly InkTextBlock _chapterTitle;

        /// <summary>章节副标题（Subheading 样式，居中对齐）</summary>
        private readonly InkTextBlock _subtitle;

        /// <summary>"进入世界"按钮</summary>
        private readonly InkButton _enterButton;

        /// <summary>底部进度条（SubTask 7.2，可外部驱动）</summary>
        private InkBar _progressBar;

        /// <summary>进度数值标签（DIN 字体）</summary>
        private Label _progressLabel;

        /// <summary>当前进度值（0.0~1.0）</summary>
        private float _progress;

        /// <summary>是否已触发完成事件（避免重复触发）</summary>
        private bool _completed;

        /// <summary>
        /// "进入世界"按钮点击事件。
        /// 由 <see cref="InkPageRouter"/> 订阅后调用 <c>NavigateTo("combat-hud")</c> 进入战斗 HUD。
        /// </summary>
        public event Action EnterWorldClicked;

        /// <summary>
        /// 进度满时触发的事件（SubTask 7.2）。
        /// 由外部调用 <see cref="SetProgress"/> 驱动，进度首次到达 1.0 时触发一次。
        /// </summary>
        public event Action ProgressComplete;

        /// <summary>
        /// 构造函数：初始化章节过场页面。
        /// 读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// 异步加载 <see cref="InkWashTheme.TexAssetPathChapterInk"/> 纹理作为背景，
        /// 加载失败时记录日志但不抛异常，页面背景回退为 <see cref="InkWashTheme.BaseDefault"/>。
        /// </summary>
        public ChapterTransitionPage()
        {
            _screenSize = LoadingPageHelper.ResolveScreenSize();

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            // 1. 全屏背景图
            _backgroundImage = LoadingPageHelper.CreateBackgroundImage(
                InkWashTheme.TexAssetPathChapterInk, nameof(ChapterTransitionPage), _screenSize);
            AddChild(_backgroundImage);

            // 2. 章节名："第一章 江湖初启"，Display 样式，居中
            _chapterTitle = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "第一章 江湖初启",
                AnchorPreset = AnchorPresets.TopLeft,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_chapterTitle);

            // 3. 章节副标题："墨色苍茫，江湖路远"，Subheading 样式，覆盖为居中对齐
            _subtitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "墨色苍茫，江湖路远",
                AnchorPreset = AnchorPresets.TopLeft,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(_subtitle);

            // 4. "进入世界"按钮：Primary Lg
            _enterButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "进入世界",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = EnterButtonSize,
            };
            _enterButton.ButtonClicked += OnEnterButtonClicked;
            AddChild(_enterButton);

            // 5. 底部进度条：Gold 变体（SubTask 7.2，可外部驱动）
            _progressBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Value = 0f,
            };
            AddChild(_progressBar);

            // 6. 进度数值 Label：DIN 字体
            _progressLabel = new Label
            {
                Text = "0%",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(80f, 20f),
            };
            AddChild(_progressLabel);

            // 应用初始布局
            ApplyLayout();
        }

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            // 背景图：全屏
            if (_backgroundImage != null)
            {
                _backgroundImage.Location = Float2.Zero;
                _backgroundImage.Size = _screenSize;
            }

            // 章节名：(sw/2 - 200, sh/2 - 100)，尺寸 (400, 80)
            if (_chapterTitle != null)
            {
                _chapterTitle.Location = new Float2(sw * 0.5f - 200f, sh * 0.5f - 100f);
                _chapterTitle.Size = ChapterTitleSize;
            }

            // 副标题：(sw/2 - 200, sh/2 - 20)，尺寸 (400, 30)
            if (_subtitle != null)
            {
                _subtitle.Location = new Float2(sw * 0.5f - 200f, sh * 0.5f - 20f);
                _subtitle.Size = SubtitleSize;
            }

            // 按钮：(sw/2 - 80, sh/2 + 60)，尺寸 (160, 44)
            if (_enterButton != null)
            {
                _enterButton.Location = new Float2(sw * 0.5f - 80f, sh * 0.5f + 60f);
            }

            // 进度条：(sw/2 - 200, sh - 80)，尺寸 (400, 8)
            if (_progressBar != null)
            {
                _progressBar.Location = new Float2(sw * 0.5f - 200f, sh - 80f);
                _progressBar.Size = new Float2(400f, 8f);
            }

            // 进度数值标签：(sw/2 + 210, sh - 80)，尺寸 (80, 20)
            if (_progressLabel != null)
            {
                _progressLabel.Location = new Float2(sw * 0.5f + 210f, sh - 80f);
                _progressLabel.Size = new Float2(80f, 20f);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 外部（如 <see cref="InkPageShell"/> 或屏幕大小变更监听器）应调用此方法。
        /// </summary>
        public void RefreshLayout()
        {
            _screenSize = LoadingPageHelper.ResolveLayoutSize(Width, Height);
            ApplyLayout();
        }

        /// <summary>
        /// "进入世界"按钮点击处理：触发 <see cref="EnterWorldClicked"/> 事件。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnEnterButtonClicked(Button button)
        {
            try
            {
                EnterWorldClicked?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[ChapterTransitionPage] EnterWorldClicked 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新进度条与数值（SubTask 7.2）。
        /// value 会被钳制到 0-1 范围；value &gt;= 1 时触发 <see cref="ProgressComplete"/> 事件（仅触发一次）。
        /// </summary>
        /// <param name="value">进度值（0-1）</param>
        public void SetProgress(float value)
        {
            LoadingPageHelper.SetProgressInternal(
                _progressBar, _progressLabel,
                ref _progress, ref _completed,
                value, ProgressComplete, nameof(ChapterTransitionPage));
        }
    }

    // =======================================================================

    /// <summary>
    /// 加载页共享的构建辅助方法。
    /// 提供 LoadingPage1 / LoadingPage2 / ChapterTransitionPage 共用的屏幕尺寸解析、
    /// 背景图创建与进度推进逻辑，避免相似页面类之间的代码重复。
    /// </summary>
    internal static class LoadingPageHelper
    {
        /// <summary>屏幕尺寸未就绪时的兜底值（1920x1080）</summary>
        private static readonly Float2 FallbackScreenSize = new Float2(1920f, 1080f);

        /// <summary>
        /// 解析当前屏幕尺寸，未就绪（&lt;=0）时使用 1920x1080 兜底。
        /// </summary>
        /// <returns>屏幕尺寸（Float2）</returns>
        public static Float2 ResolveScreenSize()
        {
            var size = FlaxEngine.Screen.Size;
            if (size.X <= 0f || size.Y <= 0f)
                return FallbackScreenSize;
            return size;
        }

        /// <summary>
        /// 解析页面布局尺寸：优先使用控件实际尺寸，其次屏幕尺寸，最后 1920x1080 兜底。
        /// </summary>
        /// <param name="width">控件实际宽度</param>
        /// <param name="height">控件实际高度</param>
        /// <returns>用于布局计算的尺寸</returns>
        public static Float2 ResolveLayoutSize(float width, float height)
        {
            if (width > 0f && height > 0f)
                return new Float2(width, height);
            return ResolveScreenSize();
        }

        /// <summary>
        /// 创建全屏背景 <see cref="Image"/> 控件并异步加载纹理。
        /// 加载失败时记录日志但不抛异常，Image 保持透明
        /// （由页面本身的 <see cref="InkWashTheme.BaseDefault"/> 深墨黑底色兜底显示）。
        /// </summary>
        /// <param name="texturePath">纹理资产路径</param>
        /// <param name="pageName">页面名（用于日志标识）</param>
        /// <param name="screenSize">屏幕尺寸（用于初始化 Image 尺寸）</param>
        /// <returns>已配置的 Image 控件</returns>
        public static Image CreateBackgroundImage(string texturePath, string pageName, Float2 screenSize)
        {
            var image = new Image
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Location = Float2.Zero,
                Size = screenSize,
                BackgroundColor = Color.Transparent,
            };

            try
            {
                var texture = Content.LoadAsync<Texture>(texturePath);
                if (texture != null)
                {
                    image.Brush = new TextureBrush(texture);
                }
                else
                {
                    FlaxEngine.Debug.LogWarning(
                        $"[{pageName}] 背景纹理加载失败 (返回 null): {texturePath}");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[{pageName}] 背景纹理加载异常: {texturePath} — {ex.Message}");
            }

            return image;
        }

        /// <summary>
        /// 通用进度更新逻辑：钳制 value，同步进度条与数值标签，
        /// value &gt;= 1 且未完成时触发 <see cref="Action"/> 回调（仅触发一次）。
        /// </summary>
        /// <param name="bar">进度条控件</param>
        /// <param name="label">进度数值标签控件</param>
        /// <param name="progress">引用传递的当前进度值字段</param>
        /// <param name="completed">引用传递的已完成标志字段</param>
        /// <param name="value">待设置的进度值（0-1）</param>
        /// <param name="onComplete">完成回调（通常为 ProgressComplete 事件）</param>
        /// <param name="pageName">页面名（用于日志标识）</param>
        public static void SetProgressInternal(
            InkBar bar, Label label,
            ref float progress, ref bool completed,
            float value, Action onComplete, string pageName)
        {
            progress = Mathf.Clamp(value, 0f, 1f);

            if (bar != null)
                bar.Value = progress;

            if (label != null)
                label.Text = $"{(int)Math.Round(progress * 100f)}%";

            if (progress >= 1f && !completed)
            {
                completed = true;
                try
                {
                    onComplete?.Invoke();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError(
                        $"[{pageName}] ProgressComplete 触发失败: {ex.Message}");
                }
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 加载页图标字形种类（对应设计稿中的 lucide 图标）。
    /// </summary>
    internal enum LoadingGlyphKind
    {
        /// <summary>雪顶山峦（水印 mountain-snow）</summary>
        MountainSnow,

        /// <summary>山峦（水印 mountain）</summary>
        Mountain,

        /// <summary>放射状旋转加载环（loader）</summary>
        Loader,

        /// <summary>缺口旋转圆环（loader-circle）</summary>
        LoaderCircle,

        /// <summary>摊开的书卷（book-open）</summary>
        BookOpen,

        /// <summary>星芒（sparkles）</summary>
        Sparkles,

        /// <summary>鼠标点击指针（mouse-pointer-click）</summary>
        MousePointerClick
    }

    /// <summary>
    /// 加载页矢量图标控件。
    /// 用 <see cref="Render2D"/> 线段与三角面近似绘制设计稿中的 lucide 图标，
    /// 通过 <see cref="SpinSpeed"/> 驱动旋转动画。
    /// </summary>
    internal class LoadingGlyph : ContainerControl
    {
        /// <summary>Loader 图标的放射线条数量</summary>
        private const int SpokeCount = 8;

        /// <summary>动画累计时间（秒）</summary>
        private float _time;

        /// <summary>图标种类</summary>
        public LoadingGlyphKind Kind { get; set; } = LoadingGlyphKind.MountainSnow;

        /// <summary>图标颜色</summary>
        public Color GlyphColor { get; set; } = InkWashTheme.TextBrand;

        /// <summary>线宽</summary>
        public float StrokeThickness { get; set; } = 1.5f;

        /// <summary>旋转角速度（弧度/秒），0 表示静止</summary>
        public float SpinSpeed { get; set; }

        /// <summary>
        /// 构造函数：透明背景、不裁剪子控件。
        /// </summary>
        public LoadingGlyph()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (SpinSpeed != 0f)
                _time += deltaTime * SpinSpeed;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            DrawShape(Kind, new Rectangle(0f, 0f, Width, Height), GlyphColor, StrokeThickness, _time);
        }

        /// <summary>
        /// 在指定矩形内绘制图标字形（静态方法，供其它自定义绘制控件复用）。
        /// </summary>
        /// <param name="kind">图标种类</param>
        /// <param name="rect">绘制区域</param>
        /// <param name="color">颜色</param>
        /// <param name="thickness">线宽</param>
        /// <param name="time">动画时间（弧度，用于旋转类图标）</param>
        internal static void DrawShape(
            LoadingGlyphKind kind, Rectangle rect, Color color, float thickness, float time)
        {
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f || color.A <= 0f)
                return;

            float x = rect.Location.X;
            float y = rect.Location.Y;
            float w = rect.Size.X;
            float h = rect.Size.Y;
            float t = Mathf.Max(0.5f, thickness);

            switch (kind)
            {
                case LoadingGlyphKind.Loader:
                {
                    var center = new Float2(x + w * 0.5f, y + h * 0.5f);
                    float outer = Mathf.Min(w, h) * 0.48f;
                    float inner = outer * 0.42f;
                    for (int i = 0; i < SpokeCount; i++)
                    {
                        float angle = time + i * Mathf.TwoPi / SpokeCount;
                        float fade = (i + 1f) / SpokeCount;
                        var spoke = new Float2(Mathf.Cos(angle), Mathf.Sin(angle));
                        Render2D.DrawLine(
                            center + spoke * inner,
                            center + spoke * outer,
                            LoadingDecor.WithAlpha(color, 0.22f + 0.78f * fade),
                            t);
                    }
                    break;
                }

                case LoadingGlyphKind.LoaderCircle:
                {
                    var center = new Float2(x + w * 0.5f, y + h * 0.5f);
                    float radius = Mathf.Min(w, h) * 0.45f;
                    LoadingDecor.DrawArc(center, radius, time, Mathf.Pi * 1.55f, color, t, 24);
                    break;
                }

                case LoadingGlyphKind.Mountain:
                {
                    Render2D.DrawLine(new Float2(x + w * 0.04f, y + h * 0.86f), new Float2(x + w * 0.46f, y + h * 0.16f), color, t);
                    Render2D.DrawLine(new Float2(x + w * 0.46f, y + h * 0.16f), new Float2(x + w * 0.96f, y + h * 0.86f), color, t);
                    Render2D.DrawLine(new Float2(x + w * 0.04f, y + h * 0.86f), new Float2(x + w * 0.96f, y + h * 0.86f), color, t);
                    break;
                }

                case LoadingGlyphKind.BookOpen:
                {
                    float midX = x + w * 0.5f;
                    Render2D.DrawLine(new Float2(midX, y + h * 0.24f), new Float2(x + w * 0.08f, y + h * 0.10f), color, t);
                    Render2D.DrawLine(new Float2(x + w * 0.08f, y + h * 0.10f), new Float2(x + w * 0.08f, y + h * 0.76f), color, t);
                    Render2D.DrawLine(new Float2(x + w * 0.08f, y + h * 0.76f), new Float2(midX, y + h * 0.90f), color, t);
                    Render2D.DrawLine(new Float2(midX, y + h * 0.90f), new Float2(midX, y + h * 0.24f), color, t);
                    Render2D.DrawLine(new Float2(midX, y + h * 0.24f), new Float2(x + w * 0.92f, y + h * 0.10f), color, t);
                    Render2D.DrawLine(new Float2(x + w * 0.92f, y + h * 0.10f), new Float2(x + w * 0.92f, y + h * 0.76f), color, t);
                    Render2D.DrawLine(new Float2(x + w * 0.92f, y + h * 0.76f), new Float2(midX, y + h * 0.90f), color, t);
                    break;
                }

                case LoadingGlyphKind.Sparkles:
                {
                    DrawDiamond(new Float2(x + w * 0.50f, y + h * 0.52f), Mathf.Min(w, h) * 0.30f, color);
                    DrawDiamond(new Float2(x + w * 0.20f, y + h * 0.20f), Mathf.Min(w, h) * 0.15f, color);
                    DrawDiamond(new Float2(x + w * 0.82f, y + h * 0.80f), Mathf.Min(w, h) * 0.12f, color);
                    break;
                }

                case LoadingGlyphKind.MousePointerClick:
                {
                    var points = new[]
                    {
                        new Float2(x + w * 0.30f, y + h * 0.10f),
                        new Float2(x + w * 0.74f, y + h * 0.58f),
                        new Float2(x + w * 0.52f, y + h * 0.60f),
                        new Float2(x + w * 0.66f, y + h * 0.88f),
                        new Float2(x + w * 0.54f, y + h * 0.95f),
                        new Float2(x + w * 0.40f, y + h * 0.67f),
                        new Float2(x + w * 0.30f, y + h * 0.78f),
                    };
                    for (int i = 0; i < points.Length; i++)
                        Render2D.DrawLine(points[i], points[(i + 1) % points.Length], color, t);
                    break;
                }

                default: // MountainSnow
                {
                    Render2D.DrawLine(new Float2(x + w * 0.04f, y + h * 0.86f), new Float2(x + w * 0.46f, y + h * 0.16f), color, t);
                    Render2D.DrawLine(new Float2(x + w * 0.46f, y + h * 0.16f), new Float2(x + w * 0.96f, y + h * 0.86f), color, t);
                    // 山顶雪线
                    Render2D.DrawLine(new Float2(x + w * 0.30f, y + h * 0.44f), new Float2(x + w * 0.40f, y + h * 0.56f), color, t);
                    Render2D.DrawLine(new Float2(x + w * 0.40f, y + h * 0.56f), new Float2(x + w * 0.50f, y + h * 0.42f), color, t);
                    break;
                }
            }
        }

        /// <summary>
        /// 绘制实心菱形（星芒图标的最小单元）。
        /// </summary>
        private static void DrawDiamond(Float2 center, float radius, Color color)
        {
            var top = new Float2(center.X, center.Y - radius);
            var right = new Float2(center.X + radius, center.Y);
            var bottom = new Float2(center.X, center.Y + radius);
            var left = new Float2(center.X - radius, center.Y);

            Render2D.FillTriangles(new[] { top, right, bottom }, color);
            Render2D.FillTriangles(new[] { top, bottom, left }, color);
        }
    }

    // =======================================================================

    /// <summary>
    /// 竖排文本控件（对应 CSS <c>writing-mode: vertical-rl</c>）。
    /// 与 <see cref="InkVerticalTitle"/> 的区别是字体角色/字号/颜色/字间距可配置，
    /// 用于加载页诗句落款等小字号竖排文字。
    /// </summary>
    internal class LoadingVerticalText : ContainerControl
    {
        /// <summary>单字行高系数</summary>
        private const float LineHeightRatio = 1.2f;

        /// <summary>竖排文字</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>字体角色</summary>
        public InkWashTheme.FontRole FontRole { get; set; } = InkWashTheme.FontRole.Heading;

        /// <summary>字号</summary>
        public float FontSize { get; set; } = 12f;

        /// <summary>文字颜色</summary>
        public Color TextColor { get; set; } = InkWashTheme.TextTertiary;

        /// <summary>字间距</summary>
        public float LetterSpacing { get; set; } = 2f;

        /// <summary>
        /// 构造函数：透明背景、不接收交互。
        /// </summary>
        public LoadingVerticalText()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Enabled = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || string.IsNullOrEmpty(Text) || Width <= 0f || Height <= 0f)
                return;

            var font = InkRenderHelper.GetFontRef(FontRole, FontSize).GetFont();
            if (font == null)
                return;

            float step = FontSize + LetterSpacing;
            for (int i = 0; i < Text.Length; i++)
            {
                var charRect = new Rectangle(0f, i * step, Width, FontSize * LineHeightRatio);
                Render2D.DrawText(
                    font,
                    Text[i].ToString(),
                    charRect,
                    TextColor,
                    TextAlignment.Center,
                    TextAlignment.Near,
                    TextWrapping.NoWrap);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 朱红印章控件（对应设计稿 <c>.loading-poem-seal</c> / <c>.loading2-poem-seal</c>）：
    /// 1.5px 朱红褪色描边方框 + 居中毛笔书法单字，整体 60% 不透明度。
    /// </summary>
    internal class LoadingSealBadge : ContainerControl
    {
        /// <summary>整体不透明度（对应设计稿 opacity: 0.6）</summary>
        private const float SealOpacity = 0.6f;

        /// <summary>描边线宽</summary>
        private const float BorderThickness = 1.5f;

        /// <summary>印章文字</summary>
        public string SealText { get; set; } = string.Empty;

        /// <summary>印章字号</summary>
        public float FontSize { get; set; } = 16f;

        /// <summary>印章颜色（默认朱红褪色）</summary>
        public Color SealColor { get; set; } = InkWashTheme.VermilionFaded;

        /// <summary>
        /// 构造函数：透明背景、不接收交互。
        /// </summary>
        public LoadingSealBadge()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Enabled = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var color = LoadingDecor.WithAlpha(SealColor, SealOpacity);
            InkRenderHelper.DrawRoundedRectangle(
                new Rectangle(0f, 0f, Width, Height), InkWashTheme.RadiusSm, color, BorderThickness);

            if (string.IsNullOrEmpty(SealText))
                return;

            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, FontSize).GetFont();
            if (font == null)
                return;

            Render2D.DrawText(
                font,
                SealText,
                new Rectangle(0f, 0f, Width, Height),
                color,
                TextAlignment.Center,
                TextAlignment.Center,
                TextWrapping.NoWrap);
        }
    }

    // =======================================================================

    /// <summary>
    /// 竖排书法诗句组（对应设计稿 <c>.loading-poem</c> / <c>.loading2-poem</c>）：
    /// 右侧第一列诗句 + 左侧第二列诗句 + 更左侧的竖排落款 + 下方朱红印章。
    /// </summary>
    internal class LoadingPoemGroup : ContainerControl
    {
        /// <summary>第一列（最右）水平位置</summary>
        private const float LineOneLeft = 80f;

        /// <summary>第二列水平位置</summary>
        private const float LineTwoLeft = 40f;

        /// <summary>落款水平位置</summary>
        private const float AuthorLeft = 8f;

        /// <summary>诗句列宽</summary>
        private const float ColumnWidth = 40f;

        /// <summary>诗句列高</summary>
        private const float ColumnHeight = 160f;

        /// <summary>落款列宽</summary>
        private const float AuthorWidth = 18f;

        /// <summary>落款列高</summary>
        private const float AuthorHeight = 140f;

        private readonly InkVerticalTitle _lineOne;
        private readonly InkVerticalTitle _lineTwo;
        private readonly LoadingVerticalText _author;
        private readonly LoadingSealBadge _seal;
        private readonly float _sealTop;
        private readonly float _sealSize;

        /// <summary>
        /// 构造函数：按设计稿组装两列诗句、竖排落款与朱红印章。
        /// </summary>
        /// <param name="lineOne">第一列诗句（最右）</param>
        /// <param name="lineTwo">第二列诗句</param>
        /// <param name="author">竖排落款</param>
        /// <param name="seal">印章文字</param>
        /// <param name="titleFontSize">诗句字号</param>
        /// <param name="sealFontSize">印章字号</param>
        /// <param name="sealSize">印章边长</param>
        /// <param name="sealTop">印章距诗句行顶部的偏移</param>
        public LoadingPoemGroup(
            string lineOne, string lineTwo, string author, string seal,
            float titleFontSize, float sealFontSize, float sealSize, float sealTop)
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Enabled = false;

            _sealTop = sealTop;
            _sealSize = sealSize;

            _lineOne = new InkVerticalTitle
            {
                Text = lineOne,
                FontSize = titleFontSize,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_lineOne);

            _lineTwo = new InkVerticalTitle
            {
                Text = lineTwo,
                FontSize = titleFontSize,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_lineTwo);

            _author = new LoadingVerticalText
            {
                Text = author,
                FontRole = InkWashTheme.FontRole.Heading,
                FontSize = 12f,
                TextColor = InkWashTheme.TextTertiary,
                LetterSpacing = 2f,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_author);

            _seal = new LoadingSealBadge
            {
                SealText = seal,
                FontSize = sealFontSize,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_seal);

            Size = new Float2(LineOneLeft + ColumnWidth, sealTop + sealSize);
            ApplyLayout();
        }

        /// <summary>
        /// 应用诗句组内部布局。
        /// </summary>
        public void ApplyLayout()
        {
            if (_lineOne != null)
            {
                _lineOne.Location = new Float2(LineOneLeft, 0f);
                _lineOne.Size = new Float2(ColumnWidth, ColumnHeight);
            }

            if (_lineTwo != null)
            {
                _lineTwo.Location = new Float2(LineTwoLeft, 0f);
                _lineTwo.Size = new Float2(ColumnWidth, ColumnHeight);
            }

            if (_author != null)
            {
                _author.Location = new Float2(AuthorLeft, 0f);
                _author.Size = new Float2(AuthorWidth, AuthorHeight);
            }

            if (_seal != null)
            {
                _seal.Location = new Float2(AuthorLeft, _sealTop);
                _seal.Size = new Float2(_sealSize, _sealSize);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 品牌水印控件（对应设计稿 <c>.loading-watermark</c> / <c>.loading2-watermark</c>）：
    /// 18px 图标 + 10px 间距 + 18px 毛笔书法「混沌世界」，整体半透明。
    /// </summary>
    internal class LoadingWatermark : ContainerControl
    {
        /// <summary>图标尺寸</summary>
        private const float GlyphSize = 18f;

        /// <summary>图标与文字间距</summary>
        private const float Gap = 10f;

        /// <summary>文字字号</summary>
        private const float FontSize = 18f;

        /// <summary>行高</summary>
        private const float RowHeight = 22f;

        /// <summary>文字宽度冗余</summary>
        private const float TextPadding = 8f;

        private readonly LoadingGlyph _glyph;
        private readonly Label _text;

        /// <summary>
        /// 构造函数：组装图标 + 品牌文字，并设置整体不透明度。
        /// </summary>
        /// <param name="text">水印文字</param>
        /// <param name="glyphKind">图标种类</param>
        /// <param name="tooltip">悬停提示（设计稿文档标题）</param>
        /// <param name="color">基础颜色（通常为品牌金）</param>
        /// <param name="opacity">整体不透明度</param>
        public LoadingWatermark(
            string text, LoadingGlyphKind glyphKind, string tooltip, Color color, float opacity)
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            // 保持可交互，使水印上的悬停提示（设计稿文档标题）可用
            TooltipText = tooltip;

            var tint = LoadingDecor.WithAlpha(color, opacity);
            float textWidth = LoadingDecor.EstimateTextWidth(text, FontSize) + TextPadding;

            _glyph = new LoadingGlyph
            {
                Kind = glyphKind,
                GlyphColor = tint,
                StrokeThickness = 1.5f,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, (RowHeight - GlyphSize) * 0.5f),
                Size = new Float2(GlyphSize, GlyphSize),
            };
            AddChild(_glyph);

            _text = new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, FontSize),
                TextColor = tint,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(GlyphSize + Gap, 0f),
                Size = new Float2(textWidth, RowHeight),
            };
            AddChild(_text);

            Size = new Float2(GlyphSize + Gap + textWidth, RowHeight);
        }
    }

    // =======================================================================

    /// <summary>
    /// 柔光晕染装饰控件（对应设计稿 <c>.loading-splash-deco</c> / <c>.loading2-splash-deco</c>）：
    /// 圆形径向渐变光斑，边缘完全透明，模拟 CSS <c>filter: blur(20px)</c>。
    /// </summary>
    internal class LoadingGlow : ContainerControl
    {
        /// <summary>渐变分段数（越大越柔和）</summary>
        private const int GradientSteps = 24;

        /// <summary>光斑颜色</summary>
        public Color GlowColor { get; set; } = InkWashTheme.GoldPrimary;

        /// <summary>光斑中心 alpha</summary>
        public float GlowAlpha { get; set; } = 0.08f;

        /// <summary>
        /// 构造函数：透明背景、不接收交互。
        /// </summary>
        public LoadingGlow()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Enabled = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f || GlowAlpha <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float radius = Mathf.Min(Width, Height) * 0.5f;
            var core = LoadingDecor.WithAlpha(GlowColor, GlowAlpha);
            var edge = new Color(GlowColor.R, GlowColor.G, GlowColor.B, 0f);
            InkRenderHelper.FillRadialGradient(center, radius, core, edge, GradientSteps);
        }
    }

    // =======================================================================

    /// <summary>
    /// 双层旋转金环控件（对应设计稿 <c>.loading-ring</c> 系列）：
    /// 外环 2px（金亮 / 金深两段）顺时针、内环 1px（金主色 / 古铜两段）逆时针，环心为毛笔书法单字。
    /// </summary>
    internal class LoadingRingSpinner : ContainerControl
    {
        /// <summary>外环旋转周期（秒，对应设计稿 2.5s）</summary>
        private const float OuterPeriod = 2.5f;

        /// <summary>内环旋转周期（秒，对应设计稿 3s，方向相反）</summary>
        private const float InnerPeriod = 3f;

        /// <summary>内环内缩距离</summary>
        private const float InnerInset = 12f;

        /// <summary>外环线宽</summary>
        private const float OuterThickness = 2f;

        /// <summary>内环线宽</summary>
        private const float InnerThickness = 1f;

        /// <summary>内环整体不透明度（对应设计稿 opacity: 0.6）</summary>
        private const float InnerOpacity = 0.6f;

        /// <summary>四分之一圆周（每段边框色的扫描角度，π/2）</summary>
        private static readonly float QuarterTurn = Mathf.Pi * 0.5f;

        /// <summary>圆弧分段数</summary>
        private const int ArcSegments = 16;

        /// <summary>动画累计时间</summary>
        private float _time;

        /// <summary>环心文字</summary>
        public string CenterText { get; set; } = string.Empty;

        /// <summary>环心文字字号</summary>
        public float CenterFontSize { get; set; } = 32f;

        /// <summary>
        /// 构造函数：透明背景、不裁剪子控件。
        /// </summary>
        public LoadingRingSpinner()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _time += deltaTime;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float outerRadius = Mathf.Min(Width, Height) * 0.5f - OuterThickness * 0.5f;
            float innerRadius = outerRadius - InnerInset;
            if (innerRadius <= 0f)
                return;

            // 外环：顺时针旋转，右上两段分别取金亮 / 金深
            float outerAngle = -Mathf.Pi * 0.5f + (_time / OuterPeriod) * Mathf.TwoPi;
            LoadingDecor.DrawArc(center, outerRadius, outerAngle, QuarterTurn,
                InkWashTheme.GoldBright, OuterThickness, ArcSegments);
            LoadingDecor.DrawArc(center, outerRadius, outerAngle + QuarterTurn, QuarterTurn,
                InkWashTheme.GoldDeep, OuterThickness, ArcSegments);

            // 内环：逆时针旋转，左下两段分别取古铜 / 金主色
            float innerAngle = QuarterTurn - (_time / InnerPeriod) * Mathf.TwoPi;
            LoadingDecor.DrawArc(center, innerRadius, innerAngle, QuarterTurn,
                LoadingDecor.WithAlpha(InkWashTheme.BronzePrimary, InnerOpacity), InnerThickness, ArcSegments);
            LoadingDecor.DrawArc(center, innerRadius, innerAngle + QuarterTurn, QuarterTurn,
                LoadingDecor.WithAlpha(InkWashTheme.GoldPrimary, InnerOpacity), InnerThickness, ArcSegments);

            // 环心书法单字
            if (string.IsNullOrEmpty(CenterText))
                return;

            var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, CenterFontSize).GetFont();
            if (font == null)
                return;

            Render2D.DrawText(
                font,
                CenterText,
                new Rectangle(0f, 0f, Width, Height),
                InkWashTheme.TextBrand,
                TextAlignment.Center,
                TextAlignment.Center,
                TextWrapping.NoWrap);
        }
    }

    // =======================================================================

    /// <summary>
    /// 三点脉动指示器（对应设计稿 <c>.loading-tip-dots</c>）：
    /// 三个 4px 金点，按 0.2s 步进做 1.4s 周期的明暗脉动。
    /// </summary>
    internal class LoadingPulseDots : ContainerControl
    {
        /// <summary>脉动周期（秒）</summary>
        private const float PulsePeriod = 1.4f;

        /// <summary>相邻圆点的时间偏移（秒）</summary>
        private const float PhaseStep = 0.2f;

        /// <summary>最暗 / 最亮不透明度</summary>
        private const float MinOpacity = 0.2f;

        /// <summary>最亮不透明度</summary>
        private const float MaxOpacity = 0.8f;

        /// <summary>动画累计时间</summary>
        private float _time;

        /// <summary>圆点数量</summary>
        public int DotCount { get; set; } = 3;

        /// <summary>圆点直径</summary>
        public float DotSize { get; set; } = 4f;

        /// <summary>圆点间距</summary>
        public float DotGap { get; set; } = 3f;

        /// <summary>圆点颜色</summary>
        public Color DotColor { get; set; } = InkWashTheme.GoldPrimary;

        /// <summary>
        /// 构造函数：透明背景、不裁剪子控件。
        /// </summary>
        public LoadingPulseDots()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _time += deltaTime;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f || DotCount <= 0)
                return;

            float radius = DotSize * 0.5f;
            float cy = Height * 0.5f;

            for (int i = 0; i < DotCount; i++)
            {
                float phase = (_time - i * PhaseStep) / PulsePeriod;
                float t = phase - (float)Math.Floor(phase);
                float alpha = t < 0.4f
                    ? Mathf.Lerp(MinOpacity, MaxOpacity, t / 0.4f)
                    : Mathf.Lerp(MaxOpacity, MinOpacity, (t - 0.4f) / 0.6f);

                var color = LoadingDecor.WithAlpha(DotColor, alpha);
                float cx = i * (DotSize + DotGap) + radius;
                InkRenderHelper.FillCircle(new Float2(cx, cy), radius, color);
            }
        }
    }

    // =======================================================================

    /// <summary>
    /// 底部「点击继续」提示控件（对应设计稿 <c>.loading2-continue-hint</c>）：
    /// 指针图标 + 品牌金提示文字 + 闪烁点，可点击并抛出 <see cref="Clicked"/> 事件。
    /// </summary>
    internal class LoadingContinueHint : ContainerControl
    {
        /// <summary>图标尺寸</summary>
        private const float GlyphSize = 14f;

        /// <summary>元素间距</summary>
        private const float Gap = 6f;

        /// <summary>文字字号</summary>
        private const float FontSize = 12f;

        /// <summary>闪烁周期（秒）</summary>
        private const float BlinkPeriod = 1.8f;

        /// <summary>闪烁最暗不透明度</summary>
        private const float BlinkMinOpacity = 0.3f;

        /// <summary>闪烁点直径</summary>
        private const float BlinkDotSize = 3.2f;

        /// <summary>文字宽度冗余</summary>
        private const float TextPadding = 4f;

        /// <summary>是否按下鼠标左键</summary>
        private bool _mouseDown;

        /// <summary>动画累计时间</summary>
        private float _time;

        /// <summary>点击事件。</summary>
        public event Action Clicked;

        /// <summary>提示文字</summary>
        public string HintText { get; set; } = string.Empty;

        /// <summary>提示颜色</summary>
        public Color HintColor { get; set; } = InkWashTheme.TextBrand;

        /// <summary>整体不透明度（悬停 0.75 / 点击 0.5）</summary>
        public float Opacity { get; set; } = 1f;

        /// <summary>控件建议宽度（供父容器布局使用）</summary>
        public float PreferredWidth =>
            GlyphSize + Gap + LoadingDecor.EstimateTextWidth(HintText, FontSize) + TextPadding + Gap + BlinkDotSize;

        /// <summary>
        /// 构造函数：透明背景、手型光标。
        /// </summary>
        public LoadingContinueHint()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Cursor = CursorType.Hand;
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _time += deltaTime;
        }

        /// <inheritdoc />
        public override void OnMouseEnter(Float2 location)
        {
            base.OnMouseEnter(location);
            Opacity = 0.75f;
        }

        /// <inheritdoc />
        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            Opacity = 1f;
            _mouseDown = false;
        }

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            base.OnMouseDown(location, button);
            if (button == MouseButton.Left)
                _mouseDown = true;
            return true;
        }

        /// <inheritdoc />
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            base.OnMouseUp(location, button);
            if (button == MouseButton.Left && _mouseDown)
            {
                _mouseDown = false;
                if (location.X >= 0f && location.X <= Width &&
                    location.Y >= 0f && location.Y <= Height)
                {
                    Opacity = 0.5f;
                    Clicked?.Invoke();
                }
            }
            return true;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var color = LoadingDecor.WithAlpha(HintColor, Opacity);

            // 指针图标
            LoadingGlyph.DrawShape(
                LoadingGlyphKind.MousePointerClick,
                new Rectangle(0f, (Height - GlyphSize) * 0.5f, GlyphSize, GlyphSize),
                color, 1.4f, 0f);

            // 提示文字
            float textLeft = GlyphSize + Gap;
            float textWidth = LoadingDecor.EstimateTextWidth(HintText, FontSize) + TextPadding;
            if (!string.IsNullOrEmpty(HintText))
            {
                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, FontSize).GetFont();
                if (font != null)
                {
                    Render2D.DrawText(
                        font,
                        HintText,
                        new Rectangle(textLeft, 0f, textWidth, Height),
                        color,
                        TextAlignment.Near,
                        TextAlignment.Center,
                        TextWrapping.NoWrap);
                }
            }

            // 闪烁点
            float blink = BlinkMinOpacity + (1f - BlinkMinOpacity) *
                (0.5f - 0.5f * Mathf.Cos(Mathf.TwoPi * _time / BlinkPeriod));
            float dotLeft = textLeft + textWidth + Gap + BlinkDotSize * 0.5f;
            InkRenderHelper.FillCircle(
                new Float2(dotLeft, Height * 0.5f),
                BlinkDotSize * 0.5f,
                LoadingDecor.WithAlpha(HintColor, Opacity * blink));
        }
    }

    // =======================================================================

    /// <summary>
    /// 底部加载进度区（对应设计稿 <c>.loading-bottom-bar</c> / <c>.loading2-bottom</c>）：
    /// 贴底通栏的竖向渐变遮罩上，承载「旋转图标 + 说明文案 + 进度条 + 进度数值」进度行，
    /// 以及「提示图标 + 轮播提示 + 脉动点」与版本号组成的提示行，
    /// 加载完成后可在提示行中央显示「点击继续」出口。
    /// </summary>
    internal class LoadingBottomBar : ContainerControl
    {
        /// <summary>底部进度区整体高度</summary>
        internal const float BarAreaHeight = 120f;

        /// <summary>左右内边距</summary>
        private const float SidePadding = 48f;

        /// <summary>进度行顶部偏移</summary>
        private const float ProgressRowTop = 20f;

        /// <summary>进度行高度</summary>
        private const float ProgressRowHeight = 38f;

        /// <summary>提示行顶部偏移</summary>
        private const float TipsRowTop = 76f;

        /// <summary>提示行高度</summary>
        private const float TipsRowHeight = 20f;

        /// <summary>进度行元素间距</summary>
        private const float ItemGap = 16f;

        /// <summary>进度图标尺寸</summary>
        private const float ProgressGlyphSize = 16f;

        /// <summary>提示图标尺寸</summary>
        private const float TipGlyphSize = 12f;

        /// <summary>提示图标与文字间距</summary>
        private const float TipGlyphGap = 6f;

        /// <summary>进度条厚度</summary>
        private const float ProgressBarThickness = 6f;

        /// <summary>进度条最小宽度</summary>
        private const float ProgressBarMinWidth = 120f;

        /// <summary>进度数值宽度</summary>
        private const float ProgressPctWidth = 72f;

        /// <summary>进度说明文案宽度</summary>
        private const float ProgressLabelWidth = 130f;

        /// <summary>版本号宽度</summary>
        private const float VersionWidth = 110f;

        /// <summary>提示文案字号</summary>
        private const float TipFontSize = 11f;

        /// <summary>遮罩渐变中段位置</summary>
        private const float ScrimMidStop = 0.5f;

        /// <summary>遮罩渐变中段 alpha（对应设计稿 rgba(14,16,22,0.7)）</summary>
        private const float ScrimMidAlpha = 0.7f;

        /// <summary>遮罩渐变底部 alpha（对应设计稿 rgba(14,16,22,0.95)）</summary>
        private const float ScrimBottomAlpha = 0.95f;

        /// <summary>遮罩渐变分段数</summary>
        private const int ScrimGradientSteps = 24;

        private readonly LoadingGlyph _progressGlyph;
        private readonly Label _progressLabel;
        private readonly InkBar _progressBar;
        private readonly Label _progressPct;
        private readonly LoadingGlyph _tipGlyph;
        private readonly Label _tipText;
        private readonly LoadingPulseDots _tipDots;
        private readonly Label _version;
        private readonly LoadingContinueHint _continueHint;

        /// <summary>进度条控件（供外部驱动进度）</summary>
        public InkBar ProgressBar => _progressBar;

        /// <summary>进度数值标签控件（供外部驱动进度）</summary>
        public Label ProgressPctLabel => _progressPct;

        /// <summary>「点击继续」提示控件（无此提示时为 null）</summary>
        public LoadingContinueHint ContinueHint => _continueHint;

        /// <summary>进度行说明文案</summary>
        public string ProgressLabelText
        {
            get => _progressLabel.Text;
            set => _progressLabel.Text = value ?? string.Empty;
        }

        /// <summary>右下角版本号文案</summary>
        public string VersionText
        {
            get => _version.Text;
            set => _version.Text = value ?? string.Empty;
        }

        /// <summary>提示行轮播文案（设置后自动重排脉动点位置）</summary>
        public string TipText
        {
            get => _tipText.Text;
            set
            {
                _tipText.Text = value ?? string.Empty;
                LayoutTipRow();
            }
        }

        /// <summary>是否显示「点击继续」提示</summary>
        public bool ShowContinueHint
        {
            get => _continueHint != null && _continueHint.Visible;
            set
            {
                if (_continueHint != null)
                    _continueHint.Visible = value;
            }
        }

        /// <summary>
        /// 构造函数：按设计稿组装进度行与提示行。
        /// </summary>
        /// <param name="progressGlyphKind">进度行旋转图标种类</param>
        /// <param name="tipGlyphKind">提示行图标种类</param>
        /// <param name="showTipDots">是否显示三点脉动指示器</param>
        /// <param name="showContinueHint">是否创建「点击继续」提示</param>
        public LoadingBottomBar(
            LoadingGlyphKind progressGlyphKind, LoadingGlyphKind tipGlyphKind,
            bool showTipDots, bool showContinueHint)
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;

            // 1. 进度行：旋转图标（对应设计稿 animation: spin 2s linear infinite）
            _progressGlyph = new LoadingGlyph
            {
                Kind = progressGlyphKind,
                GlyphColor = InkWashTheme.TextBrand,
                StrokeThickness = 1.5f,
                SpinSpeed = Mathf.TwoPi / 2f,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_progressGlyph);

            _progressLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 12f),
                TextColor = InkWashTheme.TextSecondary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_progressLabel);

            _progressBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Height = ProgressBarThickness,
                Value = 0f,
            };
            AddChild(_progressBar);

            _progressPct = new Label
            {
                Text = "0%",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 24f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_progressPct);

            // 2. 提示行：图标 + 轮播提示
            _tipGlyph = new LoadingGlyph
            {
                Kind = tipGlyphKind,
                GlyphColor = InkWashTheme.TextTertiary,
                StrokeThickness = 1.2f,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_tipGlyph);

            _tipText = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, TipFontSize),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_tipText);

            if (showTipDots)
            {
                _tipDots = new LoadingPulseDots
                {
                    DotCount = 3,
                    DotSize = 4f,
                    DotGap = 3f,
                    DotColor = InkWashTheme.GoldPrimary,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                AddChild(_tipDots);
            }

            _version = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
                TextColor = LoadingDecor.WithAlpha(InkWashTheme.TextTertiary, 0.5f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_version);

            if (showContinueHint)
            {
                _continueHint = new LoadingContinueHint
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Visible = false,
                };
                AddChild(_continueHint);
            }
        }

        /// <summary>
        /// 根据当前控件尺寸重新计算进度行与提示行的布局。
        /// </summary>
        public void ApplyLayout()
        {
            float w = Width;

            if (_progressGlyph != null)
            {
                _progressGlyph.Size = new Float2(ProgressGlyphSize, ProgressGlyphSize);
                _progressGlyph.Location = new Float2(
                    SidePadding, ProgressRowTop + (ProgressRowHeight - ProgressGlyphSize) * 0.5f);
            }

            if (_progressLabel != null)
            {
                _progressLabel.Size = new Float2(ProgressLabelWidth, ProgressRowHeight);
                _progressLabel.Location = new Float2(SidePadding + ProgressGlyphSize + ItemGap, ProgressRowTop);
            }

            float pctLeft = w - SidePadding - ProgressPctWidth;
            if (_progressPct != null)
            {
                _progressPct.Size = new Float2(ProgressPctWidth, ProgressRowHeight);
                _progressPct.Location = new Float2(pctLeft, ProgressRowTop);
            }

            if (_progressBar != null)
            {
                float barLeft = SidePadding + ProgressGlyphSize + ItemGap + ProgressLabelWidth + ItemGap;
                float barWidth = Mathf.Max(ProgressBarMinWidth, pctLeft - ItemGap - barLeft);
                _progressBar.Size = new Float2(barWidth, ProgressBarThickness);
                _progressBar.Location = new Float2(
                    barLeft, ProgressRowTop + (ProgressRowHeight - ProgressBarThickness) * 0.5f);
            }

            LayoutTipRow();

            if (_continueHint != null)
            {
                float hintWidth = _continueHint.PreferredWidth;
                _continueHint.Size = new Float2(hintWidth, TipsRowHeight);
                _continueHint.Location = new Float2(
                    (w - hintWidth) * 0.5f, TipsRowTop);
            }
        }

        /// <summary>
        /// 重排提示行（图标 + 提示文字 + 脉动点 + 版本号）。
        /// </summary>
        private void LayoutTipRow()
        {
            if (_tipGlyph != null)
            {
                _tipGlyph.Size = new Float2(TipGlyphSize, TipGlyphSize);
                _tipGlyph.Location = new Float2(
                    SidePadding, TipsRowTop + (TipsRowHeight - TipGlyphSize) * 0.5f);
            }

            if (_version != null)
            {
                _version.Size = new Float2(VersionWidth, TipsRowHeight);
                _version.Location = new Float2(Width - SidePadding - VersionWidth, TipsRowTop);
            }

            if (_tipText == null)
                return;

            float textWidth = LoadingDecor.EstimateTextWidth(_tipText.Text, TipFontSize);
            float textLeft = SidePadding + TipGlyphSize + TipGlyphGap;
            _tipText.Size = new Float2(textWidth + 4f, TipsRowHeight);
            _tipText.Location = new Float2(textLeft, TipsRowTop);

            if (_tipDots != null)
            {
                float dotsWidth = _tipDots.DotCount * _tipDots.DotSize +
                                  (_tipDots.DotCount - 1) * _tipDots.DotGap;
                _tipDots.Size = new Float2(dotsWidth, _tipDots.DotSize);
                _tipDots.Location = new Float2(
                    textLeft + textWidth + TipGlyphGap,
                    TipsRowTop + (TipsRowHeight - _tipDots.DotSize) * 0.5f);
            }
        }

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 贴底竖向渐变遮罩：transparent → rgba(14,16,22,0.7) → rgba(14,16,22,0.95)
            var top = new Color(
                InkWashTheme.BaseDefault.R, InkWashTheme.BaseDefault.G, InkWashTheme.BaseDefault.B, 0f);
            var mid = LoadingDecor.WithAlpha(InkWashTheme.BaseDefault, ScrimMidAlpha);
            var bottom = LoadingDecor.WithAlpha(InkWashTheme.BaseDefault, ScrimBottomAlpha);
            LoadingDecor.FillVerticalGradient(
                new Rectangle(0f, 0f, Width, Height), top, mid, bottom, ScrimMidStop, ScrimGradientSteps);

            // 子控件（进度行 / 提示行）
            base.Draw();
        }
    }

    // =======================================================================

    /// <summary>
    /// 加载页绘制辅助方法。
    /// 提供 <see cref="Render2D"/> 不直接支持的三段竖向渐变、
    /// 任意起始角的圆弧折线绘制、CJK 文本宽度估算与颜色透明度调整。
    /// </summary>
    internal static class LoadingDecor
    {
        /// <summary>
        /// 绘制三段竖向渐变（上 → 中 → 下），用水平色带近似。
        /// </summary>
        /// <param name="rect">目标矩形</param>
        /// <param name="top">顶部颜色</param>
        /// <param name="mid">中段颜色</param>
        /// <param name="bottom">底部颜色</param>
        /// <param name="midStop">中段位置（0-1）</param>
        /// <param name="steps">色带数量</param>
        internal static void FillVerticalGradient(
            Rectangle rect, Color top, Color mid, Color bottom, float midStop, int steps)
        {
            if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
                return;

            steps = Mathf.Max(2, steps);
            midStop = Mathf.Clamp(midStop, 0.01f, 0.99f);
            float bandHeight = rect.Size.Y / steps;

            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                Color color = t <= midStop
                    ? Color.Lerp(top, mid, t / midStop)
                    : Color.Lerp(mid, bottom, (t - midStop) / (1f - midStop));

                Render2D.FillRectangle(
                    new Rectangle(rect.Location.X, rect.Location.Y + i * bandHeight, rect.Size.X, bandHeight + 1f),
                    color);
            }
        }

        /// <summary>
        /// 绘制指定起始角与扫描角的圆弧（折线近似）。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">半径</param>
        /// <param name="startAngle">起始角（弧度）</param>
        /// <param name="sweep">扫描角（弧度，正负决定方向）</param>
        /// <param name="color">颜色</param>
        /// <param name="thickness">线宽</param>
        /// <param name="segments">折线段数</param>
        internal static void DrawArc(
            Float2 center, float radius, float startAngle, float sweep,
            Color color, float thickness, int segments)
        {
            if (radius <= 0f || segments <= 0 || color.A <= 0f)
                return;

            float step = sweep / segments;
            var prev = center + new Float2(Mathf.Cos(startAngle) * radius, Mathf.Sin(startAngle) * radius);

            for (int i = 1; i <= segments; i++)
            {
                float angle = startAngle + step * i;
                var cur = center + new Float2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                Render2D.DrawLine(prev, cur, color, thickness);
                prev = cur;
            }
        }

        /// <summary>
        /// 估算文本像素宽度：中日韩全角字符按 1 个字宽计算，其余按 0.55 个字宽计算。
        /// 用于让行内元素（如提示文字后的脉动点）紧贴文字排布。
        /// </summary>
        /// <param name="text">文本</param>
        /// <param name="fontSize">字号</param>
        /// <returns>估算宽度（像素）</returns>
        internal static float EstimateTextWidth(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;

            float width = 0f;
            for (int i = 0; i < text.Length; i++)
                width += text[i] < 128 ? fontSize * 0.55f : fontSize;
            return width;
        }

        /// <summary>
        /// 返回按系数调整 alpha 后的颜色（系数自动钳制到 0-1）。
        /// </summary>
        /// <param name="color">基础颜色</param>
        /// <param name="alpha">alpha 系数</param>
        /// <returns>调整后的颜色</returns>
        internal static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.R, color.G, color.B, color.A * Mathf.Clamp(alpha, 0f, 1f));
        }
    }
}
