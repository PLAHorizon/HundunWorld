using System;
using System.Collections.Generic;
using FlaxEngine;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.StateValidation
{
    /// <summary>
    /// 场景切换验证器 - 专门用于验证场景切换的合法性
    /// 修复场景切换后无法再次切换的Bug
    /// </summary>
    public class SceneTransitionValidator
    {
        private static SceneTransitionValidator _instance;
        public static SceneTransitionValidator Instance => _instance ??= new SceneTransitionValidator();

        // 场景切换历史记录（用于调试和错误恢复）
        private readonly Queue<SceneTransitionRecord> _transitionHistory = new Queue<SceneTransitionRecord>();
        private const int MAX_HISTORY_COUNT = 10;

        // 错误计数器
        private readonly Dictionary<string, int> _errorCounters = new Dictionary<string, int>();
        private const int MAX_ERROR_COUNT = 3;

        /// <summary>
        /// 验证场景切换是否合法
        /// </summary>
        /// <param name="fromScene">源场景</param>
        /// <param name="toScene">目标场景</param>
        /// <param name="userSession">用户会话信息</param>
        /// <returns>验证结果</returns>
        public ValidationResult ValidateTransition(SceneType fromScene, SceneType toScene, UserSession userSession)
        {
            var result = new ValidationResult { IsValid = true };

            try
            {
                // 1. 基本验证 - 检查场景是否相同
                if (fromScene == toScene)
                {
                    result.IsValid = false;
                    result.ErrorMessage = "源场景和目标场景相同，无需切换";
                    result.WarningLevel = ValidationWarningLevel.Info;
                    return result;
                }

                // 2. 权限验证 - 检查用户是否有权限访问目标场景
                if (!ValidateScenePermission(toScene, userSession))
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"用户无权限访问场景: {toScene}";
                    result.WarningLevel = ValidationWarningLevel.Error;
                    return result;
                }

                // 3. 状态验证 - 检查切换路径是否合法
                if (!ValidateTransitionPath(fromScene, toScene))
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"不允许从 {fromScene} 直接切换到 {toScene}";
                    result.WarningLevel = ValidationWarningLevel.Warning;
                    return result;
                }

                // 4. 历史验证 - 检查是否存在循环切换
                if (DetectCircularTransition(fromScene, toScene))
                {
                    result.IsValid = false;
                    result.ErrorMessage = "检测到循环切换，可能存在无限循环";
                    result.WarningLevel = ValidationWarningLevel.Warning;
                    return result;
                }

                // 5. 错误频率验证 - 检查是否频繁出错
                var transitionKey = $"{fromScene}->{toScene}";
                if (_errorCounters.ContainsKey(transitionKey) && _errorCounters[transitionKey] >= MAX_ERROR_COUNT)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"场景切换 {transitionKey} 错误次数过多，暂时禁用";
                    result.WarningLevel = ValidationWarningLevel.Error;
                    return result;
                }

                // 记录成功的验证
                RecordTransition(fromScene, toScene, true);
                ResetErrorCounter(transitionKey);

                FlaxEngine.Debug.Log($"场景切换验证通过: {fromScene} -> {toScene}");
                return result;
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.ErrorMessage = $"验证过程中发生异常: {ex.Message}";
                result.WarningLevel = ValidationWarningLevel.Error;
                FlaxEngine.Debug.LogError($"场景切换验证异常: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// 验证场景访问权限
        /// </summary>
        private bool ValidateScenePermission(SceneType targetScene, UserSession userSession)
        {
            switch (targetScene)
            {
                case SceneType.Start:
                case SceneType.Login:
                case SceneType.Register:
                    return true; // 任何人都可以访问

                case SceneType.CharacterSelection:
                case SceneType.CharacterCreation:
                    return userSession?.IsAuthenticated ?? false; // 需要登录

                case SceneType.GameWorld:
                case SceneType.Settings:
                case SceneType.Shop:
                case SceneType.Inventory:
                    return userSession?.IsAuthenticated ?? false; // 需要登录且有角色

                default:
                    return true; // 默认允许
            }
        }

        /// <summary>
        /// 验证切换路径合法性
        /// </summary>
        private bool ValidateTransitionPath(SceneType fromScene, SceneType toScene)
        {
            // 定义合法的切换路径矩阵
            var validTransitions = new Dictionary<SceneType, SceneType[]>
            {
                [SceneType.Start] = new[] { SceneType.Login, SceneType.Register },
                [SceneType.Login] = new[] { SceneType.Start, SceneType.Register, SceneType.CharacterSelection },
                [SceneType.Register] = new[] { SceneType.Start, SceneType.Login, SceneType.CharacterSelection },
                [SceneType.CharacterSelection] = new[] { SceneType.Login, SceneType.CharacterCreation, SceneType.GameWorld },
                [SceneType.CharacterCreation] = new[] { SceneType.CharacterSelection },
                [SceneType.GameWorld] = new[] { SceneType.CharacterSelection, SceneType.Settings, SceneType.Shop, SceneType.Inventory, SceneType.Login },
                [SceneType.Settings] = new[] { SceneType.GameWorld },
                [SceneType.Shop] = new[] { SceneType.GameWorld },
                [SceneType.Inventory] = new[] { SceneType.GameWorld }
            };

            if (validTransitions.ContainsKey(fromScene))
            {
                foreach (var validTarget in validTransitions[fromScene])
                {
                    if (validTarget == toScene)
                        return true;
                }
            }

            // 允许紧急退出到登录界面
            if (toScene == SceneType.Login || toScene == SceneType.Start)
                return true;

            return false;
        }

        /// <summary>
        /// 检测循环切换
        /// </summary>
        private bool DetectCircularTransition(SceneType fromScene, SceneType toScene)
        {
            if (_transitionHistory.Count < 3) return false;

            var recentTransitions = new List<SceneTransitionRecord>(_transitionHistory);
            var pattern = $"{fromScene}->{toScene}";

            int patternCount = 0;
            foreach (var record in recentTransitions)
            {
                if (record.TransitionPattern == pattern && DateTime.UtcNow - record.Timestamp < TimeSpan.FromSeconds(10))
                {
                    patternCount++;
                }
            }

            return patternCount >= 3; // 10秒内相同切换超过3次认为是循环
        }

        /// <summary>
        /// 记录切换历史
        /// </summary>
        private void RecordTransition(SceneType fromScene, SceneType toScene, bool isSuccess)
        {
            var record = new SceneTransitionRecord
            {
                FromScene = fromScene,
                ToScene = toScene,
                TransitionPattern = $"{fromScene}->{toScene}",
                Timestamp = DateTime.UtcNow,
                IsSuccess = isSuccess
            };

            _transitionHistory.Enqueue(record);

            // 保持历史记录数量限制
            while (_transitionHistory.Count > MAX_HISTORY_COUNT)
            {
                _transitionHistory.Dequeue();
            }
        }

        /// <summary>
        /// 记录切换错误
        /// </summary>
        public void RecordTransitionError(SceneType fromScene, SceneType toScene, string errorMessage)
        {
            var transitionKey = $"{fromScene}->{toScene}";

            if (!_errorCounters.ContainsKey(transitionKey))
            {
                _errorCounters[transitionKey] = 0;
            }

            _errorCounters[transitionKey]++;
            RecordTransition(fromScene, toScene, false);

            FlaxEngine.Debug.LogError($"场景切换错误记录: {transitionKey}, 错误次数: {_errorCounters[transitionKey]}, 错误信息: {errorMessage}");
        }

        /// <summary>
        /// 重置错误计数器
        /// </summary>
        private void ResetErrorCounter(string transitionKey)
        {
            if (_errorCounters.ContainsKey(transitionKey))
            {
                _errorCounters[transitionKey] = 0;
            }
        }

        /// <summary>
        /// 获取切换历史
        /// </summary>
        public List<SceneTransitionRecord> GetTransitionHistory()
        {
            return new List<SceneTransitionRecord>(_transitionHistory);
        }

        /// <summary>
        /// 清理过期的错误计数
        /// </summary>
        public void CleanupExpiredErrors()
        {
            var expiredKeys = new List<string>();

            // 这里可以添加过期逻辑，比如清理超过一定时间的错误记录
            // 目前简单实现为清理所有错误计数
            foreach (var key in _errorCounters.Keys)
            {
                if (_errorCounters[key] == 0)
                {
                    expiredKeys.Add(key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _errorCounters.Remove(key);
            }
        }
    }

    #region 辅助类

    /// <summary>
    /// 场景切换记录
    /// </summary>
    public class SceneTransitionRecord
    {
        public SceneType FromScene { get; set; }
        public SceneType ToScene { get; set; }
        public string TransitionPattern { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsSuccess { get; set; }
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = "";
        public ValidationWarningLevel WarningLevel { get; set; } = ValidationWarningLevel.None;
    }

    /// <summary>
    /// 验证警告级别
    /// </summary>
    public enum ValidationWarningLevel
    {
        None,
        Info,
        Warning,
        Error
    }

    #endregion
}