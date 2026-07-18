using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 点穴系统页面。
    /// 左侧展示 <see cref="InkMeridianDiagram"/> 人体穴位图（8 穴位可点击）与
    /// 竖排标题"点穴"（<see cref="InkVerticalTitle"/>）；右侧为 <see cref="InkPanel"/> 穴位详情面板，
    /// 含穴位名（<see cref="InkTextBlock"/> Heading 样式）、效果文本（<see cref="InkTextBlock"/> Body 样式）
    /// 与修炼等级（<see cref="InkBar"/>）。
    /// 点击穴位时订阅 <see cref="InkMeridianDiagram.AcupointClicked"/> 事件切换详情，
    /// 并调用 <see cref="InkMeridianDiagram.SetActiveAcupoint"/> 点亮金色光晕。
    /// 通过 <see cref="NavigationRequested"/> 事件向 <see cref="InkPageRouter"/> 暴露路由跳转。
    /// </summary>
    public class AcupointPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>人体穴位图宽度（像素）</summary>
        private const float MeridianDiagramWidth = 400f;

        /// <summary>人体穴位图高度（像素）</summary>
        private const float MeridianDiagramHeight = 600f;

        /// <summary>竖排标题"点穴"控件宽度</summary>
        private const float TitleWidth = 36f;

        /// <summary>竖排标题字号</summary>
        private const float TitleFontSize = 28f;

        /// <summary>竖排标题距屏幕顶部的边距</summary>
        private const float TitleTopMargin = 32f;

        /// <summary>右侧详情面板宽度</summary>
        private const float DetailPanelWidth = 440f;

        /// <summary>右侧详情面板高度</summary>
        private const float DetailPanelHeight = 560f;

        /// <summary>详情面板内边距</summary>
        private const float PanelPadding = 24f;

        /// <summary>属性行标签宽度（部位/效果/消耗内力）</summary>
        private const float RowLabelWidth = 96f;

        /// <summary>穴位修炼等级上限</summary>
        private const int MaxLevel = 5;

        /// <summary>默认选中穴位索引（1 = 太阳穴，对应 HTML 原型选中态）</summary>
        private const int DefaultAcupointIndex = 1;

        /// <summary>角装饰 L 型线长度（像素）</summary>
        private const float CornerDecoLength = 36f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>人体穴位图组件</summary>
        private InkMeridianDiagram _meridianDiagram;

        /// <summary>竖排标题"点穴"</summary>
        private InkVerticalTitle _pageTitle;

        /// <summary>右侧详情面板容器</summary>
        private InkPanel _detailPanel;

        /// <summary>穴位名（Heading 样式）</summary>
        private InkTextBlock _acupointNameLabel;

        /// <summary>"部位"行标签</summary>
        private InkTextBlock _partLabel;

        /// <summary>部位值标签</summary>
        private Label _partValue;

        /// <summary>"效果"行标签</summary>
        private InkTextBlock _effectLabel;

        /// <summary>效果值标签（朱红色）</summary>
        private Label _effectValue;

        /// <summary>"消耗内力"行标签</summary>
        private InkTextBlock _costLabel;

        /// <summary>消耗内力数值标签（金色）</summary>
        private Label _costValue;

        /// <summary>"穴义"小节标题</summary>
        private InkTextBlock _effectCaption;

        /// <summary>穴义描述正文（Body 样式）</summary>
        private InkTextBlock _effectText;

        /// <summary>"修炼等级"小节标题</summary>
        private InkTextBlock _levelCaption;

        /// <summary>修炼等级数值标签（如 "3/5"）</summary>
        private Label _levelValue;

        /// <summary>修炼等级进度条</summary>
        private InkBar _levelBar;

        /// <summary>返回按钮（触发 <see cref="NavigationRequested"/>）</summary>
        private InkButton _backButton;

        // ===================================================================
        // mock 数据（顺序与 InkMeridianDiagram.AcupointNames 一致）
        // =======================================================================

        /// <summary>8 穴位部位（mock）</summary>
        private string[] _acupointParts =
        {
            "头顶", "头部", "后头", "胸部", "腹部", "手部", "下腹", "足底"
        };

        /// <summary>8 穴位效果简称（mock）</summary>
        private string[] _acupointEffectNames =
        {
            "凝神", "眩晕", "驱风", "调气", "固本", "镇痛", "培元", "引火"
        };

        /// <summary>8 穴位穴义描述文本（mock）</summary>
        private string[] _acupointEffects =
        {
            "点此穴可凝神定志，使我方心神清明，抵御惑乱之术。",
            "点此穴可使敌方眩晕三秒，失去战斗能力。若运功深厚，可封其经脉，令其无法调息内力。",
            "点此穴可驱风散邪，解表发汗，疗头风之疾。",
            "点此穴可宽胸理气，调畅气机，使内力运转顺畅。",
            "点此穴可温补脾肾，固本培元，回复本源之气。",
            "点此穴可镇痛止痉，镇定心神，通经活络。",
            "点此穴可培元固本，补益下焦，增精益血。",
            "点此穴可引火归元，上病下治，平息虚火上炎。"
        };

        /// <summary>8 穴位修炼进度（mock，0-1）</summary>
        private float[] _acupointProgress =
        {
            0.4f, 0.6f, 0.2f, 0.8f, 0.4f, 0.6f, 0.2f, 0.4f
        };

        /// <summary>8 穴位施展消耗内力（mock）</summary>
        private int[] _acupointCosts =
        {
            15, 20, 12, 25, 18, 15, 22, 10
        };

        // ===================================================================
        // 屏幕尺寸缓存与状态
        // =======================================================================

        /// <summary>当前屏幕尺寸缓存，用于布局计算</summary>
        private Float2 _screenSize;

        /// <summary>当前选中穴位索引</summary>
        private int _currentIndex = DefaultAcupointIndex;

        // ===================================================================
        // 公共 API：事件
        // =======================================================================

        /// <summary>
        /// 导航请求事件。
        /// 由返回按钮触发，参数为目标路由标识（如 <c>"back-hud"</c>）。
        /// 由 <see cref="InkPageRouter"/> 订阅以执行页面跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化左侧穴位图与右侧详情面板，填充 mock 数据。
        /// 构造时读取 <see cref="FlaxEngine.Screen.Size"/> 计算布局，
        /// 屏幕尺寸未就绪时使用 1920x1080 兜底。
        /// </summary>
        public AcupointPage()
        {
            // 1. 读取屏幕尺寸
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
                _screenSize = new Float2(1920f, 1080f);

            // 2. 外壳本身：全屏拉伸 + 透明背景 + 不裁剪子控件
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                BuildLeftArea();
                BuildDetailPanel();
                ApplyLayout();
                SelectAcupoint(DefaultAcupointIndex);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AcupointPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 构造方法
        // =======================================================================

        /// <summary>
        /// SubTask 14.2：构建左侧区域。
        /// 包含 <see cref="InkVerticalTitle"/> 竖排标题"点穴"与
        /// <see cref="InkMeridianDiagram"/> 人体穴位图（400x600）。
        /// 订阅 <see cref="InkMeridianDiagram.AcupointClicked"/> 事件。
        /// </summary>
        private void BuildLeftArea()
        {
            // 竖排标题"点穴"
            _pageTitle = new InkVerticalTitle
            {
                Text = "点穴",
                FontSize = TitleFontSize,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(TitleWidth, TitleFontSize * 3f),
            };
            AddChild(_pageTitle);

            // 人体穴位图
            _meridianDiagram = new InkMeridianDiagram
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MeridianDiagramWidth, MeridianDiagramHeight),
            };
            _meridianDiagram.AcupointClicked += OnAcupointClicked;
            AddChild(_meridianDiagram);
        }

        /// <summary>
        /// SubTask 14.3：构建右侧穴位详情面板。
        /// <see cref="InkPanel"/> 内含穴位名（<see cref="InkTextStyle.Heading"/>）、
        /// 部位/效果/消耗内力属性行、穴义描述（<see cref="InkTextStyle.Body"/>）、
        /// 修炼等级（<see cref="InkBar"/>）与返回按钮。
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(DetailPanelWidth, DetailPanelHeight),
            };

            float contentWidth = DetailPanelWidth - PanelPadding * 2f;
            float valueX = PanelPadding + RowLabelWidth;
            float valueWidth = contentWidth - RowLabelWidth;

            // 穴位名（Heading）
            _acupointNameLabel = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = string.Empty,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, 24f),
                Size = new Float2(contentWidth, 40f),
            };
            _detailPanel.AddChild(_acupointNameLabel);

            // 部位行
            _partLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "部位",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, 88f),
                Size = new Float2(RowLabelWidth, 22f),
            };
            _detailPanel.AddChild(_partLabel);

            _partValue = new Label
            {
                Text = string.Empty,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
                TextColor = InkWashTheme.PaperBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueX, 88f),
                Size = new Float2(valueWidth, 22f),
            };
            _detailPanel.AddChild(_partValue);

            // 效果行
            _effectLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "效果",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, 116f),
                Size = new Float2(RowLabelWidth, 22f),
            };
            _detailPanel.AddChild(_effectLabel);

            _effectValue = new Label
            {
                Text = string.Empty,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 15f),
                TextColor = InkWashTheme.VermilionBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueX, 116f),
                Size = new Float2(valueWidth, 22f),
            };
            _detailPanel.AddChild(_effectValue);

            // 消耗内力行
            _costLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "消耗内力",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, 144f),
                Size = new Float2(RowLabelWidth, 22f),
            };
            _detailPanel.AddChild(_costLabel);

            _costValue = new Label
            {
                Text = string.Empty,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 18f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueX, 144f),
                Size = new Float2(valueWidth, 22f),
            };
            _detailPanel.AddChild(_costValue);

            // 穴义小节
            _effectCaption = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "穴义",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, 184f),
                Size = new Float2(contentWidth, 20f),
            };
            _detailPanel.AddChild(_effectCaption);

            _effectText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = string.Empty,
                Wrapping = TextWrapping.WrapWords,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, 208f),
                Size = new Float2(contentWidth, 120f),
            };
            _detailPanel.AddChild(_effectText);

            // 修炼等级小节
            _levelCaption = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "修炼等级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, 348f),
                Size = new Float2(200f, 20f),
            };
            _detailPanel.AddChild(_levelCaption);

            _levelValue = new Label
            {
                Text = string.Empty,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding + 200f, 348f),
                Size = new Float2(contentWidth - 200f, 20f),
            };
            _detailPanel.AddChild(_levelValue);

            _levelBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, 374f),
                Size = new Float2(contentWidth, 12f),
            };
            _detailPanel.AddChild(_levelBar);

            // 返回按钮
            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "返回",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, DetailPanelHeight - 64f),
                Size = new Float2(contentWidth, 40f),
            };
            _backButton.ButtonClicked += OnBackButtonClicked;
            _detailPanel.AddChild(_backButton);

            AddChild(_detailPanel);
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        /// <summary>
        /// 根据当前 <see cref="_screenSize"/> 重新计算所有子控件的位置。
        /// 由构造函数与 <see cref="RefreshLayout"/> 调用。
        /// </summary>
        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;
            float leftAreaWidth = sw * 0.58f;

            // 竖排标题"点穴"：左侧区域顶部居中
            if (_pageTitle != null)
            {
                _pageTitle.Location = new Float2(
                    leftAreaWidth * 0.5f - TitleWidth * 0.5f,
                    TitleTopMargin);
            }

            // 人体穴位图：左侧区域居中
            if (_meridianDiagram != null)
            {
                _meridianDiagram.Location = new Float2(
                    (leftAreaWidth - MeridianDiagramWidth) * 0.5f,
                    (sh - MeridianDiagramHeight) * 0.5f);
            }

            // 右侧详情面板：右侧区域居中
            if (_detailPanel != null)
            {
                float rightAreaWidth = sw - leftAreaWidth;
                _detailPanel.Location = new Float2(
                    leftAreaWidth + (rightAreaWidth - DetailPanelWidth) * 0.5f,
                    (sh - DetailPanelHeight) * 0.5f);
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

        // ===================================================================
        // 穴位选择与详情更新
        // =======================================================================

        /// <summary>
        /// 选中指定穴位：点亮金色光晕并刷新右侧详情面板。
        /// </summary>
        /// <param name="index">穴位索引 0-7</param>
        private void SelectAcupoint(int index)
        {
            if (index < 0 || index >= InkMeridianDiagram.AcupointNames.Length)
                return;

            _currentIndex = index;
            if (_meridianDiagram != null)
                _meridianDiagram.SetActiveAcupoint(index);
            UpdateDetailPanel(index);
        }

        /// <summary>
        /// 根据 <paramref name="index"/> 刷新右侧详情面板的全部字段。
        /// 穴位名取自 <see cref="InkMeridianDiagram.AcupointNames"/>，
        /// 其余取自 mock 数据数组。
        /// </summary>
        /// <param name="index">穴位索引 0-7</param>
        private void UpdateDetailPanel(int index)
        {
            if (index < 0 || index >= InkMeridianDiagram.AcupointNames.Length)
                return;

            if (_acupointNameLabel != null)
                _acupointNameLabel.Text = InkMeridianDiagram.AcupointNames[index] + "穴";
            if (_partValue != null)
                _partValue.Text = _acupointParts[index];
            if (_effectValue != null)
                _effectValue.Text = _acupointEffectNames[index];
            if (_costValue != null)
                _costValue.Text = _acupointCosts[index].ToString();
            if (_effectText != null)
                _effectText.Text = _acupointEffects[index];

            float progress = _acupointProgress[index];
            if (_levelBar != null)
                _levelBar.Value = progress;
            if (_levelValue != null)
            {
                int lvl = (int)(progress * MaxLevel);
                if (lvl < 0)
                    lvl = 0;
                if (lvl > MaxLevel)
                    lvl = MaxLevel;
                _levelValue.Text = lvl + "/" + MaxLevel;
            }
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 穴位点击事件处理：切换当前选中穴位。
        /// </summary>
        /// <param name="index">被点击的穴位索引 0-7</param>
        private void OnAcupointClicked(int index)
        {
            SelectAcupoint(index);
        }

        /// <summary>
        /// 返回按钮点击处理：触发 <see cref="NavigationRequested"/>("back-hud")。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnBackButtonClicked(Button button)
        {
            try
            {
                NavigationRequested?.Invoke("back-hud");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AcupointPage] NavigationRequested(back-hud) 触发失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 生命周期
        // =======================================================================

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();

            if (Width <= 0f || Height <= 0f)
                return;

            // 角装饰（对应 HTML 原型四角 L 型金线）
            DrawCornerDecoration(0f, 0f, 1f, 1f);
            DrawCornerDecoration(Width, Height, -1f, -1f);
        }

        /// <summary>
        /// 绘制一个 L 型角装饰。
        /// </summary>
        /// <param name="x">角点 X 坐标</param>
        /// <param name="y">角点 Y 坐标</param>
        /// <param name="dirX">水平方向（1 = 向右，-1 = 向左）</param>
        /// <param name="dirY">垂直方向（1 = 向下，-1 = 向上）</param>
        private void DrawCornerDecoration(float x, float y, float dirX, float dirY)
        {
            var color = new Color(
                InkWashTheme.GoldPrimary.R,
                InkWashTheme.GoldPrimary.G,
                InkWashTheme.GoldPrimary.B,
                0.4f);
            Render2D.DrawLine(
                new Float2(x, y),
                new Float2(x + dirX * CornerDecoLength, y),
                color, 1f);
            Render2D.DrawLine(
                new Float2(x, y),
                new Float2(x, y + dirY * CornerDecoLength),
                color, 1f);
        }
    }
}
