using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.Social
{
    /// <summary>
    /// 江湖商城页面 — 对应 shop.html 设计原型。
    /// <para>
    /// 三栏式布局：
    /// <list type="bullet">
    ///   <item>顶部：返回按钮 + 标题 + 玩家货币栏（元宝/绑元/银两/铜币）</item>
    ///   <item>左侧：8 个商品分类竖向 Tab（推荐/限定/装备/材料/时装/坐骑/礼包/其他）</item>
    ///   <item>中央：4×3 商品网格（每张卡片显示图标 + 品质边框 + 名称 + 价格 + 购买按钮）</item>
    ///   <item>右侧：选中商品详情（大图标 + 名称 + 品质 + 描述 + 属性 + 价格 + 限购 + 数量选择 + 确认购买）</item>
    ///   <item>底部：返回沉浸模式按钮，触发 <see cref="NavigationRequested"/> 回到 combat-hud</item>
    /// </list>
    /// 所有数据为 mock，通过 <see cref="RefreshLayout"/> 适配父容器尺寸变化。
    /// </para>
    /// </summary>
    public class SocialShopPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        /// <summary>屏幕外缘留白</summary>
        private const float ScreenEdge = 16f;

        /// <summary>顶部标题栏高度</summary>
        private const float HeaderHeight = 64f;

        /// <summary>底部返回栏高度</summary>
        private const float FooterHeight = 44f;

        /// <summary>栏间距</summary>
        private const float PanelGap = 8f;

        /// <summary>左侧分类栏宽度</summary>
        private const float CategoryWidth = 180f;

        /// <summary>右侧详情栏宽度</summary>
        private const float DetailWidth = 320f;

        /// <summary>每条分类项高度</summary>
        private const float CategoryItemHeight = 44f;

        /// <summary>商品卡片宽度</summary>
        private const float CardWidth = 144f;

        /// <summary>商品卡片高度</summary>
        private const float CardHeight = 168f;

        /// <summary>卡片间距</summary>
        private const float CardGap = 8f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        /// <summary>顶部标题栏</summary>
        private InkPanel _headerPanel;

        /// <summary>返回按钮</summary>
        private InkBackButton _backButton;

        /// <summary>主标题</summary>
        private Label _titleLabel;

        /// <summary>副标题</summary>
        private Label _subtitleLabel;

        /// <summary>元宝标签</summary>
        private Label _yuanbaoLabel;

        /// <summary>元宝数值</summary>
        private Label _yuanbaoValueLabel;

        /// <summary>绑元标签</summary>
        private Label _boundYuanLabel;

        /// <summary>绑元数值</summary>
        private Label _boundYuanValueLabel;

        /// <summary>银两标签</summary>
        private Label _silverLabel;

        /// <summary>银两数值</summary>
        private Label _silverValueLabel;

        /// <summary>铜币标签</summary>
        private Label _copperLabel;

        /// <summary>铜币数值</summary>
        private Label _copperValueLabel;

        /// <summary>左侧分类面板</summary>
        private InkPanel _categoryPanel;

        /// <summary>分类标题</summary>
        private Label _categoryTitleLabel;

        /// <summary>8 条分类按钮</summary>
        private InkButton[] _categoryButtons;

        /// <summary>中央商品网格面板</summary>
        private InkPanel _gridPanel;

        /// <summary>网格标题（当前分类名）</summary>
        private Label _gridTitleLabel;

        /// <summary>12 张商品卡片</summary>
        private ShopCard[] _shopCards;

        /// <summary>右侧详情面板</summary>
        private InkPanel _detailPanel;

        /// <summary>详情大图标</summary>
        private InkCell _detailIcon;

        /// <summary>详情名称</summary>
        private Label _detailNameLabel;

        /// <summary>详情品质标签</summary>
        private InkTag _detailQualityTag;

        /// <summary>详情描述</summary>
        private Label _detailDescLabel;

        /// <summary>详情属性标题</summary>
        private Label _detailAttrTitleLabel;

        /// <summary>4 条属性项</summary>
        private Label[] _detailAttrItems;

        /// <summary>详情价格标签</summary>
        private Label _detailPriceLabel;

        /// <summary>详情限购标签</summary>
        private Label _detailLimitLabel;

        /// <summary>数量减号按钮</summary>
        private InkButton _qtyMinusButton;

        /// <summary>数量数值标签</summary>
        private Label _qtyValueLabel;

        /// <summary>数量加号按钮</summary>
        private InkButton _qtyPlusButton;

        /// <summary>确认购买按钮</summary>
        private InkButton _confirmBuyButton;

        /// <summary>底部返回沉浸模式按钮</summary>
        private InkButton _footerBackButton;

        // ===================================================================
        // 公共 API
        // =======================================================================

        /// <summary>
        /// 导航请求事件。返回按钮触发时携带 <see cref="InkPageDomIds.CombatHud"/>，
        /// 确认购买按钮触发时携带 <see cref="InkPageDomIds.NavSocialShop"/>。
        /// </summary>
        public event Action<string> NavigationRequested;

        /// <summary>
        /// 粒子动效系统引用（可选，由 MainUIManager 注入）。
        /// </summary>
        public InkParticleSystem ParticleSystem { get; set; }

        // ===================================================================
        // 数据绑定字段
        // =======================================================================

        /// <summary>已绑定的角色属性组件（null 表示未绑定，使用 mock 数据）</summary>
        private CharacterAttributesComponent _boundCharacter;

        /// <summary>
        /// 绑定角色属性组件。绑定后页面可从组件读取真实角色名/等级等基础信息。
        /// 传入 null 解除绑定，回退到 mock 数据。
        /// 注意：本页面主要依赖服务端数据（通过 TouchSocket + MemoryPack 推送），
        /// 此方法仅绑定本地可获取的角色基础信息，服务端数据待网络层接入后通过 RefreshFromServerAsync 绑定。
        /// </summary>
        /// <param name="component">角色属性组件实例</param>
        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
        }

        // ===================================================================
        // 构造函数
        // =======================================================================

        /// <summary>
        /// 构造函数：初始化所有子控件。
        /// </summary>
        public SocialShopPage()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;

                BuildHeader();
                BuildCategoryPanel();
                BuildGridPanel();
                BuildDetailPanel();
                BuildFooter();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialShopPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // Build 方法
        // =======================================================================

        /// <summary>
        /// 构建顶部标题栏：返回按钮 + 标题 + 4 种货币展示。
        /// </summary>
        private void BuildHeader()
        {
            _headerPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(800f, HeaderHeight),
            };

            // 返回按钮
            _backButton = new InkBackButton
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, (HeaderHeight - 40f) * 0.5f),
                Size = new Float2(40f, 40f),
            };
            _backButton.Clicked += OnBackButtonClicked;
            _headerPanel.AddChild(_backButton);

            // 主标题
            _titleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(60f, 8f),
                Size = new Float2(160f, 24f),
                Text = "江湖商城",
                TextColor = InkWashTheme.TextBrand,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 20f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_titleLabel);

            // 副标题
            _subtitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(60f, 34f),
                Size = new Float2(160f, 16f),
                Text = "珍奇异宝 · 江湖通商",
                TextColor = InkWashTheme.TextTertiary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _headerPanel.AddChild(_subtitleLabel);

            // 4 种货币展示（元宝/绑元/银两/铜币）
            string[] curNames = { "元宝", "绑元", "银两", "铜币" };
            string[] curValues = { "1,250", "860", "45,200", "12,500" };
            Color[] curColors =
            {
                InkWashTheme.TextGold,
                InkWashTheme.TextBrand,
                InkWashTheme.TextJade,
                InkWashTheme.TextSecondary,
            };
            Label[] labelList = new Label[8];

            for (int i = 0; i < 4; i++)
            {
                float xPos = 260f + i * 130f;
                var curLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(xPos, 12f),
                    Size = new Float2(40f, 18f),
                    Text = curNames[i],
                    TextColor = InkWashTheme.TextTertiary,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                _headerPanel.AddChild(curLabel);
                labelList[i * 2] = curLabel;

                var curValLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(xPos + 40f, 12f),
                    Size = new Float2(80f, 18f),
                    Text = curValues[i],
                    TextColor = curColors[i],
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                _headerPanel.AddChild(curValLabel);
                labelList[i * 2 + 1] = curValLabel;
            }

            _yuanbaoLabel = labelList[0];
            _yuanbaoValueLabel = labelList[1];
            _boundYuanLabel = labelList[2];
            _boundYuanValueLabel = labelList[3];
            _silverLabel = labelList[4];
            _silverValueLabel = labelList[5];
            _copperLabel = labelList[6];
            _copperValueLabel = labelList[7];

            AddChild(_headerPanel);
        }

        /// <summary>
        /// 构建左侧分类面板（8 个分类按钮）。
        /// </summary>
        private void BuildCategoryPanel()
        {
            _categoryPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(CategoryWidth, 600f),
            };

            // 分类标题
            _categoryTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(CategoryWidth - 24f, 20f),
                Text = "◆ 商品分类",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _categoryPanel.AddChild(_categoryTitleLabel);

            // 8 条分类按钮
            _categoryButtons = new InkButton[8];
            string[] catNames =
            {
                "推荐", "限定", "装备", "材料",
                "时装", "坐骑", "礼包", "其他",
            };

            for (int i = 0; i < 8; i++)
            {
                var btn = new InkButton
                {
                    Variant = i == 0 ? InkButtonVariant.Primary : InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Sm,
                    Text = catNames[i],
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f, 36f + i * CategoryItemHeight),
                    Size = new Float2(CategoryWidth - 16f, CategoryItemHeight - 4f),
                };

                int idx = i;
                btn.ButtonClicked += (b) => OnCategoryButtonClicked(idx, b);

                _categoryButtons[i] = btn;
                _categoryPanel.AddChild(btn);
            }

            AddChild(_categoryPanel);
        }

        /// <summary>
        /// 构建中央商品网格面板（4×3 共 12 张商品卡片）。
        /// </summary>
        private void BuildGridPanel()
        {
            _gridPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(600f, 600f),
            };

            // 当前分类标题
            _gridTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(12f, 8f),
                Size = new Float2(560f, 20f),
                Text = "推荐商品 · 限时折扣",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _gridPanel.AddChild(_gridTitleLabel);

            // 12 张商品卡片
            _shopCards = new ShopCard[12];
            string[] cardNames =
            {
                "玄铁剑", "寒铁护腕", "生命药水", "经验丹",
                "铁矿", "坐骑令", "外观包", "强化石",
                "精铁剑", "玄铁甲", "灵魂石", "传功符",
            };
            InkWashTheme.InkQuality[] cardQualities =
            {
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Common,
                InkWashTheme.InkQuality.Legendary,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Rare,
                InkWashTheme.InkQuality.Epic,
                InkWashTheme.InkQuality.Uncommon,
                InkWashTheme.InkQuality.Rare,
            };
            string[] cardPrices =
            {
                "500 金", "300 金", "50 金", "50 钻",
                "10 金", "100 荣", "200 钻", "100 金",
                "500 金", "800 金", "30 钻", "80 荣",
            };
            string[] cardLimits =
            {
                "限购 3", string.Empty, string.Empty, "限购 5",
                string.Empty, "限购 1", string.Empty, string.Empty,
                string.Empty, "限购 2", string.Empty, "限购 3",
            };

            for (int i = 0; i < 12; i++)
            {
                int col = i % 4;
                int row = i / 4;
                var card = new ShopCard
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(8f + col * (CardWidth + CardGap), 36f + row * (CardHeight + CardGap)),
                    Size = new Float2(CardWidth, CardHeight),
                    Name = cardNames[i],
                    Quality = cardQualities[i],
                    Price = cardPrices[i],
                    Limit = cardLimits[i],
                };
                int idx = i;
                card.BuyClicked += (b) => OnGridCardBuyClicked(idx, b);
                _shopCards[i] = card;
                _gridPanel.AddChild(card);
            }

            AddChild(_gridPanel);
        }

        /// <summary>
        /// 构建右侧详情面板（大图标 + 名称 + 品质 + 描述 + 属性 + 价格 + 限购 + 数量 + 确认）。
        /// </summary>
        private void BuildDetailPanel()
        {
            _detailPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(DetailWidth, 600f),
            };

            // 大图标
            _detailIcon = new InkCell
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 16f),
                Size = new Float2(72f, 72f),
                Quality = InkWashTheme.InkQuality.Epic,
            };
            _detailPanel.AddChild(_detailIcon);

            // 名称
            _detailNameLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(100f, 20f),
                Size = new Float2(200f, 24f),
                Text = "玄铁剑",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Display), 18f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailNameLabel);

            // 品质标签
            _detailQualityTag = new InkTag
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(100f, 50f),
                Size = new Float2(56f, 22f),
                Text = "史诗",
                TagVariant = InkTagVariant.Default,
            };
            _detailPanel.AddChild(_detailQualityTag);

            // 限购标签
            _detailLimitLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(168f, 50f),
                Size = new Float2(132f, 22f),
                Text = "限购 3 件",
                TextColor = InkWashTheme.TextVermilion,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 11f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _detailPanel.AddChild(_detailLimitLabel);

            // 描述
            _detailDescLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 100f),
                Size = new Float2(DetailWidth - 32f, 48f),
                Text = "采自极北寒潭的玄铁锻造，剑身乌沉似墨，挥动间隐隐有龙吟之声，可破寻常护体罡气。",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailDescLabel);

            // 属性标题
            _detailAttrTitleLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 156f),
                Size = new Float2(DetailWidth - 32f, 18f),
                Text = "◆ 装备属性",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailAttrTitleLabel);

            // 4 条属性项
            _detailAttrItems = new Label[4];
            string[] attrTexts =
            {
                "攻击   +180",
                "暴击   +12%",
                "穿透   +35",
                "耐久   120 / 120",
            };
            for (int i = 0; i < 4; i++)
            {
                var attrLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(20f, 180f + i * 20f),
                    Size = new Float2(DetailWidth - 40f, 18f),
                    Text = attrTexts[i],
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                    HorizontalAlignment = TextAlignment.Near,
                };
                _detailAttrItems[i] = attrLabel;
                _detailPanel.AddChild(attrLabel);
            }

            // 价格标签
            _detailPriceLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 268f),
                Size = new Float2(DetailWidth - 32f, 24f),
                Text = "单价：500 金",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(_detailPriceLabel);

            // 数量选择行
            var qtyTitle = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 300f),
                Size = new Float2(56f, 22f),
                Text = "数量",
                TextColor = InkWashTheme.TextSecondary,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 12f),
                HorizontalAlignment = TextAlignment.Near,
            };
            _detailPanel.AddChild(qtyTitle);

            _qtyMinusButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "－",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(80f, 298f),
                Size = new Float2(28f, 28f),
            };
            _qtyMinusButton.ButtonClicked += (b) => OnQtyMinusClicked(b);
            _detailPanel.AddChild(_qtyMinusButton);

            _qtyValueLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(112f, 300f),
                Size = new Float2(56f, 24f),
                Text = "1",
                TextColor = InkWashTheme.TextDefault,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 14f),
                HorizontalAlignment = TextAlignment.Center,
            };
            _detailPanel.AddChild(_qtyValueLabel);

            _qtyPlusButton = new InkButton
            {
                Variant = InkButtonVariant.Default,
                ButtonSize = InkButtonSize.Sm,
                Text = "＋",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(172f, 298f),
                Size = new Float2(28f, 28f),
            };
            _qtyPlusButton.ButtonClicked += (b) => OnQtyPlusClicked(b);
            _detailPanel.AddChild(_qtyPlusButton);

            // 合计标签（与数量同一行右侧）
            var totalLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(208f, 300f),
                Size = new Float2(96f, 24f),
                Text = "合计 500",
                TextColor = InkWashTheme.TextGold,
                Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                HorizontalAlignment = TextAlignment.Far,
            };
            _detailPanel.AddChild(totalLabel);

            // 确认购买按钮
            _confirmBuyButton = new InkButton
            {
                Variant = InkButtonVariant.Primary,
                ButtonSize = InkButtonSize.Lg,
                Text = "确认购买",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 340f),
                Size = new Float2(DetailWidth - 32f, 44f),
            };
            _confirmBuyButton.ButtonClicked += (b) => OnConfirmBuyClicked(b);
            _detailPanel.AddChild(_confirmBuyButton);

            AddChild(_detailPanel);
        }

        /// <summary>
        /// 构建底部返回沉浸模式按钮。
        /// </summary>
        private void BuildFooter()
        {
            _footerBackButton = new InkButton
            {
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                Text = "返回沉浸模式",
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(180f, 36f),
            };
            _footerBackButton.ButtonClicked += (b) => OnBackButtonClicked();
            AddChild(_footerBackButton);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        /// <summary>
        /// 返回按钮点击处理：触发金粉粒子 + 导航回 combat-hud。
        /// </summary>
        private void OnBackButtonClicked()
        {
            try
            {
                if (_backButton != null)
                    EmitGoldAtButton(_backButton);
                NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialShopPage] OnBackButtonClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 分类按钮点击处理：切换选中态 + 触发金粉粒子。
        /// </summary>
        private void OnCategoryButtonClicked(int index, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                for (int i = 0; i < _categoryButtons.Length; i++)
                {
                    if (_categoryButtons[i] == null)
                        continue;
                    _categoryButtons[i].Variant = (i == index)
                        ? InkButtonVariant.Primary
                        : InkButtonVariant.Default;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[SocialShopPage] OnCategoryButtonClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 商品卡片购买按钮点击处理：触发金粉粒子 + 通知外部刷新。
        /// </summary>
        private void OnGridCardBuyClicked(int index, Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(InkPageDomIds.NavSocialShop);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[SocialShopPage] OnGridCardBuyClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 数量减号点击处理：最低保留 1。
        /// </summary>
        private void OnQtyMinusClicked(Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                if (_qtyValueLabel != null && int.TryParse(_qtyValueLabel.Text, out int q) && q > 1)
                    _qtyValueLabel.Text = (q - 1).ToString();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[SocialShopPage] OnQtyMinusClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 数量加号点击处理：最高不超过限购 3。
        /// </summary>
        private void OnQtyPlusClicked(Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                if (_qtyValueLabel != null && int.TryParse(_qtyValueLabel.Text, out int q) && q < 3)
                    _qtyValueLabel.Text = (q + 1).ToString();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[SocialShopPage] OnQtyPlusClicked 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 确认购买按钮点击处理：触发金粉粒子 + 触发导航事件。
        /// </summary>
        private void OnConfirmBuyClicked(Button sourceButton)
        {
            try
            {
                EmitGoldAtButton(sourceButton);
                NavigationRequested?.Invoke(InkPageDomIds.NavSocialShop);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialShopPage] OnConfirmBuyClicked 失败: {ex.Message}");
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
                FlaxEngine.Debug.LogWarning($"[SocialShopPage] EmitGoldAtButton 失败: {ex.Message}");
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

                // 顶部标题栏
                if (_headerPanel != null)
                {
                    _headerPanel.Location = new Float2(ScreenEdge, ScreenEdge);
                    _headerPanel.Size = new Float2(sw - 2f * ScreenEdge, HeaderHeight);
                }

                // 底部返回按钮
                if (_footerBackButton != null)
                {
                    _footerBackButton.Location = new Float2(
                        sw * 0.5f - 90f,
                        sh - ScreenEdge - 36f);
                }

                // 中间内容区域
                float contentTop = ScreenEdge + HeaderHeight + PanelGap;
                float contentBottom = sh - ScreenEdge - 36f - PanelGap;
                float contentHeight = contentBottom - contentTop;

                // 左侧分类面板
                if (_categoryPanel != null)
                {
                    _categoryPanel.Location = new Float2(ScreenEdge, contentTop);
                    _categoryPanel.Size = new Float2(CategoryWidth, contentHeight);
                }

                // 右侧详情面板
                if (_detailPanel != null)
                {
                    _detailPanel.Location = new Float2(sw - ScreenEdge - DetailWidth, contentTop);
                    _detailPanel.Size = new Float2(DetailWidth, contentHeight);
                }

                // 中央商品网格
                if (_gridPanel != null)
                {
                    float gridX = ScreenEdge + CategoryWidth + PanelGap;
                    float gridWidth = sw - 2f * ScreenEdge - CategoryWidth - DetailWidth - 2f * PanelGap;
                    _gridPanel.Location = new Float2(gridX, contentTop);
                    _gridPanel.Size = new Float2(gridWidth, contentHeight);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[SocialShopPage] RefreshLayout 失败: {ex.Message}");
            }
        }

        /// <inheritdoc />
        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        // ===================================================================
        // 内部嵌套类型
        // =======================================================================

        /// <summary>
        /// 商品卡片：中央网格中一件商品展示卡。
        /// </summary>
        private class ShopCard : ContainerControl
        {
            /// <summary>商品图标格子</summary>
            private InkCell _iconCell;

            /// <summary>商品名称标签</summary>
            private Label _nameLabel;

            /// <summary>价格标签</summary>
            private Label _priceLabel;

            /// <summary>限购徽章</summary>
            private Label _limitLabel;

            /// <summary>购买按钮</summary>
            private InkButton _buyButton;

            /// <summary>当前品质</summary>
            private InkWashTheme.InkQuality _quality = InkWashTheme.InkQuality.Common;

            /// <summary>
            /// 商品名称变更时同步更新内部标签。
            /// </summary>
            public string Name
            {
                get => _nameLabel?.Text ?? string.Empty;
                set
                {
                    if (_nameLabel != null)
                        _nameLabel.Text = value;
                }
            }

            /// <summary>
            /// 品质等级变更时同步更新图标边框。
            /// </summary>
            public InkWashTheme.InkQuality Quality
            {
                get => _quality;
                set
                {
                    _quality = value;
                    if (_iconCell != null)
                        _iconCell.Quality = value;
                }
            }

            /// <summary>
            /// 价格文本。
            /// </summary>
            public string Price
            {
                set
                {
                    if (_priceLabel != null)
                        _priceLabel.Text = value;
                }
            }

            /// <summary>
            /// 限购徽章文本，空字符串时隐藏徽章。
            /// </summary>
            public string Limit
            {
                set
                {
                    if (_limitLabel == null)
                        return;
                    _limitLabel.Text = value ?? string.Empty;
                    _limitLabel.Visible = !string.IsNullOrEmpty(value);
                }
            }

            /// <summary>
            /// 购买按钮点击事件。
            /// </summary>
            public event Action<Button> BuyClicked;

            /// <summary>
            /// 构造函数：初始化子控件。
            /// </summary>
            public ShopCard()
            {
                BackgroundColor = new Color(
                    InkWashTheme.BaseSecondary.R,
                    InkWashTheme.BaseSecondary.G,
                    InkWashTheme.BaseSecondary.B,
                    0.6f);
                ClipChildren = false;

                // 商品图标（顶部居中）
                _iconCell = new InkCell
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2((CardWidth - 56f) * 0.5f, 8f),
                    Size = new Float2(56f, 56f),
                    Quality = InkWashTheme.InkQuality.Common,
                };
                AddChild(_iconCell);

                // 商品名称
                _nameLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(6f, 70f),
                    Size = new Float2(CardWidth - 12f, 20f),
                    Text = string.Empty,
                    TextColor = InkWashTheme.TextDefault,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Heading), 13f),
                    HorizontalAlignment = TextAlignment.Center,
                };
                AddChild(_nameLabel);

                // 价格标签
                _priceLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(6f, 94f),
                    Size = new Float2(CardWidth - 12f, 18f),
                    Text = string.Empty,
                    TextColor = InkWashTheme.TextGold,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Number), 13f),
                    HorizontalAlignment = TextAlignment.Center,
                };
                AddChild(_priceLabel);

                // 限购徽章
                _limitLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(CardWidth - 56f, 6f),
                    Size = new Float2(50f, 16f),
                    Text = string.Empty,
                    TextColor = InkWashTheme.TextVermilion,
                    Font = new FontReference(InkWashTheme.GetFont(InkWashTheme.FontRole.Body), 10f),
                    HorizontalAlignment = TextAlignment.Far,
                    Visible = false,
                };
                AddChild(_limitLabel);

                // 购买按钮
                _buyButton = new InkButton
                {
                    Variant = InkButtonVariant.Default,
                    ButtonSize = InkButtonSize.Sm,
                    Text = "购买",
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, 120f),
                    Size = new Float2(CardWidth - 32f, 28f),
                };
                _buyButton.ButtonClicked += (b) => BuyClicked?.Invoke(b);
                AddChild(_buyButton);
            }

            /// <inheritdoc />
            public override void Draw()
            {
                if (!Visible || Width <= 0f || Height <= 0f)
                    return;

                var bounds = new Rectangle(0, 0, Width, Height);

                // 卡片背景
                Render2D.FillRectangle(bounds, BackgroundColor);

                // 品质色边框
                Render2D.DrawRectangle(bounds, InkWashTheme.QualityColor(_quality), 1f);

                // 绘制子控件
                base.Draw();
            }
        }
    }
}
