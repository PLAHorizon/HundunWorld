using System;

namespace Horizon.Game.Sim.Quests;

/// <summary>交付 NPC 校验的拒绝档。<b>四类拒绝必须分得开</b>，否则"交不了"只剩一句"失败"。</summary>
public enum QuestTurnInReject : byte
{
    /// <summary>通过（要求交付者时确实站在正确的那一位面前）。</summary>
    None = 0,

    /// <summary>任务要求到指定对象处交付，但玩家没带交互对象（空 id）。</summary>
    MissingEntity = 1,

    /// <summary>服务端认不出这个实体（不在本分片 / 不在交互距离 / 注册表里没有）。</summary>
    UnresolvedEntity = 2,

    /// <summary>对象类别不对（例如任务要交给 NPC，玩家递给了物件）。</summary>
    WrongKind = 3,

    /// <summary>对象不是任务指定的那一个（类别对、模板号不对）。</summary>
    WrongTemplate = 4,
}

/// <summary>一次交付身份校验的完整回执。</summary>
public readonly struct QuestTurnInVerdict(
    bool ok,
    int requiredKind,
    int requiredTemplateId,
    int providedKind,
    int providedTemplateId,
    QuestTurnInReject reason)
{
    public readonly bool Ok = ok;
    public readonly int RequiredKind = requiredKind;
    public readonly int RequiredTemplateId = requiredTemplateId;
    public readonly int ProvidedKind = providedKind;
    public readonly int ProvidedTemplateId = providedTemplateId;
    public readonly QuestTurnInReject Reason = reason;

    public readonly string Describe() =>
        $"turnin_ok={(Ok ? 1 : 0)} turnin_req_kind={RequiredKind} turnin_req={RequiredTemplateId} " +
        $"turnin_got_kind={ProvidedKind} turnin_got={ProvidedTemplateId} turnin_reject={(byte)Reason}";
}

/// <summary>
/// 任务交付对象校验（D11）：<b>任务指定了交付者，就只能在那一处交付</b>。
/// <para>
/// 口径与 WoW 一致：模板 <c>turn_in_kind/turn_in_template_id</c> 为 0 表示"沿用发布者"；
/// 两者最终都为 0 表示"哪里都能交"（不限制）。本类只做"该不该拒绝"，**不认识距离与注册表**——
/// 那是 <c>QuestInteractionAuthority</c> 的活，调用方把解析结果带进来。
/// </para>
/// <para>
/// <b>为什么不直接比较客户端报的模板号</b>：客户端报的 id 是"我认为它是什么"，
/// 而任务指定的交付者是**内容**。两边各自的真相不能混用，所以这里比较的是
/// "服务端解析出来的实体身份" 与 "任务模板要求的身份"。
/// </para>
/// </summary>
public static class QuestTurnInCore
{
    /// <summary>判据版本：改语义时递增。</summary>
    public const int Version = 1;

    /// <summary>类别：NPC（与 <c>QuestTemplate.giver_kind</c> 同值，1NPC 2怪物 3物品 4物件 5区域）。</summary>
    public const int KindNpc = 1;

    /// <summary>
    /// 生效的交付者身份：模板里 0 表示"沿用发布者"。
    /// </summary>
    public static (int Kind, int TemplateId) ResolveRequired(int giverKind, int giverTemplateId, int turnInKind, int turnInTemplateId)
        => (turnInKind > 0 ? turnInKind : giverKind,
            turnInTemplateId > 0 ? turnInTemplateId : giverTemplateId);

    /// <summary>
    /// 校验一次交付。
    /// </summary>
    /// <param name="requiredKind">生效的交付者类别（0 = 不限制）。</param>
    /// <param name="requiredTemplateId">生效的交付者模板号（≤0 = 不限制）。</param>
    /// <param name="providedEntityId">玩家报的交互对象实体 id（0 = 没带）。</param>
    /// <param name="resolved">服务端是否认出了这个实体（存在 + 本分片 + 在交互距离内）。</param>
    /// <param name="providedKind">解析出的类别（resolved=false 时忽略）。</param>
    /// <param name="providedTemplateId">解析出的模板号（resolved=false 时忽略）。</param>
    public static QuestTurnInVerdict Evaluate(
        int requiredKind,
        int requiredTemplateId,
        ulong providedEntityId,
        bool resolved,
        int providedKind,
        int providedTemplateId)
    {
        // 没指定交付者 = 不限制；这时带不带对象都通过（自由交付是合法配置）。
        if (requiredTemplateId <= 0)
            return new QuestTurnInVerdict(true, requiredKind, 0, providedKind, providedTemplateId, QuestTurnInReject.None);

        if (providedEntityId == 0UL)
            return new QuestTurnInVerdict(false, requiredKind, requiredTemplateId, 0, 0, QuestTurnInReject.MissingEntity);

        if (!resolved)
            return new QuestTurnInVerdict(false, requiredKind, requiredTemplateId, 0, 0, QuestTurnInReject.UnresolvedEntity);

        if (requiredKind > 0 && providedKind != requiredKind)
            return new QuestTurnInVerdict(false, requiredKind, requiredTemplateId, providedKind, providedTemplateId,
                QuestTurnInReject.WrongKind);

        if (providedTemplateId != requiredTemplateId)
            return new QuestTurnInVerdict(false, requiredKind, requiredTemplateId, providedKind, providedTemplateId,
                QuestTurnInReject.WrongTemplate);

        return new QuestTurnInVerdict(true, requiredKind, requiredTemplateId, providedKind, providedTemplateId,
            QuestTurnInReject.None);
    }
}

/// <summary>
/// 任务**发布者**校验：与交付同一条纪律——任务指定了发布者，就只能在那一处接。
/// <para>
/// 不校验时：直连一条 questId 就能在任何地方接走高等级任务，而"可接列表"是按发布者过滤的——
/// 列表与执行两条入口不同数（与制作清单/动作那对缺陷同形）。
/// </para>
/// <para>判据完全复用 <see cref="QuestTurnInCore.Evaluate"/>（同一个"身份对不对"的形状），本类只是命名与文档。</para>
/// </summary>
public static class QuestGiverCore
{
    /// <summary>生效的发布者身份（giver_kind/template 为 0 表示不限制）。</summary>
    public static (int Kind, int TemplateId) Required(int giverKind, int giverTemplateId)
        => (giverKind, giverTemplateId);

    /// <summary>校验一次接取；参数语义与 <see cref="QuestTurnInCore.Evaluate"/> 一一对应。</summary>
    public static QuestTurnInVerdict Evaluate(
        int giverKind,
        int giverTemplateId,
        ulong providedEntityId,
        bool resolved,
        int providedKind,
        int providedTemplateId)
        => QuestTurnInCore.Evaluate(giverKind, giverTemplateId, providedEntityId, resolved, providedKind, providedTemplateId);

    /// <summary>接取侧的一句话拒绝理由（与交付侧分开：玩家看到"交付"会以为走错了流程）。</summary>
    public static string RejectMessage(QuestTurnInReject reject) => reject switch
    {
        QuestTurnInReject.MissingEntity => "这个任务需要在发布者处接取",
        QuestTurnInReject.UnresolvedEntity => "附近没有可接取该任务的对象",
        QuestTurnInReject.WrongKind => "这个对象不是该任务的发布者",
        QuestTurnInReject.WrongTemplate => "该任务不在这个对象处发布",
        _ => string.Empty,
    };
}
