using System;
using MemoryPack;
using Orleans;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.Message.Sync;

/// <summary>
/// 同步协议版本号：每次 <see cref="SnapshotPacket"/>/<see cref="InputPacket"/>/组件 schema 变更时递增。
/// 客户端 <see cref="HandshakePacket"/> 携带本地版本，服务器据此拒绝不兼容连接。
/// </summary>
public static class SyncProtocolVersion
{
    /// <summary>
    /// 当前协议版本：
    ///   v1 = 初版（PR1/PR2 引入：Handshake/Snapshot/Input/Event）。
    ///   v2 = P1-a 扩展：新增 <see cref="WorldChunkDiffPacket"/> / <see cref="WorldPatchManifestPacket"/> /
    ///        <see cref="InputAckPacket"/> / <see cref="ReconnectResumePacket"/>，并在 <see cref="HandshakePacket"/>
    ///        与 <see cref="SnapshotPacket"/> 上引入版本向量字段；旧客户端首次握手会被服务器拒绝（不兼容）。
    /// </summary>
    public const int Current = 2;
}

/// <summary>
/// 同步包的判别字段（写入帧头第一个字节，便于在不解码 union 的情况下做 fast-path 路由）。
/// </summary>
public enum SyncPacketKind : byte
{
    Unknown = 0,
    Handshake = 1,
    Snapshot = 2,
    Input = 3,
    Event = 4,
    /// <summary>P3-b：世界 voxel/prefab diff 流（服务器→客户端，按 ChunkCell 推送）。</summary>
    WorldChunkDiff = 5,
    /// <summary>P5：世界补丁清单/版本向量协商（服务器→客户端，握手后下发）。</summary>
    WorldPatchManifest = 6,
    /// <summary>P6：服务器对客户端 input 的确认（携带 LastProcessedClientTick，用于 reconciliation）。</summary>
    InputAck = 7,
    /// <summary>P6-b：客户端断线重连时的 resume 握手（携带 lastApplied tick / diff seq / patch version）。</summary>
    ReconnectResume = 8,
}

/// <summary>
/// 同步协议消息基类（以 <see cref="Horizon.Game.Message.MessageUnion"/> 为蓝本）。
/// 通过 <see cref="MemoryPackUnionAttribute"/> 注册各派生类型，实现单一 channel 多类型派发。
/// 本类负责系统自主同步消息（快照、输入、心跳等），
/// 与 <see cref="MessageUnion"/>（用户主动请求或响应消息）在职责上互补。
/// 基类元字段（Kind、ProtocolVersion）的 Orleans [Id] 置于高位（254/255），
/// 使派生类从 0 开始编号，与 <see cref="MessageUnion"/> 的子类保持一致的编号风格。
/// </summary>
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
public abstract partial class SyncPacket
{
    /// <summary>包种类（冗余字段，便于不解码 union 时也能识别）。</summary>
    [MemoryPackOrder(0)]
    [Id(254)]
    public SyncPacketKind Kind { get; set; }

    /// <summary>协议版本号（应等于 <see cref="SyncProtocolVersion.Current"/>）。</summary>
    [MemoryPackOrder(1)]
    [Id(255)]
    public int ProtocolVersion { get; set; } = SyncProtocolVersion.Current;
}

/// <summary>
/// 握手包：客户端连接成功后第一个包，告知协议版本与本地玩家身份。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class HandshakePacket : SyncPacket
{
    /// <summary>本地玩家所控制的服务器实体 ID（用于服务器把 input 路由到对应 grain）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong LocalCharacterId { get; set; }

    /// <summary>客户端 tick 起始值（用于服务器对齐 reconciliation 时间轴）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long InitialClientTick { get; set; }

    public HandshakePacket() { Kind = SyncPacketKind.Handshake; }
}

/// <summary>
/// 服务器→客户端 快照包：可以是 baseline（全量）或 delta（仅变化）。
/// <see cref="BaselineTick"/> = 0 表示本包自身为 baseline；
/// 否则客户端必须先持有 BaselineTick 对应的 baseline 才能解码本包（缺失则请求重传）。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class SnapshotPacket : SyncPacket
{
    /// <summary>本包对应的服务器 tick。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long ServerTick { get; set; }

    /// <summary>基线 tick；为 0 表示这是一个 baseline 自身。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long BaselineTick { get; set; }

    /// <summary>本帧实体变更。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public EntityDelta[] Deltas { get; set; } = Array.Empty<EntityDelta>();

    public SnapshotPacket() { Kind = SyncPacketKind.Snapshot; }
}

/// <summary>
/// 客户端→服务器 输入包：定频上行，永不压缩以保延迟。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class InputPacket : SyncPacket
{
    /// <summary>客户端 tick / 输入序号。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long ClientTick { get; set; }

    /// <summary>位掩码：移动方向、跳跃、技能 1..N。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public uint InputBits { get; set; }

    /// <summary>视角朝向（Yaw 弧度）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public float LookYaw { get; set; }

    /// <summary>视角俯仰（Pitch 弧度）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public float LookPitch { get; set; }

    /// <summary>移动输入 X（-1..1）。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public float MoveX { get; set; }

    /// <summary>移动输入 Y（-1..1）。</summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public float MoveY { get; set; }

    public InputPacket() { Kind = SyncPacketKind.Input; }
}

/// <summary>
/// 离散事件包（技能命中、伤害、死亡、特效触发等），与 snapshot 解耦走可靠通道。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class EventPacket : SyncPacket
{
    /// <summary>事件发生时的服务器 tick。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long ServerTick { get; set; }

    /// <summary>事件序列。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public SyncEvent[] Events { get; set; } = Array.Empty<SyncEvent>();

    public EventPacket() { Kind = SyncPacketKind.Event; }
}

/// <summary>
/// 单实体的快照变更 / 全量数据。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct EntityDelta
{
    /// <summary>实体 ID。</summary>
    [MemoryPackOrder(0)] [Id(0)] public ulong EntityId;

    /// <summary>变更类型。</summary>
    [MemoryPackOrder(1)] [Id(1)] public EntityDeltaKind Kind;

    /// <summary>身份信息（仅 Spawn / 全量时有效）。</summary>
    [MemoryPackOrder(2)] [Id(2)] public NetworkIdentityAuthComponent? Identity;

    /// <summary>Transform（变更时携带）。</summary>
    [MemoryPackOrder(3)] [Id(3)] public AuthTransformComponent? Transform;

    /// <summary>状态（变更时携带）。</summary>
    [MemoryPackOrder(4)] [Id(4)] public EntityStateAuthComponent? State;
}

/// <summary>实体增量种类。</summary>
public enum EntityDeltaKind : byte
{
    /// <summary>新建实体（Identity/Transform/State 都应有效）。</summary>
    Spawn = 1,

    /// <summary>已有实体的字段更新（仅携带变化字段）。</summary>
    Update = 2,

    /// <summary>实体销毁。</summary>
    Despawn = 3,
}

/// <summary>事件类型（与服务器侧枚举对齐）。</summary>
public enum SyncEventKind : ushort
{
    Unknown = 0,
    SkillCast = 1,
    Damage = 2,
    Death = 3,
    Vfx = 4,
    Sfx = 5,
    Pickup = 6,
}

/// <summary>
/// 通用同步事件：Payload 由 sender/receiver 按 Kind 解释，
/// 复杂 Payload 可在双方约定后用 MemoryPack 二次序列化嵌入 <see cref="Payload"/>。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial struct SyncEvent
{
    [MemoryPackOrder(0)] [Id(0)] public SyncEventKind Kind;

    /// <summary>事件源实体（攻击者 / 施法者）。</summary>
    [MemoryPackOrder(1)] [Id(1)] public ulong SourceEntityId;

    /// <summary>事件目标实体（受害者 / 拾取者）。</summary>
    [MemoryPackOrder(2)] [Id(2)] public ulong TargetEntityId;

    /// <summary>整型主参数（技能 ID / 伤害值）。</summary>
    [MemoryPackOrder(3)] [Id(3)] public int IntValue;

    /// <summary>浮点参数（暴击倍率 / 持续时间）。</summary>
    [MemoryPackOrder(4)] [Id(4)] public float FloatValue;

    /// <summary>额外二进制载荷。</summary>
    [MemoryPackOrder(5)] [Id(5)] public byte[]? Payload;
}

// ---------------------------------------------------------------------------
// P1-a：以下四类包属于"补齐服务器响应 + 大世界同步基础设施"的最小协议扩展。
// 它们的运行时处理（grain、Gateway 路由、客户端 system）由后续 PR 实装；
// 本文件只承担 wire-protocol 形态，使所有上层模块基于稳定的 schema 并行开发。
// ---------------------------------------------------------------------------

/// <summary>
/// 服务器→客户端：世界 voxel/prefab diff 数据流（P3-b）。
/// 每条 diff 对应一个 ChunkCell（16m），按 <see cref="ChunkMortonKey"/> 寻址；
/// 客户端按 <see cref="DiffSeqStart"/>..<see cref="DiffSeqEnd"/> 区间应用并更新 <c>WorldDiffBufferComponent</c>，
/// 缺包时通过 <see cref="ReconnectResumePacket"/> 主动请求补流。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class WorldChunkDiffPacket : SyncPacket
{
    /// <summary>目标 ChunkCell 的 Morton 键（24 位地址压入 ulong）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong ChunkMortonKey { get; set; }

    /// <summary>本批 diff 的起始序号（含）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long DiffSeqStart { get; set; }

    /// <summary>本批 diff 的终止序号（含）；客户端落盘后应更新到该值。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public long DiffSeqEnd { get; set; }

    /// <summary>该 chunk 当前所基于的 baseline 版本（与 <see cref="WorldPatchManifestPacket"/> 对齐）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public int BaselineVersion { get; set; }

    /// <summary>该 chunk 当前累积的 patch 版本。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public int WorldPatchVersion { get; set; }

    /// <summary>
    /// 已序列化（且可能 LZ4 压缩）的 voxel/prefab op 序列。
    /// 内部 schema 由 <c>Horizon.Game.World</c>（P3-a 引入）定义，本层仅做透传，避免 wire-schema 频繁变更。
    /// </summary>
    [MemoryPackOrder(7)]
    [Id(5)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>当 <see cref="Payload"/> 为 LZ4 压缩流时为真。</summary>
    [MemoryPackOrder(8)]
    [Id(6)]
    public bool PayloadCompressed { get; set; }

    public WorldChunkDiffPacket() { Kind = SyncPacketKind.WorldChunkDiff; }
}

/// <summary>
/// 服务器→客户端：世界补丁清单（P5）。
/// 在 <see cref="HandshakePacket"/> 完成后下发，告知客户端"当前服务器接受的世界版本向量"
/// 以及需要从启动器（GengDi）补齐的 chunk patch 列表（仅给出 manifest URL/哈希，文件下载走 CDN）。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class WorldPatchManifestPacket : SyncPacket
{
    /// <summary>当前权威 baseline 版本（与游戏二进制版本一一对应，由 GengDi 通过 .pak 投递）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public int BaselineVersion { get; set; }

    /// <summary>当前权威 worldPatch 版本（每次正式发布时由 <c>Horizon.Tools.WorldPatchBuilder</c> 累加）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public int WorldPatchVersion { get; set; }

    /// <summary>清单根 URL（CDN 入口；客户端按 worldPatchVersion 拼接子路径）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public string ManifestUrl { get; set; } = string.Empty;

    /// <summary>清单文件的 SHA256（hex）。客户端下载 manifest 后须校验一致。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public string ManifestSha256 { get; set; } = string.Empty;

    /// <summary>在线增量与本地补丁的边界 diff 序号；客户端 ≥ 本值则可纯走在线 diff。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public long PatchCutoverDiffSeq { get; set; }

    public WorldPatchManifestPacket() { Kind = SyncPacketKind.WorldPatchManifest; }
}

/// <summary>
/// 服务器→客户端：input ACK（P6）。
/// 与 <see cref="SnapshotPacket"/> 解耦，可在两次 snapshot 之间高频下发以缩短 reconciliation 窗口；
/// <see cref="LastProcessedClientTick"/> 用于客户端 <c>ReconciliationSystem</c> 丢弃已确认的预测输入并按需 rewind。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class InputAckPacket : SyncPacket
{
    /// <summary>服务器最近一次处理过的客户端 tick（含）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public long LastProcessedClientTick { get; set; }

    /// <summary>服务器当前 tick（与 <see cref="SnapshotPacket.ServerTick"/> 同义，便于纯 ACK 帧也带时基）。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long ServerTick { get; set; }

    /// <summary>当 RTT 估算需要时的 echo（客户端 input 中的 ClientTick），可为 0 表示未携带。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public long EchoClientTick { get; set; }

    public InputAckPacket() { Kind = SyncPacketKind.InputAck; }
}

/// <summary>
/// 客户端→服务器：断线重连后的 resume 握手（P6-b）。
/// 服务器据此决定 "继续推增量 / 强制 baseline 重传 / 让客户端先去补 worldPatch"。
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public sealed partial class ReconnectResumePacket : SyncPacket
{
    /// <summary>本地玩家所控制的角色 ID（与 <see cref="HandshakePacket.LocalCharacterId"/> 同义）。</summary>
    [MemoryPackOrder(2)]
    [Id(0)]
    public ulong LocalCharacterId { get; set; }

    /// <summary>客户端最后已应用的 snapshot tick。</summary>
    [MemoryPackOrder(3)]
    [Id(1)]
    public long LastAppliedSnapshotTick { get; set; }

    /// <summary>客户端最后已应用的世界 diff 全局序号（跨 chunk 单调递增的 high-water mark）。</summary>
    [MemoryPackOrder(4)]
    [Id(2)]
    public long LastAppliedDiffSeq { get; set; }

    /// <summary>客户端本地 baseline 版本（来自 .pak）。</summary>
    [MemoryPackOrder(5)]
    [Id(3)]
    public int BaselineVersion { get; set; }

    /// <summary>客户端本地已套用的 worldPatch 版本（来自 GengDi 的 WorldData/）。</summary>
    [MemoryPackOrder(6)]
    [Id(4)]
    public int WorldPatchVersion { get; set; }

    public ReconnectResumePacket() { Kind = SyncPacketKind.ReconnectResume; }
}
