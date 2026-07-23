# Orleans Redis 迁移 + 角色在线状态持久化 + 心跳保活

## Context（背景）

### 当前问题

1. 角色离线持久化 BUG  ：服务器刚启动时角色下线后无法从持久化中清理。根因是 CharacterGrain 使用 \[PersistentState("character", "GameStore")]（SQL Server via AdoNet）持久化 IsOnline 字段，grain 未激活时 IsOnline=true 永久残留。
2. 存储架构问题  ：Orleans 使用 SQL Server 存储所有 grain 状态，写入慢且可能失败，导致 GoOfflineAsync 的 WriteStateAsync 不可靠。
3. 缺乏应用层心跳  ：只有 TCP 层 KeepAlive 和空闲超时（60 秒），没有角色级别的应用层心跳保活。

### 目标

1. 将 Orleans 所有存储（Clustering + ReminderService + 7 个 GrainStorage）从 SQL Server 切换到 Redis，，，保留 SQL Server 配置作为备份，，（Redis 故障时可回退）
2. 角色在线状态（IsOnline）使用独立 Redis Key + TTL 自动过期，不再依赖 Orleans GrainStorage 持久化
3. 加入客户端角色心跳保活检查（应用层心跳），断线后即刻启动清理并广播 Despawn 到所有在线玩家
4. 修复服务器刚启动时残留 IsOnline=true 无法清理的问题（Redis TTL 自动过期）

***

## 架构设计

### 1. Orleans 存储切换到 Redis（保留 SQL Server 作为备份）

NuGet 包变更

：

* 添加 Microsoft.Orleans.Clustering.Redis 10.0.1（Silo、Game.Gateway、IM.Gateway、Game.Core、WebApi）

* 添加 Microsoft.Orleans.Reminders.Redis 10.0.1（Silo）

* Microsoft.Orleans.Persistence.Redis 10.0.1 已安装

* 保留  原 AdoNet 包引用（备份方案）

配置变更

（Horizon.Orleans.Silo\Program.cs）：

* UseAdoNetClustering → UseRedisClustering

* UseAdoNetReminderService → UseRedisReminderService

* 所有 AddAdoNetGrainStorage → AddRedisGrainStorage（7 个存储：PubSubStore、默认、GameStore、PassportStore、WorldSqlStore、FlowerStore、AIStore）

* 保留  原 SQL Server 配置代码（注释掉），作为降级备份方案

客户端项目变更

：

* Horizon.Game.Core、Horizon.Game.Gateway、Horizon.IM.Gateway、Horizon.WebApi 的 UseAdoNetClustering → UseRedisClustering

连接字符串

：

* password=DB65F7F9C\@127.0.0.1:9379,abortConnect=false,syncTimeout=5000,asyncTimeout=10000

* 从 appsettings.json 的 Redis:ConnectionString 读取

### 2. 角色在线状态 Redis 持久化（双轨制）

新增接口 ICharacterPresenceStore

（位置：Horizon.Game.Core\Sim\Server\ICharacterPresenceStore.cs）：

方法签名：

* SetOnlineAsync(long characterId, string gatewayId, string connectionId) → Task<bool>

* SetOfflineAsync(long characterId) → Task<bool>

* RefreshHeartbeatAsync(long characterId) → Task<bool>

* IsOnlineAsync(long characterId) → Task<bool>

* BatchIsOnlineAsync(IReadOnlyList<long> characterIds) → Task\<Dictionary\<long, bool>>

* GetAllOnlineCharacterIdsAsync() → Task\<IReadOnlyList<long>>

* GetExpiredCharactersAsync(TimeSpan heartbeatTimeout) → Task\<IReadOnlyList<(long, DateTime)>>

Redis 实现 RedisCharacterPresenceStore

（位置：Horizon.Strategy.Storage.Redis\RedisCharacterPresenceStore.cs）：

* Key: character:presence:{characterId} → Hash { gatewayId, connectionId, lastHeartbeat }

* TTL = 90 秒（心跳间隔 30 秒 × 3 倍容错）

* SetOnlineAsync: HSET + EXPIRE

* SetOfflineAsync: DEL

* RefreshHeartbeatAsync: HSET lastHeartbeat + EXPIRE

* IsOnlineAsync: EXISTS

* GetAllOnlineCharacterIdsAsync: SCAN character:presence:\*

* GetExpiredCharactersAsync: 扫描所有 presence key，检查 lastHeartbeat

降级策略

：

* Redis 不可用时，降级到 ConnectionManager 内存状态（GetConnectionByCharacterId != null && IsConnected）

* 所有 Redis 操作 try-catch，失败时记录日志但不抛异常

### 3. 心跳保活机制

客户端心跳

：

* 复用现有 HeartbeatMessage（Horizon.Game.Message\Network\SystemMessages.cs）

* 客户端每 30 秒发送一次 HeartbeatMessage

* 服务器收到后调用 ICharacterPresenceStore.RefreshHeartbeatAsync

服务器心跳检查后台服务

&#x20;CharacterPresenceMonitorHostedService（位置：Horizon.Game.Gateway\Services\CharacterPresenceMonitorHostedService.cs）：

* 继承 BackgroundService

* 每 10 秒扫描一次 Redis 中过期的 presence

* 超时阈值：90 秒（3 倍心跳间隔）

* 发现过期角色 → 调用 PlayerDespawnScheduler.DespawnImmediatelyAsync

心跳处理 Handler

&#x20;HeartbeatHandler（位置：Horizon.Game.Gateway\Handlers\HeartbeatHandler.cs）：

* 处理 HeartbeatMessage

* 调用 ICharacterPresenceStore.RefreshHeartbeatAsync

* 回复 HeartbeatResponse

### 4. 修改 CharacterGrain

* CharacterState 中保留 IsOnline 字段但不再持久化（改用 grain 内存状态）

* OnActivateAsync：不再需要防御性重置 IsOnline（Redis TTL 自动过期）

* EnterGameAsync：设置内存 IsOnline=true + 调用 ICharacterPresenceStore.SetOnlineAsync

* GoOfflineAsync：设置内存 IsOnline=false + 调用 ICharacterPresenceStore.SetOfflineAsync

* IsOnlineAsync：优先查询 ICharacterPresenceStore.IsOnlineAsync，降级到内存状态

* CharacterGrain 注入 ICharacterPresenceStore（通过 grain 构造函数）

### 5. 修改 PlayerDespawnScheduler

* 注入 ICharacterPresenceStore

* DespawnImmediatelyAsync：先调用 ICharacterPresenceStore.SetOfflineAsync（快速清理 Redis），再调用 UnregisterEntityAsync + RemoveSessionAsync + GoOfflineAsync

* RenewAllLeasesAsync：同时刷新 Redis presence TTL

### 6. 修改 GameNetworkServer

* 注入 ICharacterPresenceStore

* 收到 HeartbeatMessage 时调用 ICharacterPresenceStore.RefreshHeartbeatAsync

* CleanupConnectionAsync：增加调用 ICharacterPresenceStore.SetOfflineAsync 作为兜底

***

## 实施步骤

### Phase 1: Orleans 存储切换到 Redis（基础设施）

步骤 1.1

：添加 NuGet 包

* Horizon.Orleans.Silo.csproj：添加 Microsoft.Orleans.Clustering.Redis、Microsoft.Orleans.Reminders.Redis

* 其他客户端项目：添加 Microsoft.Orleans.Clustering.Redis

步骤 1.2

：修改 Horizon.Orleans.Silo\Program.cs

* 注释掉原 SQL Server 配置（保留作为备份）

* 添加 Redis 配置（UseRedisClustering、UseRedisReminderService、AddRedisGrainStorage × 7）

步骤 1.3

：修改客户端项目

* Horizon.Game.Core、Horizon.Game.Gateway、Horizon.IM.Gateway、Horizon.WebApi 的 UseAdoNetClustering → UseRedisClustering

步骤 1.4

：appsettings.json 添加 Redis 连接配置

### Phase 2: 角色在线状态 Redis 持久化（双轨制）

步骤 2.1

：创建 ICharacterPresenceStore 接口（Horizon.Game.Core\Sim\Server\ICharacterPresenceStore.cs）

步骤 2.2

：创建 RedisCharacterPresenceStore 实现（Horizon.Strategy.Storage.Redis\RedisCharacterPresenceStore.cs）

步骤 2.3

：DI 注册（Gateway + Silo）

步骤 2.4

：修改 CharacterGrain.cs（注入 ICharacterPresenceStore，移除 IsOnline 持久化）

### Phase 3: 心跳保活机制

步骤 3.1

：创建 CharacterPresenceMonitorHostedService

步骤 3.2

：创建 HeartbeatHandler

步骤 3.3

：修改 GameNetworkServer（注册 HeartbeatHandler + CleanupConnectionAsync 兜底）

步骤 3.4

：修改 PlayerDespawnScheduler（注入 ICharacterPresenceStore）

步骤 3.5

：DI 注册 CharacterPresenceMonitorHostedService

### Phase 4: 客户端心跳发送（如需）

步骤 4.1

：客户端（Flax Engine C#）添加心跳定时器，每 30 秒发送 HeartbeatMessage

***

## 关键文件修改清单

### 新增文件

1. Horizon.Game.Core\Sim\Server\ICharacterPresenceStore.cs — 接口定义
2. Horizon.Strategy.Storage.Redis\RedisCharacterPresenceStore.cs — Redis 实现
3. Horizon.Game.Gateway\Services\CharacterPresenceMonitorHostedService.cs — 心跳监控服务
4. Horizon.Game.Gateway\Handlers\HeartbeatHandler.cs — 心跳处理器

### 修改文件

1. Horizon.Orleans.Silo\Program.cs — Orleans 存储切换
2. Horizon.Orleans.Silo\Horizon.Orleans.Silo.csproj — 添加 Redis Clustering/Reminders 包
3. Horizon.Game.Gateway\Program.cs — 客户端 UseRedisClustering + DI 注册
4. Horizon.Game.Core（客户端配置）— UseRedisClustering
5. Horizon.IM.Gateway\Program.cs — UseRedisClustering
6. Horizon.WebApi\Program.cs — UseRedisClustering
7. Horizon.Orleans.Grains\CharacterGrain.cs — 移除 IsOnline 持久化
8. Horizon.Game.Gateway\Services\PlayerDespawnScheduler.cs — 注入 ICharacterPresenceStore
9. Horizon.Game.Gateway\Network\GameNetworkServer.cs — 心跳处理 + CleanupConnectionAsync 兜底
10. Horizon.Game.Gateway\appsettings.json — Redis 连接配置

### 保留作为备份

* 原 SQL Server 配置代码（注释掉）

* Microsoft.Orleans.Clustering.AdoNet、Persistence.AdoNet、Reminders.AdoNet 包引用

* SQL Server 数据库和表结构

***

## 验证方案

### 验证 1: Orleans Redis 存储切换

1. 启动 Redis（127.0.0.1:9379）
2. 启动 Silo + Gateway
3. 检查日志：无 SQL Server 连接错误，Redis 连接成功
4. 用 redis-cli 检查 orleans-clustering、orleans-reminders、orleans-grain-state 相关 key

### 验证 2: 角色在线状态持久化

1. 角色 A 登录 → redis-cli HGETALL character:presence:{A} 存在，TTL 90 秒
2. 角色 A 离线 → key 被删除
3. 服务器重启 → 90 秒后所有残留 presence key 自动过期

### 验证 3: 心跳保活

1. 角色登录 → 每 10 秒发送心跳 → Redis TTL 被续期
2. 客户端断开（不发送心跳）→ 90 秒后检测到过期，
3. 触发 DespawnImmediatelyAsync → 广播 Despawn → 所有在线玩家看到角色消失
4. 若角色处于交易、支付等安全级别极高的游戏环节心跳需要提升为每5秒检活），不活则终止交易、支付等高级别安全执行流程，并即刻下发目标客户端风险提示，同时并入离线检查流程（先TODO 待开发实际功能时在完善
5. 验证场景：A 先进入，B 后进入，A 离线 → B 看到 A 消失

### 验证 4: 服务器刚启动残留清理

1. 角色 A 登录后强制关闭服务器（模拟崩溃）
2. 重启服务器 → Redis 中 A 的 presence key 仍在（TTL 未过期）
3. 等待 90 秒 → key 自动过期
4. CharacterPresenceMonitorHostedService 启动时立即扫描清理过期 presence

### 验证 5: 降级回退

1. 停止 Redis → 启动 Silo 失败
2. 取消注释 SQL Server 配置，注释 Redis 配置
3. 重新启动 → 正常工作（回退到 SQL Server）

***

## 风险与注意事项

1. Redis 单点故障  ：Redis 宕机会导致整个 Orleans 集群崩溃。保留 SQL Server 配置作为快速回退方案。
2. 数据丢失  ：切换后 Redis 从空开始，所有 grain 状态丢失。角色数据从 EF Core 数据库重新加载。
3. 序列化兼容性  ：Redis GrainStorage 使用 JSON 序列化，需要确认所有 grain 状态可 JSON 序列化。
4. 性能  ：Redis 比 SQL Server 快，但单线程模型可能成为瓶颈。监控 Redis CPU 和内存。
5. MemoryPack  ：角色在线状态 Hash 中的 lastHeartbeat 使用 ISO 8601 字符串，避免二进制序列化兼容性问题。

