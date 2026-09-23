using System;
using System.Collections.Generic;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 阶段 3.2 — SyncPacket 按 <see cref="Horizon.Game.Message.Sync.SyncPacketKind"/> 拆分后，
/// 各流处理器共享的按连接会话状态（输入去重 / 握手基线 / 握手幂等 / 交互速率限制）。
/// <para>
/// 由 <see cref="SyncPacketHandler"/> 创建并注入各 <see cref="SyncPacketFlowHandlerBase"/>
/// 派生处理器，保证拆分后多个处理器仍共享同一份 per-connection 状态与锁
/// （握手清理输入去重记录、输入校验握手基线、交互速率限制等跨处理器依赖）。
/// </para>
/// </summary>
public sealed class SyncSessionState
{
    /// <summary>保护 <see cref="LastInputTickPerConnection"/> 和 <see cref="HandshakeBaselinePerConnection"/> 的锁。</summary>
    public readonly object InputDedupLock = new();

    /// <summary>
    /// 每个连接最近一次接受的 ClientTick，用于服务端去重。
    /// key=(characterId, connectionId)，按连接隔离，防止旧连接高 tick 包污染新连接去重字典。
    /// 网关为单例（多连接并发），需通过 <see cref="InputDedupLock"/> 保护。
    /// </summary>
    /// <remarks>
    /// 修复（多连接 tick 污染）：原实现用 characterId 作 key，自动重连时旧连接的高 tick 包
    /// 在握手清理字典后重新写入，污染新连接的去重状态，导致新连接所有输入被持续拒绝
    /// （ClientTick &lt;= LastAccepted），角色卡住/离线。改用 (characterId, connectionId)
    /// 复合 key 后，不同连接的去重状态完全隔离，旧连接的包不影响新连接。
    /// </remarks>
    public readonly Dictionary<(ulong CharacterId, string ConnectionId), long> LastInputTickPerConnection = new();

    /// <summary>
    /// 握手基线 tick。key=(characterId, connectionId)，value=握手时客户端声明的 InitialClientTick。
    /// 握手后，任何 ClientTick &lt; 基线的输入包视为来自旧会话/过期连接的残留重传，直接丢弃。
    /// </summary>
    public readonly Dictionary<(ulong CharacterId, string ConnectionId), long> HandshakeBaselinePerConnection = new();

    /// <summary>保护 <see cref="HandshookConnections"/> 的锁。</summary>
    public readonly object HandshakeIdempotentLock = new();

    /// <summary>
    /// 已完成握手的 (characterId, connectionId) 集合，用于重复握手幂等保护。<br/>
    /// 修复 BUG：客户端可能错误发送两次 Sync 握手（日志显示同一连接在 ~400ms 内发送两次握手），
    /// 导致 <c>HandleHandshakeAsync</c> 被调用两次，进而：<br/>
    /// 1. <c>EnterWorldAsync</c> 被调用两次，ZoneShard 实体重复创建<br/>
    /// 2. <c>ConnectionManager.RegisterCharacter</c> 被调用两次，重复日志/映射<br/>
    /// 3. AOI 订阅被重复建立<br/>
    /// 握手幂等保护：同一 (characterId, connectionId) 第二次握手直接返回成功响应，
    /// 不重复执行 EnterWorldAsync 等副作用。连接断开时由握手流程清理记录。
    /// </summary>
    public readonly HashSet<(ulong CharacterId, string ConnectionId)> HandshookConnections = new();

    /// <summary>保护 <see cref="InteractionLastRequestTime"/> 的锁。</summary>
    public readonly object RateLimitLock = new();

    /// <summary>
    /// 每个 interactorId 最近一次交互意图请求时间，用于简单速率限制。
    /// 网关为单例，需保证线程安全（使用 <see cref="RateLimitLock"/> 保护）。
    /// </summary>
    public readonly Dictionary<long, DateTime> InteractionLastRequestTime = new();

    /// <summary>上次执行速率限制条目清理的时间。</summary>
    public DateTime LastRateLimitCleanup = DateTime.MinValue;

    /// <summary>保护 <see cref="BoundaryShardSubscriptions"/> 的锁。</summary>
    public readonly object BoundaryShardLock = new();

    /// <summary>
    /// 阶段 4.3 — 跨 Shard 边界双重订阅：characterId → 当前已建立 AOI 订阅的 Shard 集合（含主 Shard）。<br/>
    /// 由握手与位置驱动订阅更新维护：位置视口覆盖的 Shard 逐个建立订阅；
    /// 移出边界的 Shard 由更新路径调用 <c>RemoveSessionAsync</c> 清理并移除出集合，
    /// 避免边界 Shard 的 AOI 表残留订阅（内存泄漏）。
    /// </summary>
    public readonly Dictionary<long, HashSet<long>> BoundaryShardSubscriptions = new();
}
