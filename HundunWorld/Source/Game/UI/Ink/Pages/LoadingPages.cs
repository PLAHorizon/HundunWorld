using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 加载页面 1（首屏加载）。
    /// 全屏横屏水墨背景 + 右侧竖排书法标题"江湖初启" + 底部鎏金进度条 + 进度数值。
    /// 进度满时触发 <see cref="ProgressComplete"/> 事件，
    /// 由 <see cref="InkPageRouter"/> 订阅后自动推进到下一页（LoadingPage2 或 ChapterTransitionPage）。
    /// </summary>
    public class LoadingPage1 : ContainerControl, IInkPage
    {
        /// <summary>自动递增进度的速率（每秒 +0.1，对应 10 秒满）</summary>
        private const float AutoProgressRate = 0.1f;

        /// <summary>进度数值标签尺寸</summary>
        private static readonly Float2 ProgressLabelSize = new Float2(80f, 20f);

        /// <summary>屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>全屏背景图控件</summary>
        private readonly Image _backgroundImage;

        /// <summary>竖排书法标题</summary>
        private readonly InkVerticalTitle _verticalTitle;

        /// <summary>底部进度条</summary>
        private readonly InkBar _progressBar;

        /// <summary>进度数值标签（DIN 字体）</summary>
        private readonly Label _progressLabel;

        /// <summary>当前进度值（0.0~1.0）</summary>
        private float _progress;

        /// <summary>是否已触发完成事件（避免重复触发）</summary>
        private bool _completed;

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

            // 1. 全屏背景图
            _backgroundImage = LoadingPageHelper.CreateBackgroundImage(
                InkWashTheme.TexAssetPathLoadingLandscape, nameof(LoadingPage1), _screenSize);
            AddChild(_backgroundImage);

            // 2. 竖排书法标题："江湖初启"
            _verticalTitle = new InkVerticalTitle
            {
                Text = "江湖初启",
                FontSize = 32f,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_verticalTitle);

            // 3. 底部进度条：Gold 变体
            _progressBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Value = 0f,
            };
            AddChild(_progressBar);

            // 4. 进度数值 Label：DIN 字体
            _progressLabel = new Label
            {
                Text = "0%",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = ProgressLabelSize,
            };
            AddChild(_progressLabel);

            // 应用初始布局
            LoadingPageHelper.ApplyLoadingLayout(
                _backgroundImage, _verticalTitle, _progressBar, _progressLabel, _screenSize);
        }

        /// <summary>
        /// 更新进度条与数值。
        /// value 会被钳制到 0-1 范围；value &gt;= 1 时触发 <see cref="ProgressComplete"/> 事件（仅触发一次）。
        /// </summary>
        /// <param name="value">进度值（0-1）</param>
        public void SetProgress(float value)
        {
            LoadingPageHelper.SetProgressInternal(
                _progressBar, _progressLabel,
                ref _progress, ref _completed,
                value, ProgressComplete, nameof(LoadingPage1));
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 外部（如 <see cref="InkPageShell"/> 或屏幕大小变更监听器）应调用此方法。
        /// </summary>
        public void RefreshLayout()
        {
            // 优先使用控件实际尺寸（已由 InkPageShell.LoadPage 的 StretchAll 锚点填充父容器）
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                // 控件尚未布局，回退到屏幕尺寸
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                // 仍然为 0，使用 1920x1080 兜底
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            LoadingPageHelper.ApplyLoadingLayout(
                _backgroundImage, _verticalTitle, _progressBar, _progressLabel, _screenSize);
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
    /// 全屏山峦水墨背景 + 右侧竖排书法标题"远峰在望" + 底部鎏金进度条 + 进度数值。
    /// 进度满时触发 <see cref="ProgressComplete"/> 事件，
    /// 由 <see cref="InkPageRouter"/> 订阅后自动推进到下一页。
    /// </summary>
    public class LoadingPage2 : ContainerControl, IInkPage
    {
        /// <summary>自动递增进度的速率（每秒 +0.1，对应 10 秒满）</summary>
        private const float AutoProgressRate = 0.1f;

        /// <summary>进度数值标签尺寸</summary>
        private static readonly Float2 ProgressLabelSize = new Float2(80f, 20f);

        /// <summary>屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>全屏背景图控件</summary>
        private readonly Image _backgroundImage;

        /// <summary>竖排书法标题</summary>
        private readonly InkVerticalTitle _verticalTitle;

        /// <summary>底部进度条</summary>
        private readonly InkBar _progressBar;

        /// <summary>进度数值标签（DIN 字体）</summary>
        private readonly Label _progressLabel;

        /// <summary>当前进度值（0.0~1.0）</summary>
        private float _progress;

        /// <summary>是否已触发完成事件（避免重复触发）</summary>
        private bool _completed;

        /// <summary>
        /// 进度满时触发的事件。
        /// 由 <see cref="InkPageRouter"/> 订阅后自动推进到下一页。
        /// 仅在进度首次到达 1.0 时触发一次。
        /// </summary>
        public event Action ProgressComplete;

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

            // 1. 全屏背景图
            _backgroundImage = LoadingPageHelper.CreateBackgroundImage(
                InkWashTheme.TexAssetPathLoadingMountain, nameof(LoadingPage2), _screenSize);
            AddChild(_backgroundImage);

            // 2. 竖排书法标题："远峰在望"
            _verticalTitle = new InkVerticalTitle
            {
                Text = "远峰在望",
                FontSize = 32f,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_verticalTitle);

            // 3. 底部进度条：Gold 变体
            _progressBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Value = 0f,
            };
            AddChild(_progressBar);

            // 4. 进度数值 Label：DIN 字体
            _progressLabel = new Label
            {
                Text = "0%",
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                TextColor = InkWashTheme.TextBrand,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = ProgressLabelSize,
            };
            AddChild(_progressLabel);

            // 应用初始布局
            LoadingPageHelper.ApplyLoadingLayout(
                _backgroundImage, _verticalTitle, _progressBar, _progressLabel, _screenSize);
        }

        /// <summary>
        /// 更新进度条与数值。
        /// value 会被钳制到 0-1 范围；value &gt;= 1 时触发 <see cref="ProgressComplete"/> 事件（仅触发一次）。
        /// </summary>
        /// <param name="value">进度值（0-1）</param>
        public void SetProgress(float value)
        {
            LoadingPageHelper.SetProgressInternal(
                _progressBar, _progressLabel,
                ref _progress, ref _completed,
                value, ProgressComplete, nameof(LoadingPage2));
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局所有子控件。
        /// 外部（如 <see cref="InkPageShell"/> 或屏幕大小变更监听器）应调用此方法。
        /// </summary>
        public void RefreshLayout()
        {
            // 优先使用控件实际尺寸（已由 InkPageShell.LoadPage 的 StretchAll 锚点填充父容器）
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                // 控件尚未布局，回退到屏幕尺寸
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                // 仍然为 0，使用 1920x1080 兜底
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            LoadingPageHelper.ApplyLoadingLayout(
                _backgroundImage, _verticalTitle, _progressBar, _progressLabel, _screenSize);
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
            // 优先使用控件实际尺寸（已由 InkPageShell.LoadPage 的 StretchAll 锚点填充父容器）
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                // 控件尚未布局，回退到屏幕尺寸
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                // 仍然为 0，使用 1920x1080 兜底
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
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
    /// 提供 LoadingPage1 / LoadingPage2 共用的屏幕尺寸解析、背景图创建、布局应用与进度设置逻辑，
    /// 避免两个相似页面类之间的代码重复。
    /// </summary>
    internal static class LoadingPageHelper
    {
        /// <summary>屏幕尺寸未就绪时的兜底值（1920x1080）</summary>
        private static readonly Float2 FallbackScreenSize = new Float2(1920f, 1080f);

        /// <summary>进度数值标签尺寸</summary>
        private static readonly Float2 ProgressLabelSize = new Float2(80f, 20f);

        /// <summary>竖排标题尺寸</summary>
        private static readonly Float2 VerticalTitleSize = new Float2(60f, 200f);

        /// <summary>进度条尺寸</summary>
        private static readonly Float2 ProgressBarSize = new Float2(400f, 8f);

        /// <summary>竖排标题右侧偏移（screenWidth - 120）</summary>
        private const float VerticalTitleRightOffset = 120f;

        /// <summary>竖排标题垂直偏移（screenHeight/2 - 100）</summary>
        private const float VerticalTitleVerticalOffset = 100f;

        /// <summary>进度条水平半宽（用于居中计算）</summary>
        private const float ProgressBarHalfWidth = 200f;

        /// <summary>进度条距离屏幕底部的偏移</summary>
        private const float ProgressBarBottomOffset = 80f;

        /// <summary>进度数值标签距离进度条左端的偏移</summary>
        private const float ProgressLabelRightOffset = 210f;

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
        /// 应用加载页通用布局：
        /// 背景图全屏 + 竖排标题右侧中部 + 进度条底部居中 + 进度数值标签进度条右侧。
        /// </summary>
        /// <param name="bg">背景图控件</param>
        /// <param name="title">竖排标题控件</param>
        /// <param name="bar">进度条控件</param>
        /// <param name="label">进度数值标签控件</param>
        /// <param name="screenSize">当前屏幕尺寸</param>
        public static void ApplyLoadingLayout(
            Image bg, InkVerticalTitle title, InkBar bar, Label label, Float2 screenSize)
        {
            float sw = screenSize.X;
            float sh = screenSize.Y;

            if (bg != null)
            {
                bg.Location = Float2.Zero;
                bg.Size = screenSize;
            }

            // 竖排标题：(sw - 120, sh/2 - 100)，尺寸 (60, 200)
            if (title != null)
            {
                title.Location = new Float2(
                    sw - VerticalTitleRightOffset,
                    sh * 0.5f - VerticalTitleVerticalOffset);
                title.Size = VerticalTitleSize;
            }

            // 进度条：(sw/2 - 200, sh - 80)，尺寸 (400, 8)
            if (bar != null)
            {
                bar.Location = new Float2(
                    sw * 0.5f - ProgressBarHalfWidth,
                    sh - ProgressBarBottomOffset);
                bar.Size = ProgressBarSize;
            }

            // 进度数值标签：(sw/2 + 210, sh - 80)
            if (label != null)
            {
                label.Location = new Float2(
                    sw * 0.5f + ProgressLabelRightOffset,
                    sh - ProgressBarBottomOffset);
                label.Size = ProgressLabelSize;
            }
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
}
