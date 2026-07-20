using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.StyleSystem;
using HundunWorld.Game.UI.Layout;
using MemoryPack;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.Animation;
using HundunWorld.Game.UI.ErrorHandling;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.Services;
using HundunWorld.Game.Equipment;
using Game.Character.Attributes;
using InventoryItemData = HundunWorld.Game.Services.InventoryItemData;
using AppearanceData = HundunWorld.Game.Services.CharacterPersistenceService.AppearanceData;
using EquipmentSlot = HundunWorld.Game.Equipment.EquipmentSlot;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 游戏主UI系统 - 重构版本
    /// 包含HUD、快捷栏、小地图、聊天窗口等游戏界面元素
    /// 集成状态管理、动画效果和错误处理
    /// </summary>
    public class GameMainUI : Script
    {
        // 角色面板默认尺寸（会根据屏幕安全区域动态缩放）
        private const float CharacterPanelDefaultWidth = 880f;
        private const float CharacterPanelDefaultHeight = 640f;

        // 核心管理器
        private UIStateManager _stateManager;
        private UIAnimationManager _animationManager;
        private ErrorHandlingManager _errorManager;
        private ToastManager _toastManager;
        
        // 主容器
        private ContainerControl _mainContainer;
        private bool _uiInitialized = false;
        
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
        private Label _minimapCoordinatesLabel;
        private List<Panel> _minimapMarkers = new List<Panel>();
        
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
        private Button _guildButton;
        private Button _teamButton;
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
        
        // 快捷栏绑定数据 (slotIndex -> skillId)
        private readonly Dictionary<int, int> _hotbarBindings = new Dictionary<int, int>();
        
        // 窗口大小变化追踪
        private Float2 _lastScreenSize;

        #region 角色面板组件与数据

        // 当前打开的角色面板及其分栏容器
        private RoundedPanel _characterPanel;
        private Panel _leftColumnPanel;
        private Panel _middleColumnPanel;
        private Panel _rightColumnPanel;
        private Panel _attributePanel;
        private Panel _previewPlaceholderPanel;
        private Panel _equipmentSlotsContainer;
        private Panel _radarContainer;
        private Panel _previewPanel;
        private Panel _inventoryContainer;
        private Panel _inventoryContentPanel;
        private CharacterPreviewPanel _characterPreviewPanel;
        private WuxingRadarChart _wuxingRadarChart;
        private readonly List<EquipmentSlotView> _equipmentSlotViews = new List<EquipmentSlotView>();

        // 装备槽位显示顺序（与 EquipmentSlotView 默认名称对应）
        private static readonly EquipmentSlot[] EquipmentSlotOrder = new[]
        {
            EquipmentSlot.Body,
            EquipmentSlot.Head,
            EquipmentSlot.Back,
            EquipmentSlot.RightHand,
            EquipmentSlot.LeftHand,
            EquipmentSlot.Waist,
            EquipmentSlot.Face,
            EquipmentSlot.Neck
        };

        // 角色面板当前加载的数据（用于穿戴/卸下后的本地刷新）
        private AppearanceData _characterAppearanceData;
        private CharacterAttributes _characterAttributes;
        private List<InventoryItemData> _characterInventoryItems = new List<InventoryItemData>();

        #endregion

        public override void OnStart()
        {
            InitializeManagers();
            InitializeUI();
            InitializeData();
            SubscribeEvents();
            
            // 记录初始屏幕大小
            _lastScreenSize = FlaxEngine.Screen.Size;
            
            FlaxEngine.Debug.Log("游戏主界面重构版初始化完成");
        }
        
        public override void OnUpdate()
        {
            // 检查窗口大小变化，更新需要动态调整的元素
            UpdateLayoutForScreenSize();

            // 快捷键：C 打开/关闭角色面板，方便测试与绕过菜单按钮问题
            if (Input.GetKeyDown(KeyboardKeys.C))
            {
                FlaxEngine.Debug.Log("[GameMainUI] 快捷键 C 触发角色面板");
                OnCharacterClicked(null);
            }
        }
        
        /// <summary>
        /// 检查窗口大小变化并更新布局
        /// 使用 AnchorPreset 的面板会自动跟随锚点调整位置，
        /// 此方法处理需要特殊逻辑的元素
        /// </summary>
        private void UpdateLayoutForScreenSize()
        {
            var screenSize = FlaxEngine.Screen.Size;
            if (screenSize != _lastScreenSize)
            {
                _lastScreenSize = screenSize;
                // 使用 AnchorPreset 的面板会自动调整位置
                // 这里可扩展处理需要根据窗口宽高比调整大小的元素
                FlaxEngine.Debug.Log($"[GameMainUI] 窗口大小变化: {screenSize.X}x{screenSize.Y}");
            }
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
                BackgroundColor = Color.Transparent,
                ClipChildren = true
            };

            // 查找 UICanvas（参考 CharacterSceneController 的健壮查找机制）
            UICanvas uiCanvas = FindOrCreateUICanvas();
            if (uiCanvas?.GUI == null)
            {
                FlaxEngine.Debug.LogError("[GameMainUI] UICanvas.GUI 为 null！无法创建游戏主界面 UI");
                return;
            }

            uiCanvas.GUI.AddChild(_mainContainer);
            
            CreateHUDPanel();
            CreateHotbarPanel();
            CreateMinimapPanel();
            CreateChatPanel();
            CreateMenuButtonsPanel();

            _uiInitialized = true;

            // 初始状态下隐藏游戏主界面，只有进入游戏世界后才显示
            _mainContainer.Visible = false;

            // UI 控件已创建，如果角色信息已存在则立即刷新 HUD 显示
            if (_currentCharacter != null)
            {
                UpdateHUD();
            }

            FlaxEngine.Debug.Log("[GameMainUI] UI 初始化完成（初始隐藏状态）");
        }

        /// <summary>
        /// 查找或创建 UICanvas（健壮的多级查找机制）
        /// </summary>
        private UICanvas FindOrCreateUICanvas()
        {
            UICanvas uiCanvas = null;

            // 方式1: 从 Actor 自身查找
            if (Actor != null)
            {
                uiCanvas = Actor.GetScript<UICanvas>();
                if (uiCanvas == null)
                {
                    uiCanvas = Actor.GetChild<UICanvas>();
                }
                FlaxEngine.Debug.Log($"[GameMainUI] 从 Actor 查找 UICanvas: {(uiCanvas != null ? "找到 " + uiCanvas.Name : "未找到")}");
            }

            // 方式2: 从父 Actor 查找
            if (uiCanvas == null && Actor?.Parent != null)
            {
                uiCanvas = Actor.Parent.GetScript<UICanvas>();
                if (uiCanvas == null)
                {
                    uiCanvas = Actor.Parent.GetChild<UICanvas>();
                }
                FlaxEngine.Debug.Log($"[GameMainUI] 从父 Actor 查找 UICanvas: {(uiCanvas != null ? "找到 " + uiCanvas.Name : "未找到")}");
            }

            // 方式3: 从场景中查找
            if (uiCanvas == null)
            {
                uiCanvas = Actor?.Scene?.FindActor<UICanvas>();
                FlaxEngine.Debug.Log($"[GameMainUI] 从场景查找 UICanvas: {(uiCanvas != null ? "找到 " + uiCanvas.Name : "未找到")}");
            }

            // 方式4: 从 Level 全局查找
            if (uiCanvas == null)
            {
                var allCanvases = Level.GetActors<UICanvas>();
                FlaxEngine.Debug.Log($"[GameMainUI] Level 中共有 {allCanvases?.Length ?? 0} 个 UICanvas");
                if (allCanvases != null && allCanvases.Length > 0)
                {
                    // 优先使用当前场景的 Canvas
                    if (Actor?.Scene != null)
                    {
                        foreach (var c in allCanvases)
                        {
                            if (c.Scene == Actor.Scene)
                            {
                                uiCanvas = c;
                                break;
                            }
                        }
                    }
                    // 如果没有当前场景的，用第一个
                    if (uiCanvas == null)
                    {
                        uiCanvas = allCanvases[0];
                    }
                    FlaxEngine.Debug.Log($"[GameMainUI] 从 Level 查找 UICanvas: {(uiCanvas != null ? "找到 " + uiCanvas.Name : "未找到")}");
                }
            }

            // 方式5: 自动创建 UICanvas
            if (uiCanvas == null)
            {
                FlaxEngine.Debug.LogWarning("[GameMainUI] 未找到 UICanvas，自动创建...");

                var canvasActor = new EmptyActor { Name = "GameMainUICanvas" };
                if (Actor?.Scene != null)
                {
                    Level.SpawnActor(canvasActor, Actor.Scene);
                }
                else
                {
                    Level.SpawnActor(canvasActor);
                }

                uiCanvas = canvasActor.AddChild<UICanvas>();
                uiCanvas.Name = "GameMainUICanvas";
                FlaxEngine.Debug.Log($"[GameMainUI] UICanvas 自动创建完成: {uiCanvas.Name}");
            }

            // 确保设置为 ScreenSpace 模式
            if (uiCanvas != null && uiCanvas.RenderMode != CanvasRenderMode.ScreenSpace)
            {
                uiCanvas.RenderMode = CanvasRenderMode.ScreenSpace;
                FlaxEngine.Debug.Log("[GameMainUI] UICanvas RenderMode 已设置为 ScreenSpace");
            }

            return uiCanvas;
        }
        
        /// <summary>
        /// 创建HUD面板
        /// </summary>
        private void CreateHUDPanel()
        {
            _hudPanel = new RoundedPanel
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(20, 20),
                Size = new Float2(400, 120),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 15f
            };
            
            // 玩家名称
            _playerNameLabel = new Label
            {
                Text = "玩家名称",
                Font = UIHelper.SetFont(size: 14),
                TextColor = Color.White,
                Bounds = new Rectangle(10, 5, 200, 25),
                HorizontalAlignment = TextAlignment.Near
            };
            _hudPanel.AddChild(_playerNameLabel);
            
            // 等级标签
            _levelLabel = new Label
            {
                Text = "等级 1",
                Font = UIHelper.SetFont(size: 12),
                TextColor = Color.Yellow,
                Bounds = new Rectangle(220, 5, 80, 25),
                HorizontalAlignment = TextAlignment.Near
            };
            _hudPanel.AddChild(_levelLabel);
            
            // 坐标标签
            _coordinatesLabel = new Label
            {
                Text = "X:0 Y:0 Z:0",
                Font = UIHelper.SetFont(size: 12),
                TextColor = Color.LightGray,
                Bounds = new Rectangle(310, 5, 80, 25),
                HorizontalAlignment = TextAlignment.Near
            };
            _hudPanel.AddChild(_coordinatesLabel);
            
            // 生命值条
            var healthLabel = new Label
            {
                Text = "生命值",
                Font = UIHelper.SetFont(size: 11),
                TextColor = Color.White,
                Bounds = new Rectangle(10, 35, 60, 20),
                HorizontalAlignment = TextAlignment.Near
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
                Font = UIHelper.SetFont(size: 11),
                TextColor = Color.White,
                Bounds = new Rectangle(10, 60, 60, 20),
                HorizontalAlignment = TextAlignment.Near
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
                Font = UIHelper.SetFont(size: 11),
                TextColor = Color.White,
                Bounds = new Rectangle(10, 85, 60, 20),
                HorizontalAlignment = TextAlignment.Near
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
                AnchorPreset = AnchorPresets.BottomCenter,
                Location = new Float2(-260, -80),
                Size = new Float2(520, 60),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 10f
            };
            
            _hotbarSlots = new List<Button>();
            
            for (int i = 0; i < HOTBAR_SLOT_COUNT; i++)
            {
                var slot = new Button
                {
                    Bounds = new Rectangle(5 + i * 50, 5, 45, 45),
                    BackgroundColor = new Color(0.2f, 0.2f, 0.25f),
                    Text = (i + 1).ToString(),
                    Font = UIHelper.SetFont(size: 12),
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
            // 小地图面板：右上角，距右边缘20px，距顶部20px，宽200，高230
            // 使用 Offsets 精确控制位置，避免 Location 在锚点模式下的歧义
            const float minimapWidth = 200f;
            const float minimapHeight = 230f;
            const float rightMargin = 20f;
            const float topMargin = 20f;

            _minimapPanel = new RoundedPanel
            {
                AnchorPreset = AnchorPresets.TopRight,
                //Offsets = new Margin(-rightMargin*2 - minimapWidth, -rightMargin, topMargin, topMargin + minimapHeight),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 10f,
                LocalLocation = new Float2(-minimapWidth/2, 0) // 使用 Offsets 定位，Location 设置为 (0,0) 避免混淆
            };
            
            // 小地图标题
            var minimapTitle = new Label
            {
                Text = "小地图",
                Font = UIHelper.SetFont(size: 11),
                TextColor = new Color(0.8f, 0.8f, 0.8f),
                Bounds = new Rectangle(5, 2, 190, 18),
                HorizontalAlignment = TextAlignment.Center
            };
            _minimapPanel.AddChild(minimapTitle);
            
            // 小地图图像（占位符）
            _minimapImage = new Image
            {
                Bounds = new Rectangle(5, 20, 190, 175),
                BackgroundColor = new Color(0.15f, 0.35f, 0.15f)
            };
            _minimapPanel.AddChild(_minimapImage);

            // 方向标记
            string[] directions = { "N", "S", "W", "E" };
            Rectangle[] dirBounds =
            {
                new Rectangle(90, 20, 20, 16),      // 北
                new Rectangle(90, 179, 20, 16),     // 南
                new Rectangle(7, 100, 20, 16),       // 西
                new Rectangle(173, 100, 20, 16)      // 东
            };
            for (int i = 0; i < directions.Length; i++)
            {
                var dirLabel = new Label
                {
                    Text = directions[i],
                    Font = UIHelper.SetFont(size: 11),
                    TextColor = new Color(1.0f, 1.0f, 0.6f, 0.7f),
                    Bounds = dirBounds[i],
                    HorizontalAlignment = TextAlignment.Center
                };
                _minimapPanel.AddChild(dirLabel);
            }

            // 玩家位置标记（中心红点）
            _minimapPlayerDot = new Panel
            {
                Bounds = new Rectangle(95, 103, 10, 10),
                BackgroundColor = Color.Red
            };
            _minimapPanel.AddChild(_minimapPlayerDot);

            // 坐标显示
            _minimapCoordinatesLabel = new Label
            {
                Text = "(0, 0, 0)",
                Font = UIHelper.SetFont(size: 11),
                TextColor = new Color(0.7f, 0.7f, 0.7f),
                Bounds = new Rectangle(5, 200, 190, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            _minimapPanel.AddChild(_minimapCoordinatesLabel);
            
            _mainContainer.AddChild(_minimapPanel);
        }

        /// <summary>
        /// 添加小地图标记（传送点或任务标记）
        /// </summary>
        public void AddMinimapMarker(string name, float relativeX, float relativeZ, Color markerColor)
        {
            // 将相对坐标转换为小地图上的像素位置
            float mapX = 5 + relativeX * 190;
            float mapY = 20 + relativeZ * 175;

            var marker = new Panel
            {
                Bounds = new Rectangle(mapX - 4, mapY - 4, 8, 8),
                BackgroundColor = markerColor,
                TooltipText = name
            };
            _minimapPanel.AddChild(marker);
            _minimapMarkers.Add(marker);
        }

        /// <summary>
        /// 添加传送点标记到小地图
        /// </summary>
        public void AddTeleportMarker(string name, float relativeX, float relativeZ)
        {
            AddMinimapMarker(name, relativeX, relativeZ, new Color(0.3f, 0.5f, 1.0f, 0.9f));
        }

        /// <summary>
        /// 添加任务标记到小地图
        /// </summary>
        public void AddQuestMarker(string name, float relativeX, float relativeZ)
        {
            AddMinimapMarker(name, relativeX, relativeZ, new Color(1.0f, 0.9f, 0.2f, 0.9f));
        }

        /// <summary>
        /// 清除所有小地图标记
        /// </summary>
        public void ClearMinimapMarkers()
        {
            foreach (var marker in _minimapMarkers)
            {
                _minimapPanel.RemoveChild(marker);
                marker.Dispose();
            }
            _minimapMarkers.Clear();
        }
        
        /// <summary>
        /// 创建聊天面板
        /// </summary>
        private void CreateChatPanel()
        {
            _chatPanel = new RoundedPanel
            {
                AnchorPreset = AnchorPresets.BottomLeft,
                Location = new Float2(20, -250),
                Size = new Float2(400, 150),
                BackgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.8f),
                CornerRadius = 10f
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
                Font = UIHelper.SetFont(size: 12),
                WatermarkText = "输入聊天消息..."
            };
            _chatPanel.AddChild(_chatInput);
            
            // 发送按钮
            _chatSendButton = new Button
            {
                Text = "发送",
                Font = UIHelper.SetFont(size: 12),
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
            // 菜单按钮面板：右下角，距右边缘和底部快捷栏各一段间距
            const float menuWidth = 100f;
            const float menuHeight = 280f;
            const float menuRightMargin = 20f;
            const float menuBottomMargin = 100f;

            _menuButtonsPanel = new RoundedPanel
            {
                Parent = _mainContainer,
                AnchorPreset = AnchorPresets.BottomRight,
                Size = new Float2(menuWidth, menuHeight),
                BackgroundColor = new Color(0.15f, 0.15f, 0.22f, 0.95f),
                CornerRadius = 10f,
                ClipChildren = true,
                LocalLocation = new Float2(-menuWidth - menuRightMargin, menuHeight/2)
            };

            // 菜单按钮统一尺寸与紧凑间距
            const float menuButtonWidth = 80f;
            const float menuButtonHeight = 30f;
            const float menuButtonSpacing = 4f;
            const float menuStartY = 8f;

            // 背包按钮
            _inventoryButton = new Button
            {
                Text = "背包",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(10, menuStartY, menuButtonWidth, menuButtonHeight),
                BackgroundColor = new Color(0.3f, 0.3f, 0.6f),
                TextColor = Color.White
            };
            _inventoryButton.ButtonClicked += OnInventoryClicked;
            _menuButtonsPanel.AddChild(_inventoryButton);

            // 角色按钮
            _characterButton = new Button
            {
                Text = "角色",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(10, menuStartY + (menuButtonHeight + menuButtonSpacing), menuButtonWidth, menuButtonHeight),
                BackgroundColor = new Color(0.3f, 0.6f, 0.3f),
                TextColor = Color.White
            };
            _characterButton.ButtonClicked += OnCharacterClicked;
            _menuButtonsPanel.AddChild(_characterButton);

            // 技能按钮
            _skillButton = new Button
            {
                Text = "技能",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(10, menuStartY + (menuButtonHeight + menuButtonSpacing) * 2, menuButtonWidth, menuButtonHeight),
                BackgroundColor = new Color(0.6f, 0.6f, 0.3f),
                TextColor = Color.White
            };
            _skillButton.ButtonClicked += OnSkillClicked;
            _menuButtonsPanel.AddChild(_skillButton);

            // 任务按钮
            _questButton = new Button
            {
                Text = "任务",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(10, menuStartY + (menuButtonHeight + menuButtonSpacing) * 3, menuButtonWidth, menuButtonHeight),
                BackgroundColor = new Color(0.6f, 0.3f, 0.6f),
                TextColor = Color.White
            };
            _questButton.ButtonClicked += OnQuestClicked;
            _menuButtonsPanel.AddChild(_questButton);

            // 公会按钮
            _guildButton = new Button
            {
                Text = "公会",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(10, menuStartY + (menuButtonHeight + menuButtonSpacing) * 4, menuButtonWidth, menuButtonHeight),
                BackgroundColor = new Color(0.5f, 0.4f, 0.2f),
                TextColor = Color.White
            };
            _guildButton.ButtonClicked += OnGuildClicked;
            _menuButtonsPanel.AddChild(_guildButton);

            // 组队按钮
            _teamButton = new Button
            {
                Text = "组队",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(10, menuStartY + (menuButtonHeight + menuButtonSpacing) * 5, menuButtonWidth, menuButtonHeight),
                BackgroundColor = new Color(0.2f, 0.5f, 0.5f),
                TextColor = Color.White
            };
            _teamButton.ButtonClicked += OnTeamClicked;
            _menuButtonsPanel.AddChild(_teamButton);

            // 设置按钮
            _settingsButton = new Button
            {
                Text = "设置",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(10, menuStartY + (menuButtonHeight + menuButtonSpacing) * 6, menuButtonWidth, menuButtonHeight),
                BackgroundColor = new Color(0.3f, 0.6f, 0.6f),
                TextColor = Color.White
            };
            _settingsButton.ButtonClicked += OnSettingsClicked;
            _menuButtonsPanel.AddChild(_settingsButton);

            // 登出按钮
            _logoutButton = new Button
            {
                Text = "登出",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(10, menuStartY + (menuButtonHeight + menuButtonSpacing) * 7, menuButtonWidth, menuButtonHeight),
                BackgroundColor = new Color(0.6f, 0.3f, 0.3f),
                TextColor = Color.White
            };
            _logoutButton.ButtonClicked += OnLogoutClicked;
            _menuButtonsPanel.AddChild(_logoutButton);
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
                if (_playerNameLabel != null)
                {
                    _playerNameLabel.Text = string.IsNullOrEmpty(_currentCharacter.CharacterName) ? "玩家" : _currentCharacter.CharacterName;
                }
                if (_levelLabel != null)
                {
                    _levelLabel.Text = $"等级 {_currentCharacter.Level}";
                }
            }
            
            if (_coordinatesLabel != null)
            {
                _coordinatesLabel.Text = $"X:{_playerPosition.X:F0} Y:{_playerPosition.Y:F0} Z:{_playerPosition.Z:F0}";
            }
            
            // 更新小地图坐标
            if (_minimapCoordinatesLabel != null)
            {
                _minimapCoordinatesLabel.Text = $"({_playerPosition.X:F0}, {_playerPosition.Y:F0}, {_playerPosition.Z:F0})";
            }
            
            if (_healthBar != null)
            {
                _healthBar.Value = _maxHealth > 0 ? _currentHealth / _maxHealth : 0;
            }
            if (_manaBar != null)
            {
                _manaBar.Value = _maxMana > 0 ? _currentMana / _maxMana : 0;
            }
            if (_experienceBar != null)
            {
                _experienceBar.Value = _expToNextLevel > 0 ? (float)_currentExp / _expToNextLevel : 0;
            }
        }
        
        /// <summary>
        /// 快捷栏槽位点击事件
        /// </summary>
        private void OnHotbarSlotClicked(Button sender)
        {
            if (sender.Tag is int slotIndex)
            {
                if (_hotbarBindings.TryGetValue(slotIndex, out int skillId) && skillId > 0)
                {
                    FlaxEngine.Debug.Log($"快捷栏槽位 {slotIndex + 1} 被点击，使用技能 {skillId}");
                    UseHotbarSkill(slotIndex, skillId);
                }
                else
                {
                    FlaxEngine.Debug.Log($"快捷栏槽位 {slotIndex + 1} 未绑定技能");
                }
            }
        }

        /// <summary>
        /// 使用快捷栏技能
        /// </summary>
        private void UseHotbarSkill(int slotIndex, int skillId)
        {
            FlaxEngine.Debug.Log($"[GameMainUI] 使用快捷栏技能: 槽位={slotIndex}, 技能ID={skillId}");
        }

        /// <summary>
        /// 分配技能到快捷栏槽位
        /// </summary>
        public void AssignSkillToHotbar(int slotIndex, int skillId)
        {
            if (slotIndex < 0 || slotIndex >= HOTBAR_SLOT_COUNT) return;

            _hotbarBindings[slotIndex] = skillId;

            // 更新槽位显示文本
            if (_hotbarSlots != null && slotIndex < _hotbarSlots.Count)
            {
                _hotbarSlots[slotIndex].Text = skillId > 0 ? $"S{skillId}" : $"{slotIndex + 1}";
            }

            FlaxEngine.Debug.Log($"[GameMainUI] 技能 {skillId} 已绑定到快捷栏槽位 {slotIndex + 1}");
        }

        /// <summary>
        /// 清空快捷栏槽位
        /// </summary>
        public void ClearHotbarSlot(int slotIndex)
        {
            AssignSkillToHotbar(slotIndex, 0);
        }

        /// <summary>
        /// 交换两个快捷栏槽位
        /// </summary>
        public void SwapHotbarSlots(int slotA, int slotB)
        {
            if (slotA < 0 || slotA >= HOTBAR_SLOT_COUNT) return;
            if (slotB < 0 || slotB >= HOTBAR_SLOT_COUNT) return;

            // 未绑定的槽位默认技能ID为0（空槽位）
            int skillA = _hotbarBindings.TryGetValue(slotA, out int a) ? a : 0;
            int skillB = _hotbarBindings.TryGetValue(slotB, out int b) ? b : 0;

            AssignSkillToHotbar(slotA, skillB);
            AssignSkillToHotbar(slotB, skillA);
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
                
                // 发送聊天消息到服务器
                SendChatMessage(message);
            }
        }
        
        /// <summary>
        /// 发送聊天消息到服务器
        /// </summary>
        private void SendChatMessage(string message)
        {
            FlaxEngine.Debug.Log($"[GameMainUI] 发送聊天消息: {message}");
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
                Font = UIHelper.SetFont(size: 12),
                TextColor = Color.White,
                Bounds = new Rectangle(5, 5 + _chatMessages.Count * 20, 380, 20),
                HorizontalAlignment = TextAlignment.Near
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
            FlaxEngine.Debug.Log("[GameMainUI] 打开角色界面");
            // 角色面板需要足够宽度以容纳三栏布局，尺寸会被限制在屏幕安全区域内
            TogglePanel("Character", "角色", CharacterPanelDefaultWidth, CharacterPanelDefaultHeight);
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
        /// 公会按钮点击事件
        /// </summary>
        private void OnGuildClicked(Button sender)
        {
            FlaxEngine.Debug.Log("打开公会管理界面");
            TogglePanel("Guild", "公会管理", 550, 550);
        }

        /// <summary>
        /// 组队按钮点击事件
        /// </summary>
        private void OnTeamClicked(Button sender)
        {
            FlaxEngine.Debug.Log("打开组队界面");
            TogglePanel("Team", "组队邀请", 450, 500);
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
                DisposeCharacterPreview();
                _mainContainer.RemoveChild(existingPanel);
                existingPanel.Dispose();
                _panels.Remove(panelName);
                _activePanelName = null;
                return;
            }

            // 关闭当前活动面板
            if (_activePanelName != null && _panels.TryGetValue(_activePanelName, out var activePanel))
            {
                activePanel.Visible = false;
                DisposeCharacterPreview();
                _mainContainer.RemoveChild(activePanel);
                activePanel.Dispose();
                _panels.Remove(_activePanelName);
            }
            
            // 根据当前屏幕分辨率限制面板尺寸，防止超出安全区域
            var safeSize = ResponsiveLayoutCalculator.EnsureSafeSize(new Float2(width, height));
            width = safeSize.X;
            height = safeSize.Y;

            // 创建新面板：使用 MiddleCenter + Offsets 精确定位，避免 Location 在不同锚点下的歧义
            var panel = new RoundedPanel
            {
                AnchorPreset = AnchorPresets.MiddleCenter,
                Location = new Float2(-width / 2, -height / 2),
                Size = new Float2(width, height),
                BackgroundColor = ChineseClassicalTheme.DarkStoneBackgroundColor,
                ClipChildren = true
            };

            FlaxEngine.Debug.Log($"[GameMainUI] 创建 {panelName} 面板，目标尺寸 {width}x{height}，安全尺寸已限制");

            // 标题栏
            var titleLabel = new Label
            {
                Text = title,
                Font = UIHelper.SetFont(size: 14),
                TextColor = ChineseClassicalTheme.WowTitleColor,
                Bounds = new Rectangle(10, 10, width - 60, 30),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(titleLabel);

            // 关闭按钮
            var closeButton = new Button
            {
                Text = "✕",
                Font = UIHelper.SetFont(size: 12),
                Bounds = new Rectangle(width - 40, 5, 30, 30),
                BackgroundColor = new Color(0.8f, 0.2f, 0.2f, 0.8f)
            };
            closeButton.Clicked += () =>
            {
                panel.Visible = false;
                DisposeCharacterPreview();
                _mainContainer.RemoveChild(panel);
                panel.Dispose();
                _panels.Remove(panelName);
                if (_activePanelName == panelName)
                    _activePanelName = null;
            };
            panel.AddChild(closeButton);
            
            // 根据面板类型填充内容
            PopulatePanelContent(panel, panelName, width, height);
            
            _mainContainer.AddChild(panel);
            _panels[panelName] = panel;
            _activePanelName = panelName;

            FlaxEngine.Debug.Log($"[GameMainUI] 面板 {panelName} 已添加到主容器，屏幕尺寸 {FlaxEngine.Screen.Size.X}x{FlaxEngine.Screen.Size.Y}");
        }
        
        /// <summary>
        /// 根据面板类型填充内容
        /// </summary>
        private void PopulatePanelContent(RoundedPanel panel, string panelName, float width, float height)
        {
            float contentY = 50;
            float contentWidth = width - 20;
            float contentHeight = height - 60;

            switch (panelName)
            {
                case "Character":
                    PopulateCharacterPanel(panel, contentY, contentWidth, contentHeight);
                    break;
                case "Settings":
                    PopulateSettingsPanel(panel, contentY, contentWidth, contentHeight);
                    break;
                case "EquipCompare":
                    var equipUI = new EquipmentComparisonUI();
                    equipUI.PopulatePanel(panel, contentY, contentWidth, contentHeight);
                    break;
                case "Guild":
                    var guildUI = new GuildManagementUI();
                    guildUI.PopulatePanel(panel, contentY, contentWidth, contentHeight);
                    break;
                case "Team":
                    var teamUI = new TeamInviteUI();
                    teamUI.PopulatePanel(panel, contentY, contentWidth, contentHeight);
                    break;
                default:
                    // 其他面板使用默认占位内容
                    var contentLabel = new Label
                    {
                        Text = $"{panelName}面板内容区域",
                        Font = UIHelper.SetFont(size: 12),
                        TextColor = new Color(0.7f, 0.7f, 0.7f),
                        Bounds = new Rectangle(10, contentY, contentWidth, contentHeight),
                        HorizontalAlignment = TextAlignment.Center,
                        VerticalAlignment = TextAlignment.Center
                    };
                    panel.AddChild(contentLabel);
                    break;
            }

            // 统一为当前面板及其所有子控件递归应用中文字体，防止动态创建的控件使用默认英文字体导致中文显示为方块
            UIHelper.ApplyChineseFontRecursive(panel);
        }

        /// <summary>
        /// 填充角色面板内容 - 魔兽世界风格三栏布局：装备 / 预览+详情 / 属性+雷达
        /// </summary>
        private void PopulateCharacterPanel(RoundedPanel panel, float startY, float width, float height)
        {
            _characterPanel = panel;
            _equipmentSlotViews.Clear();

            const float padding = 10f;
            float contentX = padding;
            float contentY = startY;
            float contentWidth = width - padding * 2;
            float contentHeight = height - startY - padding;

            // 面板宽度不足时改用上下堆叠布局，确保所有元素可见
            bool useThreeColumnLayout = width >= 750f;

            if (useThreeColumnLayout)
            {
                CreateThreeColumnCharacterPanel(panel, contentX, contentY, contentWidth, contentHeight);
            }
            else
            {
                CreateStackedCharacterPanel(panel, contentX, contentY, contentWidth, contentHeight);
            }

            // 异步加载角色数据并刷新显示
            RefreshCharacterPanelData(panel);
        }

        /// <summary>
        /// 创建角色面板主布局（增强版）
        /// 顶部：全宽金属边框标题栏（角色名、等级、职业、战力）
        /// 中部三栏：左栏装备槽位（2×4 身形排列） | 中栏 3D 预览 | 右栏属性+五行雷达图
        /// 底部：全宽背包栏
        /// </summary>
        private void CreateThreeColumnCharacterPanel(RoundedPanel panel, float x, float y, float width, float height)
        {
            const float gap = 8f;
            const float headerHeight = 52f;
            const float inventoryHeight = 118f;

            // === 顶部全宽头部（金属边框 + 顶部金线）===
            CreateCharacterHeaderPanel(panel, x, y, width, headerHeight);

            // === 中部三栏区域 ===
            float columnY = y + headerHeight + gap;
            float columnHeight = height - headerHeight - inventoryHeight - gap * 2;

            float leftWidth = 148f;
            float rightWidth = 258f;
            float middleWidth = width - leftWidth - rightWidth - gap * 2;
            middleWidth = Mathf.Max(middleWidth, 200f);

            float leftX = x;
            float middleX = leftX + leftWidth + gap;
            float rightX = middleX + middleWidth + gap;

            // 左栏：装备槽位（标题下留白，2列4行身形排列）
            _leftColumnPanel = CreateCharacterSubPanel(panel, leftX, columnY, leftWidth, columnHeight, "装备");
            _equipmentSlotsContainer = CreateEquipmentSlotsContainer(_leftColumnPanel, 2f, 28f, leftWidth - 4f, columnHeight - 30f, columns: 2, rows: 4, slotSize: 54f, spacing: 6f);

            // 中栏：3D 角色预览（金属边框凹陷面板，无标题）
            _middleColumnPanel = CreateCharacterSubPanel(panel, middleX, columnY, middleWidth, columnHeight, null);
            _previewPlaceholderPanel = CreateCharacterPreviewContainer(_middleColumnPanel, 4f, 4f, middleWidth - 8f, columnHeight - 8f);
            _previewPanel = CreatePreviewPanel(_middleColumnPanel, 4f, 4f, middleWidth - 8f, columnHeight - 8f);
            _previewPanel.Visible = false;

            // 右栏：上半基础属性列表（分组），下半五行雷达图
            _rightColumnPanel = CreateCharacterSubPanel(panel, rightX, columnY, rightWidth, columnHeight, null);

            float radarHeight = 248f;
            float attributeHeight = columnHeight - radarHeight - gap;
            attributeHeight = Mathf.Max(attributeHeight, 180f);

            _attributePanel = CreateCharacterSubPanel(_rightColumnPanel, 0f, 0f, rightWidth, attributeHeight, "属性详情");
            CreateAttributeList(_attributePanel);

            _radarContainer = CreateRadarContainer(_rightColumnPanel, 0f, attributeHeight + gap, rightWidth, radarHeight);

            // === 底部：背包（全宽，标题栏）===
            _inventoryContainer = CreateInventoryContainer(panel, x, y + headerHeight + columnHeight + gap * 2, width, inventoryHeight);
        }

        /// <summary>
        /// 创建上下堆叠布局（窄面板回退方案）
        /// </summary>
        private void CreateStackedCharacterPanel(RoundedPanel panel, float x, float y, float width, float height)
        {
            const float gap = 8f;
            const float headerHeight = 46f;

            // 顶部全宽头部
            CreateCharacterHeaderPanel(panel, x, y, width, headerHeight);

            float contentY = y + headerHeight + gap;
            float contentHeight = height - headerHeight - gap;
            float sectionHeight = (contentHeight - gap * 3) / 4f;

            // 第一行：基础属性
            _attributePanel = CreateCharacterSubPanel(panel, x, contentY, width, sectionHeight, "基础属性");
            CreateAttributeList(_attributePanel);

            // 第二行：3D 预览 + 装备插槽
            var middleY = contentY + sectionHeight + gap;
            _middleColumnPanel = CreateCharacterSubPanel(panel, x, middleY, width, sectionHeight, null);
            _previewPlaceholderPanel = CreateCharacterPreviewContainer(_middleColumnPanel, 0, 0, width * 0.5f, sectionHeight);
            _equipmentSlotsContainer = CreateEquipmentSlotsContainer(_middleColumnPanel, width * 0.5f + gap, 0, width * 0.5f - gap, sectionHeight, columns: 4, rows: 2, slotSize: 48f);
            _previewPanel = CreatePreviewPanel(_middleColumnPanel, 0, 0, width, sectionHeight);
            _previewPanel.Visible = false;

            // 第三行：五行雷达图
            var radarY = middleY + sectionHeight + gap;
            _radarContainer = CreateRadarContainer(panel, x, radarY, width, sectionHeight * 1.5f);

            // 底部：背包
            var inventoryY = radarY + sectionHeight * 1.5f + gap;
            _inventoryContainer = CreateInventoryContainer(panel, x, inventoryY, width, sectionHeight * 0.5f);
        }

        /// <summary>
        /// 创建角色面板子容器（增强版：多层金属边框 + 凹陷石质底色）
        /// </summary>
        private Panel CreateCharacterSubPanel(Panel parent, float x, float y, float width, float height, string title, Color? backgroundColor = null)
        {
            var panel = new Panel
            {
                Bounds = new Rectangle(x, y, width, height),
                BackgroundColor = backgroundColor ?? ChineseClassicalTheme.DarkStonePanelColor,
                ClipChildren = true
            };
            parent.AddChild(panel);

            // === 外层：暗铜色金属边框（2px）===
            panel.AddChild(new Panel { Bounds = new Rectangle(0, 0, width, 2), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            panel.AddChild(new Panel { Bounds = new Rectangle(0, height - 2, width, 2), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            panel.AddChild(new Panel { Bounds = new Rectangle(0, 0, 2, height), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            panel.AddChild(new Panel { Bounds = new Rectangle(width - 2, 0, 2, height), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });

            // === 内层：金色细描边（内嵌 1px，仅 top 为金色高光）===
            panel.AddChild(new Panel { Bounds = new Rectangle(2, 2, width - 4, 1), BackgroundColor = ChineseClassicalTheme.MetalBorderSoftHighlightColor });
            panel.AddChild(new Panel { Bounds = new Rectangle(2, 3, 1, height - 6), BackgroundColor = ChineseClassicalTheme.WowInnerBorderColor });
            panel.AddChild(new Panel { Bounds = new Rectangle(width - 3, 3, 1, height - 6), BackgroundColor = ChineseClassicalTheme.WowInnerBorderColor });
            panel.AddChild(new Panel { Bounds = new Rectangle(2, height - 3, width - 4, 1), BackgroundColor = ChineseClassicalTheme.WowInnerBorderColor });

            // 标题栏（仅当有标题时）
            if (!string.IsNullOrEmpty(title))
            {
                // 标题栏底色（略亮于面板）
                var titleBg = new Panel
                {
                    Bounds = new Rectangle(2, 2, width - 4, 24),
                    BackgroundColor = ChineseClassicalTheme.DarkStonePanelHighlight
                };
                panel.AddChild(titleBg);

                // 标题顶部金线
                titleBg.AddChild(new Panel
                {
                    Bounds = new Rectangle(0, 0, width - 4, 1),
                    BackgroundColor = ChineseClassicalTheme.MetalBorderSoftHighlightColor
                });

                // 标题底部金线（分隔）
                titleBg.AddChild(new Panel
                {
                    Bounds = new Rectangle(0, 22, width - 4, 1),
                    BackgroundColor = ChineseClassicalTheme.MetalBorderHighlightColor
                });

                // 左侧金色装饰块
                titleBg.AddChild(new Panel
                {
                    Bounds = new Rectangle(6, 6, 2, 12),
                    BackgroundColor = ChineseClassicalTheme.MetalBorderHighlightColor
                });

                // 标题文本（金色）
                var titleLabel = new Label
                {
                    Bounds = new Rectangle(14, 2, width - 18, 20),
                    Text = title,
                    Font = UIHelper.SetFont(size: 12),
                    TextColor = ChineseClassicalTheme.WowTitleColor,
                    HorizontalAlignment = TextAlignment.Near,
                    VerticalAlignment = TextAlignment.Center
                };
                panel.AddChild(titleLabel);
            }

            return panel;
        }

        /// <summary>
        /// 创建 3D 角色预览容器，尝试使用 CharacterPreviewPanel 渲染实际角色，失败则回退到占位文本
        /// </summary>
        private Panel CreateCharacterPreviewContainer(Panel parent, float x, float y, float width, float height)
        {
            var panel = new Panel
            {
                Bounds = new Rectangle(x, y, width, height),
                BackgroundColor = ChineseClassicalTheme.DarkStoneInsetColor,
                ClipChildren = true
            };
            parent.AddChild(panel);

            Panel loadingPanel = null;
            try
            {
                var targetScene = Actor?.Scene ?? Level.GetScene(0);
                _characterPreviewPanel = new CharacterPreviewPanel
                {
                    TargetScene = targetScene,
                    CharacterPrefabPath = "Content/Character/Models/skm_uefn_mannequin.flax"
                };
                _characterPreviewPanel.Offsets = Margin.Zero;
                _characterPreviewPanel.AnchorPreset = AnchorPresets.StretchAll;
                panel.AddChild(_characterPreviewPanel);

                // 加载状态提示层（角色加载完成后隐藏）
                loadingPanel = CreatePreviewLoadingOverlay(width, height);
                panel.AddChild(loadingPanel);

                _characterPreviewPanel.OnCharacterLoaded += () =>
                {
                    if (loadingPanel != null)
                    {
                        loadingPanel.Visible = false;
                    }
                };

                // 兜底：若 8 秒内仍未加载完成，显示占位提示并释放可能卡住的预览面板
                Scripting.InvokeOnUpdate(() =>
                {
                    if (loadingPanel != null && loadingPanel.Visible && _characterPreviewPanel != null)
                    {
                        loadingPanel.Visible = false;
                        DisposeCharacterPreview();
                        CreatePreviewFallbackContent(panel, width, height);
                    }
                });
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[GameMainUI] 创建角色预览失败: {ex.Message}");
                _characterPreviewPanel = null;
            }

            if (_characterPreviewPanel == null)
            {
                CreatePreviewFallbackContent(panel, width, height);
                loadingPanel?.Dispose();
            }

            return panel;
        }

        /// <summary>
        /// 创建预览加载状态覆盖层
        /// </summary>
        private Panel CreatePreviewLoadingOverlay(float width, float height)
        {
            var panel = new Panel
            {
                Bounds = new Rectangle(0, 0, width, height),
                BackgroundColor = new Color(0.04f, 0.04f, 0.05f, 0.75f)
            };

            var label = new Label
            {
                Text = "角色预览加载中...",
                Font = UIHelper.SetFont(size: 12),
                TextColor = ChineseClassicalTheme.WowTitleColor,
                Bounds = new Rectangle(0, 0, width, height - 30),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            panel.AddChild(label);

            var hintLabel = new Label
            {
                Text = "（拖拽旋转 · 滚轮缩放 · 右键复位）",
                Font = UIHelper.SetFont(size: 10),
                TextColor = new Color(0.7f, 0.7f, 0.7f, 0.8f),
                Bounds = new Rectangle(0, height - 40, width, 20),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            panel.AddChild(hintLabel);

            return panel;
        }

        /// <summary>
        /// 创建角色预览失败或未启用时的占位内容
        /// </summary>
        private void CreatePreviewFallbackContent(Panel parent, float width, float height)
        {
            var label = new Label
            {
                Text = "角色预览",
                Font = UIHelper.SetFont(size: 14),
                TextColor = ChineseClassicalTheme.WowTitleColor,
                Bounds = new Rectangle(0, 0, width, height),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            parent.AddChild(label);
        }

        /// <summary>
        /// 释放角色预览面板资源
        /// </summary>
        private void DisposeCharacterPreview()
        {
            if (_characterPreviewPanel != null)
            {
                try
                {
                    _characterPreviewPanel.Dispose();
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[GameMainUI] 释放角色预览失败: {ex.Message}");
                }
                _characterPreviewPanel = null;
            }
        }

        /// <summary>
        /// 创建装备插槽网格容器，按身形排列顺序实例化 8 个 EquipmentSlotView
        /// 排列顺序：头/颈 → 面/衣 → 披/腰 → 右手/左手
        /// </summary>
        private Panel CreateEquipmentSlotsContainer(Panel parent, float x, float y, float width, float height, int columns, int rows, float slotSize, float spacing = 6f)
        {
            var panel = new Panel
            {
                Bounds = new Rectangle(x, y, width, height),
                BackgroundColor = Color.Transparent,
                ClipChildren = true
            };
            parent.AddChild(panel);

            // 身形排列顺序：头颈 → 面衣 → 披腰 → 右手左手
            var bodyOrder = new[]
            {
                EquipmentSlot.Head,
                EquipmentSlot.Neck,
                EquipmentSlot.Face,
                EquipmentSlot.Body,
                EquipmentSlot.Back,
                EquipmentSlot.Waist,
                EquipmentSlot.RightHand,
                EquipmentSlot.LeftHand
            };

            float gridWidth = columns * slotSize + (columns - 1) * spacing;
            float gridHeight = rows * slotSize + (rows - 1) * spacing;
            float startX = (width - gridWidth) * 0.5f;
            float startY = (height - gridHeight) * 0.5f;

            for (int i = 0; i < bodyOrder.Length; i++)
            {
                int row = i / columns;
                int col = i % columns;
                float slotX = startX + col * (slotSize + spacing);
                float slotY = startY + row * (slotSize + spacing);

                var slot = new EquipmentSlotView(bodyOrder[i], new Float2(slotSize, slotSize))
                {
                    Location = new Float2(slotX, slotY)
                };
                slot.Clicked += OnEquipmentSlotClicked;
                panel.AddChild(slot);
                _equipmentSlotViews.Add(slot);
            }

            return panel;
        }

        /// <summary>
        /// 创建装备详情容器
        /// </summary>
        private Panel CreatePreviewPanel(Panel parent, float x, float y, float width, float height)
        {
            var panel = new Panel
            {
                Bounds = new Rectangle(x, y, width, height),
                BackgroundColor = ChineseClassicalTheme.DarkStoneInsetColor,
                ClipChildren = true,
                ScrollBars = ScrollBars.Vertical
            };
            parent.AddChild(panel);

            var emptyLabel = new Label
            {
                Text = "点击装备查看详情",
                Font = UIHelper.SetFont(size: 12),
                TextColor = ChineseClassicalTheme.WowAttributeTextColor,
                Bounds = new Rectangle(0, 0, width, height),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            panel.AddChild(emptyLabel);

            return panel;
        }

        /// <summary>
        /// 创建背包容器（底部全宽物品栏）
        /// </summary>
        private Panel CreateInventoryContainer(RoundedPanel parent, float x, float y, float width, float height)
        {
            var panel = CreateCharacterSubPanel(parent, x, y, width, height, "背包");

            _inventoryContentPanel = new Panel
            {
                Bounds = new Rectangle(0, 28, width, height - 28),
                BackgroundColor = Color.Transparent,
                ClipChildren = true
            };
            panel.AddChild(_inventoryContentPanel);

            var inventoryUI = new InventoryUI();
            inventoryUI.PopulateEmbeddedPanel(_inventoryContentPanel, _characterInventoryItems, OnInventoryItemClicked);

            return panel;
        }

        /// <summary>
        /// 创建五行雷达图容器（增强版：使用统一的子面板风格）
        /// </summary>
        private Panel CreateRadarContainer(Panel parent, float x, float y, float width, float height)
        {
            var panel = CreateCharacterSubPanel(parent, x, y, width, height, "五行属性");

            // 雷达图尺寸：居中，留出标题栏 28px 和内边距
            float innerPad = 8f;
            float availableW = width - innerPad * 2;
            float availableH = height - 28 - innerPad;
            float chartSize = Mathf.Min(availableW, availableH);
            chartSize = Mathf.Max(chartSize, 100f);

            _wuxingRadarChart = new WuxingRadarChart
            {
                Size = new Float2(chartSize, chartSize),
                Location = new Float2((width - chartSize) * 0.5f, 28 + (availableH - chartSize) * 0.5f)
            };
            _wuxingRadarChart.SetValues(30, 30, 30, 30, 30);
            panel.AddChild(_wuxingRadarChart);

            return panel;
        }

        /// <summary>
        /// 创建全宽角色头部信息栏（增强版）
        /// 金属边框 + 顶部金色发光线 + 左侧角色名（金色发光）+ 右侧等级/职业 + 右侧战力高亮
        /// </summary>
        private void CreateCharacterHeaderPanel(Panel parent, float x, float y, float width, float height)
        {
            var headerPanel = new Panel
            {
                Bounds = new Rectangle(x, y, width, height),
                BackgroundColor = ChineseClassicalTheme.DarkStonePanelColor,
                ClipChildren = true
            };
            parent.AddChild(headerPanel);

            // 外层金属边框
            headerPanel.AddChild(new Panel { Bounds = new Rectangle(0, 0, width, 2), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            headerPanel.AddChild(new Panel { Bounds = new Rectangle(0, height - 2, width, 2), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            headerPanel.AddChild(new Panel { Bounds = new Rectangle(0, 0, 2, height), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            headerPanel.AddChild(new Panel { Bounds = new Rectangle(width - 2, 0, 2, height), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });

            // 顶部金色发光线（模拟金属反光）
            headerPanel.AddChild(new Panel { Bounds = new Rectangle(2, 2, width - 4, 1), BackgroundColor = ChineseClassicalTheme.MetalBorderSoftHighlightColor });

            // 左侧：角色名称（金色）
            var nameLabel = new Label
            {
                Text = _currentCharacter?.CharacterName ?? "玩家",
                Font = UIHelper.SetFont(size: 14),
                TextColor = ChineseClassicalTheme.WowTitleColor,
                Bounds = new Rectangle(12, 6, width * 0.5f, 26),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Tag = "CharacterName"
            };
            headerPanel.AddChild(nameLabel);

            // 左侧：等级与职业（次级文本色）
            var levelLabel = new Label
            {
                Text = $"Lv.{_currentCharacter?.Level ?? 1}  ·  江湖侠客",
                Font = UIHelper.SetFont(size: 11),
                TextColor = ChineseClassicalTheme.WowSubTextColor,
                Bounds = new Rectangle(12, 28, width * 0.5f, 20),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
                Tag = "CharacterLevelClass"
            };
            headerPanel.AddChild(levelLabel);

            // 右侧：战力标签（金属小盒子）
            float powerBoxW = 150f;
            float powerBoxH = height - 12;
            float powerBoxX = width - powerBoxW - 8f;
            float powerBoxY = 6f;

            var powerBox = new Panel
            {
                Bounds = new Rectangle(powerBoxX, powerBoxY, powerBoxW, powerBoxH),
                BackgroundColor = ChineseClassicalTheme.DarkStoneInsetColor
            };
            headerPanel.AddChild(powerBox);

            // 战力盒子金属边框
            powerBox.AddChild(new Panel { Bounds = new Rectangle(0, 0, powerBoxW, 1), BackgroundColor = ChineseClassicalTheme.MetalBorderHighlightColor });
            powerBox.AddChild(new Panel { Bounds = new Rectangle(0, powerBoxH - 1, powerBoxW, 1), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            powerBox.AddChild(new Panel { Bounds = new Rectangle(0, 0, 1, powerBoxH), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            powerBox.AddChild(new Panel { Bounds = new Rectangle(powerBoxW - 1, 0, 1, powerBoxH), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });

            // 战力标签（次级）
            var powerLabelTag = new Label
            {
                Text = "战力",
                Font = UIHelper.SetFont(size: 10),
                TextColor = ChineseClassicalTheme.WowSubTextColor,
                Bounds = new Rectangle(0, 4, powerBoxW * 0.4f, powerBoxH - 8),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
            powerBox.AddChild(powerLabelTag);

            // 分隔线（在战力盒子里）
            powerBox.AddChild(new Panel { Bounds = new Rectangle(powerBoxW * 0.4f, 6, 1, powerBoxH - 12), BackgroundColor = ChineseClassicalTheme.MetalBorderHighlightColor });

            // 战力数值（金色大号）
            var powerValueLabel = new Label
            {
                Text = "计算中...",
                Font = UIHelper.SetFont(size: 14),
                TextColor = ChineseClassicalTheme.WowTitleColor,
                Bounds = new Rectangle(powerBoxW * 0.4f, 2, powerBoxW * 0.6f, powerBoxH - 4),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Tag = "CombatPower"
            };
            powerBox.AddChild(powerValueLabel);
        }

        /// <summary>
        /// 在属性面板创建分组属性列表：基础四维 → 战斗属性 → 战力评分（增强版）
        /// </summary>
        private void CreateAttributeList(Panel parent)
        {
            float width = parent.Width;
            float rowHeight = 18f;
            float padding = 10f;
            float labelWidth = (width - padding * 2) * 0.5f;
            float currentY = 30f;

            var defaultAttributes = CharacterAttributes.GetDefault();

            // === 分组 1：基础四维（力量 / 敏捷 / 智力 / 体质）===
            currentY = AddSectionHeader(parent, "基础四维", currentY, width);
            currentY = AddAttributeRow(parent, "力量", defaultAttributes.Strength, padding, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "敏捷", defaultAttributes.Agility, padding + labelWidth, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "智力", defaultAttributes.Intelligence, padding, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "体质", defaultAttributes.Constitution, padding + labelWidth, currentY, labelWidth, rowHeight);

            currentY += 2f;

            // === 分组 2：五行属性（金木水火土）===
            currentY = AddSectionHeader(parent, "五行属性", currentY, width);
            currentY = AddAttributeRow(parent, "金", defaultAttributes.Metal, padding, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "木", defaultAttributes.Wood, padding + labelWidth, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "水", defaultAttributes.Water, padding, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "火", defaultAttributes.Fire, padding + labelWidth, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "土", defaultAttributes.Earth, padding, currentY, labelWidth, rowHeight);

            currentY += 2f;

            // === 分组 3：战斗属性（攻 / 防 / 血 / 内力）===
            currentY = AddSectionHeader(parent, "战斗属性", currentY, width);
            currentY = AddAttributeRow(parent, "攻击力", defaultAttributes.Attack, padding, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "防御力", defaultAttributes.Defense, padding + labelWidth, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "生命值", defaultAttributes.HP, padding, currentY, labelWidth, rowHeight);
            currentY = AddAttributeRow(parent, "内力值", defaultAttributes.MP, padding + labelWidth, currentY, labelWidth, rowHeight);

            currentY += 2f;

            // === 分组 4：战力评分（全宽高亮，金色大字）===
            currentY = AddSectionHeader(parent, "战力评分", currentY, width);
            var combatRowBg = new Panel
            {
                Bounds = new Rectangle(padding - 2, currentY, width - padding * 2 + 4, 30),
                BackgroundColor = ChineseClassicalTheme.DarkStoneInsetColor
            };
            parent.AddChild(combatRowBg);

            // 战力盒子金属边框
            combatRowBg.AddChild(new Panel { Bounds = new Rectangle(0, 0, combatRowBg.Width, 1), BackgroundColor = ChineseClassicalTheme.MetalBorderHighlightColor });
            combatRowBg.AddChild(new Panel { Bounds = new Rectangle(0, combatRowBg.Height - 1, combatRowBg.Width, 1), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            combatRowBg.AddChild(new Panel { Bounds = new Rectangle(0, 0, 1, combatRowBg.Height), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });
            combatRowBg.AddChild(new Panel { Bounds = new Rectangle(combatRowBg.Width - 1, 0, 1, combatRowBg.Height), BackgroundColor = ChineseClassicalTheme.MetalBorderColor });

            var combatLabel = new Label
            {
                Bounds = new Rectangle(8, 6, 100, 18),
                Text = "总战力",
                Font = UIHelper.SetFont(size: 11),
                TextColor = ChineseClassicalTheme.WowSectionHeaderColor,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            combatRowBg.AddChild(combatLabel);

            var combatValue = new Label
            {
                Bounds = new Rectangle(combatRowBg.Width - 130, 2, 120, 26),
                Text = CalculateCombatPower(defaultAttributes).ToString("F0"),
                Font = UIHelper.SetFont(size: 14),
                TextColor = ChineseClassicalTheme.WowTitleColor,
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Tag = "CombatPowerAttr"
            };
            combatRowBg.AddChild(combatValue);
        }

        /// <summary>
        /// 添加属性分组标题（增强版：暗金线 + 左侧色块 + 标题）
        /// </summary>
        private float AddSectionHeader(Panel parent, string title, float y, float width)
        {
            // 左侧金色装饰块
            parent.AddChild(new Panel
            {
                Bounds = new Rectangle(8, y + 4, 3, 10),
                BackgroundColor = ChineseClassicalTheme.MetalBorderHighlightColor
            });

            // 标题文本
            var label = new Label
            {
                Text = title,
                Font = UIHelper.SetFont(size: 11),
                TextColor = ChineseClassicalTheme.WowSectionHeaderColor,
                Bounds = new Rectangle(16, y, width - 20, 18),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            parent.AddChild(label);

            // 标题右侧虚线
            parent.AddChild(new Panel
            {
                Bounds = new Rectangle(16 + title.Length * 11 + 8, y + 9, width - 16 - title.Length * 11 - 16, 1),
                BackgroundColor = ChineseClassicalTheme.WowDividerColor
            });

            return y + 20f;
        }

        /// <summary>
        /// 添加单行属性行（增强版：属性名左对齐灰色 + 数值右对齐金色）
        /// </summary>
        private float AddAttributeRow(Panel parent, string attrName, float value, float x, float y, float colWidth, float rowHeight)
        {
            // 属性名（次级文本色，左）
            var nameLabel = new Label
            {
                Text = attrName,
                Font = UIHelper.SetFont(size: 11),
                TextColor = ChineseClassicalTheme.WowSubTextColor,
                Bounds = new Rectangle(x, y, colWidth - 40, rowHeight),
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center
            };
            parent.AddChild(nameLabel);

            // 数值（金色，右）
            var valueLabel = new Label
            {
                Text = value.ToString("F0"),
                Font = UIHelper.SetFont(size: 11),
                TextColor = ChineseClassicalTheme.WowAttributeTextColor,
                Bounds = new Rectangle(x + colWidth - 46, y, 40, rowHeight),
                HorizontalAlignment = TextAlignment.Far,
                VerticalAlignment = TextAlignment.Center,
                Tag = attrName
            };
            parent.AddChild(valueLabel);

            return y + rowHeight;
        }

        /// <summary>
        /// 异步加载角色数据并刷新角色面板显示
        /// </summary>
        private async void RefreshCharacterPanelData(RoundedPanel panel)
        {
            try
            {
                ulong characterId = _currentCharacter?.CharacterId ?? 0;

                // 无角色时使用默认数据，避免阻塞 UI
                if (_currentCharacter == null || characterId == 0)
                {
                    _characterAppearanceData = AppearanceData.GetDefaultAppearance();
                    _characterAttributes = CharacterAttributes.GetDefault();
                    _characterInventoryItems = CreateDefaultInventoryItems();
                }
                else
                {
                    // 并行加载外观、属性与背包数据
                    var appearanceTask = CharacterPersistenceService.Instance.LoadAppearanceAsync(characterId);
                    var attributesTask = CharacterPersistenceService.Instance.LoadCharacterAttributesAsync(characterId);
                    var inventoryTask = CharacterPersistenceService.Instance.LoadInventoryAsync(characterId);

                    await Task.WhenAll(appearanceTask, attributesTask, inventoryTask);

                    _characterAppearanceData = await appearanceTask;
                    _characterAttributes = await attributesTask;
                    _characterInventoryItems = await inventoryTask ?? new List<InventoryItemData>();
                }

                // 如果面板已被关闭或替换，则放弃刷新
                if (_characterPanel != panel) return;

                // 异步加载完成后，将 UI 刷新调度到主线程执行，避免非 UI 线程访问控件
                Scripting.InvokeOnUpdate(() =>
                {
                    if (_characterPanel != panel) return;

                    RefreshCharacterHeader();
                    RefreshAttributeLabels(_characterAttributes);
                    RefreshEquipmentSlots();
                    RefreshInventory();
                    RefreshWuxingRadar();
                });
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[GameMainUI] 刷新角色面板数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建默认测试背包物品，确保角色面板始终有可展示内容
        /// </summary>
        private List<InventoryItemData> CreateDefaultInventoryItems()
        {
            return new List<InventoryItemData>
            {
                new InventoryItemData { ItemId = 10001, Count = 1 },
                new InventoryItemData { ItemId = 20001, Count = 1 },
                new InventoryItemData { ItemId = 10002, Count = 3 },
                new InventoryItemData { ItemId = 10003, Count = 5 }
            };
        }

        /// <summary>
        /// 刷新基础属性与战力评分
        /// </summary>
        private void RefreshAttributeLabels(CharacterAttributes attributes)
        {
            if (_attributePanel == null) return;

            var attrMap = new Dictionary<string, float>
            {
                { "力量", attributes.Strength },
                { "敏捷", attributes.Agility },
                { "智力", attributes.Intelligence },
                { "体质", attributes.Constitution },
                { "金", attributes.Metal },
                { "木", attributes.Wood },
                { "水", attributes.Water },
                { "火", attributes.Fire },
                { "土", attributes.Earth },
                { "攻击力", attributes.Attack },
                { "防御力", attributes.Defense },
                { "生命值", attributes.HP },
                { "内力值", attributes.MP }
            };

            // 1. 遍历属性面板及其子孙控件（包含战斗盒子）
            void UpdateLabel(ContainerControl parent)
            {
                if (parent == null) return;
                foreach (var child in parent.Children)
                {
                    if (child is Label label && label.Tag is string tag && attrMap.TryGetValue(tag, out float val))
                    {
                        label.Text = val.ToString("F0");
                    }
                    if (child is ContainerControl childContainer)
                    {
                        UpdateLabel(childContainer);
                    }
                }
            }
            UpdateLabel(_attributePanel);

            // 2. 计算战力评分（从 CharacterAttributes 计算）
            float combatPower = CalculateCombatPower(attributes);

            // 3. 在整个主面板中找到战力标签并更新（包含属性面板里的战力值）
            if (_attributePanel != null)
                UpdateCombatPowerLabel(_attributePanel, combatPower);
            if (_characterPanel != null)
                UpdateCombatPowerLabel(_characterPanel, combatPower);
        }

        /// <summary>
        /// 递归查找并更新战力标签
        /// </summary>
        private void UpdateCombatPowerLabel(ContainerControl container, float combatPower)
        {
            foreach (var child in container.Children)
            {
                if (child is Label label && label.Tag is string tag && tag == "CombatPower")
                {
                    label.Text = ((int)combatPower).ToString();
                    return;
                }

                if (child is ContainerControl childContainer)
                    UpdateCombatPowerLabel(childContainer, combatPower);
            }
        }

        /// <summary>
        /// 根据角色属性计算综合战力评分
        /// </summary>
        private static float CalculateCombatPower(CharacterAttributes attributes)
        {
            return attributes.Strength * 10f
                 + attributes.Agility * 10f
                 + attributes.Intelligence * 10f
                 + attributes.Constitution * 10f
                 + attributes.Metal * 5f
                 + attributes.Wood * 5f
                 + attributes.Water * 5f
                 + attributes.Fire * 5f
                 + attributes.Earth * 5f
                 + attributes.Attack * 2f
                 + attributes.Defense * 4f
                 + attributes.HP * 0.2f
                 + attributes.MP * 0.2f;
        }

        /// <summary>
        /// 刷新角色头部信息（名字、等级）- 遍历主面板查找带 Tag 的标签
        /// </summary>
        private void RefreshCharacterHeader()
        {
            if (_characterPanel == null) return;

            RefreshHeaderLabels(_characterPanel);
        }

        /// <summary>
        /// 递归查找并刷新角色头部标签
        /// </summary>
        private void RefreshHeaderLabels(ContainerControl container)
        {
            foreach (var child in container.Children)
            {
                if (child is Label label && label.Tag is string tag)
                {
                    switch (tag)
                    {
                        case "CharacterName":
                            label.Text = _currentCharacter?.CharacterName ?? "玩家";
                            break;
                        case "CharacterLevelClass":
                            label.Text = $"Lv.{_currentCharacter?.Level ?? 1}  ·  江湖侠客";
                            break;
                    }
                }

                if (child is ContainerControl childContainer)
                {
                    RefreshHeaderLabels(childContainer);
                }
            }
        }

        /// <summary>
        /// 刷新装备插槽显示
        /// </summary>
        private void RefreshEquipmentSlots()
        {
            if (_characterAppearanceData?.EquippedItems == null) return;

            foreach (var slotView in _equipmentSlotViews)
            {
                if (_characterAppearanceData.EquippedItems.TryGetValue(slotView.Slot, out int equipmentId))
                {
                    var equipment = EquipmentDatabase.GetEquipment(equipmentId);
                    slotView.Refresh(equipment);
                }
                else
                {
                    slotView.Refresh(null);
                }
            }
        }

        /// <summary>
        /// 刷新背包内嵌显示（仅刷新内容面板，保留标题与金属边框）
        /// </summary>
        private void RefreshInventory()
        {
            if (_inventoryContentPanel == null) return;

            // 清空内容面板并重新填充背包格子
            while (_inventoryContentPanel.HasChildren)
            {
                _inventoryContentPanel.RemoveChild(_inventoryContentPanel.Children[0]);
            }

            var inventoryUI = new InventoryUI();
            inventoryUI.PopulateEmbeddedPanel(_inventoryContentPanel, _characterInventoryItems, OnInventoryItemClicked);
        }

        /// <summary>
        /// 刷新五行雷达图
        /// </summary>
        private void RefreshWuxingRadar()
        {
            _wuxingRadarChart?.SetValues(_characterAttributes);
        }

        /// <summary>
        /// 获取指定槽位当前穿戴的装备数据
        /// </summary>
        private EquipmentData GetEquippedData(EquipmentSlot slot)
        {
            if (_characterAppearanceData?.EquippedItems != null &&
                _characterAppearanceData.EquippedItems.TryGetValue(slot, out int equipmentId))
            {
                return EquipmentDatabase.GetEquipment(equipmentId);
            }
            return null;
        }

        /// <summary>
        /// 装备插槽点击事件 - 显示当前装备详情与卸下按钮
        /// </summary>
        private void OnEquipmentSlotClicked(EquipmentSlotView sender)
        {
            var currentEquipment = GetEquippedData(sender.Slot);
            if (_previewPanel != null)
                _previewPanel.Visible = true;

            var comparisonUI = new EquipmentComparisonUI();
            comparisonUI.PopulateEmbeddedPreview(_previewPanel, currentEquipment, currentEquipment, () => OnUnequip(sender.Slot), () => OnUnequip(sender.Slot));
        }

        /// <summary>
        /// 背包物品点击事件 - 若可装备则显示对比与穿戴按钮
        /// </summary>
        private void OnInventoryItemClicked(int itemId)
        {
            var selectedEquipment = EquipmentDatabase.GetEquipment(itemId);
            if (selectedEquipment == null) return;

            var currentEquipment = GetEquippedData(selectedEquipment.Slot);
            if (_previewPanel != null)
                _previewPanel.Visible = true;

            var comparisonUI = new EquipmentComparisonUI();
            comparisonUI.PopulateEmbeddedPreview(_previewPanel, currentEquipment, selectedEquipment, () => OnEquipItem(itemId), () => OnUnequip(selectedEquipment.Slot));
        }

        /// <summary>
        /// 穿戴指定装备，更新外观、背包并保存
        /// </summary>
        private async void OnEquipItem(int itemId)
        {
            try
            {
                var equipment = EquipmentDatabase.GetEquipment(itemId);
                if (equipment == null || _characterAppearanceData == null) return;

                var slot = equipment.Slot;
                int previousEquipmentId = _characterAppearanceData.EquippedItems.TryGetValue(slot, out int prevId) ? prevId : 0;

                // 穿戴新装备
                _characterAppearanceData.EquippedItems[slot] = itemId;

                // 从背包扣除一个该物品
                var inventoryItem = _characterInventoryItems.Find(i => i.ItemId == itemId);
                if (inventoryItem != null)
                {
                    inventoryItem.Count--;
                    if (inventoryItem.Count <= 0)
                    {
                        _characterInventoryItems.Remove(inventoryItem);
                    }
                }

                // 若该槽位原先有装备，将其放回背包
                if (previousEquipmentId > 0 && previousEquipmentId != itemId)
                {
                    var prevItem = _characterInventoryItems.Find(i => i.ItemId == previousEquipmentId);
                    if (prevItem != null)
                    {
                        prevItem.Count++;
                    }
                    else
                    {
                        _characterInventoryItems.Add(new InventoryItemData { ItemId = previousEquipmentId, Count = 1 });
                    }
                }

                // 保存数据
                ulong characterId = _currentCharacter?.CharacterId ?? 0;
                if (characterId > 0)
                {
                    await CharacterPersistenceService.Instance.SaveAppearanceAsync(characterId, _characterAppearanceData);
                    await CharacterPersistenceService.Instance.SaveInventoryAsync(characterId, _characterInventoryItems);
                }

                // 刷新 UI
                RefreshEquipmentSlots();
                RefreshInventory();
                RefreshWuxingRadar();

                // 刷新预览面板为当前装备的单装备视图
                var comparisonUI = new EquipmentComparisonUI();
                comparisonUI.PopulateEmbeddedPreview(_previewPanel, equipment, equipment, () => OnUnequip(slot), () => OnUnequip(slot));
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[GameMainUI] 穿戴装备失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 卸下指定槽位装备，更新外观、背包并保存
        /// </summary>
        private async void OnUnequip(EquipmentSlot slot)
        {
            try
            {
                if (_characterAppearanceData?.EquippedItems == null) return;
                if (!_characterAppearanceData.EquippedItems.TryGetValue(slot, out int itemId)) return;

                _characterAppearanceData.EquippedItems.Remove(slot);

                // 将卸下的装备放回背包
                var existingItem = _characterInventoryItems.Find(i => i.ItemId == itemId);
                if (existingItem != null)
                {
                    existingItem.Count++;
                }
                else
                {
                    _characterInventoryItems.Add(new InventoryItemData { ItemId = itemId, Count = 1 });
                }

                // 保存数据
                ulong characterId = _currentCharacter?.CharacterId ?? 0;
                if (characterId > 0)
                {
                    await CharacterPersistenceService.Instance.SaveAppearanceAsync(characterId, _characterAppearanceData);
                    await CharacterPersistenceService.Instance.SaveInventoryAsync(characterId, _characterInventoryItems);
                }

                // 刷新 UI
                RefreshEquipmentSlots();
                RefreshInventory();
                RefreshWuxingRadar();

                // 清空预览面板
                _previewPanel?.DisposeChildren();
                var emptyLabel = new Label
                {
                    Text = "点击装备或背包物品查看详情",
                    Font = UIHelper.SetFont(size: 12),
                    TextColor = new Color(0.6f, 0.6f, 0.6f),
                    Bounds = new Rectangle(0, 0, _previewPanel?.Width ?? 200, _previewPanel?.Height ?? 100),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center
                };
                _previewPanel?.AddChild(emptyLabel);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[GameMainUI] 卸下装备失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 填充设置面板内容 - 音效、画质、操作设置
        /// </summary>
        private void PopulateSettingsPanel(RoundedPanel panel, float startY, float width, float height)
        {
            float y = startY;

            // === 音频设置 ===
            var audioTitle = new Label
            {
                Text = "── 音频设置 ──",
                Font = UIHelper.SetFont(size: 14),
                TextColor = new Color(1.0f, 0.84f, 0.0f),
                Bounds = new Rectangle(10, y, width, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(audioTitle);
            y += 30;

            // 主音量
            AddSettingsSliderRow(panel, "主音量", 80, ref y, width);
            // 音效音量
            AddSettingsSliderRow(panel, "音效", 70, ref y, width);
            // 音乐音量
            AddSettingsSliderRow(panel, "音乐", 60, ref y, width);

            y += 10;

            // === 画质设置 ===
            var graphicsTitle = new Label
            {
                Text = "── 画质设置 ──",
                Font = UIHelper.SetFont(size: 14),
                TextColor = new Color(1.0f, 0.84f, 0.0f),
                Bounds = new Rectangle(10, y, width, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(graphicsTitle);
            y += 30;

            // 画质等级
            AddSettingsSliderRow(panel, "画质", 75, ref y, width);
            // 视距
            AddSettingsSliderRow(panel, "视距", 60, ref y, width);
            // 特效密度
            AddSettingsSliderRow(panel, "特效", 80, ref y, width);

            y += 10;

            // === 操作设置 ===
            var controlTitle = new Label
            {
                Text = "── 操作设置 ──",
                Font = UIHelper.SetFont(size: 14),
                TextColor = new Color(1.0f, 0.84f, 0.0f),
                Bounds = new Rectangle(10, y, width, 25),
                HorizontalAlignment = TextAlignment.Center
            };
            panel.AddChild(controlTitle);
            y += 30;

            // 鼠标灵敏度
            AddSettingsSliderRow(panel, "灵敏度", 50, ref y, width);
        }

        /// <summary>
        /// 添加设置面板滑条行
        /// </summary>
        private void AddSettingsSliderRow(RoundedPanel panel, string label, int defaultValue, ref float y, float width)
        {
            var nameLabel = new Label
            {
                Text = label,
                Font = UIHelper.SetFont(size: 12),
                TextColor = Color.White,
                Bounds = new Rectangle(15, y, 70, 22),
                HorizontalAlignment = TextAlignment.Near
            };
            panel.AddChild(nameLabel);

            float barMaxWidth = width - 150;
            float barWidth = barMaxWidth * (defaultValue / 100f);

            // 滑条背景
            var barBg = new Panel
            {
                Bounds = new Rectangle(90, y + 3, barMaxWidth, 16),
                BackgroundColor = new Color(0.15f, 0.15f, 0.2f, 0.8f)
            };
            panel.AddChild(barBg);

            // 滑条填充
            var barFill = new Panel
            {
                Bounds = new Rectangle(90, y + 3, barWidth, 16),
                BackgroundColor = new Color(0.3f, 0.7f, 1.0f, 0.8f)
            };
            panel.AddChild(barFill);

            // 数值标签
            var valueLabel = new Label
            {
                Text = $"{defaultValue}%",
                Font = UIHelper.SetFont(size: 12),
                TextColor = Color.White,
                Bounds = new Rectangle(width - 55, y, 50, 22),
                HorizontalAlignment = TextAlignment.Far
            };
            panel.AddChild(valueLabel);

            y += 26;
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
            // 检查是否需要重新初始化 UI（场景切换后 UICanvas 可能已被销毁）
            if (!_uiInitialized || _mainContainer == null || _mainContainer.Parent == null)
            {
                FlaxEngine.Debug.LogWarning("[GameMainUI] UI 未初始化或已失效，重新初始化...");
                _uiInitialized = false;
                InitializeUI();
            }

            if (_mainContainer == null || _mainContainer.Parent == null)
            {
                FlaxEngine.Debug.LogError("[GameMainUI] UI 初始化失败，无法显示游戏主界面");
                return;
            }

            _mainContainer.Visible = true;
            
            // 播放显示动画
            _animationManager.FadeIn(_mainContainer, 0.5f);
            
            // 更新角色信息（UI 重建后控件是新创建的，必须刷新 HUD）
            if (_stateManager.SelectedCharacter != null)
            {
                _currentCharacter = _stateManager.SelectedCharacter;
            }
            // 无论 SelectedCharacter 是否为 null，只要 _currentCharacter 有值就刷新 HUD
            if (_currentCharacter != null)
            {
                UpdateHUD();
            }
            
            FlaxEngine.Debug.Log("游戏主界面已显示");
        }
        
        /// <summary>
        /// 隐藏游戏主界面
        /// </summary>
        public void HideGameMainUI()
        {
            if (_mainContainer == null) return;

            _animationManager.FadeOut(_mainContainer, 0.3f, EasingType.EaseOut, () => {
                if (_mainContainer != null)
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