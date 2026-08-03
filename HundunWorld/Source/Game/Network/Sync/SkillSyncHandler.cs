using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using HundunWorld.Game.ECS;
using HundunWorld.Game.ECS.Components;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.Sync
{
    /// <summary>
    /// 技能同步处理器
    /// 实现技能施放的网络同步，包括客户端预测和服务端验证
    /// 设计参考: client-core-feature-development.md - 8.1.3 技能施放同步
    /// </summary>
    public class SkillSyncHandler : Script  // 继承自Script而不是BaseMessageHandler
    {
        #region 配置参数

        /// <summary>
        /// 是否启用客户端技能预测
        /// </summary>
        public bool EnableClientPrediction = true;

        /// <summary>
        /// 技能回滚超时时间(秒)
        /// </summary>
        public float SkillRollbackTimeout = 3.0f;

        /// <summary>
        /// 是否记录技能同步日志
        /// </summary>
        public bool EnableSkillSyncLogging = false;

        #endregion

        #region 事件定义

        /// <summary>
        /// 技能施放成功事件
        /// </summary>
        public event Action<SkillCastMessage> SkillCastSuccess;

        /// <summary>
        /// 技能施放失败事件
        /// </summary>
        public event Action<ulong, int, string> SkillCastFailed;

        /// <summary>
        /// 技能预测事件
        /// </summary>
        public event Action<PredictedSkillCast> SkillPredicted;

        /// <summary>
        /// 技能回滚事件
        /// </summary>
        public event Action<PredictedSkillCast> SkillRolledBack;

        public event Action<PredictedSkillCast> SkillVerified;

        public event Action<ulong, int, float>? DamageApplied;

        public event Action<ulong, ulong>? EntityDied;

        #endregion

        #region 预测数据结构

        /// <summary>
        /// 预测的技能施放数据
        /// </summary>
        public class PredictedSkillCast
        {
            /// <summary>
            /// 预测序列号
            /// </summary>
            public int SequenceNumber;

            /// <summary>
            /// 技能ID
            /// </summary>
            public int SkillId;

            /// <summary>
            /// 施法者ID
            /// </summary>
            public ulong CasterId;

            /// <summary>
            /// 目标ID列表
            /// </summary>
            public List<ulong> TargetIds = new();

            /// <summary>
            /// 施法位置
            /// </summary>
            public Vector3 CastPosition;

            /// <summary>
            /// 预测时间戳
            /// </summary>
            public float Timestamp;

            /// <summary>
            /// 是否已验证
            /// </summary>
            public bool IsVerified = false;

            /// <summary>
            /// 是否已回滚
            /// </summary>
            public bool IsRolledBack = false;
        }

        /// <summary>
        /// 技能冷却同步事件
        /// </summary>
        public event Action<int, float> SkillCooldownUpdated;

        #endregion

        #region 私有字段

        // 预测队列，存储等待服务端验证的技能
        private readonly Queue<PredictedSkillCast> _predictedSkills = new();

        // 序列号计数器
        private int _sequenceCounter = 0;

        // 统计数据
        private int _totalPredictions = 0;
        private int _successfulPredictions = 0;
        private int _rolledBackPredictions = 0;

        // 技能冷却状态（技能ID -> 剩余冷却时间秒）
        private readonly Dictionary<int, float> _skillCooldowns = new();

        private NetworkEntityRegistry _entityRegistry;

        private Arch.Core.World _ecsWorld;

        #endregion

        #region 技能预测

        /// <summary>
        /// 预测技能施放(客户端立即执行)
        /// </summary>
        public PredictedSkillCast PredictSkillCast(int skillId, ulong casterId, List<ulong> targetIds, Vector3 castPosition)
        {
            if (!EnableClientPrediction)
            {
                return null;
            }

            var prediction = new PredictedSkillCast
            {
                SequenceNumber = ++_sequenceCounter,
                SkillId = skillId,
                CasterId = casterId,
                TargetIds = new List<ulong>(targetIds),
                CastPosition = castPosition,
                Timestamp = Time.GameTime
            };

            _predictedSkills.Enqueue(prediction);
            _totalPredictions++;

            if (EnableSkillSyncLogging)
            {
                Debug.Log($"[SkillSync] 预测技能施放 - Seq:{prediction.SequenceNumber}, Skill:{skillId}, Caster:{casterId}");
            }

            // 触发预测事件
            SkillPredicted?.Invoke(prediction);

            return prediction;
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 处理技能施放响应
        /// </summary>
        private async Task HandleSkillCastResponse(HorizonMessagePacket message)
        {
            if (message.Body is not SkillCastMessage castMsg)
            {
                Debug.LogError("[SkillSyncHandler] 技能施放消息体类型不匹配");
                return;
            }

            // 验证预测
            if (EnableClientPrediction && _predictedSkills.Count > 0)
            {
                VerifyPrediction(castMsg);
            }

            // 触发成功事件
            SkillCastSuccess?.Invoke(castMsg);

            if (EnableSkillSyncLogging)
            {
                Debug.Log($"[SkillSync] 服务端确认技能施放 - Skill:{castMsg.SkillId}, Caster:{castMsg.CasterId}, Targets:{castMsg.TargetIds.Count}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 处理技能冷却更新
        /// </summary>
        private async Task HandleSkillCooldownUpdate(HorizonMessagePacket message)
        {
            if (message.Body is SkillCooldownUpdateMessage cooldownMsg)
            {
                // 将服务端冷却时间（毫秒）转换为秒
                float cooldownSeconds = cooldownMsg.CooldownTime / 1000f;
                _skillCooldowns[cooldownMsg.SkillId] = cooldownSeconds;

                // 通知订阅者冷却更新
                SkillCooldownUpdated?.Invoke(cooldownMsg.SkillId, cooldownSeconds);

                if (EnableSkillSyncLogging)
                {
                    Debug.Log($"[SkillSync] 技能冷却同步 - Skill:{cooldownMsg.SkillId}, CD:{cooldownSeconds:F1}s");
                }
            }
            else if (message.Body is SkillCooldownQueryResponse cooldownResponse)
            {
                // 批量同步所有技能冷却
                foreach (var kvp in cooldownResponse.SkillCooldowns)
                {
                    float cooldownSeconds = kvp.Value / 1000f;
                    _skillCooldowns[kvp.Key] = cooldownSeconds;
                    SkillCooldownUpdated?.Invoke(kvp.Key, cooldownSeconds);
                }

                if (EnableSkillSyncLogging)
                {
                    Debug.Log($"[SkillSync] 批量冷却同步完成，共{cooldownResponse.SkillCooldowns.Count}个技能");
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 检查技能是否在冷却中
        /// </summary>
        public bool IsSkillOnCooldown(int skillId)
        {
            return _skillCooldowns.TryGetValue(skillId, out var cd) && cd > 0;
        }

        /// <summary>
        /// 获取技能剩余冷却时间
        /// </summary>
        public float GetSkillCooldown(int skillId)
        {
            return _skillCooldowns.TryGetValue(skillId, out var cd) ? cd : 0f;
        }

        #endregion

        #region 预测验证

        /// <summary>
        /// 验证预测结果
        /// </summary>
        private void VerifyPrediction(SkillCastMessage serverCast)
        {
            // 查找匹配的预测
            PredictedSkillCast matchedPrediction = null;

            foreach (var prediction in _predictedSkills)
            {
                // 改进项 2：原匹配条件仅检查 (SkillId, CasterId)，未排除已验证/已回滚的预测。
                // 当同一施法者连续施放同一技能时，首次验证后预测仍留在队列中（由 OnUpdate 清理），
                // 第二次服务端响应会重复匹配到同一已验证预测，导致 IsVerified 重复置位、
                // _successfulPredictions 虚高、成功率统计失真。
                // 增加 !IsVerified && !IsRolledBack 守门，只匹配待验证预测，
                // 与 HandleSyncSkillCast 的匹配逻辑保持一致。
                if (prediction.SkillId == serverCast.SkillId && 
                    prediction.CasterId == serverCast.CasterId &&
                    !prediction.IsVerified && !prediction.IsRolledBack)
                {
                    matchedPrediction = prediction;
                    break;
                }
            }

            if (matchedPrediction != null)
            {
                matchedPrediction.IsVerified = true;
                _successfulPredictions++;

                if (EnableSkillSyncLogging)
                {
                    Debug.Log($"[SkillSync] 预测验证成功 - Seq:{matchedPrediction.SequenceNumber}, 成功率:{GetPredictionSuccessRate():F1}%");
                }
            }
        }

        /// <summary>
        /// 回滚失败的预测
        /// </summary>
        public void RollbackPrediction(PredictedSkillCast prediction, string reason = "服务端拒绝")
        {
            if (prediction.IsRolledBack)
            {
                return;
            }

            prediction.IsRolledBack = true;
            _rolledBackPredictions++;

            if (EnableSkillSyncLogging)
            {
                Debug.LogWarning($"[SkillSync] 技能回滚 - Seq:{prediction.SequenceNumber}, Skill:{prediction.SkillId}, 原因:{reason}");
            }

            // 触发回滚事件
            SkillRolledBack?.Invoke(prediction);

            // 触发失败事件
            SkillCastFailed?.Invoke(prediction.CasterId, prediction.SkillId, reason);
        }

        #endregion

        #region ECS集成

        public void SetEntityRegistry(NetworkEntityRegistry registry)
        {
            _entityRegistry = registry;
        }

        public void SetEcsWorld(Arch.Core.World world)
        {
            _ecsWorld = world;
        }

        #endregion

        #region SyncEvent处理

        private readonly ConcurrentQueue<EventPacket> _pendingEvents = new();

        public void EnqueueEventPacket(EventPacket packet)
        {
            _pendingEvents.Enqueue(packet);
        }

        public void ProcessPendingSyncEvents()
        {
            while (_pendingEvents.TryDequeue(out var eventPacket))
            {
                foreach (var syncEvent in eventPacket.Events)
                {
                    if (syncEvent.Kind == SyncEventKind.SkillCast ||
                        syncEvent.Kind == SyncEventKind.Damage ||
                        syncEvent.Kind == SyncEventKind.Death)
                    {
                        OnSyncEventReceived(syncEvent);
                    }
                }
            }
        }

        private void OnSyncEventReceived(SyncEvent syncEvent)
        {
            switch (syncEvent.Kind)
            {
                case SyncEventKind.SkillCast:
                    HandleSyncSkillCast(syncEvent);
                    break;
                case SyncEventKind.Damage:
                    HandleSyncDamage(syncEvent);
                    break;
                case SyncEventKind.Death:
                    HandleSyncDeath(syncEvent);
                    break;
            }
        }

        private void HandleSyncSkillCast(SyncEvent syncEvent)
        {
            PredictedSkillCast matchedPrediction = null;

            foreach (var prediction in _predictedSkills)
            {
                if (prediction.SkillId == syncEvent.IntValue &&
                    prediction.CasterId == syncEvent.SourceEntityId &&
                    !prediction.IsVerified && !prediction.IsRolledBack)
                {
                    matchedPrediction = prediction;
                    break;
                }
            }

            if (matchedPrediction != null)
            {
                matchedPrediction.IsVerified = true;
                _successfulPredictions++;
                SkillVerified?.Invoke(matchedPrediction);

                if (EnableSkillSyncLogging)
                {
                    Debug.Log($"[SkillSync] SyncEvent预测验证成功 - Seq:{matchedPrediction.SequenceNumber}, Skill:{matchedPrediction.SkillId}");
                }
            }
            else
            {
                var serverPrediction = new PredictedSkillCast
                {
                    SequenceNumber = 0,
                    SkillId = syncEvent.IntValue,
                    CasterId = syncEvent.SourceEntityId,
                    TargetIds = new List<ulong> { syncEvent.TargetEntityId },
                    Timestamp = Time.GameTime,
                    IsVerified = true,
                };
                SkillVerified?.Invoke(serverPrediction);

                if (EnableSkillSyncLogging)
                {
                    Debug.Log($"[SkillSync] SyncEvent服务端发起技能 - Skill:{syncEvent.IntValue}, Caster:{syncEvent.SourceEntityId}");
                }
            }
        }

        private void HandleSyncDamage(SyncEvent syncEvent)
        {
            DamageApplied?.Invoke(syncEvent.TargetEntityId, syncEvent.IntValue, syncEvent.FloatValue);

            if (_entityRegistry != null && _entityRegistry.TryGetEntity(syncEvent.TargetEntityId, out var entity))
            {
                if (_ecsWorld != null && _ecsWorld.Has<HealthComponent>(entity))
                {
                    ref var health = ref _ecsWorld.Get<HealthComponent>(entity);
                    health.CurrentHealth -= syncEvent.IntValue;
                    if (health.CurrentHealth < 0)
                        health.CurrentHealth = 0;
                    _ecsWorld.Set(entity, health);
                }
            }

            if (EnableSkillSyncLogging)
            {
                Debug.Log($"[SkillSync] SyncEvent伤害 - Target:{syncEvent.TargetEntityId}, Damage:{syncEvent.IntValue}, Crit:{syncEvent.FloatValue}");
            }
        }

        private void HandleSyncDeath(SyncEvent syncEvent)
        {
            EntityDied?.Invoke(syncEvent.TargetEntityId, syncEvent.SourceEntityId);

            if (_entityRegistry != null && _entityRegistry.TryGetEntity(syncEvent.TargetEntityId, out var entity))
            {
                if (_ecsWorld != null && _ecsWorld.Has<HealthComponent>(entity))
                {
                    ref var health = ref _ecsWorld.Get<HealthComponent>(entity);
                    health.CurrentHealth = 0;
                    _ecsWorld.Set(entity, health);
                }
            }

            if (EnableSkillSyncLogging)
            {
                Debug.Log($"[SkillSync] SyncEvent死亡 - Target:{syncEvent.TargetEntityId}, Killer:{syncEvent.SourceEntityId}");
            }
        }

        #endregion

        #region 更新与清理

        /// <summary>
        /// 更新技能同步状态(每帧调用)
        /// </summary>
        public override void OnUpdate()
        {
            ProcessPendingSyncEvents();

            float deltaTime = Time.DeltaTime;

            // 更新技能冷却计时器
            var expiredCooldowns = new List<int>();
            foreach (var kvp in _skillCooldowns)
            {
                _skillCooldowns[kvp.Key] = kvp.Value - deltaTime;
                if (_skillCooldowns[kvp.Key] <= 0)
                {
                    expiredCooldowns.Add(kvp.Key);
                }
            }
            foreach (var skillId in expiredCooldowns)
            {
                _skillCooldowns.Remove(skillId);
            }

            // 清理过期的预测
            float currentTime = Time.GameTime;

            while (_predictedSkills.Count > 0)
            {
                var prediction = _predictedSkills.Peek();

                // 已验证或已回滚，可以移除
                if (prediction.IsVerified || prediction.IsRolledBack)
                {
                    _predictedSkills.Dequeue();
                    continue;
                }

                // 超时未验证，执行回滚
                if (currentTime - prediction.Timestamp > SkillRollbackTimeout)
                {
                    RollbackPrediction(prediction, "服务端响应超时");
                    _predictedSkills.Dequeue();
                    continue;
                }

                // 队首元素未超时，后面的也不会超时
                break;
            }
        }

        /// <summary>
        /// 清理所有预测数据
        /// </summary>
        public void ClearPredictions()
        {
            _predictedSkills.Clear();
            _sequenceCounter = 0;

            if (EnableSkillSyncLogging)
            {
                Debug.Log("[SkillSync] 已清空所有预测数据");
            }
        }

        #endregion

        #region 统计与调试

        /// <summary>
        /// 获取预测成功率
        /// </summary>
        public float GetPredictionSuccessRate()
        {
            if (_totalPredictions == 0) return 100f;
            return (_successfulPredictions / (float)_totalPredictions) * 100f;
        }

        /// <summary>
        /// 获取预测回滚率
        /// </summary>
        public float GetPredictionRollbackRate()
        {
            if (_totalPredictions == 0) return 0f;
            return (_rolledBackPredictions / (float)_totalPredictions) * 100f;
        }

        /// <summary>
        /// 获取待验证预测数量
        /// </summary>
        public int GetPendingPredictionCount()
        {
            return _predictedSkills.Count;
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public string GetStatistics()
        {
            return $"技能同步统计:\n" +
                   $"  总预测次数: {_totalPredictions}\n" +
                   $"  成功预测: {_successfulPredictions}\n" +
                   $"  回滚次数: {_rolledBackPredictions}\n" +
                   $"  成功率: {GetPredictionSuccessRate():F1}%\n" +
                   $"  回滚率: {GetPredictionRollbackRate():F1}%\n" +
                   $"  待验证: {GetPendingPredictionCount()}";
        }

        /// <summary>
        /// 重置统计数据
        /// </summary>
        public void ResetStatistics()
        {
            _totalPredictions = 0;
            _successfulPredictions = 0;
            _rolledBackPredictions = 0;

            if (EnableSkillSyncLogging)
            {
                Debug.Log("[SkillSync] 统计数据已重置");
            }
        }

        #endregion

        #region 调试可视化

        /// <summary>
        /// 绘制调试信息
        /// </summary>
        public override void OnDebugDraw()
        {
            if (!EnableSkillSyncLogging) return;

            var text = GetStatistics();
            DebugDraw.DrawText(text, new Vector2(10, 200), Color.Yellow, 12);

            // 绘制待验证技能列表
            float yOffset = 300f;
            foreach (var prediction in _predictedSkills)
            {
                float age = Time.GameTime - prediction.Timestamp;
                Color color = age > SkillRollbackTimeout * 0.8f ? Color.Red : Color.Green;

                var info = $"Seq:{prediction.SequenceNumber} Skill:{prediction.SkillId} Age:{age:F1}s";
                DebugDraw.DrawText(info, new Vector2(10, yOffset), color, 10);
                yOffset += 15f;
            }
        }

        #endregion
    }
}