using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Reward
{
    /// <summary>
    /// 等级提升奖励弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPanel"/>（460x530），
    /// 内含"等级提升"标题（书法字体 Display 样式 + <see cref="InkWashTheme.GoldBright"/> 鎏金亮色）、
    /// 等级数字（带金色光晕呼吸动画，alpha 在 0.3~0.8 之间循环）、
    /// 属性变化列表（前/后对比，朱红箭头"→"）与"继续"按钮。
    /// 通过 <see cref="SetLevelUp"/> 设置等级与属性变化数据，
    /// 通过 <see cref="Confirmed"/> 事件通知外部确认。初始数据为 mock，
    /// 通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class RewardLevelUpPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>居中面板宽度（像素）</summary>
        private const float PanelWidth = 460f;

        /// <summary>居中面板高度（像素）</summary>
        private const float PanelHeight = 530f;

        /// <summary>"等级提升"标题 X 坐标（面板内水平居中：(460-380)/2 = 40）</summary>
        private const float TitleX = 40f;

        /// <summary>"等级提升"标题 Y 坐标</summary>
        private const float TitleY = 24f;

        /// <summary>"等级提升"标题宽度</summary>
        private const float TitleWidth = 380f;

        /// <summary>"等级提升"标题高度</summary>
        private const float TitleHeight = 40f;

        /// <summary>等级数字 X 坐标（面板内水平居中：(460-300)/2 = 80）</summary>
        private const float LevelX = 80f;

        /// <summary>等级数字 Y 坐标</summary>
        private const float LevelY = 76f;

        /// <summary>等级数字宽度</summary>
        private const float LevelWidth = 300f;

        /// <summary>等级数字高度</summary>
        private const float LevelHeight = 100f;

        /// <summary>金色光晕尺寸（正方形，大于等级数字形成外发光）</summary>
        private const float GlowSize = 200f;

        /// <summary>金色光晕 X 坐标（与等级数字中心对齐：(80 + 300/2) - 200/2 = 130）</summary>
        private const float GlowX = 130f;

        /// <summary>金色光晕 Y 坐标（与等级数字中心对齐：(76 + 100/2) - 200/2 = 26）</summary>
        private const float GlowY = 26f;

        /// <summary>金色光晕呼吸速度（弧度/秒）</summary>
        private const float GlowSpeed = 2.0f;

        /// <summary>金色光晕最小 alpha</summary>
        private const float GlowAlphaMin = 0.3f;

        /// <summary>金色光晕最大 alpha</summary>
        private const float GlowAlphaMax = 0.8f;

        /// <summary>"属性变化"分区标题 X 坐标</summary>
        private const float AttrSectionTitleX = 40f;

        /// <summary>"属性变化"分区标题 Y 坐标</summary>
        private const float AttrSectionTitleY = 192f;

        /// <summary>"属性变化"分区标题宽度</summary>
        private const float AttrSectionTitleWidth = 380f;

        /// <summary>"属性变化"分区标题高度</summary>
        private const float AttrSectionTitleHeight = 24f;

        /// <summary>属性行起始 Y 坐标</summary>
        private const float AttrRowStartY = 228f;

        /// <summary>属性行高度</summary>
        private const float AttrRowHeight = 32f;

        /// <summary>属性行间距</summary>
        private const float AttrRowGap = 4f;

        /// <summary>最大属性行数（支持 4-6 项变化）</summary>
        private const int MaxAttrRows = 6;

        /// <summary>属性名 X 坐标</summary>
        private const float AttrNameX = 40f;

        /// <summary>属性名宽度</summary>
        private const float AttrNameW = 100f;

        /// <summary>旧值 X 坐标</summary>
        private const float AttrOldX = 160f;

        /// <summary>旧值宽度</summary>
        private const float AttrOldW = 80f;

        /// <summary>箭头 X 坐标</summary>
        private const float AttrArrowX = 245f;

        /// <summary>箭头宽度</summary>
        private const float AttrArrowW = 20f;

        /// <summary>新值 X 坐标</summary>
        private const float AttrNewX = 270f;

        /// <summary>新值宽度</summary>
        private const float AttrNewW = 110f;

        /// <summary>"继续"按钮 X 坐标（面板内水平居中：(460-200)/2 = 130）</summary>
        private const float ContinueX = 130f;

        /// <summary>"继续"按钮宽度</summary>
        private const float ContinueWidth = 200f;

        /// <summary>"继续"按钮高度</summary>
        private const float ContinueHeight = 44f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>居中面板</summary>
        private InkPanel _panel;

        /// <summary>金色光晕层（绘制于等级数字下方）</summary>
        private LevelGlowControl _glow;

        /// <summary>"等级提升"标题</summary>
        private InkTextBlock _titleText;

        /// <summary>等级数字</summary>
        private InkTextBlock _levelText;

        /// <summary>"属性变化"分区标题</summary>
        private InkTextBlock _attrSectionTitle;

        /// <summary>属性名文本数组</summary>
        private InkTextBlock[] _attrNameTexts;

        /// <summary>属性旧值文本数组</summary>
        private InkTextBlock[] _attrOldTexts;

        /// <summary>属性箭头文本数组</summary>
        private InkTextBlock[] _attrArrowTexts;

        /// <summary>属性新值文本数组</summary>
        private InkTextBlock[] _attrNewTexts;

        /// <summary>"继续"按钮</summary>
        private InkButton _continueButton;

        // ===================================================================
        // 屏幕尺寸缓存与动画时间
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>金色光晕动画累计时间（秒）</summary>
        private float _glowTime;

        // ===================================================================
        // mock 数据
        // =======================================================================

        /// <summary>mock 当前等级</summary>
        private int _mockLevel = 43;

        /// <summary>mock 属性变化数据</summary>
        private AttributeChange[] _mockChanges;

        // ===================================================================
        // 嵌套结构：属性变化
        // =======================================================================

        /// <summary>
        /// 属性变化数据。记录单项属性的前后对比值。
        /// </summary>
        public struct AttributeChange
        {
            /// <summary>属性名（如"攻击力"）</summary>
            public string Name;

            /// <summary>变化前数值</summary>
            public int OldValue;

            /// <summary>变化后数值</summary>
            public int NewValue;
        }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全屏遮罩与居中面板，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public RewardLevelUpPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 初始化 mock 数据
            _mockChanges = new AttributeChange[]
            {
                new AttributeChange { Name = "气血", OldValue = 12450, NewValue = 13200 },
                new AttributeChange { Name = "攻击", OldValue = 3280, NewValue = 3450 },
                new AttributeChange { Name = "防御", OldValue = 2150, NewValue = 2280 },
                new AttributeChange { Name = "内力", OldValue = 2000, NewValue = 2200 },
                new AttributeChange { Name = "身法", OldValue = 1800, NewValue = 1950 },
            };

            // 3. 外壳：全屏拉伸 + 半透明遮罩 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                // 4. 居中面板
                _panel = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, PanelHeight),
                };
                AddChild(_panel);

                // 5. 面板内子控件（光晕先于等级数字添加，确保数字绘制于光晕之上）
                BuildGlow();
                BuildTitleText();
                BuildLevelText();
                BuildAttrSectionTitle();
                BuildAttributeRows();
                BuildContinueButton();

                // 6. 应用初始布局（基于屏幕尺寸居中面板）
                ApplyLayout();

                // 7. 应用 mock 数据到子控件
                ApplyMockData();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardLevelUpPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 16.2：金色光晕层。
        /// 自定义 <see cref="LevelGlowControl"/>，尺寸 200x200，位置与等级数字中心对齐，
        /// 绘制金色径向渐变。alpha 由 <see cref="Update"/> 驱动在 0.3~0.8 之间循环。
        /// </summary>
        private void BuildGlow()
        {
            _glow = new LevelGlowControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(GlowX, GlowY),
                Size = new Float2(GlowSize, GlowSize),
                GlowAlpha = (GlowAlphaMin + GlowAlphaMax) * 0.5f,
            };
            _panel.AddChild(_glow);
        }

        /// <summary>
        /// SubTask 16.2："等级提升"标题。
        /// <see cref="InkTextBlock"/> Display 样式（毛笔书法字体），文本"等级提升"，
        /// 位置 (40, 24)，宽度 380，水平居中，字色 <see cref="InkWashTheme.GoldBright"/>（鎏金亮色）。
        /// </summary>
        private void BuildTitleText()
        {
            _titleText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "等级提升",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, TitleY),
                Size = new Float2(TitleWidth, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_titleText);
        }

        /// <summary>
        /// SubTask 16.2：等级数字。
        /// <see cref="InkTextBlock"/> Number 样式（覆盖字号为 64px），文本"Lv. 43"，
        /// 位置 (80, 76)，尺寸 (300, 100)，水平居中，字色 <see cref="InkWashTheme.GoldBright"/>。
        /// </summary>
        private void BuildLevelText()
        {
            _levelText = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "Lv. 43",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(LevelX, LevelY),
                Size = new Float2(LevelWidth, LevelHeight),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 64f),
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_levelText);
        }

        /// <summary>
        /// SubTask 16.3："属性变化"分区标题。
        /// <see cref="InkTextBlock"/> Heading 样式，文本"属性变化"，
        /// 位置 (40, 192)，宽度 380，水平居中，字色 <see cref="InkWashTheme.GoldBright"/>。
        /// </summary>
        private void BuildAttrSectionTitle()
        {
            _attrSectionTitle = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "属性变化",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(AttrSectionTitleX, AttrSectionTitleY),
                Size = new Float2(AttrSectionTitleWidth, AttrSectionTitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_attrSectionTitle);
        }

        /// <summary>
        /// SubTask 16.3：属性行（最多 6 行）。
        /// 每行包含：属性名（Subheading 样式，左对齐）、
        /// 旧值（Number 样式，右对齐，<see cref="InkWashTheme.TextTertiary"/> 色）、
        /// 朱红箭头"→"（Number 样式，居中，<see cref="InkWashTheme.VermilionBright"/> 色）、
        /// 新值（Number 样式，左对齐，<see cref="InkWashTheme.TextBrand"/> 色）。
        /// 初始全部隐藏，由 <see cref="ApplyMockData"/> 与 <see cref="SetLevelUp"/> 填充并显示。
        /// </summary>
        private void BuildAttributeRows()
        {
            _attrNameTexts = new InkTextBlock[MaxAttrRows];
            _attrOldTexts = new InkTextBlock[MaxAttrRows];
            _attrArrowTexts = new InkTextBlock[MaxAttrRows];
            _attrNewTexts = new InkTextBlock[MaxAttrRows];

            for (int i = 0; i < MaxAttrRows; i++)
            {
                float rowY = AttrRowStartY + i * (AttrRowHeight + AttrRowGap);

                _attrNameTexts[i] = new InkTextBlock(InkTextStyle.Subheading)
                {
                    Text = string.Empty,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(AttrNameX, rowY),
                    Size = new Float2(AttrNameW, AttrRowHeight),
                    Visible = false,
                };
                _panel.AddChild(_attrNameTexts[i]);

                _attrOldTexts[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = string.Empty,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(AttrOldX, rowY),
                    Size = new Float2(AttrOldW, AttrRowHeight),
                    TextColor = InkWashTheme.TextTertiary,
                    Visible = false,
                };
                _panel.AddChild(_attrOldTexts[i]);

                _attrArrowTexts[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = "→",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(AttrArrowX, rowY),
                    Size = new Float2(AttrArrowW, AttrRowHeight),
                    HorizontalAlignment = TextAlignment.Center,
                    TextColor = InkWashTheme.VermilionBright,
                    Visible = false,
                };
                _panel.AddChild(_attrArrowTexts[i]);

                _attrNewTexts[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = string.Empty,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(AttrNewX, rowY),
                    Size = new Float2(AttrNewW, AttrRowHeight),
                    HorizontalAlignment = TextAlignment.Near,
                    TextColor = InkWashTheme.TextBrand,
                    Visible = false,
                };
                _panel.AddChild(_attrNewTexts[i]);
            }
        }

        /// <summary>
        /// SubTask 16.4："继续"按钮。
        /// <see cref="InkButton"/> Primary Lg，文本"继续"，
        /// 水平居中（X=130），点击触发 <see cref="Confirmed"/> 事件。
        /// Y 坐标由 <see cref="ApplyContinueButtonY"/> 根据可见属性行数动态计算。
        /// </summary>
        private void BuildContinueButton()
        {
            _continueButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "继续",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ContinueX, AttrRowStartY),
                Size = new Float2(ContinueWidth, ContinueHeight),
            };
            _continueButton.ButtonClicked += OnContinueButtonClicked;
            _panel.AddChild(_continueButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 确认事件。点击"继续"按钮时触发，由外部订阅后处理奖励发放与关闭弹窗。
        /// </summary>
        public event Action Confirmed;

        /// <summary>
        /// "继续"按钮点击处理：触发 <see cref="Confirmed"/> 事件。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnContinueButtonClicked(Button button)
        {
            try
            {
                Confirmed?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[RewardLevelUpPage] Confirmed 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 设置等级提升数据。
        /// 更新等级数字显示，并根据 <paramref name="changes"/> 数组填充属性行
        /// （最多 <see cref="MaxAttrRows"/> 行，超出部分忽略）。同时重算"继续"按钮位置。
        /// </summary>
        /// <param name="newLevel">新等级</param>
        /// <param name="changes">属性变化数组</param>
        public void SetLevelUp(int newLevel, AttributeChange[] changes)
        {
            if (_levelText != null)
                _levelText.Text = $"Lv. {newLevel}";

            if (changes == null)
                return;

            int count = Math.Min(changes.Length, MaxAttrRows);
            for (int i = 0; i < MaxAttrRows; i++)
            {
                bool visible = i < count;
                if (_attrNameTexts[i] != null)
                {
                    _attrNameTexts[i].Visible = visible;
                    if (visible)
                        _attrNameTexts[i].Text = changes[i].Name ?? string.Empty;
                }
                if (_attrOldTexts[i] != null)
                {
                    _attrOldTexts[i].Visible = visible;
                    if (visible)
                        _attrOldTexts[i].Text = changes[i].OldValue.ToString();
                }
                if (_attrArrowTexts[i] != null)
                {
                    _attrArrowTexts[i].Visible = visible;
                }
                if (_attrNewTexts[i] != null)
                {
                    _attrNewTexts[i].Visible = visible;
                    if (visible)
                        _attrNewTexts[i].Text = changes[i].NewValue.ToString();
                }
            }

            ApplyContinueButtonY(count);
        }

        // ===================================================================
        // mock 数据应用
        // =======================================================================

        /// <summary>
        /// 将 mock 数据应用到子控件（等级数字与属性行），并重算"继续"按钮位置。
        /// </summary>
        private void ApplyMockData()
        {
            if (_levelText != null)
                _levelText.Text = $"Lv. {_mockLevel}";

            if (_mockChanges == null)
                return;

            int count = Math.Min(_mockChanges.Length, MaxAttrRows);
            for (int i = 0; i < count; i++)
            {
                if (_attrNameTexts[i] != null)
                {
                    _attrNameTexts[i].Visible = true;
                    _attrNameTexts[i].Text = _mockChanges[i].Name;
                }
                if (_attrOldTexts[i] != null)
                {
                    _attrOldTexts[i].Visible = true;
                    _attrOldTexts[i].Text = _mockChanges[i].OldValue.ToString();
                }
                if (_attrArrowTexts[i] != null)
                    _attrArrowTexts[i].Visible = true;
                if (_attrNewTexts[i] != null)
                {
                    _attrNewTexts[i].Visible = true;
                    _attrNewTexts[i].Text = _mockChanges[i].NewValue.ToString();
                }
            }

            // 隐藏未使用的行
            for (int i = count; i < MaxAttrRows; i++)
            {
                if (_attrNameTexts[i] != null)
                    _attrNameTexts[i].Visible = false;
                if (_attrOldTexts[i] != null)
                    _attrOldTexts[i].Visible = false;
                if (_attrArrowTexts[i] != null)
                    _attrArrowTexts[i].Visible = false;
                if (_attrNewTexts[i] != null)
                    _attrNewTexts[i].Visible = false;
            }

            ApplyContinueButtonY(count);
        }

        /// <summary>
        /// 根据可见属性行数重算"继续"按钮 Y 坐标。
        /// 按钮位于最后一行下方 16px 处。
        /// </summary>
        /// <param name="visibleRows">可见属性行数</param>
        private void ApplyContinueButtonY(int visibleRows)
        {
            if (_continueButton == null)
                return;

            float buttonY = AttrRowStartY + visibleRows * (AttrRowHeight + AttrRowGap) + 16f;
            _continueButton.Location = new Float2(ContinueX, buttonY);
        }

        // ===================================================================
        // 金色光晕动画
        // =======================================================================

        /// <summary>
        /// 每帧更新金色光晕呼吸动画。
        /// 累计时间并使用 <see cref="Mathf.Sin"/> 计算 alpha 在
        /// <see cref="GlowAlphaMin"/>~<see cref="GlowAlphaMax"/>（0.3~0.8）之间循环。
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）</param>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            _glowTime += Time.DeltaTime;

            // sin 映射到 0..1，再线性映射到 0.3..0.8
            float t = 0.5f + 0.5f * Mathf.Sin(_glowTime * GlowSpeed);
            float alpha = GlowAlphaMin + (GlowAlphaMax - GlowAlphaMin) * t;

            if (_glow != null)
                _glow.GlowAlpha = alpha;
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算居中面板位置（保持面板尺寸不变）。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            if (_panel != null)
            {
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - PanelHeight) * 0.5f);
            }
        }

        /// <summary>
        /// 在屏幕尺寸变化时重新布局遮罩与居中面板。
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
        // 嵌套：金色光晕渲染控件
        // =======================================================================

        /// <summary>
        /// 金色光晕渲染控件。
        /// 绘制以控件中心为圆心、<see cref="InkWashTheme.GoldPrimary"/> 为色的径向渐变，
        /// 通过 <see cref="GlowAlpha"/> 控制整体不透明度，由外部 <see cref="RewardLevelUpPage.Update"/>
        /// 驱动实现呼吸效果。
        /// </summary>
        private class LevelGlowControl : ContainerControl
        {
            /// <summary>当前光晕 alpha（0.0~1.0）</summary>
            private float _alpha = 0.55f;

            /// <summary>
            /// 光晕整体不透明度（0.0~1.0），会被 <see cref="Mathf.Clamp"/> 限制到合法范围。
            /// </summary>
            public float GlowAlpha
            {
                get => _alpha;
                set => _alpha = Mathf.Clamp(value, 0f, 1f);
            }

            /// <summary>
            /// 构造函数：透明背景、不裁剪、不抢焦点。
            /// </summary>
            public LevelGlowControl()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var center = new Float2(Width * 0.5f, Height * 0.5f);
                float radius = Mathf.Min(Width, Height) * 0.5f;

                // 圆心金色（带 alpha），边缘透明
                Color centerColor = new Color(
                    InkWashTheme.GoldPrimary.R,
                    InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B,
                    _alpha);
                Color edgeColor = new Color(centerColor.R, centerColor.G, centerColor.B, 0f);

                // 多段径向渐变近似模糊光晕
                InkRenderHelper.FillRadialGradient(center, radius, centerColor, edgeColor, 20);
            }
        }
    }
}
