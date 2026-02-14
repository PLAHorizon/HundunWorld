using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    public class PresetManagerPanel : ContainerControl
    {
        public event Action<string> OnPresetSelected;
        public event Action<string> OnPresetSaved;
        public event Action<string> OnPresetDeleted;
        public event Action<string> OnSaveRequested;
        public event Action<string> OnQuickPresetSelected;
        
        private Panel _scrollContent;
        private TextBox _presetNameInput;
        private Dropdown _categoryDropdown;
        private List<Panel> _presetItems = new List<Panel>();
        
        private const float ItemSpacing = MetaHumanStyles.Sizes.ItemSpacing;
        private const float GroupSpacing = MetaHumanStyles.Sizes.GroupSpacing;
        private const float Padding = MetaHumanStyles.Sizes.Padding;
        
        public PresetManagerPanel()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = Color.Transparent;
            CreateUI();
        }
        
        private void CreateUI()
        {
            float y = 0;
            
            y = CreateSectionHeader("创建预设", y);
            y = CreatePresetCreationControls(y);
            y = CreateSeparator(y);
            
            y = CreateSectionHeader("预设分类", y);
            y = CreateCategoryFilter(y);
            y = CreateSeparator(y);
            
            y = CreateSectionHeader("我的预设", y);
            
            var scrollPanel = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Y = y,
                BackgroundColor = Color.Transparent,
                ScrollBars = ScrollBars.Vertical
            };
            
            _scrollContent = new Panel
            {
                Parent = scrollPanel,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                BackgroundColor = Color.Transparent,
                Height = 600
            };
            
            RefreshPresetList();
        }
        
        private float CreateSectionHeader(string title, float y)
        {
            var headerContainer = new Panel
            {
                Parent = this,
                X = 0,
                Y = y,
                Width = Width,
                Height = 32,
                BackgroundColor = MetaHumanStyles.Colors.SectionHeaderBackground
            };
            
            var leftBorder = new Panel
            {
                Parent = headerContainer,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Width = 3,
                BackgroundColor = MetaHumanStyles.Colors.Success
            };
            
            var headerLabel = new Label
            {
                Parent = headerContainer,
                Text = title,
                X = 12,
                Y = 0,
                Width = headerContainer.Width - 16,
                Height = headerContainer.Height,
                TextColor = MetaHumanStyles.Colors.SectionHeader,
                VerticalAlignment = TextAlignment.Center,
                HorizontalAlignment = TextAlignment.Near
            };
            
            return y + headerContainer.Height + ItemSpacing;
        }
        
        private float CreateSeparator(float y)
        {
            var separator = new Panel
            {
                Parent = this,
                X = Padding,
                Y = y,
                Width = Width - Padding * 2,
                Height = 1,
                BackgroundColor = MetaHumanStyles.Colors.Separator
            };
            
            return y + separator.Height + GroupSpacing;
        }
        
        private float CreatePresetCreationControls(float y)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            float inputWidth = Width - Padding * 2;
            
            var inputContainer = new Panel
            {
                Parent = this,
                X = Padding,
                Y = y,
                Width = inputWidth,
                Height = rowHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            _presetNameInput = new TextBox
            {
                Parent = inputContainer,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(8, 8, 4, 4),
                WatermarkText = "输入预设名称...",
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            
            y += rowHeight + ItemSpacing;
            
            var saveButton = new Button
            {
                Parent = this,
                X = Padding,
                Y = y,
                Width = inputWidth,
                Height = MetaHumanStyles.Sizes.ButtonHeight,
                Text = "保存当前预设",
                BackgroundColor = MetaHumanStyles.Colors.Success,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            saveButton.Clicked += OnSavePresetClicked;
            
            return y + MetaHumanStyles.Sizes.ButtonHeight + GroupSpacing;
        }
        
        private float CreateCategoryFilter(float y)
        {
            float rowHeight = MetaHumanStyles.Sizes.RowHeight;
            float inputWidth = Width - Padding * 2;
            
            var dropdownContainer = new Panel
            {
                Parent = this,
                X = Padding,
                Y = y,
                Width = inputWidth,
                Height = rowHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            var label = new Label
            {
                Parent = dropdownContainer,
                Text = "分类:",
                X = 8,
                Y = 0,
                Width = 60,
                Height = rowHeight,
                TextColor = MetaHumanStyles.Colors.TextSecondary,
                VerticalAlignment = TextAlignment.Center
            };
            
            _categoryDropdown = new Dropdown
            {
                Parent = dropdownContainer,
                X = 70,
                Y = 4,
                Width = dropdownContainer.Width - 78,
                Height = rowHeight - 8,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundDark,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            
            _categoryDropdown.AddItem("全部");
            _categoryDropdown.AddItem("皮肤");
            _categoryDropdown.AddItem("眼睛");
            _categoryDropdown.AddItem("头发");
            _categoryDropdown.AddItem("身体");
            _categoryDropdown.SelectedIndex = 0;
            _categoryDropdown.SelectedIndexChanged += OnCategoryChanged;
            
            return y + rowHeight + ItemSpacing;
        }
        
        private void RefreshPresetList()
        {
            foreach (var item in _presetItems)
            {
                item.Dispose();
            }
            _presetItems.Clear();
            
            float y = 0;
            
            y = CreatePresetItem("默认女性", "皮肤", y, false);
            y = CreatePresetItem("默认男性", "皮肤", y, false);
            y = CreatePresetItem("亚洲女性", "皮肤", y, false);
            y = CreatePresetItem("欧洲男性", "皮肤", y, false);
            y = CreatePresetItem("自定义角色1", "头发", y, true);
            y = CreatePresetItem("自定义角色2", "眼睛", y, true);
            
            _scrollContent.Height = y + Padding;
        }
        
        private float CreatePresetItem(string name, string category, float y, bool canDelete)
        {
            float itemHeight = 48;
            float inputWidth = _scrollContent.Width - Padding * 2;
            
            var itemContainer = new Panel
            {
                Parent = _scrollContent,
                X = Padding,
                Y = y,
                Width = inputWidth,
                Height = itemHeight,
                BackgroundColor = MetaHumanStyles.Colors.BackgroundLight
            };
            
            var colorIndicator = new Panel
            {
                Parent = itemContainer,
                X = 4,
                Y = 4,
                Width = 4,
                Height = itemHeight - 8,
                BackgroundColor = GetCategoryColor(category)
            };
            
            var nameLabel = new Label
            {
                Parent = itemContainer,
                Text = name,
                X = 16,
                Y = 4,
                Width = itemContainer.Width - 100,
                Height = 24,
                TextColor = MetaHumanStyles.Colors.TextPrimary,
                VerticalAlignment = TextAlignment.Center
            };
            
            var categoryLabel = new Label
            {
                Parent = itemContainer,
                Text = category,
                X = 16,
                Y = 26,
                Width = itemContainer.Width - 100,
                Height = 18,
                TextColor = MetaHumanStyles.Colors.TextMuted,
                VerticalAlignment = TextAlignment.Center
            };
            
            var loadButton = new Button
            {
                Parent = itemContainer,
                Text = "加载",
                X = itemContainer.Width - 70 - (canDelete ? 50 : 0),
                Y = (itemHeight - 28) / 2,
                Width = 60,
                Height = 28,
                BackgroundColor = MetaHumanStyles.Colors.Primary,
                TextColor = MetaHumanStyles.Colors.TextPrimary
            };
            loadButton.Clicked += () => OnPresetSelected?.Invoke(name);
            
            if (canDelete)
            {
                var deleteButton = new Button
                {
                    Parent = itemContainer,
                    Text = "×",
                    X = itemContainer.Width - 30,
                    Y = (itemHeight - 28) / 2,
                    Width = 28,
                    Height = 28,
                    BackgroundColor = MetaHumanStyles.Colors.Error,
                    TextColor = MetaHumanStyles.Colors.TextPrimary
                };
                deleteButton.Clicked += () => OnPresetDeleted?.Invoke(name);
            }
            
            _presetItems.Add(itemContainer);
            
            return y + itemHeight + ItemSpacing;
        }
        
        private Color GetCategoryColor(string category)
        {
            switch (category)
            {
                case "皮肤": return MetaHumanStyles.Colors.Primary;
                case "眼睛": return MetaHumanStyles.Colors.Accent;
                case "头发": return MetaHumanStyles.Colors.Warning;
                case "身体": return MetaHumanStyles.Colors.Success;
                default: return MetaHumanStyles.Colors.TextMuted;
            }
        }
        
        private void OnSavePresetClicked()
        {
            string presetName = _presetNameInput.Text;
            if (!string.IsNullOrEmpty(presetName))
            {
                OnSaveRequested?.Invoke(presetName);
                OnPresetSaved?.Invoke(presetName);
                _presetNameInput.Text = "";
                RefreshPresetList();
            }
        }
        
        private void OnCategoryChanged(Dropdown dropdown)
        {
            RefreshPresetList();
        }
        
        public void AddPreset(string name, string category)
        {
            RefreshPresetList();
        }
        
        public void RemovePreset(string name)
        {
            RefreshPresetList();
        }
    }
}
