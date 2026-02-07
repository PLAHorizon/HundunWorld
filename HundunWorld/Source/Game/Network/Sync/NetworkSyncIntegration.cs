using FlaxEngine;
using FlaxEngine.Utilities;
using Game.Network;
using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Network.Sync
{
    /// <summary>
    /// 网络同步集成示例
    /// 展示如何整合使用NetworkSyncManager、SkillSyncHandler、NpcSyncManager、AoiManager
    /// 设计参考: client-core-feature-development.md - 8. 网络同步框架
    /// </summary>
    public class NetworkSyncIntegration : Script
    {
        #region 组件引用

        [Header("网络同步组件")]
        [Tooltip("移动同步管理器")]
        public NetworkSyncManager MovementSync;

        [Tooltip("技能同步处理器")]
        public SkillSyncHandler SkillSync;

        [Tooltip("NPC同步管理器")]
        public NpcSyncManager NpcSync;

        [Tooltip("AOI管理器")]
        public AoiManager AoiManager;

        [Header("测试配置")]
        [Tooltip("是否启用自动测试")]
        public bool EnableAutoTest = false;

        [Tooltip("测试间隔(秒)")]
        public float TestInterval = 5.0f;

        [Tooltip("是否显示同步状态")]
        public bool ShowSyncStatus = true;

        #endregion

        #region 私有字段

        private float _testTimer = 0f;
        private Actor _playerActor;
        private List<Actor> _testNpcs = new();
        private int _testSkillCastCount = 0;
        private int _testNpcCount = 0;

        #endregion

        #region 初始化

        public override void OnEnable()
        {
            // 查找或创建组件
            SetupComponents();

            // 订阅事件
            SubscribeEvents();

            Debug.Log("[NetworkSync Integration] 网络同步集成已初始化");
        }

        /// <summary>
        /// 设置组件引用
        /// </summary>
        private void SetupComponents()
        {
            // 查找玩家
            _playerActor = Scene.FindActor<Actor>("Player");
            if (_playerActor == null)
            {
                Debug.LogWarning("[NetworkSync Integration] 未找到玩家对象");
                return;
            }

            // 修复: 通过服务定位器或依赖注入获取组件实例，而不是直接添加脚本
            // 查找已存在的NetworkSyncManager组件
            if (MovementSync == null)
            {
                MovementSync = _playerActor.GetScript<NetworkSyncManager>();
                if (MovementSync == null)
                {
                    // 如果找不到现有的组件，则添加新的组件
                    MovementSync = _playerActor.AddScript<NetworkSyncManager>();
                    MovementSync.IsLocalPlayer = true;
                    MovementSync.EnablePrediction = true;
                    MovementSync.EnableInterpolation = false; // 本地玩家不需要插值
                    Debug.Log("[NetworkSync Integration] 已添加 NetworkSyncManager");
                }
            }

            // 查找已存在的SkillSyncHandler组件
            if (SkillSync == null)
            {
                SkillSync = _playerActor.GetScript<SkillSyncHandler>();
                if (SkillSync == null)
                {
                    // 如果找不到现有的组件，则添加新的组件
                    SkillSync = _playerActor.AddScript<SkillSyncHandler>();
                    SkillSync.EnableClientPrediction = true;
                    Debug.Log("[NetworkSync Integration] 已添加 SkillSyncHandler");
                }
            }

            // 查找已存在的NpcSyncManager组件
            if (NpcSync == null)
            {
                NpcSync = Actor.GetScript<NpcSyncManager>();
                if (NpcSync == null)
                {
                    // 如果找不到现有的组件，则添加新的组件
                    NpcSync = Actor.AddScript<NpcSyncManager>();
                    Debug.Log("[NetworkSync Integration] 已添加 NpcSyncManager");
                }
            }

            // 查找已存在的AoiManager组件
            if (AoiManager == null)
            {
                AoiManager = Actor.GetScript<AoiManager>();
                if (AoiManager == null)
                {
                    // 如果找不到现有的组件，则添加新的组件
                    AoiManager = Actor.AddScript<AoiManager>();
                    Debug.Log("[NetworkSync Integration] 已添加 AoiManager");
                }
            }
        }

        /// <summary>
        /// 订阅组件事件
        /// </summary>
        private void SubscribeEvents()
        {
            // 技能同步事件
            if (SkillSync != null)
            {
                SkillSync.SkillCastSuccess += OnSkillCastSuccess;
                SkillSync.SkillCastFailed += OnSkillCastFailed;
                SkillSync.SkillPredicted += OnSkillPredicted;
                SkillSync.SkillRolledBack += OnSkillRolledBack;
            }

            // AOI事件
            if (AoiManager != null)
            {
                AoiManager.EntityEntered += OnEntityEntered;
                AoiManager.EntityExited += OnEntityExited;
                AoiManager.AoiUpdated += OnAoiUpdated;
            }
        }

        #endregion

        #region 更新循环

        public override void OnUpdate()
        {
            // 自动测试
            if (EnableAutoTest)
            {
                _testTimer += Time.DeltaTime;
                if (_testTimer >= TestInterval)
                {
                    RunAutoTest();
                    _testTimer = 0f;
                }
            }

            // 显示同步状态
            if (ShowSyncStatus)
            {
                DrawSyncStatus();
            }
        }

        #endregion

        #region 自动测试

        /// <summary>
        /// 运行自动测试
        /// </summary>
        private void RunAutoTest()
        {
            Debug.Log($"[NetworkSync Integration] 自动测试运行 #{_testSkillCastCount + 1}");

            // 测试1: 技能预测
            TestSkillPrediction();

            // 测试2: NPC注册
            TestNpcRegistration();

            // 测试3: AOI实体管理
            TestAoiEntityManagement();

            // 测试4: 网络延迟模拟
            TestNetworkLatency();
        }

        /// <summary>
        /// 测试技能预测
        /// </summary>
        private void TestSkillPrediction()
        {
            if (SkillSync == null || _playerActor == null) return;

            int skillId = 1001 + (_testSkillCastCount % 5); // 循环测试5个技能
            ulong casterId = 10001;
            List<ulong> targetIds = new List<ulong> { 20001, 20002 };
            Vector3 castPosition = _playerActor.Position + new Vector3(5, 0, 5);

            var prediction = SkillSync.PredictSkillCast(skillId, casterId, targetIds, castPosition);
            
            if (prediction != null)
            {
                Debug.Log($"[Test] 技能预测成功 - Skill:{skillId}, Seq:{prediction.SequenceNumber}");
                _testSkillCastCount++;

                // 模拟服务端响应(2秒后)
                // 修复: 使用FlaxEngine的定时器替代Invoke方法
                Scripting.InvokeOnUpdate(()  => SimulateServerResponse());
            }
        }

        /// <summary>
        /// 模拟服务端响应
        /// </summary>
        private void SimulateServerResponse()
        {
            // 90%成功率模拟
            // 修复: 使用FlaxEngine的随机数替代UnityEngine.Random
            bool success = RandomUtil.Random.NextFloat(0f, 1f) > 0.1f;
            
            if (success)
            {
                Debug.Log("[Test] 模拟服务端验证成功");
                // 这里应该调用SkillSync的验证成功方法
            }
            else
            {
                Debug.Log("[Test] 模拟服务端验证失败，触发回滚");
                // 这里应该调用SkillSync的回滚方法
            }
        }

        /// <summary>
        /// 测试NPC注册
        /// </summary>
        private void TestNpcRegistration()
        {
            if (NpcSync == null) return;

            // 创建测试NPC
            ulong npcId = (ulong)(30001 + _testNpcCount);
            var npcType = (NpcSyncManager.NpcSyncType)(_testNpcCount % 6);
            
            Debug.Log($"[Test] 注册NPC - ID:{npcId}, Type:{npcType}");
            
            // 实际项目中应该传入真实的Actor
            NpcSync.RegisterNpc(npcId, npcType, null);
            _testNpcCount++;
        }

        /// <summary>
        /// 测试AOI实体管理
        /// </summary>
        private void TestAoiEntityManagement()
        {
            if (AoiManager == null || _playerActor == null) return;

            // 创建测试实体
            ulong entityId = (ulong)(40001 + _testNpcCount);
            var entityType = AoiManager.EntityType.Npc;
            var position = _playerActor.Position + new Vector3(
                RandomUtil.Random.NextFloat(-50, 50),
                0,
                RandomUtil.Random.NextFloat(-50, 50)
            );

            Debug.Log($"[Test] 注册AOI实体 - ID:{entityId}, Pos:{position}");
            
            // 实际项目中应该传入真实的Actor
            AoiManager.RegisterEntity(entityId, entityType, null);
        }

        /// <summary>
        /// 测试网络延迟
        /// </summary>
        private void TestNetworkLatency()
        {
            if (MovementSync == null) return;

            float latency = MovementSync.GetNetworkLatency();
            float avgError = MovementSync.GetAveragePredictionError();

            Debug.Log($"[Test] 网络状态 - 延迟:{latency:F0}ms, 平均预测误差:{avgError:F3}m");
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 技能施放成功
        /// </summary>
        private void OnSkillCastSuccess(SkillCastMessage message)
        {
            Debug.Log($"[NetworkSync] 技能施放成功 - Skill:{message.SkillId}, Caster:{message.CasterId}");
        }

        /// <summary>
        /// 技能施放失败
        /// </summary>
        private void OnSkillCastFailed(ulong casterId, int skillId, string reason)
        {
            Debug.LogWarning($"[NetworkSync] 技能施放失败 - Skill:{skillId}, Caster:{casterId}, Reason:{reason}");
        }

        /// <summary>
        /// 技能被预测
        /// </summary>
        private void OnSkillPredicted(SkillSyncHandler.PredictedSkillCast prediction)
        {
            Debug.Log($"[NetworkSync] 技能预测 - Seq:{prediction.SequenceNumber}, Skill:{prediction.SkillId}");
        }

        /// <summary>
        /// 技能预测回滚
        /// </summary>
        private void OnSkillRolledBack(SkillSyncHandler.PredictedSkillCast prediction)
        {
            Debug.LogWarning($"[NetworkSync] 技能回滚 - Seq:{prediction.SequenceNumber}, Skill:{prediction.SkillId}");
        }

        /// <summary>
        /// 实体进入AOI
        /// </summary>
        private void OnEntityEntered(AoiManager.AoiEntity entity)
        {
            Debug.Log($"[NetworkSync] 实体进入AOI - ID:{entity.EntityId}, Type:{entity.Type}");
        }

        /// <summary>
        /// 实体离开AOI
        /// </summary>
        private void OnEntityExited(AoiManager.AoiEntity entity)
        {
            Debug.Log($"[NetworkSync] 实体离开AOI - ID:{entity.EntityId}, Type:{entity.Type}");
        }

        /// <summary>
        /// AOI更新
        /// </summary>
        private void OnAoiUpdated(int enteredCount, int exitedCount)
        {
            if (enteredCount > 0 || exitedCount > 0)
            {
                Debug.Log($"[NetworkSync] AOI更新 - 进入:{enteredCount}, 离开:{exitedCount}");
            }
        }

        #endregion

        #region 调试可视化

        /// <summary>
        /// 绘制同步状态
        /// </summary>
        private void DrawSyncStatus()
        {
            Vector3 debugPos = new Vector3(10, 100, 0);
            Color titleColor = Color.Cyan;
            Color valueColor = Color.White;

            // 标题
            DebugDraw.DrawText("=== 网络同步状态 ===", debugPos, titleColor);
            debugPos.Y += 25;

            // 移动同步
            if (MovementSync != null)
            {
                DebugDraw.DrawText("【移动同步】", debugPos, titleColor);
                debugPos.Y += 20;

                float latency = MovementSync.GetNetworkLatency();
                float avgError = MovementSync.GetAveragePredictionError();

                DebugDraw.DrawText($"  延迟: {latency:F0}ms", debugPos, 
                    latency > 100 ? Color.Red : Color.Green);
                debugPos.Y += 18;

                DebugDraw.DrawText($"  预测误差: {avgError:F3}m", debugPos,
                    avgError > 0.5f ? Color.Yellow : Color.Green);
                debugPos.Y += 20;
            }

            // 技能同步
            if (SkillSync != null)
            {
                DebugDraw.DrawText("【技能同步】", debugPos, titleColor);
                debugPos.Y += 20;

                string stats = SkillSync.GetStatistics();
                DebugDraw.DrawText(stats, debugPos, valueColor);
                debugPos.Y += 100; // 统计信息可能是多行
            }

            // NPC同步
            if (NpcSync != null)
            {
                DebugDraw.DrawText("【NPC同步】", debugPos, titleColor);
                debugPos.Y += 20;

                string npcStats = NpcSync.GetStatistics();
                DebugDraw.DrawText(npcStats, debugPos, valueColor);
                debugPos.Y += 100;
            }

            // AOI
            if (AoiManager != null)
            {
                DebugDraw.DrawText("【AOI管理】", debugPos, titleColor);
                debugPos.Y += 20;

                string aoiStats = AoiManager.GetStatistics();
                DebugDraw.DrawText(aoiStats, debugPos, valueColor);
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 手动测试技能预测
        /// </summary>
        public void ManualTestSkillCast(int skillId)
        {
            if (SkillSync == null || _playerActor == null)
            {
                Debug.LogWarning("[NetworkSync Integration] 组件未初始化");
                return;
            }

            ulong casterId = 10001;
            List<ulong> targetIds = new List<ulong> { 20001 };
            Vector3 castPosition = _playerActor.Position + _playerActor.Transform.Forward * 5f;

            var prediction = SkillSync.PredictSkillCast(skillId, casterId, targetIds, castPosition);
            
            if (prediction != null)
            {
                Debug.Log($"[NetworkSync Integration] 手动施放技能 - ID:{skillId}, Seq:{prediction.SequenceNumber}");
            }
        }

        /// <summary>
        /// 重置所有统计数据
        /// </summary>
        public void ResetAllStatistics()
        {
            if (MovementSync != null)
            {
                MovementSync.ResetSync();
            }

            if (SkillSync != null)
            {
                SkillSync.ResetStatistics();
            }

            if (NpcSync != null)
            {
                NpcSync.ClearAllNpcs();
            }

            if (AoiManager != null)
            {
                AoiManager.ClearAllEntities();
            }

            _testSkillCastCount = 0;
            _testNpcCount = 0;

            Debug.Log("[NetworkSync Integration] 已重置所有统计数据");
        }

        #endregion

        #region 清理

        public override void OnDisable()
        {
            // 取消订阅事件
            if (SkillSync != null)
            {
                SkillSync.SkillCastSuccess -= OnSkillCastSuccess;
                SkillSync.SkillCastFailed -= OnSkillCastFailed;
                SkillSync.SkillPredicted -= OnSkillPredicted;
                SkillSync.SkillRolledBack -= OnSkillRolledBack;
            }

            if (AoiManager != null)
            {
                AoiManager.EntityEntered -= OnEntityEntered;
                AoiManager.EntityExited -= OnEntityExited;
                AoiManager.AoiUpdated -= OnAoiUpdated;
            }

            Debug.Log("[NetworkSync Integration] 网络同步集成已清理");
        }

        #endregion
    }
}
