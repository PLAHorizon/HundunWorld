using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Components
{
    public class KungfuCard : ContainerControl
    {
        private Panel _cardPanel;
        private Panel _iconPanel;
        private Label _nameLabel;
        private Label _levelLabel;
        private Label _progressLabel;
        private ProgressBar _progressBar;
        private Button _detailButton;
        private Button _guideButton;
        private Button _upgradeButton;
        
        private bool _uiCreated = false;
        
        public string KungfuName
        {
            get => _nameLabel?.Text ?? string.Empty;
            set
            {
                if (_nameLabel != null)
                    _nameLabel.Text = value;
            }
        }
        
        public int CurrentLevel { get; set; } = 1;
        public int MaxLevel { get; set; } = 20;
        public int CurrentProgress { get; set; } = 0;
        public int MaxProgress { get; set; } = 100;
        
        public event Action OnDetailClicked;
        public event Action OnGuideClicked;
        public event Action OnUpgradeClicked;
        
        public KungfuCard(Float2 size)
        {
            Size = size;
            BackgroundColor = Color.Transparent;
        }
        
        public void Initialize()
        {
            if (!_uiCreated)
            {
                _uiCreated = true;
                CreateUI();
            }
        }
        
        private void CreateUI()
        {
            _cardPanel = new Panel
            {
                Parent = this,
                AnchorMin = Float2.Zero,
                AnchorMax = Float2.One,
                Offsets = Margin.Zero,
                BackgroundColor = KungfuTheme.Colors.BackgroundSecondary
            };
            
            _iconPanel = new Panel
            {
                Parent = _cardPanel,
                AnchorMin = new Float2(0, 0.5f),
                AnchorMax = new Float2(0, 0.5f),
                Pivot = new Float2(0, 0.5f),
                Size = new Float2(KungfuTheme.Sizes.IconSize, KungfuTheme.Sizes.IconSize),
                Location = new Float2(KungfuTheme.Sizes.Padding, 0),
                BackgroundColor = KungfuTheme.Colors.ButtonBackground
            };
            
            var iconLabel = new Label
            {
                Parent = _iconPanel,
                AnchorMin = Float2.Zero,
                AnchorMax = Float2.One,
                Offsets = Margin.Zero,
                Text = "功",
                TextColor = KungfuTheme.Colors.Accent,
                Font = KungfuTheme.Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            
            float contentStartX = KungfuTheme.Sizes.IconSize + KungfuTheme.Sizes.Padding * 2;
            float contentWidth = Width - contentStartX - KungfuTheme.Sizes.Padding;
            
            _nameLabel = new Label
            {
                Parent = _cardPanel,
                AnchorMin = new Float2(0, 0),
                AnchorMax = new Float2(1, 0),
                Pivot = new Float2(0, 0),
                Size = new Float2(contentWidth, 24),
                Location = new Float2(contentStartX, KungfuTheme.Sizes.Padding),
                Text = "无名枪法",
                TextColor = KungfuTheme.Colors.TextPrimary,
                Font = KungfuTheme.Fonts.GetTitleFont(),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            
            _levelLabel = new Label
            {
                Parent = _cardPanel,
                AnchorMin = new Float2(0, 0),
                AnchorMax = new Float2(1, 0),
                Pivot = new Float2(0, 0),
                Size = new Float2(80, 20),
                Location = new Float2(contentStartX, KungfuTheme.Sizes.Padding + 28),
                Text = "1/20级",
                TextColor = KungfuTheme.Colors.TextSecondary,
                Font = KungfuTheme.Fonts.GetSmallFont(),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            
            _progressLabel = new Label
            {
                Parent = _cardPanel,
                AnchorMin = new Float2(0, 0),
                AnchorMax = new Float2(1, 0),
                Pivot = new Float2(0, 0),
                Size = new Float2(contentWidth - 85, 20),
                Location = new Float2(contentStartX + 85, KungfuTheme.Sizes.Padding + 28),
                Text = "/22149目",
                TextColor = KungfuTheme.Colors.Accent,
                Font = KungfuTheme.Fonts.GetSmallFont(),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            
            _progressBar = new ProgressBar
            {
                Parent = _cardPanel,
                AnchorMin = new Float2(0, 0),
                AnchorMax = new Float2(1, 0),
                Pivot = new Float2(0, 0),
                Size = new Float2(contentWidth, 6),
                Location = new Float2(contentStartX, KungfuTheme.Sizes.Padding + 52),
                BackgroundColor = KungfuTheme.Colors.ProgressBackground,
                Value = 0,
                Minimum = 0,
                Maximum = 100
            };
            
            float buttonY = KungfuTheme.Sizes.Padding + 68;
            float buttonWidth = (contentWidth - KungfuTheme.Sizes.PaddingSmall * 2) / 3;
            
            _detailButton = CreateLinkButton("角色详情", contentStartX, buttonY, buttonWidth);
            _detailButton.Parent = _cardPanel;
            _detailButton.Clicked += () => OnDetailClicked?.Invoke();
            
            _guideButton = CreateLinkButton("流派指引", contentStartX + buttonWidth + KungfuTheme.Sizes.PaddingSmall, buttonY, buttonWidth);
            _guideButton.Parent = _cardPanel;
            _guideButton.Clicked += () => OnGuideClicked?.Invoke();
            
            _upgradeButton = CreateLinkButton("提升指南", contentStartX + (buttonWidth + KungfuTheme.Sizes.PaddingSmall) * 2, buttonY, buttonWidth);
            _upgradeButton.Parent = _cardPanel;
            _upgradeButton.Clicked += () => OnUpgradeClicked?.Invoke();
        }
        
        private Button CreateLinkButton(string text, float x, float y, float width)
        {
            var button = new Button
            {
                AnchorMin = new Float2(0, 0),
                AnchorMax = new Float2(1, 0),
                Pivot = new Float2(0, 0),
                Size = new Float2(width, 28),
                Location = new Float2(x, y),
                Text = text + ">",
                TextColor = KungfuTheme.Colors.Accent,
                Font = KungfuTheme.Fonts.GetSmallFont(),
                BackgroundColor = Color.Transparent,
                HorizontalAlignment = TextAlignment.Near
            };
            return button;
        }
        
        public void SetData(string name, int currentLevel, int maxLevel, int currentProgress, int maxProgress)
        {
            KungfuName = name;
            CurrentLevel = currentLevel;
            MaxLevel = maxLevel;
            CurrentProgress = currentProgress;
            MaxProgress = maxProgress;
            
            UpdateDisplay();
        }
        
        public void UpdateDisplay()
        {
            if (_nameLabel == null) return;
            
            _nameLabel.Text = KungfuName;
            _levelLabel.Text = $"{CurrentLevel}/{MaxLevel}级";
            _progressLabel.Text = $"/{CurrentProgress}目";
            
            if (MaxProgress > 0)
            {
                _progressBar.Value = (float)CurrentProgress / MaxProgress * 100;
            }
        }
        
        public void ShowGuideButton(bool show)
        {
            if (_guideButton != null)
                _guideButton.Visible = show;
        }
        
        public void ShowUpgradeButton(bool show)
        {
            if (_upgradeButton != null)
                _upgradeButton.Visible = show;
        }
    }
}
