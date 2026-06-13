using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Kungfu
{
    public enum KungfuTab
    {
        Kungfu = 0,
        Equipment = 1,
        Talent = 2,
        Other = 3
    }
    
    public class KungfuSystemUI : ContainerControl
    {
        private UICanvas _canvas;
        private Panel _topBar;
        private Panel _leftPanel;
        private Panel _centerPanel;
        private Panel _rightPanel;
        private Panel _bottomBar;
        
        private Button _searchButton;
        private Button[] _tabButtons;
        private KungfuTab _currentTab = KungfuTab.Kungfu;
        
        private Panel _wuXueSection;
        private Panel _xinFaSection;
        private Panel _qiShuSection;
        
        private Panel _currentSetupSection;
        private Button _characterDetailButton;
        private Button _styleGuideButton;
        private Button _upgradeGuideButton;
        
        private Button _backButton;
        private Button _weaponAppearanceButton;
        private Button _shareButton;
        private Button _schemeButton;
        
        private readonly string[] _tabNames = { "功法", "装备", "天赋", "E" };
        private readonly string[] _xinFaNames = { "斩", "退", "转", "守" };
        
        private bool _layoutInitialized = false;
        
        public event Action OnBackClicked;
        public event Action OnShareClicked;
        public event Action OnSchemeClicked;
        public event Action OnWeaponAppearanceClicked;
        public event Action OnCharacterDetailClicked;
        public event Action OnStyleGuideClicked;
        public event Action OnUpgradeGuideClicked;
        public event Action<KungfuTab> OnTabChanged;
        
        public KungfuSystemUI()
        {
            CreateCanvas();
        }
        
        private void CreateCanvas()
        {
            _canvas = UIHelper.CreateUICanvas("KungfuSystemUI");
            _canvas.RenderMode = CanvasRenderMode.ScreenSpace;
            _canvas.Order = 100;
            _canvas.ReceivesEvents = true;
            
            _canvas.GUI.AnchorPreset = AnchorPresets.StretchAll;
            _canvas.GUI.Pivot = new Float2(0.5f, 0.5f);
            _canvas.GUI.Offsets = Margin.Zero;
            _canvas.GUI.Size = Screen.Size;
            _canvas.GUI.BackgroundColor = KungfuTheme.Colors.BackgroundPrimary;
            
            Parent = _canvas.GUI;
        }
        
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            
            if (!_layoutInitialized && Width > 0 && Height > 0)
            {
                _layoutInitialized = true;
                CreateUI();
            }
        }
        
        private void CreateUI()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = new Margin(0, 0, 0, 0);
            
            CreateTopBar();
            CreateMainLayout();
            CreateBottomBar();
            UpdateTabSelection();
        }
        
        private void CreateTopBar()
        {
            _topBar = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(0, 0, 0, Height - KungfuTheme.Sizes.TopBarHeight),
                Height = KungfuTheme.Sizes.TopBarHeight,
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            _searchButton = new Button
            {
                Parent = _topBar,
                Size = new Float2(40, 40),
                Location = new Float2(20, 5),
                Text = "Q",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont(),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            _tabButtons = new Button[_tabNames.Length];
            float tabStartX = 80;
            float tabWidth = 100;
            
            for (int i = 0; i < _tabNames.Length; i++)
            {
                var tab = (KungfuTab)i;
                _tabButtons[i] = new Button
                {
                    Parent = _topBar,
                    Location = new Float2(tabStartX + i * tabWidth, 8),
                    Size = new Float2(tabWidth, 34),
                    Text = _tabNames[i],
                    TextColor = i == 0 ? KungfuTheme.Colors.Accent : KungfuTheme.Colors.TextSecondary,
                    Font = KungfuTheme.Fonts.GetBodyFont(),
                    BackgroundColor = i == 0 ? KungfuTheme.Colors.BackgroundPrimary : Color.Transparent,
                    Tag = i
                };
                _tabButtons[i].Clicked += () => SelectTab(tab);
            }
        }
        
        private void CreateMainLayout()
        {
            float leftPanelWidth = 320;
            float rightPanelWidth = 260;
            float topBarHeight = KungfuTheme.Sizes.TopBarHeight;
            float bottomBarHeight = KungfuTheme.Sizes.BottomBarHeight;
            
            _leftPanel = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Offsets = new Margin(0, topBarHeight, 0, bottomBarHeight),
                Width = leftPanelWidth,
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            _centerPanel = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(leftPanelWidth, topBarHeight, rightPanelWidth, bottomBarHeight),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            _rightPanel = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.VerticalStretchRight,
                Offsets = new Margin(0, topBarHeight, rightPanelWidth, bottomBarHeight),
                Width = rightPanelWidth,
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            CreateLeftPanel();
            CreateRightPanel();
            CreateCenterPanel();
        }
        
        private void CreateLeftPanel()
        {
            float padding = KungfuTheme.Sizes.Padding;
            float sectionSpacing = 24;
            
            var wuXueLabel = new Label
            {
                Parent = _leftPanel,
                Location = new Float2(padding, padding),
                Size = new Float2(200, 30),
                Text = "武学",
                TextColor = KungfuTheme.Colors.TextPrimary,
                Font = KungfuTheme.Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Near
            };
            
            _wuXueSection = new Panel
            {
                Parent = _leftPanel,
                Location = new Float2(padding, 60),
                Size = new Float2(_leftPanel.Width - padding * 2, 190),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            CreateKungfuItems();
            
            var xinFaLabel = new Label
            {
                Parent = _leftPanel,
                Location = new Float2(padding, 270),
                Size = new Float2(200, 30),
                Text = "心法",
                TextColor = KungfuTheme.Colors.TextPrimary,
                Font = KungfuTheme.Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Near
            };
            
            _xinFaSection = new Panel
            {
                Parent = _leftPanel,
                Location = new Float2(padding, 300),
                Size = new Float2(_leftPanel.Width - padding * 2, 110),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            CreateXinFaItems();
            
            var qiShuLabel = new Label
            {
                Parent = _leftPanel,
                Location = new Float2(padding, 430),
                Size = new Float2(200, 30),
                Text = "奇术",
                TextColor = KungfuTheme.Colors.TextPrimary,
                Font = KungfuTheme.Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Near
            };
            
            _qiShuSection = new Panel
            {
                Parent = _leftPanel,
                Location = new Float2(padding, 460),
                Size = new Float2(_leftPanel.Width - padding * 2, 110),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            CreateQiShuItems();
        }
        
        private void CreateCenterPanel()
        {
            var previewPanel = new Panel
            {
                Parent = _centerPanel,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(24, 24, 24, 24),
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            var previewLabel = new Label
            {
                Parent = previewPanel,
                AnchorPreset = AnchorPresets.MiddleCenter,
                Size = new Float2(200, 40),
                Text = "角色预览",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Center
            };
            
            var statsPanel = new Panel
            {
                Parent = previewPanel,
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Offsets = new Margin(20, 0, 20, 20),
                Height = 80,
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            var powerLabel = new Label
            {
                Parent = statsPanel,
                Location = new Float2(20, 15),
                Size = new Float2(100, 20),
                Text = "战力:",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont()
            };
            
            var powerValue = new Label
            {
                Parent = statsPanel,
                Location = new Float2(80, 15),
                Size = new Float2(100, 20),
                Text = "22149",
                TextColor = KungfuTheme.Colors.Accent,
                Font = KungfuTheme.Fonts.GetTitleFont()
            };
        }
        
        private void CreateKungfuItems()
        {
            float itemHeight = 65;
            float spacing = 12;
            
            var gunFaItem = new Panel
            {
                Parent = _wuXueSection,
                Location = new Float2(12, 12),
                Size = new Float2(_wuXueSection.Width - 24, itemHeight),
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            var gunFaIcon = new Panel
            {
                Parent = gunFaItem,
                Location = new Float2(12, 12),
                Size = new Float2(40, 40),
                BackgroundColor = new Color(0.1f, 0.1f, 0.4f, 0.8f)
            };
            
            var gunFaTextPanel = new Panel
            {
                Parent = gunFaItem,
                Location = new Float2(64, 12),
                Size = new Float2(120, 40),
                BackgroundColor = Color.Transparent
            };
            
            var gunFaName = new Label
            {
                Parent = gunFaTextPanel,
                Location = new Float2(0, 0),
                Size = new Float2(120, 20),
                Text = "无名枪法",
                TextColor = KungfuTheme.Colors.TextPrimary,
                Font = KungfuTheme.Fonts.GetBodyFont()
            };
            
            var gunFaLevel = new Label
            {
                Parent = gunFaTextPanel,
                Location = new Float2(0, 20),
                Size = new Float2(120, 20),
                Text = "1/20级",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetSmallFont()
            };
            
            var gunFaWeapon = new Panel
            {
                Parent = gunFaItem,
                Location = new Float2(_wuXueSection.Width - 84, 12),
                Size = new Float2(56, 40),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            var jianFaItem = new Panel
            {
                Parent = _wuXueSection,
                Location = new Float2(12, itemHeight + spacing + 12),
                Size = new Float2(_wuXueSection.Width - 24, itemHeight),
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            var jianFaIcon = new Panel
            {
                Parent = jianFaItem,
                Location = new Float2(12, 12),
                Size = new Float2(40, 40),
                BackgroundColor = new Color(0.4f, 0.1f, 0.1f, 0.8f)
            };
            
            var jianFaTextPanel = new Panel
            {
                Parent = jianFaItem,
                Location = new Float2(64, 12),
                Size = new Float2(120, 40),
                BackgroundColor = Color.Transparent
            };
            
            var jianFaName = new Label
            {
                Parent = jianFaTextPanel,
                Location = new Float2(0, 0),
                Size = new Float2(120, 20),
                Text = "无名剑法",
                TextColor = KungfuTheme.Colors.TextPrimary,
                Font = KungfuTheme.Fonts.GetBodyFont()
            };
            
            var jianFaLevel = new Label
            {
                Parent = jianFaTextPanel,
                Location = new Float2(0, 20),
                Size = new Float2(120, 20),
                Text = "1/20级",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetSmallFont()
            };
            
            var jianFaWeapon = new Panel
            {
                Parent = jianFaItem,
                Location = new Float2(_wuXueSection.Width - 84, 12),
                Size = new Float2(56, 40),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
        }
        
        private void CreateXinFaItems()
        {
            float itemWidth = (_xinFaSection.Width - 56) / 4;
            float itemHeight = 65;
            
            for (int i = 0; i < _xinFaNames.Length; i++)
            {
                var xinFaItem = new Panel
                {
                    Parent = _xinFaSection,
                    Location = new Float2(12 + i * (itemWidth + 12), 12),
                    Size = new Float2(itemWidth, itemHeight),
                    BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
                };
                
                var xinFaIcon = new Label
                {
                    Parent = xinFaItem,
                    AnchorPreset = AnchorPresets.MiddleCenter,
                    Size = new Float2(itemWidth - 20, 30),
                    Text = _xinFaNames[i],
                    TextColor = KungfuTheme.Colors.TextPrimary,
                    Font = KungfuTheme.Fonts.GetBodyFont(),
                    HorizontalAlignment = TextAlignment.Center
                };
            }
        }
        
        private void CreateQiShuItems()
        {
            var qiShuContent = new Panel
            {
                Parent = _qiShuSection,
                Location = new Float2(12, 12),
                Size = new Float2(_qiShuSection.Width - 24, _qiShuSection.Height - 24),
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            var qiShuIcons = new Label
            {
                Parent = qiShuContent,
                AnchorPreset = AnchorPresets.MiddleCenter,
                Size = new Float2(200, 40),
                Text = "✦ ✧ ✣ ✤",
                TextColor = KungfuTheme.Colors.Accent,
                Font = KungfuTheme.Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Center
            };
        }
        
        private void CreateRightPanel()
        {
            float padding = KungfuTheme.Sizes.Padding;
            
            var setupLabel = new Label
            {
                Parent = _rightPanel,
                Location = new Float2(padding, padding),
                Size = new Float2(150, 30),
                Text = "当前搭配",
                TextColor = KungfuTheme.Colors.TextPrimary,
                Font = KungfuTheme.Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Near
            };
            
            var progressPanel = new Panel
            {
                Parent = _rightPanel,
                Location = new Float2(padding, 60),
                Size = new Float2(_rightPanel.Width - padding * 2, 50),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            var progressText = new Label
            {
                Parent = progressPanel,
                Location = new Float2(12, 12),
                Size = new Float2(100, 24),
                Text = "战力",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont()
            };
            
            var progressValue = new Label
            {
                Parent = progressPanel,
                Location = new Float2(12, 32),
                Size = new Float2(100, 24),
                Text = "22149",
                TextColor = KungfuTheme.Colors.Accent,
                Font = KungfuTheme.Fonts.GetTitleFont()
            };
            
            _currentSetupSection = new Panel
            {
                Parent = _rightPanel,
                Location = new Float2(padding, 128),
                Size = new Float2(_rightPanel.Width - padding * 2, 140),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            
            _characterDetailButton = new Button
            {
                Parent = _currentSetupSection,
                Location = new Float2(12, 12),
                Size = new Float2(_currentSetupSection.Width - 24, 36),
                Text = "角色详情 >",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont(),
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            _characterDetailButton.Clicked += () => OnCharacterDetailClicked?.Invoke();
            
            _styleGuideButton = new Button
            {
                Parent = _currentSetupSection,
                Location = new Float2(12, 56),
                Size = new Float2(_currentSetupSection.Width - 24, 36),
                Text = "流派指引 >",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont(),
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            _styleGuideButton.Clicked += () => OnStyleGuideClicked?.Invoke();
            
            _upgradeGuideButton = new Button
            {
                Parent = _currentSetupSection,
                Location = new Float2(12, 100),
                Size = new Float2(_currentSetupSection.Width - 24, 36),
                Text = "提升指南 >",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont(),
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            _upgradeGuideButton.Clicked += () => OnUpgradeGuideClicked?.Invoke();
        }
        
        private void CreateBottomBar()
        {
            _bottomBar = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchBottom,
                Offsets = new Margin(0, 0, Height - KungfuTheme.Sizes.BottomBarHeight, 0),
                Height = KungfuTheme.Sizes.BottomBarHeight,
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            _backButton = new Button
            {
                Parent = _bottomBar,
                Location = new Float2(24, 16),
                Size = new Float2(88, 36),
                Text = "返回",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont(),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            _backButton.Clicked += () => OnBackClicked?.Invoke();
            
            _weaponAppearanceButton = new Button
            {
                Parent = _bottomBar,
                Location = new Float2(124, 16),
                Size = new Float2(112, 36),
                Text = "武器外观",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont(),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            _weaponAppearanceButton.Clicked += () => OnWeaponAppearanceClicked?.Invoke();
            
            _shareButton = new Button
            {
                Parent = _bottomBar,
                Location = new Float2(248, 16),
                Size = new Float2(88, 36),
                Text = "分享",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont(),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            _shareButton.Clicked += () => OnShareClicked?.Invoke();
            
            _schemeButton = new Button
            {
                Parent = _bottomBar,
                Location = new Float2(_bottomBar.Width - 112, 16),
                Size = new Float2(88, 36),
                Text = "方案管理",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetBodyFont(),
                BackgroundColor = KungfuTheme.Colors.BackgroundPrimary
            };
            _schemeButton.Clicked += () => OnSchemeClicked?.Invoke();
        }
        
        public void SelectTab(KungfuTab tab)
        {
            _currentTab = tab;
            UpdateTabSelection();
            OnTabChanged?.Invoke(tab);
        }
        
        private void UpdateTabSelection()
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                bool isSelected = (int)_currentTab == i;
                _tabButtons[i].TextColor = isSelected ? KungfuTheme.Colors.Accent : KungfuTheme.Colors.TextSecondary;
                _tabButtons[i].BackgroundColor = isSelected ? KungfuTheme.Colors.BackgroundPrimary : Color.Transparent;
            }
        }
        
        public void Close()
        {
            UICanvas.Destroy(_canvas);
            Actor.Destroy(_canvas.Parent);
            Dispose();
        }
    }
}