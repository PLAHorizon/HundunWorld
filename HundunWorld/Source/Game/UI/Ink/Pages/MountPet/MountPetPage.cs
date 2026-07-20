using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.MountPet
{
    /// <summary>
    /// 坐骑灵兽页面 — 对应 mount-pet.html 设计原型（dom-id: nav-mount-pet）。
    /// <para>
    /// 采用"顶部 Tab/标题栏 + 左侧坐骑名册 + 中央预览/属性/技能 + 右侧操作面板 + 底部导航"五区结构，
    /// 居中显示 1400x900 主面板。通过 <see cref="NavigationRequested"/> 事件向路由器
    /// 暴露导航跳转：返回沉浸模式（combat-hud）。
    /// </para>
    /// <list type="bullet">
    ///   <item>顶部：坐骑/灵兽 Tab + "坐骑灵兽"居中标题 + 右上返回按钮</item>
    ///   <item>左侧（300px）：坐骑名册标题 + 6 张坐骑卡片（品质色图标 + 名称 + 品质徽章 + 等级 + 速度）</item>
    ///   <item>中央：3D 预览舞台（280px）+ 名称 + 五行/坐骑标签 + 3 张属性卡（速度/耐力/跳跃力）+ 3 个技能槽 + 灵兽传说</item>
    ///   <item>右侧（340px）：出战状态 + 喂养/驯养 + 技能研习 + 外观幻化 + 移速加成预览</item>
    ///   <item>底部：返回沉浸模式按钮</item>
    /// </list>
    /// </summary>
    public class MountPetPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>主面板尺寸（居中显示，对应 .mp-panel 1400×900）</summary>
        private static readonly Float2 MainPanelSize = new Float2(1400f, 900f);

        /// <summary>顶部标题栏高度</summary>
        private const float TopBarHeight = 56f;

        /// <summary>底部导航栏高度</summary>
        private const float BottomBarHeight = 56f;

        /// <summary>面板内边距</summary>
        private const float Padding = 12f;

        /// <summary>子面板间距</summary>
        private const float PanelGap = 10f;

        /// <summary>左侧坐骑名册面板宽度</summary>
        private const float LeftListWidth = 300f;

        /// <summary>右侧操作面板宽度</summary>
        private const float RightOpsWidth = 340f;

        /// <summary>左侧列表头高度</summary>
        private const float ListHeaderHeight = 40f;

        /// <summary>坐骑列表项高度</summary>
        private const float MountItemHeight = 60f;

        /// <summary>坐骑列表项间距</summary>
        private const float MountItemGap = 6f;

        /// <summary>3D 预览舞台高度</summary>
        private const float PreviewStageHeight = 280f;

        /// <summary>属性卡片高度</summary>
        private const float AttrCardHeight = 78f;

        /// <summary>属性卡片间距</summary>
        private const float AttrCardGap = 12f;

        /// <summary>技能槽尺寸</summary>
        private const float SkillSlotSize = 64f;

        /// <summary>技能槽间距</summary>
        private const float SkillSlotGap = 12f;

        /// <summary>外观幻化缩略图尺寸</summary>
        private const float MorphThumbSize = 84f;

        /// <summary>外观幻化缩略图间距</summary>
        private const float MorphThumbGap = 8f;

        /// <summary>导航按钮宽度</summary>
        private const float NavButtonWidth = 160f;

        /// <summary>导航按钮高度</summary>
        private const float NavButtonHeight = 36f;

        // ===================================================================
        // 子控件引用 — 主面板与顶部栏
        // =======================================================================

        /// <summary>主面板容器（居中 1400x900，带抬升阴影）</summary>
        private InkPanelElevated _mainPanel;

        /// <summary>顶部标题栏</summary>
        private InkPanel _topBar;

        /// <summary>"坐骑"Tab 按钮（默认激活）</summary>
        private InkButton _tabMount;

        /// <summary>"灵兽"Tab 按钮</summary>
        private InkButton _tabPet;

        /// <summary>居中"坐骑灵兽"标题</summary>
        private Label _centerTitleLabel;

        /// <summary>右上角返回按钮</summary>
        private InkButton _backButton;

        // ===================================================================
        // 子控件引用 — 左侧坐骑名册
        // =======================================================================

        /// <summary>左侧坐骑名册面板</summary>
        private InkPanel _leftList;

        /// <summary>6 张坐骑列表项容器</summary>
        private ContainerControl[] _mountItems;

        // ===================================================================
        // 子控件引用 — 中央预览与属性
        // =======================================================================

        /// <summary>中央内容容器</summary>
        private InkPanel _middleContent;

        /// <summary>3D 预览舞台容器</summary>
        private ContainerControl _previewStage;

        /// <summary>预览舞台中央灵兽字徽标签</summary>
        private Label _previewGlyphLabel;

        /// <summary>坐骑名称大标题</summary>
        private Label _mountNameLabel;

        /// <summary>坐骑副标题（瑞兽·异兽类·已驯服）</summary>
        private Label _mountSubLabel;

        /// <summary>3 张属性卡片容器</summary>
        private ContainerControl[] _attrCards;

        /// <summary>耐力进度条（嵌在第二张属性卡内）</summary>
        private InkBar _staminaBar;

        /// <summary>3 个技能槽</summary>
        private InkCell[] _skillSlots;

        /// <summary>灵兽传说文字标签</summary>
        private Label _loreLabel;

        // ===================================================================
        // 子控件引用 — 右侧操作面板
        // =======================================================================

        /// <summary>右侧操作面板</summary>
        private InkPanel _rightOps;

        /// <summary>出战状态指示标签</summary>
        private Label _deployStatusLabel;

        /// <summary>"休息"切换按钮</summary>
        private InkButton _btnRest;

        /// <summary>"喂养"按钮</summary>
        private InkButton _btnFeed;

        /// <summary>"驯养"按钮</summary>
        private InkButton _btnTrain;

        /// <summary>技能升级按钮</summary>
        private InkButton _btnSkillUp;

        /// <summary>"学习新技能"按钮</summary>
        private InkButton _btnSkillLearn;

        /// <summary>技能升级进度条</summary>
        private InkBar _skillUpgradeBar;

        /// <summary>3 个外观幻化缩略图按钮</summary>
        private InkButton[] _morphThumbs;

        /// <summary>移速加成进度条</summary>
        private InkBar _speedBonusBar;

        // ===================================================================
        // 子控件引用 — 底部导航栏
        // =======================================================================

        /// <summary>底部导航栏</summary>
        private InkPanel _bottomBar;

        /// <summary>返回沉浸模式按钮</summary>
        private InkButton _btnReturnHud;

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
        /// 构造函数：初始化主面板与所有子控件。
        /// </summary>
        public MountPetPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = InkWashTheme.Scrim;
                ClipChildren = false;
                AutoFocus = false;

                BuildMainPanel();
                BuildTopBar();
                BuildLeftList();
                BuildMiddleContent();
                BuildRightOps();
                BuildBottomBar();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MountPetPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建居中主面板容器（1400x900，带抬升阴影）。
        /// </summary>
        private void BuildMainPanel()
        {
            _mainPanel = new InkPanelElevated
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = MainPanelSize,
            };
            AddChild(_mainPanel);
        }

        /// <summary>
        /// 构建顶部标题栏：左侧双 Tab + 居中标题 + 右上返回按钮。
        /// </summary>
        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(MainPanelSize.X, TopBarHeight),
            };
            _mainPanel.AddChild(_topBar);

            // 左侧双 Tab（坐骑 / 灵兽）
            _tabMount = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "坐骑",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(80f, 28f),
                TextColor = InkWashTheme.TextGold,
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.1f),
            };
            _topBar.AddChild(_tabMount);

            _tabPet = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "灵兽",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20f + 80f + 24f, (TopBarHeight - 28f) * 0.5f),
                Size = new Float2(80f, 28f),
                TextColor = InkWashTheme.TextSecondary,
            };
            _topBar.AddChild(_tabPet);

            // 居中"坐骑灵兽"标题
            _centerTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((MainPanelSize.X - 200f) * 0.5f, 0f),
                Size = new Float2(200f, TopBarHeight),
                Text = "坐骑灵兽",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 20f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _topBar.AddChild(_centerTitleLabel);

            // 右上角返回按钮
            _backButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "✕",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(MainPanelSize.X - 50f - Padding, (TopBarHeight - 36f) * 0.5f),
                Size = new Float2(36f, 36f),
            };
            _backButton.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _topBar.AddChild(_backButton);
        }

        /// <summary>
        /// 构建左侧坐骑名册面板：列表头 + 6 张坐骑卡片。
        /// </summary>
        private void BuildLeftList()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;

            _leftList = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, contentTop),
                Size = new Float2(LeftListWidth, contentH),
            };
            _mainPanel.AddChild(_leftList);

            // 列表头："坐骑名册" + 数量
            var headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(LeftListWidth, ListHeaderHeight),
                BackgroundColor = Color.Transparent,
            };
            _leftList.AddChild(headerPanel);

            var headerTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 0f),
                Size = new Float2(160f, ListHeaderHeight),
                Text = "坐骑名册",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            headerPanel.AddChild(headerTitle);

            var headerCount = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(LeftListWidth - 60f, 0f),
                Size = new Float2(44f, ListHeaderHeight),
                Text = "06",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 12f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            headerPanel.AddChild(headerCount);

            // 6 张坐骑列表项
            string[] mountNames = { "墨麒麟", "踏雪乌骓", "雪羽鹤", "青骢追风", "黄骠马", "雪原驯鹿" };
            string[] mountChars = { "麟", "骓", "鹤", "骢", "马", "鹿" };
            string[] mountBadges = { "出战", "史诗", "飞禽", "稀有", "良好", "普通" };
            int[] mountLevels = { 85, 72, 68, 60, 45, 30 };
            int[] mountSpeeds = { 320, 280, 265, 250, 210, 180 };
            InkWashTheme.InkQuality[] mountQualities =
            {
                InkWashTheme.InkQuality.Legendary,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Common,
            };

            _mountItems = new ContainerControl[6];
            float itemW = LeftListWidth - Padding * 2;
            float listTop = ListHeaderHeight + Padding;

            for (int i = 0; i < 6; i++)
            {
                float itemY = listTop + i * (MountItemHeight + MountItemGap);
                bool isActive = (i == 0);
                Color qualityColor = InkWashTheme.QualityColor(mountQualities[i]);

                var item = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding, itemY),
                    Size = new Float2(itemW, MountItemHeight),
                    BackgroundColor = isActive
                        ? new Color(InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                                    InkWashTheme.GoldPrimary.B, 0.09f)
                        : Color.Transparent,
                };
                _mountItems[i] = item;
                _leftList.AddChild(item);

                // 品质色图标格
                var iconBox = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 8f),
                    Size = new Float2(44f, 44f),
                    BackgroundColor = new Color(
                        qualityColor.R, qualityColor.G, qualityColor.B, 0.14f),
                };
                item.AddChild(iconBox);

                var charLabel = new Label
                {
                    AnchorPreset = AnchorPresets.StretchAll,
                    Text = mountChars[i],
                    TextColor = isActive ? InkWashTheme.GoldBright : qualityColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 22f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                iconBox.AddChild(charLabel);

                // 坐骑名称（左上）
                var nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(60f, 8f),
                    Size = new Float2(itemW - 60f - 56f, 22f),
                    Text = mountNames[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 14f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(nameLabel);

                // 品质/出战徽章（右上）
                var badgeLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopRight,
                    Location = new Float2(itemW - 54f, 8f),
                    Size = new Float2(46f, 18f),
                    Text = mountBadges[i],
                    TextColor = isActive ? InkWashTheme.TextInverse : qualityColor,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 10f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                    BackgroundColor = isActive
                        ? InkWashTheme.GoldPrimary
                        : new Color(qualityColor.R, qualityColor.G, qualityColor.B, 0.18f),
                };
                item.AddChild(badgeLabel);

                // 等级 + 速度
                var statLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(60f, 30f),
                    Size = new Float2(itemW - 68f, 18f),
                    Text = "Lv." + mountLevels[i] + "  ·  速 " + mountSpeeds[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                item.AddChild(statLabel);
            }
        }

        /// <summary>
        /// 构建中央内容：3D 预览舞台 + 名称 + 属性卡 + 技能槽 + 传说。
        /// </summary>
        private void BuildMiddleContent()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;
            float middleX = Padding + LeftListWidth + PanelGap;
            float middleW = MainPanelSize.X - Padding * 2 - LeftListWidth - RightOpsWidth - PanelGap * 2;

            _middleContent = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(middleX, contentTop),
                Size = new Float2(middleW, contentH),
            };
            _mainPanel.AddChild(_middleContent);

            float innerW = middleW - Padding * 2;
            float cursorY = Padding;

            // ===== 3D 预览舞台 =====
            _previewStage = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, PreviewStageHeight),
                BackgroundColor = InkWashTheme.Abyss,
            };
            _middleContent.AddChild(_previewStage);

            // 中央灵兽字徽圆盘
            var glyphCircle = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2((innerW - 120f) * 0.5f, (PreviewStageHeight - 120f) * 0.5f),
                Size = new Float2(120f, 120f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.16f),
            };
            _previewStage.AddChild(glyphCircle);

            _previewGlyphLabel = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "麟",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 64f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            glyphCircle.AddChild(_previewGlyphLabel);

            // 顶部品质徽章
            var qualityBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopCenter,
                Location = new Float2((innerW - 80f) * 0.5f, 14f),
                Size = new Float2(80f, 22f),
                Text = "传说",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(
                    InkWashTheme.GoldDeep.R, InkWashTheme.GoldDeep.G,
                    InkWashTheme.GoldDeep.B, 0.24f),
            };
            _previewStage.AddChild(qualityBadge);

            // 底部操作提示
            var hintLabel = new Label
            {
                AnchorPreset = AnchorPresets.BottomCenter,
                Location = new Float2((innerW - 200f) * 0.5f, PreviewStageHeight - 28f),
                Size = new Float2(200f, 18f),
                Text = "拖拽旋转 · 滚轮缩放",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            _previewStage.AddChild(hintLabel);

            cursorY += PreviewStageHeight + 16f;

            // ===== 名称 + 五行标签行 =====
            _mountNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(200f, 36f),
                Text = "墨麒麟",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 28f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middleContent.AddChild(_mountNameLabel);

            _mountSubLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY + 36f),
                Size = new Float2(innerW - Padding * 2, 18f),
                Text = "瑞兽 · 异兽类 · 已驯服",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _middleContent.AddChild(_mountSubLabel);

            // 右上五行 + 坐骑徽章
            var wuxingBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 150f, cursorY + 6f),
                Size = new Float2(70f, 22f),
                Text = "五行 · 金",
                TextColor = InkWashTheme.ElementMetal,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = new Color(
                    InkWashTheme.ElementMetal.R, InkWashTheme.ElementMetal.G,
                    InkWashTheme.ElementMetal.B, 0.12f),
            };
            _middleContent.AddChild(wuxingBadge);

            var typeBadge = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 72f, cursorY + 6f),
                Size = new Float2(60f, 22f),
                Text = "坐骑",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                BackgroundColor = InkWashTheme.PanelSolid,
            };
            _middleContent.AddChild(typeBadge);

            cursorY += 36f + 18f + 16f;

            // ===== 3 张属性卡片（速度/耐力/跳跃力） =====
            string[] attrLabels = { "速度", "耐力", "跳跃力" };
            string[] attrValues = { "320", "4500/4500", "8.5" };
            string[] attrUnits = { "尺/息", "", "丈" };
            float[] attrProgress = { 1.0f, 1.0f, 0.85f };

            _attrCards = new ContainerControl[3];
            float attrColW = (innerW - AttrCardGap * 2) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float cardX = Padding + i * (attrColW + AttrCardGap);
                var card = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(cardX, cursorY),
                    Size = new Float2(attrColW, AttrCardHeight),
                    BackgroundColor = InkWashTheme.PanelSolid,
                };
                _attrCards[i] = card;
                _middleContent.AddChild(card);

                var labL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 10f),
                    Size = new Float2(attrColW - 24f, 16f),
                    Text = attrLabels[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(labL);

                var valL = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(12f, 28f),
                    Size = new Float2(attrColW - 24f, 24f),
                    Text = attrValues[i] + (string.IsNullOrEmpty(attrUnits[i]) ? "" : " " + attrUnits[i]),
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 20f),
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                };
                card.AddChild(valL);

                // 耐力卡片附进度条
                if (i == 1)
                {
                    _staminaBar = new InkBar
                    {
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(12f, AttrCardHeight - 14f),
                        Size = new Float2(attrColW - 24f, 6f),
                        Value = attrProgress[i],
                        FillVariant = InkBarFillVariant.Gold,
                    };
                    card.AddChild(_staminaBar);
                }
            }

            cursorY += AttrCardHeight + 16f;

            // ===== 技能槽行（3 个） =====
            var skillHeader = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 20f),
                BackgroundColor = Color.Transparent,
            };
            _middleContent.AddChild(skillHeader);

            var skillTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 0f),
                Size = new Float2(120f, 20f),
                Text = "特殊技能",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            skillHeader.AddChild(skillTitle);

            var skillCount = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 60f, 0f),
                Size = new Float2(50f, 20f),
                Text = "2 / 3",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            skillHeader.AddChild(skillCount);

            cursorY += 24f;

            // 3 个技能槽
            _skillSlots = new InkCell[3];
            string[] skillChars = { "火", "撞", "+" };
            string[] skillNames = { "踏火穿云", "神威冲撞", "未学习" };
            for (int i = 0; i < 3; i++)
            {
                bool filled = (i < 2);
                var slot = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding + i * (SkillSlotSize + SkillSlotGap), cursorY),
                    Size = new Float2(SkillSlotSize, SkillSlotSize),
                    Quality = filled ? InkWashTheme.InkQuality.Legendary : InkWashTheme.InkQuality.Common,
                };
                _skillSlots[i] = slot;
                _middleContent.AddChild(slot);

                var slotChar = new Label
                {
                    AnchorPreset = AnchorPresets.TopCenter,
                    Location = new Float2((SkillSlotSize - 48f) * 0.5f, 8f),
                    Size = new Float2(48f, 28f),
                    Text = skillChars[i],
                    TextColor = filled ? InkWashTheme.GoldBright : InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 20f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                slot.AddChild(slotChar);

                var slotName = new Label
                {
                    AnchorPreset = AnchorPresets.TopCenter,
                    Location = new Float2((SkillSlotSize - 60f) * 0.5f, 38f),
                    Size = new Float2(60f, 16f),
                    Text = skillNames[i],
                    TextColor = InkWashTheme.TextSecondary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 9f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
                slot.AddChild(slotName);
            }

            // 技能详情卡片（右侧）
            float skillDetailX = Padding + 3 * (SkillSlotSize + SkillSlotGap);
            float skillDetailW = innerW - skillDetailX - Padding;
            var skillDetail = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(skillDetailX, cursorY),
                Size = new Float2(skillDetailW, SkillSlotSize),
                BackgroundColor = InkWashTheme.PanelSolid,
            };
            _middleContent.AddChild(skillDetail);

            var sdName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 6f),
                Size = new Float2(120f, 18f),
                Text = "踏火穿云",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            skillDetail.AddChild(sdName);

            var sdLvl = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(skillDetailW - 60f, 6f),
                Size = new Float2(50f, 18f),
                Text = "Lv.3",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            skillDetail.AddChild(sdLvl);

            var sdDesc = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 28f),
                Size = new Float2(skillDetailW - 20f, 32f),
                Text = "奔腾时蹄生烈焰，无视减速地形，持续 8 息。",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Near,
            };
            skillDetail.AddChild(sdDesc);

            cursorY += SkillSlotSize + 16f;

            // ===== 灵兽传说 =====
            var lorePanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 56f),
                BackgroundColor = InkWashTheme.BgMist,
            };
            _middleContent.AddChild(lorePanel);

            _loreLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(innerW - 24f, 40f),
                Text = "麒麟踏火而出，墨鳞如甲，乃瑞兽之首。性烈而忠，唯修为深厚者可驭。",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            lorePanel.AddChild(_loreLabel);
        }

        /// <summary>
        /// 构建右侧操作面板：出战状态 + 喂养/驯养 + 技能研习 + 外观幻化 + 移速加成。
        /// </summary>
        private void BuildRightOps()
        {
            float contentTop = TopBarHeight + PanelGap;
            float contentBottom = MainPanelSize.Y - BottomBarHeight - PanelGap;
            float contentH = contentBottom - contentTop;
            float opsX = MainPanelSize.X - Padding - RightOpsWidth;

            _rightOps = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(opsX, contentTop),
                Size = new Float2(RightOpsWidth, contentH),
            };
            _mainPanel.AddChild(_rightOps);

            float innerW = RightOpsWidth - Padding * 2;
            float cursorY = Padding;

            // ===== 出战状态卡片 =====
            var deployCard = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 76f),
                BackgroundColor = new Color(
                    InkWashTheme.GoldPrimary.R, InkWashTheme.GoldPrimary.G,
                    InkWashTheme.GoldPrimary.B, 0.06f),
            };
            _rightOps.AddChild(deployCard);

            var deployHead = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(innerW - 24f, 24f),
                BackgroundColor = Color.Transparent,
            };
            deployCard.AddChild(deployHead);

            // 出战状态指示圆点 + 文字
            var statusDot = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, 8f),
                Size = new Float2(8f, 8f),
                BackgroundColor = InkWashTheme.GoldBright,
            };
            deployHead.AddChild(statusDot);

            _deployStatusLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(14f, 0f),
                Size = new Float2(100f, 24f),
                Text = "出战中",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            deployHead.AddChild(_deployStatusLabel);

            _btnRest = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "休息",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 24f - 80f, 0f),
                Size = new Float2(80f, 24f),
            };
            deployHead.AddChild(_btnRest);

            var deployHint = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 40f),
                Size = new Float2(innerW - 24f, 28f),
                Text = "出战期间持续消耗耐力，归厩后自动恢复。",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            deployCard.AddChild(deployHint);

            cursorY += 76f + 14f;

            // ===== 驯养喂养区 =====
            var feedTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(120f, 20f),
                Text = "驯养喂养",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightOps.AddChild(feedTitle);

            cursorY += 24f;

            float feedBtnW = (innerW - 8f) * 0.5f;
            _btnFeed = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "喂养",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(feedBtnW, 34f),
            };
            _rightOps.AddChild(_btnFeed);

            _btnTrain = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Md,
                Text = "驯养",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding + feedBtnW + 8f, cursorY),
                Size = new Float2(feedBtnW, 34f),
            };
            _rightOps.AddChild(_btnTrain);

            cursorY += 34f + 8f;

            // 消耗材料说明条
            var costRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 30f),
                BackgroundColor = InkWashTheme.PanelSolid,
            };
            _rightOps.AddChild(costRow);

            var costLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(10f, 0f),
                Size = new Float2(70f, 30f),
                Text = "消耗材料",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            costRow.AddChild(costLabel);

            var costDetail = new Label
            {
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 170f, 0f),
                Size = new Float2(160f, 30f),
                Text = "灵草 ×3   驯兽丹 ×1",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
            };
            costRow.AddChild(costDetail);

            cursorY += 30f + 14f;

            // ===== 技能研习区 =====
            var skillUpTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(120f, 20f),
                Text = "技能研习",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightOps.AddChild(skillUpTitle);

            cursorY += 24f;

            // 技能升级行
            var skillUpRow = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 50f),
                BackgroundColor = InkWashTheme.PanelSolid,
            };
            _rightOps.AddChild(skillUpRow);

            var skillUpIcon = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(8f, 5f),
                Size = new Float2(40f, 40f),
                Quality = InkWashTheme.InkQuality.Legendary,
            };
            skillUpRow.AddChild(skillUpIcon);

            var skillUpIconChar = new Label
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "火",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            skillUpIcon.AddChild(skillUpIconChar);

            var skillUpName = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 6f),
                Size = new Float2(innerW - 56f - 90f - 8f, 18f),
                Text = "踏火穿云",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 12f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            skillUpRow.AddChild(skillUpName);

            var skillUpLvl = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 24f),
                Size = new Float2(innerW - 56f - 90f - 8f, 14f),
                Text = "Lv.3 → 4",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 11f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            skillUpRow.AddChild(skillUpLvl);

            _skillUpgradeBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(56f, 40f),
                Size = new Float2(innerW - 56f - 90f - 8f, 6f),
                Value = 0.72f,
                FillVariant = InkBarFillVariant.Gold,
            };
            skillUpRow.AddChild(_skillUpgradeBar);

            _btnSkillUp = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "升级",
                AnchorPreset = AnchorPresets.TopRight,
                Location = new Float2(innerW - 8f - 60f, 10f),
                Size = new Float2(60f, 30f),
            };
            skillUpRow.AddChild(_btnSkillUp);

            cursorY += 50f + 8f;

            // 学习新技能按钮（占整行）
            _btnSkillLearn = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "+ 学习新技能",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 34f),
            };
            _rightOps.AddChild(_btnSkillLearn);

            cursorY += 34f + 14f;

            // ===== 外观幻化 =====
            var morphTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(120f, 20f),
                Text = "外观幻化",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _rightOps.AddChild(morphTitle);

            cursorY += 24f;

            _morphThumbs = new InkButton[3];
            string[] morphChars = { "麟", "焰", "霜" };
            Color[] morphColors =
            {
                InkWashTheme.GoldBright,
                InkWashTheme.TextBlood,
                InkWashTheme.TextJade,
            };
            float morphColW = (innerW - MorphThumbGap * 2) / 3f;
            for (int i = 0; i < 3; i++)
            {
                bool isActive = (i == 0);
                var thumb = new InkButton
                {
                    Variant = isActive ? InkButtonVariant.Primary : InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Md,
                    Text = morphChars[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(Padding + i * (morphColW + MorphThumbGap), cursorY),
                    Size = new Float2(morphColW, MorphThumbSize),
                };
                _morphThumbs[i] = thumb;
                _rightOps.AddChild(thumb);
            }

            cursorY += MorphThumbSize + 14f;

            // ===== 移速加成预览 =====
            var speedPanel = new ContainerControl
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(Padding, cursorY),
                Size = new Float2(innerW, 100f),
                BackgroundColor = InkWashTheme.PanelSolid,
            };
            _rightOps.AddChild(speedPanel);

            var speedTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 10f),
                Size = new Float2(160f, 20f),
                Text = "移速加成",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 13f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(speedTitle);

            // 基础移速（左侧）
            var baseSpeedLab = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 34f),
                Size = new Float2(60f, 14f),
                Text = "基础移速",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(baseSpeedLab);

            var baseSpeedVal = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 48f),
                Size = new Float2(60f, 20f),
                Text = "100",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 16f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(baseSpeedVal);

            // 骑乘移速（右侧）
            var rideSpeedLab = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(120f, 34f),
                Size = new Float2(120f, 14f),
                Text = "骑乘移速",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(rideSpeedLab);

            var rideSpeedVal = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(120f, 48f),
                Size = new Float2(120f, 22f),
                Text = "160  +60%",
                TextColor = InkWashTheme.GoldBright,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 18f),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            speedPanel.AddChild(rideSpeedVal);

            _speedBonusBar = new InkBar
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 76f),
                Size = new Float2(innerW - 24f, 6f),
                Value = 0.80f,
                FillVariant = InkBarFillVariant.Gold,
            };
            speedPanel.AddChild(_speedBonusBar);
        }

        /// <summary>
        /// 构建底部导航栏：返回沉浸模式按钮。
        /// </summary>
        private void BuildBottomBar()
        {
            _bottomBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0f, MainPanelSize.Y - BottomBarHeight),
                Size = new Float2(MainPanelSize.X, BottomBarHeight),
            };
            _mainPanel.AddChild(_bottomBar);

            _btnReturnHud = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(
                    (MainPanelSize.X - NavButtonWidth) * 0.5f,
                    (BottomBarHeight - NavButtonHeight) * 0.5f),
                Size = new Float2(NavButtonWidth, NavButtonHeight),
            };
            _btnReturnHud.ButtonClicked += (b) => OnSystemNavButtonClicked(InkPageDomIds.CombatHud, b);
            _bottomBar.AddChild(_btnReturnHud);
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
                FlaxEngine.Debug.LogError($"[MountPetPage] NavigationRequested({domId}) 触发失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 在按钮中心位置触发金粉爆发粒子反馈。
        /// </summary>
        private void EmitGoldAtButton(Control button)
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
                FlaxEngine.Debug.LogWarning($"[MountPetPage] EmitGoldAtButton 失败: {ex.Message}");
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

                // 主面板居中
                if (_mainPanel != null)
                {
                    float panelX = (sw - MainPanelSize.X) * 0.5f;
                    float panelY = (sh - MainPanelSize.Y) * 0.5f;
                    _mainPanel.Location = new Float2(
                        panelX > 0f ? panelX : 0f,
                        panelY > 0f ? panelY : 0f);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MountPetPage] RefreshLayout 失败: {ex.Message}");
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
