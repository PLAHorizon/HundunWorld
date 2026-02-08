using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI;
using MemoryPack;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.Animation;
using HundunWorld.Game.UI.ErrorHandling;
using HundunWorld.Game.UI.Authentication;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 游戏主UI系统 - 重构版本
    /// 包含HUD、快捷栏、小地图、聊天窗口等游戏界面元素
    /// 集成状态管理、动画效果和错误处理
    /// </summary>
    public class GameMainUI : Script
    {
        // 核心管理器
        private UIStateManager _stateManager;
        private UIAnimationManager _animationManager;
        private ErrorHandlingManager _errorManager;
        private ToastManager _toastManager;
        
        // 主容器
        private ContainerControl _mainContainer;
        
        // HUD组件
        private RoundedPanel _hudPanel;
        private Label _playerNameLabel;
        private RoundedProgressBar _healthBar;
        private RoundedProgressBar _manaBar;
        private RoundedProgressBar _experienceBar;
        private Label _levelLabel;
        private Label _coordinatesLabel;
        
        // 快捷栏组件
        private RoundedPanel _hotbarPanel;
        private List<Button> _hotbarSlots;
        private const int HOTBAR_SLOT_COUNT = 10;
        
        // 小地图组件
        private RoundedPanel _minimapPanel;
        private Image _minimapImage;
        private Panel _minimapPlayerDot;
        
        // 聊天窗口组件
        private RoundedPanel _chatPanel;
        private ScrollableControl _chatScrollView;
        private TextBox _chatInput;
        private Button _chatSendButton;
        private List<Label> _chatMessages;
        private const int MAX_CHAT_MESSAGES = 50;
        
        // 菜单按钮组件
        private RoundedPanel _menuButtonsPanel;
        private Button _inventoryButton;
        private Button _characterButton;
        private Button _skillButton;
        private Button _questButton;
        private Button _settingsButton;
        private Button _logoutButton;
        
        // 状态信息
        private CharacterInfo _currentCharacter;
        private Vector3 _playerPosition;
        private float _currentHealth = 100f;
        private float _maxHealth = 100f;
        private float _currentMana = 50f;
        private float _maxMana = 50f;
        private int _currentExp = 0;
        private int _expToNextLevel = 1000;
        
        // 面板管理
        private readonly Dictionary<string, RoundedPanel> _panels = new Dictionary<string, RoundedPanel>();
        private string _activePanelName;
        
        public override void OnStart()
        {
            InitializeManagers();
            InitializeUI();
            InitializeData();
            SubscribeEvents();
            
            FlaxEngine.Debug.Log("游戏主界面重构版初始化完成");
        }
        
        /// <summary>
        /// 初始化管理器
        /// </summary>
        private void InitializeManagers()
        {
            _stateManager = UIStateManager.Instance;
            _animationManager = UIAnimationManager.Instance;
            _errorManager = ErrorHandlingManager.Instance;
            _toastManager = UIHelper.ToastManager;
        }
        
        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeEvents()
        {
            _stateManager.SceneChanged += OnSceneChanged;
            _stateManager.SelectedCharacterChanged += OnSelectedCharacterChanged;
        }
        
        /// <summary>
        /// 场景切换事件处理
        /// </summary>
        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            if (newScene == SceneType.GameWorld)
            {
                ShowGameMainUI();
            }
            else
            {
                HideGameMainUI();
            }
        }
        
        /// <summary>
        /// 选中角色变化事件处理
        /// </summary>
        private void OnSelectedCharacterChanged(CharacterInfo character)
        {
            _currentCharacter = character;
            UpdateHUD();
        }
        
        /// <summary>
        /// 初始化用户界面
        /// </summary>
        private void InitializeUI()
        {
            // 创建主容器
            _mainContainer = new ContainerControl
            {
                AnchorPreset = AnchorPresets.StretchAll,
                BackgroundColor = Color.Transparent
            };
            
            // 添加到GUI
            var gui = Actor.GetScript<UICanvas>();
            if (gui?.GUI != null)
            {
                gui.GUI.AddChild(_mainContainer);
            }
            
            CreateHUDPanel();
            CreateHotbarPanel();
            CreateMinimapPanel();
            CreateChatPanel();
            CreateMenuButtonsPanel();
        }
        
        /// <summary>
        /// 创建HUD面板
        /// </summary>
        private void CreateHUDPanel()
        {
            _hudPanel = new RoundedPanel
            {
                Bounds = new Rectangle(20, 20, 400, 120),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 15f
                // 移除BorderColor属性
            };
            
            // 玩家名称
            _playerNameLabel = new Label
            {
                Text = "玩家名称",
                Font = UIHelper.SetFont(),  // 修正为FontReference
                TextColor = Color.White,
                Bounds = new Rectangle(10, 5, 200, 25),
                HorizontalAlignment = TextAlignment.Near  // 添加HorizontalAlignment
            };
            _hudPanel.AddChild(_playerNameLabel);
            
            // 等级标签
            _levelLabel = new Label
            {
                Text = "等级 1",
                TextColor = Color.Yellow,
                Bounds = new Rectangle(220, 5, 80, 25),
                HorizontalAlignment = TextAlignment.Near  // 添加HorizontalAlignment
            };
            _hudPanel.AddChild(_levelLabel);
            
            // 坐标标签
            _coordinatesLabel = new Label
            {
                Text = "X:0 Y:0 Z:0",
                TextColor = Color.LightGray,
                Bounds = new Rectangle(310, 5, 80, 25),
                HorizontalAlignment = TextAlignment.Near  // 添加HorizontalAlignment
            };
            _hudPanel.AddChild(_coordinatesLabel);
            
            // 生命值条
            var healthLabel = new Label
            {
                Text = "生命值",
                TextColor = Color.White,
                Bounds = new Rectangle(10, 35, 60, 20),
                HorizontalAlignment = TextAlignment.Near  // 添加HorizontalAlignment
            };
            _hudPanel.AddChild(healthLabel);
            
            _healthBar = new RoundedProgressBar
            {
                Bounds = new Rectangle(80, 35, 200, 20),
                BackgroundColor = new Color(0.3f, 0.1f, 0.1f),
                BarColor = new Color(0.8f, 0.2f, 0.2f),
                CornerRadius = 10f
            };
            _hudPanel.AddChild(_healthBar);
            
            // 魔法值条
            var manaLabel = new Label
            {
                Text = "魔法值",
                TextColor = Color.White,
                Bounds = new Rectangle(10, 60, 60, 20),
                HorizontalAlignment = TextAlignment.Near  // 添加HorizontalAlignment
            };
            _hudPanel.AddChild(manaLabel);
            
            _manaBar = new RoundedProgressBar
            {
                Bounds = new Rectangle(80, 60, 200, 20),
                BackgroundColor = new Color(0.1f, 0.1f, 0.3f),
                BarColor = new Color(0.2f, 0.2f, 0.8f),
                CornerRadius = 10f
            };
            _hudPanel.AddChild(_manaBar);
            
            // 经验值条
            var expLabel = new Label
            {
                Text = "经验值",
                TextColor = Color.White,
                Bounds = new Rectangle(10, 85, 60, 20),
                HorizontalAlignment = TextAlignment.Near  // 添加HorizontalAlignment
            };
            _hudPanel.AddChild(expLabel);
            
            _experienceBar = new RoundedProgressBar
            {
                Bounds = new Rectangle(80, 85, 200, 20),
                BackgroundColor = new Color(0.1f, 0.3f, 0.1f),
                BarColor = new Color(0.2f, 0.8f, 0.2f),
                CornerRadius = 10f
            };
            _hudPanel.AddChild(_experienceBar);
            
            _mainContainer.AddChild(_hudPanel);
        }
        
        /// <summary>
        /// 创建快捷栏面板
        /// </summary>
        private void CreateHotbarPanel()
        {
            _hotbarPanel = new RoundedPanel
            {
                Bounds = new Rectangle(300, FlaxEngine.Screen.Size.Y - 80, 520, 60),  // 修正为FlaxEngine.Screen
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 10f
                // 移除BorderColor属性
            };
            
            _hotbarSlots = new List<Button>();
            
            for (int i = 0; i < HOTBAR_SLOT_COUNT; i++)
            {
                var slot = new Button
                {
                    Bounds = new Rectangle(5 + i * 50, 5, 45, 45),
                    BackgroundColor = new Color(0.2f, 0.2f, 0.25f),
                    Text = (i + 1).ToString(),
                    TextColor = Color.White
                };
                slot.Tag = i;
                slot.ButtonClicked += OnHotbarSlotClicked;
                
                _hotbarPanel.AddChild(slot);
                _hotbarSlots.Add(slot);
            }
            
            _mainContainer.AddChild(_hotbarPanel);
        }
        
        /// <summary>
        /// 创建小地图面板
        /// </summary>
        private void CreateMinimapPanel()
        {
            _minimapPanel = new RoundedPanel
            {
                Bounds = new Rectangle(FlaxEngine.Screen.Size.X - 220, 20, 200, 200),  // 修正为FlaxEngine.Screen
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 10f
                // 移除BorderColor属性
            };
            
            // 小地图图像（占位符）
            _minimapImage = new Image
            {
                Bounds = new Rectangle(5, 5, 190, 190),
                BackgroundColor = new Color(0.2f, 0.4f, 0.2f)
                // 移除BorderColor属性
            };
            _minimapPanel.AddChild(_minimapImage);
            
            // 玩家位置标记
            _minimapPlayerDot = new Panel
            {
                Bounds = new Rectangle(95, 95, 10, 10),
                BackgroundColor = Color.Red
                // 移除BorderColor属性
            };
            _minimapPanel.AddChild(_minimapPlayerDot);
            
            _mainContainer.AddChild(_minimapPanel);
        }
        
        /// <summary>
        /// 创建聊天面板
        /// </summary>
        private void CreateChatPanel()
        {
            _chatPanel = new RoundedPanel
            {
                Bounds = new Rectangle(20, FlaxEngine.Screen.Size.Y - 250, 400, 150),  // 修正为FlaxEngine.Screen
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 10f
                // 移除BorderColor属性
            };
            
            // 聊天滚动视图
            _chatScrollView = new ScrollableControl
            {
                Bounds = new Rectangle(5, 5, 390, 100),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.7f)
            };
            _chatPanel.AddChild(_chatScrollView);
            
            // 聊天输入框
            _chatInput = new TextBox
            {
                Bounds = new Rectangle(5, 110, 320, 30),
                BackgroundColor = new Color(0.3f, 0.3f, 0.35f),
                TextColor = Color.White,
                WatermarkText = "输入聊天消息..."
            };
            _chatPanel.AddChild(_chatInput);
            
            // 发送按钮
            _chatSendButton = new Button
            {
                Text = "发送",
                Bounds = new Rectangle(330, 110, 65, 30),
                BackgroundColor = new Color(0.3f, 0.6f, 0.3f),
                TextColor = Color.White
            };
            _chatSendButton.ButtonClicked += OnChatSendClicked;
            _chatPanel.AddChild(_chatSendButton);
            
            _chatMessages = new List<Label>();
            
            _mainContainer.AddChild(_chatPanel);
        }
        
        /// <summary>
        /// 创建菜单按钮面板
        /// </summary>
        private void CreateMenuButtonsPanel()
        {
            _menuButtonsPanel = new RoundedPanel
            {
                Bounds = new Rectangle(FlaxEngine.Screen.Size.X - 120, FlaxEngine.Screen.Size.Y - 300, 100, 280),  // 修正为FlaxEngine.Screen
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 10f
                // 移除BorderColor属性
            };
            
            // 背包按钮
            _inventoryButton = new Button
            {
                Text = "背包",
                Bounds = new Rectangle(10, 10, 80, 35),
                BackgroundColor = new Color(0.3f, 0.3f, 0.6f),
                TextColor = Color.White
            };
            _inventoryButton.ButtonClicked += OnInventoryClicked;
            _menuButtonsPanel.AddChild(_inventoryButton);
            
            // 角色按钮
            _characterButton = new Button
            {
                Text = "角色",
                Bounds = new Rectangle(10, 55, 80, 35),
                BackgroundColor = new Color(0.3f, 0.6f, 0.3f),
                TextColor = Color.White
            };
            _characterButton.ButtonClicked += OnCharacterClicked;
            _menuButtonsPanel.AddChild(_characterButton);
            
            // 技能按钮
            _skillButton = new Button
            {
                Text = "技能",
                Bounds = new Rectangle(10, 100, 80, 35),
                BackgroundColor = new Color(0.6f, 0.6f, 0.3f),
                TextColor = Color.White
            };
            _skillButton.ButtonClicked += OnSkillClicked;
            _menuButtonsPanel.AddChild(_skillButton);
            
            // 任务按钮
            _questButton = new Button
            {
                Text = "任务",
                Bounds = new Rectangle(10, 145, 80, 35),
                BackgroundColor = new Color(0.6f, 0.3f, 0.6f),
                TextColor = Color.White
            };
            _questButton.ButtonClicked += OnQuestClicked;
            _menuButtonsPanel.AddChild(_questButton);
            
            // 设置按钮
            _settingsButton = new Button
            {
                Text = "设置",
                Bounds = new Rectangle(10, 190, 80, 35),
                BackgroundColor = new Color(0.3f, 0.6f, 0.6f),
                TextColor = Color.White
            };
            _settingsButton.ButtonClicked += OnSettingsClicked;
            _menuButtonsPanel.AddChild(_settingsButton);
            
            // 登出按钮
            _logoutButton = new Button
            {
                Text = "登出",
                Bounds = new Rectangle(10, 235, 80, 35),
                BackgroundColor = new Color(0.6f, 0.3f, 0.3f),
                TextColor = Color.White
            };
            _logoutButton.ButtonClicked += OnLogoutClicked;
            _menuButtonsPanel.AddChild(_logoutButton);
            
            _mainContainer.AddChild(_menuButtonsPanel);
        }
        
        /// <summary>
        /// 初始化数据
        /// </summary>
        private void InitializeData()
        {
            // 模拟初始数据
            _currentCharacter = new CharacterInfo
            {
                CharacterName = "玩家名称",
                Level = 1
            };
            
            UpdateHUD();
        }
        
        /// <summary>
        /// 更新HUD显示
        /// </summary>
        private void UpdateHUD()
        {
            if (_currentCharacter != null)
            {
                _playerNameLabel.Text = _currentCharacter.CharacterName;
                _levelLabel.Text = $"等级 {_currentCharacter.Level}";
            }
            
            _coordinatesLabel.Text = $"X:{_playerPosition.X:F0} Y:{_playerPosition.Y:F0} Z:{_playerPosition.Z:F0}";
            
            _healthBar.Value = _maxHealth > 0 ? _currentHealth / _maxHealth : 0;
            _manaBar.Value = _maxMana > 0 ? _currentMana / _maxMana : 0;
            _experienceBar.Value = _expToNextLevel > 0 ? (float)_currentExp / _expToNextLevel : 0;
        }
        
        /// <summary>
        /// 快捷栏槽位点击事件
        /// </summary>
        private void OnHotbarSlotClicked(Button sender)
        {
            if (sender.Tag is int slotIndex)
            {
                FlaxEngine.Debug.Log($"快捷栏槽位 {slotIndex + 1} 被点击");
                // TODO: 实现快捷栏功能
            }
        }
        
        /// <summary>
        /// 聊天发送按钮点击事件
        /// </summary>
        private void OnChatSendClicked(Button sender)
        {
            var message = _chatInput.Text?.Trim();
            if (!string.IsNullOrEmpty(message))
            {
                AddChatMessage($"玩家: {message}");
                _chatInput.Text = "";
                
                // TODO: 发送聊天消息到服务器
            }
        }
        
        /// <summary>
        /// 添加聊天消息
        /// </summary>
        private void AddChatMessage(string message)
        {
            // 移除最旧的消息（如果超过最大数量）
            if (_chatMessages.Count >= MAX_CHAT_MESSAGES)
            {
                var oldestMessage = _chatMessages[0];
                _chatScrollView.RemoveChild(oldestMessage);
                _chatMessages.RemoveAt(0);
            }
            
            // 创建新消息标签
            var messageLabel = new Label
            {
                Text = message,
                TextColor = Color.White,
                Bounds = new Rectangle(5, 5 + _chatMessages.Count * 20, 380, 20),
                HorizontalAlignment = TextAlignment.Near  // 添加HorizontalAlignment
            };
            
            _chatScrollView.AddChild(messageLabel);
            _chatMessages.Add(messageLabel);

            // 滚动到底部
            _chatScrollView.Height = _chatScrollView.Bottom;
        }
        
        /// <summary>
        /// 背包按钮点击事件
        /// </summary>
        private void OnInventoryClicked(Button sender)
        {
            FlaxEngine.Debug.Log("打开背包界面");
            TogglePanel("Inventory", "背包", 500, 450);
        }
        
        /// <summary>
        /// 角色按钮点击事件
        /// </summary>
        private void OnCharacterClicked(Button sender)
        {
            FlaxEngine.Debug.Log("打开角色界面");
            TogglePanel("Character", "角色", 400, 500);
        }
        
        /// <summary>
        /// 技能按钮点击事件
        /// </summary>
        private void OnSkillClicked(Button sender)
        {
            FlaxEngine.Debug.Log("打开技能界面");
            TogglePanel("Skill", "技能", 450, 400);
        }
        
        /// <summary>
        /// 任务按钮点击事件
        /// </summary>
        private void OnQuestClicked(Button sender)
        {
            FlaxEngine.Debug.Log("打开任务界面");
            TogglePanel("Quest", "任务", 400, 500);
        }
        
        /// <summary>
        /// 设置按钮点击事件
        /// </summary>
        private void OnSettingsClicked(Button sender)
        {
            FlaxEngine.Debug.Log("打开设置界面");
            TogglePanel("Settings", "设置", 400, 350);
        }
        
        /// <summary>
        /// 切换面板显示/隐藏
        /// </summary>
        private void TogglePanel(string panelName, string title, float width, float height)
        {
            // 如果面板已打开，则关闭
            if (_activePanelName == panelName && _panels.TryGetValue(panelName, out var existingPanel))
            {
                existingPanel.Visible = false;
                _mainContainer.RemoveChild(existingPanel);
                _panels.Remove(panelName);
                _activePanelName = null;
                return;
            }
            
            // 关闭当前活动面板
            if (_activePanelName != null && _panels.TryGetValue(_activePanelName, out var activePanel))
            {
                activePanel.Visible = false;
                _mainContainer.RemoveChild(activePanel);
                _panels.Remove(_activePanelName);
            }
            
            // 创建新面板
            var panel = new RoundedPanel
            {
                Bounds = new Rectangle(
                    (_mainContainer.Width - width) / 2,
                    (_mainContainer.Height - height) / 2,
                    width, height),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.95f)
            };
            
            // 标题栏
            var titleLabel = new Label
            {
                Text = title,
                TextColor = Color.White,
                Bounds = new Rectangle(10, 10, width - 60, 30),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(titleLabel);
            
            // 关闭按钮
            var closeButton = new Button
            {
                Text = "✕",
                Bounds = new Rectangle(width - 40, 5, 30, 30),
                BackgroundColor = new Color(0.8f, 0.2f, 0.2f, 0.8f)
            };
            closeButton.Clicked += () =>
            {
                panel.Visible = false;
                _mainContainer.RemoveChild(panel);
                _panels.Remove(panelName);
                if (_activePanelName == panelName)
                    _activePanelName = null;
            };
            panel.AddChild(closeButton);
            
            // 内容区域提示
            var contentLabel = new Label
            {
                Text = $"{title}面板内容区域",
                TextColor = new Color(0.7f, 0.7f, 0.7f),
                Bounds = new Rectangle(10, 50, width - 20, height - 60),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            panel.AddChild(contentLabel);
            
            _mainContainer.AddChild(panel);
            _panels[panelName] = panel;
            _activePanelName = panelName;
        }
        
        /// <summary>
        /// 登出按钮点击事件
        /// </summary>
        private void OnLogoutClicked(Button sender)
        {
            var confirmDialog = ConfirmDialog.CreateLogoutDialog(() =>
            {
                FlaxEngine.Debug.Log("用户请求登出");
                
                // 播放退出动画
                _animationManager.FadeOut(_mainContainer, 0.5f, EasingType.EaseOut, () =>
                {
                    _stateManager.TransitionToScene(SceneType.Login);
                });
            });
            
            _mainContainer.AddChild(confirmDialog);
        }
        
        public override void OnDestroy()
        {
            // 取消事件订阅
            if (_stateManager != null)
            {
                _stateManager.SceneChanged -= OnSceneChanged;
                _stateManager.SelectedCharacterChanged -= OnSelectedCharacterChanged;
            }
            
            // 清理资源
            _mainContainer?.Dispose();
        }
        
        #region 显示控制
        
        /// <summary>
        /// 显示游戏主界面
        /// </summary>
        public void ShowGameMainUI()
        {
            _mainContainer.Visible = true;
            
            // 播放显示动画
            _animationManager.FadeIn(_mainContainer, 0.5f);
            
            // 更新角色信息
            if (_stateManager.SelectedCharacter != null)
            {
                _currentCharacter = _stateManager.SelectedCharacter;
                UpdateHUD();
            }
            
            FlaxEngine.Debug.Log("游戏主界面已显示");
        }
        
        /// <summary>
        /// 隐藏游戏主界面
        /// </summary>
        public void HideGameMainUI()
        {
            _animationManager.FadeOut(_mainContainer, 0.3f, EasingType.EaseOut, () => {
                _mainContainer.Visible = false;
            });
            
            FlaxEngine.Debug.Log("游戏主界面已隐藏");
        }
        
        #endregion
        
        // 属性
        public CharacterInfo CurrentCharacter => _currentCharacter;
        public Vector3 PlayerPosition 
        { 
            get => _playerPosition; 
            set 
            { 
                _playerPosition = value; 
                UpdateHUD(); 
            } 
        }
        public float CurrentHealth 
        { 
            get => _currentHealth; 
            set 
            { 
                _currentHealth = value; 
                UpdateHUD(); 
            } 
        }
        public float MaxHealth 
        { 
            get => _maxHealth; 
            set 
            { 
                _maxHealth = value; 
                UpdateHUD(); 
            } 
        }
        public float CurrentMana 
        { 
            get => _currentMana; 
            set 
            { 
                _currentMana = value; 
                UpdateHUD(); 
            } 
        }
        public float MaxMana 
        { 
            get => _maxMana; 
            set 
            { 
                _maxMana = value; 
                UpdateHUD(); 
            } 
        }
        public int CurrentExp 
        { 
            get => _currentExp; 
            set 
            { 
                _currentExp = value; 
                UpdateHUD(); 
            } 
        }
        public int ExpToNextLevel 
        { 
            get => _expToNextLevel; 
            set 
            { 
                _expToNextLevel = value; 
                UpdateHUD(); 
            } 
        }
    }
}