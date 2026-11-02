using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Npcs;
using HundunWorld.Game.Selection;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 客户端头顶任务符号（<see cref="NpcQuestMark"/>）的语义与它和权威内核
/// <see cref="NpcQuestIndicatorCore"/> 的一致性。
/// <para>
/// 这份逻辑在客户端，**没有服务端兜底**——算错了不会有人拒绝请求，玩家只会"看不见问号"或
/// "看见金色叹号却接不了"。判据的输入全部是服务端裁决过的事实：
/// 清单（等级/前置已过滤）、<c>LockedOfferCount</c>（条件不足条数）、
/// 以及按发布者缓存的"交付者就是我"任务号集合。
/// </para>
/// <para>
/// <b>已知的输入域差异</b>：服务端过滤后的可接清单里没有"条件不足"的任务，
/// 内核的 <c>LockedOffer</c> 在这边由 <c>lockedOfferCount</c> 单独表达；
/// "已交过且不可重复"的清单项客户端按"不提示"处理，也不进等价对照。
/// </para>
/// </summary>
public class NpcQuestMarkTests
{
    private static QuestOfferInfo Offer(int id, bool accepted, bool finished = false) => new()
    {
        QuestId = id, AlreadyAccepted = accepted, AlreadyFinished = finished,
    };

    private static QuestSummary Quest(int id, bool done, bool withObjectives = true, int status = 0) => new()
    {
        QuestId = id,
        Status = status,
        Objectives = withObjectives
            ? new List<QuestObjectiveWire> { new() { Completed = done } }
            : new List<QuestObjectiveWire>(),
    };

    private static IReadOnlyCollection<int> Here(params int[] ids) => ids;

    [Fact]
    public void 可交付优先于可接()
    {
        var (kind, count) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: true), Offer(2, accepted: false) },
            new[] { Quest(1, done: true) },
            lockedOfferCount: 0,
            turnInHereQuestIds: Here(1));

        Assert.Equal(NpcQuestMarkKind.TurnIn, kind);
        Assert.Equal(1, count);
    }

    [Fact]
    public void 可接优先于进行中()
    {
        var (kind, count) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: true), Offer(2, accepted: false) },
            new[] { Quest(1, done: false) },
            lockedOfferCount: 0,
            turnInHereQuestIds: Here(1));

        Assert.Equal(NpcQuestMarkKind.Available, kind);
        Assert.Equal(1, count);
    }

    [Fact]
    public void 只有进行中时显示灰问号()
    {
        var (kind, count) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: true) },
            new[] { Quest(1, done: false) },
            lockedOfferCount: 0,
            turnInHereQuestIds: Here(1));

        Assert.Equal(NpcQuestMarkKind.InProgress, kind);
        Assert.Equal(1, count);
        Assert.Equal("?", NpcQuestMark.GlyphOf(kind));
    }

    [Fact]
    public void 交付者在别处时发布者头上不画问号()
    {
        // 已接、目标也做完了，但交付者不是这位：WoW 里发布者头上不该亮黄 ?。
        var (kind, count) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: true) },
            new[] { Quest(1, done: true) },
            lockedOfferCount: 0,
            turnInHereQuestIds: Here());

        Assert.Equal(NpcQuestMarkKind.None, kind);
        Assert.Equal(0, count);
    }

    [Fact]
    public void 交付者面前没有清单也能画_身上的任务参与判据()
    {
        var (kind, count) = NpcQuestMark.Resolve(
            Array.Empty<QuestOfferInfo>(),
            new[] { Quest(1, done: true) },
            lockedOfferCount: 0,
            turnInHereQuestIds: Here(1));

        Assert.Equal(NpcQuestMarkKind.TurnIn, kind);
        Assert.Equal(1, count);
    }

    [Fact]
    public void 行囊里已交付的旧记录不再提示()
    {
        var (kind, _) = NpcQuestMark.Resolve(
            Array.Empty<QuestOfferInfo>(),
            new[] { Quest(1, done: true, status: 2) },
            lockedOfferCount: 0,
            turnInHereQuestIds: Here(1));

        Assert.Equal(NpcQuestMarkKind.None, kind);
    }

    [Fact]
    public void 条件不足显示灰叹号并带条数()
    {
        var (kind, count) = NpcQuestMark.Resolve(
            Array.Empty<QuestOfferInfo>(),
            Array.Empty<QuestSummary>(),
            lockedOfferCount: 3,
            turnInHereQuestIds: Here());

        Assert.Equal(NpcQuestMarkKind.Locked, kind);
        Assert.Equal(3, count);
        Assert.Equal("!", NpcQuestMark.GlyphOf(kind));
    }

    [Fact]
    public void 进行中优先于条件不足()
    {
        var (kind, _) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: true) },
            new[] { Quest(1, done: false) },
            lockedOfferCount: 2,
            turnInHereQuestIds: Here(1));

        Assert.Equal(NpcQuestMarkKind.InProgress, kind);
    }

    [Fact]
    public void 同类多条要带条数()
    {
        var (kind, count) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: false), Offer(2, accepted: false) },
            Array.Empty<QuestSummary>(),
            lockedOfferCount: 0,
            turnInHereQuestIds: Here());

        Assert.Equal(NpcQuestMarkKind.Available, kind);
        Assert.Equal(2, count);
        Assert.Equal("!", NpcQuestMark.GlyphOf(kind));
    }

    [Fact]
    public void 已交过且不可重复的清单项不提示()
    {
        var (kind, count) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: false, finished: true) },
            Array.Empty<QuestSummary>(),
            lockedOfferCount: 0,
            turnInHereQuestIds: Here());

        Assert.Equal(NpcQuestMarkKind.None, kind);
        Assert.Equal(0, count);
    }

    [Fact]
    public void 已接但身上没有这条任务_按无关系处理()
    {
        var (kind, count) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: true) },
            Array.Empty<QuestSummary>(),
            lockedOfferCount: 0,
            turnInHereQuestIds: Here(1));

        Assert.Equal(NpcQuestMarkKind.None, kind);
        Assert.Equal(0, count);
    }

    [Fact]
    public void 空目标列表按未完成处理_宁可灰问号也不谎报可交付()
    {
        var (kind, _) = NpcQuestMark.Resolve(
            new[] { Offer(1, accepted: true) },
            new[] { Quest(1, done: true, withObjectives: false) },
            lockedOfferCount: 0,
            turnInHereQuestIds: Here(1));

        Assert.Equal(NpcQuestMarkKind.InProgress, kind);
    }

    /// <summary>
    /// 与权威内核逐组合对照：客户端清单里能表达的三种关系（可接 / 已接未完成 / 已接已完成）
    /// 映射成 <see cref="NpcQuestRow"/> 后，内核给出的符号与条数必须与客户端一致。
    /// <para>这条能抓住"有人只改了一边"——比如把交付优先级挪到可接之后，或把条数改成去重。</para>
    /// </summary>
    [Fact]
    public void 与权威内核在可表达输入域内逐组合一致()
    {
        var states = new[]
        {
            (Accepted: false, Done: false, HasQuest: false),
            (Accepted: true, Done: false, HasQuest: true),
            (Accepted: true, Done: true, HasQuest: true),
        };

        foreach (var a in states)
        foreach (var b in states)
        {
            var offers = new List<QuestOfferInfo>();
            var quests = new List<QuestSummary>();
            var rows = new List<NpcQuestRow>();
            var turnInHere = new List<int>();

            void Add(int questId, bool accepted, bool done, bool hasQuest)
            {
                offers.Add(Offer(questId, accepted));
                if (hasQuest)
                {
                    quests.Add(Quest(questId, done));
                    if (accepted) rows.Add(new NpcQuestRow(questId, true, true, true, done, true));
                }

                if (accepted) turnInHere.Add(questId);
                if (!accepted) rows.Add(new NpcQuestRow(questId, true, true, false, false, true));
            }

            Add(1, a.Accepted, a.Done, a.HasQuest);
            Add(2, b.Accepted, b.Done, b.HasQuest);

            var (clientKind, clientCount) = NpcQuestMark.Resolve(
                offers.ToArray(), quests.ToArray(), lockedOfferCount: 0, turnInHereQuestIds: turnInHere);
            var kernelCounts = NpcQuestIndicatorCore.Accumulate(rows);
            var kernelKind = NpcQuestIndicatorCore.Resolve(kernelCounts);

            var expectedKind = clientKind switch
            {
                NpcQuestMarkKind.TurnIn => NpcQuestIndicatorKind.TurnInReady,
                NpcQuestMarkKind.Available => NpcQuestIndicatorKind.OfferAvailable,
                NpcQuestMarkKind.InProgress => NpcQuestIndicatorKind.InProgress,
                _ => NpcQuestIndicatorKind.None,
            };

            Assert.True(expectedKind == kernelKind,
                $"符号不一致：client={clientKind} kernel={kernelKind} rows={string.Join(",", rows.Select(r => $"{r.QuestId}/{r.Accepted}/{r.ObjectivesDone}"))}");
            Assert.True(clientCount == NpcQuestIndicatorCore.CountOf(kernelCounts, kernelKind),
                $"条数不一致：client={clientCount} kernel={NpcQuestIndicatorCore.CountOf(kernelCounts, kernelKind)}");
        }
    }
}
