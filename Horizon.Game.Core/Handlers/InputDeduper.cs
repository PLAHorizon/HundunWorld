using System;
using System.Collections.Generic;

namespace Horizon.Game.Core.Handlers;

/// <summary>输入去重判定结果。</summary>
public enum InputDedupDecision
{
    /// <summary>新输入，接受并推进去重水位。</summary>
    Accepted,

    /// <summary>重复/过期输入（ClientTick ≤ 本连接已接受的最大 tick），不转发。</summary>
    Duplicate,

    /// <summary>低于握手基线的旧会话残留输入，直接丢弃。</summary>
    StaleSession,
}

/// <summary>
/// 阶段 5.2 — 服务端输入去重（横切服务，从 <see cref="SyncInputHandler"/> 提取）。<br/>
/// 封装 per-(characterId, connectionId) 的 ClientTick 去重水位与握手基线，供输入热路径
/// 与握手流程共享同一份状态（握手设置基线、清理旧连接残留）。实现线程安全
/// （锁内仅字典操作，调用方在锁外做 await）。
/// </summary>
public interface IInputDeduper
{
    /// <summary>
    /// 判定输入包是否应被接受。线程安全（内部加锁，锁内仅字典操作）。
    /// </summary>
    /// <param name="characterId">输入包携带的角色 ID。</param>
    /// <param name="connectionId">当前连接 ID（复合 key 隔离不同连接）。</param>
    /// <param name="clientTick">输入包 ClientTick。</param>
    /// <param name="lastAcceptedTick">本连接已接受的最大 tick（Duplicate 时有意义）。</param>
    InputDedupDecision CheckDedup(ulong characterId, string connectionId, long clientTick, out long lastAcceptedTick);

    /// <summary>
    /// 握手成功后记录该连接的输入基线（InitialClientTick）。
    /// 低于基线的后续输入视为旧会话残留（StaleSession），直接丢弃。
    /// </summary>
    void RecordHandshakeBaseline(ulong characterId, string connectionId, long initialClientTick);

    /// <summary>
    /// 握手时清理同一 characterId 其他连接的去重/基线残留记录（防内存泄漏）。
    /// </summary>
    void CleanupCharacter(ulong characterId, string keepConnectionId);
}

/// <summary>
/// <see cref="IInputDeduper"/> 的会话态实现：底层复用 <see cref="SyncSessionState"/> 的
/// 去重字典与锁，保证与拆分前完全相同的跨处理器共享状态与锁语义。
/// </summary>
public sealed class SessionInputDeduper : IInputDeduper
{
    private readonly SyncSessionState _sessionState;

    public SessionInputDeduper(SyncSessionState sessionState)
    {
        _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
    }

    /// <inheritdoc />
    public InputDedupDecision CheckDedup(ulong characterId, string connectionId, long clientTick, out long lastAcceptedTick)
    {
        lastAcceptedTick = 0;
        var connKey = (characterId, connectionId);
        lock (_sessionState.InputDedupLock)
        {
            // 握手基线检查：低于基线的包来自旧会话/过期连接，直接丢弃
            if (_sessionState.HandshakeBaselinePerConnection.TryGetValue(connKey, out var baselineTick)
                && clientTick < baselineTick)
            {
                return InputDedupDecision.StaleSession;
            }

            // 序号去重：重复或更早的 ClientTick 直接返回最新 ack，不转发到 ZoneShard（避免重复模拟）
            if (_sessionState.LastInputTickPerConnection.TryGetValue(connKey, out lastAcceptedTick)
                && clientTick <= lastAcceptedTick)
            {
                return InputDedupDecision.Duplicate;
            }

            _sessionState.LastInputTickPerConnection[connKey] = clientTick;
            return InputDedupDecision.Accepted;
        }
    }

    /// <inheritdoc />
    public void RecordHandshakeBaseline(ulong characterId, string connectionId, long initialClientTick)
    {
        // 修复：客户端可能错误发送 Unix 时间戳作为 InitialClientTick，
        // 若值过大（> 1000000 判定为时间戳），重置为 0，避免后续所有顺序 tick 被误判为"旧会话残留"而拒绝。
        long baseline = initialClientTick;
        if (baseline > 1_000_000) baseline = 0;

        lock (_sessionState.InputDedupLock)
        {
            _sessionState.HandshakeBaselinePerConnection[(characterId, connectionId)] = baseline;
        }
    }

    /// <inheritdoc />
    public void CleanupCharacter(ulong characterId, string keepConnectionId)
    {
        lock (_sessionState.InputDedupLock)
        {
            // 清理同一 characterId 的旧连接记录（防内存泄漏）。
            // 复合 key 已隔离不同连接，此清理仅为释放断开连接的残留记录。
            var staleKeys = new List<(ulong, string)>();
            foreach (var k in _sessionState.LastInputTickPerConnection.Keys)
            {
                if (k.CharacterId == characterId && k.ConnectionId != keepConnectionId)
                    staleKeys.Add(k);
            }
            foreach (var sk in staleKeys)
            {
                _sessionState.LastInputTickPerConnection.Remove(sk);
                _sessionState.HandshakeBaselinePerConnection.Remove(sk);
            }
        }
    }
}
