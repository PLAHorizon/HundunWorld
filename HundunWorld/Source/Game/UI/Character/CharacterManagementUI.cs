using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.Animation;
using HundunWorld.Game.UI.ErrorHandling;
using HundunWorld.Game.UI.Guidance;
using HundunWorld.Game.UI.GameMain;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.Services;
using HundunWorld.Game.Network;
using TouchSocket.Core;

namespace HundunWorld.Game.UI.Character
{
    /// <summary>
    /// 角色管理界面 - 重构版本
    /// 提供角色列表显示、创建、删除和选择功能
    /// 集成状态管理、动画效果和用户引导
    /// 
    /// [DEPRECATED - 已废弃]
    /// 此类已被废弃，不再使用。请使用以下替代方案：
    /// - CharacterSelectionUI: 角色选择界面
    /// - CharacterCreationUI: 角色创建界面
    /// 
    /// 由UISceneManager统一管理场景切换和显社隐藏逻辑。
    /// </summary>
    [Obsolete("CharacterManagementUI has been deprecated. Use CharacterSelectionUI and CharacterCreationUI instead.")]
    public class CharacterManagementUI : Script
    {
        // 核心管理器
        private UIStateManager _stateManager;
        private UIAnimationManager _animationManager;
        private HundunWorld.Game.UI.ErrorHandling.ErrorHandlingManager _errorManager;
        private UserGuidanceManager _guidanceManager;
        private CharacterService _characterService;
        private NetworkManager _networkManager;

        // UI容器
        private ContainerControl _mainContainer;
        private Panel _characterListPanel;
        private Panel _createCharacterPanel;
        private LoadingIndicator _loadingIndicator;

        // 角色列表组件
        private Button _createNewCharacterButton;
        private Button _backToLoginButton;
        private Label _titleLabel;
        private ScrollableControl _characterScrollView;

        // 角色创建面板组件
        private ValidatedTextBox _characterNameInput;
        private Dropdown _professionDropdown;
        private Dropdown _genderDropdown;
        private Button _confirmCreateButton;
        private Button _cancelCreateButton;
        private Panel _appearancePanel;

        // 外观选择控件
        private Dropdown _hairModelDropdown;
        private Dropdown _hairColorDropdown;
        private Dropdown _faceModelDropdown;
        private Dropdown _clothingDropdown;

        // 数据
        private List<CharacterInfo> _characters = new List<CharacterInfo>();
        private bool _isProcessing = false;

        // 预设数据
        private readonly string[] _professions = { "剑客", "刀客", "拳师", "医师", "毒师" };
        private readonly string[] _genders = { "男", "女" };

        public override void OnStart()
        {
            InitializeManagers();
            InitializeUI();
            SubscribeEvents();

            FlaxEngine.Debug.Log("角色管理界面重构版初始化完成");
        }

        /// <summary>
        /// 初始化管理器
        /// </summary>
        private void InitializeManagers()
        {
            _stateManager = UIStateManager.Instance;
            _animationManager = UIAnimationManager.Instance;
            _errorManager = HundunWorld.Game.UI.ErrorHandling.ErrorHandlingManager.Instance;
            _guidanceManager = UserGuidanceManager.Instance;
            _characterService = CharacterService.Instance;
            _networkManager = HundunWorldGame.Instance.NetworkManager;
        }

        /// <summary>
        /// 订阅事件
        /// </summary>
        private void SubscribeEvents()
        {
            _stateManager.SceneChanged += OnSceneChanged;
            _stateManager.LoadingStateChanged += OnLoadingStateChanged;
            _stateManager.CharacterListUpdated += OnCharacterListUpdated;
            _stateManager.SelectedCharacterChanged += OnSelectedCharacterChanged;
        }

        /// <summary>
        /// 场景切换事件处理
        /// </summary>
        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            FlaxEngine.Debug.Log($"[CharacterManagementUI] 场景切换事件: {previousScene} -> {newScene}");
            
            switch (newScene)
            {
                case SceneType.CharacterSelection:
                    // 确保主容器可见
                    if (_mainContainer != null)
                    {
                        _mainContainer.Visible = true;
                    }
                    ShowCharacterList();
                    FlaxEngine.Debug.Log("[CharacterManagementUI] 显示角色选择界面");
                    break;
                case SceneType.CharacterCreation:
                    // 确保主容器可见
                    if (_mainContainer != null)
                    {
                        _mainContainer.Visible = true;
                    }
                    ShowCharacterCreation();
                    FlaxEngine.Debug.Log("[CharacterManagementUI] 显示角色创建界面");
                    break;
                default:
                    // 其他场景隐藏角色管理界面
                    if (_mainContainer != null)
                    {
                        _mainContainer.Visible = false;
                    }
                    break;
            }
        }

        /// <summary>
        /// 加载状态变化事件处理
        /// </summary>
        private void OnLoadingStateChanged(bool isLoading)
        {
            if (isLoading)
            {
                _loadingIndicator.Show("正在处理请求...");
            }
            else
            {
                _loadingIndicator.Hide();
            }
        }

        /// <summary>
        /// 角色列表更新事件处理
        /// </summary>
        private void OnCharacterListUpdated(List<CharacterInfo> characters)
        {
            _characters = characters;
            RefreshCharacterList();
        }

        /// <summary>
        /// 选中角色变化事件处理
        /// </summary>
        private void OnSelectedCharacterChanged(CharacterInfo character)
        {
            // 更新UI选中状态
            RefreshCharacterList();
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
               // BackgroundColor = UIHelper.BackgroundColor
            };

            // 创建加载指示器
            _loadingIndicator = UIHelper.CreateLoadingIndicator();
            _mainContainer.AddChild(_loadingIndicator);

            // 将主容器添加到GUI
            var uiCanvas = FindUICanvas();
            if (uiCanvas?.GUI != null)
            {
                uiCanvas.GUI.AnchorPreset = AnchorPresets.StretchAll;
                uiCanvas.GUI.AddChild(_mainContainer);
                FlaxEngine.Debug.Log("成功添加认证UI主容器到GUI");
            }

            CreateCharacterListPanel();
            CreateCharacterCreationPanel();
            _mainContainer.Visible = false;


        }
        public void Show()
        {
            _mainContainer.Visible = true;
            // 默认显示角色列表
            ShowCharacterList();
        }

        public void Hide()
        {
            _mainContainer.Visible = false;
        }

        /// <summary>
        /// 创建角色列表面板
        /// </summary>
        private void CreateCharacterListPanel()
        {
            _characterListPanel = UIHelper.CreatePanel(new Float2(800, 600));
            _characterListPanel.AnchorPreset = AnchorPresets.StretchAll;
            _characterListPanel.BackgroundColor = Color.Transparent;

            // 标题
            _titleLabel = UIHelper.CreateTitleLabel("选择角色", 20);
            _titleLabel.Bounds = new Rectangle(0, 50, 800, 60);
            _characterListPanel.AddChild(_titleLabel);

            // 角色列表滚动视图
            _characterScrollView = new ScrollableControl
            {
                Bounds = new Rectangle(100, 150, 600, 400),
                BackgroundColor = UIHelper.PanelColor
            };
            _characterListPanel.AddChild(_characterScrollView);

            // 创建新角色按钮
            _createNewCharacterButton = UIHelper.CreatePrimaryButton("创建新角色");
            _createNewCharacterButton.Bounds = new Rectangle(200, 580, 150, 40);
            _createNewCharacterButton.ButtonClicked += OnCreateNewCharacterClicked;
            _characterListPanel.AddChild(_createNewCharacterButton);

            // 返回登录按钮
            _backToLoginButton = UIHelper.CreateSecondaryButton("返回登录");
            _backToLoginButton.Bounds = new Rectangle(450, 580, 150, 40);
            _backToLoginButton.ButtonClicked += OnBackToLoginClicked;
            _characterListPanel.AddChild(_backToLoginButton);

            _mainContainer.AddChild(_characterListPanel);
        }

        /// <summary>
        /// 创建角色创建面板
        /// </summary>
        private void CreateCharacterCreationPanel()
        {
            _createCharacterPanel = UIHelper.CreatePanel(new Float2(800, 700));
            _createCharacterPanel.AnchorPreset = AnchorPresets.StretchAll;
            _createCharacterPanel.BackgroundColor = Color.Transparent;
            _createCharacterPanel.Visible = false;

            // 标题
            var createTitle = UIHelper.CreateTitleLabel("创建角色", 20);
            createTitle.Bounds = new Rectangle(0, 50, 800, 60);
            _createCharacterPanel.AddChild(createTitle);

            // 角色名输入
            var nameLabel = UIHelper.CreateLabel("角色名:");
            nameLabel.Bounds = new Rectangle(200, 150, 100, 30);
            _createCharacterPanel.AddChild(nameLabel);

            _characterNameInput = new ValidatedTextBox
            {
                WatermarkText = "请输入角色名称",
                Bounds = new Rectangle(320, 150, 200, 60)
            };
            _characterNameInput.SetValidator(text =>
            {
                if (string.IsNullOrWhiteSpace(text))
                    return (false, "角色名不能为空");
                if (text.Length < 2)
                    return (false, "角色名至少2个字符");
                if (text.Length > 12)
                    return (false, "角色名最多12个字符");
                return (true, "");
            });
            _createCharacterPanel.AddChild(_characterNameInput);

            // 职业选择
            var professionLabel = UIHelper.CreateLabel("职业:");
            professionLabel.Bounds = new Rectangle(200, 220, 100, 30);
            _createCharacterPanel.AddChild(professionLabel);

            _professionDropdown = new Dropdown
            {
                Bounds = new Rectangle(320, 220, 200, 30),
                BackgroundColor = UIHelper.InputColor,
                TextColor = Color.White
            };
            foreach (var profession in _professions)
            {
                _professionDropdown.AddItem(profession);
            }
            _professionDropdown.SelectedIndex = 0;
            _createCharacterPanel.AddChild(_professionDropdown);

            // 性别选择
            var genderLabel = UIHelper.CreateLabel("性别:");
            genderLabel.Bounds = new Rectangle(200, 270, 100, 30);
            _createCharacterPanel.AddChild(genderLabel);

            _genderDropdown = new Dropdown
            {
                Bounds = new Rectangle(320, 270, 200, 30),
                BackgroundColor = UIHelper.InputColor,
                TextColor = Color.White
            };
            foreach (var gender in _genders)
            {
                _genderDropdown.AddItem(gender);
            }
            _genderDropdown.SelectedIndex = 0;
            _createCharacterPanel.AddChild(_genderDropdown);

            // 外观面板
            CreateAppearancePanel();

            // 确认创建按钮
            _confirmCreateButton = UIHelper.CreatePrimaryButton("确认创建");
            _confirmCreateButton.Bounds = new Rectangle(250, 580, 120, 40);
            _confirmCreateButton.ButtonClicked += OnConfirmCreateClicked;
            _createCharacterPanel.AddChild(_confirmCreateButton);

            // 取消按钮
            _cancelCreateButton = UIHelper.CreateDangerButton("取消");
            _cancelCreateButton.Bounds = new Rectangle(430, 580, 120, 40);
            _cancelCreateButton.ButtonClicked += OnCancelCreateClicked;
            _createCharacterPanel.AddChild(_cancelCreateButton);

            _mainContainer.AddChild(_createCharacterPanel);

        }

        /// <summary>
        /// 查找UICanvas组件
        /// </summary>
        private UICanvas FindUICanvas()
        {
            // 方法1：从当前Actor查找
            var canvas = Actor.GetScript<UICanvas>();
            if (canvas != null) return canvas;

            // 方法2：从父Actor查找
            if (Actor.Parent != null)
            {
                canvas = Actor.Parent.GetScript<UICanvas>();
                if (canvas != null) return canvas;
            }

            // 方法3：从场景中查找名为UICanvas的Actor
            var uiCanvasActor = Level.FindActor("MainUICanvas");
            if (uiCanvasActor != null)
            {
                canvas = uiCanvasActor.GetChild<UICanvas>();
                if (canvas != null) return canvas;
            }

            // 方法4：查找所有UICanvas组件
            var allActors = Level.GetActors<Actor>();
            foreach (var actor in allActors)
            {
                canvas = actor.GetScript<UICanvas>();
                if (canvas != null) return canvas;
            }

            return null;
        }

        /// <summary>
        /// 创建外观面板
        /// </summary>
        private void CreateAppearancePanel()
        {
            _appearancePanel = new Panel
            {
                Bounds = new Rectangle(150, 300, 500, 180),
                BackgroundColor = new Color(0.25f, 0.25f, 0.3f, 0.8f)
                // 移除BorderColor属性
            };

            var appearanceTitle = new Label
            {
                Text = "外观设置",
                TextColor = Color.White,
                Bounds = new Rectangle(0, 10, 500, 30),
                HorizontalAlignment = TextAlignment.Center  // 修正为HorizontalAlignment
            };
            _appearancePanel.AddChild(appearanceTitle);

            // 发型选择
            var hairModelLabel = new Label
            {
                Text = "发型:",
                TextColor = Color.White,
                Bounds = new Rectangle(20, 50, 80, 30),
                HorizontalAlignment = TextAlignment.Near  // 修正为HorizontalAlignment
            };
            _appearancePanel.AddChild(hairModelLabel);

            _hairModelDropdown = new Dropdown  // 修正为Dropdown
            {
                Bounds = new Rectangle(110, 50, 120, 30),
                BackgroundColor = new Color(0.3f, 0.3f, 0.35f),
                // 移除BorderColor属性
                TextColor = Color.White
            };
            for (int i = 1; i <= 10; i++)
            {
                _hairModelDropdown.AddItem($"发型{i}");
            }
            _hairModelDropdown.SelectedIndex = 0;
            _appearancePanel.AddChild(_hairModelDropdown);

            // 发色选择
            var hairColorLabel = new Label
            {
                Text = "发色:",
                TextColor = Color.White,
                Bounds = new Rectangle(250, 50, 80, 30),
                HorizontalAlignment = TextAlignment.Near  // 修正为HorizontalAlignment
            };
            _appearancePanel.AddChild(hairColorLabel);

            _hairColorDropdown = new Dropdown  // 修正为Dropdown
            {
                Bounds = new Rectangle(340, 50, 120, 30),
                BackgroundColor = new Color(0.3f, 0.3f, 0.35f),
                // 移除BorderColor属性
                TextColor = Color.White
            };
            var hairColors = new[] { "黑色", "棕色", "金色", "红色", "蓝色" };
            foreach (var color in hairColors)
            {
                _hairColorDropdown.AddItem(color);
            }
            _hairColorDropdown.SelectedIndex = 0;
            _appearancePanel.AddChild(_hairColorDropdown);

            // 脸型选择
            var faceModelLabel = new Label
            {
                Text = "脸型:",
                TextColor = Color.White,
                Bounds = new Rectangle(20, 100, 80, 30),
                HorizontalAlignment = TextAlignment.Near  // 修正为HorizontalAlignment
            };
            _appearancePanel.AddChild(faceModelLabel);

            _faceModelDropdown = new Dropdown  // 修正为Dropdown
            {
                Bounds = new Rectangle(110, 100, 120, 30),
                BackgroundColor = new Color(0.3f, 0.3f, 0.35f),
                // 移除BorderColor属性
                TextColor = Color.White
            };
            for (int i = 1; i <= 5; i++)
            {
                _faceModelDropdown.AddItem($"脸型{i}");
            }
            _faceModelDropdown.SelectedIndex = 0;
            _appearancePanel.AddChild(_faceModelDropdown);

            // 服装选择
            var clothingLabel = new Label
            {
                Text = "服装:",
                TextColor = Color.White,
                Bounds = new Rectangle(250, 100, 80, 30),
                HorizontalAlignment = TextAlignment.Near  // 修正为HorizontalAlignment
            };
            _appearancePanel.AddChild(clothingLabel);

            _clothingDropdown = new Dropdown  // 修正为Dropdown
            {
                Bounds = new Rectangle(340, 100, 120, 30),
                BackgroundColor = new Color(0.3f, 0.3f, 0.35f),
                // 移除BorderColor属性
                TextColor = Color.White
            };
            var clothingTypes = new[] { "普通装", "战斗装", "礼服", "休闲装" };
            foreach (var clothing in clothingTypes)
            {
                _clothingDropdown.AddItem(clothing);
            }
            _clothingDropdown.SelectedIndex = 0;
            _appearancePanel.AddChild(_clothingDropdown);

            _createCharacterPanel.AddChild(_appearancePanel);
        }

        /// <summary>
        /// 显示角色列表
        /// </summary>
        private void ShowCharacterList()
        {
            _characterListPanel.Visible = true;
            _createCharacterPanel.Visible = false;

            // 刷新角色列表
            RefreshCharacterList();
        }

        /// <summary>
        /// 显示角色创建面板
        /// </summary>
        private void ShowCharacterCreation()
        {
            _characterListPanel.Visible = false;
            _createCharacterPanel.Visible = true;

            // 重置输入
            _characterNameInput.Text = "";
            _professionDropdown.SelectedIndex = 0;
            _genderDropdown.SelectedIndex = 0;
            _hairModelDropdown.SelectedIndex = 0;
            _hairColorDropdown.SelectedIndex = 0;
            _faceModelDropdown.SelectedIndex = 0;
            _clothingDropdown.SelectedIndex = 0;
        }

        /// <summary>
        /// 刷新角色列表
        /// </summary>
        private void RefreshCharacterList()
        {
            // 清空现有角色项
            _characterScrollView.Children.Clear();

            // 添加角色项
            for (int i = 0; i < _characters.Count; i++)
            {
                var character = _characters[i];
                var characterItem = CreateCharacterItem(character, i);
                characterItem.Location = new Float2(10, 10 + i * 110);
                _characterScrollView.AddChild(characterItem);
            }

            // 如果没有角色，显示提示信息
            if (_characters.Count == 0)
            {
                var noCharacterLabel = new Label
                {
                    Text = "暂无角色，请创建新角色",
                    TextColor = Color.Gray,
                    Bounds = new Rectangle(0, 150, 600, 30),
                    HorizontalAlignment = TextAlignment.Center  // 修正为HorizontalAlignment
                };
                _characterScrollView.AddChild(noCharacterLabel);
            }
        }

        /// <summary>
        /// 创建角色项
        /// </summary>
        private ContainerControl CreateCharacterItem(CharacterInfo character, int index)
        {
            var container = new ContainerControl
            {
                Size = new Float2(580, 100),
                BackgroundColor = new Color(0.3f, 0.3f, 0.35f, 0.8f)
                // 移除BorderColor属性
            };

            // 角色名
            var nameLabel = new Label
            {
                Text = character.CharacterName,
                TextColor = Color.White,
                Bounds = new Rectangle(20, 20, 200, 30),
                HorizontalAlignment = TextAlignment.Near  // 修正为HorizontalAlignment
            };
            container.AddChild(nameLabel);

            // 职业
            var professionLabel = new Label
            {
                Text = $"职业: {_professions[(int)character.Profession]}",
                TextColor = Color.LightGray,
                Bounds = new Rectangle(20, 50, 150, 25),
                HorizontalAlignment = TextAlignment.Near  // 修正为HorizontalAlignment
            };
            container.AddChild(professionLabel);

            // 等级
            var levelLabel = new Label
            {
                Text = $"等级: {character.Level}",
                TextColor = Color.LightGray,
                Bounds = new Rectangle(180, 50, 100, 25),
                HorizontalAlignment = TextAlignment.Near  // 修正为HorizontalAlignment
            };
            container.AddChild(levelLabel);

            // 进入游戏按钮
            var enterButton = new Button
            {
                Text = "进入游戏",
                Bounds = new Rectangle(400, 20, 100, 35),
                BackgroundColor = new Color(0.3f, 0.6f, 0.3f),
                // 移除BorderColor属性
                TextColor = Color.White
            };
            enterButton.Tag = character; // 将角色信息附加到按钮
            enterButton.ButtonClicked += OnEnterGameClicked;
            container.AddChild(enterButton);

            // 删除按钮
            var deleteButton = new Button
            {
                Text = "删除",
                Bounds = new Rectangle(400, 60, 100, 35),
                BackgroundColor = new Color(0.6f, 0.3f, 0.3f),
                // 移除BorderColor属性
                TextColor = Color.White
            };
            deleteButton.Tag = character; // 将角色信息附加到按钮
            deleteButton.ButtonClicked += OnDeleteCharacterClicked;
            container.AddChild(deleteButton);

            return container;
        }

        /// <summary>
        /// 创建新角色按钮点击事件
        /// </summary>
        private void OnCreateNewCharacterClicked(Button sender)
        {
            _stateManager.TransitionToScene(SceneType.CharacterCreation);

            // 显示创建引导
            var guidance = UserGuidanceManager.CreateCharacterCreationGuidance();
            _guidanceManager.StartGuidance(guidance);
        }

        /// <summary>
        /// 确认创建按钮点击事件
        /// </summary>
        private async void OnConfirmCreateClicked(Button sender)
        {
            if (_isProcessing) return;

            // 验证输入
            if (!_characterNameInput.IsValid)
            {
                UIHelper.ShowError("请检查角色名称");
                return;
            }

            var characterName = _characterNameInput.Text?.Trim();

            try
            {
                _isProcessing = true;

                // 播放按钮点击动画
                _animationManager.Bounce(_confirmCreateButton, 0.3f);

                // 创建外观信息
                var appearance = new AppearanceInfo
                {
                    HairModel = (byte)_hairModelDropdown.SelectedIndex,
                    HairColor = (byte)_hairColorDropdown.SelectedIndex,
                    FaceModel = (byte)_faceModelDropdown.SelectedIndex,
                    Clothing = (byte)_clothingDropdown.SelectedIndex
                };

                // 使用CharacterService创建角色
                var result = await _characterService.CreateCharacterAsync(
                    characterName,
                    (Horizon.Game.Message.Enums.Profession)_professionDropdown.SelectedIndex,
                    _genderDropdown.SelectedIndex,
                    appearance);

                if (result.IsSuccess)
                {
                    UIHelper.ShowInfo(result.Message);
                    // 可以考虑在这里添加等待服务器响应的逻辑
                }
                else
                {
                    UIHelper.ShowError(result.Message);
                }
            }
            catch (Exception ex)
            {
                _errorManager.HandleError($"创建角色过程中发生错误: {ex.Message}", ErrorType.Unknown, ErrorSeverity.Error, "CharacterManagementUI");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// 取消创建按钮点击事件
        /// </summary>
        private void OnCancelCreateClicked(Button sender)
        {
            _stateManager.TransitionToScene(SceneType.CharacterSelection);
        }

        /// <summary>
        /// 返回登录按钮点击事件
        /// </summary>
        private void OnBackToLoginClicked(Button sender)
        {
            var confirmDialog = UIHelper.CreateConfirmDialog(
                "返回登录",
                "您确定要返回登录界面吗？",
                () =>
                {
                    _mainContainer.Visible = false;
                    _stateManager.TransitionToScene(SceneType.Login);
                }
            );
            _mainContainer.AddChild(confirmDialog);
        }

        /// <summary>
        /// 进入游戏按钮点击事件
        /// </summary>
        private async void OnEnterGameClicked(Button sender)
        {
            if (_isProcessing || sender.Tag == null) return;

            var character = sender.Tag as CharacterInfo;
            if (character == null) return;

            try
            {
                _isProcessing = true;

                // 选择角色
                _characterService.SelectCharacter(character);

                // 发送进入游戏请求
                var request = new EnterGameRequest
                {
                    CharacterId = character.CharacterId,
                    ServiceType = ServiceType.Game,
                    Type = MessageType.EnterGame
                };

                // 将请求包装成消息包
                var messagePacket = new HorizonMessagePacket
                {
                    Header = new MessageHeader
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        MessageType = MessageType.EnterGame,
                        ServiceType = ServiceType.Game
                    },
                    Body = request
                };

                var success = await _networkManager.SendMessageAsync(messagePacket);
                
                if (success)
                {
                    FlaxEngine.Debug.Log($"进入游戏请求已发送: {character.CharacterName}");
                    UIHelper.ShowInfo("正在进入游戏...");
                    // 这里应该等待服务器响应后再切换场景
                }
                else
                {
                    UIHelper.ShowError("发送进入游戏请求失败");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"进入游戏过程中发生错误: {ex.Message}");
                UIHelper.ShowError($"进入游戏失败: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// 删除角色按钮点击事件
        /// </summary>
        private async void OnDeleteCharacterClicked(Button sender)
        {
            if (_isProcessing || sender.Tag == null) return;

            var character = sender.Tag as CharacterInfo;
            if (character == null) return;

            try
            {
                _isProcessing = true;

                // 确认删除对话框
                var confirmDialog = UIHelper.CreateConfirmDialog(
                    "删除角色",
                    $"您确定要删除角色 \"{character.CharacterName}\" 吗？此操作不可撤销！",
                    async () =>
                    {
                        // 使用CharacterService删除角色
                        var result = await _characterService.DeleteCharacterAsync(character.CharacterId);

                        if (result)
                        {
                            UIHelper.ShowInfo("角色删除成功");
                            // 从本地列表移除
                            _characters.RemoveAll(c => c.CharacterId == character.CharacterId);
                            RefreshCharacterList();
                        }
                        else
                        {
                            UIHelper.ShowError("角色删除失败");
                        }
                    }
                );
                
                _mainContainer.AddChild(confirmDialog);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"删除角色过程中发生错误: {ex.Message}");
                UIHelper.ShowError($"删除角色失败: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// 处理创建角色响应
        /// </summary>
        private void OnCreateCharacterResponse(CreateCharacterResponse response)
        {
            _isProcessing = false;

            if (response.IsSuccess)
            {
                FlaxEngine.Debug.Log($"角色创建成功: {response.Character?.CharacterName}");

                // 添加新角色到列表
                if (response.Character != null)
                {
                    _characters.Add(response.Character);
                    RefreshCharacterList();
                }

                // 返回角色列表
                ShowCharacterList();
            }
            else
            {
                FlaxEngine.Debug.LogWarning($"角色创建失败: {response.Message}");
            }
        }

        /// <summary>
        /// 处理删除角色响应
        /// </summary>
        private void OnDeleteCharacterResponse(DeleteCharacterResponse response)
        {
            _isProcessing = false;

            if (response.Success)
            {
                FlaxEngine.Debug.Log("角色删除成功");

                // 从列表中移除角色
                var characterToRemove = _characters.FirstOrDefault(c => c.CharacterId == response.CharacterId);
                if (characterToRemove != null)
                {
                    _characters.Remove(characterToRemove);
                    RefreshCharacterList();
                }
            }
            else
            {
                FlaxEngine.Debug.LogWarning($"角色删除失败: {response.Message}");
            }
        }

        /// <summary>
        /// 处理进入游戏响应
        /// </summary>
        private void OnEnterGameResponse(EnterGameResponse response)
        {
            _isProcessing = false;

            if (response.Success)
            {
                FlaxEngine.Debug.Log("进入游戏成功");

                // 隐藏角色管理界面
                _mainContainer.Visible = false;

                // 查找并显示游戏主界面
                var gameMainUI = Actor.Parent?.GetScript<GameMainUI>();  // 修正为Actor.Parent?.FindControl
                if (gameMainUI != null)
                {
                    gameMainUI.Actor.IsActive = true;
                }
                else
                {
                    FlaxEngine.Debug.LogError("未找到GameMainUI");
                }
            }
            else
            {
                FlaxEngine.Debug.LogWarning($"进入游戏失败: {response.Message}");
            }
        }

        public override void OnDisable()
        {

            // 清理资源
            _mainContainer?.Dispose();
        }
    }
}