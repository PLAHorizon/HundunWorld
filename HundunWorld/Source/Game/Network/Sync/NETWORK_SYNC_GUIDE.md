# 网络同步框架使用指南

## 📖 概述

本文档介绍HundunWorld项目中网络同步框架的使用方法。网络同步框架由4个核心组件组成：

1. **NetworkSyncManager** - 移动同步管理器
2. **SkillSyncHandler** - 技能同步处理器
3. **NpcSyncManager** - NPC同步管理器
4. **AoiManager** - AOI兴趣区域管理器

---

## 🎯 核心概念

### 客户端预测 (Client Prediction)

客户端在发送网络请求的同时立即执行动作，无需等待服务端响应，从而消除网络延迟的感觉。

**优点**:
- ✅ 零延迟体验
- ✅ 流畅的操作手感
- ✅ 隐藏网络波动

**缺点**:
- ❌ 可能需要回滚
- ❌ 增加客户端复杂度

### 服务端校验 (Server Reconciliation)

服务端验证客户端的预测动作是否合法，如果不合法则通知客户端回滚。

**工作流程**:
```
客户端预测 → 发送请求 → 服务端验证 → 返回结果 → 修正/回滚
```

### 插值平滑 (Interpolation)

远程实体的移动不是直接跳跃到新位置，而是平滑插值过渡，看起来更自然。

**原理**:
- 延迟100ms播放动画
- 在100ms内平滑移动到目标位置
- 缓冲网络波动

---

## 🚀 快速开始

### 1. 设置玩家移动同步

```csharp
// 在玩家Actor上添加NetworkSyncManager
var syncManager = playerActor.AddScript<NetworkSyncManager>();

// 配置为本地玩家
syncManager.IsLocalPlayer = true;
syncManager.EnablePrediction = true;      // 启用客户端预测
syncManager.EnableInterpolation = false;  // 本地玩家不需要插值

// 配置同步参数
syncManager.NetworkUpdateRate = 20;       // 20Hz (每50ms发送一次)
syncManager.PositionCorrectionThreshold = 0.5f;  // 误差超过0.5m才修正
syncManager.InterpolationSpeed = 10.0f;   // 插值速度
```

### 2. 设置远程玩家同步

```csharp
// 在远程玩家Actor上添加NetworkSyncManager
var remoteSyncManager = remotePlayerActor.AddScript<NetworkSyncManager>();

// 配置为远程玩家
remoteSyncManager.IsLocalPlayer = false;
remoteSyncManager.EnablePrediction = false;  // 远程玩家不预测
remoteSyncManager.EnableInterpolation = true; // 远程玩家需要插值

// 当收到服务端位置更新时调用
remoteSyncManager.OnServerPositionUpdate(position, rotation, sequenceNumber);
```

### 3. 设置技能同步

```csharp
// 在玩家Actor上添加SkillSyncHandler
var skillSync = playerActor.AddScript<SkillSyncHandler>();

// 配置参数
skillSync.EnableClientPrediction = true;   // 启用技能预测
skillSync.SkillRollbackTimeout = 3.0f;     // 3秒后自动回滚
skillSync.EnableSkillSyncLogging = true;   // 启用日志

// 订阅事件
skillSync.SkillCastSuccess += (message) => {
    Debug.Log($"技能施放成功: {message.SkillId}");
};

skillSync.SkillCastFailed += (casterId, skillId, reason) => {
    Debug.LogWarning($"技能施放失败: {skillId}, 原因: {reason}");
};

skillSync.SkillRolledBack += (prediction) => {
    Debug.LogWarning($"技能被回滚: {prediction.SkillId}");
    // 在这里恢复技能冷却、播放取消动画等
};
```

### 4. 施放技能（客户端预测）

```csharp
// 玩家按下技能键
if (Input.GetKeyDown(KeyCode.Q))
{
    int skillId = 1001;
    ulong casterId = localPlayerId;
    List<ulong> targetIds = new List<ulong> { targetId };
    Vector3 castPosition = targetActor.Position;

    // 客户端立即预测
    var prediction = skillSync.PredictSkillCast(skillId, casterId, targetIds, castPosition);
    
    if (prediction != null)
    {
        // 立即播放技能动画和特效
        PlaySkillAnimation(skillId);
        PlaySkillEffect(skillId, castPosition);
        
        // 发送给服务端验证
        SendSkillCastToServer(skillId, targetIds, castPosition);
    }
}
```

### 5. 设置NPC同步

```csharp
// 在场景中添加NpcSyncManager
var npcSync = sceneActor.AddScript<NpcSyncManager>();

// 配置参数
npcSync.MaxVisibleNpcs = 200;               // 最多同时显示200个NPC
npcSync.EnableBandwidthOptimization = true; // 启用带宽优化
npcSync.EnableLodOptimization = true;       // 启用LOD优化

// 注册不同类型的NPC
npcSync.RegisterNpc(npcId1, NpcSyncManager.NpcSyncType.Static, npcActor1);   // 静态NPC
npcSync.RegisterNpc(npcId2, NpcSyncManager.NpcSyncType.Patrol, npcActor2);   // 巡逻NPC
npcSync.RegisterNpc(npcId3, NpcSyncManager.NpcSyncType.Combat, npcActor3);   // 战斗NPC
npcSync.RegisterNpc(npcId4, NpcSyncManager.NpcSyncType.Boss, npcActor4);     // Boss

// 设置巡逻路径（巡逻NPC专用）
var patrolPath = new List<Vector3>
{
    new Vector3(0, 0, 0),
    new Vector3(10, 0, 0),
    new Vector3(10, 0, 10),
    new Vector3(0, 0, 10)
};
npcSync.SetPatrolPath(npcId2, patrolPath);
```

### 6. 设置AOI管理

```csharp
// 在场景中添加AoiManager
var aoiManager = sceneActor.AddScript<AoiManager>();

// 配置参数
aoiManager.ViewRadius = 100f;           // 100m视野范围
aoiManager.BufferRadius = 120f;         // 120m缓冲范围
aoiManager.EnableGridOptimization = true; // 启用网格优化
aoiManager.GridSize = 50f;              // 50m网格大小

// 设置实体限制
aoiManager.MaxVisiblePlayers = 100;
aoiManager.MaxVisibleNpcs = 200;
aoiManager.MaxVisibleMonsters = 150;

// 订阅AOI事件
aoiManager.EntityEntered += (entity) => {
    Debug.Log($"实体进入视野: {entity.EntityId}");
    // 加载实体资源、显示实体
    LoadAndShowEntity(entity);
};

aoiManager.EntityExited += (entity) => {
    Debug.Log($"实体离开视野: {entity.EntityId}");
    // 卸载实体资源、隐藏实体
    HideAndUnloadEntity(entity);
};

// 注册实体
aoiManager.RegisterEntity(entityId, AoiManager.EntityType.Player, actor, position, 1.0f, 100);
```

---

## 📊 完整工作流程示例

### 场景1: 玩家移动同步

```csharp
// ============ 客户端 ============
// 1. 玩家输入
Vector3 input = GetPlayerInput(); // WASD输入

// 2. 客户端预测（立即移动）
networkSync.PredictMovement(input);

// 3. 发送给服务端
SendMovementToServer(input, currentPosition, sequenceNumber);

// ============ 服务端 ============
// 4. 服务端验证并计算权威位置
Vector3 authorityPosition = CalculateServerPosition(input);

// 5. 返回权威位置给客户端
SendPositionUpdate(playerId, authorityPosition, sequenceNumber);

// ============ 客户端 ============
// 6. 收到服务端位置
networkSync.OnServerPositionUpdate(authorityPosition, rotation, sequenceNumber);

// 7. 检查误差并修正
if (误差 > 0.5m)
{
    // 平滑修正到服务端位置
    // 重新预测后续输入
}
```

### 场景2: 技能施放同步

```csharp
// ============ 客户端 ============
// 1. 玩家按下技能键
if (Input.GetKeyDown(KeyCode.Q))
{
    // 2. 检查本地条件（冷却、内力等）
    if (!CanCastSkill(skillId)) return;

    // 3. 客户端预测（立即播放动画）
    var prediction = skillSync.PredictSkillCast(skillId, casterId, targets, position);
    PlaySkillAnimation(skillId);
    PlaySkillEffect(skillId);
    StartSkillCooldown(skillId);

    // 4. 发送给服务端
    SendSkillCastToServer(skillId, targets, position, prediction.SequenceNumber);
}

// ============ 服务端 ============
// 5. 服务端验证技能
bool valid = ValidateSkillCast(skillId, casterId, targets);

if (valid)
{
    // 6. 扣除内力、应用效果
    ApplySkillEffects(skillId, targets);
    
    // 7. 广播技能施放成功
    BroadcastSkillCastSuccess(skillId, casterId, targets);
}
else
{
    // 8. 技能验证失败
    SendSkillCastFailed(casterId, skillId, "内力不足");
}

// ============ 客户端 ============
// 9. 收到服务端响应
if (成功)
{
    // 标记预测为已验证
    skillSync.VerifyPrediction(sequenceNumber);
}
else
{
    // 回滚技能（取消动画、恢复冷却）
    skillSync.RollbackPrediction(sequenceNumber);
    CancelSkillAnimation();
    ResetSkillCooldown(skillId);
}
```

### 场景3: NPC进入视野

```csharp
// ============ AOI系统 ============
// 1. 玩家移动，触发AOI更新
aoiManager.UpdateAoi(); // 每秒自动调用

// 2. 检测到新NPC进入100m视野范围
aoiManager.EntityEntered事件触发 → OnEntityEntered(npcEntity);

// 3. 加载NPC资源
LoadNpcModel(npcEntity.EntityId);
LoadNpcTextures(npcEntity.EntityId);

// 4. 显示NPC
ShowNpc(npcEntity.EntityId, npcEntity.Position);

// ============ NPC同步系统 ============
// 5. 根据NPC类型开始同步
switch (npcEntity.Type)
{
    case NpcSyncType.Static:
        // 静态NPC不同步，只显示即可
        break;
        
    case NpcSyncType.Patrol:
        // 巡逻NPC每2秒同步一次路径点索引
        StartPatrolSync(npcEntity.EntityId, 2.0f);
        break;
        
    case NpcSyncType.Combat:
        // 战斗NPC每200ms同步位置和目标
        StartCombatSync(npcEntity.EntityId, 0.2f);
        break;
        
    case NpcSyncType.Boss:
        // Boss每100ms同步完整状态
        StartBossSync(npcEntity.EntityId, 0.1f);
        break;
}

// 6. 玩家移动超过120m，NPC离开缓冲区
aoiManager.EntityExited事件触发 → OnEntityExited(npcEntity);

// 7. 停止同步并卸载资源
StopNpcSync(npcEntity.EntityId);
HideNpc(npcEntity.EntityId);
UnloadNpcResources(npcEntity.EntityId);
```

---

## 🎨 优化技巧

### 1. 带宽优化

```csharp
// NPC分类同步 - 不同类型使用不同频率和数据量
public class BandwidthOptimization
{
    // 静态NPC：只发送初始位置（0 bytes/s）
    public void SyncStaticNpc(ulong npcId)
    {
        // 只在注册时发送一次
        SendNpcInitialPosition(npcId);
    }

    // 巡逻NPC：只发送路径点索引（5 bytes，每2秒）
    public void SyncPatrolNpc(ulong npcId, int pathIndex)
    {
        // 发送数据: { NpcId(8), PathIndex(4) } = 12 bytes
        // 频率: 2秒 = 0.5Hz
        // 带宽: 12 / 2 = 6 bytes/s ≈ 5 bytes/s
    }

    // Boss：发送完整状态（80 bytes，每100ms）
    public void SyncBoss(ulong npcId, Vector3 pos, int skillId, int phase, List<ulong> aggro)
    {
        // 发送数据:
        // - NpcId: 8 bytes
        // - Position: 12 bytes (3 floats)
        // - Rotation: 16 bytes (4 floats)
        // - SkillId: 4 bytes
        // - Phase: 4 bytes
        // - Aggro: 8 * 5 = 40 bytes (最多5个目标)
        // 总计: 84 bytes ≈ 80 bytes
        // 频率: 100ms = 10Hz
        // 带宽: 80 * 10 = 800 bytes/s
    }
}

// 总带宽估算（200个NPC的场景）：
// - 50个静态NPC: 0 bytes/s
// - 100个巡逻NPC: 100 * 5 = 500 bytes/s
// - 40个战斗NPC: 40 * 30 * 5 = 6000 bytes/s
// - 10个Boss: 10 * 800 = 8000 bytes/s
// 总计: 14.5 KB/s (非常节省)
```

### 2. 预测回滚最小化

```csharp
// 策略1: 提高网络频率，减少误差累积
syncManager.NetworkUpdateRate = 30; // 30Hz更新，更精确

// 策略2: 调整修正阈值
syncManager.PositionCorrectionThreshold = 1.0f; // 1米才修正，减少频繁修正

// 策略3: 本地验证
public bool ValidateSkillLocally(int skillId)
{
    // 在客户端预先检查条件
    if (CurrentNeiLi < skillCost) return false;
    if (IsSkillOnCooldown(skillId)) return false;
    if (!IsInRange(target, skillRange)) return false;
    
    return true; // 通过本地验证，大概率服务端也会通过
}
```

### 3. AOI网格优化

```csharp
// 优化前: O(n²) 复杂度
public void UpdateAoiNaive()
{
    foreach (var entity in allEntities) // n
    {
        foreach (var other in allEntities) // n
        {
            float distance = Vector3.Distance(entity.Position, other.Position);
            if (distance < ViewRadius)
            {
                AddToVisible(entity);
            }
        }
    }
    // 时间复杂度: O(n²) - 1000个实体需要100万次计算
}

// 优化后: O(n) 复杂度
public void UpdateAoiWithGrid()
{
    // 1. 玩家所在网格
    var playerGrid = WorldToGrid(player.Position);
    
    // 2. 只检查周围9宫格
    for (int x = -1; x <= 1; x++)
    {
        for (int z = -1; z <= 1; z++)
        {
            var grid = (playerGrid.x + x, playerGrid.z + z);
            if (gridEntities.TryGetValue(grid, out var entities))
            {
                foreach (var entity in entities) // 平均每格约 10-20 个实体
                {
                    float distance = Vector3.Distance(player.Position, entity.Position);
                    if (distance < ViewRadius)
                    {
                        AddToVisible(entity);
                    }
                }
            }
        }
    }
    // 时间复杂度: O(n) - 1000个实体只需要约100-200次计算
}
```

---

## 🐛 常见问题

### Q1: 技能施放后立即被回滚？

**原因**: 客户端预测和服务端验证不一致

**解决方案**:
```csharp
// 确保客户端和服务端使用相同的验证逻辑
public bool ValidateSkill(int skillId)
{
    // ❌ 错误: 客户端和服务端验证逻辑不同
    // 客户端: if (NeiLi >= 50)
    // 服务端: if (NeiLi > 50)  // 注意这里是 >，不是 >=
    
    // ✅ 正确: 使用相同的验证逻辑
    if (NeiLi < GetSkillCost(skillId)) return false;
    if (GetSkillCooldown(skillId) > 0) return false;
    
    return true;
}
```

### Q2: 远程玩家移动卡顿？

**原因**: 插值延迟设置不当

**解决方案**:
```csharp
// 增加插值延迟，缓冲网络波动
syncManager.InterpolationDelay = 0.15f; // 从100ms增加到150ms

// 提高插值速度
syncManager.InterpolationSpeed = 15.0f; // 从10x增加到15x

// 或者使用更高的网络更新频率
syncManager.NetworkUpdateRate = 30; // 从20Hz增加到30Hz
```

### Q3: NPC同步占用带宽过多？

**原因**: 所有NPC使用相同的高频同步

**解决方案**:
```csharp
// ❌ 错误: 所有NPC都用Boss级别同步
npcSync.RegisterNpc(npcId, NpcSyncType.Boss, actor); // 100ms，80 bytes

// ✅ 正确: 根据NPC类型选择合适的同步策略
if (npc.IsStatic)
    npcSync.RegisterNpc(npcId, NpcSyncType.Static, actor);    // 0 bytes/s
else if (npc.IsPatrolling)
    npcSync.RegisterNpc(npcId, NpcSyncType.Patrol, actor);    // 5 bytes/s
else if (npc.IsInCombat)
    npcSync.RegisterNpc(npcId, NpcSyncType.Combat, actor);    // 30 bytes/s * 5Hz = 150 bytes/s
```

### Q4: 实体频繁进出AOI？

**原因**: 视野范围和缓冲范围设置不当

**解决方案**:
```csharp
// 增加缓冲范围，防止边缘实体频繁切换
aoiManager.ViewRadius = 100f;    // 视野范围
aoiManager.BufferRadius = 130f;  // 缓冲范围增加到30m差距

// 或者降低AOI更新频率
aoiManager.UpdateInterval = 2.0f; // 从1秒增加到2秒更新一次
```

---

## 📈 性能监控

### 实时统计信息

```csharp
// 移动同步统计
var latency = networkSync.GetNetworkLatency();
var avgError = networkSync.GetAveragePredictionError();
Debug.Log($"延迟: {latency}ms, 平均误差: {avgError}m");

// 技能同步统计
var stats = skillSync.GetStatistics();
float successRate = (float)stats.SuccessfulPredictions / stats.TotalPredictions * 100f;
Debug.Log($"技能预测成功率: {successRate:F1}%");

// NPC同步统计
var npcStats = npcSync.GetStatistics();
Debug.Log($"NPC: {npcStats.VisibleNpcCount}/{npcStats.TotalNpcCount}, 带宽: {npcStats.TotalBandwidthUsage} bytes/s");

// AOI统计
var aoiStats = aoiManager.GetStatistics();
Debug.Log($"AOI实体: {aoiStats.VisibleEntityCount}/{aoiStats.TotalEntityCount}");
```

### 调试可视化

```csharp
// 启用调试可视化
networkSync.ShowDebug = true;             // 显示预测轨迹和误差
networkSync.ShowPredictionPath = true;    // 显示预测路径

npcSync.ShowDebugInfo = true;             // 显示NPC同步状态

aoiManager.ShowDebugVisualization = true; // 显示AOI范围和网格
```

---

## 🔗 相关文档

- [客户端核心功能开发文档](../../../.qoder/client-core-feature-development.md)
- [网络消息定义](../../../../../../Horizon.Game.Message/Network/)
- [开发进度报告](../../../.qoder/DEVELOPMENT_PROGRESS.md)

---

## 📝 更新日志

### v1.0.0 (2025-12-07)

- ✅ 完成NetworkSyncManager移动同步
- ✅ 完成SkillSyncHandler技能同步
- ✅ 完成NpcSyncManager NPC同步
- ✅ 完成AoiManager AOI管理
- ✅ 创建NetworkSyncIntegration集成示例

---

**作者**: HundunWorld开发团队  
**最后更新**: 2025-12-07
