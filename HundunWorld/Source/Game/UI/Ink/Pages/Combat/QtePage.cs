using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// QTE 千钧一发页面。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 2-3 个 <see cref="InkSplash"/> 水墨氛围装饰，
    /// 中央覆写 <see cref="Draw"/> 绘制三层圆环计时器：
    /// <list type="bullet">
    ///   <item>最外层朱红倒计时弧（<see cref="InkWashTheme.VermilionPrimary"/>，随 <see cref="_timeRemaining"/> 顺时针收缩）</item>
    ///   <item>中层墨色渐变环（<see cref="InkWashTheme.BaseSecondary"/>）</item>
    ///   <item>内层金色细线环（<see cref="InkWashTheme.GoldPrimary"/>）</item>
    /// </list>
    /// 圆环中心为按键提示（<see cref="InkTextBlock"/> Display 样式，大字号显示 mock 按键如 "Q"/"E"/"Space"），
    /// 右下角为连击显示（"连击 ×3" mock），顶部为竖排书法"破招"标识，左下角为操作说明。
    /// <see cref="Update"/> 每帧递减 <see cref="_timeRemaining"/>，归零触发 <see cref="QteFailed"/>；
    /// 外部输入系统在正确按键时调用 <see cref="NotifyCorrectKey"/> 触发 <see cref="QteSucceeded"/>。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class QtePage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>最外层朱红倒计时圆环半径（像素）</summary>
        private const float RingTimerRadius = 184f;

        /// <summary>中层墨色渐变环半径（像素）</summary>
        private const float RingInkRadius = 170f;

        /// <summary>内层金色细线环半径（像素）</summary>
        private const float RingGoldRadius = 110f;

        /// <summary>朱红倒计时弧线宽（像素）</summary>
        private const float RingTimerThickness = 4f;

        /// <summary>墨色环线宽（像素）</summary>
        private const float RingInkThickness = 2f;

        /// <summary>金色内环线宽（像素）</summary>
        private const float RingGoldThickness = 1.5f;

        /// <summary>圆环近似分段数（越大越圆滑）</summary>
        private const int CircleSegments = 64;

        /// <summary>按键提示文本控件尺寸（正方形）</summary>
        private const float KeyPromptSize = 200f;

        /// <summary>按键提示字号（像素，对应 HTML 84px 书法大字）</summary>
        private const float KeyPromptFontSize = 84f;

        /// <summary>连击显示文本宽度（像素）</summary>
        private const float ComboWidth = 200f;

        /// <summary>连击显示文本高度（像素）</summary>
        private const float ComboHeight = 32f;

        /// <summary>连击显示距屏幕右边的边距（像素）</summary>
        private const float ComboMarginRight = 48f;

        /// <summary>连击显示距屏幕底部的边距（像素）</summary>
        private const float ComboMarginBottom = 40f;

        /// <summary>顶部"破招"竖排标题宽度（像素）</summary>
        private const float TitleWidth = 30f;

        /// <summary>顶部"破招"竖排标题高度（像素）</summary>
        private const float TitleHeight = 90f;

        /// <summary>顶部标题距屏幕顶部的边距（像素）</summary>
        private const float TitleMarginTop = 32f;

        /// <summary>顶部"破招"标题字号（像素）</summary>
        private const float TitleFontSize = 22f;

        /// <summary>左下角操作说明文本宽度（像素）</summary>
        private const float InstructionWidth = 320f;

        /// <summary>左下角操作说明文本高度（像素）</summary>
        private const float InstructionHeight = 30f;

        /// <summary>操作说明距屏幕左边的边距（像素）</summary>
        private const float InstructionMarginLeft = 48f;

        /// <summary>操作说明距屏幕底部的边距（像素）</summary>
        private const float InstructionMarginBottom = 40f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>水墨晕染装饰 1（左上角 Normal 变体）</summary>
        private InkSplash _splash1;

        /// <summary>水墨晕染装饰 2（右下角 Vermilion 变体）</summary>
        private InkSplash _splash2;

        /// <summary>水墨晕染装饰 3（中右 Elevated 变体）</summary>
        private InkSplash _splash3;

        /// <summary>圆环中心按键提示文本（Display 样式，大字号）</summary>
        private InkTextBlock _keyPromptText;

        /// <summary>右下角连击显示文本</summary>
        private InkTextBlock _comboText;

        /// <summary>顶部"破招"竖排书法标识</summary>
        private InkVerticalTitle _titleText;

        /// <summary>左下角操作说明文本</summary>
        private InkTextBlock _instructionText;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>QTE 总时长（秒），mock 默认 3 秒</summary>
        private float _totalTime = 3f;

        /// <summary>当前剩余时间（秒），由 <see cref="Update"/> 递减</summary>
        private float _timeRemaining = 3f;

        /// <summary>QTE 是否已结算（成功或失败），避免重复触发事件</summary>
        private bool _finished;

        /// <summary>mock 待按按键文本，如 "Q"/"E"/"Space"</summary>
        private string _mockKey = "Q";

        /// <summary>mock 连击数，显示为"连击 ×3"</summary>
        private int _mockCombo = 3;

        /// <summary>mock QTE 类型标识，显示为顶部竖排书法</summary>
        private string _mockQteType = "破招";

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // 公共 API：事件
        // =======================================================================

        /// <summary>
        /// QTE 失败事件。
        /// 当 <see cref="_timeRemaining"/> 递减至 0 时由 <see cref="Update"/> 触发，
        /// 表示玩家未在时限内按下正确按键。
        /// </summary>
        public event Action QteFailed;

        /// <summary>
        /// QTE 成功事件。
        /// 当外部输入系统检测到正确按键并调用 <see cref="NotifyCorrectKey"/> 时触发，
        /// 表示玩家在时限内完成破招。
        /// </summary>
        public event Action QteSucceeded;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化水墨氛围遮罩、圆环计时器与全部 mock 文本。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public QtePage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                // 屏幕尺寸尚未就绪时使用 1920x1080 兜底
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 透明背景（Scrim 在 Draw 中手动绘制）+ 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildSplashDecorations();
                BuildKeyPrompt();
                BuildCombo();
                BuildTitle();
                BuildInstruction();

                // 应用初始布局（基于屏幕尺寸计算所有子控件位置）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[QtePage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 15.2：添加 3 个 <see cref="InkSplash"/> 水墨氛围装饰。
        /// 不同位置与变体（Normal/Vermilion/Elevated），半透明、不接收鼠标事件，
        /// 营造千钧一发的水墨沉浸感。
        /// </summary>
        private void BuildSplashDecorations()
        {
            _splash1 = new InkSplash
            {
                Variant = InkSplashVariant.Normal,
                Opacity = 0.14f,
                AutoFocus = false,
            };
            _splash2 = new InkSplash
            {
                Variant = InkSplashVariant.Vermilion,
                Opacity = 0.18f,
                AutoFocus = false,
            };
            _splash3 = new InkSplash
            {
                Variant = InkSplashVariant.Elevated,
                Opacity = 0.12f,
                AutoFocus = false,
            };
            AddChild(_splash1);
            AddChild(_splash2);
            AddChild(_splash3);
        }

        /// <summary>
        /// SubTask 15.3：圆环中心按键提示。
        /// <see cref="InkTextBlock"/> Display 样式（毛笔书法字体），覆写字号为 84px 大字，
        /// 字色覆写为 <see cref="InkWashTheme.PaperBright"/>（纸白色，对应 HTML .qte-key-letter），
        /// 文本为 <see cref="_mockKey"/>（mock 按键如 "Q"/"E"/"Space"）。
        /// 位置由 <see cref="ApplyLayout"/> 基于屏幕中心计算。
        /// </summary>
        private void BuildKeyPrompt()
        {
            _keyPromptText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = _mockKey,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(KeyPromptSize, KeyPromptSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), KeyPromptFontSize),
            };
            AddChild(_keyPromptText);
        }

        /// <summary>
        /// SubTask 15.3：右下角连击显示。
        /// <see cref="InkTextBlock"/> Heading 样式，字色覆写为 <see cref="InkWashTheme.TextBrand"/>（鎏金），
        /// 文本"连击 ×3"（mock），位置由 <see cref="ApplyLayout"/> 基于屏幕右下角计算。
        /// </summary>
        private void BuildCombo()
        {
            _comboText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = $"连击 ×{_mockCombo}",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(ComboWidth, ComboHeight),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
            };
            AddChild(_comboText);
        }

        /// <summary>
        /// 顶部"破招"竖排书法标识。
        /// <see cref="InkVerticalTitle"/> 使用毛笔书法字体（Display），文本为 <see cref="_mockQteType"/>，
        /// 字号 22px，品牌鎏金色，对应 HTML 顶部 .ink-vertical-title"破招"。
        /// 位置由 <see cref="ApplyLayout"/> 基于屏幕顶部居中计算。
        /// </summary>
        private void BuildTitle()
        {
            _titleText = new InkVerticalTitle
            {
                Text = _mockQteType,
                FontSize = TitleFontSize,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(TitleWidth, TitleHeight),
            };
            AddChild(_titleText);
        }

        /// <summary>
        /// 左下角操作说明文本。
        /// <see cref="InkTextBlock"/> Subheading 样式，字色覆写为 <see cref="InkWashTheme.PaperAged"/>（陈旧纸色），
        /// 文本"按 Q 键发动破招"（mock），对应 HTML .qte-instruction。
        /// 位置由 <see cref="ApplyLayout"/> 基于屏幕左下角计算。
        /// </summary>
        private void BuildInstruction()
        {
            _instructionText = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = $"按 {_mockKey} 键发动{_mockQteType}",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(InstructionWidth, InstructionHeight),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 15f),
            };
            AddChild(_instructionText);
        }

        // ===================================================================
        // SubTask 15.4：Update 生命周期驱动倒计时
        // =======================================================================

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 已结算则不再推进计时
            if (_finished)
                return;

            _timeRemaining -= deltaTime;
            if (_timeRemaining <= 0f)
            {
                _timeRemaining = 0f;
                _finished = true;
                try
                {
                    QteFailed?.Invoke();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[QtePage] QteFailed 触发失败: {ex.Message}");
                }
            }
        }

        // ===================================================================
        // SubTask 15.3：Draw 生命周期绘制三层圆环计时器
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            // 1. 半透明遮罩（Scrim）— 手动绘制，避免 base.Draw 背景覆盖圆环
            Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), InkWashTheme.Scrim);

            // 2. 屏幕中心三层圆环计时器
            var center = new Float2(Width * 0.5f, Height * 0.5f);
            float progress = _totalTime > 0f
                ? Mathf.Clamp(_timeRemaining / _totalTime, 0f, 1f)
                : 0f;

            // 2a. 中层墨色环（底层）
            DrawCircleRing(center, RingInkRadius,
                new Color(InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G,
                          InkWashTheme.BaseSecondary.B, 0.85f),
                RingInkThickness);

            // 2b. 内层金色细线环
            DrawCircleRing(center, RingGoldRadius, InkWashTheme.GoldPrimary, RingGoldThickness);

            // 2c. 最外层朱红倒计时弧
            //    背景轨道（整圆弱朱红）+ 剩余进度弧（顺时针从正北收缩）
            var fadedVermilion = new Color(
                InkWashTheme.VermilionPrimary.R,
                InkWashTheme.VermilionPrimary.G,
                InkWashTheme.VermilionPrimary.B,
                0.08f);
            DrawCircleRing(center, RingTimerRadius, fadedVermilion, RingTimerThickness);

            if (progress > 0f)
            {
                // 屏幕坐标系：正北 = -π/2，顺时针 = 角度递增
                const float startAngle = -Mathf.PiOverTwo;
                DrawCircleArc(center, RingTimerRadius, InkWashTheme.VermilionPrimary,
                    RingTimerThickness, startAngle, progress * Mathf.TwoPi);
            }

            // 3. 调用 base.Draw 绘制子控件（按键提示、连击、标题、说明、splash 装饰）
            //    背景为 Transparent，base 仅绘制子控件，叠加于圆环之上
            base.Draw();
        }

        // ===================================================================
        // 公共 API：按键输入与布局
        // =======================================================================

        /// <summary>
        /// 通知 QTE 已按下正确按键。
        /// 由外部输入系统在检测到 <see cref="_mockKey"/> 被按下时调用，
        /// 触发 <see cref="QteSucceeded"/> 事件并标记 QTE 已结算。
        /// 若 QTE 已结算（成功或失败）则忽略后续调用。
        /// </summary>
        public void NotifyCorrectKey()
        {
            if (_finished)
                return;

            _finished = true;
            try
            {
                QteSucceeded?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[QtePage] QteSucceeded 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局遮罩与所有子控件。
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

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件位置。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;
            float centerX = sw * 0.5f;
            float centerY = sh * 0.5f;

            // 水墨晕染装饰：分散在屏幕四角与中右，营造氛围
            // splash1（Normal 300x300）：左上角部分溢出
            if (_splash1 != null)
            {
                _splash1.Location = new Float2(-80f, -100f);
            }
            // splash2（Vermilion 250x250）：右下角
            if (_splash2 != null)
            {
                _splash2.Location = new Float2(sw - 250f + 60f, sh - 250f + 120f);
            }
            // splash3（Elevated 200x200）：中右偏上
            if (_splash3 != null)
            {
                _splash3.Location = new Float2(sw * 0.78f, sh * 0.28f);
            }

            // 按键提示：屏幕中心
            if (_keyPromptText != null)
            {
                _keyPromptText.Location = new Float2(
                    centerX - KeyPromptSize * 0.5f,
                    centerY - KeyPromptSize * 0.5f);
            }

            // 连击显示：右下角
            if (_comboText != null)
            {
                _comboText.Location = new Float2(
                    sw - ComboWidth - ComboMarginRight,
                    sh - ComboHeight - ComboMarginBottom);
            }

            // 顶部"破招"标题：顶部居中
            if (_titleText != null)
            {
                _titleText.Location = new Float2(
                    centerX - TitleWidth * 0.5f,
                    TitleMarginTop);
            }

            // 左下角操作说明
            if (_instructionText != null)
            {
                _instructionText.Location = new Float2(
                    InstructionMarginLeft,
                    sh - InstructionHeight - InstructionMarginBottom);
            }
        }

        // ===================================================================
        // 圆环绘制辅助
        // =======================================================================

        /// <summary>
        /// 使用多段 <see cref="Render2D.DrawLine"/> 近似绘制完整圆环描边。
        /// 参考 InkMinimap 的 DrawLine 近似圆环实现。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">圆半径（像素）</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽（像素）</param>
        private static void DrawCircleRing(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;

            float angleStep = Mathf.TwoPi / CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float a1 = i * angleStep;
                float a2 = (i + 1) * angleStep;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }

        /// <summary>
        /// 使用多段 <see cref="Render2D.DrawLine"/> 近似绘制圆弧。
        /// 从 <paramref name="startAngle"/> 起，沿角度递增方向（屏幕坐标系即顺时针）扫描
        /// <paramref name="sweepRadians"/> 弧度。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">圆半径（像素）</param>
        /// <param name="color">描边颜色</param>
        /// <param name="thickness">线宽（像素）</param>
        /// <param name="startAngle">起始角度（弧度，屏幕坐标系：-π/2 = 正北）</param>
        /// <param name="sweepRadians">扫描弧度（正值 = 顺时针）</param>
        private static void DrawCircleArc(Float2 center, float radius, Color color, float thickness,
            float startAngle, float sweepRadians)
        {
            if (radius <= 0f || sweepRadians <= 0f)
                return;

            int segments = Mathf.CeilToInt((sweepRadians / Mathf.TwoPi) * CircleSegments);
            if (segments < 1)
                segments = 1;

            float step = sweepRadians / segments;
            for (int i = 0; i < segments; i++)
            {
                float a1 = startAngle + i * step;
                float a2 = startAngle + (i + 1) * step;
                var p1 = center + new Float2(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
                var p2 = center + new Float2(Mathf.Cos(a2) * radius, Mathf.Sin(a2) * radius);
                Render2D.DrawLine(p1, p2, color, thickness);
            }
        }
    }
}
