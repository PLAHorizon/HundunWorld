using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Network;
using Game.UI.Controllers;
using HundunWorld.Game.UI.ErrorHandling;
using Horizon.Game.Message.Enums;
using HundunWorld.Game;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Events;
using HundunWorld.Game.UI.Controllers;
using HundunWorld.Game.Network;
using HundunWorld.Game.Services;
using HundunWorld.Game.Equipment;

namespace Game.UI.Character
{
    /// <summary>
    /// 角色管理器 - 重构版本
    /// 专注于角色相关的业务逻辑，与新架构集成
    /// 负责角色创建、选择、删除等操作
    /// </summary>
    public class CharacterManager
    {
        private static CharacterManager _instance;
        public static CharacterManager Instance => _instance ??= new CharacterManager();

        /// <summary>
        /// 重置单例实例，取消所有事件订阅以防止跨Play/Stop周期的事件处理器积累
        /// </summary>
        public static void ResetInstance()
        {
            if (_instance != null)
            {
                UIEventBus.Instance?.UnsubscribeAll("CharacterManager");
                _instance.CharacterListUpdated = null;
                _instance.CharacterSelected = null;
                _instance.CharacterCreated = null;
                _instance.CharacterDeleted = null;
                _instance = null;
            }
        }

        // 核心管理器
        private HundunWorld.Game.UI.UIStateManager _stateManager;
        private UIEventBus _eventBus;
        private ErrorHandler _errorHandler;
        private UISwitchController _switchController;

        // 角色管理状态
        private bool _isLoadingCharacterList = false;  // 加载角色列表时设置
        private bool _isCreatingCharacter = false;      // 创建角色时设置
        private bool _isDeletingCharacter = false;      // 删除角色时设置
        private bool _isEnteringGame = false;           // 进入游戏时设置
        private List<CharacterInfo> _characterList = new List<CharacterInfo>();
        private CharacterInfo _selectedCharacter = null;

        // 事件
        public event Action<List<CharacterInfo>> CharacterListUpdated;
        public event Action<CharacterInfo> CharacterSelected;
        public event Action<CharacterInfo> CharacterCreated;
        public event Action<ulong> CharacterDeleted;

        private CharacterManager()
        {
            InitializeManager();
        }

        /// <summary>
        /// 初始化管理器
        /// </summary>
        private void InitializeManager()
        {
            _stateManager = HundunWorld.Game.UI.UIStateManager.Instance;
            _eventBus = UIEventBus.Instance;
            _errorHandler = ErrorHandler.Instance;
            _switchController = UISwitchController.Instance;

            // 订阅事件
            _eventBus.Subscribe<UserSessionChangedEvent>(OnUserSessionChanged, subscriberName: "CharacterManager");
            _eventBus.Subscribe<SceneTransitionCompletedEvent>(OnSceneTransitionCompleted, subscriberName: "CharacterManager");

            // 订阅状态管理器的角色列表更新事件，确保数据同步
            if (_stateManager != null)
            {
                _stateManager.CharacterListUpdated += (list) =>
                {
                    _characterList = new List<CharacterInfo>(list);
                    CharacterListUpdated?.Invoke(_characterList);
                    _eventBus.Publish(new CharacterListUpdatedEvent(_characterList));
                    FlaxEngine.Debug.Log($"[CharacterManager] 收到状态管理器角色列表更新: {_characterList.Count} 个角色");
                };
            }

            // 网络管理器会自动注册消息处理器，这里不需要手动处理
        }



        #region 角色列表管理

        /// <summary>
        /// 加载角色列表
        /// </summary>
        /// <returns>是否成功</returns>
        public async Task<bool> LoadCharacterListAsync()
        {
            // 如果正在加载角色列表，直接返回 true，避免触发 UI 错误提示
            if (_isLoadingCharacterList)
            {
                FlaxEngine.Debug.Log("[CharacterManager] 角色列表加载正在处理中，跳过重复调用");
                return true;
            }

            var currentState = _stateManager.GetCurrentState();
            if (!currentState.UserSession.IsAuthenticated)
            {
                FlaxEngine.Debug.LogWarning("[CharacterManager] 加载角色列表失败：用户未登录");
                _errorHandler.HandleAuthenticationError("用户未登录，无法加载角色列表");
                return false;
            }

            _isLoadingCharacterList = true;
            FlaxEngine.Debug.Log("[CharacterManager] 开始加载角色列表...");

            try
            {
                _stateManager.SetLoadingState(true);

                // 从状态管理器获取角色列表
                var characters = currentState.Characters;
                if (characters != null && characters.Count > 0)
                {
                    _characterList = new List<CharacterInfo>(characters);
                    CharacterListUpdated?.Invoke(_characterList);
                    
                    // 发布事件
                    _eventBus.Publish(new CharacterListUpdatedEvent(_characterList));
                    
                    FlaxEngine.Debug.Log($"角色列表加载成功，共 {_characterList.Count} 个角色");
                    return true;
                }
                else
                {
                    // 如果没有角色，初始化空列表
                    _characterList.Clear();
                    CharacterListUpdated?.Invoke(_characterList);
                    _eventBus.Publish(new CharacterListUpdatedEvent(_characterList));
                    
                    FlaxEngine.Debug.Log("角色列表为空");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.Data, $"加载角色列表失败: {ex.Message}", ex, "load_character_list");
                return false;
            }
            finally
            {
                _isLoadingCharacterList = false;
                _stateManager.SetLoadingState(false);
            }
        }

        /// <summary>
        /// 选择角色
        /// </summary>
        /// <param name="character">要选择的角色</param>
        /// <returns>是否成功</returns>
        public bool SelectCharacter(CharacterInfo character)
        {
            if (character == null)
            {
                _errorHandler.HandleValidationError("角色信息不能为空", "character_selection");
                return false;
            }

            try
            {
                _selectedCharacter = character;
                
                // 更新状态管理器
                var currentState = _stateManager.GetCurrentState();
                currentState.SelectedCharacter = character;
                currentState.IncrementVersion();

                // 发布事件
                CharacterSelected?.Invoke(character);
                _eventBus.Publish(new SelectedCharacterChangedEvent(null, character));

                FlaxEngine.Debug.Log($"选择角色: {character.CharacterName}");
                return true;
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.System, $"选择角色失败: {ex.Message}", ex, "select_character");
                return false;
            }
        }

        /// <summary>
        /// 进入游戏
        /// </summary>
        /// <returns>是否成功</returns>
        public async Task<bool> EnterGameAsync()
        {
            if (_selectedCharacter == null)
            {
                _errorHandler.HandleValidationError("请先选择一个角色", "character_selection");
                return false;
            }

            if (_isEnteringGame) return false;

            _isEnteringGame = true;

            try
            {
                _stateManager.SetLoadingState(true);

                // 0. 检查网关连接状态：网关不在线时尝试按需拉起连接
                // 断线超时后客户端退回角色选择界面，不再自动重连。
                // 用户主动选择进入游戏时，先探查网关是否可达：
                //   - 网关不可达 → 停留在角色选择界面并提示用户
                //   - 网关可达 → 建立连接后继续进入游戏世界
                var networkManager = HundunWorldGame.Instance.NetworkManager;
                if (networkManager == null)
                {
                    FlaxEngine.Debug.LogError("[CharacterManager] NetworkManager 为空，无法进入游戏");
                    _errorHandler.HandleError(UIErrorType.Network, "网络模块未初始化，无法进入游戏。", null, "enter_game_network_null");
                    return false;
                }

                if (!networkManager.CanSendMessage())
                {
                    FlaxEngine.Debug.Log("[CharacterManager] 网关不在线，尝试按需拉起连接...");
                    // [连接精简治理 spec 5.1.1] 经单连接协调器编排进游戏建连：
                    // 返回 false 表示连接已在线被复用或另有路径在建连，直接复用/等待。
                    var coordinator = HundunWorldGame.Instance.ConnectionCoordinator;
                    var acquired = coordinator != null
                        ? await coordinator.RequestConnectAsync(ClientConnectionRequestKind.EnterGame)
                        : await networkManager.ConnectOnDemandAsync();
                    if (coordinator == null && !acquired)
                    {
                        FlaxEngine.Debug.LogWarning("[CharacterManager] 网关不可达，停留在角色选择界面");
                        _errorHandler.HandleError(UIErrorType.Network, "服务器当前不在线，无法进入游戏。请稍后重试。", null, "enter_game_gateway_offline");
                        return false;
                    }
                    if (coordinator != null && acquired)
                    {
                        // 实际执行建连成功：调用方发送 EnterGameRequest 首包（下方继续执行）。
                        coordinator.MarkFirstPacketSent();
                    }
                    FlaxEngine.Debug.Log("[CharacterManager] 按需连接成功，继续进入游戏");
                }

                // 1. 发送进入游戏请求（通知服务器）
                var enterGameRequest = new EnterGameRequest
                {
                    CharacterId = _selectedCharacter.CharacterId,
                    ClientVersion = "1.0.0"
                };

                var messagePacket = new HorizonMessagePacket
                {
                    Header = new MessageHeader
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        MessageType = MessageType.EnterGame,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        GameId = networkManager?.GameId ?? 1,
                        ZoneId = networkManager?.ZoneId ?? 1,
                        ServerId = networkManager?.ServerId ?? 1,
                        UserId = networkManager?.UserId ?? 0,
                        AuthToken = networkManager?.AuthToken ?? "",
                        MachineId = MachineIdentifier.GetMachineGuid()
                    },
                    ServiceType = ServiceType.Game,
                    Body = enterGameRequest
                };

                // 发送消息（fire-and-forget，不阻塞场景切换）
                bool sendSuccess = false;
                try
                {
                    sendSuccess = await HundunWorldGame.Instance.NetworkManager.SendMessageAsync(messagePacket);
                    FlaxEngine.Debug.Log($"[CharacterManager] 进入游戏消息发送: {(sendSuccess ? "成功" : "失败，继续场景切换")}");
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogWarning($"[CharacterManager] 发送进入游戏消息异常: {ex.Message}，继续场景切换");
                }

                // 2. 更新状态管理器：选中角色 + 设置场景为 GameWorld
                var currentState = _stateManager.GetCurrentState();
                currentState.SelectedCharacter = _selectedCharacter;
                currentState.IncrementVersion();

                // 3. 直接通过 GameSceneManager 加载 World.scene（带淡入淡出过渡）
                var sceneManager = GameSceneManager.Instance ?? GameSceneManager.GetOrCreate();
                if (sceneManager != null)
                {
                    bool transitionStarted = sceneManager.TransitionTo(SceneType.GameWorld);
                    FlaxEngine.Debug.Log($"[CharacterManager] GameSceneManager.TransitionTo(GameWorld) 结果: {transitionStarted}");
                    
                    if (!transitionStarted)
                    {
                        _errorHandler.HandleError(UIErrorType.Transition, "启动场景切换失败", null, "enter_game_transition");
                        return false;
                    }
                }
                else
                {
                    FlaxEngine.Debug.LogError("[CharacterManager] GameSceneManager 不可用");
                    _errorHandler.HandleError(UIErrorType.System, "场景管理器不可用", null, "enter_game_no_manager");
                    return false;
                }

                FlaxEngine.Debug.Log($"成功进入游戏: {_selectedCharacter.CharacterName}");

                // 订阅场景切换完成事件，切换后检查角色是否已生成
                sceneManager.TransitionCompleted += OnGameWorldTransitionCompleted;

                return true;
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.System, $"进入游戏失败: {ex.Message}", ex, "enter_game");
                return false;
            }
            finally
            {
                _isEnteringGame = false;
                _stateManager.SetLoadingState(false);
            }
        }

        /// <summary>
        /// 场景切换到 GameWorld 完成后的回调：检查本地玩家是否已生成
        /// </summary>
        private void OnGameWorldTransitionCompleted(SceneType previousScene, SceneType targetScene)
        {
            if (targetScene != SceneType.GameWorld) return;

            // 取消订阅（一次性）
            var sceneManager = GameSceneManager.Instance;
            if (sceneManager != null)
            {
                sceneManager.TransitionCompleted -= OnGameWorldTransitionCompleted;
            }

            FlaxEngine.Debug.Log("[CharacterManager] 场景已切换到 GameWorld，检查本地玩家是否已生成");

            // 检查本地玩家 Actor 是否已生成
            var game = HundunWorldGame.Instance;
            if (game == null)
            {
                FlaxEngine.Debug.LogWarning("[CharacterManager] HundunWorldGame.Instance 为空，无法检查本地玩家");
                return;
            }

            if (game.LocalPlayerActor == null)
            {
                FlaxEngine.Debug.Log("[CharacterManager] 本地玩家尚未生成，等待网络响应或 WorldSceneInitializer 兜底");
            }
            else
            {
                FlaxEngine.Debug.Log($"[CharacterManager] 本地玩家已生成: {game.LocalPlayerActor.Name}");
            }
        }

        #endregion

        #region 角色创建

        /// <summary>
        /// 创建角色
        /// </summary>
        /// <param name="characterName">角色名称</param>
        /// <param name="profession">职业</param>
        /// <param name="gender">性别</param>
        /// <param name="appearance">外观信息</param>
        /// <returns>是否成功</returns>
        public async Task<bool> CreateCharacterAsync(string characterName, int profession, int gender, AppearanceInfo appearance)
        {
            if (string.IsNullOrEmpty(characterName))
            {
                _errorHandler.HandleValidationError("角色名称不能为空", "character_name");
                return false;
            }

            if (_isCreatingCharacter) return false;

            var currentState = _stateManager.GetCurrentState();
            if (!currentState.UserSession.IsAuthenticated)
            {
                _errorHandler.HandleAuthenticationError("用户未登录，无法创建角色");
                return false;
            }

            _isCreatingCharacter = true;

            try
            {
                _stateManager.SetLoadingState(true);

                // 发送创建角色请求
                var createCharacterRequest = new CreateCharacterRequest
                {
                    UserId = currentState.UserSession.UserId,
                    CharacterName = characterName,
                    Profession = (Profession)profession,
                    Gender = gender,
                    Appearance = appearance ?? new AppearanceInfo(),
                    GameId = 1,
                    ZoneId = 1,
                    ServerId = 1
                };
                
                var networkManager = HundunWorldGame.Instance.NetworkManager;
                var createMessagePacket = new HorizonMessagePacket
                {
                    Header = new MessageHeader
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        MessageType = MessageType.CreateCharacter,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        GameId = networkManager?.GameId ?? 1,
                        ZoneId = networkManager?.ZoneId ?? 1,
                        ServerId = networkManager?.ServerId ?? 1,
                        UserId = networkManager?.UserId ?? 0,
                        AuthToken = networkManager?.AuthToken ?? "",
                        MachineId = MachineIdentifier.GetMachineGuid()
                    },
                    ServiceType = ServiceType.Game,
                    Body = createCharacterRequest
                };

                var success = await HundunWorldGame.Instance.NetworkManager.SendMessageAsync(createMessagePacket);

                if (success)
                {
                    FlaxEngine.Debug.Log($"角色创建请求已发送: {characterName}");
                    return true;
                }
                else
                {
                    _errorHandler.HandleError(  UIErrorType.Network, "创建角色请求失败", null, "create_character_request");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.System, $"创建角色失败: {ex.Message}", ex, "create_character");
                return false;
            }
            finally
            {
                _isCreatingCharacter = false;
                _stateManager.SetLoadingState(false);
            }
        }

        /// <summary>
        /// 处理创建角色响应
        /// </summary>
        /// <param name="response">创建角色响应</param>
        public void HandleCreateCharacterResponse(CreateCharacterResponse response)
        {
            try
            {
                if (response == null)
                {
                    FlaxEngine.Debug.LogError("[CharacterManager] 创建角色响应为空");
                    return;
                }

                if (response.IsSuccess)
                {
                    // 添加新角色到列表
                    if (response.Character != null)
                    {
                        _characterList.Add(response.Character);
                        
                        // 更新状态管理器
                        if (_stateManager != null)
                        {
                            var currentState = _stateManager.GetCurrentState();
                            currentState.Characters = new List<CharacterInfo>(_characterList);
                            currentState.IncrementVersion();
                        }

                        // 发布事件
                        CharacterCreated?.Invoke(response.Character);
                        CharacterListUpdated?.Invoke(_characterList);
                        _eventBus?.Publish(new CharacterListUpdatedEvent(_characterList));

                        FlaxEngine.Debug.Log($"角色创建成功: {response.Character.CharacterName}");

                        // 保存默认外观数据
                        _ = SaveDefaultAppearanceAsync(response.Character.CharacterId);

                        // 自动切换回角色选择界面
                        if (_switchController != null)
                        {
                            _switchController.RequestSceneSwitch(SceneType.CharacterSelection);
                        }
                        else
                        {
                            FlaxEngine.Debug.LogWarning("[CharacterManager] _switchController为空，无法切换场景");
                            // 降级方案：直接通过状态管理器切换
                            _stateManager?.TransitionToScene(SceneType.CharacterSelection, checkConditions: false);
                        }
                    }
                }
                else
                {
                    _errorHandler?.HandleValidationError(response.Message ?? "角色创建失败", "character_creation");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterManager] 处理角色创建响应异常: {ex.Message}\n{ex.StackTrace}");
                _errorHandler?.HandleError(UIErrorType.System, $"处理角色创建响应失败: {ex.Message}", ex, "create_character_response");
            }
        }

        /// <summary>
        /// 保存角色默认外观数据
        /// </summary>
        /// <param name="characterId">角色ID</param>
        private async Task SaveDefaultAppearanceAsync(ulong characterId)
        {
            try
            {
                var appearance = new CharacterPersistenceService.AppearanceData
                {
                    BodyEquipmentId = EquipmentDatabase.DefaultBodyId,
                    AccessoryIds = new System.Collections.Generic.List<int> { EquipmentDatabase.DefaultHeadScarfId },
                    WeaponIds = new System.Collections.Generic.List<int> { EquipmentDatabase.DefaultLongswordId }
                };

                await CharacterPersistenceService.Instance.SaveAppearanceAsync(characterId, appearance);
                FlaxEngine.Debug.Log($"[CharacterManager] 已保存角色默认外观: CharacterId={characterId}");
            }
            catch (System.Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterManager] 保存角色默认外观失败: {ex.Message}");
            }
        }

        #endregion

        #region 角色删除

        /// <summary>
        /// 删除角色
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <returns>是否成功</returns>
        public async Task<bool> DeleteCharacterAsync(ulong characterId)
        {
            if (characterId == 0)
            {
                _errorHandler.HandleValidationError("无效的角色ID", "character_id");
                return false;
            }

            if (_isDeletingCharacter) return false;

            var currentState = _stateManager.GetCurrentState();
            if (!currentState.UserSession.IsAuthenticated)
            {
                _errorHandler.HandleAuthenticationError("用户未登录，无法删除角色");
                return false;
            }

            _isDeletingCharacter = true;

            try
            {
                _stateManager.SetLoadingState(true);

                // 发送删除角色请求
                var deleteCharacterRequest = new DeleteCharacterRequest
                {
                    CharacterId = characterId,
                    UserId = currentState.UserSession.UserId
                };
                
                var networkManager = HundunWorldGame.Instance.NetworkManager;
                var deleteMessagePacket = new HorizonMessagePacket
                {
                    Header = new MessageHeader
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        MessageType = MessageType.CharacterDelete,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        GameId = networkManager?.GameId ?? 1,
                        ZoneId = networkManager?.ZoneId ?? 1,
                        ServerId = networkManager?.ServerId ?? 1,
                        UserId = networkManager?.UserId ?? 0,
                        AuthToken = networkManager?.AuthToken ?? "",
                        MachineId = MachineIdentifier.GetMachineGuid()
                    },
                    ServiceType = ServiceType.Game,
                    Body = deleteCharacterRequest
                };

                var success = await HundunWorldGame.Instance.NetworkManager.SendMessageAsync(deleteMessagePacket);

                if (success)
                {
                    FlaxEngine.Debug.Log($"角色删除请求已发送: {characterId}");
                    return true;
                }
                else
                {
                    _errorHandler.HandleError(UIErrorType.Network, "删除角色请求失败", null, "delete_character_request");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(UIErrorType.System, $"删除角色失败: {ex.Message}", ex, "delete_character");
                return false;
            }
            finally
            {
                _isDeletingCharacter = false;
                _stateManager.SetLoadingState(false);
            }
        }

        /// <summary>
        /// 处理删除角色响应
        /// </summary>
        /// <param name="response">删除角色响应</param>
        public void HandleDeleteCharacterResponse(DeleteCharacterResponse response)
        {
            try
            {
                if (response == null)
                {
                    FlaxEngine.Debug.LogError("[CharacterManager] 删除角色响应为空");
                    return;
                }

                if (response.Success)
                {
                    // 从列表中移除角色
                    var characterToRemove = _characterList.Find(c => c.CharacterId == response.CharacterId);
                    if (characterToRemove != null)
                    {
                        _characterList.Remove(characterToRemove);

                        // 如果删除的是当前选中角色，清除选择
                        if (_selectedCharacter?.CharacterId == response.CharacterId)
                        {
                            _selectedCharacter = null;
                            if (_stateManager != null)
                            {
                                var state = _stateManager.GetCurrentState();
                                state.SelectedCharacter = null;
                                state.IncrementVersion();
                            }
                        }

                        // 更新状态管理器
                        if (_stateManager != null)
                        {
                            var updatedState = _stateManager.GetCurrentState();
                            updatedState.Characters = new List<CharacterInfo>(_characterList);
                            updatedState.IncrementVersion();
                        }

                        // 发布事件
                        CharacterDeleted?.Invoke(response.CharacterId);
                        CharacterListUpdated?.Invoke(_characterList);
                        _eventBus?.Publish(new CharacterListUpdatedEvent(_characterList));

                        FlaxEngine.Debug.Log($"角色删除成功: {response.CharacterId}");
                    }
                }
                else
                {
                    _errorHandler?.HandleValidationError(response.Message ?? "角色删除失败", "character_deletion");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CharacterManager] 处理角色删除响应异常: {ex.Message}\n{ex.StackTrace}");
                _errorHandler?.HandleError(UIErrorType.System, $"处理角色删除响应失败: {ex.Message}", ex, "delete_character_response");
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 用户会话变更事件处理
        /// </summary>
        /// <param name="eventData">事件数据</param>
        private void OnUserSessionChanged(UserSessionChangedEvent eventData)
        {
            if (eventData.NewSession?.IsAuthenticated == true)
            {
                // 用户登录成功，加载角色列表
                _ = LoadCharacterListAsync();
            }
            else
            {
                // 用户登出，清空角色数据
                _characterList.Clear();
                _selectedCharacter = null;
                CharacterListUpdated?.Invoke(_characterList);
            }
        }

        /// <summary>
        /// 场景切换完成事件处理
        /// </summary>
        /// <param name="eventData">事件数据</param>
        private void OnSceneTransitionCompleted(SceneTransitionCompletedEvent eventData)
        {
            if (eventData.ToScene == SceneType.CharacterSelection && eventData.IsSuccess)
            {
                // 切换到角色选择场景时，确保角色列表是最新的
                _ = LoadCharacterListAsync();
            }
        }

        #endregion

        #region 公共属性

        /// <summary>
        /// 当前角色列表
        /// </summary>
        public List<CharacterInfo> CharacterList => new List<CharacterInfo>(_characterList);

        /// <summary>
        /// 当前选中的角色
        /// </summary>
        public CharacterInfo SelectedCharacter => _selectedCharacter;

        /// <summary>
        /// 是否正在处理操作
        /// </summary>
        public bool IsProcessing => _isLoadingCharacterList || _isCreatingCharacter || _isDeletingCharacter || _isEnteringGame;

        #endregion
    }
}