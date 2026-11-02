using System;
using System.Linq;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.Personal
{
    public class MenuPersonalInfoPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        /// <summary>
        /// 标准导航事件（对齐 Ink 页面契约，由工厂 HookNavigationRequested 挂钩后经路由器跳转）。
        /// </summary>
        public event Action<string> NavigationRequested;

        private Float2 _screenSize;

        private InkPanel _leftNav;
        private InkPanel _topBar;
        private InkButton _backButton;
        private ContainerControl _playerAvatar;
        private Label _playerNameLabel;
        private Label _playerLevelLabel;
        private Label _sectLabel;
        private Label _currencyCoinLabel;
        private Label _currencyIngotLabel;

        private Label _pageTitle;

        private InkPanel _leftInfoPanel;
        private ContainerControl _characterAvatar;
        private InkVerticalTitle _verticalName;
        private Label[] _infoLabels;
        private InkPanel _titlePanel;
        private InkPanel[] _statCards;
        private InkDivider _infoDivider1;
        private Label _basicTitle;
        private InkDivider _infoDivider2;
        private Label _titlesTitle;
        private Label[] _titleLabels;

        private InkPanel _rightSection;
        private InkButton[] _tabButtons;
        private InkPanel _statsGrid;
        private Label[] _statLabels;

        private InkPanel _achievementPanel;
        private Label _achievementTitle;
        private Label _achievementValue;
        private InkBar _achievementBar;

        private string[] _basicInfoLabels = { "等级", "门派", "性别", "创建日期" };
        private string[] _basicInfoValues = { "—", "—", "—", "—" };
        private string[] _titles = { "暂无称号" };

        private string[] _statCategories = { "游戏时长", "击败敌人", "完成任务", "探索区域", "收集物品", "PVP胜场" };
        private string[] _statValues = { "—", "—", "—", "—", "—", "—" };
        private string[] _statUnits = { "", "", "", "", "", "" };

        // 真实身份（构建控件前算好：Build* 直接读数组取文本）
        private string _realNick = "—";
        private string _realSect = "—";

        public MenuPersonalInfoPage()
        {
            _screenSize = FlaxEngine.Screen.Size;
            if (_screenSize.X <= 0f || _screenSize.Y <= 0f)
            {
                _screenSize = new Float2(1920f, 1080f);
            }

            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;
            Size = _screenSize;

            // 身份/基本信息先按真实数据填好，再建控件（Build* 阶段直接从数组取文本）
            PrepareRealData();

            try
            {
                BuildLeftNav();
                BuildTopBar();
                BuildLeftInfoPanel();
                BuildRightSection();

                ApplyLayout();
                RefreshAllData();

                // 顶栏身份接真实数据
                InkPlayerHeaderBinder.Bind(_playerNameLabel, _playerLevelLabel);
                if (_verticalName != null) _verticalName.Text = _realNick;
                if (_sectLabel != null) _sectLabel.Text = _realSect;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[MenuPersonalInfoPage] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 用真实角色数据准备身份与基本信息：等级取属性组件、门派取当前角色职业，
        /// 性别/创建日期与全部统计没有数据源，统一显示「—」；称号列表改为空态，
        /// 不再摆设计稿的假称号（2026-10-29）。
        /// </summary>
        private void PrepareRealData()
        {
            var local = HundunWorldGame.Instance?.LocalPlayerActor;
            var attrs = local?.GetScript<global::Game.Character.Attributes.CharacterAttributesComponent>();

            _realNick = attrs != null && !string.IsNullOrWhiteSpace(attrs.Nickname) ? attrs.Nickname : "—";

            var role = global::Game.UI.Character.CharacterManager.Instance?.SelectedCharacter;
            string sect = global::HundunWorld.Game.UI.Ink.Pages.CharacterCreation.CcSectNames.Get(
                role?.Profession ?? Horizon.Game.Message.Enums.Profession.None);
            _realSect = string.IsNullOrEmpty(sect) ? "无门派" : sect;

            string levelText = attrs != null && attrs.Level > 0 ? "Lv. " + attrs.Level : "—";
            _basicInfoValues = new[] { levelText, _realSect, "—", "—" };
        }

        private void BuildLeftNav()
        {
            _leftNav = new InkPanel
            {
                Width = 240f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseDefault,
                Parent = this
            };

            var logoText = new Label
            {
                Text = "混沌世界",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 22f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                Size = new Float2(240f, 40f),
                Location = new Float2(0f, 20f),
                Parent = _leftNav
            };

            var divider = new InkDivider
            {
                Width = 240f,
                Height = 1f,
                Location = new Float2(0f, 68f),
                Parent = _leftNav
            };

            string[] navItems = { "角色", "装备", "外观", "备战", "门派", "个人信息", "时辰", "任务", "博物志", "武林录", "营生", "组队", "邮箱", "商店", "设置" };
            int[] disabledIndices = { 7, 8, 9, 10, 11, 12, 13, 14 };

            // 左栏导航即设计链路：角色→装备→外观→备战→门派→个人信息(自身)→时辰。
            // 索引 7+ 为禁用项（disabledIndices），不接线。
            string[] navTargets =
            {
                InkPageDomIds.NavCharacterV2,
                InkPageDomIds.NavEquipment,
                InkPageDomIds.NavAppearance,
                InkPageDomIds.NavBattlePrep,
                InkPageDomIds.NavSect,
                InkPageDomIds.NavPersonalInfo,
                InkPageDomIds.NavTime,
            };

            float y = 80f;
            for (int i = 0; i < navItems.Length; i++)
            {
                var navItem = new InkButton
                {
                    Width = 240f,
                    Height = 40f,
                    Text = navItems[i],
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center,
                    Location = new Float2(0f, y),
                    Parent = _leftNav
                };

                if (i == 5)
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.BackgroundColor = InkWashTheme.GoldPrimary * 0.1f;
                    navItem.TextColor = InkWashTheme.GoldBright;
                }
                else if (disabledIndices.Contains(i))
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.TextColor = InkWashTheme.TextDisabled;
                    navItem.Enabled = false;
                }
                else
                {
                    navItem.Variant = InkButtonVariant.Ghost;
                    navItem.TextColor = InkWashTheme.PaperBright;
                }

                // 左栏项直达对应菜单（禁用项 Enabled=false，点击不触发）。
                if (i < navTargets.Length)
                {
                    int navIndex = i;
                    navItem.Clicked += () => NavigationRequested?.Invoke(navTargets[navIndex]);
                }

                y += 40f;

                if (i == 6 || i == 13)
                {
                    var div = new InkDivider
                    {
                        Width = 240f,
                        Height = 1f,
                        Location = new Float2(0f, y),
                        Parent = _leftNav
                    };
                    y += 8f;
                }
            }
        }

        private void BuildTopBar()
        {
            _topBar = new InkPanel
            {
                Height = 60f,
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = InkWashTheme.BaseTertiary,
                Parent = this
            };

            _backButton = new InkButton
            {
                Width = 36f,
                Height = 36f,
                Text = "<",
                Variant = InkButtonVariant.Ghost,
                Parent = _topBar
            };
            // 返回战斗 HUD（设计隐藏出口 back-hud）
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);

            _playerAvatar = new ContainerControl
            {
                Size = new Float2(36f, 36f),
                BackgroundColor = Color.Lerp(InkWashTheme.BaseElevated, InkWashTheme.BaseTertiary, 0.5f),
                Parent = _topBar
            };
            var avatarLabel = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                AnchorPreset = AnchorPresets.StretchAll,
                Parent = _playerAvatar
            };

            _playerNameLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 14f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _topBar
            };

            _playerLevelLabel = new Label
            {
                // 初值留空位，真实等级由 InkPlayerHeaderBinder 覆盖
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.VermilionBright,
                Parent = _topBar
            };

            _sectLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _topBar
            };

            _currencyCoinLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _topBar
            };

            _currencyIngotLabel = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.JadeBright,
                Parent = _topBar
            };
        }

        private void BuildLeftInfoPanel()
        {
            _leftInfoPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = this
            };

            var cornerDecoTL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Parent = _leftInfoPanel
            };

            var cornerDecoTR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.TopRight,
                Parent = _leftInfoPanel
            };

            var cornerDecoBL = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Parent = _leftInfoPanel
            };

            var cornerDecoBR = new InkCornerDeco
            {
                AnchorPreset = AnchorPresets.BottomRight,
                Parent = _leftInfoPanel
            };

            _characterAvatar = new ContainerControl
            {
                Size = new Float2(80f, 80f),
                BackgroundColor = Color.Lerp(InkWashTheme.BaseElevated, InkWashTheme.BaseTertiary, 0.5f),
                Parent = _leftInfoPanel
            };
            _characterAvatar.AnchorPreset = AnchorPresets.TopLeft;
            var charAvatarLabel = new Label
            {
                Text = "无",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 32f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                // 头像圈固定 80×80：StretchAll 在这里不会跟着父尺寸走（实测停 180×100），
                // 居中后字的一半落在圈外，被裁成「= / -」两笔。直接给死尺寸。
                Location = Float2.Zero,
                Size = new Float2(80f, 80f),
                Parent = _characterAvatar
            };

            _verticalName = new InkVerticalTitle
            {
                Text = "—",
                FontSize = 28f,
                Width = 30f,
                Height = 120f,
                Parent = _leftInfoPanel
            };

            // 下面四件（两条分隔线 + 两个小节标题）原来是局部变量且从未定位，
            // 全部叠在面板 (0,0)：画面上表现为标题压在卡片上边框、多出两条短线。
            _infoDivider1 = new InkDivider
            {
                Height = 1f,
                Parent = _leftInfoPanel
            };

            _basicTitle = new Label
            {
                Text = "基本信息",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(200f, 20f),
                Parent = _leftInfoPanel
            };

            _infoLabels = new Label[_basicInfoLabels.Length * 2];
            for (int i = 0; i < _basicInfoLabels.Length; i++)
            {
                _infoLabels[i * 2] = new Label
                {
                    Text = _basicInfoLabels[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = _leftInfoPanel
                };

                _infoLabels[i * 2 + 1] = new Label
                {
                    Text = _basicInfoValues[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = _leftInfoPanel
                };
            }

            _infoDivider2 = new InkDivider
            {
                Height = 1f,
                Parent = _leftInfoPanel
            };

            _titlesTitle = new Label
            {
                Text = "已获称号",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Size = new Float2(200f, 20f),
                Parent = _leftInfoPanel
            };

            _titlePanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _leftInfoPanel
            };

            _titleLabels = new Label[_titles.Length];
            for (int i = 0; i < _titles.Length; i++)
            {
                _titleLabels[i] = new Label
                {
                    Text = _titles[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = i == 1 ? InkWashTheme.GoldBright : InkWashTheme.TextSecondary,
                    Parent = _titlePanel
                };
            }
        }

        private void BuildRightSection()
        {
            _rightSection = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopRight,
                BackgroundColor = Color.Transparent,
                Parent = this
            };

            _pageTitle = new Label
            {
                Text = "个人信息",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 22f),
                TextColor = InkWashTheme.PaperBright,
                Parent = _rightSection
            };

            _tabButtons = new InkButton[4];
            string[] tabNames = { "总览", "战斗", "探索", "社交" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                _tabButtons[i] = new InkButton
                {
                    Width = 80f,
                    Height = 32f,
                    Text = tabNames[i],
                    Variant = InkButtonVariant.Ghost,
                    TextColor = i == 0 ? InkWashTheme.GoldBright : InkWashTheme.TextDisabled,
                    Parent = _rightSection
                };
                if (i == 0)
                {
                    _tabButtons[i].BackgroundColor = InkWashTheme.GoldPrimary * 0.1f;
                }
            }

            _statsGrid = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _rightSection
            };

            _statLabels = new Label[_statCategories.Length * 3];
            _statCards = new InkPanel[_statCategories.Length];
            for (int i = 0; i < _statCategories.Length; i++)
            {
                var statCard = new InkPanel
                {
                    BackgroundColor = InkWashTheme.BaseTertiary,
                    Parent = _statsGrid
                };
                _statCards[i] = statCard;

                var categoryLabel = new Label
                {
                    Text = _statCategories[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.TextDisabled,
                    Parent = statCard
                };

                var valueLabel = new Label
                {
                    Text = _statValues[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 24f),
                    TextColor = InkWashTheme.PaperBright,
                    Parent = statCard
                };

                var unitLabel = new Label
                {
                    Text = _statUnits[i],
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                    TextColor = InkWashTheme.PaperAged,
                    Parent = statCard
                };

                _statLabels[i * 3] = categoryLabel;
                _statLabels[i * 3 + 1] = valueLabel;
                _statLabels[i * 3 + 2] = unitLabel;
            }

            _achievementPanel = new InkPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                BackgroundColor = Color.Transparent,
                Parent = _rightSection
            };

            _achievementTitle = new Label
            {
                Text = "成就进度",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 13f),
                TextColor = InkWashTheme.GoldBright,
                Parent = _achievementPanel
            };

            _achievementValue = new Label
            {
                Text = "—",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = InkWashTheme.PaperAged,
                Parent = _achievementPanel
            };

            _achievementBar = new InkBar
            {
                Height = 8f,
                Parent = _achievementPanel
            };
            // 成就系统没有数据源：不显示假进度（条留空，数字为「—」）
            _achievementBar.Value = 0f;
        }

        private void ApplyLayout()
        {
            float sw = _screenSize.X;
            float sh = _screenSize.Y;

            if (_leftNav != null)
            {
                _leftNav.Size = new Float2(240f, sh);
                _leftNav.Location = new Float2(0f, 0f);
            }

            if (_topBar != null)
            {
                _topBar.Size = new Float2(sw - 240f, 60f);
                _topBar.Location = new Float2(240f, 0f);
            }

            if (_backButton != null)
                _backButton.Location = new Float2(24f, 12f);
            if (_playerAvatar != null)
                _playerAvatar.Location = new Float2(72f, 12f);
            if (_playerNameLabel != null)
                _playerNameLabel.Location = new Float2(116f, 14f);
            if (_playerLevelLabel != null)
                _playerLevelLabel.Location = new Float2(220f, 16f);
            if (_sectLabel != null)
                _sectLabel.Location = new Float2(280f, 16f);
            if (_currencyCoinLabel != null)
                _currencyCoinLabel.Location = new Float2(sw - 160f, 16f);
            if (_currencyIngotLabel != null)
                _currencyIngotLabel.Location = new Float2(sw - 80f, 16f);

            float leftWidth = (sw - 240f) * 0.35f;
            float rightWidth = (sw - 240f) * 0.65f;
            float padding = 24f;

            if (_leftInfoPanel != null)
            {
                _leftInfoPanel.Size = new Float2(leftWidth - padding, 500f);
                _leftInfoPanel.Location = new Float2(240f + padding, 80f);
            }

            if (_characterAvatar != null)
                _characterAvatar.Location = new Float2(32f, 32f);
            if (_verticalName != null)
                _verticalName.Location = new Float2(130f, 32f);

            // 左卡片：头像/竖排名（32..112）→ 分隔线 → 基本信息 → 四行 → 分隔线 → 已获称号 → 称号格
            if (_infoDivider1 != null)
            {
                _infoDivider1.Location = new Float2(32f, 128f);
                _infoDivider1.Size = new Float2(leftWidth - padding * 2f, 1f);
            }
            if (_basicTitle != null)
                _basicTitle.Location = new Float2(32f, 138f);

            // 值列右对齐到卡片内边距：原来固定 leftWidth-104 宽 100，会伸出卡片 20px
            float infoValueX = leftWidth - padding - 100f;
            float infoY = 168f;
            for (int i = 0; i < _basicInfoLabels.Length; i++)
            {
                if (_infoLabels[i * 2] != null)
                {
                    _infoLabels[i * 2].Location = new Float2(32f, infoY);
                    _infoLabels[i * 2].Size = new Float2(120f, 20f);
                }
                if (_infoLabels[i * 2 + 1] != null)
                {
                    _infoLabels[i * 2 + 1].Location = new Float2(infoValueX, infoY);
                    _infoLabels[i * 2 + 1].Size = new Float2(100f, 20f);
                }
                infoY += 28f;
            }

            if (_infoDivider2 != null)
            {
                _infoDivider2.Location = new Float2(32f, infoY + 8f);
                _infoDivider2.Size = new Float2(leftWidth - padding * 2f, 1f);
            }
            if (_titlesTitle != null)
                _titlesTitle.Location = new Float2(32f, infoY + 18f);

            if (_titlePanel != null)
            {
                _titlePanel.Location = new Float2(32f, infoY + 48f);
                _titlePanel.Size = new Float2(leftWidth - padding * 2f, 108f);
            }

            // 称号格的父容器是 _titlePanel，坐标要用面板内相对坐标（原来按左卡片坐标写，会偏出去）
            for (int i = 0; i < _titles.Length; i++)
            {
                if (_titleLabels[i] != null)
                {
                    _titleLabels[i].Location = new Float2(i * 90f, 8f);
                    _titleLabels[i].Size = new Float2(80f, 20f);
                }
            }

            if (_rightSection != null)
            {
                _rightSection.Size = new Float2(rightWidth, sh - 80f);
                _rightSection.Location = new Float2(240f + leftWidth, 80f);
            }

            if (_pageTitle != null)
                _pageTitle.Location = new Float2(0f, 0f);

            float tabX = 0f;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null)
                {
                    _tabButtons[i].Location = new Float2(tabX, 40f);
                    tabX += 100f;
                }
            }

            // 统计宫格：原来面板 Location 在循环里被反复改（最后停在最后一行的 y），
            // 且三行标签都没加行偏移，第二行起全叠在第一行上。
            if (_statsGrid != null)
            {
                _statsGrid.Size = new Float2(rightWidth, 260f);
                _statsGrid.Location = new Float2(0f, 80f);
            }

            float statCardW = (rightWidth - padding * 2f) / 3f;
            float statCardH = 100f;
            for (int i = 0; i < _statCategories.Length; i++)
            {
                int col = i % 3;
                int row = i / 3;

                // 卡片本身也没定位过（6 张全叠在网格 (0,0) 的 100×30 上）；
                // 三个标签的父级是卡片，坐标必须用卡片内相对坐标。
                if (_statCards != null && i < _statCards.Length && _statCards[i] != null)
                {
                    _statCards[i].Location = new Float2(col * (statCardW + padding * 0.5f), row * (statCardH + 12f));
                    _statCards[i].Size = new Float2(statCardW, statCardH);
                }

                var categoryLabel = _statLabels[i * 3];
                var valueLabel = _statLabels[i * 3 + 1];
                var unitLabel = _statLabels[i * 3 + 2];

                if (categoryLabel != null)
                {
                    categoryLabel.Location = new Float2(16f, 16f);
                    categoryLabel.Size = new Float2(statCardW - 32f, 20f);
                }
                if (valueLabel != null)
                {
                    valueLabel.Location = new Float2(16f, 40f);
                    valueLabel.Size = new Float2(statCardW - 90f, 32f);
                }
                if (unitLabel != null)
                {
                    unitLabel.Location = new Float2(statCardW - 64f, 52f);
                    unitLabel.Size = new Float2(48f, 18f);
                }
            }

            if (_achievementPanel != null)
            {
                _achievementPanel.Size = new Float2(rightWidth, 60f);
                _achievementPanel.Location = new Float2(0f, 356f);
            }

            if (_achievementTitle != null)
            {
                _achievementTitle.Location = new Float2(16f, 12f);
                _achievementTitle.Size = new Float2(160f, 20f);
            }
            if (_achievementValue != null)
            {
                _achievementValue.Location = new Float2(rightWidth - 96f, 12f);
                _achievementValue.Size = new Float2(80f, 20f);
            }
            if (_achievementBar != null)
            {
                _achievementBar.Size = new Float2(rightWidth - 32f, 8f);
                _achievementBar.Location = new Float2(16f, 38f);
            }
        }

        public void RefreshLayout()
        {
            _screenSize = new Float2(Width, Height);
            ApplyLayout();
        }

        public void RefreshAllData()
        {
        }

        public void OnPageEnter()
        {
            RefreshAllData();
        }

        public void OnPageLeave()
        {
        }

        public void OnPageUpdate()
        {
        }

        public void OnResolutionChanged()
        {
            _screenSize = FlaxEngine.Screen.Size;
            ApplyLayout();
        }
    }
}
