using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using MemoryPack;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// MMORPG 开发计划 M2 —— DS↔网关上行协议（控制面 + 下行快照回传）。
/// <para>
/// <b>为什么需要它</b>：<see cref="Horizon.Game.Gateway.Services.DSRegistry"/> 的
/// <c>RegisterOrUpdate</c>/<c>Heartbeat</c> 在此之前**没有任何调用方**——DS 无法被网关发现，
/// 路由表恒空（缺口 G3）；同时 DS 产出的权威快照**没有入口**回到
/// <c>GatewayZoneShardFanoutSource</c>（缺口 G4）。本文件定义 DS → 网关这一方向的线上协议，
/// 两条链路共用**同一条 TCP 连接**与**同一套长度前缀分帧**。
/// </para>
/// <para>
/// <b>帧格式</b>（与既有网关→DS 数据面 <c>DsSyncDataPlane</c> 完全一致的长度前缀，
/// 使两个方向可共用 <see cref="DsFrameReader"/>）：
/// <code>
/// +----------------+------------------------------+
/// | 4B 大端长度 N  | N 字节负载                  |
/// +----------------+------------------------------+
/// 负载 = [1B DsUplinkMessageKind][MemoryPack 编码的消息体]
/// </code>
/// </para>
/// <para>
/// <b>为什么不用既有 SyncPacketCodec 通道</b>：控制面属于网关↔DS 之间的**旁路信令**，
/// 混进客户端同步协议会污染 <c>MessageUnion</c>（225 处 union 注册）与协议版本演进面。
/// 本协议独立、尾部可扩展（未知 kind 直接跳过，见 <see cref="DsUplinkCodec.TryDecode"/>），
/// 与既有通道零耦合——符合项目「新增并行通道」纪律。
/// </para>
/// </summary>
public enum DsUplinkMessageKind : byte
{
    /// <summary>未知/保留（解码失败兜底，永不发送）。</summary>
    Unknown = 0,

    /// <summary>DS 注册/更新（启动时及 shard 集合变更时发送）。</summary>
    Register = 1,

    /// <summary>DS 心跳续约（默认 30s 周期；网关 90s 未收到即摘除）。</summary>
    Heartbeat = 2,

    /// <summary>DS 主动摘除（优雅下线）。</summary>
    Unregister = 3,

    /// <summary>网关对控制消息的应答（Register/Heartbeat/Unregister 通用）。</summary>
    ControlAck = 4,

    /// <summary>DS → 网关：一批权威快照/事件帧，按目标会话定向投递。</summary>
    SnapshotBatch = 5,

    /// <summary>
    /// DS → 网关：跨层玩法事件（击杀/掉落/玩家死亡），网关转 Orleans 业务层处理。
    /// <para>与 <see cref="SnapshotBatch"/> 的区别：那是"给客户端的字节"，这是"给业务层的事实"。</para>
    /// </summary>
    GameplayEvent = 6,

    /// <summary>
    /// **网关 → DS**：会话结束通知（客户端断线）。
    /// <para>
    /// <b>方向与其他消息相反</b>：这是首个由网关主动下发给 DS 的消息，复用 DS 已建立的那条
    /// 上行 TCP 连接（TCP 本就双向），不额外开连接。
    /// </para>
    /// <para>
    /// <b>为什么必须有它</b>：DS 是权威世界，玩家实体常驻其中。客户端断线时网关会清理自己的
    /// 连接与映射，但 DS **无从得知**——于是会永久保留该玩家并持续按 20Hz 广播快照给一个已死会话，
    /// 全部被网关丢弃（实测日志：<c>Packet dropped: session offline</c> 持续刷屏、计数不断增长）。
    /// 这既浪费带宽，也是 DS 侧的实体泄漏。
    /// </para>
    /// <para>尾部追加（kind=7），旧网关遇到未知 kind 只跳过，前向兼容。</para>
    /// </summary>
    SessionEnd = 7,
}

/// <summary>会话结束通知（网关 → DS）：这些会话已断开，DS 应移除对应玩家实体。</summary>
[MemoryPackable]
public partial class DsSessionEndNotice
{
    /// <summary>已结束的会话 ID 集合（本架构中 sessionId == characterId）。</summary>
    public long[] SessionIds { get; set; } = Array.Empty<long>();
}

/// <summary>DS 注册请求（M2.1 / M2.4）。</summary>
[MemoryPackable]
public partial class DsRegisterRequest
{
    /// <summary>DS 唯一标识（同一 DS 重复注册为幂等 upsert）。</summary>
    public string DsId { get; set; } = string.Empty;

    /// <summary>本 DS 承载的 shard 集合。</summary>
    public long[] ShardIds { get; set; } = Array.Empty<long>();

    /// <summary>DS 对外可达主机（网关据此把上行帧转发给该 DS）。</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>DS 数据面监听端口（承接 <c>[4B BE len][SyncPacketCodec 帧]</c>）。</summary>
    public int Port { get; set; }

    /// <summary>DS 所在区域（跨区盲路由防护）；空串 = 未声明区域。</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>共享密钥（<c>GatewayOptions.DsControlToken</c>）；网关配置为空时不校验。</summary>
    public string Token { get; set; } = string.Empty;
}

/// <summary>DS 心跳 / 摘除请求（仅需身份）。</summary>
[MemoryPackable]
public partial class DsIdentityRequest
{
    /// <summary>DS 唯一标识。</summary>
    public string DsId { get; set; } = string.Empty;

    /// <summary>共享密钥。</summary>
    public string Token { get; set; } = string.Empty;
}

/// <summary>控制消息应答。</summary>
[MemoryPackable]
public partial class DsControlAck
{
    /// <summary>是否受理成功。</summary>
    public bool Ok { get; set; }

    /// <summary>失败原因（成功时为空串）；供 DS 侧日志与退避决策。</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// 一批定向下行帧。每个条目携带**目标会话集合**与一段
/// <c>SyncPacketCodec</c> 编码的原始帧，网关解码后转成 <c>FanoutEvent</c> 注入既有分发管线。
/// <para>
/// 批次化而非逐帧：DS 侧一次 tick 的广播是天然批量的，逐帧发送会把跨进程往返次数放大到
/// O(实体×会话)——这正是既有架构「P-F1 把一轮广播合并为单条 observer 消息」要避免的反模式。
/// </para>
/// </summary>
[MemoryPackable]
public partial class DsSnapshotBatch
{
    /// <summary>批次内条目。</summary>
    public DsSnapshotItem[] Items { get; set; } = Array.Empty<DsSnapshotItem>();
}

/// <summary>单个定向下行条目。</summary>
[MemoryPackable]
public partial class DsSnapshotItem
{
    /// <summary>本帧的受众会话（网关据此定向投递，不广播给全服）。</summary>
    public long[] SessionIds { get; set; } = Array.Empty<long>();

    /// <summary><c>SyncPacketCodec</c> 编码的原始同步帧（网关侧解码为具体 SyncPacket）。</summary>
    public byte[] Frame { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// 一批跨层玩法事件（MMORPG 开发计划 M4.4）。
/// <para>
/// DS 只上报"发生了什么"，不直接写玩家背包/任务——业务权威仍在 Orleans。
/// 批量上报的理由与快照一致：一次战斗高潮（群怪死亡）会瞬间产生几十条事件，
/// 逐条跨进程往返会把 tick 预算吃掉。
/// </para>
/// </summary>
[MemoryPackable]
public partial class DsGameplayEventBatch
{
    /// <summary>本批事件。</summary>
    public DsGameplayEventDto[] Events { get; set; } = Array.Empty<DsGameplayEventDto>();
}

/// <summary>
/// 跨层玩法事件的可序列化形态。
/// <para>
/// 刻意与 <see cref="DsGameplayEvent"/> 分离：后者是内核结构（含 <c>BuildIdempotencyKey</c>
/// 这类行为），本 DTO 是线上契约。两者字段一一对应，转换在网关侧完成——
/// 这样内核改字段不会静默改变线上格式。
/// </para>
/// </summary>
[MemoryPackable]
public partial class DsGameplayEventDto
{
    /// <summary>事件类型（对应 <see cref="DsGameplayEventKind"/>）。</summary>
    public byte Kind { get; set; }

    /// <summary>shard。</summary>
    public long ShardId { get; set; }

    /// <summary>服务器 tick。</summary>
    public long ServerTick { get; set; }

    /// <summary>主角角色 ID。</summary>
    public ulong ActorCharacterId { get; set; }

    /// <summary>怪物实体 ID。</summary>
    public ulong MonsterEntityId { get; set; }

    /// <summary>怪物模板 ID。</summary>
    public int MonsterTemplateId { get; set; }

    /// <summary>物品 ID。</summary>
    public int ItemId { get; set; }

    /// <summary>物品数量。</summary>
    public int ItemCount { get; set; }

    /// <summary>转换为内核事件。</summary>
    public DsGameplayEvent ToCoreEvent() => new DsGameplayEvent
    {
        Kind = (DsGameplayEventKind)Kind,
        ShardId = ShardId,
        ServerTick = ServerTick,
        ActorCharacterId = ActorCharacterId,
        MonsterEntityId = MonsterEntityId,
        MonsterTemplateId = MonsterTemplateId,
        ItemId = ItemId,
        ItemCount = ItemCount,
    };

    /// <summary>由内核事件构造 DTO。</summary>
    public static DsGameplayEventDto FromCoreEvent(in DsGameplayEvent e) => new DsGameplayEventDto
    {
        Kind = (byte)e.Kind,
        ShardId = e.ShardId,
        ServerTick = e.ServerTick,
        ActorCharacterId = e.ActorCharacterId,
        MonsterEntityId = e.MonsterEntityId,
        MonsterTemplateId = e.MonsterTemplateId,
        ItemId = e.ItemId,
        ItemCount = e.ItemCount,
    };
}

/// <summary>
/// DS 上行帧编解码（纯函数，无 IO）——可被 UE 托管侧（UnrealSharp）与网关侧共用。
/// </summary>
public static class DsUplinkCodec
{
    /// <summary>单帧负载上限（16 MiB）：防御长度前缀被篡改导致的巨量分配。</summary>
    public const int MaxPayloadBytes = 16 * 1024 * 1024;

    /// <summary>把消息体编码为「kind + MemoryPack 体」的负载。</summary>
    public static byte[] EncodePayload<T>(DsUplinkMessageKind kind, T body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var bodyBytes = MemoryPackSerializer.Serialize(body);
        var payload = new byte[1 + bodyBytes.Length];
        payload[0] = (byte)kind;
        bodyBytes.CopyTo(payload.AsSpan(1));
        return payload;
    }

    /// <summary>把负载包成线上帧（4B 大端长度前缀 + 负载）。</summary>
    public static byte[] EncodeFrame<T>(DsUplinkMessageKind kind, T body)
        => FramePayload(EncodePayload(kind, body));

    /// <summary>为已有负载加 4B 大端长度前缀。</summary>
    public static byte[] FramePayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Length, $"帧负载超过上限 {MaxPayloadBytes} 字节。");
        }
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), payload.Length);
        payload.CopyTo(frame.AsSpan(4));
        return frame;
    }

    /// <summary>
    /// 解析负载为具体消息体。未知 kind 或反序列化失败时返回 <c>false</c> 且不清空
    /// <paramref name="body"/>——前向兼容：旧网关遇到新 DS 发来的新消息只跳过，不断连。
    /// </summary>
    public static bool TryDecode<T>(ReadOnlySpan<byte> payload, out DsUplinkMessageKind kind, out T? body)
        where T : class
    {
        kind = DsUplinkMessageKind.Unknown;
        body = null;
        if (payload.Length < 1)
        {
            return false;
        }

        kind = (DsUplinkMessageKind)payload[0];
        if (payload.Length == 1)
        {
            // 无体消息（保留位）：仅当 T 可空实例化时才认为成功。
            return false;
        }

        try
        {
            body = MemoryPackSerializer.Deserialize<T>(payload.Slice(1));
            return body is not null;
        }
        catch
        {
            // 损坏帧/类型不匹配：不抛出（网络输入不可信），交由调用方计数丢弃。
            return false;
        }
    }
}

/// <summary>
/// 增量长度前缀分帧读取器：把 TCP 字节流切回完整帧。
/// <para>
/// TCP 是字节流，一次 <c>Receive</c> 可能拿到半帧、整帧或「一帧半」；
/// 直接按一次读取当作一帧解析是既有网关代码里最经典的错误来源。
/// 本类把「粘包/拆包」显式建模，并在 <see cref="MaxPayloadBytes"/> 之上做拒绝，
/// 避免恶意长度前缀触发巨量分配。
/// </para>
/// <para>
/// 非线程安全：每个连接一个实例（与 TCP 连接的单读线程模型一致）。
/// </para>
/// </summary>
public sealed class DsFrameReader
{
    private byte[] _buffer;
    private int _length;

    /// <summary>创建读取器。</summary>
    /// <param name="initialCapacity">初始缓冲容量（字节）。</param>
    /// <param name="maxPayloadBytes">单帧负载上限；null 使用 <see cref="DsUplinkCodec.MaxPayloadBytes"/>。</param>
    public DsFrameReader(int initialCapacity = 8192, int? maxPayloadBytes = null)
    {
        if (initialCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _buffer = new byte[initialCapacity];
        MaxPayload = maxPayloadBytes ?? DsUplinkCodec.MaxPayloadBytes;
    }

    /// <summary>单帧负载上限。</summary>
    public int MaxPayload { get; }

    /// <summary>当前缓冲中尚未成帧的字节数（诊断用）。</summary>
    public int BufferedBytes => _length;

    /// <summary>累计因超长被丢弃的帧数（拒绝服务防护指标）。</summary>
    public long OversizedFrameCount { get; private set; }

    /// <summary>追加新到达的字节。</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        EnsureCapacity(_length + data.Length);
        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>
    /// 尝试取出一个完整帧的**负载**（不含 4B 长度前缀）。
    /// <para>
    /// 返回 <c>false</c> 表示数据不足（调用方应等待更多字节），缓冲保持不动；
    /// 返回 <c>true</c> 时 <paramref name="payload"/> 指向内部缓冲的切片，
    /// **调用方必须在下次 <see cref="Append"/> 前消费完毕**（零拷贝，避免每帧分配）。
    /// </para>
    /// </summary>
    public bool TryReadFrame(out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (_length < 4) return false;

        var declared = BinaryPrimitives.ReadInt32BigEndian(_buffer.AsSpan(0, 4));
        if (declared < 0 || declared > MaxPayload)
        {
            // 长度非法：无法安全重同步（后续字节边界未知），丢弃整个缓冲并计数。
            // 上层应在计数激增时主动断开该连接。
            OversizedFrameCount++;
            _length = 0;
            return false;
        }

        if (_length < 4 + declared) return false;

        payload = _buffer.AsSpan(4, declared);
        return true;
    }

    /// <summary>消费掉最近一次 <see cref="TryReadFrame"/> 返回的帧（调用方复制完负载后调用）。</summary>
    public void ConsumeFrame()
    {
        if (_length < 4) return;
        var declared = BinaryPrimitives.ReadInt32BigEndian(_buffer.AsSpan(0, 4));
        if (declared < 0 || declared > MaxPayload || _length < 4 + declared)
        {
            return;
        }

        var consumed = 4 + declared;
        var remaining = _length - consumed;
        if (remaining > 0)
        {
            Array.Copy(_buffer, consumed, _buffer, 0, remaining);
        }
        _length = remaining;
    }

    private void EnsureCapacity(int required)
    {
        if (_buffer.Length >= required) return;
        var next = _buffer.Length;
        while (next < required) next = next > int.MaxValue / 2 ? required : next * 2;
        Array.Resize(ref _buffer, next);
    }
}
