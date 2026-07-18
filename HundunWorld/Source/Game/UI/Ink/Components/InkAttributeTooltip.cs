using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 三段式属性 ToolBar 悬停提示控件。
    /// 对应角色属性面板/装备栏悬停时弹出的属性详情浮窗，采用"头部 + 中部 + 下部"三段式布局：
    /// <list type="bullet">
    ///   <item>头部（48px）：左侧 32x32 图标（<see cref="Texture"/>，为 null 时绘制元素色圆点占位），
    ///     右侧属性名（<see cref="InkWashTheme.FontRole.Heading"/> 15px，<see cref="InkWashTheme.TextBrand"/>），
    ///     底部 1px 金色分隔线（<see cref="InkWashTheme.GoldPrimary"/>）。</item>
    ///   <item>中部（自适应）：核心信息多行文本（<see cref="InkWashTheme.FontRole.Body"/> 12px，<see cref="InkWashTheme.PaperBright"/>），
    ///     如"当前：3200\n基础：2800\n加成：+400"。</item>
    ///   <item>下部（自适应）：附加信息（<see cref="InkWashTheme.FontRole.Body"/> 11px，<see cref="InkWashTheme.TextTertiary"/>）
    ///     与可追加项列表（每行前缀 "·"），底部留 8px 内边距。</item>
    /// </list>
    /// 视觉采用 <see cref="InkWashTheme.BaseSecondary"/> 深色背景 + <see cref="InkWashTheme.BorderGold"/> 2px 金边
    /// + <see cref="InkWashTheme.GoldBright"/> 内描边高亮，外围绘制 4px 偏移半透明黑色阴影。
    /// 固定宽度 240px，高度按内容自适应（最小 180px）。作为顶层浮窗使用，由父控件 <c>AddChild</c> 并置顶。
    /// </summary>
    public class InkAttributeTooltip : ContainerControl
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>控件固定宽度（像素）</summary>
        private const float TooltipWidth = 240f;

        /// <summary>控件最小高度（像素）</summary>
        private const float MinHeight = 180f;

        /// <summary>左右内边距（像素）</summary>
        private const float Padding = 12f;

        /// <summary>头部高度（像素）</summary>
        private const float HeaderHeight = 48f;

        /// <summary>头部图标尺寸（像素）</summary>
        private const float IconSize = 32f;

        /// <summary>段落间距（像素）</summary>
        private const float SectionGap = 8f;

        /// <summary>中部核心信息行高（字号 12，像素）</summary>
        private const float CoreLineHeight = 18f;

        /// <summary>中部核心信息字号</summary>
        private const float CoreFontSize = 12f;

        /// <summary>下部附加信息行高（字号 11，像素）</summary>
        private const float AdditionalLineHeight = 16f;

        /// <summary>下部附加信息字号</summary>
        private const float AdditionalFontSize = 11f;

        /// <summary>追加项行高（像素）</summary>
        private const float AppendLineHeight = 16f;

        /// <summary>附加信息与追加项之间的间距（像素）</summary>
        private const float AppendGap = 4f;

        /// <summary>底部内边距（像素，含底部装饰带空间）</summary>
        private const float BottomPadding = 18f;

        /// <summary>显示位置默认偏移（鼠标右下方 10 像素，左上角基准）</summary>
        private const float ShowOffset = 10f;

        /// <summary>外边框厚度（像素）</summary>
        private const float BorderThickness = 2f;

        /// <summary>内描边高亮厚度（像素）</summary>
        private const float InnerBorderThickness = 1f;

        /// <summary>内描边距外边框的内缩量（像素，距 2px 外边框内侧 1px）</summary>
        private const float InnerBorderInset = 3f;

        /// <summary>阴影偏移量（像素）</summary>
        private const float ShadowOffset = 4f;

        /// <summary>阴影色（半透明黑色）</summary>
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.12f);

        /// <summary>四角金色装饰的尺寸（像素，对应 popup-verification.html 的 corner-ornament 18px）</summary>
        private const float CornerSize = 18f;

        /// <summary>四角金色装饰的线宽（像素）</summary>
        private const float CornerLineThickness = 1f;

        /// <summary>顶部装饰带线条最大宽度（像素）</summary>
        private const float TopBandLineMaxWidth = 120f;

        /// <summary>底部装饰带线条最大宽度（像素）</summary>
        private const float BottomBandLineMaxWidth = 120f;

        /// <summary>品质强调边框厚度（像素，装备类 Tooltip 使用）</summary>
        private const float QualityBorderThickness = 2f;

        /// <summary>纸色质感叠加层的透明度（模拟 noise 纹理）</summary>
        private const float PaperTextureOpacity = 0.04f;

        /// <summary>当前 Tooltip 的品质强调色（null 表示使用默认金色边框）</summary>
        private Color? _qualityBorderColor;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>头部属性名 Label</summary>
        private Label _nameLabel;

        /// <summary>中部核心信息 Label（多行）</summary>
        private Label _coreLabel;

        /// <summary>下部附加信息 Label</summary>
        private Label _additionalLabel;

        /// <summary>下部追加项 Label 列表（每行前缀 "·"）</summary>
        private List<Label> _appendLabels = new List<Label>();

        // ===================================================================
        // 数据字段
        // =======================================================================

        /// <summary>头部图标（为 null 时绘制元素色圆点占位）</summary>
        private Texture _icon;

        /// <summary>属性名文本</summary>
        private string _name = string.Empty;

        /// <summary>中部核心信息文本（支持 \n 多行）</summary>
        private string _coreInfo = string.Empty;

        /// <summary>下部附加信息文本</summary>
        private string _additionalInfo = string.Empty;

        /// <summary>下部可追加项列表（如装备来源）</summary>
        private List<string> _appendableItems = new List<string>();

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化三段式子 Label，初始 <see cref="Visible"/> 为 false。
        /// 默认尺寸 240x180（最小高度），<see cref="SetData"/> 时按内容重新计算高度。
        /// </summary>
        public InkAttributeTooltip()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            ClipChildren = false;
            AutoFocus = false;
            BackgroundColor = Color.Transparent;
            Size = new Float2(TooltipWidth, MinHeight);
            Visible = false;

            try
            {
                BuildLabels();
                ApplyLayout();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InkAttributeTooltip] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 子控件构建
        // =======================================================================

        /// <summary>
        /// 构建头部/中部/下部三个固定 Label。
        /// 追加项 Label 由 <see cref="RebuildAppendLabels"/> 动态管理。
        /// </summary>
        private void BuildLabels()
        {
            // 头部属性名 Label（纸色背景上用 TextOnPaper + 金色强调）
            _nameLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 15f),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_nameLabel);

            // 中部核心信息 Label（纸色背景上用 TextOnPaper）
            _coreLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, CoreFontSize),
                TextColor = InkWashTheme.TextOnPaper,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_coreLabel);

            // 下部附加信息 Label（纸色背景上用 PaperDark，弱化层级）
            _additionalLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, AdditionalFontSize),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
                AnchorPreset = AnchorPresets.TopLeft,
            };
            AddChild(_additionalLabel);
        }

        /// <summary>
        /// 根据 <see cref="_appendableItems"/> 重建下部追加项 Label 列表。
        /// 每行文本前缀 "· "，字号 11，<see cref="InkWashTheme.TextTertiary"/> 色。
        /// 旧 Label 先从父控件移除再清空列表。
        /// </summary>
        private void RebuildAppendLabels()
        {
            // 移除旧 Label
            for (int i = 0; i < _appendLabels.Count; i++)
            {
                var label = _appendLabels[i];
                if (label != null)
                {
                    RemoveChild(label);
                }
            }
            _appendLabels.Clear();

            if (_appendableItems == null)
                return;

            for (int i = 0; i < _appendableItems.Count; i++)
            {
                string item = _appendableItems[i] ?? string.Empty;
                var label = new Label
                {
                    Text = "· " + item,
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, AdditionalFontSize),
                    TextColor = InkWashTheme.PaperDark,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Near,
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                AddChild(label);
                _appendLabels.Add(label);
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 设置提示控件数据，更新各 Label 文本与图标引用，并重新计算高度与布局。
        /// </summary>
        /// <param name="icon">头部图标，为 null 时绘制元素色圆点占位</param>
        /// <param name="name">属性名</param>
        /// <param name="coreInfo">中部核心信息（支持 \n 多行，如"当前：3200\n基础：2800\n加成：+400"）</param>
        /// <param name="additionalInfo">下部附加信息</param>
        /// <param name="appendableItems">下部可追加项列表（如装备来源），每行前缀 "·"</param>
        public void SetData(
            Texture icon,
            string name,
            string coreInfo,
            string additionalInfo,
            List<string> appendableItems)
        {
            try
            {
                _icon = icon;
                _name = name ?? string.Empty;
                _coreInfo = coreInfo ?? string.Empty;
                _additionalInfo = additionalInfo ?? string.Empty;
                _appendableItems = appendableItems ?? new List<string>();

                if (_nameLabel != null)
                    _nameLabel.Text = _name;
                if (_coreLabel != null)
                    _coreLabel.Text = _coreInfo;
                if (_additionalLabel != null)
                    _additionalLabel.Text = _additionalInfo;

                RebuildAppendLabels();
                ApplyLayout();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InkAttributeTooltip] SetData 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置品质强调边框色（装备类 Tooltip 使用）。
        /// 传入 null 恢复默认金色边框；传入品质色（如 <see cref="InkWashTheme.QualityLegendary"/>）
        /// 则外边框采用该色，强化装备品质识别度。
        /// </summary>
        /// <param name="qualityColor">品质色，null 恢复默认</param>
        public void SetQualityBorderColor(Color? qualityColor)
        {
            _qualityBorderColor = qualityColor;
        }

        /// <summary>
        /// 显示并定位提示控件。
        /// 接收屏幕物理坐标（通常由触发控件的 <see cref="Control.PointToScreen"/> 转换得到），
        /// 通过 <see cref="Control.PointFromScreen"/> 转换为父控件本地逻辑坐标后，
        /// 将控件左上角定位到鼠标右下方 <see cref="ShowOffset"/> 像素处；
        /// 若超出父控件边界则自动向左/向上偏移，确保完整可见。
        /// </summary>
        /// <param name="screenPosition">鼠标在屏幕物理坐标系中的坐标</param>
        public void Show(Float2 screenPosition)
        {
            try
            {
                ApplyLayout(); // 确保高度为最新内容高度

                // 触发控件传入的是屏幕物理坐标（PointToScreen 结果）。
                // 需要转换为父控件本地逻辑坐标，才能正确设置 Location。
                // PointFromScreen 会处理 DpiScale、窗口位置及所有父级变换。
                Float2 localPos = Parent != null
                    ? Parent.PointFromScreen(screenPosition)
                    : screenPosition;

                // Tooltip 本地坐标 = 鼠标在父控件中的本地坐标 + 右下偏移
                float x = localPos.X + ShowOffset;
                float y = localPos.Y + ShowOffset;

                // 边界溢出检测：使用父控件尺寸（逻辑坐标）作为边界，
                // 避免父控件尺寸小于屏幕时 Tooltip 被误判溢出而偏移到不可见区域。
                float boundsW = Parent?.Width ?? Screen.Size.X;
                float boundsH = Parent?.Height ?? Screen.Size.Y;

                // 右边界溢出 → 向左偏移到鼠标左侧
                if (x + Width > boundsW)
                    x = localPos.X - Width - ShowOffset;

                // 下边界溢出 → 向上偏移到鼠标上方
                if (y + Height > boundsH)
                    y = localPos.Y - Height - ShowOffset;

                // 防止负坐标（相对于父控件本地坐标系）
                if (x < 0f)
                    x = 0f;
                if (y < 0f)
                    y = 0f;

                FlaxEngine.Debug.Log($"[InkAttributeTooltip] Show screenPos={screenPosition} localPos={localPos} parentLoc={Parent?.Location ?? Float2.Zero} parentSize={Parent?.Size ?? Float2.Zero} local=({x},{y}) size={Width}x{Height}");
                Location = new Float2(x, y);
                Visible = true;
                // Flax的Control类没有BringToFront API，通过重新添加到父控件末尾实现置顶
                // 确保Tooltip不被后添加的装饰层/面板遮挡
                if (Parent != null)
                {
                    var parent = Parent;
                    Parent = null;
                    Parent = parent;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InkAttributeTooltip] Show 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 隐藏提示控件。
        /// </summary>
        public void Hide()
        {
            Visible = false;
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前文本内容重新计算各 Label 位置与控件总高度。
        /// 宽度固定 240px，高度按"头部 + 中部 + 下部 + 底部内边距"累加，最小 180px。
        /// </summary>
        private void ApplyLayout()
        {
            float contentWidth = TooltipWidth - Padding * 2f;

            // 1. 头部：属性名 Label（图标在 Draw 中绘制）
            float nameX = Padding + IconSize + SectionGap;
            if (_nameLabel != null)
            {
                _nameLabel.Location = new Float2(nameX, 0f);
                _nameLabel.Size = new Float2(TooltipWidth - nameX - Padding, HeaderHeight);
            }

            // 2. 中部：核心信息 Label
            int coreLines = CountLines(_coreInfo);
            float coreY = HeaderHeight + SectionGap;
            float coreHeight = coreLines * CoreLineHeight;
            if (_coreLabel != null)
            {
                _coreLabel.Location = new Float2(Padding, coreY);
                _coreLabel.Size = new Float2(contentWidth, coreHeight);
            }

            // 3. 下部：附加信息 Label
            float additionalY = coreY + coreHeight + SectionGap;
            bool hasAdditional = !string.IsNullOrEmpty(_additionalInfo);
            float additionalHeight = hasAdditional
                ? CountLines(_additionalInfo) * AdditionalLineHeight
                : 0f;
            if (_additionalLabel != null)
            {
                _additionalLabel.Location = new Float2(Padding, additionalY);
                _additionalLabel.Size = new Float2(contentWidth, additionalHeight);
                _additionalLabel.Visible = hasAdditional;
            }

            // 4. 下部：追加项 Label 列表
            float appendY = additionalY + additionalHeight + (hasAdditional ? AppendGap : 0f);
            int appendCount = _appendLabels.Count;
            for (int i = 0; i < _appendLabels.Count; i++)
            {
                var label = _appendLabels[i];
                if (label == null)
                    continue;
                label.Location = new Float2(Padding, appendY + i * AppendLineHeight);
                label.Size = new Float2(contentWidth, AppendLineHeight);
            }

            // 5. 总高度（含底部内边距），不小于 MinHeight
            float bottomY = appendY + appendCount * AppendLineHeight + BottomPadding;
            float totalHeight = Mathf.Max(bottomY, MinHeight);
            Size = new Float2(TooltipWidth, totalHeight);
        }

        /// <summary>
        /// 统计文本行数（按 '\n' 分割）。空字符串视为 1 行。
        /// </summary>
        /// <param name="text">待统计文本</param>
        /// <returns>行数（至少 1）</returns>
        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 1;

            int count = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                    count++;
            }
            return count;
        }

        // ===================================================================
        // 渲染
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            if (!Visible || Width <= 0f || Height <= 0f)
                return;

            var bounds = new Rectangle(0, 0, Width, Height);

            // 1. 外阴影（右下偏移 4px，半透明黑色，参考 popup-verification 的 paper-panel-shadow）
            Render2D.FillRectangle(
                new Rectangle(ShadowOffset, ShadowOffset, Width, Height),
                ShadowColor);

            // 2. 纸色面板背景（参考 popup-verification.html 的 ink-paper-panel，半透明纸色）
            Render2D.FillRectangle(bounds, InkWashTheme.PaperPanelBg);

            // 3. 纸色质感叠加层（模拟 noise 纹理，用淡灰色细密点阵增加纸质感）
            DrawPaperTexture();

            // 4. 内描边高亮（距外边框 1px，GoldBright 1px，模拟纸色面板的内框装饰）
            Render2D.DrawRectangle(
                new Rectangle(InnerBorderInset, InnerBorderInset,
                    Width - InnerBorderInset * 2f, Height - InnerBorderInset * 2f),
                InkWashTheme.GoldBright,
                InnerBorderThickness);

            // 5. 外边框（品质色优先，否则默认金色，2px）
            Color borderColor = _qualityBorderColor ?? InkWashTheme.BorderGold;
            Render2D.DrawRectangle(bounds, borderColor, BorderThickness);

            // 6. 四角金色 L 型装饰（参考 popup-verification.html 的 corner-ornament）
            DrawCornerOrnaments();

            // 7. 顶部装饰带（参考 modal-top-band：金色渐变线 + 中心点）
            DrawTopBand();

            // 8. 头部图标 / 元素色圆点占位
            float iconY = (HeaderHeight - IconSize) * 0.5f;
            var iconRect = new Rectangle(Padding, iconY, IconSize, IconSize);
            if (_icon != null)
            {
                Render2D.DrawTexture(_icon, iconRect, Color.White);
            }
            else
            {
                var dotCenter = new Float2(Padding + IconSize * 0.5f, HeaderHeight * 0.5f);
                InkRenderHelper.FillCircle(dotCenter, IconSize * 0.35f,
                    _qualityBorderColor ?? InkWashTheme.GoldPrimary);
            }

            // 9. 头部底部分隔线（金色渐变 1px，参考 ink-divider-short）
            DrawHeaderDivider();

            // 10. 子控件（各 Label）
            base.Draw();

            // 11. 底部装饰带（参考 modal-bottom-band：金线 + 圆点 + 金线）
            DrawBottomBand();
        }

        /// <summary>
        /// 绘制纸色质感叠加层（模拟 popup-verification.html 中 ink-paper-panel::after 的 noise 纹理）。
        /// 用淡灰色细密点阵增加纸质感，multiply 混合效果。
        /// </summary>
        private void DrawPaperTexture()
        {
            // 用半透明灰色叠加模拟纸色纹理质感
            var textureColor = new Color(
                InkWashTheme.PaperAged.R,
                InkWashTheme.PaperAged.G,
                InkWashTheme.PaperAged.B,
                PaperTextureOpacity);

            // 顶部和底部边缘略深的渐变，模拟纸张老化效果
            float edgeHeight = Mathf.Min(20f, Height * 0.15f);
            var topGrad = new Color(
                InkWashTheme.PaperAged.R,
                InkWashTheme.PaperAged.G,
                InkWashTheme.PaperAged.B,
                PaperTextureOpacity * 1.5f);
            Render2D.FillRectangle(
                new Rectangle(0, 0, Width, edgeHeight), topGrad);
            Render2D.FillRectangle(
                new Rectangle(0, Height - edgeHeight, Width, edgeHeight), topGrad);
        }

        /// <summary>
        /// 绘制四角金色 L 型装饰（对应 popup-verification.html 的 corner-ornament）。
        /// 每个 L 角由两条 1px 金线组成，尺寸 <see cref="CornerSize"/>。
        /// </summary>
        private void DrawCornerOrnaments()
        {
            Color cornerColor = InkWashTheme.GoldPrimary;
            float alpha = 0.6f;
            cornerColor = new Color(cornerColor.R, cornerColor.G, cornerColor.B, alpha);

            // 左上角：水平线（顶部）+ 垂直线（左侧）
            Render2D.FillRectangle(
                new Rectangle(0, 0, CornerSize, CornerLineThickness), cornerColor);
            Render2D.FillRectangle(
                new Rectangle(0, 0, CornerLineThickness, CornerSize), cornerColor);

            // 右上角：水平线（顶部）+ 垂直线（右侧）
            Render2D.FillRectangle(
                new Rectangle(Width - CornerSize, 0, CornerSize, CornerLineThickness), cornerColor);
            Render2D.FillRectangle(
                new Rectangle(Width - CornerLineThickness, 0, CornerLineThickness, CornerSize), cornerColor);

            // 左下角：水平线（底部）+ 垂直线（左侧）
            Render2D.FillRectangle(
                new Rectangle(0, Height - CornerLineThickness, CornerSize, CornerLineThickness), cornerColor);
            Render2D.FillRectangle(
                new Rectangle(0, Height - CornerSize, CornerLineThickness, CornerSize), cornerColor);

            // 右下角：水平线（底部）+ 垂直线（右侧）
            Render2D.FillRectangle(
                new Rectangle(Width - CornerSize, Height - CornerLineThickness, CornerSize, CornerLineThickness), cornerColor);
            Render2D.FillRectangle(
                new Rectangle(Width - CornerLineThickness, Height - CornerSize, CornerLineThickness, CornerSize), cornerColor);
        }

        /// <summary>
        /// 绘制顶部装饰带（对应 popup-verification.html 的 modal-top-band）。
        /// 中间一条短金色渐变线，强化古风卷轴感。
        /// </summary>
        private void DrawTopBand()
        {
            float centerY = HeaderHeight * 0.5f;
            float lineHalfWidth = Mathf.Min(TopBandLineMaxWidth * 0.5f, (Width - Padding * 2f) * 0.4f);
            float centerX = Width * 0.5f;

            // 中心金色圆点
            var dotRect = new Rectangle(centerX - 2f, centerY - 2f, 4f, 4f);
            Render2D.FillRectangle(dotRect, InkWashTheme.GoldDeep);

            // 左侧渐变线（从透明到金色）
            var leftLineRect = new Rectangle(
                centerX - lineHalfWidth, centerY - 0.5f, lineHalfWidth - 4f, 1f);
            Render2D.FillRectangle(leftLineRect, InkWashTheme.GoldDeep);

            // 右侧渐变线（从金色到透明）
            var rightLineRect = new Rectangle(
                centerX + 4f, centerY - 0.5f, lineHalfWidth - 4f, 1f);
            Render2D.FillRectangle(rightLineRect, InkWashTheme.GoldDeep);
        }

        /// <summary>
        /// 绘制头部底部分隔线（金色渐变 1px，参考 ink-divider-short）。
        /// 采用左右渐变（中间实，两端淡）模拟水墨分隔线。
        /// </summary>
        private void DrawHeaderDivider()
        {
            float y = HeaderHeight - 1f;
            float x = Padding;
            float w = Width - Padding * 2f;
            float midX = x + w * 0.5f;

            // 左半段（淡到实）
            Color leftColor = new Color(
                InkWashTheme.GoldPrimary.R,
                InkWashTheme.GoldPrimary.G,
                InkWashTheme.GoldPrimary.B, 0.15f);
            Color midColor = InkWashTheme.GoldPrimary;
            Color rightColor = leftColor;

            // 简化：左中右三段渐变模拟
            float segW = w / 3f;
            Render2D.FillRectangle(new Rectangle(x, y, segW, 1f), leftColor);
            Render2D.FillRectangle(new Rectangle(x + segW, y, segW, 1f), midColor);
            Render2D.FillRectangle(new Rectangle(x + segW * 2f, y, segW, 1f), rightColor);
        }

        /// <summary>
        /// 绘制底部装饰带（对应 popup-verification.html 的 modal-bottom-band）。
        /// 中间金色圆点 + 两侧金色渐变线，与顶部装饰带呼应，强化古风卷轴感。
        /// </summary>
        private void DrawBottomBand()
        {
            float y = Height - BottomPadding * 0.5f;
            float lineHalfWidth = Mathf.Min(BottomBandLineMaxWidth * 0.5f, (Width - Padding * 2f) * 0.35f);
            float centerX = Width * 0.5f;

            // 中心金色圆点
            var dotRect = new Rectangle(centerX - 2f, y - 2f, 4f, 4f);
            Render2D.FillRectangle(dotRect, InkWashTheme.GoldDeep);

            // 左侧金色线
            var leftLineRect = new Rectangle(
                centerX - lineHalfWidth, y - 0.5f, lineHalfWidth - 6f, 1f);
            Render2D.FillRectangle(leftLineRect, InkWashTheme.GoldDeep);

            // 右侧金色线
            var rightLineRect = new Rectangle(
                centerX + 6f, y - 0.5f, lineHalfWidth - 6f, 1f);
            Render2D.FillRectangle(rightLineRect, InkWashTheme.GoldDeep);
        }
    }
}
