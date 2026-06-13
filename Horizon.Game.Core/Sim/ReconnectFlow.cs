using System;
using System.Collections.Generic;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;

namespace Horizon.Game.Core.Sim;

/// <summary>
/// 断线重连的编排器（P4-b）。<br/>
/// 与 Gateway / Orleans grain 均解耦——接收一个 <see cref="ReconnectResumePacket"/>，
/// 调用 <see cref="PlayerSessionState.ApplyReconnect"/> 得到决策，
/// 然后按决策把"本次重连应该下发的数据包"组装成一个 <see cref="ReconnectPlan"/>。
/// </summary>
/// <remarks>
/// 上层（Gateway / <c>ISyncDispatcher</c>）调用 <see cref="Execute"/> 后，按 plan 顺序：
/// <list type="bullet">
///   <item>若 <see cref="ReconnectPlan.CloseConnection"/> = true，直接断连（带 <see cref="ReconnectPlan.CloseReason"/>）；</item>
///   <item>否则依次把 <see cref="ReconnectPlan.Manifest"/> / <see cref="ReconnectPlan.ChunkDiffs"/> 写回客户端。</item>
/// </list>
/// 本类保持纯函数风格：不访问 grain、不读网络，所有依赖通过构造参数注入。
/// </remarks>
public sealed class ReconnectFlow
{
    private readonly IReconnectChunkDiffSource _diffSource;
    private readonly IReconnectPatchManifestSource _manifestSource;
    private readonly IReconnectChunkBaselineSource _baselineSource;

    public ReconnectFlow(
        IReconnectChunkDiffSource diffSource,
        IReconnectPatchManifestSource manifestSource,
        IReconnectChunkBaselineSource baselineSource)
    {
        _diffSource = diffSource ?? throw new ArgumentNullException(nameof(diffSource));
        _manifestSource = manifestSource ?? throw new ArgumentNullException(nameof(manifestSource));
        _baselineSource = baselineSource ?? throw new ArgumentNullException(nameof(baselineSource));
    }

    /// <summary>
    /// 按 <paramref name="session"/> 的状态处理一次重连包，返回 Gateway 需要落地的动作计划。
    /// </summary>
    /// <param name="session">Gateway 持有的玩家会话状态（已指派 LocalCharacterId 等）。</param>
    /// <param name="packet">客户端上报的 resume 包。</param>
    /// <param name="serverHeadDiffSeq">服务器当前全局 diff head。</param>
    /// <param name="serverWorldPatchVersion">服务器当前 world patch 版本。</param>
    public ReconnectPlan Execute(
        PlayerSessionState session,
        ReconnectResumePacket packet,
        long serverHeadDiffSeq,
        int serverWorldPatchVersion)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (packet is null) throw new ArgumentNullException(nameof(packet));

        var decision = session.ApplyReconnect(packet, serverHeadDiffSeq, serverWorldPatchVersion);
        switch (decision)
        {
            case ResumeDecision.RequireLauncherPatch:
                return new ReconnectPlan
                {
                    Decision = decision,
                    Manifest = _manifestSource.GetCurrentManifest(serverWorldPatchVersion),
                    CloseConnection = true,
                    CloseReason = "RequireLauncherPatch",
                };

            case ResumeDecision.ForceReLogin:
                return new ReconnectPlan
                {
                    Decision = decision,
                    CloseConnection = true,
                    CloseReason = "ForceReLogin",
                };

            case ResumeDecision.ResendFullChunks:
                return new ReconnectPlan
                {
                    Decision = decision,
                    ChunkDiffs = _baselineSource.GetChunkBaselines(packet.LocalCharacterId),
                };

            case ResumeDecision.ResumeIncremental:
            default:
                return new ReconnectPlan
                {
                    Decision = ResumeDecision.ResumeIncremental,
                    ChunkDiffs = _diffSource.GetDiffsSince(packet.LastAppliedDiffSeq, serverHeadDiffSeq),
                };
        }
    }
}

/// <summary>Gateway 对 <see cref="ReconnectFlow.Execute"/> 的产物。</summary>
public sealed class ReconnectPlan
{
    public ResumeDecision Decision { get; set; } = ResumeDecision.ResumeIncremental;

    /// <summary>需要下发给客户端的 chunk diff 序列（按 seq 升序，可能为空）。</summary>
    public IReadOnlyList<WorldChunkDiffPacket> ChunkDiffs { get; set; } = Array.Empty<WorldChunkDiffPacket>();

    /// <summary>仅当 <see cref="Decision"/> = <see cref="ResumeDecision.RequireLauncherPatch"/> 时非 null。</summary>
    public WorldPatchManifestPacket? Manifest { get; set; }

    /// <summary>执行完上述推送后是否应断开连接。</summary>
    public bool CloseConnection { get; set; }

    /// <summary>用于连接关闭时写入日志/发给客户端的 reason 字符串。</summary>
    public string CloseReason { get; set; } = string.Empty;
}

/// <summary>由 <see cref="IWorldDiffLogGrain"/> 包装的接口；解耦 ReconnectFlow 与 Orleans。</summary>
public interface IReconnectChunkDiffSource
{
    /// <summary>拉取 (sinceExclusive, head] 区间的 diff，按 seq 升序返回。</summary>
    IReadOnlyList<WorldChunkDiffPacket> GetDiffsSince(long sinceExclusive, long head);
}

/// <summary>世界补丁清单源。</summary>
public interface IReconnectPatchManifestSource
{
    WorldPatchManifestPacket GetCurrentManifest(int serverWorldPatchVersion);
}

/// <summary>"重发全量 chunk" 的数据源（按 AOI 聚合）。</summary>
public interface IReconnectChunkBaselineSource
{
    IReadOnlyList<WorldChunkDiffPacket> GetChunkBaselines(ulong localCharacterId);
}
