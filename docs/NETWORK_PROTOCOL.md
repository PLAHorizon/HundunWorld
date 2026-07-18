# HundunWorld 网络同步协议规范

> 本文档基于已落地协议代码逆向梳理，作为网络同步迁移 spec 的阶段 0 基线。
> 所有事实均与当前代码库对齐，后续协议演进以本文档为参照。

## 1. 概述（协议版本 v5）

HundunWorld 同步协议（SyncPacket）是独立于 `MessageUnion`（用户主动请求/响应消息）的系统自主同步消息体系，承担快照、输入、事件、世界 diff 等高频实时数据的传输。

**当前协议版本：`SyncProtocolVersion.Current = 5`**

版本演进历史：

| 版本 | 变更摘要 |
| --- | --- |
| v1 | 初版（PR1/PR2 引入）：Handshake / Snapshot / Input / Event 四类包。 |
| v2 | P1-a 扩展：新增 `WorldChunkDiffPacket` / `WorldPatchManifestPacket` / `InputAckPacket` / `ReconnectResumePacket`；在 `HandshakePacket` 与 `SnapshotPacket` 上引入版本向量字段；旧客户端首次握手会被服务器拒绝（不兼容）。 |
| v3 | 修复服务端握手响应类型：服务端 `SyncPacketHandler.HandleHandshakeAsync` 改为返回 `HandshakePacket`（回显 `LocalCharacterId` / `InitialClientTick`），使客户端 `HandshakeReceived` 事件能正确触发。 |
| v4 | 修复 `SyncPacketHandler` 单例下 `_characterId` 被多连接共享覆盖的 bug：`InputPacket` 新增 `CharacterId` 字段，服务端直接从输入包读取角色 ID，不再依赖握手时缓存的实例字段。 |
| v4+ | 交互同步扩展：新增 `InteractionSyncPacket`（Kind=9）/ `InteractionSyncComponent` / `SyncEventKind` 交互事件（InteractStart=7/InteractEnd=8/InteractStolen=9）；复用 `WorldChunkDiffPacket` 信封承载（新增 `PayloadType` 字段区分内部类型）；`InteractionStateBits` 常量类统一状态位编码。 |
| v5 | 阶段 B/C/D 同步协议扩展：<br/>• 新增 `MovementStateAuthComponent`（`MovementMode` + `VelocityXZ_X/Y` + `IsGrounded`）<br/>• 新增 `AnimationStateAuthComponent`（`AnimMontageId` + `AnimInstanceId` + `PlayRate` + `TimePosition` + `IsLooping`）<br/>• `EntityStateAuthComponent` 扩展（`Mana`/`MaxMana`/`Level`/`Exp`/`Stamina`/`MaxStamina`，[Id] 从 3 起连续编号，旧字段不变）<br/>• 新增 `SceneObjectSyncPacket`（`SyncPacketKind=10`，MemoryPackUnion 编号 9）<br/>• 新增 `SceneObjectStateAuthComponent` + `SceneObjectTransformComponent`<br/>• `SyncProtocolVersion.Current = 5`，服务端 `HandleHandshakeAsync` 严格拒绝旧版本<br/>• `InputPacket` 冗余重传（客户端未确认队列容量 64 + 落后 5 tick 触发重传）+ 服务端 per-characterId 去重<br/>• `SnapshotPacket` 增量压缩（`BaselineTick` 非 0 时为 delta 帧，60 tick 强制全量）<br/>• `WorldChunkDiffPayloadType` 新增 `SceneObjectSync = 4` |

版本号递增规则：每次 `SnapshotPacket` / `InputPacket` / 组件 schema 变更时递增。客户端 `HandshakePacket` 携带本地版本，服务器据此拒绝不兼容连接。

技术依赖：
- **MemoryPack**：二进制序列化（主体）。
- **Orleans `[GenerateSerializer]`**：Orleans Grain 间序列化（与 MemoryPack 双重标注）。
- **K4os.Compression.LZ4**：快照压缩。

## 2. 帧格式

每个 `SyncPacket` 在 TCP 流上以定长头 + 变长 payload 的帧格式传输，与 TouchSocket `FixedHeaderPackageAdapter` 对齐：

```
偏移      长度    字段              说明
[0..1]    1B      Kind              包种类（与 SyncPacketKind 一致，便于 fast-path 路由）
[1..2]    1B      Compression       压缩标记（0 = none, 1 = lz4）
[2..6]    4B      PayloadLength     payload 字节数（i32, 小端序）
[6..]     N B     Payload           MemoryPack 序列化字节（或压缩后字节）
```

常量（定义于 `SyncPacketCodec`）：

| 常量 | 值 | 说明 |
| --- | --- | --- |
| `FrameHeaderSize` | 6 | 帧头固定长度 |
| `SnapshotCompressionThreshold` | 256 | Snapshot payload 超此字节数才启用 LZ4 压缩 |
| `MaxDecompressedSize` | 4 \* 1024 \* 1024（4MB） | 解压上限，防解压炸弹 |

`PayloadLength` 为 4 字节小端序有符号整数（i32），编解码时按 `& 0xFF` / `<< 8` 逐字节拼装。

`Kind` 字段是冗余的（MemoryPack union 本身能区分类型），写入帧头第一个字节便于在不解码 union 的情况下做 fast-path 路由。

## 3. 包类型表（SyncPacketKind）

`SyncPacketKind` 枚举定义帧头 Kind 字段的取值：

| Kind 值 | 枚举名 | 方向 | 用途 | 关键字段 |
| --- | --- | --- | --- | --- |
| 0 | `Unknown` | — | 保留/未知 | — |
| 1 | `Handshake` | C→S | 握手：告知协议版本与本地玩家身份 | `LocalCharacterId`, `InitialClientTick` |
| 2 | `Snapshot` | S→C | 快照：baseline（全量）或 delta（仅变化） | `ServerTick`, `BaselineTick`, `Deltas[]` |
| 3 | `Input` | C→S | 输入：定频上行，永不压缩以保延迟 | `ClientTick`, `InputBits`, `LookYaw`, `LookPitch`, `MoveX`, `MoveY`, `CharacterId` |
| 4 | `Event` | S→C | 离散事件：技能命中/伤害/死亡/特效，与 snapshot 解耦走可靠通道 | `ServerTick`, `Events[]` |
| 5 | `WorldChunkDiff` | S→C | 世界 voxel/prefab diff 流（按 ChunkCell 推送） | `ChunkMortonKey`, `DiffSeqStart`, `DiffSeqEnd`, `BaselineVersion`, `WorldPatchVersion`, `Payload`, `PayloadCompressed` |
| 6 | `WorldPatchManifest` | S→C | 世界补丁清单/版本向量协商（握手后下发） | `BaselineVersion`, `WorldPatchVersion`, `ManifestUrl`, `ManifestSha256`, `PatchCutoverDiffSeq` |
| 7 | `InputAck` | S→C | 服务器对客户端 input 的确认（携带 LastProcessedClientTick，用于 reconciliation） | `LastProcessedClientTick`, `ServerTick`, `EchoClientTick` |
| 8 | `ReconnectResume` | C→S | 断线重连 resume 握手（携带 lastApplied tick / diff seq / patch version） | `LocalCharacterId`, `LastAppliedSnapshotTick`, `LastAppliedDiffSeq`, `BaselineVersion`, `WorldPatchVersion` |
| 9 | `InteractionSync` | S→C（及 C→S 上行意图） | 交互槽状态同步（占用/释放/抢占）+ 客户端交互意图上行 | `SlotIdx`, `InteractableId`, `InteractorId`, `StateBits`, `ServerTick` |

### 3.1 各包字段详解

#### HandshakePacket（Kind=1）
客户端连接成功后第一个包。
- `LocalCharacterId`（ulong）：本地玩家控制的服务器实体 ID，服务器据此把 input 路由到对应 grain。
- `InitialClientTick`（long）：客户端 tick 起始值，用于服务器对齐 reconciliation 时间轴。

#### SnapshotPacket（Kind=2）
- `ServerTick`（long）：本包对应的服务器 tick。
- `BaselineTick`（long）：基线 tick；为 0 表示本包自身为 baseline；否则客户端必须先持有 BaselineTick 对应的 baseline 才能解码（缺失则请求重传）。
- `Deltas`（`EntityDelta[]`）：本帧实体变更。

`EntityDelta` 结构：
- `EntityId`（ulong）
- `Kind`（`EntityDeltaKind`）：Spawn=1 / Update=2 / Despawn=3
- `Identity`（`NetworkIdentityAuthComponent?`）：身份信息（仅 Spawn/全量时有效）
- `Transform`（`AuthTransformComponent?`）：变换（变更时携带）
- `State`（`EntityStateAuthComponent?`）：状态（变更时携带）

#### InputPacket（Kind=3）
- `ClientTick`（long）：客户端 tick / 输入序号。
- `InputBits`（uint）：位掩码（移动方向、跳跃、技能 1..N）。bit0=跳跃，bit3=轻功跳跃。
- `LookYaw`（float）：视角朝向（Yaw 弧度）。
- `LookPitch`（float）：视角俯仰（Pitch 弧度）。
- `MoveX` / `MoveY`（float）：移动输入（-1..1）。
- `CharacterId`（ulong）：v4 新增。发送该输入的本地玩家角色 ID。服务端 `SyncPacketHandler` 为单例，无法安全地在实例字段中缓存每连接的 characterId，因此由客户端在每个 InputPacket 中显式携带。

#### EventPacket（Kind=4）
- `ServerTick`（long）：事件发生时的服务器 tick。
- `Events`（`SyncEvent[]`）：事件序列。

`SyncEvent` 结构：
- `Kind`（`SyncEventKind`）：SkillCast=1 / Damage=2 / Death=3 / Vfx=4 / Sfx=5 / Pickup=6 / InteractStart=7 / InteractEnd=8 / InteractStolen=9
- `SourceEntityId`（ulong）：事件源实体（攻击者/施法者）
- `TargetEntityId`（ulong）：事件目标实体（受害者/拾取者）
- `IntValue`（int）：整型主参数（技能 ID / 伤害值）
- `FloatValue`（float）：浮点参数（暴击倍率 / 持续时间）
- `Payload`（byte[]?）：额外二进制载荷（复杂 Payload 可双方约定后用 MemoryPack 二次序列化嵌入）

> 交互相关事件（InteractStart/InteractEnd/InteractStolen）由 `EventApplySystem` 消费，但交互状态变更走专用 `InteractionSyncPacket` 通道，不污染快照流。`InteractStolen` 事件携带槽位归属信息，C++ 侧 `HandleBridgeInteractionEvent` 会校验槽位归属。

#### WorldChunkDiffPacket（Kind=5）
- `ChunkMortonKey`（ulong）：目标 ChunkCell 的 Morton 键（24 位地址压入 ulong）。
- `DiffSeqStart`（long）：本批 diff 起始序号（含）。
- `DiffSeqEnd`（long）：本批 diff 终止序号（含）；客户端落盘后应更新到该值。
- `BaselineVersion`（int）：该 chunk 当前所基于的 baseline 版本。
- `WorldPatchVersion`（int）：该 chunk 当前累积的 patch 版本。
- `Payload`（byte[]）：已序列化（且可能 LZ4 压缩）的 voxel/prefab op 序列。内部 schema 由 `Horizon.Game.World` 定义，本层仅做透传。
- `PayloadCompressed`（bool）：当 Payload 为 LZ4 压缩流时为真。

#### WorldPatchManifestPacket（Kind=6）
握手完成后下发，告知客户端"当前服务器接受的世界版本向量"及需从启动器（GengDi）补齐的 chunk patch 列表。
- `BaselineVersion`（int）：当前权威 baseline 版本（与游戏二进制版本一一对应，由 GengDi 通过 .pak 投递）。
- `WorldPatchVersion`（int）：当前权威 worldPatch 版本（每次正式发布时由 `Horizon.Tools.WorldPatchBuilder` 累加）。
- `ManifestUrl`（string）：清单根 URL（CDN 入口）。
- `ManifestSha256`（string）：清单文件 SHA256（hex），客户端下载后须校验一致。
- `PatchCutoverDiffSeq`（long）：在线增量与本地补丁的边界 diff 序号；客户端 ≥ 本值则可纯走在线 diff。

#### InputAckPacket（Kind=7）
与 `SnapshotPacket` 解耦，可在两次 snapshot 之间高频下发以缩短 reconciliation 窗口。
- `LastProcessedClientTick`（long）：服务器最近一次处理过的客户端 tick（含），用于客户端 `ReconciliationSystem` 丢弃已确认的预测输入并按需 rewind。
- `ServerTick`（long）：服务器当前 tick（与 `SnapshotPacket.ServerTick` 同义）。
- `EchoClientTick`（long）：RTT 估算需要的 echo（客户端 input 中的 ClientTick），可为 0 表示未携带。

#### ReconnectResumePacket（Kind=8）
服务器据此决定"继续推增量 / 强制 baseline 重传 / 让客户端先去补 worldPatch"。
- `LocalCharacterId`（ulong）：本地玩家角色 ID。
- `LastAppliedSnapshotTick`（long）：客户端最后已应用的 snapshot tick。
- `LastAppliedDiffSeq`（long）：客户端最后已应用的世界 diff 全局序号（跨 chunk 单调递增的 high-water mark）。
- `BaselineVersion`（int）：客户端本地 baseline 版本（来自 .pak）。
- `WorldPatchVersion`（int）：客户端本地已套用的 worldPatch 版本（来自 GengDi 的 WorldData/）。

#### InteractionSyncPacket（Kind=9）
交互槽状态同步与客户端交互意图上行复用同一包类型。
- `SlotIdx`（int）：交互槽位索引。
- `InteractableId`（long）：可交互对象 NetworkId（经 NetworkId 注册表解析到 UE5 `UInteractableComponent`）。
- `InteractorId`（long）：交互发起者 NetworkId（解析到 `UInteractionComponent`）。
- `StateBits`（byte）：状态位编码（见下方 InteractionStateBits 说明）。
- `ServerTick`（long）：服务器 tick。

`InteractionStateBits` 状态位编码（单一事实源：`Horizon.Game.Message\Sync\InteractionStateBits.cs`）：

| 方向 | 位 | 常量 | 值 | 说明 |
| --- | --- | --- | --- | --- |
| 下行（S→C） | bit0 | `Start` | 0x01 | 交互开始 |
| 下行（S→C） | bit1 | `End` | 0x02 | 交互结束 |
| 下行（S→C） | bit2 | `Stolen` | 0x04 | 交互被抢占 |
| 上行（C→S） | bit7 | `RequestStartFlag` | 0x80 | 请求开始交互 |
| 上行（C→S） | bit6 | `RequestStopFlag` | 0x40 | 请求停止交互 |

- 下行状态位掩码 `StateMask = 0x07`，上行意图位掩码 `IntentMask = 0xC0`。
- 说明：同一包类型复用为上行意图载体，服务端 `HandleInteractionIntent` 通过高位区分意图位与下行状态位。

## 4. 编解码流程（SyncPacketCodec）

`SyncPacketCodec` 是 `SyncPacket` 的统一编解码器，所有同步包的序列化/反序列化必须经过本类。

### 4.1 Encode 流程

```
SyncPacket packet
   │
   ▼ 1. MemoryPack 序列化
rawPayload = MemoryPackSerializer.Serialize<SyncPacket>(packet)
   │
   ▼ 2. 决定是否压缩
shouldCompress = (packet.Kind == Snapshot) && (rawPayload.Length >= 256)
   │
   ├─ shouldCompress = true:
   │    maxOut = LZ4Codec.MaximumOutputSize(rawPayload.Length)
   │    compressed = ArrayPool.Rent(maxOut)
   │    written = LZ4Codec.Encode(rawPayload → compressed)
   │    若 written > 0 且 written < rawPayload.Length:
   │        payload = new byte[written]; 拷贝; compression = Lz4
   │    否则: payload = rawPayload; compression = None  (压缩反而变大则放弃)
   │
   └─ shouldCompress = false:
        payload = rawPayload; compression = None
   │
   ▼ 3. 组装帧
frameLength = FrameHeaderSize(6) + payload.Length
frame = ArrayPool.Rent(frameLength)
frame[0] = (byte)packet.Kind
frame[1] = (byte)compression
frame[2..6] = payload.Length (小端序 i32)
frame[6..] = payload
   │
   ▼ 返回 (frame, frameLength)
   注意：frame 从 ArrayPool 借出，调用方负责 ReturnFrame
```

### 4.2 Decode 流程

```
ReadOnlySpan<byte> frame
   │
   ▼ 1. 校验帧头
if frame.Length < 6: throw "Frame is too short"
compression = (CompressionKind)frame[1]
payloadLength = frame[2] | (frame[3]<<8) | (frame[4]<<16) | (frame[5]<<24)
if payloadLength < 0 || 6 + payloadLength > frame.Length: throw "Invalid frame"
payload = frame.Slice(6, payloadLength)
   │
   ▼ 2. 按 compression 分支
   ├─ None:
   │    return MemoryPackSerializer.Deserialize<SyncPacket>(payload)
   │
   └─ Lz4:
        hint = originalPayloadLengthHint > 0 ? hint : max(payloadLength*4, payloadLength+64)
        if hint > MaxDecompressedSize(4MB): hint = 4MB
        rented = ArrayPool.Rent(hint)
        decoded = LZ4Codec.Decode(payload → rented)
        ├─ decoded < 0: 扩大缓冲重试（biggerSize = max(hint*4, payloadLength*16)）
        │   若 biggerSize > 4MB: throw "decompression bomb"
        │   decoded = LZ4Codec.Decode(payload → bigger)
        │   若 decoded > 4MB: throw "exceeds limit"
        │   return Deserialize(bigger[0..decoded])
        ├─ decoded > 4MB: throw "exceeds limit"
        └─ 正常: return Deserialize(rented[0..decoded])
        finally: ArrayPool.Return(rented)
```

### 4.3 资源管理

- `Encode` 借出的 `frame` 必须由调用方通过 `SyncPacketCodec.ReturnFrame(frame)` 归还到 `ArrayPool<byte>.Shared`。
- `Decode` 内部借出的缓冲在方法返回前归还，调用方无需关心。
- 压缩分支中若首次 `LZ4Codec.Decode` 返回 -1（缓冲不足），会自动扩大缓冲重试，但受 `MaxDecompressedSize` 上限保护。

## 5. 压缩策略

| 包类型 | 压缩策略 | 原因 |
| --- | --- | --- |
| `Snapshot`（Kind=2） | payload ≥ 256B 时启用 LZ4；压缩后反而变大则放弃 | 快照体积大、对延迟不敏感，压缩收益高 |
| `Input`（Kind=3） | **永不压缩** | 输入定频上行，对延迟极度敏感，压缩的 CPU 开销不可接受 |
| 其他包 | 不压缩（当前实现仅对 Snapshot 判定压缩） | 体积小或非高频 |

压缩标记 `CompressionKind`：

| 值 | 枚举 | 说明 |
| --- | --- | --- |
| 0 | `None` | 未压缩 |
| 1 | `Lz4` | LZ4 压缩 |

### 5.1 防解压炸弹

`MaxDecompressedSize = 4MB` 是硬性上限：

- 解压前预估 `hint` 不超过 4MB。
- 解压后实际 `decoded` 字节数若超过 4MB 立即抛 `InvalidOperationException`。
- 重试扩大缓冲时，`biggerSize` 超过 4MB 也立即抛异常。

这保证恶意构造的压缩帧无法通过放大攻击耗尽内存。

### 5.2 压缩有效性校验

`Encode` 中只有当 `written > 0 && written < rawPayload.Length` 时才采用压缩结果。若 LZ4 编码失败（written ≤ 0）或压缩后反而变大，则回退到未压缩 payload，避免负优化。

## 6. 序列化标注约定

同步协议采用 **MemoryPack + Orleans 双重序列化标注**，使同一类型既可用于 TCP 线协议（MemoryPack），又可用于 Orleans Grain 间通信（Orleans Serializer）。

### 6.1 标注组合

每个可序列化类型必须同时标注：

```csharp
[MemoryPackable]              // 或 [MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]          // Orleans 序列化器生成
public sealed partial class XxxPacket : SyncPacket
{
    [MemoryPackOrder(N)]      // MemoryPack 字段顺序（显式布局时必需）
    [Id(M)]                   // Orleans 字段 ID
    public T Field { get; set; }
}
```

### 6.2 编号规则

#### SyncPacket 基类（显式布局）

`SyncPacket` 使用 `[MemoryPackable(SerializeLayout.Explicit)]`，基类元字段占用高位 `[Id]`，使派生类从 0 开始编号，与 `MessageUnion` 子类保持一致风格：

| 字段 | MemoryPackOrder | Orleans Id | 说明 |
| --- | --- | --- | --- |
| `Kind` | 0 | 254 | 包种类（冗余字段，便于不解码 union 时识别） |
| `ProtocolVersion` | 1 | 255 | 协议版本号（默认 = `SyncProtocolVersion.Current`） |

#### 派生包类

派生类的 `[MemoryPackOrder]` 从 2 开始（0/1 留给基类），`[Id]` 从 0 开始：

```csharp
// 示例：InputPacket
[MemoryPackOrder(2)] [Id(0)] public long ClientTick;
[MemoryPackOrder(3)] [Id(1)] public uint InputBits;
[MemoryPackOrder(4)] [Id(2)] public float LookYaw;
...
```

#### struct 组件

`EntityDelta`、`SyncEvent`、`NetworkIdentityAuthComponent`、`AuthTransformComponent`、`PredictedTransformComponent`、`EntityStateAuthComponent`、`InputAckAuthComponent` 等 struct 的 `[MemoryPackOrder]` 与 `[Id]` 均从 0 开始对齐编号。

### 6.3 SyncPacket 多态注册（MemoryPackUnion）

`SyncPacket` 基类通过 `[MemoryPackUnion]` 注册所有派生类型，实现单一 channel 多类型派发：

```csharp
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[MemoryPackUnion(0, typeof(HandshakePacket))]
[MemoryPackUnion(1, typeof(SnapshotPacket))]
[MemoryPackUnion(2, typeof(InputPacket))]
[MemoryPackUnion(3, typeof(EventPacket))]
[MemoryPackUnion(4, typeof(WorldChunkDiffPacket))]
[MemoryPackUnion(5, typeof(WorldPatchManifestPacket))]
[MemoryPackUnion(6, typeof(InputAckPacket))]
[MemoryPackUnion(7, typeof(ReconnectResumePacket))]
[MemoryPackUnion(8, typeof(InteractionSyncPacket))]
public abstract partial class SyncPacket { ... }
```

MemoryPackUnion 编号与 `SyncPacketKind` 枚举值的对应关系（注意 offset 差 1）：

| MemoryPackUnion 编号 | 类型 | SyncPacketKind |
| --- | --- | --- |
| 0 | HandshakePacket | Handshake = 1 |
| 1 | SnapshotPacket | Snapshot = 2 |
| 2 | InputPacket | Input = 3 |
| 3 | EventPacket | Event = 4 |
| 4 | WorldChunkDiffPacket | WorldChunkDiff = 5 |
| 5 | WorldPatchManifestPacket | WorldPatchManifest = 6 |
| 6 | InputAckPacket | InputAck = 7 |
| 7 | ReconnectResumePacket | ReconnectResume = 8 |
| 8 | InteractionSyncPacket | InteractionSync = 9 |

> 注意：MemoryPackUnion 编号从 0 开始，而 `SyncPacketKind` 从 1 开始（0 = Unknown）。两者是独立编号空间，帧头 Kind 字段用 `SyncPacketKind`，union 内部判别用 MemoryPackUnion 编号。
>
> InteractionSyncPacket 的 MemoryPackUnion 编号为 8（与 `SyncPacketKind=9` 存在 offset 差 1，与既有 0-7 编号风格一致）。

### 6.4 构造函数约定

每个派生包类的无参构造函数负责设置自身的 `Kind` 字段，保证序列化前后 `Kind` 始终正确：

```csharp
public HandshakePacket() { Kind = SyncPacketKind.Handshake; }
public SnapshotPacket() { Kind = SyncPacketKind.Snapshot; }
// ... 其余类同
```

### 6.5 同步组件（SyncComponents）

`SyncComponents.cs` 定义服务器权威与客户端预测的组件结构，同样遵循双重标注约定：

| 组件 | 后缀语义 | 写入方 |
| --- | --- | --- |
| `NetworkIdentityAuthComponent` | Auth = 服务器权威 | 网络层从 SnapshotPacket 写入 |
| `AuthTransformComponent` | Auth = 服务器权威 | 网络层写入（含 ServerTick 用于插值排序） |
| `PredictedTransformComponent` | 客户端预测副本 | `LocalSimulationSystem` 写入；与 AuthTransform 比对超阈值时回滚 |
| `EntityStateAuthComponent` | Auth = 服务器权威 | 网络层写入（HP / 状态位） |
| `InputAckAuthComponent` | Auth = 服务器权威 | 服务器下发输入回执 |
| `InteractionSyncComponent` | Interaction = 交互槽状态 | `InteractionApplySystem` 从 `SyncPacketInbox.InteractionEvents` 写入 |

`EntityStateAuthComponent.StateBits` 状态位定义（`EntityStateBits`）：

| 位 | 常量 | 说明 |
| --- | --- | --- |
| bit0 | `Dead` | 死亡 |
| bit1 | `Invincible` | 无敌 |
| bit2 | `Stunned` | 眩晕 |
| bit3 | `Hidden` | 隐藏 |
| bit4 | `Frozen` | 冰冻 |

`InteractionSyncComponent.StateBits` 状态位定义（`InteractionStateBits`）：

| 位 | 常量 | 说明 |
| --- | --- | --- |
| bit0 | `Start` (0x01) | 交互开始（下行状态） |
| bit1 | `End` (0x02) | 交互结束（下行状态） |
| bit2 | `Stolen` (0x04) | 交互被抢占（下行状态） |
| bit7 | `RequestStartFlag` (0x80) | 请求开始交互（上行意图） |
| bit6 | `RequestStopFlag` (0x40) | 请求停止交互（上行意图） |

> 下行状态位（Start/End/Stolen）与上行意图位（RequestStartFlag/RequestStopFlag）占用不同 bit 区间，可安全复用同一 byte 字段。`InteractionApplySystem` 检测到 End/Stolen 位时触发 Despawn 逻辑。

### 6.6 与 MessageUnion 的职责划分

`SyncPacket` 与 `MessageUnion` 互补：

| 体系 | 职责 | 通道 |
| --- | --- | --- |
| `SyncPacket` | 系统自主同步消息（快照、输入、心跳、世界 diff） | 高频实时通道 |
| `MessageUnion` | 用户主动请求或响应消息（登录、聊天、交易） | 常规请求/响应通道 |

两者均为 MemoryPackUnion 多态基类，编号风格一致（派生类 `[Id]` 从 0 开始）。

## 7. 关键文件索引

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\SyncPackets.cs` — 协议版本号、SyncPacketKind 枚举、SyncPacket 基类及 10 种派生包定义（含 v5 新增 `SceneObjectSyncPacket`）、EntityDelta / SyncEvent 结构
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\SyncPacketCodec.cs` — 帧编解码器（Encode/Decode/ReturnFrame）、压缩常量、防炸弹上限
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\Components\SyncComponents.cs` — 同步组件定义（NetworkIdentityAuth / AuthTransform / PredictedTransform / EntityStateAuth / InputAckAuth / InteractionSync / v5 新增 MovementStateAuth / AnimationStateAuth / SceneObjectStateAuth / SceneObjectTransform）、EntityStateBits / SceneObjectStateBits 状态位
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\CharacterSyncConfig.cs` — 角色同步频率策略（位置 20Hz / 移动状态 10Hz / 动画事件驱动 / 属性 1Hz）

### 相关文件（非协议本体，但参与协议流转）

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\Sim\CorrectionPacket.cs` — 位置修正包（独立于 SyncPacket union，以 EventPacket 负载形式下发）、CorrectionReason 枚举
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\Sim\Server\GatewaySyncDispatcher.cs` — Gateway 侧 SyncPacket fanout 分派
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Gateway\Network\GameNetworkServer.cs` — TCP 网关（HorizonMessageAdapter 帧适配）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Orleans.Grains\World\ZoneShardGrain.cs` — 服务端 Grain（组装 SnapshotPacket / EventPacket / WorldChunkDiffPacket）
