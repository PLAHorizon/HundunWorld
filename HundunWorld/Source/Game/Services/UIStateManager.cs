using System;
using FlaxEngine;

namespace HundunWorld.Game.Services
{
    /// <summary>
    /// UI状态管理器
    /// 负责管理游戏UI的状态和切换
    /// </summary>
    public class UIStateManager
    {
        private static UIStateManager _instance;
        public static UIStateManager Instance => _instance ??= new UIStateManager();

        // 当前场景状态
        private UISceneType _currentScene = UISceneType.Login;
        private UISceneType _previousScene = UISceneType.Login;

        // 事件
        public event Action<UISceneType, UISceneType> SceneChanged;
        public event Action<bool> LoadingStateChanged;
        public event Action<string> ErrorOccurred;

        public UISceneType CurrentScene => _currentScene;
        public UISceneType PreviousScene => _previousScene;

        private UIStateManager()
        {
        }

        /// <summary>
        /// 切换场景
        /// </summary>
        public void SwitchScene(UISceneType newScene)
        {
            if (_currentScene == newScene)
                return;

            var oldScene = _currentScene;
            _previousScene = _currentScene;
            _currentScene = newScene;

            Debug.Log($"场景切换: {oldScene} -> {newScene}");
            SceneChanged?.Invoke(oldScene, newScene);
        }

        /// <summary>
        /// 设置加载状态
        /// </summary>
        public void SetLoading(bool isLoading)
        {
            LoadingStateChanged?.Invoke(isLoading);
        }

        /// <summary>
        /// 触发错误
        /// </summary>
        public void TriggerError(string errorMessage)
        {
            Debug.LogError($"UI错误: {errorMessage}");
            ErrorOccurred?.Invoke(errorMessage);
        }

        /// <summary>
        /// 回到上一个场景
        /// </summary>
        public void GoBack()
        {
            SwitchScene(_previousScene);
        }
    }

    /// <summary>
    /// UI场景类型枚举
    /// </summary>
    public enum UISceneType
    {
        Login,
        CharacterSelection,
        GameWorld,
        Settings,
        Loading
    }
}