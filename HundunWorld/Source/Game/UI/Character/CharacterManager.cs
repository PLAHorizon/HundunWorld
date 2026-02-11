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
        private UIStateManager _stateManager;
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
            _stateManager = UIStateManager.Instance;
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

                // 发送进入游戏请求
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
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    },
                    ServiceType = ServiceType.Game,
                    Body = enterGameRequest
                };

                var success = await HundunWorldGame.Instance.NetworkManager.SendMessageAsync(messagePacket);

                if (success)
                {
                    // 切换到游戏场景
                    var switchResult = _switchController.RequestSceneSwitch(SceneType.GameWorld);
                    if (switchResult.IsSuccess)
                    {
                        FlaxEngine.Debug.Log($"成功进入游戏: {_selectedCharacter.CharacterName}");
                        return true;
                    }
                    else
                    {
                        _errorHandler.HandleError(UIErrorType.Transition, "切换到游戏场景失败", null, "enter_game_transition");
                        return false;
                    }
                }
                else
                {
                    _errorHandler.HandleError(UIErrorType.Network, "进入游戏请求失败", null, "enter_game_request");
                    return false;
                }
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
                
                var createMessagePacket = new HorizonMessagePacket
                {
                    Header = new MessageHeader
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        MessageType = MessageType.CreateCharacter,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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
                
                var deleteMessagePacket = new HorizonMessagePacket
                {
                    Header = new MessageHeader
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        MessageType = MessageType.CharacterDelete,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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