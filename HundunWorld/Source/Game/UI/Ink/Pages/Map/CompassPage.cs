using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Map
{
    /// <summary>
    /// 司南页面 — 对应 compass.html 设计原型。
    /// <para>
    /// 自由探索模式下的方位导航页面，提供：
    /// <list type="bullet">
    ///   <item>顶部：标题"司南" + 副标题"天机方位 · 寻龙点脉" + 关闭按钮（返回战斗 HUD）</item>
    ///   <item>中央：大型水墨指南针圆盘（天干外环 + 八卦内环 + 北东南西四方 + POI 方位标记 + 摆动指针）</item>
    ///   <item>底部：朝向/坐标/区域三联信息 + 附近 POI 列表（3 项）+ 罗盘/地图模式切换 + 追踪目标</item>
    /// </list>
    /// 圆盘通过 <see cref="Draw"/> 自定义 Render2D 绘制；其余区域使用 <see cref="InkPanel"/> + <see cref="Label"/> + <see cref="InkButton"/> 子控件。
    /// 通过 <see cref="NavigationRequested"/> 事件向路由器暴露导航请求，dom-id 为 <see cref="InkPageDomIds.NavCompass"/>。
    /// </para>
    /// </summary>
    public class CompassPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>顶部标题栏高度</summary>
        private const float HeaderHeight = 56f;

        /// <summary>顶部标题栏宽度</summary>
        private const float HeaderWidth = 780f;

        /// <summary>底部信息面板高度（三联信息 + POI 列表 + 模式切换）</summary>
        private const float BottomPanelHeight = 240f;

        /// <summary>底部信息面板宽度</summary>
        private const float BottomPanelWidth = 780f;

        /// <summary>指南针圆盘直径</summary>
        private const float DialDiameter = 500f;

        /// <summary>指南针圆盘半径</summary>
        private const float DialRadius = DialDiameter * 0.5f;

        /// <summary>天干环半径（最外层文字环）</summary>
        private const float RingTianganRadius = 220f;

        /// <summary>八卦环半径</summary>
        private const float RingBaguaRadius = 168f;

        /// <summary>四方环半径（北东南西）</summary>
        private const float RingCardinalRadius = 112f;

        /// <summary>POI 标记环半径（最外层之外）</summary>
        private const float RingPoiRadius = 246f;

        /// <summary>中心枢轴半径</summary>
        private const float HubRadius = 24f;

        /// <summary>指针长度（从中心向尖端）</summary>
        private const float NeedleLength = 110f;

        /// <summary>指针底部半宽</summary>
        private const float NeedleBaseHalf = 7f;

        /// <summary>屏幕边缘留白</summary>
        private const float ScreenEdge = 16f;

        /// <summary>圆形描边分段数（用于 DrawCircleStroke）</summary>
        private const int CircleStrokeSegments = 48;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏面板</summary>
        private InkPanel _header;

        /// <summary>标题文字"司南"</summary>
        private Label _titleLabel;

        /// <summary>副标题文字</summary>
        private Label _subtitleLabel;

        /// <summary>关闭按钮（返回战斗 HUD）</summary>
        private InkButton _closeButton;

        /// <summary>底部信息面板</summary>
        private InkPanel _bottomPanel;

        /// <summary>朝向数值标签</summary>
        private Label _facingLabel;

        /// <summary>坐标数值标签</summary>
        private Label _coordLabel;

        /// <summary>区域名称标签</summary>
        private Label _regionLabel;

        /// <summary>POI 数量标签</summary>
        private Label _poiCountLabel;

        /// <summary>3 个 POI 行容器</summary>
        private ContainerControl[] _poiRows;

        /// <summary>罗盘模式按钮（当前激活）</summary>
        private InkButton _compassModeBtn;

        /// <summary>地图模式按钮（跳转世界地图）</summary>
        private InkButton _mapModeBtn;

        /// <summary>追踪目标按钮</summary>
        private InkButton _trackingTargetBtn;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。触发后由 MainUIManager 订阅并调用 InkPageRouter.NavigateTo。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由 MainUIManager 注入）。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化所有子控件。
        /// </summary>
        public CompassPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildBottomPanel();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：司南标题 + 副标题 + 关闭按钮。
        /// </summary>
        private void BuildHeader()
        {
            _header = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(HeaderWidth, HeaderHeight),
            };

            // 标题"司南"
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(80f, 32f),
                Text = "司南",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(_titleLabel);

            // 副标题"天机方位 · 寻龙点脉"
            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(104f, 18f),
                Size = new Float2(240f, 20f),
                Text = "天机方位 · 寻龙点脉",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _header.AddChild(_subtitleLabel);

            // 关闭按钮（右侧）
            _closeButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "关闭",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(HeaderWidth - 80f - 12f, 12f),
                Size = new Float2(80f, 32f),
            };
            _closeButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _header.AddChild(_closeButton);

            AddChild(_header);
        }

        /// <summary>
        /// 构建底部信息面板：朝向/坐标/区域三联信息 + POI 列表 + 模式切换 + 追踪目标。
        /// </summary>
        private void BuildBottomPanel()
        {
            _bottomPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(BottomPanelWidth, BottomPanelHeight),
            };

            // ========== 三联信息行：朝向 / 坐标 / 区域 ==========
            float colWidth = BottomPanelWidth / 3f;
            float infoY = 8f;

            // 朝向
            var facingTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, infoY + 4f),
                Size = new Float2(colWidth - 12f, 14f),
                Text = "朝向",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _bottomPanel.AddChild(facingTitle);

            _facingLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, infoY + 22f),
                Size = new Float2(colWidth - 12f, 22f),
                Text = "北偏东 15°",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 15f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _bottomPanel.AddChild(_facingLabel);

            // 坐标
            var coordTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colWidth + 12f, infoY + 4f),
                Size = new Float2(colWidth - 12f, 14f),
                Text = "坐标",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _bottomPanel.AddChild(coordTitle);

            _coordLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colWidth + 12f, infoY + 22f),
                Size = new Float2(colWidth - 12f, 22f),
                Text = "X:1234 Y:5678 Z:100",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _bottomPanel.AddChild(_coordLabel);

            // 区域
            var regionTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colWidth * 2f + 12f, infoY + 4f),
                Size = new Float2(colWidth - 12f, 14f),
                Text = "区域",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _bottomPanel.AddChild(regionTitle);

            _regionLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(colWidth * 2f + 12f, infoY + 22f),
                Size = new Float2(colWidth - 12f, 22f),
                Text = "清河 · 开封城郊",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _bottomPanel.AddChild(_regionLabel);

            // 分割线
            float dividerY = infoY + 52f;
            AddDivider(_bottomPanel, 0f, dividerY, BottomPanelWidth);

            // ========== POI 列表标题 ==========
            float poiTitleY = dividerY + 6f;
            var poiListTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, poiTitleY),
                Size = new Float2(200f, 18f),
                Text = "附近方位",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _bottomPanel.AddChild(poiListTitle);

            _poiCountLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(BottomPanelWidth - 80f, poiTitleY),
                Size = new Float2(64f, 18f),
                Text = "3 处",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _bottomPanel.AddChild(_poiCountLabel);

            // ========== 3 个 POI 行 ==========
            _poiRows = new ContainerControl[3];
            string[] poiIcons = { "◆", "●", "★" };
            string[] poiTags = { "界碑", "NPC", "任务" };
            string[] poiNames = { "开封城门", "药师张老", "山贼营地" };
            string[] poiDistances = { "北 200m", "东南 50m", "西 500m" };
            Color[] poiIconColors =
            {
                InkWashTheme.GoldBright,
                InkWashTheme.GoldBright,
                InkWashTheme.JadeBright,
            };
            Color[] poiDistanceColors =
            {
                InkWashTheme.TextSecondary,
                InkWashTheme.TextSecondary,
                InkWashTheme.JadeBright,
            };

            float poiRowY = poiTitleY + 22f;
            float poiRowH = 26f;
            for (int i = 0; i < 3; i++)
            {
                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, poiRowY + i * poiRowH),
                    Size = new Float2(BottomPanelWidth - 32f, poiRowH),
                    BackgroundColor = Color.Transparent,
                };

                // 图标
                var iconLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 0f),
                    Size = new Float2(20f, poiRowH),
                    Text = poiIcons[i],
                    TextColor = poiIconColors[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 15f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(iconLabel);

                // 类型标签
                var tagLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(28f, 4f),
                    Size = new Float2(56f, poiRowH - 8f),
                    Text = poiTags[i],
                    TextColor = InkWashTheme.TextBrand,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    BackgroundColor = new Color(
                        InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B, 0.12f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(tagLabel);

                // 名称
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(96f, 0f),
                    Size = new Float2(BottomPanelWidth - 32f - 96f - 100f, poiRowH),
                    Text = poiNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 13f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(nameLabel);

                // 距离
                var distLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(BottomPanelWidth - 32f - 100f, 0f),
                    Size = new Float2(100f, poiRowH),
                    Text = poiDistances[i],
                    TextColor = poiDistanceColors[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                    HorizontalAlignment = TextAlignment.Far,
                    VerticalAlignment = TextAlignment.Center,
                };
                row.AddChild(distLabel);

                _poiRows[i] = row;
                _bottomPanel.AddChild(row);
            }

            // 分割线
            float modeY = poiRowY + 3 * poiRowH + 4f;
            AddDivider(_bottomPanel, 0f, modeY, BottomPanelWidth);

            // ========== 模式切换 + 追踪目标 ==========
            float modeRowY = modeY + 8f;
            _compassModeBtn = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Sm,
                Text = "罗盘模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, modeRowY),
                Size = new Float2(110f, 30f),
            };
            _bottomPanel.AddChild(_compassModeBtn);

            _mapModeBtn = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "地图模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(132f, modeRowY),
                Size = new Float2(110f, 30f),
            };
            _mapModeBtn.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.NavWorldMap, b);
            _bottomPanel.AddChild(_mapModeBtn);

            // 追踪目标按钮（右侧）
            _trackingTargetBtn = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "追踪：山贼营地",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(BottomPanelWidth - 180f - 16f, modeRowY),
                Size = new Float2(180f, 30f),
            };
            _bottomPanel.AddChild(_trackingTargetBtn);

            AddChild(_bottomPanel);
        }

        /// <summary>
        /// 在指定父面板内添加一条水平分割线。
        /// </summary>
        private void AddDivider(ContainerControl parent, float x, float y, float width)
        {
            var divider = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(width, 1f),
                BackgroundColor = InkWashTheme.Divider,
            };
            parent.AddChild(divider);
        }

        // ===================================================================
        // 自定义绘制：中央指南针圆盘
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            // 1. 先绘制指南针圆盘（位于 header 与 bottomPanel 之间的中央区域）
            try
            {
                DrawCompassDial();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] DrawCompassDial 失败: {ex.Message}");
            }

            // 2. 基类绘制背景（透明）+ 子控件（header, bottomPanel）
            base.Draw();
        }

        /// <summary>
        /// 绘制中央指南针圆盘：外环背景 + 天干环 + 八卦环 + 四方环 + POI 标记 + 中心枢轴 + 摆动指针。
        /// </summary>
        private void DrawCompassDial()
        {
            if (Width <= 0f || Height <= 0f)
                return;

            // 圆盘中心：水平居中，垂直在 header 与 bottomPanel 之间居中
            float cx = Width * 0.5f;
            float availTop = HeaderHeight + ScreenEdge + 8f;
            float availBottom = Height - BottomPanelHeight - ScreenEdge - 8f;
            if (availBottom <= availTop)
                return;
            float cy = (availTop + availBottom) * 0.5f;

            // 1. 外环阴影 + 主背景圆
            InkRenderHelper.FillCircle(new Float2(cx, cy), DialRadius + 4f,
                new Color(8f / 255f, 9f / 255f, 12f / 255f, 0.72f));
            InkRenderHelper.FillCircle(new Float2(cx, cy), DialRadius,
                new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.85f));

            // 2. 外环金色边框
            DrawCircleStroke(new Float2(cx, cy), DialRadius, InkWashTheme.GoldPrimary, 2f);

            // 3. 内部暗色遮罩（增加文字对比度）
            InkRenderHelper.FillCircle(new Float2(cx, cy), DialRadius - 6f,
                new Color(20f / 255f, 23f / 255f, 30f / 255f, 0.34f));

            // 4. 天干环（10 个字）
            string[] tiangan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
            for (int i = 0; i < 10; i++)
            {
                float angle = i * 36f;
                var pos = CompassToScreen(cx, cy, angle, RingTianganRadius);
                DrawChar(pos, tiangan[i], InkWashTheme.TextSecondary, 15f);
            }

            // 5. 八卦环（8 个字，后天八卦序：坎艮震巽离坤兑乾）
            string[] bagua = { "坎", "艮", "震", "巽", "离", "坤", "兑", "乾" };
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                var pos = CompassToScreen(cx, cy, angle, RingBaguaRadius);
                DrawChar(pos, bagua[i], InkWashTheme.GoldDeep, 17f);
            }

            // 6. 四方环（北/东/南/西）
            string[] cardinals = { "北", "东", "南", "西" };
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                var pos = CompassToScreen(cx, cy, angle, RingCardinalRadius);
                DrawChar(pos, cardinals[i], InkWashTheme.GoldPrimary, 30f);
            }

            // 7. POI 方位标记（3 个，对应底部 POI 列表）
            // 北 0° 界碑（金）、东南 135° NPC（金）、西 270° 任务（青，追踪中）
            DrawPoiMarker(CompassToScreen(cx, cy, 0f, RingPoiRadius), "◆", InkWashTheme.GoldBright);
            DrawPoiMarker(CompassToScreen(cx, cy, 135f, RingPoiRadius), "●", InkWashTheme.GoldBright);
            DrawPoiMarker(CompassToScreen(cx, cy, 270f, RingPoiRadius), "★", InkWashTheme.JadeBright);

            // 8. 中心枢轴
            InkRenderHelper.FillCircle(new Float2(cx, cy), HubRadius,
                new Color(26f / 255f, 29f / 255f, 38f / 255f, 1f));
            DrawCircleStroke(new Float2(cx, cy), HubRadius, InkWashTheme.GoldPrimary, 1f);

            // 9. 玩家星标（青色，居中）
            DrawChar(new Float2(cx, cy), "★", InkWashTheme.JadeBright, 26f);

            // 10. 指针（朱红三角，朝向上方=北，带轻微摆动）
            float swayDeg = Mathf.Sin(Time.GameTime * 1.0f) * 4f; // ±4° 摆动
            DrawNeedle(new Float2(cx, cy), swayDeg);
        }

        /// <summary>
        /// 将罗盘角度（0=北，顺时针）转换为屏幕坐标。
        /// 屏幕坐标系：X 向右增加，Y 向下增加。
        /// 罗盘 0° 对应屏幕上方（-Y），90° 对应右（+X）。
        /// </summary>
        private Float2 CompassToScreen(float cx, float cy, float angleDeg, float radius)
        {
            float rad = angleDeg * Mathf.DegreesToRadians;
            return new Float2(
                cx + radius * Mathf.Sin(rad),
                cy - radius * Mathf.Cos(rad));
        }

        /// <summary>
        /// 在指定位置绘制一个汉字（居中对齐）。
        /// </summary>
        private void DrawChar(Float2 pos, string ch, Color color, float size)
        {
            var fontRef = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), size);
            var font = fontRef.GetFont();
            if (font == null)
                return;

            var metrics = font.MeasureText(ch);
            var rect = new Rectangle(
                pos.X - metrics.X * 0.5f,
                pos.Y - metrics.Y * 0.5f,
                metrics.X,
                metrics.Y);
            Render2D.DrawText(font, ch, rect, color,
                TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }

        /// <summary>
        /// 绘制 POI 标记（带辉光的符号）。
        /// </summary>
        private void DrawPoiMarker(Float2 pos, string symbol, Color color)
        {
            // 外发光（多层同心圆递减 alpha）
            Color glow1 = new Color(color.R, color.G, color.B, 0.25f);
            Color glow2 = new Color(color.R, color.G, color.B, 0.5f);
            InkRenderHelper.FillCircle(pos, 14f, glow1);
            InkRenderHelper.FillCircle(pos, 10f, glow2);

            DrawChar(pos, symbol, color, 17f);
        }

        /// <summary>
        /// 绘制圆形描边（用多条短线段近似圆环）。
        /// FlaxEngine Render2D 没有直接的 DrawCircle，故用线段近似。
        /// </summary>
        private void DrawCircleStroke(Float2 center, float radius, Color color, float thickness)
        {
            if (radius <= 0f)
                return;

            Float2 prev = center + new Float2(radius, 0f);
            for (int i = 1; i <= CircleStrokeSegments; i++)
            {
                float a = (i / (float)CircleStrokeSegments) * Mathf.TwoPi;
                var curr = center + new Float2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
                Render2D.DrawLine(prev, curr, color, thickness);
                prev = curr;
            }
        }

        /// <summary>
        /// 绘制指针：朱红三角形（指北）+ 灰色三角形（指南），围绕中心旋转 swayDeg 度。
        /// 0° = 正北（向上），顺时针为正。
        /// </summary>
        private void DrawNeedle(Float2 center, float swayDeg)
        {
            float rad = swayDeg * Mathf.DegreesToRadians;
            float dirX = Mathf.Sin(rad);
            float dirY = -Mathf.Cos(rad);

            // 北指针（朱红，向上）
            var tip = center + new Float2(dirX * NeedleLength, dirY * NeedleLength);
            float perpX = dirY;
            float perpY = -dirX;
            var base1 = center + new Float2(perpX * NeedleBaseHalf, perpY * NeedleBaseHalf);
            var base2 = center - new Float2(perpX * NeedleBaseHalf, perpY * NeedleBaseHalf);

            var vertices = new Float2[3];
            vertices[0] = tip;
            vertices[1] = base1;
            vertices[2] = base2;
            Render2D.FillTriangles(vertices,
                new Color(InkWashTheme.VermilionBright.R, InkWashTheme.VermilionBright.G,
                          InkWashTheme.VermilionBright.B, 0.95f));

            // 南指针（灰色，向下，半透明）
            var tipS = center - new Float2(dirX * NeedleLength * 0.8f, dirY * NeedleLength * 0.8f);
            vertices[0] = tipS;
            vertices[1] = base1;
            vertices[2] = base2;
            Render2D.FillTriangles(vertices,
                new Color(InkWashTheme.TextSecondary.R, InkWashTheme.TextSecondary.G,
                          InkWashTheme.TextSecondary.B, 0.32f));
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 系统导航按钮点击处理：发射金粉粒子 + 触发导航请求。
        /// </summary>
        private void OnSystemNavButtonClicked(string domId, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在按钮中心位置触发金粉爆发粒子反馈。
        /// </summary>
        private void EmitGoldAtButton(Button button)
        {
            try
            {
                if (ParticleSystem == null || button == null)
                    return;

                var buttonCenter = new Float2(button.Width * 0.5f, button.Height * 0.5f);
                var screenPos = button.PointToScreen(buttonCenter);
                var localPos = ParticleSystem.PointFromScreen(screenPos);
                ParticleSystem.EmitGoldBurst(localPos, count: 14, isLarge: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[CompassPage] EmitGoldAtButton 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // IInkPage 实现
        // =======================================================================

        /// <inheritdoc />
        public void RefreshLayout()
        {
            try
            {
                float sw = Width;
                float sh = Height;

                // 顶部标题栏：水平居中，顶部对齐
                if (_header != null)
                {
                    _header.Location = new Float2(sw * 0.5f - HeaderWidth * 0.5f, ScreenEdge);
                }

                // 底部信息面板：水平居中，底部对齐
                if (_bottomPanel != null)
                {
                    _bottomPanel.Location = new Float2(
                        sw * 0.5f - BottomPanelWidth * 0.5f,
                        sh - ScreenEdge - BottomPanelHeight);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CompassPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }
    }
}
