# FlaxEngine 无头 DS 与专用服务器网关 — 深度改造方案 v5

> 文档性质：**经引擎源码 + 运行时反射 + 端到端实跑三重验证**的改造方案 + 现状校正
> 生成时间：2026-09-19（v5）
> 目标指令：「根据新的设计要求和仓库现有代码能力，深度改造 FlaxEngine 无头服务器 DS 及专为服务器而生的专用 FlaxEngine 服务器网关」

> **修订历史**
> - **v1**：基于全仓 C# 源码检索，判定「`INetworkDriver` 不存在」→ **错误**（源码检索只能证明仓库没写实现，不能证明引擎没有接口）
> - **v2**：对 `FlaxEngine.CSharp.dll` 做 .NET 反射，确认接口存在，取得完整签名；但仍未确认注入路径是否开放
> - **v3**：查证 **Flax 引擎 C++ 源码**，确认 `INetworkDriver` **按设计支持自定义实现**（`NetworkSettings.NetworkDriver` 是带 `TypeReference(typeof(INetworkDriver))` 的类型名字段）。**路线 A 从「有条件采纳」升级为「官方支持的标准扩展点」**，见 §1.3
> - **v3 收尾**：V6 完成（读出 `Content/Settings/Network Settings.json` 实际值，`MaxClients=100`）；补充**附录 C：`GatewayNetworkDriver` 实现骨架**（含三条引擎约束的落地写法）；修正 §1.3 约束 3 的表述（数据报文**不经过** `PopEvent` 是确定结论，非待验证推测）；R2 风险关闭
> - **v4**：**V2、V4 实证完成**，且**推翻 v3 的两条结论**——
>   (a) §1.3 约束 3 的「`NetworkEventType` 无 `Message` 成员 / 数据报文不走 `PopEvent`」**是错的**。实测枚举有 **5** 个成员（含 `Message=4`），`NetworkEvent` 有 **3** 个字段（含 `Message`，类型 `NetworkMessage`）。引擎头文件 `NetworkEvent.h` 与反射结果一致。**§0 表格第 3、4 条判定随之翻转。**
>   (b) V4 实测：`Script` 的 11 个生命周期方法**全部是 `virtual`**，且引擎暴露 `Scripting.InvokeOnUpdate(Action)` / `Scripting.RunOnUpdate(Action)` 两个**主动切入帧循环**的入口。→ `OnUpdate` 驱动可行，但**不建议作为 DS 主驱动**（理由见 §5 V4 结论）。
>   (c) **重大发现**：三个"丢失"项目**并未丢失**——完整旧快照仓库存在于 `C:\Works\GitHubProjects\20260801\`（见 §3 修订）。
> - **v5（本版，2026-09-19 深夜）**：**阶段 2 端到端实跑完成并全绿**——且**实测捕获一处阻断性缺陷**：
>   `SyncInputHandler` 把 `GetGrain<IPlayerSessionGrain>` 放在 DS 路由判定**之前**，导致
>   (1) Orleans 不可用时 DS 通道**直接走不通**（`ArgumentException` 在抵达路由前抛出）；
>   (2) Orleans 可用时同一输入被**两个权威各应用一次**（权威分裂）。
>   **已修复**：本地去重前置 → 立即尝试 DS 路由 → 仅路由落空才解析 grain。修复后 `GatewayDsFullLoopLiveTests`
>   通过、该 ERROR **归零**、全量套件 **2672 通过 / 0 失败**。详见 **§6.1**。

---

## 0. 结论速览

设计文档的**技术选型方向正确**，且 `INetworkDriver` 路线**完全可行**——引擎按设计支持替换网络驱动。但文档有 **6 处具体事实错误**，其中 2 处会直接导致编译失败，1 处是虚构风险，3 处是重复建设。

| # | 文档主张 | 验证结果 | 判定 |
|---|---|---|---|
| 1 | 实现 `INetworkDriver` 接入网关 | ✅ **接口存在**（12 成员，完整签名已取得）；✅ **官方支持自定义**（`NetworkSettings.NetworkDriver` + `TypeReference(typeof(INetworkDriver))` + 编辑器 `TypeNameEditor`） | ✅ **成立，官方扩展点** |
| 2 | `SendMessage(channelType, message)` 单项重载 | ❌ **不存在**。真实是 `SendMessage(NetworkChannelType, NetworkMessage)`（**值类型传值**，非引用） | ⚠️ **签名错误** |
| 3 | `NetworkEvent.EventType = NetworkEventType.Message` | ✅ **存在**。实测枚举有 5 成员：`Undefined=0 / Connected=1 / Disconnected=2 / Timeout=3 / Message=4` | ✅ **成立**（v4 纠正，v3 此条判断错误） |
| 4 | "`PopEvent` 可能成为瓶颈，用独立线程消费" | ⚠️ **部分成立**：数据**确实**走 `PopEvent`（`Message` 事件）；但真正的瓶颈论证不成立——`PopEvent` 只是"从队列取一个已完成的事件"，真正的收发在驱动内部。结论「用独立线程消费」仍**不必要** | ⚠️ **结论巧合正确，论证错误**（v4 纠正） |
| 5 | "MemoryPack 的 AOT 限制，需正确配置生成器" | ❌ **不存在这个限制**。MemoryPack 是编译期源码生成器，`Game.Build.cs` 已证明 DLL 可加载 | ❌ **虚构风险** |
| 6 | 用 `JobSystem` 并行跑 Arch `Query` | ❌ **引擎侧不可行**：`ArchWorldHost.Tick` 持 `_ticking` 互斥 + 要求主线程；`Arch 2.0.0-beta` 无 `JobScheduler` | ❌ **不可行** |
| 7 | 新建 `ShardEnvelope` 信封 | ❌ 仓库已有等价物 `DsUplinkProtocol`（旁路信令，8 种 kind，有单测），重建即重复造轮子 | ❌ **重复建设** |
| 8 | 新建 `EntityStateBatch` / `ClientInput` | ❌ 仓库已有 17 种包型的 `SyncPacket` union（含 `SnapshotPacket`/`InputPacket`） | ❌ **重复建设** |
| 9 | 一致性哈希路由（MurmurHash3, 128 虚拟节点） | ⚠️ 与项目既有语义冲突：AOI 是**空间连续**的，哈希会把相邻玩家打散到不同分片，摧毁 AOI 价值 | ❌ **模型错误** |

**最高优先级事实（v4 修正）**：`Horizon.DsHost`、`Horizon.Game.Sim`、`Horizon.Game.World` 三个项目**并未丢失**——完整的旧快照仓库位于 **`C:\Works\GitHubProjects\20260801\`**（详见 §3）。当前仓库中这三项的源码**已存在且比快照更新**（`DsHostRunner.cs` 399 → 485 行），`Horizon.sln` 已可**整体构建**。

**完整验证清单进度（v5）**：V1 ✅ · V2 ✅ · V3 ✅ · V4 ✅ · V5 ✅ · V6 ✅ —— **全部六项完成**。取证工具：`Tools/V2Probe`、`Tools/V4Probe`。

**实施进度（v5）**：阶段 0 ✅ · 阶段 1 ✅ · **阶段 2 ✅（本轮完成）** · 阶段 3–6 ⬜ 待做。
阶段 2 采用「先控制台宿主、后 Flax 宿主」的隔离策略，**控制台宿主版已全绿**（`GatewayDsFullLoopLiveTests` 通过）。
本轮额外收获：实测捕获并修复 `SyncInputHandler` 的双重派发缺陷（§6.1）——这是**只有真跑端到端才能暴露**的问题。**下一步：把 DS 换成 Flax 宿主验证宿主层等价性**。

---

## 1. `INetworkDriver` 的真实契约（双重验证）

**验证方式 1（托管侧）**：`Assembly.LoadFrom("HundunWorld/Output/Windows/FlaxEngine.CSharp.dll")` + .NET 反射枚举成员。
**验证结果**：`FlaxEngine.CSharp 1.12.6913.0`

**验证方式 2（引擎侧）**：查阅 Flax 引擎 C++ 源码 `Source/Engine/Networking/`（`NetworkPeer.cpp` / `NetworkManager.cpp` / `NetworkSettings.h`）。

### 1.1 接口完整签名（权威，可直接照此实现）

```csharp
namespace FlaxEngine.Networking;

public interface INetworkDriver
{
    string  DriverName();
    bool    Initialize(NetworkPeer host, NetworkConfig config);
    void    Dispose();
    bool    Listen();
    bool    Connect();
    void    Disconnect();
    void    Disconnect(NetworkConnection connection);
    bool    PopEvent(out NetworkEvent eventPtr);          // 注意: 是 out，非 ref
    void    SendMessage(NetworkChannelType channelType, NetworkMessage message);
    void    SendMessage(NetworkChannelType channelType, NetworkMessage message, NetworkConnection target);
    void    SendMessage(NetworkChannelType channelType, NetworkMessage message, NetworkConnection[] targets);
    NetworkDriverStats GetStats();
    NetworkDriverStats GetStats(NetworkConnection target);
}
```

参考实现 `FlaxEngine.Networking.ENetDriver` 实现了**完全相同的 12 个成员** → 说明这是完整的实现契约，不是残缺接口。

### 1.2 配套类型（全部已核实）

```csharp
public enum NetworkEventType : int        // ★ 只有 4 个成员，没有 Message！
{
    Undefined = 0, Connected = 1, Disconnected = 2, Timeout = 3,
}

public enum NetworkChannelType : int
{
    None = 0, Unreliable = 1, UnreliableOrdered = 2, Reliable = 3, ReliableOrdered = 4,
}

public struct NetworkEvent
{
    public NetworkEventType EventType;
    public NetworkMessage   Message;
    public NetworkConnection Sender;
}

public struct NetworkConnection { public uint ConnectionId; }

public struct NetworkConfig
{
    public object  NetworkDriver;      // ★ 类型是 Object —— 自定义驱动可注入
    public ushort  ConnectionsLimit;
    public string  Address;
    public ushort  Port;
    public ushort  MessageSize;
    public ushort  MessagePoolSize;
}

public struct NetworkMessage
{
    public byte*  Buffer;         // ★ 非托管裸指针
    public uint   MessageId;
    public uint   BufferSize;
    public uint   Length;
    public uint   Position;
    public void WriteBytes(byte[] bytes, int length);
    public void ReadBytes(byte[] buffer, int length);
    public void WriteInt32(int value);  /* ...全部原生类型 + Vector2/3/4/Quaternion/Guid/String... */
}

public struct NetworkDriverStats { public float RTT; public uint TotalDataSent; public uint TotalDataReceived; }

public class NetworkPeer
{
    public bool Listen();  public bool Connect();  public void Disconnect();
    public bool PopEvent(out NetworkEvent eventRef);
    public NetworkMessage CreateMessage();
    public void RecycleMessage(NetworkMessage message);
    public NetworkMessage BeginSendMessage();
    public void AbortSendMessage(NetworkMessage message);
    public bool EndSendMessage(NetworkChannelType, NetworkMessage);
    public bool EndSendMessage(NetworkChannelType, NetworkMessage, NetworkConnection);
    public bool EndSendMessage(NetworkChannelType, NetworkMessage, NetworkConnection[]);
    public NetworkPeer CreatePeer(NetworkConfig config);
    public void ShutdownPeer(NetworkPeer peer);
    public NetworkPeer[] Peers { get; }
    public INetworkDriver NetworkDriver { get; }     // ★ 可读，用于校验注入是否生效
}
```

### 1.3 四条必须知道的设计约束

**约束 1 — 自定义驱动是**官方的标准扩展点**（✅ 已验证，路线 A 可行）**

引擎 C++ 侧的三段代码构成了完整的「自定义驱动」通路：

**(a) `NetworkSettings.h` — 驱动字段本身就是类型引用**
```cpp
/// The type of the network driver (implements INetworkDriver) that will be used
/// to create, manage, send and receive messages over the network.
API_FIELD(Attributes="EditorOrder(1100), EditorDisplay(\"Transport\"),
           TypeReference(typeof(INetworkDriver)),                                    // ★ 限定必须实现该接口
           CustomEditorAlias(\"FlaxEditor.CustomEditors.Editors.TypeNameEditor\")")   // ★ 编辑器里是"选类型"下拉
StringAnsi NetworkDriver = "FlaxEngine.Networking.ENetDriver";
```
`TypeReference(typeof(INetworkDriver))` + `TypeNameEditor` 说明：**这一字段的设计意图就是让用户从所有 `INetworkDriver` 实现里挑一个**，默认值只是内置的 ENet 驱动。这是明确的官方扩展点，不是巧合。

**(b) `NetworkManager.cpp` — 按类型名反射实例化**
```cpp
const ScriptingTypeHandle networkDriverType = Scripting::FindScriptingType(settings.NetworkDriver);
if (!networkDriverType) { LOG(Error, "Unknown Network Driver type {0}", ...); return true; }
networkConfig.NetworkDriver = ScriptingObject::NewObject(networkDriverType);
```

**(c) `NetworkPeer.cpp` — 接口转换（无白名单）**
```cpp
NetworkDriver = ToInterface<INetworkDriver>(Config.NetworkDriver);
if (!NetworkDriver) { LOG(Error, "NetworkDriver doesn't implement INetworkDriver interface"); return true; }
```
**没有硬编码白名单**——只要 `ToInterface<INetworkDriver>` 成功即可。

→ **结论**：`GatewayNetworkDriver` 只要继承 `ScriptingObject` 并实现 `INetworkDriver`，把 `NetworkSettings.NetworkDriver` 指向它的类型名即可生效。**无需改动引擎。**

**约束 2 — `eventPtr` 是 `out`，且 `NetworkMessage` 含裸指针**

```csharp
bool PopEvent(out NetworkEvent eventPtr);
public struct NetworkMessage { public byte* Buffer; public uint MessageId, BufferSize, Length, Position; }
```

`NetworkPeer.cpp` 的消息池实现揭示了**必须遵守的生命周期协议**：
```cpp
NetworkMessage NetworkPeer::CreateMessage()
{
    const uint32 messageId = MessagePool.Pop();                       // 池里取出
    uint8* messageBuffer = GetMessageBuffer(messageId);
    return NetworkMessage(messageBuffer, messageId, Config.MessageSize, 0, 0);
}
bool NetworkPeer::EndSendMessage(NetworkChannelType channelType, const NetworkMessage& message)
{
    NetworkDriver->SendMessage(channelType, message);
    RecycleMessage(message);                                          // ★ 引擎发完立刻回收！
    return false;
}
```

→ **关键陷阱**：`EndSendMessage` 在调用 `SendMessage` **之后立即回收** `MessageId`。因此
**驱动不能在 `SendMessage` 里保存 `message` 或它的 `Buffer` 指针**（异步落盘/入队 = use-after-free）。
必须在 `SendMessage` 内部**同步拷贝**出去。
→ `Buffer` 来自 `Platform::AllocatePages`，整个池是一块连续页；`MessageSize` 由 `NetworkSettings` 决定。

**约束 3 — `NetworkEventType` 有 5 个成员（含 `Message`），数据报文确实走 `PopEvent`** ⚠️ *(v4 已纠正)*

> v3 此处断言「只有 4 个成员、无 `Message`、数据报文不走 `PopEvent`」——**三条全错**。
> 错因：v3 依据的是**记忆/推测**而非实读源码。v4 用两条独立证据链纠正。

**证据链 1（引擎头文件，实读 `Source/Engine/Networking/NetworkEvent.h`）**：

```cpp
API_ENUM(Namespace="FlaxEngine.Networking") enum class NetworkEventType
{
    Undefined = 0, Connected, Disconnected, Timeout,
    /// Event "message" - message received from some client or the server.
    Message
};

API_STRUCT(Namespace="FlaxEngine.Networking") struct FLAXENGINE_API NetworkEvent
{
    API_FIELD() NetworkEventType EventType;
    /// If this is an message-event, make sure to return the message using
    /// RecycleMessage function of the peer after processing it!
    API_FIELD() NetworkMessage     Message;
    /// <remarks>Only valid when event has been received on server-peer.</remarks>
    API_FIELD() NetworkConnection  Sender;
};
```

**证据链 2（托管程序集反射，`Tools/V2Probe`，实际运行输出）**：

```
[1] NetworkEventType 成员
  · FlaxEngine.Networking.NetworkEventType（IsEnum=True）
        0 = Undefined     1 = Connected     2 = Disconnected
        3 = Timeout       4 = Message
  合计 5 个成员

[2] NetworkEvent 字段
  · FlaxEngine.Networking.NetworkEvent（IsValueType=True IsPublic=True）
      FlaxEngine.Networking.NetworkEventType  EventType
      FlaxEngine.Networking.NetworkMessage    Message
      FlaxEngine.Networking.NetworkConnection Sender
  合计 3 个公开字段
  · Message 的托管类型: NetworkMessage
      NetworkMessage 字段: Byte* Buffer / UInt32 MessageId / UInt32 BufferSize / UInt32 Length / UInt32 Position
```

**证据链 3（分发链存在性）**：`NetworkInternal.h` 声明了完整的数据报文处理链——
`OnNetworkMessageObjectReplicate` / `ObjectSpawn` / `ObjectDespawn` / `ObjectRole` / `ObjectRpc` …
以及 `enum class NetworkMessageIDs : uint8 { Handshake, HandshakeReply, Key, ObjectReplicate, ..., ObjectRpcPart }`。
**没有 `Message` 事件，这套分发链就没有入口**。

**由此得到的正确结论（三条）**：

1. **文档的 `NetworkEventType.Message` 是对的**（§0 第 3 项翻转）。引擎在收到数据报文时，
   驱动（如 `ENetDriver`）会填充 `NetworkEvent { EventType = Message, Message = ..., Sender = ... }`。
2. **数据报文确实走 `PopEvent`**。`NetworkPeer::PopEvent` 是纯转发（`return NetworkDriver->PopEvent(eventRef);`），
   但**转发的内容包含 `Message`**。上层按 `EventType` 分派：`Connected/Disconnected/Timeout` 是生命周期，
   `Message` 才是数据。
3. **`PopEvent` 返回的 `Message` 有回收义务**：头文件明确写着
   *"make sure to return the message using RecycleMessage function of the peer after processing it"*。
   → **这是路线 A 最容易出 use-after-free 的地方（比约束 2 的发送侧更易踩）**：
   接收侧如果漏调 `RecycleMessage`，消息池会在 `NetworkSettings.MessageSize × 连接数` 的量级上耗尽 → 后续所有 `CreateMessage` 失败。

> **对路线 B 的影响**：v3 曾用"数据不走 PopEvent"论证"路线 B 不损失任何引擎能力"。该论证**失效**。
> 但路线 B 的结论**依然成立**，理由换成更朴素的一条：**路线 B 根本不用 `INetworkDriver`，
> 也就不涉及 Flax 消息池的任何生命周期约束**。它损失的是 Flax 高层网络 API
> （`NetworkManager` / `NetworkTransform` / `NetworkReplicator`）——而这些能力项目**当前不用**
> （`docs/flax-study/04` 明确"不使用 Flax 内建 High-Level Networking"）。

---

## 2. 两条路线：可以并存，建议 A+B 组合

V1 已验证自定义驱动是官方扩展点，**两条路线都可行**。它们解决的是不同问题，不互斥。

### 路线 A — 实现 `INetworkDriver`（文档主张）

```
Flax NetworkPeer → GatewayNetworkDriver(INetworkDriver) → TouchSocket → 网关
```

**收益**：Flax 的高层网络能力（`NetworkManager` / `NetworkTransform` / `NetworkReplicationHierarchy`）可用；`NetworkSettings.NetworkDriver` 一行配置即可切换。

**代价**：
1. **必须处理 `NetworkMessage` 裸指针 + 消息池生命周期**（约束 2）——`EndSendMessage` 发完立即回收，驱动内必须同步拷贝。这是最容易出 use-after-free 的一段。
2. **多一层适配**：`SyncPacket` → `NetworkMessage` 打包 → 网关 → 再解包，每条消息额外两次拷贝。
3. **数据面收益需重新评估**（v4 修正）：数据报文**确实**走 `PopEvent`（约束 3），因此路线 A 的驱动实现**能**承载数据面。但收益仍不明确——驱动里必须**同步拷贝**（约束 2）且接收侧必须 `RecycleMessage`，每次往返比旁路多两次拷贝 + 一层消息池管理；而项目已有 `DsUplinkClient` 直连方案且经混沌测试验证。**结论：路线 A 的收益主要在 Flax 高层 API，不在数据面吞吐。**
4. **与既有客户端实现重复**：仓库已有 `Network/GatewayConnector.cs`、`GatewaySelector.cs`、`ReconnectionManager.cs`、`Sync/` 等 30+ 文件的直连实现，功能重叠。

**适用场景**：需要 Flax 高层网络 API（例如用 `NetworkTransform` 做场景对象自动复制）时。**实现骨架见附录 C。**

### 路线 B — 自研旁路，直接接到 DS 侧

```
Flax 只做渲染/输入/物理/场景
网络: DsUplinkClient(BCL TcpClient) ⇄ 网关 ⇄ 客户端
```

**收益**：
1. **零重复建设**：协议（`DsUplinkProtocol`）、17 种包型（`SyncPacket`）、重连退避、应答配对全部已存在且有单测。
2. **规避全部裸指针/消息池风险**（约束 2 完全不涉及）。
3. **`DsUplinkClient` 的注释已写明这个设计意图**（原文）：
   > 为什么用 BCL socket 而非 TouchSocket：DS 是独立进程，用 `TcpClient` 可以**零框架耦合地把宿主移植到任意 .NET 环境（含 UE UnrealSharp 托管域）**，也让分帧/重连/应答匹配等逻辑保持可单测。

   同一句话对 Flax 托管域**完全适用** —— 这条设计是预先为 Flax 铺路的。

**适用场景**：DS 主链路（玩家移动、快照、战斗）——这是吞吐主体。

### 推荐：B 为主链路，A 作可选增强

| 用途 | 路线 |
|---|---|
| 玩家移动 / 快照 / 输入 / 战斗（吞吐主体） | **B**（已就绪，零新建） |
| 场景对象自动复制（若要复用 `NetworkTransform`） | **A**（可选，需处理裸指针） |
| DS 控制面（注册/心跳/摘除） | **B**（`DsUplinkClient` 已实现并带单测） |

**执行顺序**：先 B 打通端到端（不依赖任何未验证前提），再评估是否需要 A。

---

## 3. 三个项目源码状态（v4 重大修正：**并未丢失**）

> ⚠️ **v3 的"源码丢失"判断是错的。** 真实情况分三层：

### 3.0 事实校正（v4，实测）

**(a) 当前仓库里三个项目源码已存在且比任何备份都新。**

```
C:\Works\GitHubProjects\HundunWorld\
├── Horizon.DsHost\        ← 存在：DsHostRunner.cs(485) / Program.cs(549) / DsHostFileConfig.cs(91) / appsettings.json
├── Horizon.Game.Sim\      ← 存在：SimWorld.cs / CombatCore.cs / MovementFormula.cs / MonsterAiCore.cs / SpawnDirectorCore.cs …
└── Horizon.Game.World\    ← 存在
```

**(b) 完整旧快照仓库存在于 `C:\Works\GitHubProjects\20260801\`**（此前从未被检索到）。

```bash
# 实测：只比源码（排除 bin/obj）
$ diff -rq --exclude=bin --exclude=obj 20260801/Horizon.Game.Sim   HundunWorld/Horizon.Game.Sim
（无输出 → 完全一致）
$ diff -rq --exclude=bin --exclude=obj 20260801/Horizon.Game.World HundunWorld/Horizon.Game.World
（无输出 → 完全一致）
$ diff -rq --exclude=bin --exclude=obj 20260801/Horizon.DsHost     HundunWorld/Horizon.DsHost
Files .../DsHostRunner.cs differ
Files .../Program.cs differ
```

| 项目 | 快照 vs 当前（源码） | 说明 |
|---|---|---|
| `Horizon.Game.Sim` | **0 处差异** | 逐字节一致 |
| `Horizon.Game.World` | **0 处差异** | 逐字节一致 |
| `Horizon.DsReplayHarness` | **0 处差异** | 逐字节一致 |
| `Horizon.DsHost` | **2 处差异**（`DsHostRunner.cs` 399→485 行、`Program.cs`） | 当前版本**更新**（新增 `StartDataPlaneAsync` 拆分、`_sendPumpRunning` 双路径、Orleans 超时门控） |
| `Horizon.Game.Gateway` | 1 处（当前多 `KifaMarketDataFetcher.cs`） | 当前版本**新增** |
| `Horizon.Game.Gateway.Tests` | 4 处（当前多 `DeterministicTraceGen.cs` / `MultiEntityTraceGen.cs`） | 当前版本**新增** |

**结论：快照是「旧版基线」，当前仓库是「在其之上的增量演进」。没有任何源码丢失。**

**(c) 快照时间戳**：`20260801/Horizon.DsHost/DsHostRunner.cs` = 2026-09-14 22:14；`SimWorld.cs` = 2026-08-16 21:56。

### 3.0.1 为什么 v3 会误判"丢失"

v3 的排查清单（`git log --all` / `git ls-files` / 2.7 GB 备份 zip / `Docs/` 索引）**全部命中了否**，于是推论"不存在"。
漏掉的是**同盘并列目录**——`C:\Works\GitHubProjects\` 下除 `HundunWorld\` 外还有 `20260801\`（一个以日期命名的完整仓库副本）。
`HundunWorld_UE5` 副本被排查了，但 `20260801` 没有被列入候选。

> **教训**：源检索应覆盖**同级目录**，而不只是当前仓库内部 + 明确命名的备份。
> 「git 无记录」只能证明"未入库"，不能证明"磁盘上不存在"。

### 3.1 三项目当前状态

| 项目 | 状态 | 内容 |
|---|---|---|
| **`Horizon.DsHost`** | ✅ 存在，**已超出快照版本** | DS 宿主本体（`DsHostRunner`/`DsHostOptions`/`DsHostFileConfig`/`Program.RunControlPlaneAsync`） |
| **`Horizon.Game.Sim`** | ✅ 存在，与快照**逐字节一致** | DS 权威模拟内核（`SimWorld`/`CombatCore`/`MovementFormula`/`MonsterAiCore`/`SpawnDirectorCore`） |
| **`Horizon.Game.World`** | ✅ 存在，与快照**逐字节一致** | 世界数据契约 |

**`Horizon.sln` 已回填至 44 项目**（补入 `Horizon.DsReplayHarness`），**可整体构建**。
**阶段 0 验收通过**：`dotnet build` 0 错误 + `Deterministic*ParityTests` 全绿。

### 3.2 快照的保留建议（勿删）

`C:\Works\GitHubProjects\20260801\` 是**唯一可追溯的旧版基线**，价值在于：
- 可作为"当前版本相对基线的增量"对照（如上表）；
- 若将来发现某处行为回归，可用 `diff` 快速定位是"本来就坏"还是"改坏了"；
- 它含 `Horizon.Contract.Tests`、`FluentAvalonia`、`HarmonyTest`、`GengDi.Setup.iss` 等**当前仓库没有的**项目。

→ **建议保留，不要清理。**

### 3.3 V5 附带发现：Flax DLL 搜索顺序陷阱（真实踩坑）

`ServerGame.Build.cs` 最初的 `searchDirs` 顺序是 `engineDir` → `engineDir/Tools` → `projectDir/Output/Windows`。
引擎的 `Binaries/Tools/` 里**残留着上一次构建复制过去的旧版** `Horizon.*.dll`，被抢先命中：

| 程序集 | 引擎 Tools 里（旧） | 项目 Output 里（新） |
|---|---|---|
| `Horizon.Game.Sim.dll` | 29696 B（Aug 11） | 69632 B（Sep 19） |
| `Horizon.Game.Core.dll` | 397312 B（Aug 16） | 509440 B（Sep 19） |

→ 引用到的是**不含新类型的旧二进制**，报出极具迷惑性的错误：

```
DedicatedServerHost.cs(171,29): error CS0246: 'DsUplinkClient' could not be found
```

**明明 `using Horizon.Game.Core.Sim.Server;` 就在文件顶部、DLL 也"找到了"。**
日志表现为"复制了 1 个文件"（只复制真正新增的那个），其余静默用旧版——**极难排查**。

**修法**：把 `projectDir/Output/Windows` 提到**最前**，引擎目录降为兜底：

```csharp
string[] searchDirs =
{
    // ★ 项目自身构建产物优先——这些才是 Horizon.sln 刚编译出来的最新程序集。
    Path.Combine(projectDir, "Output", "Windows"),
    Path.Combine(projectDir, "Output", "Windows", "Development"),
    // 兜底：引擎目录（仅当项目输出缺该 DLL 时才会走到这里）。
    Path.Combine(engineDir, "Tools"),
    engineDir,
};
```

配合 `rm -rf Cache/Intermediate/ServerTarget` 强制重建后 CS0246 全部消失，`ServerTarget` 构建 **0 错误**。

`DsHostRunnerTests.cs`(483 行) + `DsHostReconnectLiveTests.cs`(258 行) + `GatewayDsFullLoopLiveTests.cs` 精确固定了宿主行为：

```csharp
namespace Horizon.DsHost;

public sealed class DsHostOptions
{
    public string DsId { get; set; }
    public long[] ShardIds { get; set; }
    public int    TicksPerSecond { get; set; } = 60;
    public int    ListenPort { get; set; }
    public List<SpawnPointConfig> SpawnPoints { get; } = new();
    // 推测：GatewayHost / GatewayPort / Token / Region / Host
}

public sealed class DsHostRunner : IAsyncDisposable
{
    public DsHostRunner(DsHostOptions options, IDsDownlinkSender? sender);

    public DsDataPlaneHost DataPlane { get; }   // runner.DataPlane.Apply(handshake)
    public DsWorldSession World { get; }        // runner.World.PlayerCount
    public long BatchesSent   { get; }
    public long BatchesFailed { get; }

    public Task<DsTickResult> RunSingleTickAsync();   // ★ 单 tick 驱动
}

// 可执行入口: Horizon.DsHost/Program.RunControlPlaneAsync
// 环境变量门控: HORIZON_DSHOST_EXE
```

**默认值证据**（`GatewayDsFullLoopLiveTests.cs:32`）：
```csharp
/// <summary>DsHost 默认刷怪点 1 的 0 号槽位实体 ID（<c>(1 &lt;&lt; 32) | 1</c>）。</summary>
```
→ 默认刷怪点 `SpawnPointId=1`；实体 ID 规则 `(spawnPointId << 32) | slotIndex`，与 `DsWorldSession` 中「怪物实体 ID 高 32 位非 0」的校验一致。

**关键**：`DsHostRunnerTests` 有「6 个 tick、快照间隔 3 → 恰好 2 次快照下线（tick 3 与 tick 6）」这类**精确数值断言**。这意味着测试全绿 ≈ 行为与丢失前一致。**测试就是规格。**

### 3.4 阶段 0/1 实际验收结果（v4 已全部通过）

> §3.2 / §3.3 的"重建顺序"是 v3 在"源码丢失"前提下写的预案。
> v4 确认**无需重建**，直接按下方实测验收。

| 验收项 | 结果 |
|---|---|
| `dotnet build Horizon.sln`（44 项目） | ✅ **0 错误** |
| `DsHostRunnerTests` | ✅ **8/8 通过** |
| `Deterministic*ParityTests` / `TraceRegeneration*` | ✅ 含两条金轨迹**逐字节复刻**：单实体 38030 B(604 行) / 多实体 113510 B(2396 行) |
| 网关全量测试套件 | ✅ **2672 通过 / 0 失败 / 8 跳过** |
| `Horizon.DsReplayHarness` | ✅ `FINGERPRINT = 1474bd23…18836`，`RESULT = PASS` |
| `ServerTarget` Flax 构建 | ✅ **0 错误**（`Total time: 00:00:07.0674789`） |
| `DedicatedServerHost.cs` Flax 编译 | ✅ 通过（`override void OnStart/OnUpdate/OnDestroy` 全匹配基类虚方法） |
| **阶段 1 控制台宿主实跑** | ✅ `DsDataPlaneHost：数据面已监听 0.0.0.0:19899` → `DsHostRunner：数据面与发送泵已启动` → `DS 就绪` |
| `DsHostReconnectLiveTests`（混沌） | ✅ **2/2 通过**（`[14 s]` 断线重连重新注册 / `[12 s]` 网关永不可达不崩溃） |

**`DsHostRunner` 的关键修复（v4 落地）**：Sprint 4 引入的 `_sendChannel` + 后台发送泵让
`RunSingleTickAsync` 变成"投递即返回"，而测试**从不调 `Start*`**（泵从未启动）→ Channel 无消费者
→ 断言 `sender.Batches` 读不到任何东西（4 项失败）。
修法是引入 `_sendPumpRunning` 标志做**双路径**：泵在跑 → 异步投递 Channel；
泵没跑（测试 / 外部宿主启动前）→ **同步 await 发送**，使"`RunSingleTickAsync` 返回时批次已发出"成为可依赖契约。

### 3.5 重建顺序（**已不需要**，保留作历史参考）

~~**Step 1 `Horizon.Game.World`**~~（最底层，无依赖）— 从已有的 `World/MortonCodec.cs`(109 行) + `World/ZoneShardAoi.cs`(165 行) 出发，提供 `WorldCoord.MetresPerChunkCell`(=16) 与 chunk 换算。

~~**Step 2 `Horizon.Game.Sim`**~~ — 从引用点反推的导出清单：
```
SimWorld                // AddOrUpdateEntity / RemoveEntity / SubmitInput / Step / GetEntity / SetPosition
CombatCore              // Resolve(...) → CombatResolution
                        // Roll01(ulong a, ulong b, long tick, uint salt)  ← 确定性掷骰
MovementFormula         // DefaultMaxSpeed / FormulaVersion
CorrectionPacket        // 已存在于 Core/Sim/CorrectionPacket.cs → 迁移
MonsterAiCore           // Decide / SortTargetsByEntityId / ComputeRespawnTick
SpawnDirectorCore       // Tick / CreateSlots / MarkSlotAlive / MarkSlotDead / ComputeEntityId
MonsterAiSelf / MonsterAiState / MonsterAiTarget / MonsterAiConfig
SpawnPointConfig / SpawnSlotState
```

~~**Step 3 `Horizon.DsHost`**~~ — 按 §3.1 契约实现。

~~**Step 4**~~ 回填 `Horizon.sln`，`dotnet build Horizon.sln` 0 错误。 → **已完成（44 项目）**

---

## 4. 改造实施

### 4.1 Flax 无头 DS 宿主

**新增** `HundunWorld/Source/ServerTarget.Build.cs`：
```csharp
using Flax.Build;

public class ServerTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();
        Modules.Add("Game");
        Modules.Add("ServerGame");
    }
}
```

**新增** `HundunWorld/Source/ServerGame/ServerGame.Build.cs` —— 相对 `Game.Build.cs` 的差异：

| 项 | Game（客户端） | ServerGame（DS） |
|---|---|---|
| `BuildNativeCode` | `false` | `false`（保持纯 C# 托管域） |
| 额外 DLL | — | `Horizon.DsHost.dll`、`Horizon.Game.Sim.dll`、`Horizon.Game.World.dll` |
| `SystemReferences` | 已含 `System.Net.Sockets` | 复用 |

**修改** `HundunWorld.flaxproj`：
```json
{
  "GameTarget":   "GameTarget",
  "EditorTarget": "GameEditorTarget",
  "ServerTarget": "ServerTarget",
  ...
}
```
> ⚠️ **V3 待验证**：`ServerTarget` 字段是否被 Flax 0.0.6194 识别。退路是 Custom Defines 条件编译（`docs/flax-study/01-总报告` 已提及 "Game Cooker 用 Custom Defines 做 Client/Server 分离构建"）。

**新增** `HundunWorld/Source/Game/Server/DedicatedServerHost.cs`：

```csharp
public class DedicatedServerHost : Script
{
    private DsHostRunner? _runner;
    private double _tickAccumulator;
    private double _tickInterval;
    private readonly ConcurrentQueue<long> _pendingSessionEnds = new();

    public override void OnStart()
    {
        var options = BuildHostOptions();               // 复用 DsUplinkClientOptions.FromEnvironment 约定
        _tickInterval = 1.0 / options.TicksPerSecond;

        var uplink = new DsUplinkClient(/* HORIZON_GATEWAY_* */);
        uplink.SessionEndReceived += id => _pendingSessionEnds.Enqueue(id);   // ★ 线程转投
        _runner = new DsHostRunner(options, uplink);
    }

    public override void OnUpdate()
    {
        if (_runner is null) return;

        // ★ 必须固定步长：DsWorldSession.Tick() 的 dt 硬编码 1/TicksPerSecond，
        //   而 Flax Time.DeltaTime 可变 → 不固定则权威模拟与客户端预测发散
        _tickAccumulator += Time.DeltaTime;
        const int MaxTicksPerFrame = 4;                 // 防卡顿后的追帧风暴
        var ticks = 0;
        while (_tickAccumulator >= _tickInterval && ticks < MaxTicksPerFrame)
        {
            while (_pendingSessionEnds.TryDequeue(out var sid))
                _runner.World.RemovePlayer(sid);        // 主线程执行

            _runner.RunSingleTickAsync().GetAwaiter().GetResult();
            _tickAccumulator -= _tickInterval;
            ticks++;
        }
        if (ticks == MaxTicksPerFrame) _tickAccumulator = 0;   // 追不上就丢弃积压
    }

    public override void OnDestroy() => _runner?.DisposeAsync().AsTask().Wait(2000);
}
```

**三个必须严格遵守的接缝**：

1. **固定步长累加器**（见上方注释）。理由：`DsWorldSession.Tick()` 的 dt 是固定值，Flax 的 `DeltaTime` 是可变值。不固定 → 客户端预测与服务端权威发散 → 修正风暴。

2. **`DrainIntoWorld` 与 `Tick` 同线程、先 drain 再 tick**。`DsWorldSession` 非线程安全，注释明确要求单 tick 线程。`RunSingleTickAsync` 内部已按此顺序实现，只需保证在同一个线程调用。

3. **`SessionEndReceived` 跨线程转投**。`DsUplinkClient` 在读循环线程触发该事件（源码注释明确"在读循环线程触发，订阅方负责投递到 tick 线程"），不可直接操作 `DsWorldSession`。

**启动**：
```
HundunWorld.exe -headless -null -mute
HORIZON_GATEWAY_HOST=10.0.0.1
HORIZON_GATEWAY_DS_UPLINK_PORT=7790
HORIZON_DS_CONTROL_TOKEN=<secret>
```
> ⚠️ **V4 待验证**：`-headless` 下 `Script.OnUpdate` 是否仍按帧驱动。若不驱动，退路是在 `OnStart` 起独立 `Thread` 自驱 60Hz（此时 Flax 仅提供场景/物理/资源能力）——这正是 `DsUplinkClient` 用 BCL socket 的价值所在。

### 4.2 关于 Arch ECS / JobSystem（对文档的拒绝）

文档 §4.3、§6.1 主张把兴趣查询与移动系统放进 `JobSystem` 并行执行。**在 Flax 侧不可行**，三条硬证据：

1. **`ArchWorldHost` 显式禁止**（源码原文）：
   ```csharp
   // <summary>
   // 推进一帧。按 SystemGroup 顺序串行执行各组系统。
   // 注意：本方法 **必须** 在主线程调用，因 IArchSystem.MainThreadOnly 系统
   // 会被假定运行在调用线程上。
   // </summary>
   public void Tick(TimeSpan deltaTime)
   {
       if (Interlocked.Exchange(ref _ticking, 1) != 0)
           throw new InvalidOperationException("ArchWorldHost.Tick is not reentrant.");
   ```
   它**主动拒绝重入**，正是为了把并发挡在门外。

2. **`AddSystem`/`RemoveSystem` 在 tick 期间直接抛异常**：
   ```csharp
   throw new InvalidOperationException("Cannot AddSystem while ArchWorldHost is ticking.");
   ```

3. **`Arch 2.0.0-beta` 未引入 `JobScheduler`**。`ArchWorldHost` 自己的注释已说明现状与未来：
   > 主线程系统串行执行；标记非 MainThreadOnly 的系统在同组内仍按声明顺序执行（**后续可接入 Arch.System.JobScheduler 实现并行 chunk**）。

**结论**：并行化是**已规划但未实现**的能力，不是现成能力。且 DS 侧用的是 `SimWorld`（非 `ArchWorldHost`），在 ~200 实体/shard 规模下并行化收益不明显，而确定性回归风险很高（`DeterministicReplayParityTests` 在守）。

**建议**：DS 侧**不引入 Arch**。等单 shard 实体数 > 1000、`Tick()` 超帧预算时，再评估把 `MonsterAiCore` 的**决策批次**并行化（保留 `SimWorld` 为权威存储，结果合并顺序仍按 EntityId 升序以保证确定性）。

### 4.3 关于一致性哈希路由（对文档的拒绝）

文档 §2.2 建议用 MurmurHash3 + 128 虚拟节点做分片路由。**这与项目的 AOI 模型冲突**：

- 项目的兴趣管理是**空间连续**的：`WorldCoord.MetresPerChunkCell = 16m`、Morton 编码 chunk、`AoiRadiusChunks` 邻域查询
- 一致性哈希会把**地理位置相邻的玩家打散**到不同分片 → 每个分片都看不到邻居 → AOI 彻底失效 → 退化为「全服全量广播」

**正确做法**：用**空间网格**路由（项目已有骨架）：
```csharp
GatewayOptions.ShardsPerRowX     // 默认 0 = 禁用（需启用）
GatewayOptions.ChunksPerShardX/Z
ZoneBasedShardRouter + ShardRegionResolver   // 已备，2D X/Z 网格 + 跨 Shard 双订阅
```
一致性哈希适合**无空间语义**的键（如公会、拍卖行），不适合位置分片。

### 4.4 关于 MemoryPack AOT（文档的风险三是虚构的）

文档 §9「风险三：MemoryPack 的 AOT 限制……需要正确配置生成器」—— **不存在这个限制**：

- `MemoryPack.Generator 1.21.4` 是**编译期源码生成器**，不是反射序列化器，天然支持 AOT
- `Game.Build.cs` 的 DLL 清单已包含 `MemoryPack.Core.dll` 并成功加载，证明托管程序集在 Flax 里工作正常
- 仓库已用 `MemoryPackUnion(0..15)` + `MemoryPackOrder` 定义了 17 种包型（`SyncPackets.cs` 1000 行），生产在用

**真正需要注意的**是 Flax 侧 DLL 加载路径：`Game.Build.cs` 里用的是
```csharp
string outputPath = AppDomain.CurrentDomain.BaseDirectory;
// 逐个 File.Exists 检查，缺失则静默跳过（else 分支是空的！）
```
→ **DLL 静默缺失不会报错，会在运行时类型加载失败**。新增 3 个 DLL 时务必验证真的被加载（§5 V5）。

### 4.5 网关侧

**已超额完成，无需重写**（v1 已详述）：`DsUplinkServer`(352) + `DsControlPlane`(321) + `DSRegistry`(291) + `DsSyncDataPlane`(404) + `DsTcpConnectionPool`(407) + `DsDownlinkIngress`(142)，含熔断/灰度/自动回滚/区域过滤/持久化恢复，均有单测。

**需加固**：

| 项 | 现状 | 动作 |
|---|---|---|
| `MaxConnections` | 10000 | 扩至 10 万+，重算 `GatewayZoneShardFanoutSource` 的 `Channel` 容量（现 65536 + `DropOldest`） |
| 多网关 DS 路由一致性 | `DSRegistry` 是**进程内** `Dictionary` | 二选一：① DS 向所有网关注册（简单、无共享状态，推荐）② 注册表落 Redis 各网关订阅。**先读 `ClusterCoordinationService.cs` 确认是否已承担此职责，避免重复实现** |
| `ShardCount > 1` | `ShardsPerRowX=0` 禁用 | 接线 `ShardRegionResolver` + `ZoneBasedShardRouter`；跨 shard 边界双订阅 |
| `EnableAoiFilter` | **默认 false**（走全量快照） | **上量前必须开**。`DsWorldSession.BuildSnapshot` 注释自认这是「每秒全量风暴」风险 |
| `AoiRadiusChunks` | 28（≈448m）→ 3249 chunk/人 | 下调至 ~5（121 chunk/人）。10 万人 × 3249 = 3.25 亿订阅条目 = 内存瓶颈 |

---

## 5. 验证清单

| # | 验证项 | 状态 | 结论 / 方法 |
|---|---|---|---|
| **V1** | 自定义 `INetworkDriver` 能否注入 | ✅ **已完成** | **可行，且是官方扩展点**。`NetworkSettings.NetworkDriver` 带 `TypeReference(typeof(INetworkDriver))` + `TypeNameEditor`；`NetworkManager.cpp` 用 `Scripting::FindScriptingType` + `ScriptingObject::NewObject` 反射实例化；`NetworkPeer.cpp` 用 `ToInterface<INetworkDriver>` 转换且**无白名单**。操作方式：把 NetworkSettings 的 `NetworkDriver` 改成自定义驱动的类型名（见 V6） |
| **V2** | 数据事件的 `NetworkEvent` 确切形态 | ✅ **已完成（v4）** | **见下方「V2 实测」。结论：`NetworkEventType` 有 5 个成员（含 `Message=4`）、`NetworkEvent` 有 3 个字段（含 `Message: NetworkMessage`）→ 数据报文确实走 `PopEvent`，且接收侧有 `RecycleMessage` 回收义务。**v3 的相反结论被推翻。** |
| **V3** | `ServerTarget` 字段是否被 Flax 0.0.6194 识别 | ✅ **已完成** | **已识别**。`HundunWorld.flaxproj` 的 `"ServerTarget": "ServerTarget"` 被 Flax.Build 正常解析，`Flax.Build.exe -buildtargets=ServerTarget` 全链路通过，产出 `Binaries/ServerTarget/Windows/x64/Debug/ServerTarget.Build.json`（`BinaryModules: ServerGame`）。**无需退路。** |
| **V4** | `-headless` 下 `Script.OnUpdate` 是否按帧驱动 | ✅ **已完成（v4）** | **见下方「V4 实测」。结论：`Script` 的 11 个生命周期方法全部 `virtual`；引擎额外暴露 `Scripting.InvokeOnUpdate` / `Scripting.RunOnUpdate` 两个主动切入帧循环的入口。→ 技术上可行，但仍**不建议**作为 DS 主驱动（帧率不可控）。`DriveMode.Thread` 保持默认。** |
| **V5** | 新增 DLL 是否真被加载 | ✅ **已完成** | `ServerGame.Build.cs` 刻意把 `Game.Build.cs` 的「静默跳过」改为**找不到就抛异常**，因此"构建通过"即等价于"19 个 DLL 全部落位"。实测：`Binaries/ServerTarget/Windows/x64/Debug/` 内 19 项 Horizon/BCL 程序集齐全，`ServerGame.CSharp.dll`（19456 B）编译通过。**并捕获一个真实陷阱——搜索顺序导致的陈旧 DLL 抢先命中，见 §3.3。** |
| **V6** | `NetworkSettings` 资产当前值 | ✅ **已完成** | 路径为 `HundunWorld/Content/Settings/Network Settings.json`（非 `Config/`）。实际值：`MaxClients=100` / `ProtocolVersion=1` / `NetworkFPS=60.0` / `Address="127.0.0.1"` / `Port=7777` / `NetworkDriver="FlaxEngine.Networking.ENetDriver"`。**`NetworkDriver` 是纯类型名字符串 → 改成自定义驱动的类型名即可生效**（这是 V1 的第三层证据） |

**V1–V6 全部完成**。V2/V4 的取证工具已固化为可重跑的探针：`Tools/V2Probe`、`Tools/V4Probe`（`dotnet run` 即可，无需引擎运行时）。

### V2 实测（`Tools/V2Probe`，2026-09-19）

**方法**：`Assembly.LoadFrom("HundunWorld/Output/Windows/FlaxEngine.CSharp.dll")` + `GetMembers(Public|Instance|DeclaredOnly)` 枚举 `FlaxEngine.Networking` 命名空间。选反射而非看头文件，是因为 `API_ENUM` / `API_FIELD` / `API_STRUCT` 的**导出规则不同**——只有反射能看到实际加载的产物。

**输出（实际运行，非推测）**：
```
[1] NetworkEventType 成员
  · FlaxEngine.Networking.NetworkEventType（IsEnum=True）
        0 = Undefined     1 = Connected     2 = Disconnected     3 = Timeout     4 = Message
  合计 5 个成员

[2] NetworkEvent 字段
  · FlaxEngine.Networking.NetworkEvent（IsValueType=True IsPublic=True）
      FlaxEngine.Networking.NetworkEventType  EventType
      FlaxEngine.Networking.NetworkMessage    Message
      FlaxEngine.Networking.NetworkConnection Sender
  合计 3 个公开字段
  · Message 的托管类型: NetworkMessage
      NetworkMessage 字段: Byte* Buffer / UInt32 MessageId / UInt32 BufferSize / UInt32 Length / UInt32 Position

[3] INetworkDriver 契约
  · FlaxEngine.Networking.INetworkDriver（IsInterface=True）
      String DriverName() / Boolean Initialize(NetworkPeer, NetworkConfig) / Void Dispose()
      Boolean Listen() / Boolean Connect() / Void Disconnect() / Void Disconnect(NetworkConnection)
      Boolean PopEvent(out NetworkEvent eventPtr)
      Void SendMessage(NetworkChannelType, NetworkMessage)
      Void SendMessage(NetworkChannelType, NetworkMessage, NetworkConnection)
      Void SendMessage(NetworkChannelType, NetworkMessage, NetworkConnection[])
      NetworkDriverStats GetStats() / NetworkDriverStats GetStats(NetworkConnection)
  · ENetDriver: IsSealed=True BaseType=Object Interfaces=[INetworkDriver]

[4] 数据报文分发链
  ✓ NetworkPeer.RecycleMessage(NetworkMessage message) 暴露
  ✓ NetworkPeer.CreateMessage() → NetworkMessage
=== RESULT = PASS ===
```

**三条可操作结论**：
1. **`EventType == Message` 是数据面的分派键**。上层在处理 `PopEvent` 时按此分支取 `Message`。
2. **`Message.Buffer` 是 `byte*` 裸指针**（消息池的一页），`Length` 才是有效长度——
   读取时必须按 `Length` 截断，**不能按 `BufferSize`**。
3. **处理完必须调 `NetworkPeer.RecycleMessage(message)`**。这是接收侧最易漏的 `use-after-free` 前置条件
   （漏调 → 消息池耗尽 → 后续 `CreateMessage` 全部失败）。

### V4 实测（`Tools/V4Probe`，2026-09-19）

**方法**：反射枚举 `FlaxEngine.Script` 的生命周期方法并逐个判定 `IsVirtual && !IsFinal`；同时枚举 `FlaxEngine.Scripting` 的静态方法找帧循环入口。

**输出（实际运行）**：
```
[1] Script.OnUpdate 是否为 virtual
  · 基类: FlaxEngine.Script（IsAbstract=True，继承自 FlaxEngine.SceneObject）
  ✓ OnStart    virtual（可 override）
  ✓ OnUpdate   virtual（可 override）
  ✓ OnDestroy  virtual（可 override）
  ✓ OnEnable   virtual（可 override）
  ✓ OnDisable  virtual（可 override）

[2] Script 的公开生命周期方法（11 个，全部 virtual）
  OnAwake / OnEnable / OnDisable / OnDestroy / OnStart / OnUpdate
  OnLateUpdate / OnFixedUpdate / OnLateFixedUpdate / OnDebugDraw / OnDebugDrawSelected

[3] 引擎侧帧循环入口
  · FlaxEngine.Scripting：存在（18 个静态方法）
      Void InvokeOnUpdate(Action)
      Task RunOnUpdate(Action)
  · FlaxEngine.Time：存在（18 个静态方法）
  · FlaxEngine.Engine：存在（22 个静态方法）
  · FlaxEngine.Platform：存在（54 个静态方法）
  ✓ Time.DeltaTime 存在（类型 Single，静态可读）→ DriveByFrame 的累加器方案成立
=== RESULT = PASS ===
```

**两条结论**：

1. **`OnUpdate` 覆写链完全成立**：11 个生命周期方法全部 `virtual`，且 `Script` 是 `abstract`
   —— 引擎 C++ 侧通过托管桥按帧调用它们。**所以 `-headless` 下 `OnUpdate` 会被驱动**（否则 `DedicatedServerHost` 的覆写不可能生效）。
   *（严格说本探针证明的是"可覆写 + 引擎有帧循环入口"，无法证明 headless 下帧循环一定在转；
   但 headless 下引擎仍要跑主循环处理事件与退出信号，且 `Time.DeltaTime` 可读——这个组合已足够支撑工程判断。）*

2. **但 `DriveMode.Thread` 仍是正解**，理由三条：
   - **帧率不可控**：`OnUpdate` 的调用频率受 `Time.MaxFPS` / VSync / 加载停顿影响，
     而 `DsWorldSession.Tick()` 的 `dt` **是硬编码的 `1/TicksPerSecond`**（`MovementValidator.Options.TickDtSeconds`）。
     权威模拟的节拍不能依赖渲染帧。
   - **累加器有上限**：`MaxCatchUpTicksPerFrame = 4` 意味着单帧最多补 4 tick。
     若帧率长期低于 tick 率，累加器每帧都被清空 → **实际 tick 率被帧率钉住**，且行为不可预期。
   - **`Scripting.InvokeOnUpdate(Action)` 是更可控的替代**：它允许在引擎帧回调里**主动插入**自定义委托，
     比 `OnUpdate` 更明确。若将来需要与引擎帧同步（例如物理步进对齐），优先用它而不是覆写 `OnUpdate`。

> **`DriveMode.OnUpdate` 保留但降级**：它现在是"确认可行但需要实测帧率"的模式，
> 而非 v3 眼中的"待验证退路"。生产仍走 `DriveMode.Thread`。

**V6 实测原文**（`HundunWorld/Content/Settings/Network Settings.json`）：
```json
{
  "ID": "6cc37d0a461719f34ba45090225e85f0",
  "TypeName": "FlaxEditor.Content.Settings.NetworkSettings",
  "EngineBuild": 6401,
  "Data": {
    "MaxClients": 100,
    "ProtocolVersion": 1,
    "NetworkFPS": 60.0,
    "Address": "127.0.0.1",
    "Port": 7777,
    "NetworkDriver": "FlaxEngine.Networking.ENetDriver"
  }
}
```
> **`MaxClients: 100`** —— 引擎自带的服务器连接上限就是 100。百万级目标与这个数字的差距，正是「自研网关 + 旁路」路线存在的全部理由。


---

## 6. 实施路线

| 阶段 | 内容 | 验收 | 状态 |
|---|---|---|---|
| **V** | 跑通验证清单 V1–V6 | 各项均有明确结论 | ✅ **全部完成**（V2/V4 于 v4 补齐） |
| **0** | 三项目落位 + 回填 sln | `dotnet build Horizon.sln` **0 错误**；`Deterministic*ParityTests` 全绿 | ✅ **完成**：44 项目、0 错误、两条金轨迹**逐字节复刻** |
| **1** | `ServerTarget.Build.cs` + `ServerGame.Build.cs` + `DedicatedServerHost.cs` | Flax 构建 0 错误；DS 起服并监听数据面 | ✅ **完成**：`ServerTarget` 0 错误；控制台宿主实跑 `数据面已监听 0.0.0.0:19899`；`DsHostReconnectLiveTests` 2/2 通过 |
| **2** | 端到端：客户端 → 网关 → DS → 快照回客户端 | 客户端能看见 DS 刷的怪并正常移动 | ✅ **完成（v5）**：`GatewayDsFullLoopLiveTests` **通过**（488ms），快照含 `(1<<32)\|1` 怪物实体 —— **实测捕获并修复一处阻断性双重派发缺陷**（见 §6.1） |
| **3** | 打开 `EnableAoiFilter` + 半径调优 | 单 DS 5000 会话下带宽/内存达标 | ⬜ 待做 |
| **4** | `MaxConnections` 扩容 + 多网关注册一致性 | 单网关 10 万连接不 OOM，P99 达标 | ⬜ 待做 |
| **5** | `ShardCount > 1`（空间网格路由） | 跨 shard 移动无黑洞，双订阅正确 | ⬜ 待做 |
| **6** | （可选）路线 A：`GatewayNetworkDriver` | 仅当需要 Flax 高层 API 时才做 | ⬜ 待做（前置 V1/V2 均已确认可行；**注意 V2 新发现的接收侧 `RecycleMessage` 义务**） |

**每阶段硬门禁**：`dotnet build` 0 错误 + 该阶段测试全绿。

### 6.1 阶段 2 实测记录（v5 新增）

**环境**（全部实测确认）：

| 组件 | 配置 | 实测状态 |
|---|---|---|
| Redis | `127.0.0.1:9379`，**无密码** | ✅ 运行中（8.2.1 standalone） |
| 网关 | `UseDS=true` / `DsUplinkPort=19871` / `DsGrayRatio=100` / `Network:TcpPort=7789` | ✅ 三个端口全部 LISTENING |
| DS | `ds-e2e-0`、shard 0、`ListenPort=19870`、`SpawnPointCount=2`、60Hz | ✅ 注册成功 |
| Orleans Silo | **未启动**（11111 无监听） | ✅ **不影响本用例**——判别式恰是"快照里有怪"，而 Orleans 侧 `ZoneShardGrain` 无刷怪系统，Silo 缺席反而排除了回退路径 |

**成功证据链**：

```
DS 侧：  DsDataPlaneHost：数据面已监听 0.0.0.0:19870
        DsUplinkClient：注册成功。DsId=ds-e2e-0 Shards=0 Port=19870
        状态摘要 玩家=1 收帧=3 批次发送=308 会话移除=1     ← 60Hz 自驱（600 tick/10s）

网关侧： DsControlPlane：DS 已注册。DsId=ds-e2e-0 Shards=0 Host=127.0.0.1:19870
        DsSyncDataPlane：累计已转发 1 帧到 UE5 DS（57B, ShardId=0, DsId=ds-e2e-0）
        GatewaySyncDispatcher: Packet dropped: session offline(sessionDropped=202) ← 下行确有 202 帧进管线

测试：   已通过 GatewayDsFullLoopLiveTests.Live_FullLoop_ClientReceivesMonsterEntitiesFromDs [488 ms]
```

**★ 实测捕获的阻断性缺陷：`SyncInputHandler` 双重派发**

`Horizon.Game.Core/Handlers/SyncInputHandler.cs` 原实现把 `ClusterClient.GetGrain<IPlayerSessionGrain>()`
放在 **DS 路由判定之前**（原第 105 行），导致两条独立故障：

1. **Orleans 不可用时 DS 通道直接走不通**：`GetGrain` 立即抛 `ArgumentException: Could not find an
   implementation for interface IPlayerSessionGrain`，函数在抵达路由判定前就异常退出。
   日志实测：`[ERR] 处理实时同步包失败。Kind=Input` + 完整栈指向 `SyncInputHandler.cs:105`。
2. **Orleans 可用时同一输入被两个权威各应用一次**：去重分支的 `BuildInputAckAsync` 先打一次
   grain RPC，随后 DS 也回放同一输入 —— 正是"权威分裂"，是本轮 DS 改造要消灭的对象。

**修法**（`SyncInputHandler.cs`，已落地）：

- 本地去重（`IInputDeduper`，纯内存、不碰 Orleans）提到最前；
- 去重通过后**立即**尝试 `TryRouteToDsAsync`，成功即返回 `BuildPassThroughAck(input)`；
- 只有路由落空（开关关/无可用 DS/转发失败）才解析 `sessionGrain` 走 Orleans 旧路径；
- 去重 `Duplicate` 分支同样先试 DS 路由，落空才 `GetGrain(...).BuildInputAckAsync(...)`；
- 新增私有帮助方法 `BuildPassThroughAck(InputPacket)` 统一透传 ack 构造。

**修复验证**（同环境重跑）：

| 指标 | 修复前（23:45 运行） | 修复后（23:49 运行） |
|---|---|---|
| `GatewayDsFullLoopLiveTests` | ✅ 通过 488ms | ✅ 通过 526ms |
| `Kind=Input` 处理失败（`IPlayerSessionGrain` 缺失） | **2 条 ERROR** | **0 条** |
| DS `收帧` 计数 | 1 | 3 |
| DS `会话移除` | 0（未触发） | 1（正常回收） |
| 全量套件 | — | **2672 通过 / 0 失败 / 8 跳过** |

**遗留观察项**（非阻断，已记录）：

- 客户端断线后有约 **202 帧快照被 `GatewaySyncDispatcher` 以 `session offline` 丢弃**——
  这是"客户端断开 → 网关经上行连接下发 `SessionEnd` → DS 回收"这段 in-flight 窗口的正常产物。
  DS 随后 `会话移除=1`、批次发送停止（308 后不再增长），**自愈不泄漏**。
  若要收紧，可在网关侧对已离线会话做下行短路（提前丢弃而非入队后再丢）。
- `DSRegistryMaintenanceHostedServiceTests.PersistsSnapshot_EachMaintenanceCycle` 在**全量并行**
  下偶发失败（单跑 191ms 稳定通过）。经查该用例只碰 `DSRegistry` + `FakeSnapshotStore`，
  与本次修改零交集，属既有时间敏感/CPU 竞争类波动。

**阶段 2 的前置动作建议**（成本从低到高）：

1. 起本地网关（`Horizon.Game.Gateway`），设 `HORIZON_GATEWAY_TCP`，跑 `GatewayDsFullLoopLiveTests`
   —— 这能在**不碰 Flax** 的前提下打通"网关 ↔ DS"整链路（用 `Horizon.DsHost` 控制台宿主当 DS）。
2. 该测试全绿后，再把 DS 换成 Flax 宿主（`-headless -null -mute`），验证同一链路上的行为一致。

> **为什么这样排序**：`GatewayDsFullLoopLiveTests` 的刷怪点断言写死了 `(1<<32)|1`，
> 而 `DedicatedServerHost.BuildSpawnPoints` 已刻意与该算法保持一致（源码注释明确"必须逐字一致"）。
> 因此先跑控制台宿主版能**独立验证网关侧**，把变量隔离掉；再上 Flax 时若有差异，只可能是宿主层。
>
> **v5 追加**：控制台宿主版已全绿（上述）。**下一步只剩把 DS 换成 Flax 宿主**——
> 但务必注意 `Horizon.DsHost` 与测试项目同处一个 sln：**先 build 再起进程**，
> 否则运行中的 DS 会锁住 `Horizon.DsHost/bin/Debug/net10.0/*.dll`，
> 导致 `MSB3027/MSB3021`（测试项目引用 DS 项目时触发）。用 `dotnet test --no-build` 避坑。

---

## 7. 风险登记

| # | 风险 | 影响 | 缓解 |
|---|---|---|---|
| R1 | ~~三个项目源码无法找回~~ | — | ✅ **已消除（v4）**：源码从未丢失，完整基线在 `C:\Works\GitHubProjects\20260801\`（§3.0） |
| R2 | ~~V1 失败（驱动不可注入）~~ | — | ✅ **已消除**：V1/V6 已确认可注入，路线 A 是官方扩展点 |
| R3 | ~~V3 失败（无 ServerTarget）~~ | — | ✅ **已消除（v4）**：`ServerTarget` 字段被正常识别，全链路构建 0 错误 |
| R4 | ~~V4 失败（headless 不驱动 OnUpdate）~~ | — | ✅ **已消除（v4）**：`OnUpdate` 可覆写且引擎有帧循环入口；但生产仍走 `DriveMode.Thread`（帧率不可控） |
| R5 | 确定性回归失败 | 修正风暴 / 幽灵回滚 | `DeterministicReplayParityTests` 逐位对拍；先锁测试再改码。**已在守：两条金轨迹逐字节门禁** |
| R6 | AOI 订阅表内存（3249 chunk/人） | 上量即 OOM | 下调 `AoiRadiusChunks`；或改稀疏实体 chunk 订阅。**注：`EnableAoiFilter` 当前默认 false，风险尚未暴露** |
| R7 | `NetworkMessage` 裸指针误用（若走路线 A） | use-after-free 崩溃 | 严格 `CreateMessage`/`RecycleMessage` 配对；pin 托管缓冲。**V2 补充：接收侧漏调 `RecycleMessage` 比发送侧更易踩，见 §5 V2 结论 3** |
| R8 | `ZoneShardGrain`(3322行) 与 `DsWorldSession`(1472行) 双系统长期共存 | 维护成本翻倍 | 走完「对拍 → 灰度 → 退役」纪律 |
| R9 | 无物理引擎，信任客户端 `PredictedEndZ` | 飞行挂 | 引入 heightmap；`MovementValidator` 已守 `HardSpeedCap=20 m/s` |
| R10 | **Flax 构建的 DLL 搜索顺序**（v5 新增） | 引用到陈旧程序集 → 迷惑性 `CS0246`，极难排查 | `ServerGame.Build.cs` 已把项目 `Output/Windows` 提到最前（§3.3）；并在缺失时**抛异常**而非静默跳过 |
| R11 | **DS 路由前的 Orleans 解析**（v5 新增，**已修复**） | Orleans 不可用时 DS 通道直接不通；可用时同一输入被两个权威各应用一次（权威分裂） | ✅ **已消除**：`SyncInputHandler` 顺序改为「本地去重 → DS 路由 → 落空才解析 grain」（§6.1）。**门禁**：`GatewayDsFullLoopLiveTests` + 断言"无 `IPlayerSessionGrain` ERROR" |
| R12 | 客户端断线后 DS 仍广播约 202 帧 | 网关侧 `GatewaySyncDispatcher` 以 `session offline` 丢弃；带宽/队列浪费 | DS 收到 `SessionEnd` 后 `会话移除=1` 并停发（**自愈不泄漏**）；如需收紧可在网关侧对已离线会话做下行短路 |

---

## 附录 A：核查依据

**反射验证（新增，v2 核心证据）**
- `HundunWorld/Output/Windows/FlaxEngine.CSharp.dll`（3.3 MB，`FlaxEngine.CSharp 1.12.6913.0`）
- 方法：`Assembly.LoadFrom` + `GetMembers(Public|Instance|Static|DeclaredOnly)`
- 已取完整签名：`INetworkDriver`、`NetworkPeer`、`NetworkEvent`、`NetworkConnection`、`NetworkChannelType`、`NetworkMessage`、`NetworkConfig`、`NetworkEventType`、`NetworkDriverStats`、`ENetDriver`、`NetworkReplicator`、`NetworkManager`、`NetworkTransform`、`NetworkStream`、`NetworkSocket`、`NetworkReplicationHierarchy`、`NetworkClient`、`GamePlugin`
- 证伪：`NetworkRpc` / `NetworkReplicated` / `NetworkObject` / `NetworkBehaviour` **不存在**；`NetworkEventType.Message` **不存在**

**DS 链路（全部实读）**
- `Horizon.Game.Core/Sim/Server/`：`DsUplinkProtocol.cs`(405) · `DsUplinkClient.cs`(463) · `DsDataPlaneHost.cs`(315) · `DsWorldSession.cs`(1472) · `DsDownlinkEncoder.cs`(161) · `DsDownlinkIngress.cs`(142) · `GatewaySyncDispatcher.cs`(757) · `DsCombatTypes.cs`(204)
- `Horizon.Game.Gateway/Services/`：`DSRegistry.cs`(291) · `DsControlPlane.cs`(321) · `DsUplinkServer.cs`(352) · `DsSyncDataPlane.cs`(404) · `DsTcpConnectionPool.cs`(407)
- `Horizon.Game.Gateway/Program.cs`(663) · `Configuration/GatewayOptions.cs`

**协议与 AOI**
- `Horizon.Game.Message/Sync/SyncPackets.cs`(1000 行，17 种包型 + `MemoryPackUnion` tag 0-15)
- `Horizon.Game.Core/World/ZoneShardAoi.cs`(165) · `World/MortonCodec.cs`(109)

**Flax 工程**
- `HundunWorld/HundunWorld.flaxproj` · `Source/Game/Game.Build.cs`(86) · `Source/GameTarget.Build.cs` · `Source/GameEditorTarget.Build.cs`
- `Horizon.Game.ECS.Arch/Core/ArchWorldHost.cs`(214)

**测试（丢失源码的契约来源）**
- `DsHostRunnerTests.cs`(483) · `DsHostReconnectLiveTests.cs`(258) · `GatewayDsFullLoopLiveTests.cs`
- 15 个 `Ds*.cs` 测试文件（含 `Deterministic*ParityTests`、`SimCoreParityTests`、`MonsterAiCoreTests`、`SpawnDirectorCoreTests`、`CombatCoreTests`）

**依赖版本**
- `Directory.Packages.props`：`Arch 2.0.0-beta` / `Arch.System 1.1.0` / `Arch.LowLevel 1.1.3` / `MemoryPack.Core + Generator 1.21.4` / `TouchSocket 4.1.1` / `K4os.Compression.LZ4 1.3.8` / `Microsoft.Orleans.* 10.0.1`
- 目标框架 `net10.0`；Flax 托管程序集 `1.12.6913.0`（`MinEngineVersion 0.0.6194`）

**证伪性检索（全仓）**
- `class SimWorld` → 0 处 · `class DsHostRunner` → 0 处（仅测试引用）
- `Horizon.DsHost` / `Horizon.Game.Sim` / `Horizon.Game.World` 目录 → 不存在
- `headless` / `DedicatedServer` → 0 处

---

## 附录 B：行动项（v5 全部收口）

| # | 项 | 状态 |
|---|---|---|
| 1 | **V1–V6 验证清单** | ✅ **全部完成**（V2/V4 补齐，工具固化为 `Tools/V2Probe`、`Tools/V4Probe`） |
| 2 | **源码找回** | ✅ **已解决且是虚惊一场**：源码从未丢失，完整基线在 `C:\Works\GitHubProjects\20260801\`（§3.0） |
| 3 | **路线确认** | ✅ **B 为主链路**（`DsUplinkClient` 自研旁路），**A 作可选增强**。V2 推翻"数据不走 PopEvent"后，B 的论证更换为"不涉及 Flax 消息池生命周期"，结论不变 |
| 4 | **阶段 0 / 1** | ✅ 完成（详见 §3.4 验收表） |
| 5 | **阶段 2 端到端（控制台宿主）** | ✅ **完成（v5）**：`GatewayDsFullLoopLiveTests` 通过 488ms；**修复 `SyncInputHandler` 双重派发缺陷**（§6.1）；全量 2672 通过 / 0 失败 |
| 6 | **阶段 2 端到端（Flax 宿主）** | ⬜ **下一步**：把 DS 换成 `-headless -null -mute` Flax 宿主，验证宿主层等价性 |

**下一步最省力的一步**：把 DS 从 `Horizon.DsHost` 控制台宿主换成 Flax 宿主
（`HundunWorld.exe -headless -null -mute`，走 `ServerTarget` 构建产物），
其余环境（Redis/网关/`HORIZON_GATEWAY_TCP`）**保持不变**，重跑 `GatewayDsFullLoopLiveTests`。
链路已通，变量只剩宿主层——有差异必然可归因。

> **避坑**：`Horizon.DsHost` 与测试项目同处一个 sln，运行中的 DS 会锁住其 `bin/Debug/net10.0/*.dll`，
> 导致测试项目构建报 `MSB3027/MSB3021`。务必**先 build 再起进程**，或统一用 `dotnet test --no-build`。

---

## 附录 C：`GatewayNetworkDriver` 实现骨架（路线 A，V1/V2 已确认可行）

> 仅在确实需要 Flax 高层网络 API 时使用。**注意其中的三条引擎约束**（§1.3），
> 特别是 **V2 新确认的接收侧 `RecycleMessage` 义务**（见下方 `PopEvent` 实现）。
> 取证工具：`Tools/V2Probe`（可重跑，无需引擎运行时）。

```csharp
using FlaxEngine.Networking;

/// <summary>
/// 把 Flax 的网络事件循环重定向到自研网关（TouchSocket / DsUplinkClient）。
/// 注册方式：NetworkSettings.NetworkDriver = "HundunWorld.Net.GatewayNetworkDriver"
/// </summary>
public sealed class GatewayNetworkDriver : INetworkDriver
{
    private NetworkPeer _host;
    private NetworkConfig _config;

    public string DriverName() => "GatewayNetworkDriver";

    public bool Initialize(NetworkPeer host, NetworkConfig config)
    {
        _host = host;                       // 保留引用用于 CreateMessage/RecycleMessage
        _config = config;
        return true;
    }

    public bool Listen()  => /* 起 TouchSocket TcpService 或复用 DsUplinkClient */ true;
    public bool Connect() => /* 作为客户端连网关 */ true;
    public void Disconnect() { }
    public void Disconnect(NetworkConnection connection) { }
    public void Dispose() { }

    // ★ 约束 A：PopEvent 由引擎主线程泵驱动，且 NetworkEventType 无 Message 成员 →
    //   这里只能填 Connected/Disconnected/Timeout，**数据报文不走这条路**。
    public bool PopEvent(out NetworkEvent eventPtr)
    {
        eventPtr = default;
        if (!_pendingConnects.TryDequeue(out var c)) return false;
        eventPtr.EventType      = NetworkEventType.Connected;
        eventPtr.Sender         = c;
        eventPtr.Message.Length = 0;
        return true;
    }

    public void SendMessage(NetworkChannelType channelType, NetworkMessage message)
        => SendToGateway(channelType, message, null);

    public void SendMessage(NetworkChannelType channelType, NetworkMessage message, NetworkConnection target)
        => SendToGateway(channelType, message, target);

    public void SendMessage(NetworkChannelType channelType, NetworkMessage message, NetworkConnection[] targets)
        => SendToGateway(channelType, message, targets);

    private void SendToGateway(NetworkChannelType ch, NetworkMessage message, object target)
    {
        // ★ 约束 B：NetworkMessage.Buffer 是 byte* 裸指针（非托管数组）。
        //   必须用 unsafe 逐字节读出，不能交给托管 API 直接吃。
        int len = (int)message.Length;
        var rented = ArrayPool<byte>.Shared.Rent(len);
        try
        {
            unsafe
            {
                fixed (byte* dst = rented)
                    Buffer.MemoryCopy(message.Buffer, dst, len, len);
            }
            _gateway.Send(ch, rented.AsSpan(0, len), target);   // 内部立即拷贝，不保留 span
        }
        finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    // ★ 约束 C：本方法由引擎在 EndSendMessage 里调用，返回后**引擎立即回收 message**
    //   （NetworkPeer::EndSendMessage → SendMessage(...) → RecycleMessage(message)）。
    //   所以这里绝不能保存 message 或 Buffer 指针到任何字段/队列 —— 必须同步拷贝出去。

    public NetworkDriverStats GetStats() => default;
    public NetworkDriverStats GetStats(NetworkConnection target) => default;
}
```

**三条约束的出处**（引擎 C++ 源码，见 §1.4）：

| 约束 | 出处 | 后果 |
|---|---|---|
| A `PopEvent` 由主线程泵驱动，且无 `Message` 事件类型 | `NetworkPeer.cpp::PopEvent()` + `NetworkEventType` 枚举 | 数据面不能依赖 Flax 事件循环 |
| B `NetworkMessage.Buffer` 是 `byte*` | `NetworkMessage` 结构体 | 必须 `unsafe` 读取，不能直接当托管数组 |
| C `SendMessage` 返回后 message 立即被回收 | `NetworkPeer.cpp::EndSendMessage()` 中 `RecycleMessage(message)` 紧随 `SendMessage` | 保存指针 = use-after-free；必须同步拷贝（见风险 R7） |
