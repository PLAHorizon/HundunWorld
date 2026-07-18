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
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>居中抬升面板宽度（像素）</summary>
        private const float PanelWidth = 440f;

        /// <summary>居中抬升面板高度（像素）</summary>
        private const float PanelHeight = 420f;

        /// <summary>任务完成标题 X 坐标（面板内水平居中：(440-360)/2 = 40）</summary>
        private const float TitleX = 40f;

        /// <summary>任务完成标题 Y 坐标</summary>
        private const float TitleY = 30f;

        /// <summary>任务完成标题宽度</summary>
        private const float TitleWidth = 360f;

        /// <summary>任务完成标题高度</summary>
        private const float TitleHeight = 32f;

        /// <summary>任务名 X 坐标</summary>
        private const float QuestNameX = 40f;

        /// <summary>任务名 Y 坐标</summary>
        private const float QuestNameY = 70f;

        /// <summary>任务名宽度</summary>
        private const float QuestNameWidth = 360f;

        /// <summary>任务名高度</summary>
        private const float QuestNameHeight = 28f;

        /// <summary>奖励物品列表标题 X 坐标</summary>
        private const float RewardCaptionX = 40f;

        /// <summary>奖励物品列表标题 Y 坐标</summary>
        private const float RewardCaptionY = 110f;

        /// <summary>奖励物品列表标题宽度</summary>
        private const float RewardCaptionWidth = 200f;

        /// <summary>奖励物品列表标题高度</summary>
        private const float RewardCaptionHeight = 20f;

        /// <summary>奖励物品格子尺寸（正方形，像素）</summary>
        private const float CellSize = 56f;

        /// <summary>奖励物品格子水平间距</summary>
        private const float CellSpacing = 12f;

        /// <summary>奖励物品格子起始 X 坐标</summary>
        private const float CellStartX = 40f;

        /// <summary>奖励物品格子 Y 坐标</summary>
        private const float CellY = 140f;

        /// <summary>经验数值 X 坐标</summary>
        private const float ExpX = 40f;

        /// <summary>经验数值 Y 坐标</summary>
        private const float ExpY = 220f;

        /// <summary>经验数值宽度</summary>
        private const float ExpWidth = 360f;

        /// <summary>经验数值高度</summary>
        private const float ExpHeight = 28f;

        /// <summary>铜钱数值 X 坐标</summary>
        private const float CoinsX = 40f;

        /// <summary>铜钱数值 Y 坐标</summary>
        private const float CoinsY = 260f;

        /// <summary>铜钱数值宽度</summary>
        private const float CoinsWidth = 360f;

        /// <summary>铜钱数值高度</summary>
        private const float CoinsHeight = 28f;

        /// <summary>"领取"按钮 X 坐标（面板内水平居中：(440-160)/2 = 140）</summary>
        private const float ClaimX = 140f;

        /// <summary>"领取"按钮 Y 坐标</summary>
        private const float ClaimY = 340f;

        /// <summary>"领取"按钮宽度</summary>
        private const float ClaimWidth = 160f;

        /// <summary>"领取"按钮高度</summary>
        private const float ClaimHeight = 44f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>居中抬升面板</summary>
        private InkPanelElevated _panel;

        /// <summary>任务完成标题</summary>
        private InkTextBlock _titleText;

        /// <summary>任务名</summary>
        private InkTextBlock _questNameText;

        /// <summary>奖励物品列表标题</summary>
        private InkTextBlock _rewardCaption;

        /// <summary>奖励物品格子 1（Rare，×1）</summary>
        private InkCell _cell1;

        /// <summary>奖励物品格子 2（Uncommon，×3）</summary>
        private InkCell _cell2;

        /// <summary>奖励物品格子 3（Common，×10）</summary>
        private InkCell _cell3;

        /// <summary>经验数值</summary>
        private InkTextBlock _expText;

        /// <summary>铜钱数值</summary>
        private InkTextBlock _coinsText;

        /// <summary>"领取"按钮</summary>
        private InkButton _claimButton;

        // ===================================================================
        // 屏幕尺寸缓存
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化全屏遮罩与居中面板，使用 mock 数据填充。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public RewardQuestCompletePage()
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

                // 4. 面板内子控件
                BuildTitleText();
                BuildQuestNameText();
                BuildRewardCaption();
                BuildRewardCells();
                BuildExpText();
                BuildCoinsText();
                BuildClaimButton();

                // 5. 应用初始布局（基于屏幕尺寸居中面板）
                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[RewardQuestCompletePage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // SubTask 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 11.2：任务完成标题。
        /// <see cref="InkTextBlock"/> Heading 样式，文本"任务完成"，
        /// 位置 (40, 30)，宽度 360，水平居中，字色 <see cref="InkWashTheme.TextBrand"/>。
        /// </summary>
        private void BuildTitleText()
        {
            _titleText = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "任务完成",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(TitleX, TitleY),
                Size = new Float2(TitleWidth, TitleHeight),
                HorizontalAlignment = TextAlignment.Center,
                TextColor = InkWashTheme.TextBrand,
            };
            _panel.AddChild(_titleText);
        }

        /// <summary>
        /// SubTask 11.2：任务名。
        /// <see cref="InkTextBlock"/> Subheading 样式，文本"寻访江湖名士"，
        /// 位置 (40, 70)，宽度 360，水平居中。
        /// </summary>
        private void BuildQuestNameText()
        {
            _questNameText = new InkTextBlock(InkTextStyle.Subheading)
            {
                Text = "寻访江湖名士",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(QuestNameX, QuestNameY),
                Size = new Float2(QuestNameWidth, QuestNameHeight),
                HorizontalAlignment = TextAlignment.Center,
            };
            _panel.AddChild(_questNameText);
        }

        /// <summary>
        /// SubTask 11.2：奖励物品列表标题。
        /// <see cref="InkTextBlock"/> Caption 样式，文本"奖励物品"，
        /// 位置 (40, 110)，字色 <see cref="InkWashTheme.TextTertiary"/>。
        /// </summary>
        private void BuildRewardCaption()
        {
            _rewardCaption = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "奖励物品",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(RewardCaptionX, RewardCaptionY),
                Size = new Float2(RewardCaptionWidth, RewardCaptionHeight),
                TextColor = InkWashTheme.TextTertiary,
            };
            _panel.AddChild(_rewardCaption);
        }

        /// <summary>
        /// SubTask 11.2：奖励物品格子（3 个横向排列）。
        /// 每个 <see cref="InkCell"/> 尺寸 56x56，间距 12，起始位置 (40, 140)：
        /// 格子 1：<see cref="InkWashTheme.InkQuality.Rare"/>，徽章"×1"；
        /// 格子 2：<see cref="InkWashTheme.InkQuality.Uncommon"/>，徽章"×3"；
        /// 格子 3：<see cref="InkWashTheme.InkQuality.Common"/>，徽章"×10"。
        /// </summary>
        private void BuildRewardCells()
        {
            float cell1X = CellStartX;
            float cell2X = CellStartX + CellSize + CellSpacing;
            float cell3X = CellStartX + (CellSize + CellSpacing) * 2f;

            _cell1 = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cell1X, CellY),
                Size = new Float2(CellSize, CellSize),
                Quality = InkWashTheme.InkQuality.Rare,
                Badge = "×1",
            };
            _panel.AddChild(_cell1);

            _cell2 = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cell2X, CellY),
                Size = new Float2(CellSize, CellSize),
                Quality = InkWashTheme.InkQuality.Uncommon,
                Badge = "×3",
            };
            _panel.AddChild(_cell2);

            _cell3 = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(cell3X, CellY),
                Size = new Float2(CellSize, CellSize),
                Quality = InkWashTheme.InkQuality.Common,
                Badge = "×10",
            };
            _panel.AddChild(_cell3);
        }

        /// <summary>
        /// SubTask 11.2：经验数值。
        /// <see cref="InkTextBlock"/> Number 样式，文本"+1000 经验"，
        /// 位置 (40, 220)，字色 <see cref="InkWashTheme.TextBrand"/>。
        /// </summary>
        private void BuildExpText()
        {
            _expText = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "+1000 经验",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(ExpX, ExpY),
                Size = new Float2(ExpWidth, ExpHeight),
                TextColor = InkWashTheme.TextBrand,
            };
            _panel.AddChild(_expText);
        }

        /// <summary>
        /// SubTask 11.2：铜钱数值。
        /// <see cref="InkTextBlock"/> Number 样式，文本"+500 铜钱"，
        /// 位置 (40, 260)，字色 <see cref="InkWashTheme.TextBrand"/>。
        /// </summary>
        private void BuildCoinsText()
        {
            _coinsText = new InkTextBlock(InkTextStyle.Number)
            {
                Text = "+500 铜钱",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(CoinsX, CoinsY),
                Size = new Float2(CoinsWidth, CoinsHeight),
                TextColor = InkWashTheme.TextBrand,
            };
            _panel.AddChild(_coinsText);
        }

        /// <summary>
        /// SubTask 11.2："领取"按钮。
        /// <see cref="InkButton"/> Primary Lg，位置 (140, 340)，尺寸 (160, 44)，
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
                    $"[RewardQuestCompletePage] Claimed 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 动态设置任务名、经验与铜钱。
        /// </summary>
        /// <param name="questName">任务名</param>
        /// <param name="exp">经验值（将渲染为"+N 经验"）</param>
        /// <param name="coins">铜钱数（将渲染为"+N 铜钱"）</param>
        public void SetReward(string questName, int exp, int coins)
        {
            if (_questNameText != null)
                _questNameText.Text = questName ?? string.Empty;

            if (_expText != null)
                _expText.Text = $"+{exp} 经验";

            if (_coinsText != null)
                _coinsText.Text = $"+{coins} 铜钱";
        }

        /// <summary>
        /// 动态设置奖励内容（SubTask 7.1）。
        /// 设置任务名、标题描述，并将奖励物品列表填充到奖励物品格子徽章。
        /// 最多填充 3 个格子（与页面格子数一致），超出部分忽略。
        /// </summary>
        /// <param name="name">任务名</param>
        /// <param name="description">标题描述（渲染为页面标题，如"任务完成"/"成就达成"）</param>
        /// <param name="items">奖励物品名数组（按顺序填充到 3 个奖励格子的徽章）</param>
        public void SetReward(string name, string description, string[] items)
        {
            if (_questNameText != null)
                _questNameText.Text = name ?? string.Empty;

            if (_titleText != null && !string.IsNullOrEmpty(description))
                _titleText.Text = description;

            if (items != null)
            {
                if (items.Length > 0 && _cell1 != null)
                    _cell1.Badge = items[0];
                if (items.Length > 1 && _cell2 != null)
                    _cell2.Badge = items[1];
                if (items.Length > 2 && _cell3 != null)
                    _cell3.Badge = items[2];
            }
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
    }
}
