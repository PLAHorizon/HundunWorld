using MemoryPack;
using Orleans;
using System;

namespace Horizon.Game.Message.Sync;

/// <summary>
/// 表现型事件的下发载荷，作为 <see cref="SyncEvent.Payload"/>（Kind = <see cref="SyncEventKind.Vfx"/>）
/// 的二次序列化内容。
/// <para>
/// <b>为什么复用 Vfx 而不是新协议包</b>：<c>EventPacket</c> 的 <see cref="SyncEvent"/> 本就带
/// <c>Kind</c> + <c>Payload</c> 的通用通道，Correction/InputAck 也是这么挂进来的。新增包类型要动
/// 编解码与路由，而这里语义完全落在既有形态内。
/// </para>
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial class ScriptedEffectPayload
{
    /// <summary>效果键（供客户端分流美术资源；当前仅用于日志与后续接入）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public string EffectKey { get; set; } = string.Empty;

    /// <summary>要在世界里显示的文字（事件名/播报）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public string Text { get; set; } = string.Empty;

    /// <summary>事件位（服务端 Z-up，米）。客户端按既有约定换轴到 Y-up。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public float X { get; set; }

    [MemoryPackOrder(3)]
    [Id(3)]
    public float Y { get; set; }

    [MemoryPackOrder(4)]
    [Id(4)]
    public float Z { get; set; }

    /// <summary>显示时长（秒）。</summary>
    [MemoryPackOrder(5)]
    [Id(5)]
    public float DurationSeconds { get; set; } = 3f;
}

/// <summary>
/// 脚本化生成的结果回执载荷（<see cref="SyncEventKind.ScriptedSpawnResult"/> 的二次序列化内容）。
/// <para>
/// <b>为什么走同步通道而不是新开一条请求-响应</b>：实体 ID 只有 DS 知道（它持有脚本生成点的
/// 单调序号），而 DS 到客户端的唯一既有通路就是快照与事件帧。走 <c>EventPacket</c> 定向下发
/// 复用了这条通路，也天然带上"事件真正发生后"的时序含义。
/// </para>
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial class ScriptedSpawnResultPayload
{
    /// <summary>与客户端指令配对的序号（客户端用它把回执贴回原命令气泡）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public long RequestId { get; set; }

    /// <summary>对应令牌的 Nonce，便于与审计日志对齐。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public long Nonce { get; set; }

    /// <summary>本次实际生效的实体 ID。可能少于请求数量（容量/上限裁剪），如实回传。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public ulong[] EntityIds { get; set; } = Array.Empty<ulong>();

    /// <summary>是否为清除类指令的回执。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public bool Removed { get; set; }
}
