using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>训练师这一行的服务种类（对应 <see cref="NpcServiceBits.Trainer"/> 与 <see cref="NpcServiceBits.CraftTrainer"/> 两个位）。</summary>
public enum NpcTrainerService : byte
{
    /// <summary>未登记。<b>不当成技能教学处理</b>：漏填的语义应该是"这条行不可用"，而不是"教第一式"。</summary>
    Unknown = 0,

    /// <summary>技能教学（玩家技能树里的一式）。</summary>
    Skill = 1,

    /// <summary>配方教学（制作图纸）。消费方：<c>NpcCraftCore</c> 与 <c>NpcRecipeTaught</c> 表。</summary>
    Recipe = 2,
}

/// <summary>
/// 训练师货架的一行。
/// <para>
/// <b>这里刻意没有 <c>req_level</c> / <c>req_realm</c> / <c>req_prerequisite_skill</c> 三列</b>——
/// 本工程的游戏门槛（等级、境界、五行亲和、前置式、演化源、周天道侣、动态好感/材料/属性）
/// 全部已经在 <c>SkillGrain.CheckLearnGates</c>（<c>SkillGrain.cs:173-222</c>）里算过一次，
/// 并且那套判据有 6 组 live 测试钉着。在 NPC 侧再存一份"这个训练师要求 Lv30"会立刻产生第二个真相：
/// 改了游戏门槛忘了改 NPC 行，玩家就会看到"训练师说我不够格、技能面板说够格"这种自相矛盾。
/// 所以本行只带<b>只有 NPC 才有的信息</b>：教什么、什么服务、收多少钱、排第几。
/// </para>
/// </summary>
public readonly struct NpcTrainerRow(
    int skillId,
    NpcTrainerService service,
    long costGold,
    int costItemId,
    int costItemCount,
    int seq)
{
    /// <summary>技能 id（<see cref="NpcTrainerService.Skill"/>）或配方 id（<see cref="NpcTrainerService.Recipe"/>）。
    /// 非正数一律按坏行拒绝。</summary>
    public readonly int SkillId = skillId;

    public readonly NpcTrainerService Service = service;

    /// <summary>金币学费。<b>0 是合法值</b>（免费教学是真实设计，尤其入门式），
    /// 所以"没定价"不能靠 0 表达——由 <see cref="Priced"/> 显式声明。</summary>
    public readonly long CostGold = costGold;

    /// <summary>额外材料代价的物品 id；≤0 = 不要材料（WoW 观测：部分专业教学要交材料）。</summary>
    public readonly int CostItemId = costItemId;

    public readonly int CostItemCount = costItemCount;

    /// <summary>窗口里的排序位（与商人 <c>slot</c> 同理：顺序是玩家可观测的数据）。</summary>
    public readonly int Seq = seq;

    /// <summary>这一行是不是完整可用（服务种类已登记 + id 正 + 声明了价格）。</summary>
    public readonly bool IsUsable =>
        Service is NpcTrainerService.Skill or NpcTrainerService.Recipe && SkillId > 0 && CostGold >= 0;

    /// <summary>是否显式定了价（允许 0，但必须 >=0 且由装配写进来）。</summary>
    public readonly bool Priced => CostGold >= 0;
}

/// <summary>教学拒绝的分档。<b>每条出口都回同一组字段</b>（学费、游戏门槛码、材料档）。</summary>
public enum NpcTeachReject : byte
{
    /// <summary>可教。</summary>
    None = 0,

    /// <summary>这个 NPC 名下没有这一行（点了不该出现的项 / 行被撤）。</summary>
    NotTaughtByThisNpc = 1,

    /// <summary>行数据坏（服务种类未登记或 id 非正）。</summary>
    BadRow = 2,

    /// <summary>游戏本体门槛没过。<b>具体为什么没过由 <c>GameGateCode</c> 透传，本层不解释、不改写</b>
    /// （改写一次就等于在 NPC 侧复制了一份门槛判据）。</summary>
    GameGateRefused = 3,

    /// <summary>钱不够（玩家态档）。</summary>
    NotEnoughGold = 4,

    /// <summary>教学材料不够（玩家态档）。</summary>
    NotEnoughItem = 5,
}

/// <summary>一次教学请求的回执。</summary>
public readonly struct NpcTeachVerdict(
    bool allowed,
    NpcTeachReject reject,
    int gameGateCode,
    long costGold,
    int costItemId,
    int costItemCount)
{
    public readonly bool Allowed = allowed;
    public readonly NpcTeachReject Reject = reject;

    /// <summary>来自 <c>SkillGrain.CheckLearnGates</c> 的原样理由码。<b>未走游戏门槛这一路时为 0</b>
    /// （0 = "本层没问"，不是"游戏门槛通过"——两种情形由 <see cref="Reject"/> 区分）。</summary>
    public readonly int GameGateCode = gameGateCode;

    public readonly long CostGold = costGold;
    public readonly int CostItemId = costItemId;
    public readonly int CostItemCount = costItemCount;

    public readonly string Describe() =>
        $"teach_ok={(Allowed ? 1 : 0)} teach_reject={(byte)Reject} teach_gate={GameGateCode} " +
        $"teach_gold={CostGold} teach_item={CostItemId}x{CostItemCount}";
}

/// <summary>
/// 训练师判据：NPC 教不教 + 收多少（规格 §6.3）。
/// <para>
/// <b>本层与游戏门槛层的关系是"叠加一层 NPC 轴"，不是"重新判一遍能不能学"</b>：
/// 游戏侧结论由调用方取回后原样传入（<paramref name="gameGatePassed"/> / <paramref name="gameGateCode"/>），
/// 不通过就直接不通过。灰行显示的理由因此永远与技能面板一致（观测：WoW 训练师里那一行的灰因与职业面板的灰因是同一个）。
/// </para>
/// </summary>
public static class NpcTrainerCore
{
    /// <summary>判据版本。</summary>
    public const int Version = 1;

    /// <summary>在游戏门槛已过的前提下，判这一行能不能成交。</summary>
    /// <param name="row">训练师行。<b>找不到行时不要传 <c>default</c> 进来</b>——
    /// 那条路会让"行不存在"与"行存在但 id=0"同形；请走 <see cref="NotFound"/>。</param>
    /// <param name="gameGatePassed">游戏本体门槛结论。</param>
    /// <param name="gameGateCode">游戏本体门槛理由码（原样透传）。</param>
    /// <param name="buyerGold">玩家金币。</param>
    /// <param name="haveCostItem">玩家持有的教学材料件数（该行不要材料时传 0 即可）。</param>
    public static NpcTeachVerdict Evaluate(
        in NpcTrainerRow row,
        bool gameGatePassed,
        int gameGateCode,
        long buyerGold,
        int haveCostItem)
    {
        if (!row.IsUsable)
            return new NpcTeachVerdict(false, NpcTeachReject.BadRow, gameGateCode, row.CostGold, row.CostItemId, row.CostItemCount);

        if (!gameGatePassed)
            return new NpcTeachVerdict(false, NpcTeachReject.GameGateRefused, gameGateCode, row.CostGold, row.CostItemId, row.CostItemCount);

        if (buyerGold < row.CostGold)
            return new NpcTeachVerdict(false, NpcTeachReject.NotEnoughGold, gameGateCode, row.CostGold, row.CostItemId, row.CostItemCount);

        if (row.CostItemId > 0 && haveCostItem < row.CostItemCount)
            return new NpcTeachVerdict(false, NpcTeachReject.NotEnoughItem, gameGateCode, row.CostGold, row.CostItemId, row.CostItemCount);

        return new NpcTeachVerdict(true, NpcTeachReject.None, gameGateCode, row.CostGold, row.CostItemId, row.CostItemCount);
    }

    /// <summary>这个 NPC 名下根本没有这一行（与"行存在但被拒"分开报，同族拒绝分支纪律）。</summary>
    public static NpcTeachVerdict NotFound(int gameGateCode) =>
        new NpcTeachVerdict(false, NpcTeachReject.NotTaughtByThisNpc, gameGateCode, 0, 0, 0);

    /// <summary>按 <see cref="NpcTrainerRow.Seq"/> 稳定排序（就地排序，窗口顺序即数据顺序）。</summary>
    public static void SortBySeq(IList<NpcTrainerRow> rows)
    {
        if (rows is null) return;

        // 插入排序：小列表更快，且同 seq 时保留原相对次序（稳定），不会让窗口里的行随机互换。
        for (var i = 1; i < rows.Count; i++)
        {
            var key = rows[i];
            var j = i - 1;
            while (j >= 0 && rows[j].Seq > key.Seq)
            {
                rows[j + 1] = rows[j];
                j--;
            }

            rows[j + 1] = key;
        }
    }

    /// <summary>
    /// 训练师窗口的灰行统计：返回 (可学数, 因游戏门槛灰数, 因钱灰数)。
    /// <para><b>三档分开是刻意的</b>：观测里"等级不足"与"买不起"是两种不同的灰、两种不同的玩家行为
    /// （前者要去升级，后者要去赚钱）。合成一个数就没法给 UI 正确的提示。</para>
    /// </summary>
    public static (int Learnable, int GateGray, int GoldGray) SummarizeWindow(
        IReadOnlyList<NpcTrainerRow> rows,
        IReadOnlyList<bool> gatePassed,
        IReadOnlyList<int> gateCodes,
        long buyerGold)
    {
        int learnable = 0, gateGray = 0, goldGray = 0;
        if (rows is null) return (0, 0, 0);

        for (var i = 0; i < rows.Count; i++)
        {
            var passed = gatePassed is not null && i < gatePassed.Count && gatePassed[i];
            var code = gateCodes is not null && i < gateCodes.Count ? gateCodes[i] : 0;
            var row = rows[i];
            var v = Evaluate(in row, passed, code, buyerGold, 0);
            if (v.Allowed) learnable++;
            else if (v.Reject == NpcTeachReject.GameGateRefused) gateGray++;
            else if (v.Reject == NpcTeachReject.NotEnoughGold) goldGray++;
        }

        return (learnable, gateGray, goldGray);
    }
}
