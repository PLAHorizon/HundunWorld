using System;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using MemoryPack;
using Orleans;

namespace Horizon.Orleans.Interface.World;

// 说明：ScriptPermissionFlags 与 GmScriptTicket 定义在共享契约程序集
// Horizon.Game.Message.Network（纯逻辑裁决核 Horizon.Game.Core 也要用同一份定义，
// 而 Game.Core 不引用 Orleans.Interface）。此处只保留 Grain 侧的出入参与状态类型。

/// <summary>
/// 授权裁决结果。<see cref="Allowed"/> 为 true 时 <see cref="Token"/> 必非空且已签名。
/// </summary>
[GenerateSerializer]
public sealed class GmScriptVerdict
{
    [Id(0)] public bool Allowed { get; set; }

    [Id(1)] public ScriptRejectReason Reason { get; set; }

    /// <summary>面向操作者的说明文本。</summary>
    [Id(2)] public string Message { get; set; } = string.Empty;

    /// <summary>裁决通过时下发的自证令牌；拒绝时为 null。</summary>
    [Id(3)] public ScriptGrantToken? Token { get; set; }

    /// <summary>本次裁决后剩余的分钟配额。</summary>
    [Id(4)] public int RemainingBudget { get; set; }

    /// <summary>操作者当前生效权限位。</summary>
    [Id(5)] public uint EffectiveFlags { get; set; }

    /// <summary>审计条目号，便于把客户端现象与后台日志对齐。</summary>
    [Id(6)] public string OperationId { get; set; } = string.Empty;
}

/// <summary>某操作者的授权快照。</summary>
[GenerateSerializer]
public sealed class GmScriptGrantSnapshot
{
    [Id(0)] public long OperatorCharacterId { get; set; }

    [Id(1)] public uint Flags { get; set; }

    [Id(2)] public DateTime ExpiresAtUtc { get; set; }

    [Id(3)] public DateTime GrantedAtUtc { get; set; }

    [Id(4)] public long GrantedBy { get; set; }

    [Id(5)] public string Reason { get; set; } = string.Empty;

    /// <summary>是否已过期（读取时按当前 UTC 计算，避免各调用方自行比较口径不一）。</summary>
    [Id(6)] public bool IsExpired { get; set; }
}

/// <summary>
/// Grain 侧授权与裁决的持久化状态（Redis 热层 + DB 权威层双轨，见设计方案 §4.4）。
/// <para>项目规范：Grain 状态类需同时标注 <c>MemoryPackable</c> 与 <c>MemoryPackOrder</c>。</para>
/// </summary>
[MemoryPackable]
[GenerateSerializer]
public partial class GmScriptGrantState
{
    [MemoryPackOrder(0)]
    [Id(0)]
    public uint Flags { get; set; }

    [MemoryPackOrder(1)]
    [Id(1)]
    public DateTime ExpiresAtUtc { get; set; }

    [MemoryPackOrder(2)]
    [Id(2)]
    public DateTime GrantedAtUtc { get; set; }

    [MemoryPackOrder(3)]
    [Id(3)]
    public long GrantedBy { get; set; }

    [MemoryPackOrder(4)]
    [Id(4)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>最近一次被 <c>TryConsumeScriptAsync</c> 接受的 Unix 毫秒，用于冷却判定。</summary>
    [MemoryPackOrder(5)]
    [Id(5)]
    public long LastAcceptedUnixMs { get; set; }

    /// <summary>当前配额窗口起始（Unix 毫秒）。</summary>
    [MemoryPackOrder(6)]
    [Id(6)]
    public long QuotaWindowStartUnixMs { get; set; }

    /// <summary>当前配额窗口内已消耗次数。</summary>
    [MemoryPackOrder(7)]
    [Id(7)]
    public int QuotaWindowUsed { get; set; }

    /// <summary>已签发的令牌数（单调，用于生成 Nonce）。</summary>
    [MemoryPackOrder(8)]
    [Id(8)]
    public long IssuedTokenCounter { get; set; }
}
