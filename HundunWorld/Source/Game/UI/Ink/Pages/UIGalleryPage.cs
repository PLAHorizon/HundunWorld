﻿using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages
{
    /// <summary>
    /// UI 浏览器（Debug 菜单）页面。
    /// 用于串联和查看所有已实现的 UI 页面，便于验证实际效果。
    /// 采用两栏布局：
    /// <list type="bullet">
    ///   <item>左侧分类导航栏（240px）：按类别分组显示所有页面入口</item>
    ///   <item>右侧主区域：顶部信息栏 + 页面网格（4列响应式）</item>
    /// </list>
    /// 点击页面卡片触发 <see cref="NavigationRequested"/> 事件，由路由器执行跳转。
    /// </summary>
    public class UIGalleryPage : ContainerControl, IInkPage
    {
        // ===================================================================
        // 布局常量
        // =======================================================================

        private const float SidebarWidth = 240f;
        private const float TopHeaderHeight = 60f;
        private const float PageMargin = 20f;
        private const float CardGap = 16f;
        private const float CardWidth = 200f;
        private const float CardHeight = 88f;

        // ===================================================================
        // 子控件引用
        // =======================================================================

        private InkPanel _sidebar;
        private InkTextBlock _sidebarTitle;
        private InkPanel _mainArea;
        private InkPanel _topHeader;
        private InkButton _backButton;
        private InkTextBlock _headerTitle;

        private InkPanel _contentScroll;
        private InkPanel _contentContainer;

        private readonly List<GalleryCategory> _categories = new();
        private readonly List<GalleryCard> _allCards = new();

        private string _activeCategory = "全部";
        private readonly List<GalleryCard> _visibleCards = new();

        private Float2 _screenSize;

        // ===================================================================
        // 事件
        // =======================================================================

        /// <summary>
        /// 页面导航请求事件。
        /// 参数为目标页面的 dom-id，由 <see cref="InkPageRouter"/> 订阅后执行跳转。
        /// </summary>
        public event Action<string> NavigationRequested;

        // ===================================================================
        // 构造函数
        // =======================================================================

        public UIGalleryPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = false;
            AutoFocus = true;
            Location = Float2.Zero;
            Size = _screenSize;

            try
            {
                InitCategoryData();
                BuildSidebar();
                BuildMainArea();
                BuildTopHeader();
                BuildContentScroll();
                RefreshVisibleCards();

                ApplyLayout();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[UIGalleryPage] 初始化失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 数据初始化
        // =======================================================================

        private void InitCategoryData()
        {
            // 战斗与 HUD
            _categories.Add(new GalleryCategory
            {
                Name = "战斗与HUD",
                Items =
                {
                    new GalleryItem("战斗 HUD", "combat-hud", "战斗主界面", InkWashTheme.VermilionPrimary),
                    new GalleryItem("战斗 HUD V2", "combat-hud-v2", "新版战斗HUD", InkWashTheme.VermilionBright),
                    new GalleryItem("阵亡界面", "death-screen", "破招/返回", InkWashTheme.VermilionDeep),
                    new GalleryItem("战前备战", "nav-battle-prep", "装备/武学/药品", InkWashTheme.BronzePrimary),
                    new GalleryItem("点穴系统", "acupoint", "人体穴位图", InkWashTheme.JadePrimary),
                    new GalleryItem("QTE 千钧一发", "qte", "圆环计时器", InkWashTheme.VermilionBright),
                    new GalleryItem("元素视野", "nav-element-vision", "高亮元素标记", InkWashTheme.JadeBright),
                }
            });

            // 角色与属性
            _categories.Add(new GalleryCategory
            {
                Name = "角色与属性",
                Items =
                {
                    new GalleryItem("角色属性 V2", "nav-character-v2", "两栏属性面板", InkWashTheme.GoldPrimary),
                    new GalleryItem("装备管理", "nav-equipment", "背包/纸娃娃", InkWashTheme.GoldBright),
                    new GalleryItem("外观", "nav-appearance", "发型/服饰/坐骑", InkWashTheme.GoldDeep),
                    new GalleryItem("个人信息", "nav-personal-info", "角色卡+统计", InkWashTheme.GoldPrimary),
                    new GalleryItem("武学记录", "nav-martial-record", "已学武学列表", InkWashTheme.BronzeBright),
                }
            });

            // 社交与组队
            _categories.Add(new GalleryCategory
            {
                Name = "社交与组队",
                Items =
                {
                    new GalleryItem("组队", "nav-team", "成员+邀请面板", InkWashTheme.JadePrimary),
                    new GalleryItem("门派", "nav-sect", "门派列表+详情", InkWashTheme.BronzePrimary),
                    new GalleryItem("多人模式", "nav-multiplayer", "房间列表", InkWashTheme.JadeBright),
                    new GalleryItem("NPC对话", "dialogue-confirm", "纸色卷轴对话", InkWashTheme.PaperAged),
                }
            });

            // 商店与经济
            _categories.Add(new GalleryCategory
            {
                Name = "商店与经济",
                Items =
                {
                    new GalleryItem("商店", "nav-shop", "三栏购物面板", InkWashTheme.GoldPrimary),
                    new GalleryItem("奇珍阁", "nav-shop-rare", "稀有商品", InkWashTheme.GoldBright),
                    new GalleryItem("抽卡祈愿", "nav-gacha", "祈愿池+结果", InkWashTheme.VermilionPrimary),
                }
            });

            // 活动与任务
            _categories.Add(new GalleryCategory
            {
                Name = "活动与任务",
                Items =
                {
                    new GalleryItem("任务菜单", "nav-quests", "三栏任务面板", InkWashTheme.GoldPrimary),
                    new GalleryItem("活动", "nav-activities", "限时活动", InkWashTheme.SpringGreenPrimary),
                    new GalleryItem("通行证", "nav-battle-pass", "战令进度", InkWashTheme.GoldBright),
                    new GalleryItem("休闲模式", "nav-casual-mode", "活动卡片", InkWashTheme.SpringGreenBright),
                }
            });

            // 收集与图鉴
            _categories.Add(new GalleryCategory
            {
                Name = "收集与图鉴",
                Items =
                {
                    new GalleryItem("博物志", "nav-bestiary", "收集品图鉴", InkWashTheme.JadePrimary),
                    new GalleryItem("邮件", "nav-mail", "信笺列表+详情", InkWashTheme.PaperAged),
                }
            });

            // 系统与设置
            _categories.Add(new GalleryCategory
            {
                Name = "系统与设置",
                Items =
                {
                    new GalleryItem("设置", "nav-settings", "画面/音效/操作", InkWashTheme.GoldPrimary),
                    new GalleryItem("音频设置", "nav-settings-audio", "音量调节", InkWashTheme.GoldBright),
                    new GalleryItem("拍照模式", "nav-photo-mode", "取景+滤镜", InkWashTheme.PaperBright),
                    new GalleryItem("时间系统", "nav-time", "时辰显示", InkWashTheme.GoldDeep),
                    new GalleryItem("生活技能", "nav-livelihood", "采集/制作", InkWashTheme.SpringGreenPrimary),
                }
            });

            // 加载与过场
            _categories.Add(new GalleryCategory
            {
                Name = "加载与过场",
                Items =
                {
                    new GalleryItem("加载页 1", "loading-1", "进度条加载", InkWashTheme.GoldPrimary),
                    new GalleryItem("加载页 2", "loading-2", "水墨晕染加载", InkWashTheme.GoldBright),
                    new GalleryItem("章节过场", "chapter-transition", "章节标题", InkWashTheme.GoldDeep),
                    new GalleryItem("创角捏脸", "cc-face-customize", "参数调整", InkWashTheme.PaperAged),
                    new GalleryItem("创角命名", "cc-naming", "姓名输入", InkWashTheme.PaperBright),
                }
            });

            // 弹窗
            _categories.Add(new GalleryCategory
            {
                Name = "弹窗与奖励",
                Items =
                {
                    new GalleryItem("获得物品", "popup-item-acquired", "物品获得弹窗", InkWashTheme.GoldBright),
                    new GalleryItem("江湖来信", "popup-message", "信笺弹窗", InkWashTheme.PaperAged),
                    new GalleryItem("任务验证完成", "popup-verification", "朱红印章", InkWashTheme.VermilionPrimary),
                    new GalleryItem("奇术详情", "popup-martial-arts", "技能详情", InkWashTheme.BronzePrimary),
                    new GalleryItem("心法领悟", "popup-skill-realization", "领悟弹窗", InkWashTheme.GoldDeep),
                    new GalleryItem("武学详情", "popup-martial-detail", "武学属性", InkWashTheme.BronzeBright),
                    new GalleryItem("引导侧边栏", "popup-guide-side", "引导步骤", InkWashTheme.GoldPrimary),
                    new GalleryItem("图鉴侧边栏", "popup-bestiary-side", "异兽详情", InkWashTheme.JadePrimary),
                    new GalleryItem("成就奖励", "reward-achievement", "成就解锁", InkWashTheme.GoldBright),
                    new GalleryItem("任务完成奖励", "reward-quest-complete", "完成评价", InkWashTheme.GoldPrimary),
                    new GalleryItem("等级提升", "reward-level-up", "属性对比", InkWashTheme.VermilionBright),
                }
            });

            // 收集所有卡片
            foreach (var category in _categories)
            {
                foreach (var item in category.Items)
                {
                    item.Category = category.Name;
                    _allCards.Add(new GalleryCard(item));
                }
            }
        }

        // ===================================================================
        // 构建方法
        // =======================================================================

        private void BuildSidebar()
        {
            _sidebar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseSecondary,
            };
            AddChild(_sidebar);

            _sidebarTitle = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "UI 浏览器",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PageMargin, PageMargin),
                Size = new Float2(SidebarWidth - PageMargin * 2f, 28f),
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
            };
            _sidebar.AddChild(_sidebarTitle);

            InkTextBlock subTitle = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = "UI Gallery · Debug",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PageMargin, PageMargin + 32f),
                Size = new Float2(SidebarWidth - PageMargin * 2f, 16f),
                TextColor = InkWashTheme.PaperDark,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
            };
            _sidebar.AddChild(subTitle);

            InkPanel divider = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PageMargin, PageMargin + 60f),
                Size = new Float2(SidebarWidth - PageMargin * 2f, 1f),
                BackgroundColor = InkWashTheme.GoldDeep,
            };
            _sidebar.AddChild(divider);

            // "全部" 分类按钮
            float navY = PageMargin + 80f;
            InkButton allBtn = new InkButton
            {
                Text = "全部",
                Variant = _activeCategory == "全部" ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PageMargin, navY),
                Size = new Float2(SidebarWidth - PageMargin * 2f, 36f),
            };
            allBtn.ButtonClicked += (btn) => SetActiveCategory("全部");
            _sidebar.AddChild(allBtn);

            // 各分类按钮
            for (int i = 0; i < _categories.Count; i++)
            {
                var category = _categories[i];
                float y = navY + 44f + i * 44f;

                InkButton catBtn = new InkButton
                {
                    Text = category.Name,
                    Variant = _activeCategory == category.Name ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(PageMargin, y),
                    Size = new Float2(SidebarWidth - PageMargin * 2f, 36f),
                };
                string catName = category.Name;
                catBtn.ButtonClicked += (btn) => SetActiveCategory(catName);
                _sidebar.AddChild(catBtn);
            }
        }

        private void BuildMainArea()
        {
            _mainArea = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
            };
            AddChild(_mainArea);
        }

        private void BuildTopHeader()
        {
            _topHeader = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.Panel,
            };
            _mainArea.AddChild(_topHeader);

            _backButton = new InkButton
            {
                Text = "← 返回",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Md,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PageMargin, TopHeaderHeight / 2f - 18f),
                Size = new Float2(100f, 36f),
                TextColor = InkWashTheme.GoldBright,
            };
            _backButton.ButtonClicked += OnBackButtonClicked;
            _topHeader.AddChild(_backButton);

            _headerTitle = new InkTextBlock(InkTextStyle.Heading)
            {
                Text = "UI 画廊 · 查看所有界面",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PageMargin + 120f, TopHeaderHeight / 2f - 14f),
                Size = new Float2(400f, 28f),
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 18f),
            };
            _topHeader.AddChild(_headerTitle);

            InkTextBlock countLabel = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = $"共 {_allCards.Count} 个页面",
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PageMargin + 120f, TopHeaderHeight / 2f + 14f),
                Size = new Float2(200f, 16f),
                TextColor = InkWashTheme.PaperDark,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
            };
            _topHeader.AddChild(countLabel);
        }

        private void BuildContentScroll()
        {
            _contentScroll = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
            };
            _mainArea.AddChild(_contentScroll);

            _contentContainer = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
            };
            _contentScroll.AddChild(_contentContainer);
        }

        // ===================================================================
        // 分类切换与卡片渲染
        // =======================================================================

        private void SetActiveCategory(string categoryName)
        {
            if (_activeCategory == categoryName) return;
            _activeCategory = categoryName;

            // 更新侧边栏按钮样式（重建侧边栏按钮）
            try
            {
                // 简单策略：销毁并重建侧边栏按钮区域
                // 由于侧边栏结构简单，重建成本低
                for (int i = _sidebar.ChildrenCount - 1; i >= 0; i--)
                {
                    var child = _sidebar.GetChild(i);
                    if (child is InkButton)
                    {
                        _sidebar.RemoveChild(child);
                        child.Dispose();
                    }
                }

                // 重新构建分类按钮
                float navY = PageMargin + 80f;
                InkButton allBtn = new InkButton
                {
                    Text = "全部",
                    Variant = _activeCategory == "全部" ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                    ButtonSize = InkButtonSize.Md,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(PageMargin, navY),
                    Size = new Float2(SidebarWidth - PageMargin * 2f, 36f),
                };
                allBtn.ButtonClicked += (btn) => SetActiveCategory("全部");
                _sidebar.AddChild(allBtn);

                for (int i = 0; i < _categories.Count; i++)
                {
                    var category = _categories[i];
                    float y = navY + 44f + i * 44f;

                    InkButton catBtn = new InkButton
                    {
                        Text = category.Name,
                        Variant = _activeCategory == category.Name ? InkButtonVariant.Primary : InkButtonVariant.Ghost,
                        ButtonSize = InkButtonSize.Md,
                        AnchorPreset = AnchorPresets.TopLeft,
                        Location = new Float2(PageMargin, y),
                        Size = new Float2(SidebarWidth - PageMargin * 2f, 36f),
                    };
                    string catName = category.Name;
                    catBtn.ButtonClicked += (btn) => SetActiveCategory(catName);
                    _sidebar.AddChild(catBtn);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[UIGalleryPage] 切换分类失败: {ex.Message}");
            }

            RefreshVisibleCards();
            ApplyLayout();
        }

        private void RefreshVisibleCards()
        {
            // 清理旧的卡片
            for (int i = _contentContainer.ChildrenCount - 1; i >= 0; i--)
            {
                var child = _contentContainer.GetChild(i);
                _contentContainer.RemoveChild(child);
                child.Dispose();
            }
            _visibleCards.Clear();

            // 筛选当前分类的卡片
            foreach (var card in _allCards)
            {
                if (_activeCategory == "全部" || card.Item.Category == _activeCategory)
                {
                    _visibleCards.Add(card);
                }
            }

            // 创建卡片控件
            foreach (var card in _visibleCards)
            {
                CreateCardControl(card);
            }
        }

        private void CreateCardControl(GalleryCard card)
        {
            InkPanel cardPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Size = new Float2(CardWidth, CardHeight),
            };
            _contentContainer.AddChild(cardPanel);

            // 左侧色条
            InkPanel colorBar = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(0, 0),
                Size = new Float2(4f, CardHeight),
                BackgroundColor = card.Item.Color,
            };
            cardPanel.AddChild(colorBar);

            // 页面名称
            InkTextBlock nameText = new InkTextBlock(InkTextStyle.Body)
            {
                Text = card.Item.DisplayName,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 12f),
                Size = new Float2(CardWidth - 24f, 22f),
                TextColor = InkWashTheme.PaperBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
            };
            cardPanel.AddChild(nameText);

            // 描述
            InkTextBlock descText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = card.Item.Description,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 38f),
                Size = new Float2(CardWidth - 24f, 16f),
                TextColor = InkWashTheme.PaperDark,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
            };
            cardPanel.AddChild(descText);

            // dom-id
            InkTextBlock domIdText = new InkTextBlock(InkTextStyle.Caption)
            {
                Text = card.Item.DomId,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, 58f),
                Size = new Float2(CardWidth - 24f, 14f),
                TextColor = InkWashTheme.GoldDeep,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 10f),
            };
            cardPanel.AddChild(domIdText);

            // 点击进入按钮
            InkButton enterBtn = new InkButton
            {
                Text = "进入 →",
                Variant = InkButtonVariant.Ghost,
                ButtonSize = InkButtonSize.Sm,
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(16f, CardHeight - 24f),
                Size = new Float2(CardWidth - 32f, 20f),
                TextColor = InkWashTheme.GoldBright,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
            };
            string targetDomId = card.Item.DomId;
            enterBtn.ButtonClicked += (btn) => OnCardClicked(targetDomId);
            cardPanel.AddChild(enterBtn);
        }

        // ===================================================================
        // 事件处理
        // =======================================================================

        private void OnCardClicked(string domId)
        {
            try
            {
                FlaxEngine.Debug.Log($"[UIGalleryPage] 请求导航到: {domId}");
                NavigationRequested?.Invoke(domId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[UIGalleryPage] 导航请求失败: {ex.Message}");
            }
        }

        private void OnBackButtonClicked(Button button)
        {
            try
            {
                NavigationRequested?.Invoke(InkPageDomIds.CombatHud);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[UIGalleryPage] 返回失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 布局计算
        // =======================================================================

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_sidebar != null)
            {
                _sidebar.Location = new Float2(0, 0);
                _sidebar.Size = new Float2(SidebarWidth, sh);
            }

            if (_mainArea != null)
            {
                _mainArea.Location = new Float2(SidebarWidth, 0);
                _mainArea.Size = new Float2(sw - SidebarWidth, sh);
            }

            if (_topHeader != null)
            {
                _topHeader.Location = new Float2(0, 0);
                _topHeader.Size = new Float2(sw - SidebarWidth, TopHeaderHeight);
            }

            if (_contentScroll != null)
            {
                _contentScroll.Location = new Float2(0, TopHeaderHeight);
                _contentScroll.Size = new Float2(sw - SidebarWidth, sh - TopHeaderHeight);
            }

            if (_contentContainer != null)
            {
                float containerX = PageMargin;
                float containerY = PageMargin;
                _contentContainer.Location = new Float2(containerX, containerY);

                // 响应式网格：根据宽度计算列数
                float availableWidth = sw - SidebarWidth - PageMargin * 2f;
                int cols = Math.Max(1, (int)((availableWidth + CardGap) / (CardWidth + CardGap)));
                cols = Math.Min(cols, 6); // 最多 6 列

                // 布局卡片
                for (int i = 0; i < _visibleCards.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;
                    float x = col * (CardWidth + CardGap);
                    float y = row * (CardHeight + CardGap);

                    if (i < _contentContainer.ChildrenCount)
                    {
                        var child = _contentContainer.GetChild(i);
                        child.Location = new Float2(x, y);
                    }
                }

                // 计算容器总大小
                int totalRows = (_visibleCards.Count + cols - 1) / cols;
                float containerWidth = cols * (CardWidth + CardGap) - CardGap;
                float containerHeight = totalRows * (CardHeight + CardGap) - CardGap;
                _contentContainer.Size = new Float2(
                    Math.Max(containerWidth, availableWidth),
                    Math.Max(containerHeight, sh - TopHeaderHeight - PageMargin * 2f));
            }
        }

        /// <inheritdoc />
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

        // ===================================================================
        // 内部数据类
        // =======================================================================

        private class GalleryCategory
        {
            public string Name { get; set; }
            public List<GalleryItem> Items { get; } = new();
        }

        private class GalleryItem
        {
            public string DisplayName { get; }
            public string DomId { get; }
            public string Description { get; }
            public Color Color { get; }
            public string Category { get; set; }

            public GalleryItem(string displayName, string domId, string description, Color color)
            {
                DisplayName = displayName;
                DomId = domId;
                Description = description;
                Color = color;
            }
        }

        private class GalleryCard
        {
            public GalleryItem Item { get; }

            public GalleryCard(GalleryItem item)
            {
                Item = item;
            }
        }
    }
}