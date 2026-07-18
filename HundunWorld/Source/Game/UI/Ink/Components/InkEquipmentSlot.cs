using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 水墨风格装备槽控件。
    /// 对应纸娃娃装备槽位（Head/Neck/Body/Back/RightHand/LeftHand/Waist/Face），
    /// 继承 <see cref="ContainerControl"/>，通过 <see cref="Draw"/> 绘制：
    /// <list type="bullet">
    ///   <item>有装备：装备图标（80% 居中）+ 品质色发光边框（2-3px）+ 右下角强化等级胶囊("+N")</item>
    ///   <item>有装备：图标下方追加装备名/装备类型 Label（由 <see cref="Refresh"/> 同步）</item>
    ///   <item>空槽：墨青底色 + 细玉色/金色边框（1px）+ 槽位类型中文简称居中</item>
    /// </list>
    /// 控件高度扩展至 <see cref="DefaultSlotHeight"/>（≈78px），顶部 <see cref="IconAreaSize"/>
    /// 区域承载图标与边框，下方区域承载装备名/装备类型 Label。
    /// 覆写 <see cref="OnMouseDown"/>/<see cref="OnMouseUp"/> 检测双击
    /// （两次点击间隔 &lt; 0.5s），覆写 <see cref="OnMouseEnter"/>/<see cref="OnMouseLeave"/>
    /// 触发悬停事件。双击检测使用 <see cref="Time.UnscaledGameTime"/> 记录上次点击时间。
    /// </summary>
    public class InkEquipmentSlot : ContainerControl
    {
        // ===================================================================
        // 常量
        // =======================================================================

        /// <summary>双击判定阈值（秒），两次点击间隔小于此值视为双击</summary>
        private const float DoubleClickThreshold = 0.5f;

        /// <summary>默认尺寸（正方形边长）</summary>
        public const float DefaultSlotSize = 56f;

        /// <summary>装备图标占槽位的比例（80%）</summary>
        private const float IconScaleRatio = 0.8f;

        /// <summary>有装备时品质色边框厚度</summary>
        private const float QualityBorderThickness = 2f;

        /// <summary>空槽时暗色边框厚度</summary>
        private const float EmptyBorderThickness = 1f;

        /// <summary>强化等级胶囊标签字号</summary>
        private const float EnhanceLabelFontSize = 10f;

        /// <summary>空槽中文名字号</summary>
        private const float SlotNameFontSize = 12f;

        /// <summary>装备图标区域尺寸（正方形边长，位于控件顶部）</summary>
        private const float IconAreaSize = 48f;

        /// <summary>图标区域与装备名标签的垂直间距</summary>
        private const float LabelGap = 4f;

        /// <summary>装备名标签高度</summary>
        private const float NameLabelHeight = 14f;

        /// <summary>装备类型标签高度</summary>
        private const float TypeLabelHeight = 12f;

        /// <summary>装备名字号</summary>
        private const float NameLabelFontSize = 11f;

        /// <summary>装备类型字号</summary>
        private const float TypeLabelFontSize = 10f;

        /// <summary>强化胶囊左右内边距</summary>
        private const float CapsulePaddingX = 4f;

        /// <summary>强化胶囊上下内边距</summary>
        private const float CapsulePaddingY = 1f;

        /// <summary>强化胶囊相对图标右侧的外偏移（right: -4px）</summary>
        private const float CapsuleOffsetX = 4f;

        /// <summary>强化胶囊相对图标底部的外偏移（bottom: -6px）</summary>
        private const float CapsuleOffsetY = 6f;

        /// <summary>强化胶囊边框厚度</summary>
        private const float CapsuleBorderThickness = 1f;

        /// <summary>
        /// 默认装备槽高度（图标区 + 间距 + 装备名 + 装备类型）：
        /// 48 + 4 + 14 + 12 = 78
        /// </summary>
        private const float DefaultSlotHeight =
            IconAreaSize + LabelGap + NameLabelHeight + TypeLabelHeight;

        // ===================================================================
        // 装备槽位中文名映射
        // =======================================================================

        /// <summary>
        /// 装备槽位枚举到中文简称的映射表（最多两个汉字）。
        /// </summary>
        private static readonly Dictionary<EquipmentSlot, string> SlotNames =
            new Dictionary<EquipmentSlot, string>
            {
                { EquipmentSlot.Head, "头" },
                { EquipmentSlot.Neck, "颈" },
                { EquipmentSlot.Shoulder, "肩" },
                { EquipmentSlot.Back, "背" },
                { EquipmentSlot.Body, "身" },
                { EquipmentSlot.Waist, "腰" },
                { EquipmentSlot.Legs, "腿" },
                { EquipmentSlot.Feet, "足" },
                { EquipmentSlot.RightHand, "右" },
                { EquipmentSlot.LeftHand, "左" },
                { EquipmentSlot.RightRing, "右戒" },
                { EquipmentSlot.LeftRing, "左戒" },
                { EquipmentSlot.RightWrist, "右腕" },
                { EquipmentSlot.LeftWrist, "左腕" },
                { EquipmentSlot.Face, "面" },
            };

        // ===================================================================
        // 公共字段
        // =======================================================================

        /// <summary>槽位类型</summary>
        public EquipmentSlot SlotType;

        /// <summary>当前装备数据，null 表示空槽</summary>
        public EquipmentData CurrentEquipment;

        /// <summary>装备图标纹理，null 时绘制占位</summary>
        public Texture Icon;

        /// <summary>空槽图标纹理</summary>
        public Texture EmptySlotIcon;

        /// <summary>
        /// 强化等级（独立于 <see cref="EquipmentData.ItemLevel"/> 的语义），
        /// 大于 0 时在图标右下角绘制 "+N" 胶囊标签。
        /// </summary>
        public int EnhanceLevel;

        // ===================================================================
        // 子控件
        // =======================================================================

        /// <summary>装备名 Label，仅已装备时可见</summary>
        private Label _equipmentNameLabel;

        /// <summary>装备类型 Label，仅已装备时可见</summary>
        private Label _equipmentTypeLabel;

        // ===================================================================
        // 状态字段
        // =======================================================================

        /// <summary>鼠标左键是否按下（用于点击释放判定）</summary>
        private bool _isMouseDown;

        /// <summary>上次点击时间（<see cref="Time.UnscaledGameTime"/>），-1 表示未点击或已触发双击</summary>
        private float _lastClickTime = -1f;

        /// <summary>当前是否悬停</summary>
        private bool _isHovered;

        // ===================================================================
        // 公共事件
        // =======================================================================

        /// <summary>
        /// 双击事件。两次左键点击间隔小于 <see cref="DoubleClickThreshold"/> 时触发。
        /// 参数：槽位类型。
        /// </summary>
        public event Action<EquipmentSlot> DoubleClicked;

        /// <summary>
        /// 悬停事件。参数：槽位类型、控件左上角的屏幕坐标（<see cref="PointToScreen"/>）。
        /// </summary>
        public event Action<EquipmentSlot, Float2> Hovered;

        /// <summary>
        /// 悬停结束事件。鼠标离开槽位时触发。
        /// </summary>
        public event Action HoverEnded;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：默认 56x56，深墨黑背景，裁剪子控件。
        /// </summary>
        public InkEquipmentSlot()
        {
            // 宽度保持默认槽位尺寸，高度扩展以容纳图标区 + 装备名 + 装备类型
            Size = new Float2(DefaultSlotSize, DefaultSlotHeight);
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = true;
            AutoFocus = false;

            _equipmentNameLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, NameLabelFontSize),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Visible = false,
            };
            AddChild(_equipmentNameLabel);

            _equipmentTypeLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, TypeLabelFontSize),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Visible = false,
            };
            AddChild(_equipmentTypeLabel);

            UpdateLabelLayout();
        }

        // ===================================================================
        // 公共方法
        // =======================================================================

        /// <summary>
        /// 刷新装备数据。传入 null 表示清空槽位。
        /// </summary>
        /// <param name="equipment">新装备数据，可为 null</param>
        public void Refresh(EquipmentData equipment)
        {
            CurrentEquipment = equipment;
            if (equipment != null)
            {
                // EquipmentData 暂无独立强化等级字段，默认 0；外部可单独设置 EnhanceLevel
                EnhanceLevel = 0;
                _equipmentNameLabel.Text = equipment.Name ?? string.Empty;
                _equipmentTypeLabel.Text = MapEquipmentType(equipment.Type);
                _equipmentNameLabel.Visible = true;
                _equipmentTypeLabel.Visible = true;
            }
            else
            {
                EnhanceLevel = 0;
                _equipmentNameLabel.Text = string.Empty;
                _equipmentTypeLabel.Text = string.Empty;
                _equipmentNameLabel.Visible = false;
                _equipmentTypeLabel.Visible = false;
            }
        }

        // ===================================================================
        // 装备类型映射 / 布局
        // =======================================================================

        /// <summary>
        /// 将 <see cref="EquipmentType"/> 映射为中文显示名，用于装备类型 Label。
        /// </summary>
        /// <param name="type">装备类型枚举</param>
        /// <returns>中文类型名</returns>
        private static string MapEquipmentType(EquipmentType type)
        {
            return type switch
            {
                EquipmentType.Body => "身体装备",
                EquipmentType.Accessory => "配饰",
                EquipmentType.Weapon => "武器",
                _ => string.Empty,
            };
        }

        /// <inheritdoc />
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            UpdateLabelLayout();
        }

        /// <summary>
        /// 根据当前控件尺寸重新计算装备名/装备类型 Label 的位置与尺寸。
        /// Label 位于图标区域下方：装备名紧贴图标下沿（间隔 <see cref="LabelGap"/>），
        /// 装备类型在装备名之下。
        /// </summary>
        private void UpdateLabelLayout()
        {
            if (_equipmentNameLabel != null)
            {
                _equipmentNameLabel.Location = new Float2(0f, IconAreaSize + LabelGap);
                _equipmentNameLabel.Size = new Float2(Width, NameLabelHeight);
            }
            if (_equipmentTypeLabel != null)
            {
                _equipmentTypeLabel.Location = new Float2(
                    0f, IconAreaSize + LabelGap + NameLabelHeight);
                _equipmentTypeLabel.Size = new Float2(Width, TypeLabelHeight);
            }
        }

        // ===================================================================
        // 绘制
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            try
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                // 悬停时整体上浮 2px，模拟 CSS translateY(-2px)
                float yOffset = _isHovered ? -2f : 0f;

                // 悬停时绘制投影（模拟 box-shadow: 0 4px 12px rgba(0,0,0,0.3)）
                // 阴影偏移 (2, 4)，半透明黑色，仅覆盖图标区域高度
                if (_isHovered)
                {
                    var shadowRect = new Rectangle(2f, 4f, Width, IconAreaSize);
                    Render2D.FillRectangle(shadowRect, new Color(0f, 0f, 0f, 0.3f));
                }

                // 图标区域（顶部 IconAreaSize×Width），所有图标相关绘制基于此矩形
                var iconArea = new Rectangle(0f, yOffset, Width, IconAreaSize);

                // 1. 背景：深墨黑底色
                Render2D.FillRectangle(iconArea, InkWashTheme.BaseDefault);

                if (CurrentEquipment != null)
                {
                    // 2. 品质色内发光（模拟 box-shadow: inset 0 0 8px rgba(quality, 0.12)）
                    // 在背景之后、图标之前绘制，使图标叠在内发光之上
                    var quality = MapQuality(CurrentEquipment.Quality);
                    Color baseQualityColor = InkWashTheme.QualityColor(quality);
                    Color innerGlowColor = new Color(
                        baseQualityColor.R, baseQualityColor.G, baseQualityColor.B, 0.12f);
                    var innerGlowRect = new Rectangle(
                        2f, 2f + yOffset, Width - 4f, IconAreaSize - 4f);
                    Render2D.FillRectangle(innerGlowRect, innerGlowColor);

                    // 3. 装备图标（居中，占 80%）
                    if (Icon != null && Icon.IsLoaded)
                    {
                        float iconSize = Mathf.Min(Width, IconAreaSize) * IconScaleRatio;
                        float iconX = (Width - iconSize) * 0.5f;
                        float iconY = (IconAreaSize - iconSize) * 0.5f + yOffset;
                        Render2D.DrawTexture(
                            Icon,
                            new Rectangle(iconX, iconY, iconSize, iconSize),
                            Color.White);
                    }

                    // 4. 品质色发光边框（2-3px 辉光）
                    Color qualityColor = _isHovered
                        ? InkWashTheme.GoldBright
                        : baseQualityColor;
                    Color glowColor = new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.22f);

                    // 外发光层
                    Render2D.DrawRectangle(
                        new Rectangle(-2f, -2f + yOffset, Width + 4f, IconAreaSize + 4f),
                        glowColor, 1f);
                    Render2D.DrawRectangle(
                        new Rectangle(-1f, -1f + yOffset, Width + 2f, IconAreaSize + 2f),
                        new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.35f), 1f);
                    // 主品质边框
                    Render2D.DrawRectangle(iconArea, qualityColor, QualityBorderThickness);
                    // 内侧高光
                    Render2D.DrawRectangle(
                        new Rectangle(1f, 1f + yOffset, Width - 2f, IconAreaSize - 2f),
                        new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.55f), 1f);

                    // 5. 强化等级胶囊标签（仅 EnhanceLevel > 0 时绘制）
                    if (EnhanceLevel > 0)
                    {
                        DrawEnhanceCapsule(yOffset);
                    }
                }
                else
                {
                    // 2. 空槽图标（若设置）
                    if (EmptySlotIcon != null && EmptySlotIcon.IsLoaded)
                    {
                        float iconSize = Mathf.Min(Width, IconAreaSize) * IconScaleRatio;
                        float iconX = (Width - iconSize) * 0.5f;
                        float iconY = (IconAreaSize - iconSize) * 0.5f + yOffset;
                        Render2D.DrawTexture(
                            EmptySlotIcon,
                            new Rectangle(iconX, iconY, iconSize, iconSize),
                            Color.White);
                    }

                    // 3. 鎏金弱边框（悬停时高亮为 GoldBright）
                    Color borderColor = _isHovered
                        ? InkWashTheme.GoldBright
                        : InkWashTheme.BorderGold;
                    Render2D.DrawRectangle(iconArea, borderColor, EmptyBorderThickness);

                    // 4. 空槽内发光（柔和金色）
                    Color innerGlow = new Color(
                        InkWashTheme.GoldPrimary.R,
                        InkWashTheme.GoldPrimary.G,
                        InkWashTheme.GoldPrimary.B,
                        _isHovered ? 0.08f : 0.04f);
                    Render2D.DrawRectangle(
                        new Rectangle(1f, 1f + yOffset, Width - 2f, IconAreaSize - 2f),
                        innerGlow, 1f);

                    // 5. 槽位类型中文简称居中显示（暗纸色，10-12px）
                    var nameFontRef = InkRenderHelper.GetFontRef(
                        InkWashTheme.FontRole.Heading, SlotNameFontSize);
                    var nameFont = nameFontRef.GetFont();
                    if (nameFont != null && SlotNames.TryGetValue(SlotType, out string slotName))
                    {
                        Render2D.DrawText(
                            nameFont,
                            slotName,
                            iconArea,
                            InkWashTheme.TextTertiary,
                            TextAlignment.Center,
                            TextAlignment.Center,
                            TextWrapping.NoWrap);
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[InkEquipmentSlot] Draw 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 绘制强化等级胶囊标签："+N"。
        /// 样式：10px Number 字体 GoldBright 文字 + BaseDefault 背景 + 1px GoldDeep 边框。
        /// 定位：相对图标区域右下角偏外（right +<see cref="CapsuleOffsetX"/>px,
        /// bottom +<see cref="CapsuleOffsetY"/>px）。圆角 2px 用 FillRectangle +
        /// DrawRectangle 近似（Flax Render2D 无原生圆角 API）。
        /// </summary>
        /// <param name="yOffset">悬停时的垂直偏移，胶囊随图标一起上浮</param>
        private void DrawEnhanceCapsule(float yOffset)
        {
            var fontRef = InkRenderHelper.GetFontRef(
                InkWashTheme.FontRole.Number, EnhanceLabelFontSize);
            var font = fontRef.GetFont();
            if (font == null)
                return;

            string text = "+" + EnhanceLevel.ToString();
            Float2 textSize = font.MeasureText(text);
            float capsuleWidth = textSize.X + CapsulePaddingX * 2f;
            float capsuleHeight = EnhanceLabelFontSize + CapsulePaddingY * 2f;
            // 相对图标右下角偏外：X = Width - capsuleWidth + 4, Y = IconAreaSize - capsuleHeight + 6
            float capsuleX = Width - capsuleWidth + CapsuleOffsetX;
            float capsuleY = IconAreaSize - capsuleHeight + CapsuleOffsetY + yOffset;
            var capsuleRect = new Rectangle(capsuleX, capsuleY, capsuleWidth, capsuleHeight);

            // 背景：BaseDefault
            Render2D.FillRectangle(capsuleRect, InkWashTheme.BaseDefault);
            // 边框：1px GoldDeep（2px 圆角用矩形近似）
            Render2D.DrawRectangle(capsuleRect, InkWashTheme.GoldDeep, CapsuleBorderThickness);
            // 文本：GoldBright，居中
            Render2D.DrawText(
                font,
                text,
                capsuleRect,
                InkWashTheme.GoldBright,
                TextAlignment.Center,
                TextAlignment.Center,
                TextWrapping.NoWrap);
        }

        // ===================================================================
        // 双击检测
        // =======================================================================

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            base.OnMouseDown(location, button);
            if (button == MouseButton.Left)
                _isMouseDown = true;
            return true;
        }

        /// <inheritdoc />
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            base.OnMouseUp(location, button);
            if (button == MouseButton.Left && _isMouseDown)
            {
                _isMouseDown = false;
                // 判定点击是否在控件范围内
                if (location.X >= 0f && location.X <= Width &&
                    location.Y >= 0f && location.Y <= Height)
                {
                    // 双击检测：使用 UnscaledGameTime 避免受时间缩放影响
                    float now = Time.UnscaledGameTime;
                    if (_lastClickTime > 0f && (now - _lastClickTime) < DoubleClickThreshold)
                    {
                        try
                        {
                            DoubleClicked?.Invoke(SlotType);
                        }
                        catch (Exception ex)
                        {
                            FlaxEngine.Debug.LogError(
                                $"[InkEquipmentSlot] DoubleClicked 触发失败: {ex.Message}");
                        }
                        _lastClickTime = -1f;
                    }
                    else
                    {
                        _lastClickTime = now;
                    }
                }
            }
            return true;
        }

        // ===================================================================
        // 悬停事件
        // =======================================================================

        /// <inheritdoc />
        public override void OnMouseEnter(Float2 location)
        {
            base.OnMouseEnter(location);
            FlaxEngine.Debug.Log($"[InkEquipmentSlot] OnMouseEnter slot={SlotType} location={location} size={Size}");
            _isHovered = true;
            try
            {
                // 使用控件本地坐标转换为窗口客户区坐标，与宿主页面 UI 坐标系一致，
                // 避免 MouseScreenPosition 在编辑器/窗口模式下使用显示器坐标导致的偏移。
                Float2 screenPos = PointToScreen(location);
                Hovered?.Invoke(SlotType, screenPos);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[InkEquipmentSlot] Hovered 触发失败: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _isHovered = false;
            try
            {
                HoverEnded?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[InkEquipmentSlot] HoverEnded 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 品质映射
        // =======================================================================

        /// <summary>
        /// 将 <see cref="EquipmentData.Quality"/>（1-5）映射到
        /// <see cref="InkWashTheme.InkQuality"/>（0-4）。
        /// </summary>
        /// <param name="quality">装备品质（1=普通灰,2=优秀绿,3=精良蓝,4=史诗紫,5=传说红/鎏金）</param>
        /// <returns>对应的 <see cref="InkWashTheme.InkQuality"/> 枚举值</returns>
        private static InkWashTheme.InkQuality MapQuality(int quality)
        {
            int clamped = Mathf.Clamp(quality - 1, 0, 4);
            return (InkWashTheme.InkQuality)clamped;
        }
    }
}
