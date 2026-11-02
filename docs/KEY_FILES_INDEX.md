# 关键文件索引 · KEY_FILES_INDEX

> **最后更新**：2026-10-05（新增 §10 任务系统落点）· 按 `file:line` 格式组织的开发快速查找表
>
> 所有路径相对仓库根 `C:\Works\GitHubProjects\HundunWorld\`

---

## 1. Flax 客户端（HundunWorld/Source/Game/）

### 1.1 入口与主循环

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| Flax 插件入口 | `HundunWorld/Source/Game/HundunWorldGamePlugin.cs` | 15 | `Plugin.Initialize()` |
| 游戏主单例 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 25 | 懒加载单例 |
| 单例访问触发 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 40 | `Instance` getter |
| 网络初始化 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 60 | `InitializeNetworkManager` |
| ECS 创建 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 71 | `World.Create` + `ArchWorldHost` |
| 系统反射注册 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 74 | `SystemRegistry.RegisterFromAssembly` |
| 启动 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 315 | `StartAsync` |
| Sync 事件订阅 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 288 | `SubscribeSyncHandlerEvents` |
| Snapshot 桥接 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 206 | `SnapshotReceiveBuffer.Enqueue` |
| ChunkDiff 桥接 | `HundunWorld/Source/Game/HundunWorldGame.cs` | 259 | 反序列化 EntityDelta 入队 |
| 每帧 ECS 驱动 | `HundunWorld/Source/Game/ECSUpdateDriver.cs` | 33 | `OnUpdate` |
| 自研 ECS 更新 | `HundunWorld/Source/Game/ECSUpdateDriver.cs` | 41 | `ECSManager.Update` |
| Arch Tick | `HundunWorld/Source/Game/ECSUpdateDriver.cs` | 47 | `archHost.Tick` |
| 输入发送 | `HundunWorld/Source/Game/ECSUpdateDriver.cs` | 58 | `FlushInputSendQueue` |

### 1.2 网络层

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 网络管理器 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 22 | `IDisposable`，TcpClient |
| 网络客户端 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 26 | `TcpClient` 字段 |
| 连接 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 203 | `ConnectAsync` |
| 重连配置 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 244 | `UseReconnection` |
| 数据接收 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 413 | `OnDataReceived` |
| 重连管理 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 406 | `ReconnectionManager` |
| 消息处理 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 619 | `ProcessMessageAsync` |
| 心跳 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 660 | `HandleHeartbeatMessageAsync` |
| 发送 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 833 | `SendAsync<T>` |
| 实际发送 | `HundunWorld/Source/Game/Network/NetworkManager.cs` | 864 | `_client.SendAsync` |
| 网关连接器 | `HundunWorld/Source/Game/Network/GatewayConnector.cs` | 13 | 薄封装 |
| 连接网关 | `HundunWorld/Source/Game/Network/GatewayConnector.cs` | 50 | `ConnectToGatewayAsync` |
| 发到网关 | `HundunWorld/Source/Game/Network/GatewayConnector.cs` | 72 | `SendMessageToGatewayAsync` |
| 线路适配器 | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | 28 | 8B 定长头 |
| 头部注入 | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | 60 | GameId/UserId/MachineId |
| 序列化 | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | 79 | `MemoryPackSerializer` |
| 压缩 | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | 127 | LZ4 >256B |
| 校验和 | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | 293 | `CalculateChecksum` |
| 机器 GUID | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | 336 | `GetMachineGuid` |
| 消息分发 | `HundunWorld/Source/Game/Network/MessageHandlers/MessageProcessor.cs` | 42 | 路由表 |
| 消息类型读取 | `HundunWorld/Source/Game/Network/MessageHandlers/MessageProcessor.cs` | 82 | `Header.MessageType` |
| 同步包枢纽 | `HundunWorld/Source/Game/Network/MessageHandlers/SyncPacketMessageHandler.cs` | 12 | 5 事件分发 |
| SyncPacket 解码 | `HundunWorld/Source/Game/Network/MessageHandlers/SyncPacketMessageHandler.cs` | 41 | `SyncPacketCodec.Decode` |
| 进入游戏处理 | `HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs` | 28 | `HandleAsync` |
| SetPlayerId | `HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs` | 47 | 设置本地玩家 ID |
| 同步握手 | `HundunWorld/Source/Game/Network/MessageHandlers/EnterGameHandler.cs` | 71 | `SendSyncHandshakeAsync` |
| 基类 | `HundunWorld/Source/Game/Network/MessageHandlers/BaseMessageHandler.cs` | 13 | `ValidateMessage` |
| 网络配置 | `HundunWorld/Source/Game/Network/NetworkConfig.cs` | 55 | `network_config.json` |
| INI 凭证 | `HundunWorld/Source/Game/Services/HorizonGameIniReader.cs` | 52 | `HorizonGame.ini` |

### 1.3 玩家与渲染

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 玩家控制器 | `HundunWorld/Source/Game/PlayerController.cs` | 17 | `Script`，输入采集 |
| 每帧更新 | `HundunWorld/Source/Game/PlayerController.cs` | 330 | `OnUpdate` |
| 输入抽象 | `HundunWorld/Source/Game/PlayerController.cs` | 109 | `InputManager` |
| 本地移动 | `HundunWorld/Source/Game/PlayerController.cs` | 348 | `HandleCharacterMovement` |
| 写入 ECS | `HundunWorld/Source/Game/PlayerController.cs` | 411 | `WriteInputToEcs` |
| 直接发包回退 | `HundunWorld/Source/Game/PlayerController.cs` | 369 | `BuildAndSendInputPacket` |
| 立即预测移动 | `HundunWorld/Source/Game/PlayerController.cs` | 726 | `ApplyMovement` |
| 点击移动 | `HundunWorld/Source/Game/PlayerController.cs` | 778 | `HandleClickToMove` |
| 状态机 | `HundunWorld/Source/Game/PlayerController.cs` | 891 | `CharacterState` |
| ECS→Actor 桥 | `HundunWorld/Source/Game/FlaxActorSyncSystem.cs` | 19 | 实体映射 |
| 映射表 | `HundunWorld/Source/Game/FlaxActorSyncSystem.cs` | 22 | `_entityIdToActor` |
| Spawn 订阅 | `HundunWorld/Source/Game/FlaxActorSyncSystem.cs` | 107 | `EntitySpawned` |
| Spawn Actor | `HundunWorld/Source/Game/FlaxActorSyncSystem.cs` | 200 | `Level.SpawnActor` |
| 位置写回 | `HundunWorld/Source/Game/FlaxActorSyncSystem.cs` | 231 | 写回 Actor |

### 1.4 角色与 UI

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 角色管理器 | `HundunWorld/Source/Game/UI/Character/CharacterManager.cs` | 22 | 单例 |
| 防重入标志 | `HundunWorld/Source/Game/UI/Character/CharacterManager.cs` | 50 | 4 个 bool |
| 加载列表 | `HundunWorld/Source/Game/UI/Character/CharacterManager.cs` | 105 | `LoadCharacterListAsync` |
| 选角色 | `HundunWorld/Source/Game/UI/Character/CharacterManager.cs` | 170 | `SelectCharacter` |
| 进入游戏 | `HundunWorld/Source/Game/UI/Character/CharacterManager.cs` | 205 | `EnterGameAsync` |
| 创建角色 | `HundunWorld/Source/Game/UI/Character/CharacterManager.cs` | 311 | `CreateCharacterAsync` |
| 删除角色 | `HundunWorld/Source/Game/UI/Character/CharacterManager.cs` | 461 | `DeleteCharacterAsync` |
| 角色场景 UI | `HundunWorld/Source/Game/UI/Character/CharacterSceneController.cs` | 24 | `Script` |
| 3D 预览 | `HundunWorld/Source/Game/UI/Character/CharacterSceneController.cs` | 59 | `CharacterPreviewPanel` |
| 创建 UI | `HundunWorld/Source/Game/UI/Character/CharacterSceneController.cs` | 92 | `IntegratedCharacterCreationUI` |

### 1.5 HundunAgent（编辑器 AI Agent 插件，取代旧 TraeBridge）

| 模块 | 文件 | 说明 |
|------|------|------|
| 插件入口 | `HundunWorld/Plugins/HundunAgent/Source/HundunAgentEditor/HundunAgentPlugin.cs` | `EditorPlugin`，启动服务器/注册工具 |
| 工具注册表 | `HundunWorld/Plugins/HundunAgent/Source/HundunAgentEditor/Core/ToolRegistry.cs` | 统一工具入口 |
| 场景与 Actor | `HundunWorld/Plugins/HundunAgent/Source/HundunAgentEditor/Tools/SceneActorTools.cs` | 场景/Actor/预制体 |
| 材质与资产 | `HundunWorld/Plugins/HundunAgent/Source/HundunAgentEditor/Tools/MaterialAssetTools.cs` | 资产/材质 |
| 代码热重载 | `HundunWorld/Plugins/HundunAgent/Source/HundunAgentEditor/Tools/CodeTools.cs` | Source 白名单读写 + 编译等待 |
| HTTP 服务 | `HundunWorld/Plugins/HundunAgent/Source/HundunAgentEditor/Server/AgentHttpServer.cs` | `:21900` |
| MCP 服务 | `HundunWorld/Plugins/HundunAgent/Source/HundunAgentEditor/Server/McpServer.cs` | `:21901/mcp` |
| 聊天窗口 | `HundunWorld/Plugins/HundunAgent/Source/HundunAgentEditor/Chat/AgentChatWindow.cs` | 编辑器内 LLM 任务闭环 |

---

## 2. Arch ECS（Horizon.Game.ECS.Arch/）

### 2.1 核心

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| ECS 调度核心 | `Horizon.Game.ECS.Arch/Core/ArchWorldHost.cs` | 17 | 封装 Arch World |
| Tick 主循环 | `Horizon.Game.ECS.Arch/Core/ArchWorldHost.cs` | 127 | `Tick(deltaTime)` |
| 系统组遍历 | `Horizon.Game.ECS.Arch/Core/ArchWorldHost.cs` | 141 | 5 阶段 |
| 系统执行 | `Horizon.Game.ECS.Arch/Core/ArchWorldHost.cs` | 153 | `sys.Update` |
| 系统组枚举 | `Horizon.Game.ECS.Arch/Core/SystemGroup.cs` | 7 | NetworkReceive→...→NetworkSend |
| 系统注册 | `Horizon.Game.ECS.Arch/Core/SystemRegistry.cs` | 19 | `RegisterFromAssembly` |

### 2.2 系统

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 快照应用 | `Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs` | 23 | NetworkReceive:10 |
| 本地预测 | `Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs` | 20 | FixedUpdate:10 |
| 回滚修正 | `Horizon.Game.ECS.Arch/Systems/ReconciliationSystem.cs` | 23 | FixedUpdate:20 |
| 插值 | `Horizon.Game.ECS.Arch/Systems/InterpolationSystem.cs` | 18 | Update:0 |
| 事件应用 | `Horizon.Game.ECS.Arch/Systems/EventApplySystem.cs` | 20 | NetworkReceive:20 |
| 输入发送 | `Horizon.Game.ECS.Arch/Systems/InputSendSystem.cs` | 20 | NetworkSend:0 |

### 2.3 组件与缓冲区

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 插值变换 | `Horizon.Game.ECS.Arch/Components/InterpolatedTransformComponent.cs` | 12 | StartXYZ/TargetXYZ/Alpha |
| 纠错缓冲 | `Horizon.Game.ECS.Arch/Network/CorrectionReceiveBuffer.cs` | — | 单值覆盖+lock |

---

## 3. 服务端核心（Horizon.Game.Core/）

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 同步总入口 | `Horizon.Game.Core/Handlers/SyncPacketHandler.cs` | 15 | Kind 分派 |
| 版本校验 | `Horizon.Game.Core/Handlers/SyncPacketHandler.cs` | 43 | SyncProtocolVersion |
| Handler 基类 | `Horizon.Game.Core/Handlers/MessageHandlerBase.cs` | — | `IMessageHandler` |
| 反作弊 | `Horizon.Game.Core/Sim/MovementValidator.cs` | 21 | 确定性回放 |
| 漂移阈值 | `Horizon.Game.Core/Sim/MovementValidator.cs` | 24 | `DefaultPositionEpsilon=0.5f` |
| 速度上限 | `Horizon.Game.Core/Sim/MovementValidator.cs` | 27 | `DefaultHardSpeedCap=20f` |
| 输入位 | `Horizon.Game.Core/Sim/MovementValidator.cs` | 81 | bit0=跳，bit3=轻功跳 |
| 轻功冲量 | `Horizon.Game.Core/Sim/MovementValidator.cs` | 93 | 5.5/4.5/3.5 |
| 跳跃上限 | `Horizon.Game.Core/Sim/MovementValidator.cs` | 36 | Max=2 / Qinggong=3 |
| 移动公式 | `Horizon.Game.Core/Sim/MovementFormula.cs` | 25 | `FormulaVersion=2` |
| 重力 | `Horizon.Game.Core/Sim/MovementFormula.cs` | 28 | `Gravity=9.81f` |
| 终端速度 | `Horizon.Game.Core/Sim/MovementFormula.cs` | 31 | `TerminalVelocity=50f` |
| 默认速度 | `Horizon.Game.Core/Sim/MovementFormula.cs` | 34 | `DefaultMaxSpeed=6f` |
| AOI | `Horizon.Game.Core/World/ZoneShardAoi.cs` | — | Morton 键 |
| Morton 编码 | `Horizon.Game.Core/World/MortonCodec.cs` | — | 空间填充曲线 |
| 线路适配器 | `Horizon.Game.Core/Adapters/HorizonMessageAdapter.cs` | 138 | `WrapPacket` |
| 指纹接口 | `Horizon.Game.Core/Interfaces/ICharacterFingerprintService.cs` | 11 | 防多开 |
| 消息处理器契约 | `Horizon.Game.Core/IMessageHandler.cs` | 15 | `IMessageHandler` |

---

## 4. Orleans Grain（Horizon.Orleans.Grains/）

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 空间权威 | `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` | 20 | `IZoneShardGrain` 实现 |
| Tick 注册 | `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` | 48 | `OnActivateAsync` 1/60s |
| 构造函数 | `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` | 36 | `MovementValidator` |
| 主键 | `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` | 59 | `GetPrimaryKeyLong` |
| Broadcast | `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` | 462 | `BroadcastSnapshotAsync` |
| SimulatedEntity | `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` | 582 | 内部结构体 |
| 会话 | `Horizon.Orleans.Grains/World/PlayerSessionGrain.cs` | 25 | 瞬态 |
| 输入去重 | `Horizon.Orleans.Grains/World/PlayerSessionGrain.cs` | — | `InputAcceptResult` |
| RPG 持久化 | `Horizon.Orleans.Grains/CharacterGrain.cs` | 29 | 937 行 |
| 持久状态 | `Horizon.Orleans.Grains/CharacterGrain.cs` | 44 | `[PersistentState("character","GameStore")]` |
| DB fallback | `Horizon.Orleans.Grains/CharacterGrain.cs` | 58 | `OnActivateAsync` |

---

## 5. 网关（Horizon.Game.Gateway/）

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 程序入口 | `Horizon.Game.Gateway/Program.cs` | 35 | `class Program` |
| DI 装配 | `Horizon.Game.Gateway/Program.cs` | 244 | fanout 数据流 |
| UseOrleansClient | `Horizon.Game.Gateway/Program.cs` | 276 | clustering 配置 |
| AdoNet | `Horizon.Game.Gateway/Program.cs` | 291 | `UseAdoNetClustering` |
| ClusterOptions | `Horizon.Game.Gateway/Program.cs` | 295 | `ClusterId/ServiceId` |
| 超时修复 | `Horizon.Game.Gateway/Program.cs` | 301 | `ResponseTimeout=30s` |
| fanout 源 | `Horizon.Game.Gateway/Services/GatewaySyncWiring.cs` | 26 | `GatewayZoneShardFanoutSource` |
| Session 注册 | `Horizon.Game.Gateway/Services/GatewaySyncWiring.cs` | 111 | `ConnectionManagerSessionRegistry` |
| 发包槽 | `Horizon.Game.Gateway/Services/GatewaySyncWiring.cs` | 141 | `GameConnectionPacketSink` |
| 后台循环 | `Horizon.Game.Gateway/Services/SyncDispatcherHostedService.cs` | 24 | `IHostedService` |
| 指纹服务 | `Horizon.Game.Gateway/Services/CharacterFingerprintService.cs` | — | Redis 锁 |
| 配置 | `Horizon.Game.Gateway/appsettings.json` | 27 | `UseSyncPacketDispatch` |
| 端口 | `Horizon.Game.Gateway/appsettings.json` | 31 | TCP 7789 |
| Orleans | `Horizon.Game.Gateway/appsettings.json` | 47 | ClusterId=dev |

---

## 6. 共享协议（Horizon.Game.Message/）

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 外层包 | `Horizon.Game.Message/Network/HorizonMessagePacket.cs` | 15 | `[MemoryPackable]` |
| 头部 | `Horizon.Game.Message/Network/HorizonMessagePacket.cs` | 279 | `OnParsingHeader` |
| 消息头 | `Horizon.Game.Message/Network/MessageHeader.cs` | 13 | 路由+鉴权 |
| MessageType | `Horizon.Game.Message/Enums/MessageType.cs` | 9 | `: ushort` |
| 协议版本 | `Horizon.Game.Message/Sync/SyncPackets.cs` | 21 | `SyncProtocolVersion.Current = 2` |
| SyncPacket | `Horizon.Game.Message/Sync/SyncPackets.cs` | 52 | union |
| EntityDelta | `Horizon.Game.Message/Sync/SyncPackets.cs` | 187 | 快照载荷 |
| 编解码 | `Horizon.Game.Message/Sync/SyncPacketCodec.cs` | 13 | 6B 帧 |

---

## 7. Silo（Horizon.Orleans.Silo/）

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 程序入口 | `Horizon.Orleans.Silo/Program.cs` | 44 | `namespace Horizon.Orleans.Silo` |
| Main | `Horizon.Orleans.Silo/Program.cs` | 46 | `class Program` |
| Main 方法 | `Horizon.Orleans.Silo/Program.cs` | 53 | `async Task<int> Main` |

---

## 8. 构建与项目配置

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| Flax 项目定义 | `HundunWorld/HundunWorld.flaxproj` | 10 | 引用 Flax.flaxproj |
| Flax SDK | `HundunWorld/global.json` | 3 | SDK 10.0.300 |
| 主 csproj | `HundunWorld/Source/Game.csproj` | 35 | `FLAX_1_12_OR_NEWER` |
| DLL 引用 | `HundunWorld/Source/Game.csproj` | 160 | HintPath |
| Flax 构建 | `HundunWorld/Source/Game/Game.Build.cs` | 23 | `FileReferences.Add` |
| Arch 版本 | `Horizon.Game.ECS.Arch/Horizon.Game.ECS.Arch.csproj` | 15 | Arch 2.0.0-beta |
| Arch.System | `Horizon.Game.ECS.Arch/Horizon.Game.ECS.Arch.csproj` | 16 | 1.1.0 |
| Message 版本 | `Horizon.Game.Message/Horizon.Game.Message.csproj` | 21 | MemoryPack 1.21.4 |
| TouchSocket | `Horizon.Game.Message/Horizon.Game.Message.csproj` | 37 | 4.1.1 |
| CopyToFlax | `Horizon.Game.Message/Horizon.Game.Message.csproj` | 51 | MSBuild Target |
| Orleans 版本 | `Horizon.Orleans.Grains/Horizon.Orleans.Grains.csproj` | 16 | Orleans 10.0.1 |
| 主解决方案 | `Horizon.sln` | — | 27 个项目 |

---

## 9. 跨平台与机器标识

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| Windows GUID | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | 336 | 注册表读取 |
| Linux machine-id | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | — | `/etc/machine-id` |
| macOS UUID | `HundunWorld/Source/Game/Network/Adapters/HorizonMessageAdapter.cs` | — | `sysctl IOPlatformUUID` |

---

## 10. 任务系统（Quest · 2026-10-05，2026-10-06 同步）

> 设计、决策与部署顺序全部在 [QUEST_SYSTEM_DESIGN.md](./QUEST_SYSTEM_DESIGN.md)；这里只做落点索引。
> **单一真相纪律**：目标语法只有一份编解码、校验只有一份、库行↔草稿映射只有一份、编辑器写库只有 CLI 一条路。

| 层 | 文件 | 行 | 说明 |
|------|------|----|------|
| 实体引用与类别 | `Horizon.Game.Sim/Quests/QuestEntityRef.cs` | 4 | `QuestEntityKind`（npc/monster/item/prop/zone）——发布者可以是人也可以是物件 |
| 完成条件语法 | `Horizon.Game.Sim/Quests/QuestObjectiveCodec.cs` | 7 | `kill_monster:`/`kill_domain:`/`talk_npc:`/`collect_item:`/`use_item:`/`reach_zone:`/`interact_prop:`；`HasProducer` 是"有没有事件源"的唯一口径 |
| 奖励编解码 | `Horizon.Game.Sim/Quests/QuestRewardSpec.cs` | 9 | 键 `item:`/`favor:`/`exp`/`gold`，**只认数字值** |
| 草稿模型 | `Horizon.Game.Sim/Quests/QuestDraft.cs` | 11 | 编辑器 ↔ CLI 的唯一交换 JSON |
| 唯一校验器 | `Horizon.Game.Sim/Quests/QuestTemplateValidator.cs` | 6 | Q000–Q050；发布按"发布之后"的样子校验 |
| 发布者名字 | `Horizon.Game.Sim/Quests/QuestGiverNaming.cs` | 14 | 服务端解析，客户端不自报 |
| 库行↔草稿 | `Horizon.Entities/QuestTemplateMapper.cs` | 16 | 列宽截断在这里，不在 UI |
| 模板实体 | `Horizon.Model/GameModel/QuestTemplateEntity.cs` | 20 | 迁移 013 加的 9 列 |
| 目录/发布 Grain | `Horizon.Orleans.Grains/World/QuestManagerGrain.cs` | 27 | 10s TTL 目录缓存 + 上下线 + 撤销队列（**不做 Grain 互 call**） |
| 契约 | `Horizon.Orleans.Interface/World/IQuestManagerGrain.cs` | 20 | 实时发布/关闭/取消三入口都在这 |
| 玩家任务 Grain | `Horizon.Orleans.Grains/QuestGrain.cs` | 21 | 接取门：发布态 + 前置 + 周期 |
| 网关处理 | `Horizon.Game.Core/Handlers/QuestHandler.cs` | 16 | 清单/可接/接取/提交/交互裁决；撤销队列在这里落地 |
| 线上↔模板映射 | `Horizon.Game.Core/Handlers/QuestWireMapper.cs` | 16 | 唯一一份目标/奖励解码 |
| 实体身份注册表 | `Horizon.Game.Core/Sim/Server/DsEntityRegistry.cs` | 28 | DS 上行 `EntityDescriptor=9` 的落点，按 shard 分片 |
| 交互裁决（纯函数） | `Horizon.Game.Core/Sim/Server/QuestInteractionAuthority.cs` | 14 | 存在 + 可交互类别 + 邻近；换轴口径在这里 |
| 写库 CLI | `Horizon.QuestCli/Program.cs` | 23 | `catalog/list/get/validate/draft/publish/online/unpublish/retire/delete`，编辑器的唯一写库通道；**`publish` = 草稿落库并发布**，`online` = 把库里已有的下线行放上线，`delete` = 只删已作废的（已接玩家不受影响，依据在 `QuestDeletionSafetyTests`） |
| 草稿层（Redis） | `Horizon.QuestCli/QuestDraftStore.cs` | — | 草稿只住 Redis：`quest:draft:{id}` 正文 + `quest:drafts` ZSET 索引，72 小时、每次保存续期；索引项惰性剔除；发布成功后删草稿（内容权威权交给 SQL） |
| CLI 编码核验（只读） | `scripts/verify-quest-cli-encoding.py` | — | 不带 stdin 的动词 / 带 BOM 草稿 / 无 BOM 草稿；把解码器改成 936 做过变异验证（会判红） |
| CLI 草稿核验（只读） | `scripts/verify-quest-draft-redis.py` | — | Redis 往返中文按字段值比对、TTL≈72h、删除即失效、**无草稿时发布被拒且 SQL 侧查无此行** |
| 编辑器窗口 | `HundunWorld/Plugins/QuestEditor/Source/QuestEditorEditor/QuestEditorWindow.cs` | 19 | Flax 编辑器 Tools → 任务编辑器 |
| 客户端服务 | `HundunWorld/Source/Game/Network/QuestClientService.cs` | 21 | 回包在连接线程 → UI 靠 `Revision` 脏标记主线程重绘 |
| 客户端接收 | `HundunWorld/Source/Game/Network/MessageHandlers/QuestMessageHandler.cs` | 15 | 含接取/提交/交互三类回执 |
| 任务日志页 | `HundunWorld/Source/Game/UI/Ink/Pages/Quest/QuestLogPage.cs` | 15 | 数据驱动（含"可接任务"组） |
| 菜单卷宗页 | `HundunWorld/Source/Game/UI/Ink/Pages/MenuQuestsPage.cs` | 24 | 数据驱动；协议没有的字段显示占位不编造 |
| 世界内触发点 | `HundunWorld/Source/Game/Selection/WorldSelectionService.cs` | — | 选中 Npc/物品/采集点后按 **G** → `ReportInteract(entityId)` |
| 迁移与种子 | `scripts/sql/013_quest_editor_columns.sql` / `014_seed_quest_editor.sql` / `015_seed_material_item_templates.sql` | — | **三条都已在 dev 库执行**（015 是材料 `700001..700041`，回退见方案 §12.1） |
| 种子入库前核验（只读） | `scripts/verify-quest-material-seed.py` | — | 对账 SQL 双分支列覆盖、与客户端 `MaterialData` 的 id/名称一致、与库里现有 item_id 零重叠；`--offline` 跳过连库项 |
| 特殊物品种子（**待批准**） | `scripts/sql/016_seed_special_item_templates.sql` | — | 任务物品 5 / 配方 2 / 宝箱 2，号段 750001·760001·770001（全部 <800000，避开秘籍启发式）；5 道入库后自检不满足即整批回滚；**只生成未执行** |
| 物品种子通用核验（只读） | `scripts/verify-item-template-seed.py` | — | INSERT/UPDATE 双分支列覆盖、同列重复赋值（Msg 264）、号段与档位自洽、重名；`--live` 再对一次真库。015/016 都跑通（反向对照会判红） |
| 奖励显示覆盖面普查（只读） | `scripts/verify-quest-reward-display-coverage.py` | — | 按 `ItemCatalog.Build` 真实解析路径逐条数 171 件：156 叫得出 / 15 叫不出（正是 T9 那批），"多一件就 FAIL"；明细落 `docs/quest_reward_display_coverage.json` |
| 作者链路实况核验（只读） | `scripts/verify-quest-authoring-cli.py` | — | 真的调用 CLI `validate` 跑四案例：`talk_npc` 可入库(带 Q039)、未登记 NPC 被 Q027 拒、材料未入库被 Q034 拒（`QUEST_MATERIALS_IN_DB=1` 后翻成"必须通过"）、**管道中文长度探针**盯 stdin 编码 |
| 名称→模板 id 解析 | `Horizon.Game.Sim/Quests/QuestCatalogLookup.cs` | 60 | 数字优先 → 整名相等 → 唯一子串；歧义不猜。插件里那份 `QuestEditorWindow.ResolveTemplateId` 是刻意复制（不引服务端程序集），三步顺序由源码闸门钉住 |
| 实时发布/关闭（运行态证明） | `Horizon.QuestContractTests/QuestLivePublishTests.cs` | 44 | 2 条：只改库不碰 Grain 写接口 → 可接列表出现→消失→出现 + 版本号跟随；走 Grain 的热更/发布/关闭 + 服务端等级门槛。双重门控 `HORIZON_QUEST_DB` + `HORIZON_ORLEANS_REDIS`，探针行自建自清 |
| 契约测试 | `Horizon.QuestContractTests/` | — | 130 条，能在 Silo/Gateway/DsHost 跑着时单独执行；`QuestEditorContractTests`（编辑器/契约口径）、`QuestCatalogLookupTests`（名称解析 + 两边顺序不得漂移）、`QuestRewardRoutingTests`（奖励物品的使用分档：号段只是必要条件，T9①）、`QuestHandlerInteractTests`（网关交互胶水：目标下标、四条拒绝码、只推自己那类）、`QuestHandlerOfferListTests`（网关可接收窄胶水：**发布者认不出/超距时一次都不请求可接表**，防"用不存在的对象换整个目录"）、`QuestManagerRevocationLiveTests`（真集群，`HORIZON_ORLEANS_REDIS` 门控）、`DsEntityIdentityTests`（身份与裁决）、`DsEntityDescriptorProductionTests`（真开一个 `DsWorldSession` 看 DS 到底有没有把身份带出来）、`QuestGrainLockDisciplineTests`（源码级锁纪律，突变验证过）、`QuestCatalogFingerprintTests`（目录版本号=内容指纹，T10，突变验证过）、`FlaxToolsRefreshListTests`（Tools 刷新清单漂移闸门，T13）、`QuestAuthoringDbTests`（连真库那几条，`HORIZON_QUEST_DB=1` 门控；含"秘籍号段里不得混入非秘籍物品"这条奖励域现状闸门，T9） |
| 目录版本号（纯函数） | `Horizon.Orleans.Interface/World/QuestCatalogFingerprint.cs` | 26 | 玩家可见字段的有序 FNV-1a；空目录固定值；唯一调用点 `QuestManagerGrain.cs:416` |
| 行为级集群回归 | `Horizon.QuestContractTests/QuestManagerRevocationLiveTests.cs` | 41 | T8：`unpublish(force)` 在 5 秒死时限内返回 + 撤销队列正是那个持有者 + 后续调用仍能应答；`HORIZON_ORLEANS_REDIS` 门控，探针任务 20001（对库净零）。**2026-10-06 真集群首跑 2/2 通过**。（原放 `Gateway.Tests`，被并行会话在途改动挡住编译后搬到本工程——这里不引 Gateway/Silo 工程，不会被进程锁住） |

---

## 相关文档

- [ARCHITECTURE.md](./ARCHITECTURE.md) — 整体架构
- [NETCODE.md](./NETCODE.md) — netcode 设计
- [SERVER.md](./SERVER.md) — 服务端详解
- [CLIENT.md](./CLIENT.md) — 客户端详解
