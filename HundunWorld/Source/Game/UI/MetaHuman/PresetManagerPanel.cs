using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.UI.MetaHuman
{
    /// <summary>
    /// 预设管理面板
    /// 提供预设的加载、保存、删除和快速选择功能
    /// </summary>
    public class PresetManagerPanel : Panel
    {
        // 预设目录
        private const string PresetDirectory = "Content/Presets/Characters";
        
        // UI控件
        private DropdownMenu _presetDropdown;
        private Button _loadButton;
        private Button _saveButton;
        private Button _deleteButton;
        private Button _resetButton;
        private TextBox _presetNameInput;
        
        // 快速预设按钮容器
        private HorizontalPanel _quickPresetBar;
        
        // 预设列表
        private List<string> _presetFiles = new List<string>();
        private string _selectedPresetPath;
        
        // 事件
        public event Action<string> OnPresetSelected;
        public event Action<string> OnSaveRequested;
        public event Action<string> OnQuickPresetSelected;
        public event Action OnResetRequested;
        
        public PresetManagerPanel()
        {
            BackgroundColor = new Color(0.18f, 0.18f, 0.2f, 1.0f);
        }
        
        /// <inheritdoc/>
        public override void OnParentResized()
        {
            base.OnParentResized();
            CreateUI();
        }
        
        /// <summary>
        /// 创建UI
        /// </summary>
        private void CreateUI()
        {
            DisposeChildren();
            
            float currentX = 10;
            float buttonY = 10;
            float buttonHeight = 30;
            
            // 预设下拉框标签
            var presetLabel = new Label
            {
                Parent = this,
                Text = "预设:",
                X = currentX,
                Y = buttonY,
                Width = 40,
                Height = buttonHeight,
                TextColor = Color.White,
                VerticalAlignment = TextAlignment.Center
            };
            currentX += 45;
            
            // 预设下拉框
            _presetDropdown = new DropdownMenu
            {
                Parent = this,
                X = currentX,
                Y = buttonY,
                Width = 150,
                Height = buttonHeight
            };
            _presetDropdown.SelectedIndexChanged += OnPresetDropdownChanged;
            currentX += 160;
            
            // 加载按钮
            _loadButton = new Button
            {
                Parent = this,
                Text = "加载",
                X = currentX,
                Y = buttonY,
                Width = 50,
                Height = buttonHeight,
                BackgroundColor = new Color(0.3f, 0.5f, 0.3f)
            };
            _loadButton.Clicked += LoadSelectedPreset;
            currentX += 60;
            
            // 保存名称输入
            _presetNameInput = new TextBox
            {
                Parent = this,
                X = currentX,
                Y = buttonY,
                Width = 120,
                Height = buttonHeight,
                WatermarkText = "预设名称..."
            };
            currentX += 130;
            
            // 保存按钮
            _saveButton = new Button
            {
                Parent = this,
                Text = "保存",
                X = currentX,
                Y = buttonY,
                Width = 50,
                Height = buttonHeight,
                BackgroundColor = new Color(0.3f, 0.4f, 0.6f)
            };
            _saveButton.Clicked += SaveCurrentPreset;
            currentX += 60;
            
            // 重置按钮
            _resetButton = new Button
            {
                Parent = this,
                Text = "重置",
                X = currentX,
                Y = buttonY,
                Width = 50,
                Height = buttonHeight,
                BackgroundColor = new Color(0.5f, 0.4f, 0.3f)
            };
            _resetButton.Clicked += () => OnResetRequested?.Invoke();
            
            // 刷新预设列表
            RefreshPresetList();
        }
        
        /// <summary>
        /// 刷新预设列表
        /// </summary>
        public void RefreshPresetList()
        {
            _presetFiles.Clear();
            _presetDropdown?.ClearItems();
            
            // 添加内置预设
            AddBuiltInPresets();
            
            // 扫描用户预设目录
            string fullPath = Path.Combine(Globals.ProjectContentFolder, "Presets/Characters");
            if (Directory.Exists(fullPath))
            {
                var files = Directory.GetFiles(fullPath, "*.json");
                foreach (var file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    _presetFiles.Add(file);
                    _presetDropdown?.AddItem(fileName);
                }
            }
        }
        
        /// <summary>
        /// 添加内置预设选项
        /// </summary>
        private void AddBuiltInPresets()
        {
            var builtInPresets = new[]
            {
                ("默认", "default"),
                ("亚洲面孔", "asian"),
                ("欧洲面孔", "european"),
                ("年轻", "young"),
                ("成熟", "mature")
            };
            
            foreach (var (name, id) in builtInPresets)
            {
                _presetFiles.Add($"builtin:{id}");
                _presetDropdown?.AddItem($"[内置] {name}");
            }
        }
        
        /// <summary>
        /// 预设下拉框选择变化
        /// </summary>
        private void OnPresetDropdownChanged(DropdownMenu menu)
        {
            int index = menu.SelectedIndex;
            if (index >= 0 && index < _presetFiles.Count)
            {
                _selectedPresetPath = _presetFiles[index];
            }
        }
        
        /// <summary>
        /// 加载选中的预设
        /// </summary>
        private void LoadSelectedPreset()
        {
            if (string.IsNullOrEmpty(_selectedPresetPath))
            {
                Debug.LogWarning("未选择预设");
                return;
            }
            
            // 检查是否为内置预设
            if (_selectedPresetPath.StartsWith("builtin:"))
            {
                string presetId = _selectedPresetPath.Substring(8);
                OnQuickPresetSelected?.Invoke(presetId);
            }
            else
            {
                OnPresetSelected?.Invoke(_selectedPresetPath);
            }
        }
        
        /// <summary>
        /// 保存当前预设
        /// </summary>
        private void SaveCurrentPreset()
        {
            string presetName = _presetNameInput?.Text?.Trim();
            
            if (string.IsNullOrEmpty(presetName))
            {
                Debug.LogWarning("请输入预设名称");
                ShowMessage("请输入预设名称");
                return;
            }
            
            // 验证文件名
            if (!IsValidFileName(presetName))
            {
                Debug.LogWarning("预设名称包含非法字符");
                ShowMessage("预设名称包含非法字符");
                return;
            }
            
            OnSaveRequested?.Invoke(presetName);
            
            // 刷新列表
            RefreshPresetList();
            
            // 清空输入框
            if (_presetNameInput != null)
            {
                _presetNameInput.Text = "";
            }
            
            ShowMessage($"预设 '{presetName}' 已保存");
        }
        
        /// <summary>
        /// 验证文件名合法性
        /// </summary>
        private bool IsValidFileName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return name.IndexOfAny(invalidChars) < 0;
        }
        
        /// <summary>
        /// 显示消息提示
        /// </summary>
        private void ShowMessage(string message)
        {
            // 简单的消息显示，可以扩展为Toast提示
            Debug.Log(message);
        }
        
        /// <summary>
        /// 设置当前选中的预设
        /// </summary>
        public void SetSelectedPreset(string presetPath)
        {
            int index = _presetFiles.IndexOf(presetPath);
            if (index >= 0 && _presetDropdown != null)
            {
                _presetDropdown.SelectedIndex = index;
            }
        }
        
        /// <summary>
        /// 获取预设保存路径
        /// </summary>
        public static string GetPresetSavePath(string presetName)
        {
            string directory = Path.Combine(Globals.ProjectContentFolder, "Presets/Characters");
            
            // 确保目录存在
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            return Path.Combine(directory, $"{presetName}.json");
        }
    }
    
    /// <summary>
    /// 简化版下拉菜单控件
    /// </summary>
    public class DropdownMenu : ContainerControl
    {
        private Button _mainButton;
        private Panel _dropdownPanel;
        private List<string> _items = new List<string>();
        private int _selectedIndex = -1;
        private bool _isOpen;
        
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex != value && value >= -1 && value < _items.Count)
                {
                    _selectedIndex = value;
                    UpdateButtonText();
                    SelectedIndexChanged?.Invoke(this);
                }
            }
        }
        
        public string SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count 
            ? _items[_selectedIndex] : null;
        
        public event Action<DropdownMenu> SelectedIndexChanged;
        
        public DropdownMenu()
        {
            _mainButton = new Button
            {
                Parent = this,
                AnchorPreset = AnchorPresets.StretchAll,
                Text = "选择预设...",
                BackgroundColor = new Color(0.25f, 0.25f, 0.27f)
            };
            _mainButton.Clicked += ToggleDropdown;
        }
        
        /// <summary>
        /// 添加选项
        /// </summary>
        public void AddItem(string item)
        {
            _items.Add(item);
        }
        
        /// <summary>
        /// 清除所有选项
        /// </summary>
        public void ClearItems()
        {
            _items.Clear();
            _selectedIndex = -1;
            UpdateButtonText();
            CloseDropdown();
        }
        
        /// <summary>
        /// 切换下拉状态
        /// </summary>
        private void ToggleDropdown()
        {
            if (_isOpen)
            {
                CloseDropdown();
            }
            else
            {
                OpenDropdown();
            }
        }
        
        /// <summary>
        /// 打开下拉菜单
        /// </summary>
        private void OpenDropdown()
        {
            if (_items.Count == 0) return;
            
            _isOpen = true;
            
            // 创建下拉面板 - 使用Root作为父级
            ContainerControl parentControl = Root != null ? (ContainerControl)Root : this;
            _dropdownPanel = new Panel
            {
                Parent = parentControl,
                X = PointToWindow(Float2.Zero).X,
                Y = PointToWindow(Float2.Zero).Y + Height,
                Width = Width,
                Height = Math.Min(_items.Count * 25, 200),
                BackgroundColor = new Color(0.2f, 0.2f, 0.22f)
            };
            
            // 创建选项按钮
            for (int i = 0; i < _items.Count; i++)
            {
                int index = i;
                var itemButton = new Button
                {
                    Parent = _dropdownPanel,
                    X = 0,
                    Y = i * 25,
                    Width = Width,
                    Height = 25,
                    Text = _items[i],
                    BackgroundColor = i == _selectedIndex 
                        ? new Color(0.3f, 0.5f, 0.7f) 
                        : new Color(0.22f, 0.22f, 0.24f)
                };
                itemButton.Clicked += () => SelectItem(index);
            }
        }
        
        /// <summary>
        /// 关闭下拉菜单
        /// </summary>
        private void CloseDropdown()
        {
            _isOpen = false;
            _dropdownPanel?.Dispose();
            _dropdownPanel = null;
        }
        
        /// <summary>
        /// 选择选项
        /// </summary>
        private void SelectItem(int index)
        {
            SelectedIndex = index;
            CloseDropdown();
        }
        
        /// <summary>
        /// 更新按钮文本
        /// </summary>
        private void UpdateButtonText()
        {
            if (_mainButton != null)
            {
                _mainButton.Text = SelectedItem ?? "选择预设...";
            }
        }
        
        /// <inheritdoc/>
        public override void OnDestroy()
        {
            CloseDropdown();
            base.OnDestroy();
        }
    }
}
