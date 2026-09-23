using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Server;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// B2 — 重连编排器 <see cref="ReconnectFlow"/> 纯函数层契约测试（阶段 P4-b）。
/// <para>
/// 本层零网络零 Grain：直接驱动 <see cref="PlayerSessionState.ApplyReconnect"/> 四条决策分支
/// 与 <see cref="ReconnectFlow.Execute"/> 的产物组装（三源注入）。端到端层见 ReconnectEndToEndTests。
/// </para>
/// </summary>
public class ReconnectFlowContractTests
{
    private sealed class FakeDiffSource : IReconnectChunkDiffSource
    {
        public List<WorldChunkDiffPacket> Diffs { get; } = new();
        public long? LastSince;
        public long? LastHead;

        public IReadOnlyList<WorldChunkDiffPacket> GetDiffsSince(long sinceExclusive, long head)
        {
            LastSince = sinceExclusive;
            LastHead = head;
            return Diffs;
        }
    }

    private sealed class FakeManifestSource : IReconnectPatchManifestSource
    {
        public WorldPatchManifestPacket? Manifest;

        public WorldPatchManifestPacket GetCurrentManifest(int serverWorldPatchVersion)
            => Manifest ?? new WorldPatchManifestPacket { WorldPatchVersion = serverWorldPatchVersion };
    }

    private sealed class FakeBaselineSource : IReconnectChunkBaselineSource
    {
        public List<WorldChunkDiffPacket> Baselines { get; } = new();
        public ulong? LastCharacterId;

        public IReadOnlyList<WorldChunkDiffPacket> GetChunkBaselines(ulong localCharacterId)
        {
            LastCharacterId = localCharacterId;
            return Baselines;
        }
    }

    private sealed class Harness
    {
        public FakeDiffSource Diffs { get; } = new();
        public FakeManifestSource Manifest { get; } = new();
        public FakeBaselineSource Baselines { get; } = new();
        public ReconnectFlow Flow { get; }

        public Harness()
        {
            Flow = new ReconnectFlow(Diffs, Manifest, Baselines);
        }
    }

    private static ReconnectResumePacket Resume(
        int worldPatchVersion = 10,
        long lastAppliedDiffSeq = 100,
        ulong localCharacterId = 42)
        => new()
        {
            WorldPatchVersion = worldPatchVersion,
            LastAppliedDiffSeq = lastAppliedDiffSeq,
            LocalCharacterId = localCharacterId,
            BaselineVersion = 3,
        };

    // ── ResumeIncremental：正常续连 ─────────────────────────────────────

    [Fact]
    public void NormalResume_DecidesIncremental_AndFetchesDiffRange()
    {
        var h = new Harness();
        var session = new PlayerSessionState();
        var diff = new WorldChunkDiffPacket { BaselineVersion = 3 };
        h.Diffs.Diffs.Add(diff);

        var plan = h.Flow.Execute(
            session, Resume(lastAppliedDiffSeq: 100), serverHeadDiffSeq: 200, serverWorldPatchVersion: 10);

        Assert.Equal(ResumeDecision.ResumeIncremental, plan.Decision);
        Assert.False(plan.CloseConnection);
        Assert.Same(diff, Assert.Single(plan.ChunkDiffs));
        // 增量窗口 = (100, 200]：三源之一被正确调用。
        Assert.Equal(100, h.Diffs.LastSince);
        Assert.Equal(200, h.Diffs.LastHead);
        // 版本向量归并：LastAppliedDiffSeq = min(客户端值, head)。
        Assert.Equal(100, session.LastAppliedDiffSeq);
    }

    [Fact]
    public void Resume_ClientDiffBelowHead_ServesFromMin()
    {
        var h = new Harness();
        var session = new PlayerSessionState();

        // 客户端自报 0 但 head=500：不得跳过，服务器按 min(0,500)=0 推 [1,500]。
        var plan = h.Flow.Execute(
            session, Resume(lastAppliedDiffSeq: 0), serverHeadDiffSeq: 500, serverWorldPatchVersion: 10);

        Assert.Equal(ResumeDecision.ResumeIncremental, plan.Decision);
        Assert.Equal(0, h.Diffs.LastSince);
        Assert.Equal(500, h.Diffs.LastHead);
        Assert.Equal(0, session.LastAppliedDiffSeq);
    }

    // ── RequireLauncherPatch：版本差过大 ─────────────────────────────────

    [Fact]
    public void VersionGapTooLarge_RequiresLauncherPatch_ClosesWithManifest()
    {
        var h = new Harness();
        var session = new PlayerSessionState();

        // 客户端 WorldPatchVersion=5，服务器=10：差 5 > 1 → 必须走启动器拉 patch。
        var plan = h.Flow.Execute(
            session, Resume(worldPatchVersion: 5), serverHeadDiffSeq: 200, serverWorldPatchVersion: 10);

        Assert.Equal(ResumeDecision.RequireLauncherPatch, plan.Decision);
        Assert.True(plan.CloseConnection);
        Assert.Equal("RequireLauncherPatch", plan.CloseReason);
        Assert.NotNull(plan.Manifest);
        Assert.Equal(10, plan.Manifest!.WorldPatchVersion);
        Assert.Empty(plan.ChunkDiffs);
    }

    [Fact]
    public void VersionGap_ExactlyOne_StillResumesIncrementally()
    {
        var h = new Harness();
        var session = new PlayerSessionState();

        // 边界：差 == 1（serverWorldPatchVersion - 1）不触发 patch。
        var plan = h.Flow.Execute(
            session, Resume(worldPatchVersion: 9), serverHeadDiffSeq: 200, serverWorldPatchVersion: 10);

        Assert.Equal(ResumeDecision.ResumeIncremental, plan.Decision);
    }

    // ── ForceReLogin：客户端状态矛盾 ─────────────────────────────────────

    [Fact]
    public void ClientDiffAboveServerHead_ForcesReLogin()
    {
        var h = new Harness();
        var session = new PlayerSessionState();

        // 客户端自报已应用 diff=500 > 服务器 head=200：内部矛盾，踢回登录。
        var plan = h.Flow.Execute(
            session, Resume(lastAppliedDiffSeq: 500), serverHeadDiffSeq: 200, serverWorldPatchVersion: 10);

        Assert.Equal(ResumeDecision.ForceReLogin, plan.Decision);
        Assert.True(plan.CloseConnection);
        Assert.Equal("ForceReLogin", plan.CloseReason);
        Assert.Empty(plan.ChunkDiffs);
        Assert.Null(plan.Manifest);
    }

    // ── ResendFullChunks：backlog 超容 ───────────────────────────────────

    [Fact]
    public void BacklogOverCapacity_ResendsFullChunks()
    {
        var h = new Harness();
        // InputBufferCapacity=256 → 阈值 256×64 = 16384。
        var session = new PlayerSessionState(new PlayerSessionOptions { InputBufferCapacity = 256 });

        // 客户端落后 20000 > 16384 → 重发全量 chunk。
        var plan = h.Flow.Execute(
            session, Resume(lastAppliedDiffSeq: 0), serverHeadDiffSeq: 20000, serverWorldPatchVersion: 10);

        Assert.Equal(ResumeDecision.ResendFullChunks, plan.Decision);
        Assert.False(plan.CloseConnection);
        Assert.Equal(42UL, h.Baselines.LastCharacterId);
    }

    [Fact]
    public void Backlog_ExactlyAtCapacity_StillIncremental()
    {
        var h = new Harness();
        var session = new PlayerSessionState(new PlayerSessionOptions { InputBufferCapacity = 256 });

        // 边界：backlog == 16384 不超容 → 增量续连。
        var plan = h.Flow.Execute(
            session, Resume(lastAppliedDiffSeq: 0), serverHeadDiffSeq: 16384, serverWorldPatchVersion: 10);

        Assert.Equal(ResumeDecision.ResumeIncremental, plan.Decision);
    }

    // ── 状态归并与幂等 ───────────────────────────────────────────────────

    [Fact]
    public void ApplyReconnect_UpdatesVersionVector_AndRepeatedResumeIsStable()
    {
        var h = new Harness();
        var session = new PlayerSessionState();

        h.Flow.Execute(session, Resume(worldPatchVersion: 9, lastAppliedDiffSeq: 50), 300, 10);
        Assert.Equal(9, session.WorldPatchVersion);
        Assert.Equal(50, session.LastAppliedDiffSeq);
        Assert.Equal(3, session.BaselineVersion);

        // 重复重连（版本向量已更新）：决策仍为增量，且 diff 窗口基于新向量。
        h.Flow.Execute(session, Resume(worldPatchVersion: 9, lastAppliedDiffSeq: 50), 300, 10);
        Assert.Equal(50, h.Diffs.LastSince);
        Assert.Equal(300, h.Diffs.LastHead);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var h = new Harness();
        Assert.Throws<ArgumentNullException>(() => h.Flow.Execute(null!, Resume(), 0, 0));
        Assert.Throws<ArgumentNullException>(() => h.Flow.Execute(new PlayerSessionState(), null!, 0, 0));
    }
}