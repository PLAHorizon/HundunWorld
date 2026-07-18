# 关键文件索引 · KEY_FILES_INDEX

> **最后更新**：2026-06-15 · 按 `file:line` 格式组织的开发快速查找表
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

### 1.5 TraeBridge

| 模块 | 文件 | 行 | 说明 |
|------|------|----|------|
| 编辑器插件 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeEditorPlugin.cs` | 1 | `#if FLAX_EDITOR` |
| 初始化 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeEditorPlugin.cs` | 29 | `Initialize` |
| HTTP 服务器 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 16 | 单例 |
| HttpListener | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 21 | `:21888` |
| 场景信息 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 168 | `/api/scene/info` |
| 层级树 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 396 | `/api/scene/hierarchy` |
| 设属性 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 716 | `/api/.../property` |
| 设 Transform | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 644 | `/api/.../transform` |
| 导入资产 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 1049 | `/api/assets/import` |
| 截图 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 1108 | `/api/viewport/screenshot` |
| 执行代码 | `HundunWorld/Source/Game/TraeBridge/TraeBridgeServer.cs` | 1174 | `/api/execute` |

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

## 相关文档

- [ARCHITECTURE.md](./ARCHITECTURE.md) — 整体架构
- [NETCODE.md](./NETCODE.md) — netcode 设计
- [SERVER.md](./SERVER.md) — 服务端详解
- [CLIENT.md](./CLIENT.md) — 客户端详解
