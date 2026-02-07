using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI
{
    /// <summary>
    /// 场景切换验证器
    /// 负责验证场景切换的合法性和前置条件
    /// </summary>
    public static class SceneTransitionValidator
    {
        /// <summary>
        /// 验证结果
        /// </summary>
        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public string ErrorMessage { get; set; }
            public List<string> Warnings { get; set; } = new List<string>();
        }

        /// <summary>
        /// 错误记录
        /// </summary>
        private class TransitionError
        {
            public SceneType FromScene { get; set; }
            public SceneType ToScene { get; set; }
            public string ErrorMessage { get; set; }
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// 场景切换规则定义
        /// </summary>
        private static readonly Dictionary<SceneType, HashSet<SceneType>> _transitionRules = new Dictionary<SceneType, HashSet<SceneType>>
        {
            [SceneType.Start] = new HashSet<SceneType> 
            { 
                SceneType.Login, 
                SceneType.Register ,
                SceneType.CharacterSelection
            },
            
            [SceneType.Login] = new HashSet<SceneType> 
            { 
                SceneType.Start, 
                SceneType.Register, 
                SceneType.CharacterSelection 
            },
            
            [SceneType.Register] = new HashSet<SceneType> 
            { 
                SceneType.Start, 
                SceneType.Login, 
            },
            
            [SceneType.CharacterSelection] = new HashSet<SceneType> 
            { 
                SceneType.Login, 
                SceneType.CharacterCreation, 
                SceneType.GameWorld 
            },
            
            [SceneType.CharacterCreation] = new HashSet<SceneType> 
            { 
                SceneType.CharacterSelection 
            },
            
            [SceneType.GameWorld] = new HashSet<SceneType> 
            { 
                SceneType.CharacterSelection, 
                SceneType.Login, 
                SceneType.Settings, 
                SceneType.Shop, 
                SceneType.Inventory 
            },
            
            [SceneType.Settings] = new HashSet<SceneType> 
            { 
                SceneType.GameWorld 
            },
            
            [SceneType.Shop] = new HashSet<SceneType> 
            { 
                SceneType.GameWorld 
            },
            
            [SceneType.Inventory] = new HashSet<SceneType> 
            { 
                SceneType.GameWorld 
            }
        };

        /// <summary>
        /// 需要认证的场景
        /// </summary>
        private static readonly HashSet<SceneType> _authenticatedScenes = new HashSet<SceneType>
        {
            SceneType.CharacterSelection,
            SceneType.CharacterCreation,
            SceneType.GameWorld,
            SceneType.Settings,
            SceneType.Shop,
            SceneType.Inventory
        };

        /// <summary>
        /// 需要角色的场景
        /// </summary>
        private static readonly HashSet<SceneType> _characterRequiredScenes = new HashSet<SceneType>
        {
            SceneType.GameWorld,
            SceneType.Settings,
            SceneType.Shop,
            SceneType.Inventory
        };

        /// <summary>
        /// 错误记录列表（用于统计和分析）
        /// </summary>
        private static readonly List<TransitionError> _errorHistory = new List<TransitionError>();

        /// <summary>
        /// 错误记录保留时间（小时）
        /// </summary>
        private const int ErrorRetentionHours = 24;

        /// <summary>
        /// 最大错误记录数
        /// </summary>
        private const int MaxErrorRecords = 100;

        /// <summary>
        /// 验证场景切换是否合法
        /// </summary>
        /// <param name="fromScene">源场景</param>
        /// <param name="toScene">目标场景</param>
        /// <param name="isAuthenticated">用户是否已认证</param>
        /// <param name="hasCharacter">用户是否有角色</param>
        /// <returns>验证结果</returns>
        public static ValidationResult ValidateTransition(
            SceneType fromScene, 
            SceneType toScene, 
            bool isAuthenticated = false, 
            bool hasCharacter = false)
        {
            var result = new ValidationResult { IsValid = true };

            // 1. 验证转换路径是否合法
            if (_transitionRules.TryGetValue(fromScene, out var allowedScenes))
            {
                if (!allowedScenes.Contains(toScene))
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"不允许从 {fromScene} 切换到 {toScene}";
                    return result;
                }
            }
            else
            {
                result.Warnings.Add($"未定义场景 {fromScene} 的切换规则");
            }

            // 2. 验证认证要求
            if (_authenticatedScenes.Contains(toScene) && !isAuthenticated)
            {
                result.IsValid = false;
                result.ErrorMessage = $"场景 {toScene} 需要用户认证";
                return result;
            }

            // 3. 验证角色要求
            if (_characterRequiredScenes.Contains(toScene) && !hasCharacter)
            {
                result.IsValid = false;
                result.ErrorMessage = $"场景 {toScene} 需要选择角色";
                return result;
            }

            return result;
        }

        /// <summary>
        /// 获取场景的前置条件描述
        /// </summary>
        public static string GetSceneRequirements(SceneType sceneType)
        {
            var requirements = new List<string>();

            if (_authenticatedScenes.Contains(sceneType))
            {
                requirements.Add("需要登录");
            }

            if (_characterRequiredScenes.Contains(sceneType))
            {
                requirements.Add("需要选择角色");
            }

            return requirements.Count > 0 ? string.Join(", ", requirements) : "无特殊要求";
        }

        /// <summary>
        /// 记录场景切换错误
        /// </summary>
        /// <param name="fromScene">源场景</param>
        /// <param name="toScene">目标场景</param>
        /// <param name="errorMessage">错误消息</param>
        internal static void RecordTransitionError(SceneType fromScene, SceneType toScene, string errorMessage)
        {
            lock (_errorHistory)
            {
                var error = new TransitionError
                {
                    FromScene = fromScene,
                    ToScene = toScene,
                    ErrorMessage = errorMessage,
                    Timestamp = DateTime.UtcNow
                };

                _errorHistory.Add(error);

                // 限制错误记录数量
                if (_errorHistory.Count > MaxErrorRecords)
                {
                    _errorHistory.RemoveAt(0);
                }

                Debug.LogWarning($"[SceneTransitionValidator] 记录切换错误: {fromScene} -> {toScene}: {errorMessage}");
            }
        }

        /// <summary>
        /// 清理过期的错误记录
        /// </summary>
        internal static void CleanupExpiredErrors()
        {
            lock (_errorHistory)
            {
                var cutoffTime = DateTime.UtcNow.AddHours(-ErrorRetentionHours);
                var removedCount = _errorHistory.RemoveAll(e => e.Timestamp < cutoffTime);

                if (removedCount > 0)
                {
                    Debug.Log($"[SceneTransitionValidator] 清理了 {removedCount} 条过期错误记录");
                }
            }
        }

        /// <summary>
        /// 获取错误统计信息
        /// </summary>
        /// <returns>错误统计信息</returns>
        public static Dictionary<string, int> GetErrorStatistics()
        {
            lock (_errorHistory)
            {
                var statistics = new Dictionary<string, int>();

                foreach (var error in _errorHistory)
                {
                    var key = $"{error.FromScene} -> {error.ToScene}";
                    if (statistics.ContainsKey(key))
                    {
                        statistics[key]++;
                    }
                    else
                    {
                        statistics[key] = 1;
                    }
                }

                return statistics;
            }
        }

        /// <summary>
        /// 获取最近的错误记录
        /// </summary>
        /// <param name="count">获取数量</param>
        /// <returns>错误记录列表</returns>
        public static List<string> GetRecentErrors(int count = 10)
        {
            lock (_errorHistory)
            {
                var recentErrors = _errorHistory
                    .OrderByDescending(e => e.Timestamp)
                    .Take(count)
                    .Select(e => $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}] {e.FromScene} -> {e.ToScene}: {e.ErrorMessage}")
                    .ToList();

                return recentErrors;
            }
        }

        /// <summary>
        /// 清除所有错误记录
        /// </summary>
        public static void ClearAllErrors()
        {
            lock (_errorHistory)
            {
                var count = _errorHistory.Count;
                _errorHistory.Clear();
                Debug.Log($"[SceneTransitionValidator] 清除了所有错误记录，共 {count} 条");
            }
        }
    }
}
