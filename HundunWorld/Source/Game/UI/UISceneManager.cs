using System;
using System.Collections.Generic;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Authentication;
using HundunWorld.Game.UI.GameMain;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// UI场景管理器
    /// 负责管理UI场景的切换、显示和隐藏逻辑
    /// </summary>
    public class UISceneManager : Script
    {
        private UIStateManager _stateManager;
        
        // UI场景组件
        private AuthenticationUI _authenticationUI;
        private GameMainUI _gameMainUI;
        
        // 场景映射
        private Dictionary<SceneType, Action> _showSceneActions;
        private Dictionary<SceneType, Action> _hideSceneActions;
        
        public override void OnStart()
        {
            // ⚠️ UISceneManager 已由 MainUIManager 替代
            // 此脚本需要禁用以避免与 MainUIManager 的 UI 组件创建冲突
            FlaxEngine.Debug.LogWarning("[UISceneManager] 已禁用此脚本，请使用 MainUIManager 管理 UI 组件");
            Actor.IsActive = false;
        }
        
        /// <summary>
        /// 初始化UI组件
        /// </summary>
        private void InitializeUIComponents()
        {
            // 查找UI组件，如果不存在则创建
            _authenticationUI = Actor.GetScript<AuthenticationUI>();
            if (_authenticationUI == null)
            {
                var authUIActor = Scene.FindActor("AuthenticationUI") ?? new EmptyActor();
                authUIActor.Name = "AuthenticationUI";
                Level.SpawnActor(authUIActor, Actor);
                _authenticationUI = authUIActor.AddScript<AuthenticationUI>();
            }
            
            _gameMainUI = Actor.GetScript<GameMainUI>();
            if (_gameMainUI == null)
            {
                var gameUIActor = Scene.FindActor("GameMainUI") ?? new EmptyActor();
                gameUIActor.Name = "GameMainUI";
                Level.SpawnActor(gameUIActor, Actor);
                _gameMainUI = gameUIActor.AddScript<GameMainUI>();
            }
        }
        
        /// <summary>
        /// 设置场景映射
        /// </summary>
        private void SetupSceneMappings()
        {
            _showSceneActions = new Dictionary<SceneType, Action>
            {
                { SceneType.Login, ShowLoginScreen },
                { SceneType.Register, ShowRegisterScreen },
                { SceneType.GameWorld, ShowGameWorld }
            };
            
            _hideSceneActions = new Dictionary<SceneType, Action>
            {
                { SceneType.Login, HideLoginScreen },
                { SceneType.Register, HideRegisterScreen },
                { SceneType.GameWorld, HideGameWorld }
            };
        }
        
        /// <summary>
        /// 场景切换事件处理
        /// </summary>
        private void OnSceneChanged(SceneType previousScene, SceneType newScene)
        {
            FlaxEngine.Debug.Log($"场景切换: {previousScene} -> {newScene}");
            
            try
            {
                // 隐藏前一个场景
                if (previousScene != SceneType.Start && _hideSceneActions.ContainsKey(previousScene))
                {
                    _hideSceneActions[previousScene]?.Invoke();
                }
                
                // 显示新场景
                if (_showSceneActions.ContainsKey(newScene))
                {
                    _showSceneActions[newScene]?.Invoke();
                }
                else
                {
                    FlaxEngine.Debug.LogWarning($"未找到场景 {newScene} 的显示方法");
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"处理场景切换时出错: {ex.Message}");
                // 尝试错误恢复
                if (_stateManager != null)
                {
                    _stateManager.SetError($"场景切换失败: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 显示指定场景
        /// </summary>
        public void ShowScene(SceneType scene)
        {
            _stateManager.TransitionToScene(scene);
        }
        
        #region 场景显示方法
        
        private void ShowLoginScreen()
        {
            _authenticationUI?.ShowAuthenticationUI();
            FlaxEngine.Debug.Log("显示登录界面");
        }
        
        private void ShowRegisterScreen()
        {
            _authenticationUI?.ShowRegisterPanel();
            FlaxEngine.Debug.Log("显示注册界面");
        }
        
        private void ShowGameWorld()
        {
            _gameMainUI?.ShowGameMainUI();
            FlaxEngine.Debug.Log("显示游戏主界面");
        }
        
        #endregion
        
        #region 场景隐藏方法
        
        private void HideLoginScreen()
        {
            _authenticationUI?.HideLoginPanel();
            FlaxEngine.Debug.Log("隐藏登录界面");
        }
        
        private void HideRegisterScreen()
        {
            _authenticationUI?.HideRegisterPanel();
            FlaxEngine.Debug.Log("隐藏注册界面");
        }
        
        private void HideGameWorld()
        {
            _gameMainUI?.HideGameMainUI();
            FlaxEngine.Debug.Log("隐藏游戏主界面");
        }
        
        #endregion
        
        #region 公共接口
        
        /// <summary>
        /// 切换到登录界面
        /// </summary>
        public void SwitchToLogin()
        {
            ShowScene(SceneType.Login);
        }
        
        /// <summary>
        /// 切换到注册界面
        /// </summary>
        public void SwitchToRegister()
        {
            ShowScene(SceneType.Register);
        }
        
        /// <summary>
        /// 切换到游戏世界界面
        /// </summary>
        public void SwitchToGameWorld()
        {
            ShowScene(SceneType.GameWorld);
        }
        
        /// <summary>
        /// 登出并返回登录界面
        /// </summary>
        public void Logout()
        {
            _stateManager.ClearUserSession();
            _stateManager.ClearError();
            ShowScene(SceneType.Login);
        }
        
        #endregion
        
        public override void OnDestroy()
        {
            // 取消订阅事件
            if (_stateManager != null)
            {
                _stateManager.SceneChanged -= OnSceneChanged;
            }
        }
    }
}