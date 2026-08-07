using System;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.SyncGuard.Contracts;

/// <summary>
/// 一次上行发送请求的上下文：携带发起实体 ID、请求类型与请求时机。
/// </summary>
public readonly record struct SendRequestContext(
    ulong RequestingEntityId,
    SyncPacketKind RequestKind,
    DateTimeOffset RequestedAt);

/// <summary>
/// 发送资格判定结论：放行/拒绝二值，拒绝时必带分类与原因。
/// </summary>
public readonly record struct SyncSendVerdict
{
    /// <summary>是否允许发送。</summary>
    public bool Allowed { get; init; }

    /// <summary>判定出的实体发送资格分类。</summary>
    public EntitySendCategory Category { get; init; }

    /// <summary>拒绝原因（Allowed=false 时必填）。</summary>
    public SendRejectReason Reason { get; init; }

    /// <summary>放行结论（分类由调用方指定）。</summary>
    public static SyncSendVerdict Allow(EntitySendCategory category) => new()
    {
        Allowed = true,
        Category = category,
    };

    /// <summary>拒绝结论（必带分类与原因）。</summary>
    public static SyncSendVerdict Deny(EntitySendCategory category, SendRejectReason reason) => new()
    {
        Allowed = false,
        Category = category,
        Reason = reason,
    };
}

/// <summary>
/// 一次违规发送尝试的信息：违规实体 ID、实体类型、拒绝原因、发生时间。
/// </summary>
public readonly record struct SendViolationInfo(
    ulong EntityId,
    EntitySendCategory EntityType,
    SendRejectReason Reason,
    DateTimeOffset OccurredAt);

/// <summary>
/// 本地资格状态快照：状态变化事件的载荷，供绑定实体资格联动与日志审计。
/// </summary>
public readonly record struct LocalEligibilitySnapshot(
    LocalEligibilityPhase Phase,
    bool IsLocalEligible,
    DateTimeOffset ChangedAt);

/// <summary>
/// 一条深度绑定实体的绑定关系记录。
/// </summary>
public sealed class BindingRelationship
{
    /// <summary>绑定实体（唤物/宠物）ID。</summary>
    public ulong BoundEntityId { get; init; }

    /// <summary>归属主人（本地角色）实体 ID。</summary>
    public ulong OwnerEntityId { get; init; }

    /// <summary>绑定类型（唤物/宠物）。</summary>
    public BindingType BindingType { get; init; }

    /// <summary>绑定是否有效（登记时为 true，失效后为 false）。</summary>
    public bool IsValid { get; set; }

    /// <summary>绑定建立时间。</summary>
    public DateTimeOffset BoundAt { get; init; }

    /// <summary>绑定失效时间（IsValid=false 时记录）。</summary>
    public DateTimeOffset InvalidatedAt { get; set; }
}