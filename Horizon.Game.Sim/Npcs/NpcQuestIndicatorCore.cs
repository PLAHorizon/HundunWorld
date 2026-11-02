using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// 头顶任务符号的种类（规格 §4.1，五种形态 + 一个"无"）。
/// <para>
/// <b>0 是"无符号"，不是任何合法符号</b>：<see cref="None"/> 只在四类计数全为 0 时出现，
/// 由 <see cref="NpcQuestIndicatorCore.Resolve"/> 保证。任何"计数不全为 0 却回了 None"的分支都是缺陷，
/// 单测按这条不变量断言（记忆 [结构体默认 0 会撞成合法下标]：这里的防线是"None ⇔ 全空"这条双向断言，
/// 而不是给默认值另找一个哨兵）。
/// </para>
/// </summary>
public enum NpcQuestIndicatorKind : byte
{
    /// <summary>不显示符号。当且仅当四类计数全为 0。</summary>
    None = 0,

    /// <summary>黄色 <c>?</c>：有可交付的任务。<b>优先级最高</b>——玩家最该先做这件事。</summary>
    TurnInReady = 1,

    /// <summary>黄色 <c>!</c>：有新任务可接，且门槛已过。</summary>
    OfferAvailable = 2,

    /// <summary>灰色 <c>?</c>：已接、目标未达成（或达成了但交付者不是这个 NPC）。</summary>
    InProgress = 3,

    /// <summary>灰色 <c>!</c>：条件不足（等级不够 / 前置链未走完）。</summary>
    LockedOffer = 4,
}

/// <summary>单条任务在这个 NPC 与这个玩家身上的关系行（聚合的输入）。</summary>
/// <param name="QuestId">任务模板 id。<b>0 是合法 id 吗？不是</b>——任务表自增 id 从 1 起，
/// 但这里的 0 只可能来自未初始化的结构体，因此 <see cref="NpcQuestIndicatorCore.Classify"/>
/// 对 <c>QuestId &lt;= 0</c> 的行直接判成 <see cref="NpcIndicatorBucket.Ignored"/> 并记账，不静默参与统计。</param>
/// <param name="IsGiverHere">这个 NPC 是该任务的发布者。</param>
/// <param name="IsTurnInHere">这个 NPC 是该任务的交付者。</param>
/// <param name="Accepted">玩家已接。</param>
/// <param name="ObjectivesDone">目标已达成。</param>
/// <param name="GatePassed">接取门槛（等级/前置链）是否通过。<b>已接的行不看这一位</b>——
/// 接上之后门槛变化不该让已接任务从头顶消失。</param>
public readonly struct NpcQuestRow(
    int questId,
    bool isGiverHere,
    bool isTurnInHere,
    bool accepted,
    bool objectivesDone,
    bool gatePassed)
{
    public readonly int QuestId = questId;
    public readonly bool IsGiverHere = isGiverHere;
    public readonly bool IsTurnInHere = isTurnInHere;
    public readonly bool Accepted = accepted;
    public readonly bool ObjectivesDone = objectivesDone;
    public readonly bool GatePassed = gatePassed;
}

/// <summary>一行任务被归进哪一格。<see cref="Ignored"/> 与 <see cref="NotRelated"/> 都进计数器的"不计"，但理由码不同——诊断要能分辨。</summary>
public enum NpcIndicatorBucket : byte
{
    /// <summary>可交付（黄 <c>?</c>）。</summary>
    TurnInReady = 0,

    /// <summary>可接（黄 <c>!</c>）。</summary>
    OfferAvailable = 1,

    /// <summary>进行中（灰 <c>?</c>）。</summary>
    InProgress = 2,

    /// <summary>条件不足（灰 <c>!</c>）。</summary>
    LockedOffer = 3,

    /// <summary>这个 NPC 与该任务无发布/交付关系。</summary>
    NotRelated = 4,

    /// <summary>行本身不可用（id 非正数）。<b>绝不静默丢弃</b>：调用方要能数出有几条坏行。</summary>
    Ignored = 5,
}

/// <summary>
/// 四类计数。<b>刻意不用"一个 kind + 一个 count"</b>：数字前缀（<c>2!</c> / <c>3?</c>）按种类各自计数，
/// 玩家同时有 2 个可接和 1 个可交付时看到的是 <c>?</c> 而不是 <c>3</c>——只留一个数就表达不了这件事。
/// </summary>
public readonly struct NpcQuestCounts(
    int turnInReady,
    int offerAvailable,
    int inProgress,
    int lockedOffer)
{
    public readonly int TurnInReady = turnInReady;
    public readonly int OfferAvailable = offerAvailable;
    public readonly int InProgress = inProgress;
    public readonly int LockedOffer = lockedOffer;

    /// <summary>有没有任何任务关系（<see cref="NpcServiceCore"/> 用它决定两个任务位的可见性）。</summary>
    public readonly bool HasAny => TurnInReady > 0 || OfferAvailable > 0 || InProgress > 0 || LockedOffer > 0;

    /// <summary>ASCII 自证串（中文标签会吞掉紧随其后的数字）。</summary>
    public readonly string Describe() =>
        $"turnin={TurnInReady} offer={OfferAvailable} progress={InProgress} locked={LockedOffer}";
}

/// <summary>
/// 头顶任务指示器的确定性判据（规格 §4）。
/// <para>
/// <b>为什么判据必须在服务端</b>：指示器直接指挥玩家"要不要点这个 NPC"，
/// 客户端自算等于把接取门槛交给客户端——与 <c>SkillHandler.cs:184-203</c> 拒绝
/// 客户端上报 <c>LearnMethod</c> 是同一个理由（那边注释写得很清楚：白读一卷不存在的书）。
/// 见设计文档决策 D3。
/// </para>
/// <para>
/// <b>与 <c>QuestManagerGrain</c> 的分工</b>：那边判"这个玩家能不能接这一条"（写库与发奖前的权威校验），
/// 这边判"该画哪个符号"。两边共用同一份门槛结论（由调用方取回后传入 <see cref="NpcQuestRow.GatePassed"/>），
/// 所以不存在第二套门槛判据。<b>这边绝不重新查表</b>——查表是 IO，本类保持纯函数。
/// </para>
/// </summary>
public static class NpcQuestIndicatorCore
{
    /// <summary>判据版本，进握手与金轨迹。</summary>
    public const int Version = 1;

    /// <summary>
    /// 单行归类。判据顺序即优先级，逐条对应规格 §4.1 的观测：
    /// 已接且目标达成且这个 NPC 是交付者 → 可交付；
    /// 已接但（目标未达成 或 交付者不是它） → 进行中；
    /// 未接且门槛过且这个 NPC 是发布者 → 可接；
    /// 未接且门槛未过且这个 NPC 是发布者 → 条件不足。
    /// </summary>
    public static NpcIndicatorBucket Classify(in NpcQuestRow row)
    {
        if (row.QuestId <= 0) return NpcIndicatorBucket.Ignored;

        if (row.Accepted)
        {
            // 已接的行不再看门槛：等级掉下去（换装备/改境界）也不该让头顶的进行中消失。
            if (row.ObjectivesDone)
            {
                // 目标达成但交付者不是这个 NPC → 在这个 NPC 头上仍是"灰 ?"（等你去别处交）。
                return row.IsTurnInHere ? NpcIndicatorBucket.TurnInReady : NpcIndicatorBucket.InProgress;
            }

            return NpcIndicatorBucket.InProgress;
        }

        if (!row.IsGiverHere) return NpcIndicatorBucket.NotRelated;

        return row.GatePassed ? NpcIndicatorBucket.OfferAvailable : NpcIndicatorBucket.LockedOffer;
    }

    /// <summary>把若干行聚合成四类计数。<b>一条坏行（id 非正）不改变任何合法计数</b>，但会进诊断回执。</summary>
    public static NpcQuestCounts Accumulate(IEnumerable<NpcQuestRow> rows, out int ignoredRows)
    {
        int turnIn = 0, offer = 0, progress = 0, locked = 0;
        ignoredRows = 0;

        foreach (var row in rows)
        {
            switch (Classify(row))
            {
                case NpcIndicatorBucket.TurnInReady: turnIn++; break;
                case NpcIndicatorBucket.OfferAvailable: offer++; break;
                case NpcIndicatorBucket.InProgress: progress++; break;
                case NpcIndicatorBucket.LockedOffer: locked++; break;
                case NpcIndicatorBucket.Ignored: ignoredRows++; break;
                case NpcIndicatorBucket.NotRelated: break;
                default: ignoredRows++; break;
            }
        }

        return new NpcQuestCounts(turnIn, offer, progress, locked);
    }

    /// <summary>聚合（不关心坏行数时的简写）。</summary>
    public static NpcQuestCounts Accumulate(IEnumerable<NpcQuestRow> rows)
    {
        return Accumulate(rows, out _);
    }

    /// <summary>当前该画哪个符号。<b>不变量：<c>None ⇔ !HasAny</c></b>（双向，单测按这两向断言）。</summary>
    public static NpcQuestIndicatorKind Resolve(in NpcQuestCounts counts)
    {
        if (counts.TurnInReady > 0) return NpcQuestIndicatorKind.TurnInReady;
        if (counts.OfferAvailable > 0) return NpcQuestIndicatorKind.OfferAvailable;
        if (counts.InProgress > 0) return NpcQuestIndicatorKind.InProgress;
        if (counts.LockedOffer > 0) return NpcQuestIndicatorKind.LockedOffer;
        return NpcQuestIndicatorKind.None;
    }

    /// <summary>
    /// 符号本体（不含数字前缀）。返回 ASCII 两字符串，第一个字符是符号、第二个是色调档
    /// （<c>Y</c>=黄、<c>G</c>=灰）；客户端据此选图，服务端不产出颜色常量。
    /// <para>这里刻意用 ASCII 而不是中文标签：跨端日志与对账脚本按子串匹配中文会失配（既有教训）。</para>
    /// </summary>
    public static string GlyphOf(NpcQuestIndicatorKind kind) => kind switch
    {
        NpcQuestIndicatorKind.TurnInReady => "?Y",
        NpcQuestIndicatorKind.OfferAvailable => "!Y",
        NpcQuestIndicatorKind.InProgress => "?G",
        NpcQuestIndicatorKind.LockedOffer => "!G",
        _ => string.Empty,
    };

    /// <summary>
    /// 数字前缀。<b>可交付优先显示可交付的条数</b>（与 <see cref="Resolve"/> 同一条优先级，
    /// 两处顺序若不一致，玩家会看到"符号是 ?、数字是可接的条数"这种自相矛盾的串）。
    /// 只有一条时返回 0，表示"不画数字"——观测：WoW 单任务时不带前缀。
    /// </summary>
    public static int PrefixCountOf(in NpcQuestCounts counts)
    {
        var kind = Resolve(counts);
        var n = CountOf(counts, kind);
        return n > 1 ? n : 0;
    }

    /// <summary>取某个符号种类对应的条数。</summary>
    public static int CountOf(in NpcQuestCounts counts, NpcQuestIndicatorKind kind) => kind switch
    {
        NpcQuestIndicatorKind.TurnInReady => counts.TurnInReady,
        NpcQuestIndicatorKind.OfferAvailable => counts.OfferAvailable,
        NpcQuestIndicatorKind.InProgress => counts.InProgress,
        NpcQuestIndicatorKind.LockedOffer => counts.LockedOffer,
        _ => 0,
    };

    /// <summary>完整自证串（ASCII 键名，给日志与 CLI 回执用）。</summary>
    public static string Describe(in NpcQuestCounts counts) =>
        $"indicator={GlyphOf(Resolve(counts))} prefix={PrefixCountOf(counts)} {counts.Describe()}";
}
