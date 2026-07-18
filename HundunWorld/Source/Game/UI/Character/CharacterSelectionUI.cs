using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI.StyleSystem;
using UIStateManager = HundunWorld.Game.UI.UIStateManager;
using CharacterService = HundunWorld.Game.Services.CharacterService;
using Game.UI.Character;

namespace HundunWorld.Game.UI.Character
{
    public class CharacterSelectionUI : ContainerControl
    {
        #region Events
        public event Action OnCharacterSelected;
        public event Action OnBackToLogin;
        public event Action OnCreateNewCharacter;
        #endregion

        #region Constants
        private static readonly string[] ProfessionNames = { "剑客", "刀客", "枪客", "弓手", "法师", "道士", "刺客", "医师" };

        // 古典金色 RGB(212,175,55)
        private static readonly Color GoldColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1f);
        // 25% 透明金色（用于选中态背景）
        private static readonly Color GoldHighlightColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.25f);

        // 角色卡片尺寸与间距
        private const float CharacterItemHeight = 100f;
        private const float CharacterItemSpacing = 12f;
        #endregion

        #region UI Components
        private Panel _characterListPanel;
        private Label _titleLabel;
        private ScrollableControl _characterScrollView;
        private Label _emptyHintLabel;
        private LoadingIndicator _loadingIndicator;
        private CharacterPreviewPanel _previewPanel;
        private BottomActionBar _bottomBar;
        #endregion

        #region Data
        private List<CharacterInfo> _characters = new List<CharacterInfo>();
        private bool _isProcessing;
        private string _selectedCharacterId;
        #endregion

        #region Initialization
        private bool _layoutInitialized = false;

        public bool IsInitialized => _layoutInitialized;

        public CharacterSelectionUI(CharacterPreviewPanel sharedPreviewPanel = null)
        {
            AnchorPreset = AnchorPresets.StretchAll;
            Offsets = Margin.Zero;
            BackgroundColor = Color.Transparent;

            if (sharedPreviewPanel != null)
            {
                _previewPanel = sharedPreviewPanel;
            }
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_layoutInitialized && Width > 0 && Height > 0)
            {
                _layoutInitialized = true;
                CreateUI();
                SubscribeEvents();
            }
        }

        public void ForceInitialize()
        {
            if (!_layoutInitialized)
            {
                _layoutInitialized = true;
                CreateUI();
                SubscribeEvents();
                PerformLayout();
            }
        }

        public void CreateUIImmediate()
        {
            _layoutInitialized = true;
            CreateUI();
            SubscribeEvents();
            PerformLayout();
        }

        private void CreateUI()
        {
            CreateCharacterListPanel();
            CreateBottomBar();
            CreateLoadingIndicator();
        }

        private void CreateCharacterListPanel()
        {
            _characterListPanel = new Panel
            {
                Parent = this,
                AnchorPreset = AnchorPresets.VerticalStretchLeft,
                Offsets = new Margin(30, 0, 30, 80),
                Width = 320,
                BackgroundColor = new Color(0.08f, 0.08f, 0.10f, 0.9f)
            };

            // 标题区域
            var titleContainer = new ContainerControl
            {
                Parent = _characterListPanel,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(0, 0, 0, 55),
                Height = 55,
                BackgroundColor = Color.Transparent
            };

            _titleLabel = new Label
            {
                Parent = titleContainer,
                Text = "选择角色",
                Font = UIHelper.SetFont(size: 24),
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                TextColor = GoldColor
            };

            // 滚动区域
            _characterScrollView = new ScrollableControl
            {
                Parent = _characterListPanel,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(10, 60, 10, 0),
                BackgroundColor = Color.Transparent
            };

            _emptyHintLabel = new Label
            {
                Parent = _characterScrollView,
                Text = "暂无角色，请创建新角色",
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                TextColor = new Color(0.6f, 0.6f, 0.65f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center
            };
        }

        private void CreateBottomBar()
        {
            _bottomBar = new BottomActionBar
            {
                Parent = this,
                AnchorPreset = AnchorPresets.HorizontalStretchBottom
            };
            _bottomBar.SetButtons(
                new string[] { "返回登录", "创建新角色", "进入游戏" },
                new BottomActionBar.ButtonStyle[] {
                    BottomActionBar.ButtonStyle.Ghost,
                    BottomActionBar.ButtonStyle.Default,
                    BottomActionBar.ButtonStyle.Accent
                }
            );
            _bottomBar.OnButtonClicked += OnBottomBarButtonClicked;
        }

        private void OnBottomBarButtonClicked(string buttonName)
        {
            switch (buttonName)
            {
                case "返回登录":
                    OnBackToLoginClicked();
                    break;
                case "创建新角色":
                    OnCreateNewCharacterClicked();
                    break;
                case "进入游戏":
                    // 通过选中的角色ID找到角色对象并进入游戏
                    if (!string.IsNullOrEmpty(_selectedCharacterId))
                    {
                        var selectedCharacter = _characters.Find(c => c.CharacterId.ToString() == _selectedCharacterId);
                        if (selectedCharacter != null)
                        {
                            OnEnterGameClicked(selectedCharacter);
                        }
                        else
                        {
                            Debug.LogWarning("[CharacterSelectionUI] 未找到选中的角色");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[CharacterSelectionUI] 请先选择一个角色");
                    }
                    break;
            }
        }

        private void CreateLoadingIndicator()
        {
            _loadingIndicator = UIHelper.CreateLoadingIndicator();
            _loadingIndicator.Parent = this;
            _loadingIndicator.AnchorPreset = AnchorPresets.MiddleCenter;
            _loadingIndicator.Offsets = new Margin(0, 40, 0, 40);
            _loadingIndicator.Visible = false;
        }

        private void SubscribeEvents()
        {
            var stateManager = UIStateManager.Instance;
            if (stateManager != null)
            {
                stateManager.CharacterListUpdated += OnCharacterListUpdated;
            }
        }

        public void Cleanup()
        {
            var stateManager = UIStateManager.Instance;
            if (stateManager != null)
            {
                stateManager.CharacterListUpdated -= OnCharacterListUpdated;
            }

            if (_previewPanel != null)
            {
                _previewPanel.OnDestroy();
                _previewPanel = null;
            }
        }
        #endregion

        #region Event Handlers
        private void OnCharacterListUpdated(List<CharacterInfo> characters)
        {
            _characters = characters ?? new List<CharacterInfo>();
            RefreshCharacterList();
        }

        private void OnCreateNewCharacterClicked()
        {
            if (_isProcessing) return;
            OnCreateNewCharacter?.Invoke();
        }

        private void OnBackToLoginClicked()
        {
            if (_isProcessing) return;

            var confirmDialog = UIHelper.CreateConfirmDialog(
                "返回登录",
                "您确定要返回登录界面吗？",
                () =>
                {
                    OnBackToLogin?.Invoke();
                }
            );
            AddChild(confirmDialog);
        }

        private async void OnEnterGameClicked(CharacterInfo character)
        {
            if (_isProcessing || character == null) return;

            try
            {
                _isProcessing = true;
                _loadingIndicator.Show();

                // 使用 CharacterManager 统一管理角色选择 + 进入游戏流程
                var charMgr = CharacterManager.Instance;
                if (charMgr == null)
                {
                    Debug.LogError("[CharacterSelectionUI] CharacterManager 不可用，降级使用直接发送");
                    // 降级方案：直接发送网络消息
                    var request = new EnterGameRequest
                    {
                        CharacterId = character.CharacterId,
                        ClientVersion = "1.0.0"
                    };
                    var messagePacket = new HorizonMessagePacket
                    {
                        Header = new MessageHeader
                        {
                            MessageId = Guid.NewGuid().ToString(),
                            MessageType = MessageType.EnterGame,
                            ServiceType = ServiceType.Game,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        },
                        ServiceType = ServiceType.Game,
                        Body = request
                    };
                    var networkManager = HundunWorldGame.Instance?.NetworkManager;
                    if (networkManager == null || !networkManager.CanSendMessage())
                    {
                        Debug.LogError("[CharacterSelectionUI] 网络未连接，无法进入游戏");
                        return;
                    }
                    
                    messagePacket.Header.GameId = networkManager.GameId;
                    messagePacket.Header.ZoneId = networkManager.ZoneId;
                    messagePacket.Header.ServerId = networkManager.ServerId;
                    messagePacket.Header.UserId = networkManager.UserId;
                    messagePacket.Header.AuthToken = networkManager.AuthToken;
                    messagePacket.Header.MachineId = MachineIdentifier.GetMachineGuid();
                    var sent = await networkManager.SendMessageAsync(messagePacket);
                    if (sent)
                    {
                        CharacterService.Instance.SelectCharacter(character);
                        OnCharacterSelected?.Invoke();

                        // 降级路径：CharacterManager 不可用，直接通过 GameSceneManager 切换到游戏世界场景
                        // （与主路径 CharacterManager.EnterGameAsync 中的场景切换写法一致）
                        var sceneManager = GameSceneManager.Instance ?? GameSceneManager.GetOrCreate();
                        if (sceneManager != null)
                        {
                            Debug.Log("[CharacterSelectionUI] 降级路径：启动场景切换到 GameWorld");
                            bool transitionStarted = sceneManager.TransitionTo(SceneType.GameWorld);
                            if (!transitionStarted)
                            {
                                Debug.LogError("[CharacterSelectionUI] 降级路径：GameSceneManager.TransitionTo(GameWorld) 返回 false，场景切换未启动");
                            }
                        }
                        else
                        {
                            Debug.LogError("[CharacterSelectionUI] 降级路径：GameSceneManager 不可用，无法切换到游戏世界场景");
                        }
                    }
                    else
                    {
                        Debug.LogError("[CharacterSelectionUI] 发送进入游戏请求失败");
                    }
                    return;
                }

                // 1. 选择角色（更新状态管理器）
                if (!charMgr.SelectCharacter(character))
                {
                    Debug.LogError("[CharacterSelectionUI] 选择角色失败");
                    return;
                }

                // 2. 发送进入游戏请求
                bool success = await charMgr.EnterGameAsync();
                if (success)
                {
                    OnCharacterSelected?.Invoke();
                }
                else
                {
                    Debug.LogError("[CharacterSelectionUI] 进入游戏失败");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterSelectionUI] 进入游戏过程中发生错误: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
                _loadingIndicator.Hide();
            }
        }

        private void OnDeleteCharacterClicked(CharacterInfo character)
        {
            if (character == null) return;

            var confirmDialog = UIHelper.CreateConfirmDialog(
                "删除角色",
                $"您确定要删除角色 \"{character.CharacterName}\" 吗？此操作不可撤销！",
                async () =>
                {
                    if (_isProcessing) return;
                    _isProcessing = true;

                    try
                    {
                        var result = await CharacterService.Instance.DeleteCharacterAsync(character.CharacterId);

                        if (result)
                        {
                            _characters.RemoveAll(c => c.CharacterId == character.CharacterId);
                            if (_selectedCharacterId == character.CharacterId.ToString())
                            {
                                _selectedCharacterId = null;
                            }
                            RefreshCharacterList();
                        }
                        else
                        {
                            Debug.LogError("[CharacterSelectionUI] 角色删除失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CharacterSelectionUI] 删除角色过程中发生错误: {ex.Message}");
                    }
                    finally
                    {
                        _isProcessing = false;
                    }
                }
            );
            AddChild(confirmDialog);
        }
        #endregion

        #region UI Refresh
        private void RefreshCharacterList()
        {
            _characterScrollView.RemoveChildren();

            if (_characters.Count == 0)
            {
                _emptyHintLabel.Visible = true;
                _emptyHintLabel.Parent = _characterScrollView;
                return;
            }

            _emptyHintLabel.Visible = false;

            for (int i = 0; i < _characters.Count; i++)
            {
                var character = _characters[i];
                var characterItem = CreateCharacterItem(character, i);
                characterItem.Parent = _characterScrollView;
                float cardStep = CharacterItemHeight + CharacterItemSpacing;
                characterItem.Offsets = new Margin(0, 0, i * cardStep, i * cardStep + CharacterItemHeight);
            }
        }

        private Panel CreateCharacterItem(CharacterInfo character, int index)
        {
            bool isSelected = !string.IsNullOrEmpty(_selectedCharacterId)
                && _selectedCharacterId == character.CharacterId.ToString();

            var itemPanel = new ClickablePanel
            {
                Height = CharacterItemHeight,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(5, 5, 0, CharacterItemHeight),
                BackgroundColor = isSelected
                    ? GoldHighlightColor
                    : new Color(0.08f, 0.08f, 0.1f, 0.7f)
            };

            var professionIndex = (int)character.Profession;
            var professionName = professionIndex >= 0 && professionIndex < ProfessionNames.Length
                ? ProfessionNames[professionIndex]
                : "未知";

            var nameLabel = new Label
            {
                Parent = itemPanel,
                Text = character.CharacterName,
                AnchorPreset = AnchorPresets.HorizontalStretchTop,
                Offsets = new Margin(15, 100, 12, 38),
                TextColor = new Color(1.0f, 0.95f, 0.8f),
                HorizontalAlignment = TextAlignment.Near
            };

            var professionLabel = new Label
            {
                Parent = itemPanel,
                Text = $"职业: {professionName}",
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(15, 120, 40, 62),
                TextColor = new Color(0.7f, 0.7f, 0.75f)
            };

            var levelLabel = new Label
            {
                Parent = itemPanel,
                Text = $"Lv.{character.Level}",
                AnchorPreset = AnchorPresets.TopLeft,
                Offsets = new Margin(120, 220, 40, 62),
                TextColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 0.8f)
            };

            var enterButton = UIHelper.CreatePrimaryButton("进入");
            enterButton.Parent = itemPanel;
            enterButton.AnchorPreset = AnchorPresets.TopRight;
            enterButton.Offsets = new Margin(0, 80, 12, 44);
            var capturedCharacter = character;
            enterButton.Clicked += () => OnEnterGameClicked(capturedCharacter);

            var deleteButton = UIHelper.CreateDangerButton("删除");
            deleteButton.Parent = itemPanel;
            deleteButton.AnchorPreset = AnchorPresets.TopRight;
            deleteButton.Offsets = new Margin(0, 10, 50, 82);
            deleteButton.Clicked += () => OnDeleteCharacterClicked(capturedCharacter);

            // 卡片选中事件：使用 OnMouseDown 替代 Panel.Clicked（Panel 不支持 Clicked 事件）
            var selectCharacter = character;
            itemPanel.OnMouseDownCallback = () => SelectCharacter(selectCharacter);

            return itemPanel;
        }

        private void SelectCharacter(CharacterInfo character)
        {
            if (character == null) return;
            _selectedCharacterId = character.CharacterId.ToString();
            RefreshCharacterList();
        }
        #endregion

        #region Public Methods
        public void ShowCharacterPreview(string prefabPath)
        {
            if (_previewPanel != null && !string.IsNullOrEmpty(prefabPath))
            {
                _previewPanel.LoadCharacter(prefabPath);
            }
        }

        public void SetTargetScene(FlaxEngine.Scene scene)
        {
            if (_previewPanel != null)
            {
                _previewPanel.TargetScene = scene;
            }
        }

        public CharacterPreviewPanel GetPreviewPanel()
        {
            return _previewPanel;
        }

        public void Show()
        {
            Visible = true;

            var characters = UIStateManager.Instance?.CharacterList;
            if (characters != null)
            {
                _characters = characters;
            }
            RefreshCharacterList();
        }

        public void Hide()
        {
            Visible = false;
        }

        public void Clear()
        {
            _characters.Clear();
            _selectedCharacterId = null;
            RefreshCharacterList();
            _isProcessing = false;
            _loadingIndicator.Hide();
        }
        #endregion

        #region ClickablePanel
        /// <summary>
        /// 支持点击回调的 Panel（Panel 本身不支持 Clicked 事件）
        /// </summary>
        private class ClickablePanel : Panel
        {
            public Action OnMouseDownCallback;

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left)
                {
                    OnMouseDownCallback?.Invoke();
                    return true;
                }
                return base.OnMouseDown(location, button);
            }
        }
        #endregion
    }
}