# 角色位置 Redis 永久存储实现方案

## Context（背景）

**问题**：当前角色位置存储存在三个问题，导致服务器重启后角色位置无法正确恢复：

1. **5 分钟过期检查**：`CharacterGrain.GetLastPositionAsync` 有 `PositionStaleMinutes = 5.0` 过期阈值（[CharacterGrain.cs#L1341-L1376](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Orleans.Grains/CharacterGrain.cs#L1341-L1376)），超过 5 分钟返回 null，回退到握手坐标。服务器重启后角色若久未上线，位置已"过期"。
2. **全量持久化开销大**：`UpdateLastPositionAsync` 调用 `WriteStateAsync()` 持久化整个 `CharacterState`（含 `CharacterInfo` 大对象），每秒一次，开销大且与业务数据写入争抢锁。
3. **依赖 GrainStorage 而非独立 Redis Key**：用户明确要求"从 Redis 加载相关信息"，应参照 `ICharacterPresenceStore` 双轨制架构，使用独立 Redis Key 存储。

**目标**：参照 [ICharacterPresenceStore](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.Core/Sim/Server/ICharacterPresenceStore.cs) 的双轨制架构模式，新建独立的 `ICharacterPositionStore` Redis 存储接口，实现位置数据永久存储（无 TTL），服务器重启激活 Grain 时从 Redis 加载位置。

## 架构设计

```
┌─────────────────────────────────────────────────────────────┐
│                    CharacterGrain                           │
│  ┌────────────────────────────────────────────────────┐    │
│  │  CharacterState (Orleans GrainStorage - Redis)     │    │
│  │  ├── CharacterInfo (大对象，低频写入)              │    │
│  │  └── LastPosition* (内存缓存，不再 WriteState)     │    │
│  └────────────────────────────────────────────────────┘    │
│                          ↕                                  │
│  ┌────────────────────────────────────────────────────┐    │
│  │  ICharacterPositionStore (独立 Redis Key，永久)    │    │
│  │  Key: character:position:{characterId}             │    │
│  │  Value: Hash { x, y, z, yaw, updatedAt }           │    │
│  │  TTL: 无（永久存储）                                │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
            ↑                              ↑
   OnActivateAsync 读取             UpdateLastPositionAsync 写入
   （覆盖内存缓存）                  （仅写 Redis，不 WriteState）
```

## 实施步骤

### 步骤 1：新建接口 ICharacterPositionStore

**文件**：`Horizon.Game.Core\Sim\Server\ICharacterPositionStore.cs`（新建）

定义三个方法：
- `SavePositionAsync(long characterId, float x, float y, float z, float yaw)` → `Task<bool>`
- `GetPositionAsync(long characterId)` → `Task<(float X, float Y, float Z, float Yaw)?>`
- `ClearPositionAsync(long characterId)` → `Task<bool>`（预留，用于未来角色删除）

坐标系约定：存储 ECS Z-up 坐标（X=左右, Y=前后, Z=上下），由调用方（ZoneShardGrain）做 Flax Y-up ↔ ECS Z-up 转换。

### 步骤 2：新建实现 RedisCharacterPositionStore

**文件**：`Horizon.Strategy.Storage.Redis\RedisCharacterPositionStore.cs`（新建）

**参照模板**：[RedisCharacterPresenceStore.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Strategy.Storage.Redis/RedisCharacterPresenceStore.cs)

设计要点：
- Key：`character:position:{characterId}` → Hash { x, y, z, yaw, updatedAt }
- **无 TTL**（永久存储）
- float 字段使用 `"R"`（Round-Trip）格式化字符串存储，保证精度无损
- `updatedAt` 存储 `DateTime.UtcNow.Ticks`（long），用于诊断
- 降级策略：所有 Redis 操作 try-catch，失败返回 false/null，不抛异常
- 构造函数注入 `RedisConnection`（单例）和 `ILogger<RedisCharacterPositionStore>?`

### 步骤 3：DI 注册

**文件**：[Horizon.Orleans.Silo\Program.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Orleans.Silo/Program.cs)

在 `ConfigureApplicationServices` 方法中，紧跟 `ICharacterPresenceStore` 注册之后（L608 之后）插入：

```csharp
// ===== 角色位置 Redis 永久存储（双轨制架构） =====
services.AddSingleton<Horizon.Game.Core.Sim.Server.ICharacterPositionStore>(provider =>
{
    var redisConnection = provider.GetRequiredService<RedisConnection>();
    var logger = provider.GetService<ILogger<RedisCharacterPositionStore>>();
    return new RedisCharacterPositionStore(redisConnection, logger);
});
```

### 步骤 4：修改 CharacterGrain

**文件**：[Horizon.Orleans.Grains\CharacterGrain.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Orleans.Grains/CharacterGrain.cs)

#### 4.1 新增字段和构造函数注入
- 字段区新增：`private readonly ICharacterPositionStore _positionStore;`
- 构造函数（L59-L77）新增参数：`ICharacterPositionStore positionStore`

#### 4.2 OnActivateAsync 加载位置（L124 之前，base.OnActivateAsync 之前）
从 Redis 加载位置到 `CharacterState.LastPosition*`（作为内存缓存）。Redis 不可用时降级到 GrainState 中的旧值，加载失败不影响 Grain 激活。

#### 4.3 UpdateLastPositionAsync 改造（L1344-L1357）
- 先更新内存缓存（`CharacterState.LastPosition*`）
- 再写入 Redis（`_positionStore.SavePositionAsync`）
- **不再调用 `WriteStateAsync()`** 持久化整个 CharacterState

#### 4.4 GetLastPositionAsync 改造（L1359-L1376）
- 优先从 Redis 读取（权威源）
- Redis 不可用或无数据时降级到内存缓存（`CharacterState.LastPosition*`）
- **移除 5 分钟过期检查**（位置永久存储）
- 方法从同步 `Task<...>` 改为 `async Task<...>`（调用方已使用 await，无需修改）

#### 4.5 删除 PositionStaleMinutes 常量（L1341-L1342）

## 关键设计决策

| 决策 | 理由 |
|------|------|
| 保留 CharacterState.LastPosition* 字段 | 作为内存缓存和 Redis 不可用时的降级兜底；零破坏性迁移，MemoryPack 序列化兼容 |
| 永久存储不设 TTL | 用户需求明确要求"永久存储"；与 presence（TTL 90 秒）形成职责分离 |
| 不再调用 WriteStateAsync | CharacterState 含 CharacterInfo 大对象，每秒一次 WriteState 性能开销大 |
| 坐标系保持 ECS Z-up | ZoneShardGrain 调用链不变，Store 层不感知坐标系 |
| 移除 5 分钟过期检查 | 位置永久存储，挂机角色不应被错误回退到握手坐标 |
| 使用 Hash 存储 | 与 RedisCharacterPresenceStore 风格一致；支持部分字段更新；HashGetAllAsync 单次往返 |

## 不修改的文件（调用链不变）

- [ICharacterGrain.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Orleans.Interface/ICharacterGrain.cs) — 接口签名不变
- [ZoneShardGrain.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Orleans.Grains/World/ZoneShardGrain.cs) — 调用 `UpdateLastPositionAsync`/`GetLastPositionAsync` 不变
- [GrainStateModels.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Game.Message/Network/GrainStateModels.cs) — `CharacterState.LastPosition*` 字段定义不变
- [RedisCharacterPresenceStore.cs](file:///c:/Works/GitHubProjects/HundunWorld/Horizon.Strategy.Storage.Redis/RedisCharacterPresenceStore.cs) — 参照模板，不修改

## 验证方案

### 测试 1：基本读写正确性
1. 启动 Silo，角色进入世界移动
2. redis-cli 执行 `HGETALL character:position:{characterId}`
3. 预期：返回 x, y, z, yaw, updatedAt 五个字段，值与角色位置一致

### 测试 2：服务器重启后位置恢复
1. 角色移动到坐标 (100, 200, 300)
2. 等待 2 秒（确保 TickAsync 写入 Redis）
3. 重启 Silo
4. 角色重新连接
5. 预期：角色出现在 (100, 200, 300) 而非握手初始坐标；日志显示"从 CharacterGrain 恢复位置"

### 测试 3：Redis 不可用降级
1. 角色正常进入世界，移动到某位置
2. 停止 Redis 服务
3. 预期：`UpdateLastPositionAsync` 不抛异常（内存缓存持续更新）；日志显示降级告警
4. 重启 Silo（Redis 仍不可用）
5. 预期：`GetLastPositionAsync` 降级到内存缓存，角色出现在最后一次持久化的位置

### 测试 4：WriteStateAsync 调用频率下降
1. 角色移动 60 秒
2. 预期：`WriteStateAsync` 调用次数为 0（位置变更不再触发）；仅 HP、装备等变更会触发

## 实施顺序

1. 新建 `ICharacterPositionStore.cs` 接口
2. 新建 `RedisCharacterPositionStore.cs` 实现
3. 在 `Silo Program.cs` 注册 DI
4. 修改 `CharacterGrain.cs`（注入、OnActivateAsync、UpdateLastPositionAsync、GetLastPositionAsync、删除常量）
5. 编译验证

## 关键文件清单

| 文件 | 操作 |
|------|------|
| `Horizon.Game.Core\Sim\Server\ICharacterPositionStore.cs` | 新建 |
| `Horizon.Strategy.Storage.Redis\RedisCharacterPositionStore.cs` | 新建 |
| `Horizon.Orleans.Silo\Program.cs` | 修改（DI 注册） |
| `Horizon.Orleans.Grains\CharacterGrain.cs` | 修改（4 处改动） |
