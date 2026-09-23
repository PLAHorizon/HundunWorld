using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Combat
{
    /// <summary>
    /// 点穴系统页面（对齐设计稿 <c>acupoint.html</c>）。
    /// <para>
    /// 左侧：<see cref="InkVerticalTitle"/> 竖排标题"点穴" + <see cref="InkMeridianDiagram"/> 人体穴位图
    /// （8 个可点击穴位、穴位名标签、选中朱红扩散环）+ 左下角经脉说明（金色横线 + "督脉 · 任脉" + "共标记 8 处穴位"）。
    /// 右侧：满高宣纸细节栏（<see cref="InkPanel"/>，宽 40%）：竖排穴位名 + 朱红竖线、
    /// "要穴" 朱红标签 + "已选中" 状态点、部位/效果/消耗内力三行、穴义正文、
    /// 穴位等级（<see cref="InkBar"/> + 等级点阵 + "下一级：眩晕4秒"）、底部"施展点穴 / 返回"双按钮。
    /// </para>
    /// <para>
    /// 点击穴位订阅 <see cref="InkMeridianDiagram.AcupointClicked"/> 切换详情，
    /// 并调用 <see cref="InkMeridianDiagram.SetActiveAcupoint"/> 点亮朱红扩散环。
    /// 通过 <see cref="NavigationRequested"/> 事件向 <see cref="InkPageRouter"/> 暴露路由跳转。
    /// </para>
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

        /// <summary>穴位图所在左区宽度占屏幕宽度比例（设计稿右侧详情栏固定 40%）</summary>
        private const float LeftAreaWidthRatio = 0.60f;

        /// <summary>竖排标题"点穴"控件宽度</summary>
        private const float TitleWidth = 36f;

        /// <summary>竖排标题字号</summary>
        private const float TitleFontSize = 28f;

        /// <summary>竖排标题距屏幕顶部的边距</summary>
        private const float TitleTopMargin = 32f;

        /// <summary>经脉说明距左区左下角的边距（设计稿 var(--ink-space-5) = 24px）</summary>
        private const float MeridianHintMargin = 24f;

        /// <summary>经脉说明 — 金色横线宽度（设计稿 20px）</summary>
        private const float HintLineWidth = 20f;

        /// <summary>经脉说明 — 横线与文字间距（设计稿 gap: var(--ink-space-2) = 8px）</summary>
        private const float HintLineGap = 8f;

        /// <summary>右侧详情栏宽度比例（设计稿 lg:w-[40%]）</summary>
        private const float DetailPanelWidthRatio = 0.40f;

        /// <summary>详情栏内边距（设计稿 padding: var(--ink-space-6) = 32px）</summary>
        private const float PanelPadding = 32f;

        /// <summary>详情栏块间距（设计稿 gap: var(--ink-space-6) = 32px，块内 gap 16px）</summary>
        private const float SectionGap = 16f;

        /// <summary>详情栏竖排穴位名列宽度</summary>
        private const float AcupointTitleColumnWidth = 44f;

        /// <summary>详情栏竖排穴位名字号（设计稿 36px）</summary>
        private const float AcupointTitleFontSize = 36f;

        /// <summary>竖排穴位名与内容列间距（设计稿 gap: var(--ink-space-6) = 32px）</summary>
        private const float TitleColumnGap = 32f;

        /// <summary>竖排穴位名下方朱红竖线高度（设计稿 48px）</summary>
        private const float TitleAccentHeight = 48f;

        /// <summary>属性行行高（部位/效果/消耗内力）</summary>
        private const float PropertyRowHeight = 30f;

        /// <summary>属性行间距（设计稿 gap: var(--ink-space-2) = 8px）</summary>
        private const float PropertyRowGap = 8f;

        /// <summary>属性行标签宽度（部位/效果/消耗内力）</summary>
        private const float RowLabelWidth = 96f;

        /// <summary>穴义正文高度（13px 字号 × 1.75 行高 × 3 行）</summary>
        private const float EffectTextHeight = 72f;

        /// <summary>穴位修炼等级上限</summary>
        private const int MaxLevel = 5;

        /// <summary>默认选中穴位索引（1 = 太阳穴，对应设计稿选中态）</summary>
        private const int DefaultAcupointIndex = 1;

        /// <summary>底部按钮行高度（设计稿 ink-btn 大号按钮）</summary>
        private const float ButtonRowHeight = 40f;

        /// <summary>"返回"按钮宽度（"施展点穴"按钮占据剩余宽度）</summary>
        private const float BackButtonWidth = 120f;

        /// <summary>底部按钮间距（设计稿 gap: var(--ink-space-4) = 16px）</summary>
        private const float ButtonGap = 16f;

        /// <summary>"要穴"标签尺寸</summary>
        private const float TagWidth = 56f;

        /// <summary>"要穴"标签高度</summary>
        private const float TagHeight = 22f;

        /// <summary>角装饰 L 型线长度（像素）</summary>
        private const float CornerDecoLength = 36f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>人体穴位图组件</summary>
        private InkMeridianDiagram _meridianDiagram;

        /// <summary>竖排标题"点穴"</summary>
        private InkVerticalTitle _pageTitle;

        /// <summary>经脉说明容器（左下角）</summary>
        private ContainerControl _meridianHint;

        /// <summary>经脉说明 — 金色横线</summary>
        private Panel _hintLine;

        /// <summary>经脉说明 — "督脉 · 任脉"</summary>
        private Label _hintMeridianLabel;

        /// <summary>经脉说明 — "共标记 8 处穴位"</summary>
        private Label _hintCountLabel;

        /// <summary>右侧详情栏容器（满高，40% 宽）</summary>
        private InkPanel _detailPanel;

        /// <summary>穴位名竖排标题（设计稿 .ink-vertical-title，36px）</summary>
        private InkVerticalTitle _acupointTitle;

        /// <summary>穴位名下方朱红渐变竖线</summary>
        private Panel _titleAccentLine;

        /// <summary>"要穴" 朱红标签</summary>
        private InkTag _tierTag;

        /// <summary>"已选中" 状态指示点</summary>
        private AcupointSelectedDot _selectedDot;

        /// <summary>"已选中" 状态文字</summary>
        private Label _selectedLabel;

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

        /// <summary>"穴位等级"小节标题</summary>
        private InkTextBlock _levelCaption;

        /// <summary>穴位等级数值标签（如 "3 / 5"）</summary>
        private Label _levelValue;

        /// <summary>穴位等级进度条</summary>
        private InkBar _levelBar;

        /// <summary>穴位等级点阵（5 颗，已达成者点亮）</summary>
        private AcupointPipRow _levelPips;

        /// <summary>"下一级：眩晕4秒" 提示标签</summary>
        private Label _nextLevelLabel;

        /// <summary>返回按钮（触发 <see cref="NavigationRequested"/>）</summary>
        private InkButton _backButton;

        /// <summary>施展点穴按钮（设计稿 .link-dialogue-confirm，触发对话确认）</summary>
        private InkButton _castButton;

        /// <summary>段落间的四条水墨分割线</summary>
        private InkDivider _dividerTop;
        private InkDivider _dividerProperties;
        private InkDivider _dividerEffect;
        private InkDivider _dividerLevel;
        private InkDivider _dividerActions;

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

        /// <summary>8 穴位"下一级"效果提示（mock，对应设计稿"下一级：眩晕4秒"）</summary>
        private string[] _acupointNextLevelHints =
        {
            "下一级：凝神4秒",
            "下一级：眩晕4秒",
            "下一级：驱风4秒",
            "下一级：调气5秒",
            "下一级：固本4秒",
            "下一级：镇痛5秒",
            "下一级：培元4秒",
            "下一级：引火3秒"
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
        /// 构造函数：初始化左侧穴位图 + 左下经脉说明 + 右侧满高穴位详情栏，填充 mock 数据。
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
        /// 构建左侧区域。
        /// 包含 <see cref="InkVerticalTitle"/> 竖排标题"点穴"、
        /// <see cref="InkMeridianDiagram"/> 人体穴位图（400x600）与
        /// 左下角经脉说明（金色横线 + "督脉 · 任脉" + "共标记 8 处穴位"）。
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

            // 人体穴位图（设计稿底部有经脉说明，穴位图整体略上移，避免与说明重叠）
            _meridianDiagram = new InkMeridianDiagram
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(MeridianDiagramWidth, MeridianDiagramHeight),
            };
            _meridianDiagram.AcupointClicked += OnAcupointClicked;
            AddChild(_meridianDiagram);

            // 左下角经脉说明
            _meridianHint = new ContainerControl
            {
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
                AutoFocus = false,
                Size = new Float2(200f, 44f),
            };

            // 金色渐变横线（设计稿 20px 金线，右端渐隐）
            _hintLine = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 10f),
                Size = new Float2(HintLineWidth, 1f),
                BackgroundColor = InkWashTheme.GoldPrimary,
            };
            _meridianHint.AddChild(_hintLine);

            // "督脉 · 任脉"（书法体 13px 亮金）
            _hintMeridianLabel = new Label
            {
                Text = "督脉 · 任脉",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(HintLineWidth + HintLineGap, 0f),
                Size = new Float2(160f, 20f),
            };
            _meridianHint.AddChild(_hintMeridianLabel);

            // "共标记 8 处穴位"（正文 11px 宣纸做旧色）
            _hintCountLabel = new Label
            {
                Text = "共标记 8 处穴位",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 22f),
                Size = new Float2(160f, 16f),
            };
            _meridianHint.AddChild(_hintCountLabel);

            AddChild(_meridianHint);
        }

        /// <summary>
        /// 构建右侧满高穴位详情栏（设计稿右上 section，宽 40%）。
        /// 结构自上而下：竖排穴位名 + 朱红竖线 → "要穴" 标签 + "已选中" →
        /// 部位/效果/消耗内力 → 穴义 → 穴位等级（进度条 + 点阵 + 下一级提示）→ 施展点穴 / 返回。
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                // 设计稿详情栏为满高贴边栏目（无圆角、仅左侧金线），
                // 此处沿用 InkPanel 但取消圆角，既贴近设计又不新增并行面板体系。
                Radius = 0f,
                Size = new Float2(_screenSize.X * DetailPanelWidthRatio, _screenSize.Y),
            };

            // 内容列起点：内边距 + 竖排名列 + 列间距
            float contentX = PanelPadding + AcupointTitleColumnWidth + TitleColumnGap;
            float contentWidth = _detailPanel.Width - contentX - PanelPadding;
            float valueX = contentX + RowLabelWidth;
            float valueWidth = contentWidth - RowLabelWidth;

            // 1. 竖排穴位名（设计稿 .ink-vertical-title，36px）
            _acupointTitle = new InkVerticalTitle
            {
                Text = string.Empty,
                FontSize = AcupointTitleFontSize,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding, PanelPadding + 8f),
                Size = new Float2(AcupointTitleColumnWidth, AcupointTitleFontSize * 1.2f * 4f),
            };
            _detailPanel.AddChild(_acupointTitle);

            // 2. 穴位名下方朱红竖线（设计稿 1px × 48px 渐变）
            _titleAccentLine = new Panel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelPadding + AcupointTitleColumnWidth * 0.5f,
                    PanelPadding + 8f + AcupointTitleFontSize * 1.2f * 4f + SectionGap),
                Size = new Float2(1f, TitleAccentHeight),
                BackgroundColor = InkWashTheme.VermilionPrimary,
            };
            _detailPanel.AddChild(_titleAccentLine);

            float y = PanelPadding;

            // 3. 顶行："要穴" 朱红标签 + "已选中" 状态
            _tierTag = new InkTag
            {
                Text = "要穴",
                TagVariant = InkTagVariant.Vermilion,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 11f),
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(TagWidth, TagHeight),
            };
            _detailPanel.AddChild(_tierTag);

            _selectedDot = new AcupointSelectedDot
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueX + valueWidth - 48f, y + (TagHeight - 6f) * 0.5f),
                Size = new Float2(6f, 6f),
            };
            _detailPanel.AddChild(_selectedDot);

            _selectedLabel = new Label
            {
                Text = "已选中",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueX, y),
                Size = new Float2(valueWidth - 56f, TagHeight),
            };
            _detailPanel.AddChild(_selectedLabel);

            y += TagHeight + SectionGap;

            // 4. 分割线
            _dividerTop = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(contentWidth, 1f),
            };
            _detailPanel.AddChild(_dividerTop);
            y += 1f + SectionGap;

            // 5. 部位 / 效果 / 消耗内力
            _partLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "部位",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(RowLabelWidth, PropertyRowHeight),
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
                Location = new Float2(valueX, y),
                Size = new Float2(valueWidth, PropertyRowHeight),
            };
            _detailPanel.AddChild(_partValue);

            y += PropertyRowHeight + PropertyRowGap;

            _effectLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "效果",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(RowLabelWidth, PropertyRowHeight),
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
                Location = new Float2(valueX, y),
                Size = new Float2(valueWidth, PropertyRowHeight),
            };
            _detailPanel.AddChild(_effectValue);

            y += PropertyRowHeight + PropertyRowGap;

            _costLabel = new InkTextBlock(InkTextStyle.Body)
            {
                Text = "消耗内力",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(RowLabelWidth, PropertyRowHeight),
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
                Location = new Float2(valueX, y),
                Size = new Float2(valueWidth, PropertyRowHeight),
            };
            _detailPanel.AddChild(_costValue);

            y += PropertyRowHeight + SectionGap;

            // 6. 分割线
            _dividerProperties = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(contentWidth, 1f),
            };
            _detailPanel.AddChild(_dividerProperties);
            y += 1f + SectionGap;

            // 7. 穴义
            _effectCaption = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "穴义",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(contentWidth, 18f),
            };
            _detailPanel.AddChild(_effectCaption);
            y += 18f + 8f;

            _effectText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = string.Empty,
                Wrapping = TextWrapping.WrapWords,
                VerticalAlignment = TextAlignment.Near,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(contentWidth, EffectTextHeight),
            };
            _detailPanel.AddChild(_effectText);

            y += EffectTextHeight + SectionGap;

            // 8. 分割线
            _dividerEffect = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(contentWidth, 1f),
            };
            _detailPanel.AddChild(_dividerEffect);
            y += 1f + SectionGap;

            // 9. 穴位等级
            _levelCaption = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "穴位等级",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(200f, 18f),
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
                Location = new Float2(valueX, y),
                Size = new Float2(valueWidth, 18f),
            };
            _detailPanel.AddChild(_levelValue);

            y += 18f + 8f;

            _levelBar = new InkBar
            {
                FillVariant = InkBarFillVariant.Gold,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(contentWidth, 8f),
            };
            _detailPanel.AddChild(_levelBar);

            y += 8f + 8f;

            // 等级点阵 + 下一级提示
            _levelPips = new AcupointPipRow
            {
                Total = MaxLevel,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
            };
            _detailPanel.AddChild(_levelPips);

            _nextLevelLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperDark,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(valueX, y - 4f),
                Size = new Float2(valueWidth, _levelPips.Height + 8f),
            };
            _detailPanel.AddChild(_nextLevelLabel);

            y += _levelPips.Height + SectionGap;

            // 10. 分割线（按钮行上方）
            _dividerLevel = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, y),
                Size = new Float2(contentWidth, 1f),
            };
            _detailPanel.AddChild(_dividerLevel);

            // 11. 底部按钮行：设计稿顺序为 [施展点穴（flex-1）][返回（Ghost）]
            float buttonY = _screenSize.Y - PanelPadding - ButtonRowHeight;
            _castButton = new InkButton
            {
                Variant = InkButtonVariant.Vermilion,
                ButtonSize = InkButtonSize.Lg,
                Text = "施展点穴",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, buttonY),
                Size = new Float2(contentWidth - BackButtonWidth - ButtonGap, ButtonRowHeight),
            };
            _castButton.ButtonClicked += OnCastButtonClicked;
            _detailPanel.AddChild(_castButton);

            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Lg,
                Text = "返回",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX + contentWidth - BackButtonWidth, buttonY),
                Size = new Float2(BackButtonWidth, ButtonRowHeight),
            };
            _backButton.ButtonClicked += OnBackButtonClicked;
            _detailPanel.AddChild(_backButton);

            _dividerActions = new InkDivider
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(contentX, buttonY - SectionGap),
                Size = new Float2(contentWidth, 1f),
            };
            _detailPanel.AddChild(_dividerActions);

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
            float leftAreaWidth = sw * LeftAreaWidthRatio;

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

            // 经脉说明：左区左下角（设计稿 bottom/left = var(--ink-space-5) = 24px）
            if (_meridianHint != null)
            {
                _meridianHint.Location = new Float2(
                    MeridianHintMargin,
                    sh - MeridianHintMargin - _meridianHint.Height);
            }

            // 右侧详情栏：满高贴右边（宽 40%）
            if (_detailPanel != null)
            {
                _detailPanel.Size = new Float2(sw * DetailPanelWidthRatio, sh);
                _detailPanel.Location = new Float2(sw * LeftAreaWidthRatio, 0f);

                // 底部按钮行随面板高度重新贴底
                float buttonY = sh - PanelPadding - ButtonRowHeight;
                if (_castButton != null)
                    _castButton.Location = new Float2(_castButton.Location.X, buttonY);
                if (_backButton != null)
                    _backButton.Location = new Float2(_backButton.Location.X, buttonY);
                if (_dividerActions != null)
                    _dividerActions.Location = new Float2(_dividerActions.Location.X, buttonY - SectionGap);
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
        /// 选中指定穴位：点亮朱红扩散环并刷新右侧详情栏。
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
        /// 根据 <paramref name="index"/> 刷新右侧详情栏的全部字段。
        /// 穴位名取自 <see cref="InkMeridianDiagram.AcupointNames"/>，
        /// 其余取自 mock 数据数组。
        /// </summary>
        /// <param name="index">穴位索引 0-7</param>
        private void UpdateDetailPanel(int index)
        {
            if (index < 0 || index >= InkMeridianDiagram.AcupointNames.Length)
                return;

            if (_acupointTitle != null)
                _acupointTitle.Text = InkMeridianDiagram.AcupointNames[index] + "穴";
            if (_partValue != null)
                _partValue.Text = _acupointParts[index];
            if (_effectValue != null)
                _effectValue.Text = _acupointEffectNames[index];
            if (_costValue != null)
                _costValue.Text = _acupointCosts[index].ToString();
            if (_effectText != null)
                _effectText.Text = _acupointEffects[index];
            if (_nextLevelLabel != null)
                _nextLevelLabel.Text = _acupointNextLevelHints[index];

            float progress = _acupointProgress[index];
            int lvl = (int)(progress * MaxLevel);
            if (lvl < 0)
                lvl = 0;
            if (lvl > MaxLevel)
                lvl = MaxLevel;

            if (_levelBar != null)
                _levelBar.Value = progress;
            if (_levelValue != null)
                _levelValue.Text = lvl + " / " + MaxLevel;
            if (_levelPips != null)
                _levelPips.ActiveCount = lvl;
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

        /// <summary>
        /// 施展点穴按钮点击处理：设计稿 .link-dialogue-confirm → 导航到 NPC 对话确认页。
        /// </summary>
        /// <param name="button">触发事件的按钮（未使用）</param>
        private void OnCastButtonClicked(Button button)
        {
            try
            {
                NavigationRequested?.Invoke(InkPageDomIds.DialogueConfirm);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[AcupointPage] NavigationRequested(dialogue-confirm) 触发失败: {ex.Message}");
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

        // ===================================================================
        // 内嵌小控件
        // =======================================================================

        /// <summary>
        /// 穴位等级点阵。
        /// 对应设计稿 <c>.acupoint-pip</c> / <c>.acupoint-pip-active</c>：
        /// 8px 圆点、间距 6px；前 <see cref="ActiveCount"/> 颗为金色实心（带金晕），
        /// 其余为金 12% 底 + 金 25% 描边。
        /// </summary>
        private class AcupointPipRow : ContainerControl
        {
            /// <summary>圆点直径（设计稿 8px）</summary>
            private const float PipSize = 8f;

            /// <summary>圆点间距（设计稿 gap: 6px）</summary>
            private const float PipGap = 6f;

            /// <summary>已达成等级数</summary>
            private int _activeCount;

            /// <summary>等级总点数</summary>
            private int _total = 5;

            /// <summary>已达成等级数（点亮前 N 颗）。</summary>
            public int ActiveCount
            {
                get => _activeCount;
                set => _activeCount = value;
            }

            /// <summary>等级总点数。</summary>
            public int Total
            {
                get => _total;
                set
                {
                    _total = Mathf.Max(1, value);
                    UpdateSize();
                }
            }

            /// <summary>构造函数：默认 5 点、高度 12px、透明背景。</summary>
            public AcupointPipRow()
            {
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
                UpdateSize();
            }

            /// <summary>按点数计算控件宽度与高度。</summary>
            private void UpdateSize()
            {
                Width = _total * PipSize + (_total - 1) * PipGap;
                Height = PipSize + 4f;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                float radius = PipSize * 0.5f;
                float centerY = Height * 0.5f;

                for (int i = 0; i < _total; i++)
                {
                    var center = new Float2(radius + i * (PipSize + PipGap), centerY);

                    if (i < _activeCount)
                    {
                        // 已达成：金色实心 + 金晕 + 亮金描边
                        InkRenderHelper.FillRadialGradient(
                            center, radius + 3f,
                            new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.4f),
                            Color.Transparent, 6);
                        InkRenderHelper.FillCircle(center, radius, InkWashTheme.GoldPrimary);
                        InkRenderHelper.DrawCircle(center, radius, InkWashTheme.GoldBright, 1f);
                    }
                    else
                    {
                        // 未达成：金 12% 底 + 金 25% 描边
                        InkRenderHelper.FillCircle(center, radius,
                            new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.12f));
                        InkRenderHelper.DrawCircle(center, radius,
                            new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G, InkWashTheme.GoldPrimary.B, 0.25f), 1f);
                    }
                }
            }
        }

        /// <summary>
        /// "已选中" 状态指示点。
        /// 对应设计稿顶行内联样式：6px 朱红实心点 + 朱红柔光。
        /// </summary>
        private class AcupointSelectedDot : Control
        {
            /// <summary>圆点半径（设计稿直径 6px）</summary>
            private const float DotRadius = 3f;

            /// <summary>构造函数：6x6、透明背景。</summary>
            public AcupointSelectedDot()
            {
                Size = new Float2(6f, 6f);
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var center = new Float2(Width * 0.5f, Height * 0.5f);

                // 朱红柔光（设计稿 box-shadow: 0 0 6px rgba(192,57,43,0.5)）
                InkRenderHelper.FillRadialGradient(
                    center, DotRadius + 3f,
                    new Color(InkWashTheme.VermilionBright.R, InkWashTheme.VermilionBright.G, InkWashTheme.VermilionBright.B, 0.5f),
                    Color.Transparent, 6);
                InkRenderHelper.FillCircle(center, DotRadius, InkWashTheme.VermilionBright);
            }
        }
    }
}
