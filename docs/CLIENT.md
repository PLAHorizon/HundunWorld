# 客户端架构 · CLIENT

> **最后更新**：2026-06-15 · 配套文档：[NETCODE](./NETCODE.md) · [NETWORK_PROTOCOL](./NETWORK_PROTOCOL.md) · [SERVER](./SERVER.md)

---

## 1. 游戏引擎确认

**游戏引擎：Flax Engine 1.12**（C# 游戏引擎，非 Unity/UE/Stride）。

证据：
- `HundunWorld/HundunWorld.flaxproj:10` 引用 `"$(EnginePath)/Flax.flaxproj"`
- `Game.csproj:35` DefineConstants 含 `FLAX_1`、`FLAX_1_12`、`FLAX_1_12_OR_NEWER`（最高版本）
- 所有脚本继承 `FlaxEngine.Script`
- 所有 DLL HintPath 指向 `C:\Program Files (x86)\Flax\Flax_1.12\`
- `Content/GameSettings.json:6-7`：`"ProductName": "混沌世界"`, `"CompanyName": "成阳网络"`

**输出**：`OutputType=Library`，构建为 `Game.CSharp.dll`，由 FlaxEditor 加载。

---

## 2. 双世界架构（核心设计）★

客户端运行**两套 ECS 并存**：

| ECS | 职责 | 入口 |
|-----|------|------|
| **自研 ECSManager** | Flax 侧游戏逻辑（非网络） | `ECS/ECSManager.cs` |
| **Arch ECS**（`Horizon.Game.ECS.Arch`） | **网络同步/预测/插值/回滚**（权威逻辑） | `ArchWorldHost` |

两者由 `ECSUpdateDriver.OnUpdate()` 每帧共同驱动（`ECSUpdateDriver.cs:33-53`）。

> 设计意图：Flax Actor 世界负责视觉渲染，Arch ECS 世界负责网络权威状态，两者通过 `FlaxActorSyncSystem` 桥接。本地玩家走预测管线（直接写 Actor），远程实体走插值管线（ECS → Actor）。

---

## 3. 启动流程时序图 ★

```
1. Flax Engine 启动
   └─ 加载 HundunWorldGamePlugin.Initialize()              [HundunWorldGamePlugin.cs:15]
       └─ 日志输出，单例 _instance = this

2. Flax 加载初始场景（DefaultScene dc72848f...）
   └─ 场景中的某个 Script 访问 HundunWorldGame.Instance   [HundunWorldGame.cs:40-52]
       └─ 触发懒加载构造（单例）:
           ├─ InitializeNetworkManager()                   [line 60, 103]
           │   ├─ NetworkConfigManager.LoadConfig()         读 network_config.json
           │   ├─ HorizonGameIniReader.TryRead()            读 HorizonGame.ini (优先)
           │   ├─ new NetworkManager(gatewayList)           [line 127]
           │   ├─ 订阅 ConnectionStatusChanged 等事件
           │   ├─ SubscribeSyncHandlerEvents()              延迟订阅 SyncPacketMessageHandler 5 类事件
           │   └─ Task.Run → ConnectAsync(ip, port)         后台首连 [line 146-166]
           ├─ World.Create() + new ArchWorldHost(_archWorld) [line 71-74]
           │   └─ SystemRegistry.RegisterFromAssembly()     反射注册 6 个同步系统
           ├─ new ECSManager / WorldManager / PlayerPositionUpdater / EventBroadcaster
           └─ StartAsync()                                  [line 315]
               ├─ DatabaseManager.ClearAllCacheData()
               ├─ _ecsManager.Start()
               └─ _worldManager.StartSynchronization()

3. ECSUpdateDriver (场景 Script) 每帧 OnUpdate              [ECSUpdateDriver.cs:33]
   ├─ ECSManager.Update(dt)
   ├─ archHost.Tick(dt) → NetworkReceive→FixedUpdate→Update→Render→NetworkSend
   └─ FlushInputSendQueue() → SendAsync(SyncFrameMessage)

4. 用户登录/认证（UI 触发 AuthenticationManager）
   └─ LoginResponseHandler 处理响应 → 设置 AuthToken/Passport

5. 角色选择场景
   └─ CharacterManager.LoadCharacterListAsync()            [CharacterManager.cs:105]
       └─ CharacterSceneController 渲染角色列表 + 3D 预览

6. 进入游戏
   └─ CharacterManager.EnterGameAsync()                    [line 205]
       ├─ NetworkManager.SendMessageAsync(EnterGameRequest)
       ├─ GameSceneManager.TransitionTo(GameWorld)         切场景
       └─ [服务端回 EnterGameResponse]
           └─ EnterGameHandler.HandleAsync                 [EnterGameHandler.cs:28]
               ├─ gameInstance.SetPlayerId(characterId)    设置本地玩家 ID
               └─ networkManager.SendSyncHandshakeAsync(characterId)  开启 CSP 同步

7. 游戏循环中持续:
   ├─ PlayerController 采集输入 → WriteInputToEcs → 本地预测移动
   ├─ InputSendSystem 打包 → ECSUpdateDriver 发送 SyncFrameMessage
   ├─ 服务端快照 → SnapshotApplySystem → FlaxActorSyncSystem → 远程角色 Actor
   └─ ReconciliationSystem 修正预测偏差
```

### 3.1 单例懒加载

`HundunWorldGame.cs:25-52` —— 注意是纯 C# 单例（非 `Script`）：
```csharp
public static HundunWorldGame Instance
{
    get {
        if (_instance == null) {
            _instance = new HundunWorldGame();
            _instance.StartAsync().ConfigureFalseAwait();   // line 48
        }
        return _instance;
    }
}
```

单例在首次被访问时（通常由场景中的 `Script` 触发）自动构造并启动。

---

## 4. 每帧驱动：ECSUpdateDriver

`ECSUpdateDriver.cs:16` —— 挂在场景 Actor 上的 `Script`，是 **Flax 主线程 → ECS 的桥接器**。

`OnUpdate()`（`:33-53`）执行两件事：

```csharp
public override void OnUpdate()
{
    if (HundunWorldGame.Instance?.ECSManager != null)
    {
        // (1) 更新自研 ECSManager（非 Arch）
        HundunWorldGame.Instance.ECSManager.Update(Time.DeltaTime);   // line 41

        // (2) 驱动 ArchWorldHost（含 SnapshotApply/Interpolation 等同步系统）
        var archHost = HundunWorldGame.Instance.ArchHost;
        if (archHost != null)
        {
            archHost.Tick(TimeSpan.FromSeconds(Time.DeltaTime));      // line 47
            FlushInputSendQueue();  // line 50 — 把 ECS 产出的输入包发到服务端
        }
    }
}
```

`FlushInputSendQueue()`（`:58-97`）：从 `InputSendSystem.GetPendingInputs()` 取出输入 → `SyncPacketCodec.Encode` 编码成 `SyncFrameMessage` → `networkManager.SendAsync(syncFrame)`。

**这是客户端→服务端输入上行的关键出风口。**

---

## 5. 网络层

### 5.1 组件层次

```
NetworkManager (TcpClient + 消息分发)
  └─ GatewayConnector (网关连接封装/事件转发)
       └─ HorizonMessageAdapter (MemoryPack + LZ4 + 8 字节定长头)
            └─ MessageProcessor (MessageType → IMessageHandler 路由表)
                 └─ 26+ 个 MessageHandler
```

### 5.2 NetworkManager

`Network/NetworkManager.cs:22` —— `public class NetworkManager : IDisposable`

- **底层传输**：`TouchSocket.Sockets.TcpClient`（`:26`），**不是 Flax 自带网络**
- 配置：`TouchSocketConfig` + `UseReconnection<ITcpClient>` 自动重连（`:244-247`）
- `ConnectAsync(ip, port)`（`:203`）：幂等检查状态 → 初始化 TcpClient → 注册 `Connected/Closed/Received` 事件 → `SetupAsync` + `ConnectAsync`
- `OnDataReceived`（`:413`）→ 解包 → `ProcessMessageAsync`（`:619`）→ 分发到 `MessageProcessor`
- `SendAsync<T>(T message)`（`:833`）：检查 `_client.Online` → `HorizonMessageAdapter.PackMessage` → `_client.SendAsync`（`:864`）
- 重连：`ReconnectionManager`（`:406`）
- 心跳：`HandleHeartbeatMessageAsync`（`:660`）自动回 ACK

### 5.3 GatewayConnector

`Network/GatewayConnector.cs:13` —— 对 `NetworkManager` 的薄封装：
- `ConnectToGatewayAsync`（`:50`）、`SendMessageToGatewayAsync<T>`（`:72`）
- 转发 `ConnectionStatusChanged` / `ConnectionError` 事件
- **事件中继 + 网关列表管理**

### 5.4 HorizonMessageAdapter（线路协议）

`Network/Adapters/HorizonMessageAdapter.cs:28` —— 继承 `CustomFixedHeaderDataHandlingAdapter`，**8 字节定长头**（`:37`）：

```
[4字节长度][1字节MessageType][1字节压缩标志][2字节校验和][payload]
```

详见 [NETWORK_PROTOCOL.md §2](./NETWORK_PROTOCOL.md)。

**机器标识注入**（`:60-71`）：每包带 `MachineId`，由 `MachineIdentifier.GetMachineGuid()`（`:336-475`）跨平台读取机器 GUID：
- Windows：注册表 `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`
- Linux：`/etc/machine-id`
- macOS：`sysctl IOPlatformUUID`

### 5.5 MessageProcessor（消息分发）

`Network/MessageHandlers/MessageProcessor.cs:42` —— 经典路由表：

```csharp
public async Task ProcessMessageAsync(HorizonMessagePacket message)
{
    var messageType = message.Header.MessageType;          // line 82
    if (_handlers.ContainsKey(messageType))                 // line 84
        foreach (var handler in _handlers[messageType])     // line 86 — 支持多处理器
            await handler.HandleAsync(message);             // line 90
}
```

`BaseMessageHandler.cs:13` —— 抽象基类，`ValidateMessage`（`:34`）验证 `MessageType` + `ServiceType` 匹配。

### 5.6 Handler 清单（26 个）

| 类别 | Handler |
|------|---------|
| **认证/登录** | `LoginResponseHandler`, `RegisterResponseHandler` |
| **角色管理** | `CreateCharacterHandler`, `CreateCharacterResponseHandler`, `CharacterListHandler`, `DeleteCharacterHandler/ResponseHandler`, `CharacterManagementMessageHandler`, `EnterGameHandler` |
| **同步核心** | `SyncPacketMessageHandler`（**最关键**）, `HeartbeatResponseHandler` |
| **战斗** | `AttackResponseHandler`, `DamageResponseHandler`, `CombatMessageHandler`, `SkillCastResponseHandler`, `SkillInterruptHandler` |
| **社交/系统** | `ChatMessageHandler`, `FriendMessageHandler`, `MailMessageHandler`, `TradeMessageHandler`, `QuestMessageHandler`, `RankingMessageHandler`, `AchievementMessageHandler`, `DungeonMessageHandler` |

---

## 6. SyncPacketMessageHandler（同步包枢纽）★

`Network/MessageHandlers/SyncPacketMessageHandler.cs:12` —— 处理所有 ECS 同步包：
- `SyncPacketCodec.Decode` 解码（`:41`）
- 按 `SyncPacketKind` 触发 5 个事件：

```csharp
public event Action<SnapshotPacket> SnapshotReceived;       // line 19
public event Action<InputAckPacket> InputAckReceived;       // line 20
public event Action<EventPacket> EventReceived;             // line 21
public event Action<WorldChunkDiffPacket> ChunkDiffReceived;// line 22
public event Action<HandshakePacket> HandshakeReceived;     // line 23
```

这些事件被 `HundunWorldGame.SubscribeSyncHandlerEvents()`（`:288-310`）订阅，桥接到 ECS 缓冲区：

| 事件 | 桥接目标 |
|------|---------|
| `OnSnapshotReceived` | `SnapshotReceiveBuffer.Instance.Enqueue`（`:206`） |
| `OnInputAckReceived` | `InputAckReceiveBuffer.Instance.Latest = inputAck`（`:220`） |
| `OnHandshakeReceived` | `SetPlayerId` + `MarkSyncHandshakeComplete`（`:237`） |
| `OnChunkDiffReceived` | 反序列化 `EntityDelta[]` 构造快照入队（`:259-281`） |

---

## 7. PlayerController（玩家输入）★

`PlayerController.cs:17` —— 继承 `Script`，挂在本地玩家 Actor 上。

### 7.1 输入采集

- **InputManager 抽象层**（`:109, :299`）：优先 `_inputManager.IsActionPressed("MoveForward")`，否则回退 `Input.GetKey(KeyboardKeys.W)`（`:569-575`）。支持 WASD/方向键
- **武侠特色**：`QinggongSystem`（轻功系统，`:110, :300`）通过 `GetQinggongInputBits()` 贡献额外输入位
- **点击移动**：`HandleClickToMove`（`:778`）—— 鼠标左键 `GroundClick` + 地面射线检测
- **状态机**：`CharacterState`（Walking/Jumping/Falling 等，`:891-1012`）

### 7.2 输入双路上报（关键设计）

`OnUpdate`（`:330-369`）每帧执行**双路输入上报**：

```csharp
HandleCharacterMovement();                  // line 348 — 本地立即移动（预测）
UpdateMovementBuffer();
bool ecsInputSent = WriteInputToEcs();      // line 360 — 优先路径：写 ECS
if (!ecsInputSent)                          // line 363 — 回退路径
    if (_inputSendAccumulator >= InputSendInterval)  // 1/60秒 (line 134)
        BuildAndSendInputPacket();          // line 369 — 直接构造 InputPacket 发送
```

`WriteInputToEcs()`（`:411-439`）—— 主路径：把输入写入 Arch World 中本地玩家实体的 `PlayerInputComponent`：
```csharp
ref var input = ref archWorld.Get<PlayerInputComponent>(_localPlayerEntity);  // line 424
input.MoveX = _moveDirection.X;    // line 425
input.MoveY = _moveDirection.Z;    // line 426
input.InputBits = inputBits;       // line 439
```
之后由 `InputSendSystem`（NetworkSend 阶段）打包，`ECSUpdateDriver.FlushInputSendQueue` 发出。

**为什么是双路**：
- `HandleCharacterMovement` 本地立即移动 → 提供"零延迟"手感
- `WriteInputToEcs` 写入 ECS → 让同步系统统一处理打包/去重/重传

### 7.3 本地预测移动

`ApplyMovement`（`:726-742`）每帧立即 `Actor.Position += totalMovement`（`:742`），不等服务端确认 —— 这就是客户端预测。后续 `ReconciliationSystem` 会修正偏差。

---

## 8. FlaxActorSyncSystem（ECS → Actor 视觉桥）★

`FlaxActorSyncSystem.cs:19` —— 维护 `Dictionary<ulong, Actor> _entityIdToActor`（`:22`）：
- 订阅 `SnapshotApplySystem.EntitySpawned` 事件（`:107`），Spawn 时 `Level.SpawnActor` + 挂 `RemotePlayerActor` 脚本（`:200-214`）
- 每帧从 Arch World 读 `NetworkIdentityComponent` 位置，写回 Flax Actor（`:231-266`）

**桥接关系**：
```
Arch ECS World (权威状态)
  ↓ NetworkIdentityComponent / PredictedTransformComponent / InterpolatedTransformComponent
FlaxActorSyncSystem (每帧读取)
  ↓ 写回 Actor.Position / Rotation
Flax Actor World (视觉渲染)
```

---

## 9. 角色与 UI 系统

### 9.1 CharacterManager

`UI/Character/CharacterManager.cs:22-25` —— 单例 `CharacterManager.Instance`。状态机 `_stateManager` + 事件总线 `_eventBus` 驱动。4 个 `bool` 防重入标志（`:50-53`）：`_isLoadingCharacterList` / `_isCreatingCharacter` / `_isDeletingCharacter` / `_isEnteringGame`。

核心流程方法：

| 方法 | 行 | 功能 |
|------|----|------|
| `LoadCharacterListAsync()` | 105 | 从 StateManager 读角色列表，否则请求刷新 |
| `SelectCharacter()` | 170 | 选中角色，发 `SelectedCharacterChangedEvent` |
| `EnterGameAsync()` | 205 | **发 EnterGame 消息 → 切场景到 GameWorld** |
| `CreateCharacterAsync()` | 311 | 发 CreateCharacterRequest（名字/职业/性别/外观） |
| `DeleteCharacterAsync()` | 461 | 发 DeleteCharacterRequest |

**进入游戏的网络调用**（`:222-251`）：
```csharp
var enterGameRequest = new EnterGameRequest { CharacterId = _selectedCharacter.CharacterId };
var networkManager = HundunWorldGame.Instance.NetworkManager;
sendSuccess = await networkManager.SendMessageAsync(messagePacket);  // line 251
GameSceneManager.Instance.TransitionTo(GameWorld)                    // line 269
```

### 9.2 CharacterSceneController

`UI/Character/CharacterSceneController.cs:24` —— 继承 `Script`，挂在角色选择场景上。管理 3D 角色预览 `CharacterPreviewPanel`（`:59`）、角色滚动列表、集成创建 UI `IntegratedCharacterCreationUI`（`:92`）。

### 9.3 EnterGameHandler

`EnterGameHandler.cs:28-108` —— 收到 `EnterGameResponse` 后：
1. `gameInstance.SetPlayerId(response.CharacterInfo.CharacterId)`（`:47`）
2. `networkManager.SendSyncHandshakeAsync(characterId)`（`:71`）—— **发起同步握手，开启 CSP 同步链路**

---

## 10. ECS 客户端架构（与 Arch 集成）

### 10.1 系统注册（反射扫描）

`HundunWorldGame.cs:74-77`：
```csharp
_archWorldHost = new ArchWorldHost(_archWorld);
var archAssembly = typeof(SnapshotApplySystem).Assembly;
var registeredSystems = SystemRegistry.RegisterFromAssembly(_archWorldHost, archAssembly);
```

`SystemRegistry.RegisterFromAssembly`（`Core/SystemRegistry.cs:19`）扫描所有带 `[ArchSystem]` 特性的类，无参构造后 `AddSystem`。

### 10.2 完整的预测-协调-插值管线

详见 [NETCODE.md](./NETCODE.md)。6 个系统：

| 系统 | Group | 职责 |
|------|-------|------|
| `SnapshotApplySystem` | NetworkReceive | 应用服务端快照，Spawn/Despawn |
| `LocalSimulationSystem` | FixedUpdate | 本地预测（`MovementFormula.Step`，轻功多段跳） |
| `ReconciliationSystem` | FixedUpdate | 回滚修正（0.5m 阈值） |
| `InterpolationSystem` | Update | 远程实体插值（100ms 到达目标） |
| `EventApplySystem` | NetworkReceive | 技能/伤害/死亡事件 |
| `InputSendSystem` | NetworkSend | 打包本地输入入队 |

---

## 11. HundunAgent（编辑器 AI Agent 插件）

**让 AI Agent 直接在 Flax 编辑器中完成游戏客户端开发工作（场景/Actor/预制体/材质/贴图/代码热重载）。** 独立插件位于 `Plugins/HundunAgent/`（取代旧 TraeBridge，后者已移除）。

### 11.1 三种接入方式

| 方式 | 端点 | 说明 |
|------|------|------|
| MCP 服务器 | `http://localhost:21901/mcp` | JSON-RPC 2.0（initialize / tools/list / tools/call），任意 MCP 客户端可直接驱动编辑器 |
| HTTP REST | `http://localhost:21900/` | `GET /api/tools` 工具清单；`POST /api/tools/{name}` 调用工具 |
| 编辑器聊天窗口 | 菜单 Tools → HundunAgent 聊天窗口 | 配置任意 OpenAI 兼容 API（BaseUrl/ApiKey/Model），编辑器内 function-calling 任务闭环 |

### 11.2 工具集（约 30 个）

| 分类 | 工具 |
|------|------|
| 场景与 Actor | `scene_list / scene_load / scene_save / scene_new / scene_hierarchy / actor_get / actor_find / actor_create / actor_set_transform / actor_set_property / actor_delete / actor_duplicate / actor_reparent / selection_get / selection_set` |
| 预制体 | `prefab_spawn / prefab_create / prefab_apply` |
| 材质与资产 | `asset_search / asset_get / asset_import / material_create / material_set_param / material_assign / material_instance_create` |
| 截图与环境 | `viewport_screenshot / viewport_camera_set / env_set` |
| 代码与热重载 | `code_list / code_read / code_write / code_build_wait / code_build_status`（仅限 Source 目录白名单，写入前自动备份） |
| 任务控制 | `agent_status / undo_checkpoint / undo_rollback / agent_plan_echo / chat_window_open` |

### 11.3 安全与审计

- 变更操作纳入编辑器 Undo 栈，`undo_checkpoint` + `undo_rollback` 支持任务级整体回滚。
- 危险操作（删除 Actor、写代码等）在聊天窗口中需用户确认。
- 全部工具调用记录在 `Logs/HundunAgent/tools-yyyyMMdd.jsonl`；聊天设置存于 `Cache/HundunAgent/settings.json`（不含内置密钥）。

**用途**：AI 辅助关卡/场景编辑、资产装配、代码修改热重载。仅编辑器模式生效，非游戏运行时逻辑。

---

## 12. 凭证注入机制

### 12.1 HorizonGame.ini（外部启动器写入）

`Services/HorizonGameIniReader.cs:52` —— 从工作目录读 `HorizonGame.ini`，含：
- `[Auth] AuthToken`
- `[User] PassportId / UserId`
- `[Game] GameId / ServerId`
- `[GameGateway] Host / Port`
- `[IMGateway]`

这是**外部启动器（耕地）在启动客户端时写入的登录凭证和网关地址**，优先级最高。

### 12.2 优先级

`HundunWorldGame.cs:113-125` 中 `gatewayList.Insert(0, ...)` 把 INI 的网关插到列表头部：
1. **HorizonGame.ini**（最高，启动器注入）
2. `network_config.json`（本地配置）
3. 代码默认值（最低）

### 12.3 network_config.json

`Network/NetworkConfig.cs:55` —— 文件名 `network_config.json`，从 `Config/` 目录或工作目录读取。默认配置（`:144-157`）：
```
192.168.1.78:7789 (华东)
192.168.2.78:7789 (华南)
192.168.3.78:7789 (华北)
AutoConnect=true, ReconnectInterval=5000ms
```

---

## 13. 配置文件

| 文件 | 用途 |
|------|------|
| `Source/App.config` | 占位（Flax 用 net10.0，bindingRedirect 已移除） |
| `Content/GameSettings.json` | Flax 标准设置，产品名"混沌世界" |
| `Content/Settings/Network Settings.json` | Flax 内置 ENet 配置（**未实际使用**，游戏用 TouchSocket） |
| `Network/network_config.json` | 网关列表（运行时生成） |
| `HorizonGame.ini` | 启动器注入的凭证 |

> ⚠️ `Network Settings.json` 配置了 Flax ENet 驱动（7777 端口），但**项目未启用**它。游戏实际使用 TouchSocket TCP 7789。详见 [TECH_DEBT.md](./TECH_DEBT.md)。

---

## 14. 客户端-服务端交互模式总结

**模式：权威服务器 + 客户端预测 + 快照插值（CSP + Snapshot Interpolation 混合）**

| 维度 | 实现 |
|------|------|
| **传输** | TouchSocket TCP 长连接（非 Flax ENet） |
| **协议** | 8 字节头 + MemoryPack + LZ4(>256B) + 累加校验和 |
| **消息路由** | `MessageType` 枚举 → `Dictionary<MessageType, List<IMessageHandler>>` |
| **本地玩家** | 客户端预测（`LocalSimulationSystem` + `PlayerController.ApplyMovement`）+ 服务端协调（`ReconciliationSystem`，0.5m 阈值） |
| **远程玩家** | 快照插值（`SnapshotApplySystem` → `InterpolationSystem` → `FlaxActorSyncSystem`） |
| **输入上行** | 60Hz，`InputPacket` 编码为 `SyncFrameMessage` |
| **状态下行** | `SnapshotPacket` / `WorldChunkDiffPacket` / `EventPacket` |
| **握手** | `EnterGameResponse` → `SendSyncHandshakeAsync` → `HandshakePacket` → `MarkSyncHandshakeComplete` |

---

## 15. 相关文档

- [NETCODE.md](./NETCODE.md) — 客户端预测/协调/插值的完整管线
- [NETWORK_PROTOCOL.md](./NETWORK_PROTOCOL.md) — 协议帧格式
- [ARCHITECTURE.md](./ARCHITECTURE.md) — 客户端在整体架构中的位置
- [KEY_FILES_INDEX.md](./KEY_FILES_INDEX.md) — 客户端关键文件索引
