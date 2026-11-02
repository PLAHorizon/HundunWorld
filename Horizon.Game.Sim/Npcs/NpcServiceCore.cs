using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// NPC 的服务能力位（规格 §2：<c>creature_template.npcflag</c> 的本工程对应物）。
/// <para>
/// <b>位值由本工程自己定义，不照抄 WoW 的十进制值</b>（那边是 <c>1=Gossip、128=Vendor…</c>）。
/// 照抄只会得到一份没人能核对的魔法数：我们的商人不需要占 128 这个号，
/// 而且抄来之后任何一位的语义变更都要同时解释"为什么和暴雪的一样/不一样"。
/// 形状（一个位掩码声明"这个 NPC 会提供哪些服务"）照抄，值不照抄。
/// </para>
/// <para>
/// <b>为什么这里只有七位</b>：只登记目标里点名了的服务——对话、发布任务、交付任务、售卖、技能学习、
/// 制作配方教学、制作设施。旅店/飞行点/仓库/修理在 WoW 的位图里有，但本工程没有对应的后端系统，
/// 现在写进位图就是"声明了却没人读"的第二例（记忆 [schema-key-declared-but-never-applied]）。
/// 等那几套系统真落地时再加位——加位是追加，不是重写。
/// </para>
/// </remarks>
[Flags]
public enum NpcServiceBits : uint
{
    /// <summary>无服务（只有外观与名牌，玩家只能选中它）。</summary>
    None = 0,

    /// <summary>可对话（gossip）。这是所有服务的入口外壳：右键菜单里的"对话"。</summary>
    Gossip = 1u << 0,

    /// <summary>任务发布者。头顶的 <c>!</c> 由 <see cref="NpcQuestIndicatorCore"/> 判，不由本位判。</summary>
    QuestGiver = 1u << 1,

    /// <summary>任务交付者。与 <see cref="QuestGiver"/> 分开是刻意的：WoW 里"给任务的"和"收任务的"经常不是同一个人。</summary>
    QuestTurnIn = 1u << 2,

    /// <summary>商人（有货架）。</summary>
    Vendor = 1u << 3,

    /// <summary>技能训练师（教已有技能树里的式）。</summary>
    Trainer = 1u << 4,

    /// <summary>配方训练师（教制作配方）。</summary>
    CraftTrainer = 1u << 5,

    /// <summary>制作设施（铁砧/药炉一类：站在这里才能做）。</summary>
    CraftStation = 1u << 6,
}

/// <summary>服务入口的隐藏理由。<b>每条拒绝都要回这一个枚举</b>，不许有的分支给 0、有的给文案（记忆 [同族拒绝分支要回同一套对账字段]）。</summary>
public enum NpcServiceHiddenReason : byte
{
    /// <summary>没隐藏，入口可见。</summary>
    Visible = 0,

    /// <summary>模板根本没开这个位。</summary>
    BitOff = 1,

    /// <summary>开了位，但这个 NPC 名下没有可卖的货（货架空 = 窗口打开是空白，等于坏体验）。</summary>
    NoVendorStock = 2,

    /// <summary>开了位，但没有可教的技能行。</summary>
    NoTrainerRows = 3,

    /// <summary>开了位，但没有可教的配方行。</summary>
    NoRecipeRows = 4,

    /// <summary>开了任务位，但这个玩家在这个 NPC 身上没有任何任务关系（可接/进行中/可交付全空）。</summary>
    NoQuestForThisNpc = 5,
}

/// <summary>
/// 服务可见性求值所需的"这个玩家 + 这个 NPC"的事实集合。
/// <para>
/// <b>纪律：每个字段都必须至少被一条可见性规则消费</b>。加一个没人读的字段，
/// 等于给后来人制造"配了没反应"的谜案。
/// </para>
/// </summary>
/// <param name="PlayerLevel">玩家等级。当前只有配方教学用它（<see cref="NpcTrainerCore"/> 的等级门槛）。</param>
/// <param name="VendorItemCount">这个 NPC 货架上的条目数。消费方：<see cref="NpcServiceBits.Vendor"/>。</param>
/// <param name="TrainerRowCount">这个 NPC 名下的技能教学行数。消费方：<see cref="NpcServiceBits.Trainer"/>。</param>
/// <param name="RecipeRowCount">这个 NPC 名下的配方教学行数。消费方：<see cref="NpcServiceBits.CraftTrainer"/>。</param>
/// <param name="Quest">这个 NPC 与该玩家的任务关系摘要。消费方：两个任务位。
/// 用摘要而不是重新查表，是为了让"头顶指示器"和"右键菜单里有没有任务项"<b>必然同进同退</b>——
/// 玩家看到 <c>!</c> 却没有"交任务"可点，是这类系统最刺眼的错。</param>
public readonly struct NpcServiceContext(
    int playerLevel,
    int vendorItemCount,
    int trainerRowCount,
    int recipeRowCount,
    NpcQuestCounts @quest)
{
    public readonly int PlayerLevel = playerLevel;
    public readonly int VendorItemCount = vendorItemCount;
    public readonly int TrainerRowCount = trainerRowCount;
    public readonly int RecipeRowCount = recipeRowCount;
    public readonly NpcQuestCounts Quest = @quest;

    /// <summary>空上下文（编辑器预览用：只按位图判可见性，不带玩家态）。</summary>
    public static NpcServiceContext TemplateOnly => new NpcServiceContext(0, 0, 0, 0, default);

    /// <summary>把某个计数字段按位取出来（<see cref="Describe"/> 与诊断回执用）。</summary>
    public readonly int CountOf(NpcServiceBits bit) => bit switch
    {
        NpcServiceBits.Vendor => VendorItemCount,
        NpcServiceBits.Trainer => TrainerRowCount,
        NpcServiceBits.CraftTrainer => RecipeRowCount,
        _ => 0,
    };
}

/// <summary>一个可见/不可见服务入口的回执。</summary>
public readonly struct NpcServiceEntry(NpcServiceBits bit, bool visible, NpcServiceHiddenReason reason)
{
    public readonly NpcServiceBits Bit = bit;
    public readonly bool Visible = visible;
    public readonly NpcServiceHiddenReason Reason = reason;

    /// <summary>可见性判据的 ASCII 自证串（日志里中文标签会吞掉紧随其后的数字，故键名用 ASCII）。</summary>
    public readonly string Describe() => $"svc={Bit} visible={(Visible ? 1 : 0)} reason={(byte)Reason}";
}

/// <summary>
/// NPC 服务位图 → 玩家可见入口列表（规格 §2.3）。
/// <para>
/// <b>为什么"能力"与"可见"要分两跳</b>：位图声明的是"这个 NPC <i>能</i>提供商人服务"，
/// 玩家看到的却是"我右键它时菜单里到底有没有'购买'这一项"。中间那一跳取决于玩家态与内容态
/// （货架空不空、有没有任务关系）。WoW 观测到的正是这个两跳：同一个 NPC 对死掉的玩家、
/// 对没任务的玩家，菜单项就是不一样。
/// </para>
/// <para>
/// <b>纯函数、无 IO</b>：编辑器预览、CLI <c>validate</c>、运行期下发走同一份代码——
/// 任何一方自己再判一遍，就会出现"编辑器里显示有商人、进游戏点不到"这类无法归因的分裂。
/// </para>
/// </summary>
public static class NpcServiceCore
{
    /// <summary>判据版本，进握手与金轨迹。</summary>
    public const int Version = 1;

    /// <summary>
    /// 入口的固定展示顺序。<b>必须是这张数组而不是枚举数值序</b>：右键菜单的顺序是玩家可观测的产品决策，
    /// 将来加位时不能因为新位的数值小就插到"对话"前面。
    /// </summary>
    public static readonly NpcServiceBits[] DisplayOrder =
    {
        NpcServiceBits.QuestGiver,
        NpcServiceBits.QuestTurnIn,
        NpcServiceBits.Vendor,
        NpcServiceBits.Trainer,
        NpcServiceBits.CraftTrainer,
        NpcServiceBits.CraftStation,
        NpcServiceBits.Gossip,
    };

    /// <summary>单个位的可见性判据。</summary>
    public static NpcServiceEntry Evaluate(NpcServiceBits bit, uint serviceFlags, in NpcServiceContext ctx)
    {
        if ((serviceFlags & (uint)bit) == 0)
            return new NpcServiceEntry(bit, false, NpcServiceHiddenReason.BitOff);

        var reason = bit switch
        {
            // 货架空 = 打开是白窗，按不可见处理（规格 §5.2：商人的清单本身就是数据）。
            NpcServiceBits.Vendor => ctx.VendorItemCount > 0
                ? NpcServiceHiddenReason.Visible
                : NpcServiceHiddenReason.NoVendorStock,

            NpcServiceBits.Trainer => ctx.TrainerRowCount > 0
                ? NpcServiceHiddenReason.Visible
                : NpcServiceHiddenReason.NoTrainerRows,

            NpcServiceBits.CraftTrainer => ctx.RecipeRowCount > 0
                ? NpcServiceHiddenReason.Visible
                : NpcServiceHiddenReason.NoRecipeRows,

            // 两个任务位共用同一份摘要：头顶符号与菜单项必须同进同退（见 NpcServiceContext.Quest 注释）。
            NpcServiceBits.QuestGiver or NpcServiceBits.QuestTurnIn => ctx.Quest.HasAny
                ? NpcServiceHiddenReason.Visible
                : NpcServiceHiddenReason.NoQuestForThisNpc,

            // 设施本身是个物理对象，玩家知道/不知道配方都进得去这个窗口（WoW 的铁砧就是如此）：
            // 开了位就可见，"能做什么"交给 NpcCraftCore 判。
            NpcServiceBits.CraftStation => NpcServiceHiddenReason.Visible,

            // 对话永远跟着位走——它是其他入口的壳。
            NpcServiceBits.Gossip => NpcServiceHiddenReason.Visible,

            _ => NpcServiceHiddenReason.BitOff,
        };

        return new NpcServiceEntry(bit, reason == NpcServiceHiddenReason.Visible, reason);
    }

    /// <summary>
    /// 把"开了位但内容为空"的三个数据位摘掉：商人（无货架行）/技能训练（无技能行）/配方训练（无配方行）。
    /// <para>
    /// <b>为什么要在装配期就摘</b>：位图是这个 NPC 的**服务身份**，客户端拿它决定"右键有哪些项"、
    /// 服务端拿它决定"接不接受某条请求"。位开着而数据为空时，两边的表现分别是"菜单里有'看看货架'"
    /// 与"点下去回 not_a_vendor"——玩家看到的是点了没反应，而日志里全是正常拒绝。
    /// 摘掉后两侧自然一致：没有入口，也没有请求。
    /// </para>
    /// <para>
    /// <b>只摘数据决定的位</b>：任务位取决于"这个玩家"与 NPC 的关系（运行时才知道），
    /// 设施与对话是物理/壳层入口（见 <see cref="Evaluate"/> 的判据），都不在这里处理。
    /// </para>
    /// </summary>
    public static uint PruneDataEmpty(uint serviceFlags, int vendorItemCount, int trainerRowCount, int recipeRowCount)
    {
        var flags = serviceFlags;
        if ((flags & (uint)NpcServiceBits.Vendor) != 0 && vendorItemCount <= 0)
            flags &= ~(uint)NpcServiceBits.Vendor;
        if ((flags & (uint)NpcServiceBits.Trainer) != 0 && trainerRowCount <= 0)
            flags &= ~(uint)NpcServiceBits.Trainer;
        if ((flags & (uint)NpcServiceBits.CraftTrainer) != 0 && recipeRowCount <= 0)
            flags &= ~(uint)NpcServiceBits.CraftTrainer;
        return flags;
    }

    /// <summary>
    /// 全量求值，按 <see cref="DisplayOrder"/> 返回。
    /// 返回长度恒等于 <see cref="DisplayOrder"/> 长度——<b>包括全不可见时</b>：
    /// 调用方（编辑器与客户端）要能区分"这个位没开"与"这条入口我没算到"，前者是数据、后者是缺陷。
    /// </summary>
    public static List<NpcServiceEntry> Resolve(uint serviceFlags, in NpcServiceContext ctx)
    {
        var list = new List<NpcServiceEntry>(DisplayOrder.Length);
        for (var i = 0; i < DisplayOrder.Length; i++)
        {
            list.Add(Evaluate(DisplayOrder[i], serviceFlags, ctx));
        }

        return list;
    }

    /// <summary>可见入口的位掩码（下发给客户端的紧凑形式；客户端仍不自己判可见性）。</summary>
    public static uint VisibleMask(uint serviceFlags, in NpcServiceContext ctx)
    {
        uint mask = 0;
        var entries = Resolve(serviceFlags, ctx);
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Visible) mask |= (uint)entries[i].Bit;
        }

        return mask;
    }

    /// <summary>位图的字面展开（编辑器的"这个位图到底开了哪几项"面板；不要让 UI 自己再解析一遍位）。</summary>
    public static string DescribeBits(uint serviceFlags)
    {
        if (serviceFlags == 0) return "none";

        var parts = new List<string>(8);
        foreach (var bit in Enum.GetValues<NpcServiceBits>())
        {
            if (bit != NpcServiceBits.None && (serviceFlags & (uint)bit) != 0) parts.Add(bit.ToString());
        }

        var joined = string.Join("|", parts);
        return parts.Count == 0 ? $"unknown:{serviceFlags}" : joined;
    }

    /// <summary>
    /// 校验未识别的位。<b>必须显式拒绝死位</b>：往 uint 里塞一个没人消费的位，
    /// 症状是"编辑器勾了、游戏里没反应"，且不报错。
    /// </summary>
    public static uint UnknownBits(uint serviceFlags)
    {
        uint known = 0;
        foreach (var bit in Enum.GetValues<NpcServiceBits>())
        {
            if (bit != NpcServiceBits.None) known |= (uint)bit;
        }

        return serviceFlags & ~known;
    }
}
