using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Network.Sync
{
    /// <summary>
    /// NPC同步管理器
    /// 实现NPC的分类同步策略，优化网络带宽和性能
    /// 设计参考: client-core-feature-development.md - 8.1.4 NPC移动同步
    /// </summary>
    public class NpcSyncManager : Script
    {
        #region NPC类型定义

        /// <summary>
        /// NPC同步类型
        /// </summary>
        public enum NpcSyncType
        {
            Static,      // 静态NPC - 不同步
            Patrol,      // 巡逻NPC - 路径点同步(每2秒)
            Combat,      // 战斗NPC - 实时同步(每200ms)
            Boss,        // Boss - 高频同步(每100ms)
            Follower,    // 队友NPC - 跟随同步(每300ms)
            Flying       // 飞行NPC - 三维同步(每150ms)
        }

        #endregion

        #region NPC数据结构

        /// <summary>
        /// NPC同步数据
        /// </summary>
        public class NpcSyncData
        {
            public ulong NpcId;
            public NpcSyncType SyncType;
            public Actor NpcActor;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
            public float LastSyncTime;
            public int SyncPriority;  // 同步优先级(0-100)

            // 巡逻NPC专用
            public List<Vector3> PatrolPath = new();
            public int CurrentPatrolIndex = 0;

            // 战斗NPC专用
            public ulong CurrentTargetId = 0;
            public int CurrentSkillId = 0;

            // Boss专用
            public int PhaseIndex = 0;
            public List<ulong> AggroList = new();

            // 统计数据
            public int TotalSyncCount = 0;
            public float TotalBandwidth = 0f;
        }

        #endregion

        #region 配置参数

        [Header("同步配置")]
        [Tooltip("同屏最大NPC数量限制")]
        public int MaxVisibleNpcs = 200;

        [Tooltip("是否启用带宽优化")]
        public bool EnableBandwidthOptimization = true;

        [Tooltip("是否启用LOD优化")]
        public bool EnableLodOptimization = true;

        [Header("同步频率(秒)")]
        [Tooltip("静态NPC同步频率")]
        public float StaticNpcSyncInterval = 0f;  // 不同步

        [Tooltip("巡逻NPC同步频率")]
        public float PatrolNpcSyncInterval = 2.0f;

        [Tooltip("战斗NPC同步频率")]
        public float CombatNpcSyncInterval = 0.2f;

        [Tooltip("Boss同步频率")]
        public float BossSyncInterval = 0.1f;

        [Tooltip("队友NPC同步频率")]
        public float FollowerSyncInterval = 0.3f;

        [Tooltip("飞行NPC同步频率")]
        public float FlyingNpcSyncInterval = 0.15f;

        [Header("可见范围(米)")]
        [Tooltip("静态NPC可见范围")]
        public float StaticNpcRange = 100f;

        [Tooltip("巡逻NPC可见范围")]
        public float PatrolNpcRange = 80f;

        [Tooltip("战斗NPC可见范围")]
        public float CombatNpcRange = 100f;

        [Tooltip("Boss可见范围")]
        public float BossRange = 200f;

        [Tooltip("飞行NPC可见范围")]
        public float FlyingNpcRange = 150f;

        [Header("带宽估算(bytes/次)")]
        [Tooltip("静态NPC带宽占用")]
        public int StaticNpcBandwidth = 0;

        [Tooltip("巡逻NPC带宽占用")]
        public int PatrolNpcBandwidth = 5;

        [Tooltip("战斗NPC带宽占用")]
        public int CombatNpcBandwidth = 30;

        [Tooltip("Boss带宽占用")]
        public int BossBandwidth = 80;

        [Tooltip("队友NPC带宽占用")]
        public int FollowerNpcBandwidth = 15;

        [Tooltip("飞行NPC带宽占用")]
        public int FlyingNpcBandwidth = 40;

        [Header("调试")]
        [Tooltip("是否启用日志")]
        public bool EnableLogging = false;

        [Tooltip("是否显示调试信息")]
        public bool ShowDebugInfo = false;

        #endregion

        #region 私有字段

        // NPC字典
        private readonly Dictionary<ulong, NpcSyncData> _npcs = new();

        // 可见NPC列表(按优先级排序)
        private readonly List<NpcSyncData> _visibleNpcs = new();

        // 玩家引用(用于计算距离)
        private Actor _player;

        // 统计数据
        private int _totalNpcCount = 0;
        private int _visibleNpcCount = 0;
        private float _totalBandwidthUsage = 0f;  // bytes/s
        private int _culledNpcCount = 0;

        #endregion

        #region 初始化

        public override void OnEnable()
        {
            // 查找玩家
            _player = Scene.FindActor<Actor>("Player");

            if (_player == null)
            {
                Debug.LogWarning("[NpcSync] 未找到玩家对象");
            }

            if (EnableLogging)
            {
                Debug.Log("[NpcSync] NPC同步管理器已启动");
            }
        }

        #endregion

        #region NPC注册与管理

        /// <summary>
        /// 注册NPC
        /// </summary>
        public void RegisterNpc(ulong npcId, NpcSyncType syncType, Actor npcActor)
        {
            if (_npcs.ContainsKey(npcId))
            {
                Debug.LogWarning($"[NpcSync] NPC {npcId} 已存在，忽略重复注册");
                return;
            }

            var syncData = new NpcSyncData
            {
                NpcId = npcId,
                SyncType = syncType,
                NpcActor = npcActor,
                Position = npcActor != null ? npcActor.Position : Vector3.Zero,
                Rotation = npcActor != null ? npcActor.Orientation : Quaternion.Identity,
                LastSyncTime = Time.GameTime,
                SyncPriority = CalculateSyncPriority(syncType)
            };

            _npcs.Add(npcId, syncData);
            _totalNpcCount++;

            if (EnableLogging)
            {
                Debug.Log($"[NpcSync] 注册NPC - ID:{npcId}, 类型:{syncType}, 总数:{_totalNpcCount}");
            }
        }

        /// <summary>
        /// 注销NPC
        /// </summary>
        public void UnregisterNpc(ulong npcId)
        {
            if (_npcs.Remove(npcId))
            {
                _totalNpcCount--;
                _visibleNpcs.RemoveAll(n => n.NpcId == npcId);

                if (EnableLogging)
                {
                    Debug.Log($"[NpcSync] 注销NPC - ID:{npcId}, 剩余:{_totalNpcCount}");
                }
            }
        }

        /// <summary>
        /// 更新NPC位置
        /// </summary>
        public void UpdateNpcPosition(ulong npcId, Vector3 position, Quaternion rotation, Vector3 velocity = default)
        {
            if (_npcs.TryGetValue(npcId, out var syncData))
            {
                syncData.Position = position;
                syncData.Rotation = rotation;
                syncData.Velocity = velocity;

                // 更新Actor位置(如果存在)
                if (syncData.NpcActor != null && syncData.NpcActor.IsActiveInHierarchy)
                {
                    syncData.NpcActor.Position = position;
                    syncData.NpcActor.Orientation = rotation;
                }
            }
        }

        /// <summary>
        /// 设置NPC巡逻路径
        /// </summary>
        public void SetPatrolPath(ulong npcId, List<Vector3> patrolPath)
        {
            if (_npcs.TryGetValue(npcId, out var syncData) && syncData.SyncType == NpcSyncType.Patrol)
            {
                syncData.PatrolPath = new List<Vector3>(patrolPath);
                syncData.CurrentPatrolIndex = 0;
            }
        }

        /// <summary>
        /// 设置NPC目标
        /// </summary>
        public void SetNpcTarget(ulong npcId, ulong targetId)
        {
            if (_npcs.TryGetValue(npcId, out var syncData))
            {
                syncData.CurrentTargetId = targetId;
            }
        }

        /// <summary>
        /// 清除所有NPC
        /// </summary>
        public void ClearAllNpcs()
        {
            _npcs.Clear();
            _visibleNpcs.Clear();
            _totalNpcCount = 0;
            
            if (EnableLogging)
            {
                Debug.Log("[NpcSync] 已清除所有NPC");
            }
        }

        #endregion

        #region 更新逻辑

        public override void OnUpdate()
        {
            if (_player == null) return;

            // 更新可见NPC列表
            UpdateVisibleNpcs();

            // 执行NPC同步
            PerformNpcSync();

            // 更新统计数据
            UpdateStatistics();
        }

        /// <summary>
        /// 更新可见NPC列表
        /// </summary>
        private void UpdateVisibleNpcs()
        {
            _visibleNpcs.Clear();
            _culledNpcCount = 0;

            Vector3 playerPos = _player.Position;

            foreach (var syncData in _npcs.Values)
            {
                // 计算距离
                float distance = Vector3.Distance(playerPos, syncData.Position);

                // 获取该类型NPC的可见范围
                float range = GetVisibleRange(syncData.SyncType);

                // 距离超过可见范围，剔除
                if (distance > range)
                {
                    _culledNpcCount++;
                    continue;
                }

                // 更新优先级(距离越近优先级越高)
                syncData.SyncPriority = CalculateSyncPriority(syncData.SyncType, distance);

                _visibleNpcs.Add(syncData);
            }

            // 按优先级排序
            _visibleNpcs.Sort((a, b) => b.SyncPriority.CompareTo(a.SyncPriority));

            // 限制最大数量
            if (_visibleNpcs.Count > MaxVisibleNpcs)
            {
                _culledNpcCount += _visibleNpcs.Count - MaxVisibleNpcs;
                _visibleNpcs.RemoveRange(MaxVisibleNpcs, _visibleNpcs.Count - MaxVisibleNpcs);
            }

            _visibleNpcCount = _visibleNpcs.Count;
        }

        /// <summary>
        /// 执行NPC同步
        /// </summary>
        private void PerformNpcSync()
        {
            float currentTime = Time.GameTime;
            _totalBandwidthUsage = 0f;

            foreach (var syncData in _visibleNpcs)
            {
                float syncInterval = GetSyncInterval(syncData.SyncType);

                // 静态NPC不同步
                if (syncInterval <= 0f)
                {
                    continue;
                }

                // 检查是否到达同步时间
                if (currentTime - syncData.LastSyncTime >= syncInterval)
                {
                    SyncNpc(syncData);
                    syncData.LastSyncTime = currentTime;
                    syncData.TotalSyncCount++;

                    // 计算带宽消耗
                    int bandwidth = GetBandwidthUsage(syncData.SyncType);
                    syncData.TotalBandwidth += bandwidth;
                    _totalBandwidthUsage += bandwidth / syncInterval;  // 转换为bytes/s
                }
            }
        }

        /// <summary>
        /// 同步单个NPC
        /// </summary>
        private void SyncNpc(NpcSyncData syncData)
        {
            switch (syncData.SyncType)
            {
                case NpcSyncType.Static:
                    // 静态NPC不需要同步
                    break;

                case NpcSyncType.Patrol:
                    SyncPatrolNpc(syncData);
                    break;

                case NpcSyncType.Combat:
                    SyncCombatNpc(syncData);
                    break;

                case NpcSyncType.Boss:
                    SyncBossNpc(syncData);
                    break;

                case NpcSyncType.Follower:
                    SyncFollowerNpc(syncData);
                    break;

                case NpcSyncType.Flying:
                    SyncFlyingNpc(syncData);
                    break;
            }
        }

        /// <summary>
        /// 同步巡逻NPC
        /// </summary>
        private void SyncPatrolNpc(NpcSyncData syncData)
        {
            // 客户端根据路径点预测位置，服务端仅同步路径点索引
            if (syncData.PatrolPath.Count > 0)
            {
                // 获取下一个路径点
                int nextIndex = (syncData.CurrentPatrolIndex + 1) % syncData.PatrolPath.Count;
                Vector3 targetPos = syncData.PatrolPath[nextIndex];

                // 简单的线性插值移动
                // 修复 BUG（NaN 传播）：当 NPC 已抵达 targetPos 时，(targetPos - Position) 为零向量，
                // 零向量归一化会产生 NaN，进而污染 Velocity 与后续 Position，导致 NPC 位置变为 NaN
                // 并向 AOI/同步管线扩散。归一化前先检查长度平方，零向量时速度置零。
                Vector3 toTarget = targetPos - syncData.Position;
                Vector3 direction = toTarget.LengthSquared > 1e-8f ? toTarget.Normalized : Vector3.Zero;
                float moveSpeed = 2.0f;  // 假设巡逻速度为2m/s
                syncData.Velocity = direction * moveSpeed;

                // 到达路径点后切换到下一个
                if (Vector3.Distance(syncData.Position, targetPos) < 0.5f)
                {
                    syncData.CurrentPatrolIndex = nextIndex;
                }
            }
        }

        /// <summary>
        /// 同步战斗NPC
        /// </summary>
        private void SyncCombatNpc(NpcSyncData syncData)
        {
            // 仅同步攻击目标ID和技能ID，客户端自行播放动画
            // 实际实现需要从服务端获取数据
        }

        /// <summary>
        /// 同步Boss
        /// </summary>
        private void SyncBossNpc(NpcSyncData syncData)
        {
            // Boss需要全场景广播技能施放
            // 实际实现需要特殊处理
        }

        /// <summary>
        /// 同步队友NPC
        /// </summary>
        private void SyncFollowerNpc(NpcSyncData syncData)
        {
            // 仅同步目标位置，客户端AI跟随
            if (_player != null)
            {
                Vector3 followPos = _player.Position - _player.Transform.Forward * 3.0f;  // 跟随在玩家后方3米
                // 修复 BUG（NaN 传播）：当 NPC 已处于跟随点时，(followPos - Position) 为零向量，
                // 归一化产生 NaN 并污染 Velocity/Position。归一化前检查长度平方，零向量时速度置零。
                Vector3 toFollow = followPos - syncData.Position;
                syncData.Velocity = toFollow.LengthSquared > 1e-8f ? toFollow.Normalized * 3.0f : Vector3.Zero;
            }
        }

        /// <summary>
        /// 同步飞行NPC
        /// </summary>
        private void SyncFlyingNpc(NpcSyncData syncData)
        {
            // 同步XYZ坐标+飞行状态
            // 实际实现需要三维运动逻辑
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 计算同步优先级
        /// </summary>
        private int CalculateSyncPriority(NpcSyncType syncType, float distance = 0f)
        {
            // 基础优先级
            int basePriority = syncType switch
            {
                NpcSyncType.Boss => 100,
                NpcSyncType.Combat => 80,
                NpcSyncType.Patrol => 50,
                NpcSyncType.Follower => 90,
                NpcSyncType.Flying => 60,
                NpcSyncType.Static => 10,
                _ => 50
            };

            // 距离修正(距离越近，优先级越高)
            if (distance > 0f)
            {
                int distancePenalty = (int)(distance / 10f);  // 每10米降低1点优先级
                basePriority = Mathf.Max(0, basePriority - distancePenalty);
            }

            return basePriority;
        }

        /// <summary>
        /// 获取同步间隔
        /// </summary>
        private float GetSyncInterval(NpcSyncType syncType)
        {
            return syncType switch
            {
                NpcSyncType.Static => StaticNpcSyncInterval,
                NpcSyncType.Patrol => PatrolNpcSyncInterval,
                NpcSyncType.Combat => CombatNpcSyncInterval,
                NpcSyncType.Boss => BossSyncInterval,
                NpcSyncType.Follower => FollowerSyncInterval,
                NpcSyncType.Flying => FlyingNpcSyncInterval,
                _ => 1.0f
            };
        }

        /// <summary>
        /// 获取可见范围
        /// </summary>
        private float GetVisibleRange(NpcSyncType syncType)
        {
            return syncType switch
            {
                NpcSyncType.Static => StaticNpcRange,
                NpcSyncType.Patrol => PatrolNpcRange,
                NpcSyncType.Combat => CombatNpcRange,
                NpcSyncType.Boss => BossRange,
                NpcSyncType.Follower => float.MaxValue,  // 队友总是可见
                NpcSyncType.Flying => FlyingNpcRange,
                _ => 100f
            };
        }

        /// <summary>
        /// 获取带宽占用
        /// </summary>
        private int GetBandwidthUsage(NpcSyncType syncType)
        {
            return syncType switch
            {
                NpcSyncType.Static => StaticNpcBandwidth,
                NpcSyncType.Patrol => PatrolNpcBandwidth,
                NpcSyncType.Combat => CombatNpcBandwidth,
                NpcSyncType.Boss => BossBandwidth,
                NpcSyncType.Follower => FollowerNpcBandwidth,
                NpcSyncType.Flying => FlyingNpcBandwidth,
                _ => 10
            };
        }

        #endregion

        #region 统计与调试

        /// <summary>
        /// 更新统计数据
        /// </summary>
        private void UpdateStatistics()
        {
            // 已在其他方法中更新
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public string GetStatistics()
        {
            return $"NPC同步统计:\n" +
                   $"  总NPC数: {_totalNpcCount}\n" +
                   $"  可见NPC: {_visibleNpcCount}\n" +
                   $"  剔除NPC: {_culledNpcCount}\n" +
                   $"  总带宽: {_totalBandwidthUsage / 1024f:F2} KB/s\n" +
                   $"  平均带宽/NPC: {(_visibleNpcCount > 0 ? _totalBandwidthUsage / _visibleNpcCount : 0):F0} bytes/s";
        }

        /// <summary>
        /// 绘制调试信息
        /// </summary>
        public override void OnDebugDraw()
        {
            if (!ShowDebugInfo) return;

            // 绘制可见范围
            if (_player != null)
            {
                DebugDraw.DrawCircle(_player.Position, Vector3.Up, BossRange, Color.Red, 1.0f);
                DebugDraw.DrawCircle(_player.Position, Vector3.Up, CombatNpcRange, Color.Yellow, 1.0f);
                DebugDraw.DrawCircle(_player.Position, Vector3.Up, PatrolNpcRange, Color.Green, 1.0f);
            }

            // 绘制可见NPC
            foreach (var syncData in _visibleNpcs)
            {
                Color color = syncData.SyncType switch
                {
                    NpcSyncType.Boss => Color.Red,
                    NpcSyncType.Combat => Color.Orange,
                    NpcSyncType.Patrol => Color.Yellow,
                    NpcSyncType.Follower => Color.Blue,
                    NpcSyncType.Flying => Color.Cyan,
                    _ => Color.Gray
                };

                DebugDraw.DrawSphere(new BoundingSphere(syncData.Position, 0.5f), color, 1.0f);

                // 绘制速度方向
                if (syncData.Velocity.LengthSquared > 0.01f)
                {
                    DebugDraw.DrawLine(syncData.Position, syncData.Position + syncData.Velocity, color, 1.0f);
                }
            }
        }

        #endregion
    }
}
