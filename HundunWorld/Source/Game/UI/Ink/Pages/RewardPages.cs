using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// 成就奖励弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPanelElevated"/>（400x360），
    /// 内含传奇品质成就图标（带金色光晕呼吸动画，alpha 在 0.3~0.8 之间循环）、
    /// 成就名、描述与"领取"按钮。通过 <see cref="Claimed"/> 事件通知外部领取。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class RewardAchievementPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>居中抬升面板宽度（像素）</summary>
        private const float PanelWidth = 400f;

        /// <summary>居中抬升面板高度（像素）</summary>
        private const float PanelHeight = 360f;

        /// <summary>成就图标尺寸（正方形，像素）</summary>
        private const float CellSize = 80f;

        /// <summary>成就图标 X 坐标（面板内水平居中：(400-80)/2 = 160）</summary>
        private const float CellX = 160f;

        /// <summary>成就图标 Y 坐标</summary>
        private const float CellY = 40f;

        /// <summary>成就名文本 X 坐标（面板内水平居中：(400-320)/2 = 40）</summary>
        private const float NameX = 40f;

        /// <summary>成就名文本 Y 坐标</summary>
        private const float NameY = 140f;

        /// <summary>成就名文本宽度</summary>
        private const float NameWidth = 320f;

        /// <summary>成就名文本高度</summary>
        private const float NameHeight = 40f;

        /// <summary>成就描述文本 X 坐标</summary>
        private const float DescX = 40f;

        /// <summary>成就描述文本 Y 坐标</summary>
        private const float DescY = 200f;

        /// <summary>成就描述文本宽度</summary>
        private const float DescWidth = 320f;

        /// <summary>成就描述文本高度</summary>
        private const float DescHeight = 60f;

        /// <summary>"领取"按钮 X 坐标（面板内水平居中：(400-160)/2 = 120）</summary>
        private const float ClaimX = 120f;

        /// <summary>"领取"按钮 Y 坐标</summary>
        private const float ClaimY = 290f;

        /// <summary>"领取"按钮宽度</summary>
        private const float ClaimWidth = 160f;

        /// <summary>"领取"按钮高度</summary>
        private const float ClaimHeight = 44f;

        /// <summary>金色光晕尺寸（大于图标，形成外发光，正方形）</summary>
        private const float GlowSize = 120f;

        /// <summary>金色光晕相对图标的偏移（使光晕中心与图标中心对齐：(80-120)/2 = -20）</summary>
        private const float GlowOffset = (CellSize - GlowSize) * 0.5f;

        /// <summary>金色光晕呼吸速度（弧度/秒）</summary>
        private const float GlowSpeed = 2.0f;

        /// <summary>金色光晕最小 alpha</summary>
        private const float GlowAlphaMin = 0.3f;

        /// <summary>金色光晕最大 alpha</summary>
        private const float GlowAlphaMax = 0.8f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>居中抬升面板</summary>
        private InkPanelElevated _panel;

        /// <summary>金色光晕层（绘制于图标下方）</summary>
        private GoldGlowControl _glow;

        /// <summary>成就图标格子</summary>
        private InkCell _iconCell;

        /// <summary>成就名文本</summary>
        private InkTextBlock _nameText;

        /// <summary>成就描述文本</summary>
        private InkTextBlock _descText;

        /// <summary>"领取"按钮</summary>
        private InkButton _claimButton;

        // ===================================================================
        // 屏幕尺寸缓存与动画时间
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>金色光晕动画累计时间（秒）</summary>
        private float _glowTime;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全屏遮罩与居中面板，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public RewardAchievementPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            // 2. 外壳：全屏拉伸 + 半透明遮罩 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                // 3. 居中抬升面板（尺寸固定，位置由 ApplyLayout 居中计算）
                _panel = new InkPanelElevated
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, PanelHeight),
                };
                AddChild(_panel);

                // 4. 面板内子控件（光晕先于图标添加，确保图标绘制于光晕之上）
                BuildGlow();
                BuildIconCell();
                BuildNameText();
                BuildDescText();
                BuildClaimButton();

                // 5. 应用初始布局（基于屏幕尺寸居中面板）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardAchievementPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 11.1：金色光晕层。
        /// 自定义 <see cref="GoldGlowControl"/>，尺寸 120x120，位置与图标中心对齐，
        /// 绘制金色径向渐变。alpha 由 <see cref="Update"/> 驱动在 0.3~0.8 之间循环。
        /// </summary>
        private void BuildGlow()
        {
            _glow = new GoldGlowControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CellX + GlowOffset, CellY + GlowOffset),
                Size = new Float2(GlowSize, GlowSize),
                GlowAlpha = (GlowAlphaMin + GlowAlphaMax) * 0.5f,
            };
            _panel.AddChild(_glow);
        }

        /// <summary>
        /// SubTask 11.1：成就图标格子。
        /// <see cref="InkCell"/> 尺寸 80x80，位置 (160, 40)，
        /// <see cref="InkWashTheme.InkQuality.Legendary"/> 品质（朱红光晕边框）。
        /// </summary>
        private void BuildIconCell()
        {
            _iconCell = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CellX, CellY),
                Size = new Float2(CellSize, CellSize),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            _panel.AddChild(_iconCell);
        }

        /// <summary>
        /// SubTask 11.1：成就名文本。
        /// <see cref="InkTextBlock"/> Display 样式，文本"江湖初悟"，
        /// 位置 (40, 140)，宽度 320，水平居中。
        /// </summary>
        private void BuildNameText()
        {
            _nameText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "江湖初悟",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(NameX, NameY),
                Size = new Float2(NameWidth, NameHeight),
                HorizontalAlignment = TextAlignment.Center,
            };
            _panel.AddChild(_nameText);
        }

        /// <summary>
        /// SubTask 11.1：成就描述文本。
        /// <see cref="InkTextBlock"/> Body 样式，文本"完成首次江湖历练，领悟武学真意"，
        /// 位置 (40, 200)，尺寸 (320, 60)，水平居中，字色 <see cref="InkWashTheme.TextSecondary"/>，
        /// 启用 <see cref="TextWrapping.WrapWords"/> 以支持动态长文本换行。
        /// </summary>
        private void BuildDescText()
        {
            _descText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "完成首次江湖历练，领悟武学真意",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(DescX, DescY),
                Size = new Float2(DescWidth, DescHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextSecondary,
                Wrapping = TextWrapping.WrapWords,
            };
            _panel.AddChild(_descText);
        }

        /// <summary>
        /// SubTask 11.1："领取"按钮。
        /// <see cref="InkButton"/> Primary Lg，位置 (120, 290)，尺寸 (160, 44)，
        /// 点击触发 <see cref="Claimed"/> 事件。
        /// </summary>
        private void BuildClaimButton()
        {
            _claimButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "领取",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ClaimX, ClaimY),
                Size = new Float2(ClaimWidth, ClaimHeight),
            };
            _claimButton.ButtonClicked += OnClaimButtonClicked;
            _panel.AddChild(_claimButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 领取事件。点击"领取"按钮时触发，由外部（Router）订阅后处理奖励发放与关闭弹窗。
        /// </summary>
        public event Action Claimed;

        /// <summary>
        /// 领取按钮点击处理：触发 <see cref="Claimed"/> 事件。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnClaimButtonClicked(Button button)
        {
            try
            {
                Claimed?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError(
                    $"[RewardAchievementPage] Claimed 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 动态设置成就名与描述。
        /// </summary>
        /// <param name="name">成就名</param>
        /// <param name="description">成就描述（支持多行换行）</param>
        public void SetAchievement(string name, string description)
        {
            if (_nameText != null)
                _nameText.Text = name ?? string.Empty;

            if (_descText != null)
                _descText.Text = description ?? string.Empty;
        }

        /// <summary>
        /// 动态设置奖励内容（SubTask 7.1）。
        /// 设置成就名、描述，并将奖励物品列表追加到描述文本中显示。
        /// </summary>
        /// <param name="name">成就名</param>
        /// <param name="description">成就描述</param>
        /// <param name="items">奖励物品名数组（追加到描述下方显示）</param>
        public void SetReward(string name, string description, string[] items)
        {
            if (_nameText != null)
                _nameText.Text = name ?? string.Empty;

            if (_descText != null)
            {
                string text = description ?? string.Empty;
                if (items != null && items.Length > 0)
                {
                    text += "\n奖励：" + string.Join("、", items);
                }
                _descText.Text = text;
            }
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
        /// 通过 <see cref="GlowAlpha"/> 控制整体不透明度，由外部 <see cref="RewardAchievementPage.Update"/>
        /// 驱动实现呼吸效果。
        /// </summary>
        private class GoldGlowControl : ContainerControl
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
            public GoldGlowControl()
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

    // ===================================================================
    // RewardQuestCompletePage
    // =======================================================================

    /// <summary>
    /// 任务完成奖励弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPanelElevated"/>（440x420），
    /// 内含任务完成标题、任务名、奖励物品列表（3 个品质格）、经验与铜钱数值、"领取"按钮。
    /// 通过 <see cref="Claimed"/> 事件通知外部领取。全部数据为 mock，
    /// 通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class RewardQuestCompletePage : ContainerControl, IInkPage
    {
        private const float PanelWidth = 500f;
        private const float PanelPaddingX = 32f;
        private const float PanelPaddingY = 24f;

        private const float TitleY = 24f;
        private const float TitleHeight = 40f;

        private const float StampSize = 40f;
        private const float StampY = 28f;
        private const float StampOffsetX = 100f;

        private const float NameY = 80f;
        private const float NameHeight = 32f;
        private const float NameWidth = 400f;

        private const float TagY = 118f;

        private const float EvaluationY = 140f;
        private const float EvaluationGradeSize = 64f;
        private const float EvaluationStarsY = 210f;

        private const float RewardSectionY = 240f;
        private const float RewardItemHeight = 40f;
        private const float RewardItemGap = 4f;

        private const float ExpSectionY = 400f;
        private const float ExpBarHeight = 8f;
        private const float ExpBarWidth = 432f;

        private const float ActionsY = 460f;

        private InkPanelElevated _panel;
        private InkTextBlock _titleText;
        private InkPanel _stampPanel;
        private InkTextBlock _stampChar;
        private InkTextBlock _questName;
        private InkTag _questTag;
        private InkTextBlock _evaluationSubtitle;
        private InkTextBlock _evaluationGrade;
        private InkTextBlock _evaluationRank;
        private InkPanel[] _starPanels;
        private InkTextBlock _rewardTitle;
        private InkPanel[] _rewardRows;
        private InkTextBlock[] _rewardLabels;
        private InkTextBlock[] _rewardValues;
        private InkTextBlock _expSubtitle;
        private InkTextBlock _expValue;
        private InkPanel _expBar;
        private InkPanel _expBarFill;
        private InkTextBlock _expRemaining;
        private InkButton _confirmButton;
        private InkButton _photoButton;
        private InkTextBlock _shareText;

        private Float2 _screenSize;

        public RewardQuestCompletePage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                _panel = new InkPanelElevated
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, 520f),
                };
                AddChild(_panel);

                BuildTitleSection();
                BuildNameSection();
                BuildEvaluationSection();
                BuildRewardSection();
                BuildExpSection();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardQuestCompletePage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildTitleSection()
        {
            _titleText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "任务完成",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 160f) * 0.5f - StampOffsetX * 0.5f, TitleY),
                Size = new Float2(160f, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_titleText);

            _stampPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - StampSize) * 0.5f + StampOffsetX * 0.5f, StampY),
                Size = new Float2(StampSize, StampSize),
                BackgroundColor = InkWashTheme.VermilionPrimary,
            };
            _stampPanel.Rotation = -5f * Mathf.DegreesToRadians;
            _panel.AddChild(_stampPanel);

            _stampChar = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "成",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(StampSize, StampSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
            };
            _stampPanel.AddChild(_stampChar);
        }

        private void BuildNameSection()
        {
            _questName = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "昆仑山异兽调查",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - NameWidth) * 0.5f, NameY),
                Size = new Float2(NameWidth, NameHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperBright,
            };
            _panel.AddChild(_questName);

            _questTag = new InkTag
            {
                Text = "主线任务",
                TagVariant = InkTagVariant.Brand,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 80f) * 0.5f, TagY),
                Size = new Float2(80f, 22f),
            };
            _panel.AddChild(_questTag);
        }

        private void BuildEvaluationSection()
        {
            _evaluationSubtitle = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "完成评价",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, EvaluationY),
                Size = new Float2(100f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_evaluationSubtitle);

            _evaluationGrade = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "S",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - EvaluationGradeSize) * 0.5f, EvaluationY + 24f),
                Size = new Float2(EvaluationGradeSize, EvaluationGradeSize),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 56f),
            };
            _panel.AddChild(_evaluationGrade);

            _evaluationRank = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "完美通关",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, EvaluationY + 96f),
                Size = new Float2(100f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_evaluationRank);

            _starPanels = new InkPanel[5];
            float starsStartX = (PanelWidth - 100f) * 0.5f;
            for (int i = 0; i < 5; i++)
            {
                _starPanels[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(starsStartX + i * 20f, EvaluationStarsY),
                    Size = new Float2(16f, 16f),
                    BackgroundColor = InkWashTheme.GoldPrimary,
                };
                _panel.AddChild(_starPanels[i]);
            }
        }

        private void BuildRewardSection()
        {
            _rewardTitle = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "获得奖励",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, RewardSectionY),
                Size = new Float2(100f, 24f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_rewardTitle);

            _rewardRows = new InkPanel[5];
            _rewardLabels = new InkTextBlock[5];
            _rewardValues = new InkTextBlock[5];

            string[] labels = { "经验", "铜钱", "元宝", "物品", "声望" };
            string[] values = { "+8,500", "+3,200", "+50", "墨麒麟鳞片 ×2", "+100 青城派" };
            Color[] labelColors = { InkWashTheme.PaperAged, InkWashTheme.PaperAged, InkWashTheme.PaperAged, InkWashTheme.PaperAged, InkWashTheme.PaperAged };
            Color[] valueColors = { InkWashTheme.JadeBright, InkWashTheme.GoldBright, InkWashTheme.GoldBright, InkWashTheme.VermilionBright, InkWashTheme.JadeBright };

            float rowStartX = (PanelWidth - 432f) * 0.5f;
            float rowStartY = RewardSectionY + 32f;

            for (int i = 0; i < 5; i++)
            {
                float rowY = rowStartY + i * (RewardItemHeight + RewardItemGap);

                _rewardRows[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(rowStartX, rowY),
                    Size = new Float2(432f, RewardItemHeight),
                    BackgroundColor = Color.Transparent,
                };
                _panel.AddChild(_rewardRows[i]);

                _rewardLabels[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 10f),
                    Size = new Float2(80f, 20f),
                    TextColor = labelColors[i],
                };
                _rewardRows[i].AddChild(_rewardLabels[i]);

                _rewardValues[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = values[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(300f, 10f),
                    Size = new Float2(92f, 20f),
                    HorizontalAlignment = TextAlignment.Far,
                    TextColor = valueColors[i],
                };
                _rewardRows[i].AddChild(_rewardValues[i]);
            }
        }

        private void BuildExpSection()
        {
            float expStartX = (PanelWidth - ExpBarWidth) * 0.5f;

            _expSubtitle = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "经验值",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(expStartX, ExpSectionY),
                Size = new Float2(100f, 20f),
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_expSubtitle);

            _expValue = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "8,500 / 12,000",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(expStartX + ExpBarWidth - 100f, ExpSectionY),
                Size = new Float2(100f, 20f),
                HorizontalAlignment = TextAlignment.Far,
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_expValue);

            _expBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(expStartX, ExpSectionY + 24f),
                Size = new Float2(ExpBarWidth, ExpBarHeight),
                BackgroundColor = new Color(0f, 0f, 0f, 0.4f),
            };
            _panel.AddChild(_expBar);

            _expBarFill = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(ExpBarWidth * 0.708f, ExpBarHeight),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _expBar.AddChild(_expBarFill);

            _expRemaining = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "距离升级还需 3,500 经验",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 160f) * 0.5f, ExpSectionY + 40f),
                Size = new Float2(160f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_expRemaining);
        }

        private void BuildActions()
        {
            _confirmButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "确认",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f, ActionsY),
                Size = new Float2(200f, 44f),
            };
            _confirmButton.ButtonClicked += OnConfirmButtonClicked;
            _panel.AddChild(_confirmButton);

            _photoButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                Text = "拍照留念",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f - 80f, ActionsY + 56f),
                Size = new Float2(100f, 28f),
            };
            _photoButton.ButtonClicked += OnPhotoButtonClicked;
            _panel.AddChild(_photoButton);

            _shareText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "分享战绩",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f + 140f, ActionsY + 62f),
                Size = new Float2(80f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperFaded,
            };
            _panel.AddChild(_shareText);
        }

        public event Action Confirmed;
        public event Action TakePhoto;
        public event Action Claimed;

        private void OnConfirmButtonClicked(Button button)
        {
            Confirmed?.Invoke();
            Claimed?.Invoke();
        }

        private void OnPhotoButtonClicked(Button button)
        {
            TakePhoto?.Invoke();
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ActionsY + 80f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
            }
        }

        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }
    }

    // ===================================================================
    // RewardCongratulationsPage
    // =======================================================================

    /// <summary>
    /// 恭喜获得 — 多物品奖励庆祝弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPanelElevated"/>（560x可变高），
    /// 内含标题区（"恭喜获得"+金色分隔线+副标题）、物品网格（2列，支持6个物品）、品质徽章、
    /// 总价值栏、"全部领取"和"查看背包"按钮。通过 <see cref="Claimed"/> 和 <see cref="ViewBag"/>
    /// 事件通知外部操作。全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class RewardCongratulationsPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        private const float PanelWidth = 560f;
        private const float PanelPaddingX = 32f;
        private const float PanelPaddingY = 24f;

        private const float TitleY = 20f;
        private const float TitleHeight = 40f;
        private const float TitleWidth = 496f;

        private const float DividerY = 68f;
        private const float DividerWidth = 140f;

        private const float SubtitleY = 90f;
        private const float SubtitleHeight = 20f;
        private const float SubtitleWidth = 496f;

        private const float ItemGridY = 120f;
        private const float ItemGridWidth = 496f;
        private const float ItemCellWidth = 238f;
        private const float ItemCellHeight = 110f;
        private const float ItemCellGap = 20f;

        private const float SectionDividerY = 390f;

        private const float TotalValueBarY = 400f;
        private const float TotalValueBarWidth = 496f;
        private const float TotalValueBarHeight = 40f;

        private const float ActionsY = 450f;

        private const float ClaimBtnWidth = 200f;
        private const float ClaimBtnHeight = 44f;
        private const float BagBtnWidth = 140f;
        private const float BagBtnHeight = 44f;
        private const float BtnGap = 12f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        private InkPanelElevated _panel;
        private InkTextBlock _titleText;
        private InkTextBlock _subtitleText;
        private InkCell[] _itemCells;
        private InkTextBlock[] _itemNames;
        private InkTag[] _itemBadges;
        private InkTextBlock[] _itemQuantities;
        private InkTextBlock _totalValueNumber;
        private InkButton _claimButton;
        private InkButton _bagButton;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        private Float2 _screenSize;

        // ===================================================================
        // 构造函数
        // =======================================================================

        public RewardCongratulationsPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                _panel = new InkPanelElevated
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, 500f),
                };
                AddChild(_panel);

                BuildTitle();
                BuildSubtitle();
                BuildItemGrid();
                BuildTotalValueBar();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardCongratulationsPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        private void BuildTitle()
        {
            _titleText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "恭喜获得",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - TitleWidth) * 0.5f, TitleY),
                Size = new Float2(TitleWidth, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_titleText);
        }

        private void BuildSubtitle()
        {
            _subtitleText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "以下物品已放入背包",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - SubtitleWidth) * 0.5f, SubtitleY),
                Size = new Float2(SubtitleWidth, SubtitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.PaperAged,
            };
            _panel.AddChild(_subtitleText);
        }

        private void BuildItemGrid()
        {
            _itemCells = new InkCell[6];
            _itemNames = new InkTextBlock[6];
            _itemBadges = new InkTag[6];
            _itemQuantities = new InkTextBlock[6];

            string[] itemNames = { "青锋剑·寒光", "紫霞秘籍", "灵石", "百年人参", "铜钱", "经验丹" };
            InkWashTheme.InkQuality[] qualities = {
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Uncommon
            };
            string[] quantities = { "×1", "×1", "×50", "×3", "×5000", "×5" };
            string[] qualityLabels = { "史", "史", "珍", "良", "凡", "良" };

            float gridStartX = (PanelWidth - ItemGridWidth) * 0.5f;

            for (int i = 0; i < 6; i++)
            {
                int row = i / 2;
                int col = i % 2;

                float cellX = gridStartX + col * (ItemCellWidth + ItemCellGap);
                float cellY = ItemGridY + row * (ItemCellHeight + ItemCellGap);

                InkPanel cellPanel = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cellX, cellY),
                    Size = new Float2(ItemCellWidth, ItemCellHeight),
                    BackgroundColor = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.9f),
                };
                _panel.AddChild(cellPanel);

                float iconSize = 56f;
                float iconX = (ItemCellWidth - iconSize) * 0.5f;
                float iconY = 8f;

                _itemCells[i] = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(iconX, iconY),
                    Size = new Float2(iconSize, iconSize),
                    Quality = qualities[i],
                };
                cellPanel.AddChild(_itemCells[i]);

                float nameY = iconY + iconSize + 4f;
                _itemNames[i] = new InkTextBlock(InkTextStyle.Subheading)
                {
                    Text = itemNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0, nameY),
                    Size = new Float2(ItemCellWidth, 20f),
                    HorizontalAlignment = TextAlignment.Center,
                };
                cellPanel.AddChild(_itemNames[i]);

                float footerY = nameY + 20f;
                float badgeWidth = 40f;
                float badgeHeight = 18f;
                float badgeX = (ItemCellWidth - badgeWidth - 60f) * 0.5f;

                _itemBadges[i] = new InkTag
                {
                    Text = qualityLabels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(badgeX, footerY),
                    Size = new Float2(badgeWidth, badgeHeight),
                };
                SetBadgeQuality(_itemBadges[i], qualities[i]);
                cellPanel.AddChild(_itemBadges[i]);

                float quantityX = badgeX + badgeWidth + 8f;
                _itemQuantities[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = quantities[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(quantityX, footerY),
                    Size = new Float2(60f, 18f),
                    HorizontalAlignment = TextAlignment.Near,
                    TextColor = InkWashTheme.GoldBright,
                };
                cellPanel.AddChild(_itemQuantities[i]);
            }
        }

        private void SetBadgeQuality(InkTag tag, InkWashTheme.InkQuality quality)
        {
            switch (quality)
            {
                case InkWashTheme.InkQuality.Epic:
                    tag.TextColor = InkWashTheme.QualityEpic;
                    break;
                case InkWashTheme.InkQuality.Rare:
                    tag.TextColor = InkWashTheme.QualityRare;
                    break;
                case InkWashTheme.InkQuality.Uncommon:
                    tag.TextColor = InkWashTheme.QualityUncommon;
                    break;
                case InkWashTheme.InkQuality.Common:
                default:
                    tag.TextColor = InkWashTheme.QualityCommon;
                    break;
            }
        }

        private void BuildTotalValueBar()
        {
            InkPanel barPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - TotalValueBarWidth) * 0.5f, TotalValueBarY),
                Size = new Float2(TotalValueBarWidth, TotalValueBarHeight),
                BackgroundColor = new Color(0f, 0f, 0f, 0.4f),
            };
            _panel.AddChild(barPanel);

            InkTextBlock label = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "总价值:",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 10f),
                Size = new Float2(60f, 20f),
                TextColor = InkWashTheme.PaperAged,
            };
            barPanel.AddChild(label);

            InkTextBlock prefix = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "约",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(150f, 10f),
                Size = new Float2(20f, 20f),
                TextColor = InkWashTheme.PaperAged,
            };
            barPanel.AddChild(prefix);

            _totalValueNumber = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "12,800",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(175f, 8f),
                Size = new Float2(100f, 24f),
                TextColor = InkWashTheme.GoldBright,
            };
            barPanel.AddChild(_totalValueNumber);

            InkTextBlock unit = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "元宝",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(280f, 10f),
                Size = new Float2(40f, 20f),
                TextColor = InkWashTheme.PaperAged,
            };
            barPanel.AddChild(unit);
        }

        private void BuildActions()
        {
            float totalBtnWidth = ClaimBtnWidth + BagBtnWidth + BtnGap;
            float startX = (PanelWidth - totalBtnWidth) * 0.5f;

            _claimButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "全部领取",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX, ActionsY),
                Size = new Float2(ClaimBtnWidth, ClaimBtnHeight),
            };
            _claimButton.ButtonClicked += OnClaimButtonClicked;
            _panel.AddChild(_claimButton);

            _bagButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "查看背包",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX + ClaimBtnWidth + BtnGap, ActionsY),
                Size = new Float2(BagBtnWidth, BagBtnHeight),
            };
            _bagButton.ButtonClicked += OnBagButtonClicked;
            _panel.AddChild(_bagButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        public event Action Claimed;
        public event Action ViewBag;

        private void OnClaimButtonClicked(Button button)
        {
            try
            {
                Claimed?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardCongratulationsPage] Claimed 触发失败: {ex.Message}");
            }
        }

        private void OnBagButtonClicked(Button button)
        {
            try
            {
                ViewBag?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardCongratulationsPage] ViewBag 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        public void SetTotalValue(int value)
        {
            if (_totalValueNumber != null)
                _totalValueNumber.Text = value.ToString("N0");
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ActionsY + ClaimBtnHeight + 24f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
            }
        }

        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }
    }

    // ===================================================================
    // RewardMapUnlockPage
    // =======================================================================

    /// <summary>
    /// 地图解锁弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPanelElevated"/>（520x可变高），
    /// 内含标题区（"新区域解锁"+金色分隔线）、区域名（书法字体）、副标题、朱红印章、
    /// 区域描述、区域预览图、探索奖励列表（3行）、"前往探索"和"升级奖励"按钮。
    /// 通过 <see cref="GoExplore"/> 和 <see cref="ViewLevelUp"/> 事件通知外部操作。
    /// 全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class RewardMapUnlockPage : ContainerControl, IInkPage
    {
        private const float PanelWidth = 520f;
        private const float PanelPaddingX = 44f;
        private const float PanelPaddingY = 40f;

        private const float TitleY = 20f;
        private const float TitleHeight = 32f;

        private const float DividerY = 60f;

        private const float AreaNameY = 85f;
        private const float AreaNameHeight = 40f;
        private const float AreaSubY = 130f;
        private const float AreaSubHeight = 20f;

        private const float SealSize = 38f;

        private const float DescY = 160f;
        private const float DescWidth = 400f;
        private const float DescHeight = 50f;

        private const float PreviewY = 220f;
        private const float PreviewWidth = 432f;
        private const float PreviewHeight = 120f;

        private const float RewardsY = 350f;
        private const float RewardRowHeight = 48f;
        private const float RewardRowGap = 8f;

        private const float ActionsY = 490f;
        private const float BtnWidth = 160f;
        private const float BtnHeight = 44f;
        private const float BtnGap = 16f;

        private InkPanelElevated _panel;
        private InkTextBlock _titleText;
        private InkTextBlock _areaNameText;
        private InkTextBlock _areaSubText;
        private InkTextBlock _descText;
        private InkPanel _previewPanel;
        private InkPanel[] _rewardRows;
        private InkTextBlock[] _rewardLabels;
        private InkTextBlock[] _rewardValues;
        private InkButton _exploreButton;
        private InkButton _levelUpButton;

        private Float2 _screenSize;

        public RewardMapUnlockPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                _panel = new InkPanelElevated
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, 550f),
                };
                AddChild(_panel);

                BuildTitle();
                BuildAreaName();
                BuildDesc();
                BuildPreview();
                BuildRewards();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardMapUnlockPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildTitle()
        {
            _titleText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "新区域解锁",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f, TitleY),
                Size = new Float2(200f, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextBrand,
            };
            _panel.AddChild(_titleText);
        }

        private void BuildAreaName()
        {
            _areaNameText = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "昆仑雪山",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f, AreaNameY),
                Size = new Float2(200f, AreaNameHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldBright,
            };
            _panel.AddChild(_areaNameText);

            _areaSubText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "雪域秘境",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 100f) * 0.5f, AreaSubY),
                Size = new Float2(100f, AreaSubHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.GoldDeep,
            };
            _panel.AddChild(_areaSubText);
        }

        private void BuildDesc()
        {
            _descText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "终年积雪的高山之地，传说有上古异兽栖息于此。寒风凛冽，需备足御寒之物方可深入探索。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - DescWidth) * 0.5f, DescY),
                Size = new Float2(DescWidth, DescHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextSecondary,
                Wrapping = TextWrapping.WrapWords,
            };
            _panel.AddChild(_descText);
        }

        private void BuildPreview()
        {
            _previewPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - PreviewWidth) * 0.5f, PreviewY),
                Size = new Float2(PreviewWidth, PreviewHeight),
                BackgroundColor = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.8f),
            };
            _panel.AddChild(_previewPanel);

            InkTextBlock watermark = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "雪",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PreviewWidth - 64f) * 0.5f, 20f),
                Size = new Float2(64f, 64f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = new Color(InkWashTheme.PaperFaded.R, InkWashTheme.PaperFaded.G, InkWashTheme.PaperFaded.B, 0.18f),
            };
            _previewPanel.AddChild(watermark);

            InkTextBlock hint = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "区域预览图",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PreviewWidth - 100f) * 0.5f, 95f),
                Size = new Float2(100f, 18f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextTertiary,
            };
            _previewPanel.AddChild(hint);
        }

        private void BuildRewards()
        {
            _rewardRows = new InkPanel[3];
            _rewardLabels = new InkTextBlock[3];
            _rewardValues = new InkTextBlock[3];

            string[] labels = { "首次进入", "探索度十成", "发现秘境" };
            string[] values = { "经验 ×3000", "铜钱 ×1000", "元宝 ×100" };

            float rowStartX = (PanelWidth - 432f) * 0.5f;

            for (int i = 0; i < 3; i++)
            {
                float rowY = RewardsY + i * (RewardRowHeight + RewardRowGap);

                _rewardRows[i] = new InkPanel
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(rowStartX, rowY),
                    Size = new Float2(432f, RewardRowHeight),
                    BackgroundColor = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.9f),
                };
                _panel.AddChild(_rewardRows[i]);

                _rewardLabels[i] = new InkTextBlock(InkTextStyle.Body)
                {
                    Text = labels[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(40f, 14f),
                    Size = new Float2(120f, 20f),
                    TextColor = InkWashTheme.TextDefault,
                };
                _rewardRows[i].AddChild(_rewardLabels[i]);

                _rewardValues[i] = new InkTextBlock(InkTextStyle.Number)
                {
                    Text = values[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(320f, 14f),
                    Size = new Float2(80f, 20f),
                    HorizontalAlignment = TextAlignment.Far,
                    TextColor = InkWashTheme.GoldBright,
                };
                _rewardRows[i].AddChild(_rewardValues[i]);
            }
        }

        private void BuildActions()
        {
            float totalBtnWidth = BtnWidth * 2 + BtnGap;
            float startX = (PanelWidth - totalBtnWidth) * 0.5f;

            _exploreButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "前往探索",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX, ActionsY),
                Size = new Float2(BtnWidth, BtnHeight),
            };
            _exploreButton.ButtonClicked += OnExploreButtonClicked;
            _panel.AddChild(_exploreButton);

            _levelUpButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "升级奖励",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(startX + BtnWidth + BtnGap, ActionsY),
                Size = new Float2(BtnWidth, BtnHeight),
            };
            _levelUpButton.ButtonClicked += OnLevelUpButtonClicked;
            _panel.AddChild(_levelUpButton);
        }

        public event Action GoExplore;
        public event Action ViewLevelUp;

        private void OnExploreButtonClicked(Button button)
        {
            try
            {
                GoExplore?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardMapUnlockPage] GoExplore 触发失败: {ex.Message}");
            }
        }

        private void OnLevelUpButtonClicked(Button button)
        {
            try
            {
                ViewLevelUp?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardMapUnlockPage] ViewLevelUp 触发失败: {ex.Message}");
            }
        }

        public void SetAreaInfo(string areaName, string areaSub, string description)
        {
            if (_areaNameText != null)
                _areaNameText.Text = areaName ?? string.Empty;
            if (_areaSubText != null)
                _areaSubText.Text = areaSub ?? string.Empty;
            if (_descText != null)
                _descText.Text = description ?? string.Empty;
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ActionsY + BtnHeight + 34f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
            }
        }

        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }
    }

    // ===================================================================
    // RewardTeleportUnlockPage
    // =======================================================================

    /// <summary>
    /// 传送点解锁弹窗。
    /// 全屏半透明遮罩（<see cref="InkWashTheme.Scrim"/>）+ 居中 <see cref="InkPanelElevated"/>（440x可变高），
    /// 内含标题区（"传送点已解锁"+装饰线）、传送图标（旋转圆环+金色圆形+脉冲动画）、
    /// 传送点名称、位置信息、地图缩略图（网格线+传送点标记）、说明文字、
    /// "确认"和"打开地图"按钮。通过 <see cref="Confirmed"/> 和 <see cref="OpenMap"/>
    /// 事件通知外部操作。全部数据为 mock，通过 <see cref="RefreshLayout"/> 支持屏幕尺寸变化。
    /// </summary>
    public class RewardTeleportUnlockPage : ContainerControl, IInkPage
    {
        private const float PanelWidth = 440f;

        private const float TitleY = 20f;
        private const float TitleHeight = 32f;

        private const float IconSectionY = 70f;
        private const float IconSectionSize = 80f;

        private const float TeleportNameY = 160f;
        private const float TeleportNameHeight = 24f;

        private const float LocationY = 190f;

        private const float MapThumbnailY = 220f;
        private const float MapThumbnailWidth = 352f;
        private const float MapThumbnailHeight = 120f;

        private const float InfoTextY = 350f;
        private const float InfoTextWidth = 320f;
        private const float InfoTextHeight = 40f;

        private const float ActionsY = 400f;
        private const float ConfirmBtnWidth = 180f;
        private const float ConfirmBtnHeight = 44f;
        private const float MapBtnWidth = 160f;
        private const float MapBtnHeight = 36f;
        private const float BtnGap = 8f;

        private InkPanelElevated _panel;
        private InkTextBlock _titleText;
        private InkPanel _iconSection;
        private InkTextBlock _teleportName;
        private InkPanel _locationInfo;
        private InkTextBlock _locationText;
        private InkPanel _mapThumbnail;
        private InkTextBlock _infoText;
        private InkButton _confirmButton;
        private InkButton _mapButton;

        private Float2 _screenSize;
        private float _rotationTime;

        public RewardTeleportUnlockPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.Scrim;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                _panel = new InkPanelElevated
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(PanelWidth, 470f),
                };
                AddChild(_panel);

                BuildTitle();
                BuildIconSection();
                BuildTeleportName();
                BuildLocationInfo();
                BuildMapThumbnail();
                BuildInfoText();
                BuildActions();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardTeleportUnlockPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildTitle()
        {
            _titleText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "传送点已解锁",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 200f) * 0.5f, TitleY),
                Size = new Float2(200f, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextBrand,
            };
            _panel.AddChild(_titleText);
        }

        private void BuildIconSection()
        {
            _iconSection = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - IconSectionSize) * 0.5f, IconSectionY),
                Size = new Float2(IconSectionSize, IconSectionSize),
                BackgroundColor = Color.Transparent,
            };
            _panel.AddChild(_iconSection);

            InkPanel outerRing = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(IconSectionSize, IconSectionSize),
                BackgroundColor = Color.Transparent,
            };
            _iconSection.AddChild(outerRing);

            InkPanel innerRing = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 8f),
                Size = new Float2(64f, 64f),
                BackgroundColor = Color.Transparent,
            };
            _iconSection.AddChild(innerRing);

            InkPanel icon = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 12f),
                Size = new Float2(56f, 56f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _iconSection.AddChild(icon);

            InkTextBlock iconChar = new InkTextBlock(InkTextStyle.Display)
            {
                Text = "阵",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(56f, 56f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextOnBrand,
            };
            icon.AddChild(iconChar);
        }

        private void BuildTeleportName()
        {
            _teleportName = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "青城山脚",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 150f) * 0.5f, TeleportNameY),
                Size = new Float2(150f, TeleportNameHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextDefault,
            };
            _panel.AddChild(_teleportName);
        }

        private void BuildLocationInfo()
        {
            _locationInfo = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - 180f) * 0.5f, LocationY),
                Size = new Float2(180f, 28f),
                BackgroundColor = new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.08f),
            };
            _panel.AddChild(_locationInfo);

            _locationText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "青城山 · 山脚村落",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(30f, 4f),
                Size = new Float2(120f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextSecondary,
            };
            _locationInfo.AddChild(_locationText);
        }

        private void BuildMapThumbnail()
        {
            _mapThumbnail = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - MapThumbnailWidth) * 0.5f, MapThumbnailY),
                Size = new Float2(MapThumbnailWidth, MapThumbnailHeight),
                BackgroundColor = new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.6f),
            };
            _panel.AddChild(_mapThumbnail);

            InkTextBlock mapLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "地图缩略图",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((MapThumbnailWidth - 100f) * 0.5f, 50f),
                Size = new Float2(100f, 20f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = new Color(InkWashTheme.TextTertiary.R, InkWashTheme.TextTertiary.G, InkWashTheme.TextTertiary.B, 0.6f),
            };
            _mapThumbnail.AddChild(mapLabel);

            InkTextBlock mapHint = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "点击查看大地图",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((MapThumbnailWidth - 120f) * 0.5f, 100f),
                Size = new Float2(120f, 16f),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = new Color(InkWashTheme.TextSecondary.R, InkWashTheme.TextSecondary.G, InkWashTheme.TextSecondary.B, 0.7f),
            };
            _mapThumbnail.AddChild(mapHint);
        }

        private void BuildInfoText()
        {
            _infoText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "已解锁快速传送功能，可随时通过地图传送至此处。",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - InfoTextWidth) * 0.5f, InfoTextY),
                Size = new Float2(InfoTextWidth, InfoTextHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextSecondary,
                Wrapping = TextWrapping.WrapWords,
            };
            _panel.AddChild(_infoText);
        }

        private void BuildActions()
        {
            _confirmButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "确认",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - ConfirmBtnWidth) * 0.5f, ActionsY),
                Size = new Float2(ConfirmBtnWidth, ConfirmBtnHeight),
            };
            _confirmButton.ButtonClicked += OnConfirmButtonClicked;
            _panel.AddChild(_confirmButton);

            _mapButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "打开地图",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((PanelWidth - MapBtnWidth) * 0.5f, ActionsY + ConfirmBtnHeight + BtnGap),
                Size = new Float2(MapBtnWidth, MapBtnHeight),
            };
            _mapButton.ButtonClicked += OnMapButtonClicked;
            _panel.AddChild(_mapButton);
        }

        public event Action Confirmed;
        public event Action OpenMap;

        private void OnConfirmButtonClicked(Button button)
        {
            try
            {
                Confirmed?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardTeleportUnlockPage] Confirmed 触发失败: {ex.Message}");
            }
        }

        private void OnMapButtonClicked(Button button)
        {
            try
            {
                OpenMap?.Invoke();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardTeleportUnlockPage] OpenMap 触发失败: {ex.Message}");
            }
        }

        public void SetTeleportInfo(string name, string location)
        {
            if (_teleportName != null)
                _teleportName.Text = name ?? string.Empty;
            if (_locationText != null)
                _locationText.Text = location ?? string.Empty;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _rotationTime += deltaTime;
        }

        private void ApplyLayout()
        {
            if (_panel != null)
            {
                float panelHeight = ActionsY + ConfirmBtnHeight + BtnGap + MapBtnHeight + 32f;
                _panel.Size = new Float2(PanelWidth, panelHeight);
                _panel.Location = new Float2(
                    (_screenSize.X - PanelWidth) * 0.5f,
                    (_screenSize.Y - panelHeight) * 0.5f);
            }
        }

        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
            {
                var screen = FlaxEngine.Screen.Size;
                w = screen.X;
                h = screen.Y;
            }
            if (w <= 0f || h <= 0f)
            {
                w = 1920f;
                h = 1080f;
            }
            _screenSize = new Float2(w, h);
            ApplyLayout();
        }
    }
}
