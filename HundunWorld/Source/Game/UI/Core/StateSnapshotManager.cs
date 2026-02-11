using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using HundunWorld.Game.UI.Events;
using HundunWorld.Game.UI.States;
using HundunWorld.Game.UI.Enums;
using Horizon.Game.Message.Enums;

namespace HundunWorld.Game.UI.Core
{
    /// <summary>
    /// 状态快照管理器
    /// 负责创建、存储、管理和恢复UI状态快照
    /// 支持自动快照、手动快照和错误恢复机制
    /// </summary>
    public class StateSnapshotManager
    {
        private readonly Dictionary<string, StateSnapshot> _snapshots = new Dictionary<string, StateSnapshot>();
        private readonly Queue<string> _snapshotOrder = new Queue<string>();
        private readonly UnifiedStateManager _stateManager;
        private readonly UIEventBus _eventBus;

        // 配置参数
        public int MaxSnapshotCount { get; set; } = 50;
        public int MaxCriticalSnapshotCount { get; set; } = 10;
        public bool EnableAutomaticSnapshots { get; set; } = true;
        public TimeSpan AutoSnapshotInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool LogSnapshotOperations { get; set; } = true;

        // 自动快照相关
        private DateTime _lastAutoSnapshot = DateTime.MinValue;
        private readonly HashSet<SceneType> _autoSnapshotTriggerScenes = new HashSet<SceneType>
        {
            SceneType.Login,
            SceneType.CharacterSelection,
            SceneType.GameWorld
        };

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="stateManager">状态管理器</param>
        public StateSnapshotManager(UnifiedStateManager stateManager)
        {
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _eventBus = UIEventBus.Instance;

            // 订阅状态变更事件，用于自动快照
            SubscribeToEvents();
        }

        #region 快照创建

        /// <summary>
        /// 创建手动快照
        /// </summary>
        /// <param name="name">快照名称</param>
        /// <param name="description">快照描述</param>
        /// <param name="tags">快照标签</param>
        /// <returns>快照ID</returns>
        public string CreateManualSnapshot(string name, string description = "", List<string> tags = null)
        {
            var snapshot = CreateSnapshot(name, SnapshotType.Manual, description);
            
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    snapshot.AddTag(tag);
                }
            }

            return StoreSnapshot(snapshot);
        }

        /// <summary>
        /// 创建自动快照
        /// </summary>
        /// <param name="trigger">触发原因</param>
        /// <returns>快照ID</returns>
        public string CreateAutomaticSnapshot(string trigger = "")
        {
            if (!EnableAutomaticSnapshots) return null;

            var name = $"Auto_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var description = string.IsNullOrEmpty(trigger) ? "自动快照" : $"自动快照: {trigger}";
            
            var snapshot = CreateSnapshot(name, SnapshotType.Automatic, description);
            snapshot.AddTag("auto");
            
            if (!string.IsNullOrEmpty(trigger))
            {
                snapshot.AddTag(trigger);
            }

            _lastAutoSnapshot = DateTime.UtcNow;
            return StoreSnapshot(snapshot);
        }

        /// <summary>
        /// 创建错误恢复快照
        /// </summary>
        /// <param name="errorInfo">错误信息</param>
        /// <returns>快照ID</returns>
        public string CreateErrorRecoverySnapshot(string errorInfo = "")
        {
            var name = $"ErrorRecovery_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var description = string.IsNullOrEmpty(errorInfo) ? "错误恢复快照" : $"错误恢复快照: {errorInfo}";
            
            var snapshot = CreateSnapshot(name, SnapshotType.ErrorRecovery, description);
            snapshot.AddTag("error_recovery");
            snapshot.IsCritical = true;
            
            if (!string.IsNullOrEmpty(errorInfo))
            {
                snapshot.SetMetadata("error_info", errorInfo);
            }

            return StoreSnapshot(snapshot);
        }

        /// <summary>
        /// 创建场景切换快照
        /// </summary>
        /// <param name="fromScene">源场景</param>
        /// <param name="toScene">目标场景</param>
        /// <returns>快照ID</returns>
        public string CreateSceneTransitionSnapshot(SceneType fromScene, SceneType toScene)
        {
            var name = $"Transition_{fromScene}_{toScene}_{DateTime.UtcNow:HHmmss}";
            var description = $"场景切换快照: {fromScene} -> {toScene}";
            
            var snapshot = CreateSnapshot(name, SnapshotType.SceneTransition, description);
            snapshot.AddTag("scene_transition");
            snapshot.AddTag(fromScene.ToString().ToLower());
            snapshot.AddTag(toScene.ToString().ToLower());
            snapshot.SetMetadata("from_scene", fromScene);
            snapshot.SetMetadata("to_scene", toScene);

            return StoreSnapshot(snapshot);
        }

        /// <summary>
        /// 创建关键操作快照
        /// </summary>
        /// <param name="operationName">操作名称</param>
        /// <param name="operationData">操作数据</param>
        /// <returns>快照ID</returns>
        public string CreateCriticalOperationSnapshot(string operationName, Dictionary<string, object> operationData = null)
        {
            var name = $"Critical_{operationName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            var description = $"关键操作快照: {operationName}";
            
            var snapshot = CreateSnapshot(name, SnapshotType.CriticalOperation, description);
            snapshot.AddTag("critical_operation");
            snapshot.AddTag(operationName.ToLower().Replace(" ", "_"));
            snapshot.IsCritical = true;
            snapshot.SetMetadata("operation_name", operationName);
            
            if (operationData != null)
            {
                foreach (var kvp in operationData)
                {
                    snapshot.SetMetadata(kvp.Key, kvp.Value);
                }
            }

            return StoreSnapshot(snapshot);
        }

        #endregion

        #region 快照恢复

        /// <summary>
        /// 恢复指定快照
        /// </summary>
        /// <param name="snapshotId">快照ID</param>
        /// <returns>是否成功恢复</returns>
        public bool RestoreSnapshot(string snapshotId)
        {
            if (!_snapshots.TryGetValue(snapshotId, out var snapshot))
            {
                FlaxEngine.Debug.LogError($"未找到快照: {snapshotId}");
                return false;
            }

            return RestoreSnapshot(snapshot);
        }

        /// <summary>
        /// 恢复快照
        /// </summary>
        /// <param name="snapshot">快照对象</param>
        /// <returns>是否成功恢复</returns>
        public bool RestoreSnapshot(StateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                FlaxEngine.Debug.LogError("快照对象为空");
                return false;
            }

            try
            {
                // 验证快照完整性
                if (!snapshot.ValidateIntegrity())
                {
                    FlaxEngine.Debug.LogError($"快照完整性验证失败: {snapshot.SnapshotId}");
                    return false;
                }

                // 在恢复前创建当前状态快照
                CreateErrorRecoverySnapshot($"恢复前备份 - 恢复快照: {snapshot.Name}");

                // 恢复状态（这里需要与UnifiedStateManager配合实现）
                // 由于状态管理器的实现细节，这里暂时记录日志
                if (LogSnapshotOperations)
                {
                    FlaxEngine.Debug.Log($"恢复快照: {snapshot.Name} ({snapshot.SnapshotId})");
                    FlaxEngine.Debug.Log($"快照信息: {snapshot.GetSummary()}");
                }

                // 发布快照恢复事件
                _eventBus.Publish(new SnapshotRestoredEvent(snapshot, true));

                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"恢复快照失败: {ex.Message}");
                _eventBus.Publish(new SnapshotRestoredEvent(snapshot, false));
                return false;
            }
        }

        /// <summary>
        /// 恢复最近的快照
        /// </summary>
        /// <param name="snapshotType">快照类型过滤</param>
        /// <returns>是否成功恢复</returns>
        public bool RestoreLatestSnapshot(SnapshotType? snapshotType = null)
        {
            var snapshots = GetSnapshots(snapshotType, maxCount: 1);
            if (snapshots.Count == 0)
            {
                FlaxEngine.Debug.LogWarning("没有找到可恢复的快照");
                return false;
            }

            return RestoreSnapshot(snapshots.First());
        }

        /// <summary>
        /// 智能恢复 - 根据当前错误情况选择最佳快照
        /// </summary>
        /// <param name="errorContext">错误上下文</param>
        /// <returns>是否成功恢复</returns>
        public bool SmartRestore(string errorContext = "")
        {
            // 优先恢复错误恢复快照
            var errorRecoverySnapshots = GetSnapshots(SnapshotType.ErrorRecovery, maxCount: 3);
            foreach (var snapshot in errorRecoverySnapshots)
            {
                if (RestoreSnapshot(snapshot))
                {
                    FlaxEngine.Debug.Log($"智能恢复成功 - 使用错误恢复快照: {snapshot.Name}");
                    return true;
                }
            }

            // 其次尝试关键操作快照
            var criticalSnapshots = GetSnapshots(SnapshotType.CriticalOperation, maxCount: 3);
            foreach (var snapshot in criticalSnapshots)
            {
                if (RestoreSnapshot(snapshot))
                {
                    FlaxEngine.Debug.Log($"智能恢复成功 - 使用关键操作快照: {snapshot.Name}");
                    return true;
                }
            }

            // 最后尝试手动快照
            var manualSnapshots = GetSnapshots(SnapshotType.Manual, maxCount: 3);
            foreach (var snapshot in manualSnapshots)
            {
                if (RestoreSnapshot(snapshot))
                {
                    FlaxEngine.Debug.Log($"智能恢复成功 - 使用手动快照: {snapshot.Name}");
                    return true;
                }
            }

            FlaxEngine.Debug.LogError("智能恢复失败 - 没有可用的快照");
            return false;
        }

        #endregion

        #region 快照管理

        /// <summary>
        /// 获取快照列表
        /// </summary>
        /// <param name="snapshotType">快照类型过滤</param>
        /// <param name="tags">标签过滤</param>
        /// <param name="maxCount">最大数量</param>
        /// <returns>快照列表</returns>
        public List<StateSnapshot> GetSnapshots(SnapshotType? snapshotType = null, List<string> tags = null, int maxCount = 0)
        {
            var query = _snapshots.Values.ToList();

            // 按类型过滤
            if (snapshotType.HasValue)
            {
                query = query.Where(s => s.Type == snapshotType.Value).ToList();
            }

            // 按标签过滤
            if (tags != null && tags.Count > 0)
            {
                query = query.Where(s => tags.Any(tag => s.HasTag(tag))).ToList();
            }

            // 按时间排序（最新的在前）
            query = query.OrderByDescending(s => s.CreatedTime).ToList();

            // 限制数量
            if (maxCount > 0)
            {
                query = query.Take(maxCount).ToList();
            }

            return query;
        }

        /// <summary>
        /// 获取快照信息
        /// </summary>
        /// <param name="snapshotId">快照ID</param>
        /// <returns>快照信息</returns>
        public StateSnapshot GetSnapshot(string snapshotId)
        {
            return _snapshots.TryGetValue(snapshotId, out var snapshot) ? snapshot : null;
        }

        /// <summary>
        /// 删除快照
        /// </summary>
        /// <param name="snapshotId">快照ID</param>
        /// <returns>是否成功删除</returns>
        public bool DeleteSnapshot(string snapshotId)
        {
            if (_snapshots.TryGetValue(snapshotId, out var snapshot))
            {
                if (snapshot.IsCritical)
                {
                    FlaxEngine.Debug.LogWarning($"无法删除关键快照: {snapshotId}");
                    return false;
                }

                _snapshots.Remove(snapshotId);
                
                if (LogSnapshotOperations)
                {
                    FlaxEngine.Debug.Log($"删除快照: {snapshot.Name} ({snapshotId})");
                }
                
                return true;
            }

            return false;
        }

        /// <summary>
        /// 清理过期快照
        /// </summary>
        /// <returns>清理的快照数量</returns>
        public int CleanupExpiredSnapshots()
        {
            var expiredSnapshots = _snapshots.Values
                .Where(s => !s.IsCritical && s.IsExpired())
                .ToList();

            int cleanedCount = 0;
            foreach (var snapshot in expiredSnapshots)
            {
                if (DeleteSnapshot(snapshot.SnapshotId))
                {
                    cleanedCount++;
                }
            }

            if (LogSnapshotOperations && cleanedCount > 0)
            {
                FlaxEngine.Debug.Log($"清理过期快照: {cleanedCount} 个");
            }

            return cleanedCount;
        }

        /// <summary>
        /// 清理多余快照（保持快照数量在限制内）
        /// </summary>
        /// <returns>清理的快照数量</returns>
        public int CleanupExcessSnapshots()
        {
            var allSnapshots = _snapshots.Values.OrderByDescending(s => s.CreatedTime).ToList();
            var criticalSnapshots = allSnapshots.Where(s => s.IsCritical).ToList();
            var normalSnapshots = allSnapshots.Where(s => !s.IsCritical).ToList();

            int cleanedCount = 0;

            // 清理普通快照
            if (normalSnapshots.Count > MaxSnapshotCount)
            {
                var excessNormal = normalSnapshots.Skip(MaxSnapshotCount);
                foreach (var snapshot in excessNormal)
                {
                    if (DeleteSnapshot(snapshot.SnapshotId))
                    {
                        cleanedCount++;
                    }
                }
            }

            // 清理关键快照
            if (criticalSnapshots.Count > MaxCriticalSnapshotCount)
            {
                var excessCritical = criticalSnapshots.Skip(MaxCriticalSnapshotCount);
                foreach (var snapshot in excessCritical)
                {
                    _snapshots.Remove(snapshot.SnapshotId);
                    cleanedCount++;
                    
                    if (LogSnapshotOperations)
                    {
                        FlaxEngine.Debug.Log($"清理多余关键快照: {snapshot.Name} ({snapshot.SnapshotId})");
                    }
                }
            }

            return cleanedCount;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 创建快照核心实现
        /// </summary>
        /// <param name="name">快照名称</param>
        /// <param name="type">快照类型</param>
        /// <param name="description">快照描述</param>
        /// <returns>快照对象</returns>
        private StateSnapshot CreateSnapshot(string name, SnapshotType type, string description)
        {
            var snapshot = new StateSnapshot(name, type)
            {
                Description = description,
                UIState = _stateManager.GetCurrentState(),
                SceneStates = _stateManager.GetAllSceneStates(),
                TransitionState = _stateManager.GetCurrentTransition()
            };

            // 计算大小和生成校验和
            snapshot.CalculateEstimatedSize();
            snapshot.GenerateChecksum();

            // 设置过期时间（自动快照1天后过期）
            if (type == SnapshotType.Automatic)
            {
                snapshot.SetExpirationRelative(TimeSpan.FromDays(1));
            }

            return snapshot;
        }

        /// <summary>
        /// 存储快照
        /// </summary>
        /// <param name="snapshot">快照对象</param>
        /// <returns>快照ID</returns>
        private string StoreSnapshot(StateSnapshot snapshot)
        {
            _snapshots[snapshot.SnapshotId] = snapshot;
            _snapshotOrder.Enqueue(snapshot.SnapshotId);

            // 发布快照创建事件
            _eventBus.Publish(new SnapshotCreatedEvent(snapshot));

            if (LogSnapshotOperations)
            {
                FlaxEngine.Debug.Log($"创建快照: {snapshot.Name} ({snapshot.SnapshotId})");
            }

            // 清理多余快照
            CleanupExcessSnapshots();

            return snapshot.SnapshotId;
        }

        /// <summary>
        /// 订阅状态变更事件
        /// </summary>
        private void SubscribeToEvents()
        {
            // 监听场景切换完成事件，创建自动快照
            _eventBus.Subscribe<SceneTransitionCompletedEvent>(OnSceneTransitionCompleted, 
                subscriberName: "StateSnapshotManager");

            // 监听错误事件，创建错误恢复快照
            _eventBus.Subscribe<ErrorOccurredEvent>(OnErrorOccurred, 
                subscriberName: "StateSnapshotManager");
        }

        /// <summary>
        /// 场景切换完成事件处理
        /// </summary>
        /// <param name="eventData">事件数据</param>
        private void OnSceneTransitionCompleted(SceneTransitionCompletedEvent eventData)
        {
            if (eventData.IsSuccess && _autoSnapshotTriggerScenes.Contains(eventData.ToScene))
            {
                CreateSceneTransitionSnapshot(eventData.FromScene, eventData.ToScene);
            }
        }

        /// <summary>
        /// 错误事件处理
        /// </summary>
        /// <param name="eventData">事件数据</param>
        private void OnErrorOccurred(ErrorOccurredEvent eventData)
        {
            if (eventData.Severity == ErrorSeverity.Error || eventData.Severity == ErrorSeverity.Critical)
            {
                CreateErrorRecoverySnapshot(eventData.ErrorMessage);
            }
        }

        /// <summary>
        /// 释放资源，取消所有事件订阅
        /// </summary>
        public void Dispose()
        {
            _eventBus?.UnsubscribeAll("StateSnapshotManager");
            _snapshots.Clear();
            _snapshotOrder.Clear();
        }

        #endregion
    }
}