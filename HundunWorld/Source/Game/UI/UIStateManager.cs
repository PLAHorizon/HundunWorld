using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.UI.Enums;
using HundunWorld.Game.UI.ErrorRecovery;
using HundunWorld.Game.UI.States;
using HundunWorld.Game.UI.StateValidation;
using HundunWorld.Game.UI.Events;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 场景类型枚举
    /// </summary>
    

    /// <summary>
    /// 用户会话信息
    /// </summary>
    public class UserSession
    {
        public string Username { get; set; } = "";
        public ulong UserId { get; set; }
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

        public string SessionToken { get; internal set; }
        public DateTime LoginTime { get; internal set; }

        public void Clear()
        {
            Username = "";
            UserId = 0;
            AccessToken = "";
            RefreshToken = "";
        }
    }

   
    /// <summary>
    /// UI状态管理器
    /// 负责管理UI的全局状态、场景切换和数据共享
    /// </summary>
    public class UIStateManager : Script
    {
        private static UIStateManager _instance;
        
        // 当前状态
        private SceneType _currentScene = SceneType.Start;
        private bool _isLoading = false;
        private string _errorMessage = "";
        
        // 用户会话
        private UserSession _userSession = new UserSession();
        
        // 角色信息
        private CharacterInfo _selectedCharacter;
        private List<CharacterInfo> _characterList = new List<CharacterInfo>();
        
        // 增强功能组件
       
        private EnhancedSceneRecoveryManager _recoveryManager;
        
        // 事件
        public event Action<SceneType, SceneType> SceneChanged;
        public event Action<bool> LoadingStateChanged;
        public event Action<string> ErrorOccurred;
        public event Action<UserSession> UserSessionChanged;
        public event Action<CharacterInfo> SelectedCharacterChanged;
        public event Action<List<CharacterInfo>> CharacterListUpdated;
        public static UIStateManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var gameObject = Level.FindActor("UIStateManager") ?? new EmptyActor();
                    gameObject.Name = "UIStateManager";
                    _instance = gameObject.GetScript<UIStateManager>() ?? gameObject.AddScript<UIStateManager>();
                    Engine.RequestingExit += () =>
                    {
                       _instance = null;
                    };
                }
                return _instance;
            }
        }

        public override void OnAwake()
        {
            if (_instance == null)
            {
                _instance = this;
                // 保持状态管理器的持久性，不要销毁Actor
                Actor.SetStaticFlag(StaticFlags.FullyStatic, true);
            }
            else if (_instance != this)
            {
                // 如果已存在实例，销毁重复的脚本但保持Actor
                FlaxEngine.Debug.LogWarning("检测到重复的UIStateManager实例，销毁重复脚本");
                Destroy(this);
                return;
            }
            
            // 重置当前场景状态
            ResetSceneState();
        }

        public override void OnStart()
        {
            FlaxEngine.Debug.Log($"UI状态管理器初始化完成- 当前场景: {_currentScene}");
            
           
            
            // 初始化增强功能组件
            InitializeEnhancedComponents();
        }
        
        public override void OnDestroy()
        {
            // 清理状态管理器资源
            CleanupStateManager();
            
            // 濡傛灉杩欐槸褰撳墠瀹炰緥锛屾竻绌洪潤鎬佸紩鐢?
            if (_instance == this)
            {
                _instance = null;
            }
        }
        
        /// <summary>
        /// 重置场景状态- 修复FlaxEditor播放模式退出后状态残留问题
        /// </summary>
        private void ResetSceneState()
        {
            _currentScene = SceneType.Start;
            _isLoading = false;
            _errorMessage = "";
            
            // 娓呯悊鐢ㄦ埛浼氳瘽
            _userSession?.Clear();
            _selectedCharacter = null;
            _characterList?.Clear();
            
            FlaxEngine.Debug.Log("UI状态已重置到初始状态");
        }
        
        /// <summary>
        /// 初始化增强功能组件
        /// </summary>
        private void InitializeEnhancedComponents()
        {
            try
            {
                
                _recoveryManager = EnhancedSceneRecoveryManager.Instance;
                
                // 创建初始状态快照
                _recoveryManager.CreateStateSnapshot(this);
                
                FlaxEngine.Debug.Log("增强功能组件初始化完成");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"增强功能组件初始化失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清理状态管理器资源
        /// </summary>
        private void CleanupStateManager()
        {
            // 简化清理过程
            FlaxEngine.Debug.Log("UI状态管理器资源已清理");
        }


      
        #region 状态管理

        /// <summary>
        /// 鍦烘櫙杞崲
        /// </summary>
        /// <param name="newScene">目标场景</param>
        /// <param name="checkConditions">是否检查转换条件</param>
        /// <returns>转换是否成功</returns>
        public bool TransitionToScene(SceneType newScene, bool checkConditions = true)
        {
            FlaxEngine.Debug.Log($"[TransitionToScene] 被调用: 从 {_currentScene} 到 {newScene}, checkConditions={checkConditions}");
            
            // 检查是否为重复切换
            if (_currentScene == newScene)
            {
                FlaxEngine.Debug.LogWarning($"目标场景与当前场景相同: {newScene}，跳过切换");
                return true; // 返回true表示已经在目标场景
            }

            if (checkConditions && !CanTransitionTo(newScene))
            {
                FlaxEngine.Debug.LogWarning($"无法从{_currentScene} 转换到{newScene}");
                return false;
            }

            var previousScene = _currentScene;
            
            FlaxEngine.Debug.Log($"[TransitionToScene] 场景转换: {previousScene} -> {newScene}");
            
            try
            {
                // 先更新场景状态，再触发事件
                _currentScene = newScene;
                FlaxEngine.Debug.Log($"[TransitionToScene] _currentScene已更新为: {_currentScene}");
                
                // 触发场景切换事件
                FlaxEngine.Debug.Log($"[TransitionToScene] 准备触发SceneChanged事件");
                SceneChanged?.Invoke(previousScene, newScene);
                FlaxEngine.Debug.Log($"[TransitionToScene] SceneChanged事件已触发");
                
                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"场景切换事件处理失败: {ex.Message}");
                FlaxEngine.Debug.LogError($"堆栈跟踪: {ex.StackTrace}");
                // 回滚状态
                _currentScene = previousScene;
                return false;
            }
        }
        
        /// <summary>
        /// 登录成功后更新状态（不再负责场景切换）
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="userId">用户ID</param>
        /// <param name="accessToken">访问令牌</param>
        /// <param name="refreshToken">刷新令牌</param>
        public void HandleLoginSuccess(string username, ulong userId, string accessToken, string refreshToken = "")
        {
            FlaxEngine.Debug.Log($"[HandleLoginSuccess] 更新用户会话: username={username}, userId={userId}");
            
            // 只更新用户会话，场景切换由调用方负责
            UpdateUserSession(username, userId, accessToken, refreshToken);
            
            FlaxEngine.Debug.Log("[HandleLoginSuccess] 用户会话已更新，等待场景切换");
        }
        
        /// <summary>
        /// 角色选择成功后更新状态（不再负责场景切换）
        /// </summary>
        /// <param name="character">选中的角色</param>
        public void HandleCharacterSelected(CharacterInfo character)
        {
            SetSelectedCharacter(character);
            FlaxEngine.Debug.Log($"[HandleCharacterSelected] 角色已选中: {character?.CharacterName ?? "null"}，等待场景切换");
        }
        
        /// <summary>
        /// 强制转换场景（用于错误处理和特殊情况）
        /// </summary>
        /// <param name="newScene">目标场景</param>
        /// <param name="reason">强制转换的原因</param>
        public void ForceTransitionToScene(SceneType newScene, string reason = "")
        {
            try
            {
                var previousScene = _currentScene;
                _currentScene = newScene;
                
                FlaxEngine.Debug.LogWarning($"强制场景转换: {previousScene} -> {newScene}, 原因: {reason}");
                
                // 清除错误状态
                ClearError();
                SetLoadingState(false);
                
                SceneChanged?.Invoke(previousScene, newScene);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"强制场景转换失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 手动重置UI状态
        /// 用于在编辑器播放模式退出后手动调用
        /// </summary>
        public void ManualResetUIState()
        {
            FlaxEngine.Debug.Log("手动重置UI状态");
            ResetSceneState();
        }

        /// <summary>
        /// 检查是否可以转换到指定场景
        /// 修复登录后无法转换到游戏主界面的问题
        /// </summary>
        /// <param name="targetScene">目标场景</param>
        /// <returns>是否可以转换</returns>
        private bool CanTransitionTo(SceneType targetScene)
        {
           

            switch (_currentScene)
            {
                case SceneType.Start:
                    return targetScene == SceneType.Login || 
                           targetScene == SceneType.Register || 
                           targetScene == SceneType.CharacterSelection; // 允许直接转换
                    
                case SceneType.Login:
                    return targetScene == SceneType.Register ||
                           targetScene == SceneType.CharacterSelection ||
                           targetScene == SceneType.Start; // 允许返回初始状态
                    
                case SceneType.Register:
                    return targetScene == SceneType.Login ||
                           targetScene == SceneType.CharacterSelection ||
                           targetScene == SceneType.Start; // 允许返回初始状态
                    
                case SceneType.CharacterSelection:
                    return targetScene == SceneType.Login || 
                           targetScene == SceneType.CharacterCreation ||
                           targetScene == SceneType.GameWorld ||
                           targetScene == SceneType.Register || // 允许重新注册
                           targetScene == SceneType.Start; // 允许返回初始状态
                    
                case SceneType.CharacterCreation:
                    return targetScene == SceneType.CharacterSelection ||
                           targetScene == SceneType.Login ||
                           targetScene == SceneType.Register || // 允许重新注册
                           targetScene == SceneType.Start; // 允许返回初始状态
                    
                case SceneType.GameWorld:
                    return targetScene == SceneType.CharacterSelection ||
                           targetScene == SceneType.Login ||
                           targetScene == SceneType.Register || // 允许重新注册
                           targetScene == SceneType.Settings || // 允许访问设置
                           targetScene == SceneType.Shop || // 允许访问商城
                           targetScene == SceneType.Inventory || // 允许访问背包
                           targetScene == SceneType.Start; // 允许直接登出或返回初始状态
                    
                case SceneType.Settings:
                case SceneType.Shop:
                case SceneType.Inventory:
                    return targetScene == SceneType.GameWorld || // 允许返回游戏世界
                           targetScene == SceneType.CharacterSelection ||
                           targetScene == SceneType.Login ||
                           targetScene == SceneType.Start; // 允许退出
                    
                default:
                    return true; // 默认允许转换，由具体管理器处理验证
            }
        }

        /// <summary>
        /// 设置加载状态
        /// </summary>
        /// <param name="isLoading">是否正在加载</param>
        public void SetLoadingState(bool isLoading)
        {
            if (_isLoading != isLoading)
            {
                _isLoading = isLoading;
                LoadingStateChanged?.Invoke(_isLoading);
            }
        }

        /// <summary>
        /// 设置错误信息
        /// </summary>
        /// <param name="errorMessage">错误消息</param>
        public void SetError(string errorMessage)
        {
            _errorMessage = errorMessage;
            if (!string.IsNullOrEmpty(errorMessage))
            {
                FlaxEngine.Debug.LogError($"UI错误: {errorMessage}");
                ErrorOccurred?.Invoke(errorMessage);
            }
        }

        /// <summary>
        /// 清除错误信息
        /// </summary>
        public void ClearError()
        {
            _errorMessage = "";
        }

        #endregion

        #region 用户会话管理

        /// <summary>
        /// 更新用户会话信息
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="userId">用户ID</param>
        /// <param name="accessToken">访问令牌</param>
        /// <param name="refreshToken">刷新令牌</param>
        public void UpdateUserSession(string username, ulong userId, string accessToken, string refreshToken = "")
        {
            var oldSession = new UserSession
            {
                Username = _userSession.Username,
                UserId = _userSession.UserId,
                AccessToken = _userSession.AccessToken,
                RefreshToken = _userSession.RefreshToken
            };

            _userSession.Username = username;
            _userSession.UserId = userId;
            _userSession.AccessToken = accessToken;
            _userSession.RefreshToken = refreshToken;
            
            FlaxEngine.Debug.Log($"用户会话已更新: {username} (ID: {userId})");
            UserSessionChanged?.Invoke(_userSession);

            // 发布到全局事件总线，确保其他系统（如 CharacterManager）能收到通知
            UIEventBus.Instance.Publish(new UserSessionChangedEvent(oldSession, _userSession));
        }

        /// <summary>
        /// 清除用户会话
        /// </summary>
        public void ClearUserSession()
        {
            _userSession.Clear();
            _selectedCharacter = null;
            _characterList.Clear();
            
            FlaxEngine.Debug.Log("用户会话已清除");
            UserSessionChanged?.Invoke(_userSession);
            SelectedCharacterChanged?.Invoke(null);
            CharacterListUpdated?.Invoke(_characterList);
        }

        #endregion

        #region 角色管理

        /// <summary>
        /// 更新角色列表
        /// </summary>
        public void UpdateCharacterList(List<CharacterInfo> characters)
        {
            _characterList = characters ?? new List<CharacterInfo>();
            FlaxEngine.Debug.Log($"角色列表已更新: {_characterList.Count} 个角色");
            CharacterListUpdated?.Invoke(_characterList);
        }

        /// <summary>
        /// 添加角色
        /// </summary>
        public void AddCharacter(CharacterInfo character)
        {
            if (character != null && !_characterList.Contains(character))
            {
                _characterList.Add(character);
                FlaxEngine.Debug.Log($"添加角色: {character.CharacterName}");
                CharacterListUpdated?.Invoke(_characterList);
            }
        }

        /// <summary>
        /// 移除角色
        /// </summary>
        public void RemoveCharacter(ulong characterId)
        {
            var character = _characterList.Find(c => c.CharacterId == characterId);
            if (character != null)
            {
                _characterList.Remove(character);
                FlaxEngine.Debug.Log($"移除角色: {character.CharacterName}");
                
                // 如果移除的是当前选中的角色，清除选择
                if (_selectedCharacter?.CharacterId == characterId)
                {
                    SetSelectedCharacter(null);
                }
                
                CharacterListUpdated?.Invoke(_characterList);
            }
        }

        /// <summary>
        /// 设置选中的角色
        /// </summary>
        public void SetSelectedCharacter(CharacterInfo character)
        {
            _selectedCharacter = character;
            FlaxEngine.Debug.Log($"选中角色: {character?.CharacterName ?? "无"}");
            SelectedCharacterChanged?.Invoke(_selectedCharacter);
        }

        #endregion
        
        #region 新架构兼容方法
        
        /// <summary>
        /// 获取当前UI状态（新架构兼容）
        /// </summary>
        public UIState GetCurrentState()
        {
            return new UIState
            {
                CurrentScene = _currentScene,
                UserSession = new UserSession
                {
                    Username = _userSession.Username,
                    UserId = _userSession.UserId,
                    SessionToken = _userSession.SessionToken,
                    AccessToken = _userSession.AccessToken,
                    LoginTime = _userSession.LoginTime
                },
                Characters = _characterList?.ToList() ?? new List<CharacterInfo>(),
                SelectedCharacter = _selectedCharacter,
                HasError = !string.IsNullOrEmpty(_errorMessage),
                ErrorMessage = _errorMessage,
                IsLoading = _isLoading
            };
        }
        
        /// <summary>
        /// 更新当前场景（异步兼容）
        /// </summary>
        public async Task UpdateCurrentSceneAsync(SceneState sceneState)
        {
            if (sceneState != null)
            {
                var success = TransitionToScene(sceneState.SceneType, false);
                SetLoadingState(sceneState.IsLoading);
                
                if (!string.IsNullOrEmpty(sceneState.LoadingMessage))
                {
                    FlaxEngine.Debug.Log($"加载状态: {sceneState.LoadingMessage}");
                }
            }
            
            await Task.CompletedTask;
        }
        
        /// <summary>
        /// 更新用户会话（异步兼容）
        /// </summary>
        public async Task UpdateUserSessionAsync(UserSession userSession)
        {
            if (userSession != null)
            {
                // 转换新架构的UserSession到旧结构
                _userSession.Username = userSession.Username;
                _userSession.UserId = userSession.UserId;
                _userSession.AccessToken = userSession.AccessToken;
                _userSession.SessionToken = userSession.SessionToken;
                _userSession.LoginTime = userSession.LoginTime;
                
                FlaxEngine.Debug.Log($"用户会话已更新: {userSession.Username}");
                UserSessionChanged?.Invoke(_userSession);
            }
            
            await Task.CompletedTask;
        }
        
        #endregion
        
        #region 错误恢复方法
        
        /// <summary>
        /// 尝试从验证错误中恢复
        /// </summary>
        private async Task TryRecoverFromValidationError(ValidationResult validationResult, SceneType fromScene, SceneType toScene)
        {
            if (_recoveryManager == null) return;
            
            RecoveryScenario scenario;
            switch (validationResult.WarningLevel)
            {
                case ValidationWarningLevel.Warning:
                    scenario = RecoveryScenario.CircularTransition;
                    break;
                case ValidationWarningLevel.Error:
                    scenario = RecoveryScenario.PermissionDenied;
                    break;
                default:
                    return;
            }
            
            await _recoveryManager.HandleTransitionErrorAsync(
                scenario, fromScene, toScene, validationResult.ErrorMessage, this);
        }
        
        /// <summary>
        /// 尝试从异常中恢复
        /// </summary>
        private async Task TryRecoverFromException(Exception ex, SceneType fromScene, SceneType toScene)
        {
            if (_recoveryManager == null) return;
            
            await _recoveryManager.HandleTransitionErrorAsync(
                RecoveryScenario.UnexpectedException, fromScene, toScene, ex.Message, this);
        }
        
        /// <summary>
        /// 获取恢复统计信息
        /// </summary>
        public RecoveryStatistics GetRecoveryStatistics()
        {
            return _recoveryManager?.GetStatistics() ?? new RecoveryStatistics();
        }
        
        /// <summary>
        /// 定期清理过期数据
        /// </summary>
        public void PerformMaintenance()
        {
            try
            {
                SceneTransitionValidator.CleanupExpiredErrors();
                _recoveryManager?.CleanupExpiredSnapshots();
                FlaxEngine.Debug.Log("状态管理器维护完成");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"维护过程中发生错误: {ex.Message}");
            }
        }
        
        #endregion

        #region 属性

        public SceneType CurrentScene => _currentScene;
        public bool IsLoading => _isLoading;
        public string ErrorMessage => _errorMessage;
        public UserSession UserSession => _userSession;
        public CharacterInfo SelectedCharacter => _selectedCharacter;
        public List<CharacterInfo> CharacterList => new List<CharacterInfo>(_characterList);

        // 兼容性属性 - 为旧代码提供访问
        public UserSession CurrentUserSession => _userSession;

        #endregion

        #region 兼容性方法

        /// <summary>
        /// 更新用户会话（兼容性方法）
        /// </summary>
        public void UpdateUserSession(UserSession session)
        {
            if (session != null)
            {
                _userSession.Username = session.Username;
                _userSession.UserId = session.UserId;
                _userSession.AccessToken = session.AccessToken;
                _userSession.RefreshToken = session.RefreshToken;
                _userSession.SessionToken = session.SessionToken;
                _userSession.LoginTime = session.LoginTime;
                
                FlaxEngine.Debug.Log($"用户会话已更新: {session.Username}");
                UserSessionChanged?.Invoke(_userSession);
            }
        }

        #endregion
    }
}
