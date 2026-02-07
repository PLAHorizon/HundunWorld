using System;
using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 控制状态管理器接口
    /// 负责管理角色的各种控制状态（眩晕、沉默、定身等）
    /// </summary>
    public interface IControlStateManager
    {
        /// <summary>
        /// 应用控制状态
        /// </summary>
        void ApplyControlState(ulong characterId, ControlState state, float duration);
        
        /// <summary>
        /// 移除控制状态
        /// </summary>
        void RemoveControlState(ulong characterId, ControlState state);
        
        /// <summary>
        /// 检查是否具有指定控制状态
        /// </summary>
        bool HasControlState(ulong characterId, ControlState state);
        
        /// <summary>
        /// 获取所有当前控制状态
        /// </summary>
        List<ActiveControlState> GetActiveControlStates(ulong characterId);
        
        /// <summary>
        /// 清除所有控制状态
        /// </summary>
        void ClearAllControlStates(ulong characterId);
        
        /// <summary>
        /// 订阅控制状态变化事件
        /// </summary>
        void SubscribeControlStateChanged(ulong characterId, Action<ControlState, bool> callback);
        
        /// <summary>
        /// 取消订阅控制状态变化事件
        /// </summary>
        void UnsubscribeControlStateChanged(ulong characterId, Action<ControlState, bool> callback);
    }

    /// <summary>
    /// 控制状态管理器实现
    /// </summary>
    public class ControlStateManager : IControlStateManager
    {
        private static ControlStateManager _instance;
        public static ControlStateManager Instance => _instance ??= new ControlStateManager();

        private readonly Dictionary<ulong, List<ActiveControlState>> _activeControlStates;
        private readonly Dictionary<ulong, List<Action<ControlState, bool>>> _controlStateChangeCallbacks;

        private ControlStateManager()
        {
            _activeControlStates = new Dictionary<ulong, List<ActiveControlState>>();
            _controlStateChangeCallbacks = new Dictionary<ulong, List<Action<ControlState, bool>>>();
        }

        public void ApplyControlState(ulong characterId, ControlState state, float duration)
        {
            if (!_activeControlStates.ContainsKey(characterId))
            {
                _activeControlStates[characterId] = new List<ActiveControlState>();
            }

            // 检查是否已有相同状态
            var existingState = _activeControlStates[characterId].Find(s => s.State == state);
            if (existingState != null)
            {
                // 刷新持续时间
                existingState.RemainingTime = Math.Max(existingState.RemainingTime, duration);
                Debug.Log($"[ControlStateManager] 刷新控制状态: {state} (持续时间: {duration}s)");
            }
            else
            {
                // 添加新状态
                var newState = new ActiveControlState
                {
                    State = state,
                    RemainingTime = duration
                };
                
                _activeControlStates[characterId].Add(newState);
                TriggerControlStateChangeEvent(characterId, state, true);
                Debug.Log($"[ControlStateManager] 应用控制状态: {state} (持续时间: {duration}s)");
            }
        }

        public void RemoveControlState(ulong characterId, ControlState state)
        {
            if (!_activeControlStates.ContainsKey(characterId))
                return;

            var states = _activeControlStates[characterId];
            var stateToRemove = states.Find(s => s.State == state);
            
            if (stateToRemove != null)
            {
                states.Remove(stateToRemove);
                TriggerControlStateChangeEvent(characterId, state, false);
                Debug.Log($"[ControlStateManager] 移除控制状态: {state}");
            }
        }

        public bool HasControlState(ulong characterId, ControlState state)
        {
            if (!_activeControlStates.ContainsKey(characterId))
                return false;

            return _activeControlStates[characterId].Exists(s => s.State == state);
        }

        public List<ActiveControlState> GetActiveControlStates(ulong characterId)
        {
            if (!_activeControlStates.ContainsKey(characterId))
                return new List<ActiveControlState>();

            return new List<ActiveControlState>(_activeControlStates[characterId]);
        }

        public void ClearAllControlStates(ulong characterId)
        {
            if (!_activeControlStates.ContainsKey(characterId))
                return;

            var states = _activeControlStates[characterId];
            var statesToRemove = new List<ControlState>();

            foreach (var state in states)
            {
                statesToRemove.Add(state.State);
            }

            foreach (var state in statesToRemove)
            {
                RemoveControlState(characterId, state);
            }

            Debug.Log($"[ControlStateManager] 清除角色 {characterId} 的所有控制状态");
        }

        public void SubscribeControlStateChanged(ulong characterId, Action<ControlState, bool> callback)
        {
            if (!_controlStateChangeCallbacks.ContainsKey(characterId))
            {
                _controlStateChangeCallbacks[characterId] = new List<Action<ControlState, bool>>();
            }
            
            _controlStateChangeCallbacks[characterId].Add(callback);
        }

        public void UnsubscribeControlStateChanged(ulong characterId, Action<ControlState, bool> callback)
        {
            if (_controlStateChangeCallbacks.ContainsKey(characterId))
            {
                _controlStateChangeCallbacks[characterId].Remove(callback);
            }
        }

        /// <summary>
        /// 更新控制状态（每帧调用）
        /// </summary>
        public void Update(float deltaTime)
        {
            var charactersToRemove = new List<ulong>();

            foreach (var kvp in _activeControlStates)
            {
                var characterId = kvp.Key;
                var states = kvp.Value;
                var statesToRemove = new List<ControlState>();

                foreach (var state in states)
                {
                    state.RemainingTime -= deltaTime;
                    if (state.RemainingTime <= 0)
                    {
                        statesToRemove.Add(state.State);
                    }
                }

                // 移除过期的状态
                foreach (var state in statesToRemove)
                {
                    RemoveControlState(characterId, state);
                }

                // 如果该角色没有控制状态了，标记为待清理
                if (states.Count == 0)
                {
                    charactersToRemove.Add(characterId);
                }
            }

            // 清理没有控制状态的角色
            foreach (var characterId in charactersToRemove)
            {
                _activeControlStates.Remove(characterId);
            }
        }

        /// <summary>
        /// 触发控制状态变化事件
        /// </summary>
        private void TriggerControlStateChangeEvent(ulong characterId, ControlState state, bool isApplied)
        {
            if (_controlStateChangeCallbacks.ContainsKey(characterId))
            {
                foreach (var callback in _controlStateChangeCallbacks[characterId])
                {
                    try
                    {
                        callback?.Invoke(state, isApplied);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ControlStateManager] 控制状态变化回调执行异常: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 活跃的控制状态
    /// </summary>
    public class ActiveControlState
    {
        public ControlState State { get; set; }
        public float RemainingTime { get; set; }
    }

    /// <summary>
    /// 控制状态枚举
    /// </summary>
    public enum ControlState
    {
        Stunned,    // 眩晕 - 无法移动和攻击
        Silenced,   // 沉默 - 无法使用技能
        Rooted,     // 定身 - 无法移动
        Feared,     // 恐惧 - 强制移动（远离施法者）
        Confused,   // 混乱 - 随机行动
        Disarmed,   // 缴械 - 无法普通攻击
        Blinded,    // 致盲 - 攻击命中率降低
        Taunted,    // 嘲讽 - 强制攻击特定目标
        Sleep,      // 睡眠 - 无法行动，受到伤害会醒来
        Frozen      // 冰冻 - 完全无法行动
    }
}