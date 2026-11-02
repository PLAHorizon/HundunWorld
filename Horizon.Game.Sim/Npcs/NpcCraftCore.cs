using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>配方的一条材料需求。</summary>
/// <param name="ItemId">物品模板 id。<b>非正数按坏需求拒绝</b>（与商人行的同一条纪律：0 会撞成"第一件物品"）。</param>
/// <param name="NeedCount">需要几件。≤0 是坏数据：一份"要 0 件"的需求会在材料普查里静默通过，
/// 让一个永远不缺料的配方看起来正常。</param>
public readonly struct NpcMaterialNeed(int itemId, int needCount)
{
    public readonly int ItemId = itemId;
    public readonly int NeedCount = needCount;

    public readonly bool IsUsable => ItemId > 0 && NeedCount > 0;
}

/// <summary>玩家背包里某一类物品的持有数（由调用方从 <c>IInventoryGrain</c> 取来，本内核不查表）。</summary>
public readonly struct NpcHeldItem(int itemId, int haveCount)
{
    public readonly int ItemId = itemId;

    /// <summary>持有件数。<b>负数按 0 处理并计入坏行回执</b>，不参与"缺多少"的计算。</summary>
    public readonly int HaveCount = haveCount;
}

/// <summary>一条材料的缺料回执（逐件给，因为观测里 UI 要"缺哪样写在哪样上"，规格 §7.1）。</summary>
public readonly struct NpcMaterialShortfall(int itemId, int need, int have, int missing)
{
    public readonly int ItemId = itemId;
    public readonly int Need = need;
    public readonly int Have = have;
    public readonly int Missing = missing;

    public readonly bool IsMissing => Missing > 0;

    public readonly string Describe() => $"mat={ItemId} need={Need} have={Have} missing={Missing}";
}

/// <summary>材料校验的拒绝分档。</summary>
public enum NpcCraftReject : byte
{
    /// <summary>材料齐。</summary>
    None = 0,

    /// <summary>至少一样材料缺。</summary>
    ShortMaterial = 1,

    /// <summary>配方没有任何有效材料需求（<b>空配方等于凭空造物</b>，与本工程 <c>CraftingGrain.cs:89-103</c>
    /// 今天恒真返回的行为是同一类缺陷，必须显式拒绝而不是放行）。</summary>
    NoMaterialsDefined = 2,

    /// <summary>配方/需求行数据坏（id 非正或需求数非正）。</summary>
    BadRow = 3,

    /// <summary>不在设施旁（<see cref="NpcCraftCore.RequireStation"/> 为真时）。</summary>
    NotAtStation = 4,

    /// <summary>没学过这个配方。</summary>
    RecipeNotLearned = 5,
}

/// <summary>一次材料校验的完整回执。<b>每条出口都带同一组字段</b>：缺料清单、缺样数、坏行数、总缺件数。</summary>
public readonly struct NpcCraftCheckVerdict(
    NpcCraftReject reason,
    IReadOnlyList<NpcMaterialShortfall> shortfalls,
    int missingKinds,
    int missingUnits,
    int badRows,
    int scannedRows)
{
    public readonly NpcCraftReject Reason = reason;

    /// <summary>逐件缺料清单（<b>只列缺的那几样</b>，齐的样不进清单，免得 UI 自己再筛一遍）。</summary>
    public readonly IReadOnlyList<NpcMaterialShortfall> Shortfalls = shortfalls;

    /// <summary>缺几种。</summary>
    public readonly int MissingKinds = missingKinds;

    /// <summary>总共缺几件。</summary>
    public readonly int MissingUnits = missingUnits;

    /// <summary>坏行数（必须被数出来，不能静默跳过）。</summary>
    public readonly int BadRows = badRows;

    /// <summary>扫过的需求行数。<b>扫描型判据一律报条数</b>（记忆 [扫描型检查会"安静地找到 0 条"]）。</summary>
    public readonly int ScannedRows = scannedRows;

    public readonly bool IsOk => Reason == NpcCraftReject.None;

    public readonly string Describe() =>
        $"craft_ok={(IsOk ? 1 : 0)} craft_reject={(byte)Reason} craft_missing_kinds={MissingKinds} " +
        $"craft_missing_units={MissingUnits} craft_bad_rows={BadRows} craft_scanned={ScannedRows}";
}

/// <summary>制作机会档（WoW 观测的橙/黄/绿/灰四档，规格 §7.1：<b>同一配方不同人做的成功率不同</b>）。</summary>
public enum NpcCraftChanceTier : byte
{
    /// <summary>灰（差值最大，几乎做不出）。</summary>
    Gray = 0,

    /// <summary>绿。</summary>
    Green = 1,

    /// <summary>黄。</summary>
    Yellow = 2,

    /// <summary>橙（十拿九稳）。</summary>
    Orange = 3,
}

/// <summary>
/// 成功率曲线（<b>阈值与概率一律由数据表提供，本内核不内置任何"WoW 数值"</b>）。
/// <para>
/// 为什么不给默认值：经典服四档的确切成功几率与技能差阈值，我在本次调研里<b>没有取得可信来源</b>
/// （规格 §14 待读）。在代码里写一个 <c>{0,5,10,15}</c> 装作是逆向结论，
/// 等于把凭空数字变成"看起来有依据的平衡值"——这比没有值更坏。
/// 于是这里要求显式给表，缺表就报 <see cref="IsUsable"/> 为 false，由 <c>validate</c> 拦住。
/// </para>
/// <para>
/// <b>数组顺序是"由易到难"：橙、黄、绿、灰</b>，<i>不是</i> <see cref="NpcCraftChanceTier"/> 的数值序
/// （数值序是 灰=0 … 橙=3，正好相反）。两套顺序混用就是"槽位错位"的经典来源，
/// 所以这里的校验与取档都只认"由易到难"这一份，并在 <see cref="IndexOf"/> 里做一次显式换算。
/// </para>
/// </summary>
/// <param name="gapCapsEasyToHard">四档的技能差上限（橙、黄、绿、灰），必须严格递增。
/// 技能差 = <c>配方要求 − 玩家技能</c>，越大越难。</param>
/// <param name="chanceBpsEasyToHard">同序的四档成功率（万分比），必须非递增（越难越做不出）。</param>
public readonly struct NpcCraftChanceCurve(int[] gapCapsEasyToHard, int[] chanceBpsEasyToHard)
{
    /// <summary>四档的技能差上限（由易到难）。</summary>
    public readonly int[] GapCaps = gapCapsEasyToHard;

    /// <summary>四档成功率（由易到难）。</summary>
    public readonly int[] ChanceBps = chanceBpsEasyToHard;

    /// <summary>表是否完整可用（两表各 4 项、上限严格递增、概率非递增且钳在 0..10000）。</summary>
    public readonly bool IsUsable
    {
        get
        {
            if (GapCaps is null || GapCaps.Length != 4) return false;
            if (ChanceBps is null || ChanceBps.Length != 4) return false;

            for (var i = 0; i < 4; i++)
            {
                if (ChanceBps[i] < 0 || ChanceBps[i] > 10000) return false;
                if (i > 0 && (GapCaps[i] <= GapCaps[i - 1] || ChanceBps[i] > ChanceBps[i - 1])) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 取档：技能差越大越难。
    /// <para><b>差值 ≤0（玩家已高于配方要求）落进最容易的橙档</b>——观测：熟练工做入门货从不失手。</para>
    /// </summary>
    public NpcCraftChanceTier TierFor(int recipeSkillMinusPlayerSkill)
    {
        if (!IsUsable) return NpcCraftChanceTier.Gray;

        // 由易到难找到第一个"上限罩得住这个差值"的档；罩不住的就是最难档。
        for (var i = 0; i < GapCaps.Length; i++)
        {
            if (recipeSkillMinusPlayerSkill <= GapCaps[i]) return (NpcCraftChanceTier)(3 - i);
        }

        return NpcCraftChanceTier.Gray;
    }

    /// <summary>档 → 数组下标（橙=0、黄=1、绿=2、灰=3）。唯一的换算点，别让调用方各写一遍 <c>3 - (int)</c>。</summary>
    public static int IndexOf(NpcCraftChanceTier tier) => 3 - (int)tier;

    /// <summary>取该档成功率（万分比）。表不可用时回 0（<b>不猜一个中间值</b>）。</summary>
    public int ChanceBpsFor(NpcCraftChanceTier tier)
    {
        if (!IsUsable) return 0;
        var i = IndexOf(tier);
        return i >= 0 && i < ChanceBps.Length ? ChanceBps[i] : 0;
    }
}

/// <summary>
/// 制作判据（规格 §7.3）。
/// <para>
/// <b>本内核存在的直接理由</b>：工程里 <c>CraftingGrain.CheckMaterialsAsync</c>（<c>CraftingGrain.cs:89-103</c>）
/// <b>不看背包直接返回 true</b>，<c>LearnRecipeAsync</c>（<c>:57-76</c>）当场捏一个 <c>SuccessRate=1.0</c> 的配方。
/// 那两个方法等于"无中生有"。本类给出真判据，接线时把那两个方法改成调用这里——
/// 只在编辑器里做张配方表而不修它们，"制作装备或物品"这条目标就只有数据没有行为（设计文档 D10 已拍板：本期一并修）。
/// </para>
/// <para>
/// <b>纯函数、不查背包</b>：持有数由调用方喂进来（<see cref="NpcHeldItem"/>），
/// 这样离线单测、CLI <c>preview-craft</c> 与运行期三方能用同一份判据对拍。
/// </para>
/// </summary>
public static class NpcCraftCore
{
    /// <summary>判据版本。</summary>
    public const int Version = 1;

    /// <summary>
    /// 材料校验：逐样比对，产出缺料清单。
    /// <para><b>同一物品在需求里出现两次</b>（坏数据）按"合并成一条需求"处理并计入 <see cref="NpcCraftCheckVerdict.BadRows"/>：
    /// 直接取第一条会让"缺 2 个 + 缺 3 个"被算成"缺 3 个"，玩家按清单补齐后仍做不出。</para>
    /// </summary>
    public static NpcCraftCheckVerdict CheckMaterials(
        IReadOnlyList<NpcMaterialNeed>? needs,
        IReadOnlyList<NpcHeldItem>? held)
    {
        var shortfalls = new List<NpcMaterialShortfall>(8);
        var scanned = needs?.Count ?? 0;
        var bad = 0;
        var validKinds = 0;

        // 先把持有量收成"物品 → 件数"，负数按 0。同一物品出现多行时累加（背包可能分多格）。
        var haveMap = new Dictionary<int, int>(16);
        var heldCount = held?.Count ?? 0;
        for (var i = 0; i < heldCount; i++)
        {
            var h = held![i];
            if (h.ItemId <= 0) continue;
            var qty = Math.Max(0, h.HaveCount);
            haveMap.TryGetValue(h.ItemId, out var acc);
            haveMap[h.ItemId] = acc + qty;
        }

        // 需求合并：物品 → 需要总件数（保留首次出现顺序，清单顺序 = 配方数据顺序）。
        var needOrder = new List<int>(8);
        var needMap = new Dictionary<int, int>(16);
        for (var i = 0; i < scanned; i++)
        {
            var n = needs![i];
            if (!n.IsUsable)
            {
                bad++;
                continue;
            }

            if (!needMap.TryGetValue(n.ItemId, out var acc))
            {
                needMap[n.ItemId] = n.NeedCount;
                needOrder.Add(n.ItemId);
                validKinds++;
            }
            else
            {
                needMap[n.ItemId] = acc + n.NeedCount;
                bad++; // 重复行本身是坏数据，虽然值已经合并
            }
        }

        if (scanned == 0 || (validKinds == 0 && bad > 0))
        {
            // 一条有效需求都没有 = 空配方 = 凭空造物，显式拒绝（见 NpcCraftReject.NoMaterialsDefined）。
            var reason = bad > 0 ? NpcCraftReject.BadRow : NpcCraftReject.NoMaterialsDefined;
            return new NpcCraftCheckVerdict(reason, shortfalls, 0, 0, bad, scanned);
        }

        if (validKinds == 0)
            return new NpcCraftCheckVerdict(NpcCraftReject.NoMaterialsDefined, shortfalls, 0, 0, bad, scanned);

        int missingKinds = 0, missingUnits = 0;
        for (var i = 0; i < needOrder.Count; i++)
        {
            var itemId = needOrder[i];
            var need = needMap[itemId];
            haveMap.TryGetValue(itemId, out var have);
            var missing = need - have;
            if (missing <= 0) continue;

            missingKinds++;
            missingUnits += missing;
            shortfalls.Add(new NpcMaterialShortfall(itemId, need, have, missing));
        }

        var verdict = missingKinds > 0 ? NpcCraftReject.ShortMaterial : NpcCraftReject.None;
        if (verdict == NpcCraftReject.None && bad > 0) verdict = NpcCraftReject.BadRow;

        return new NpcCraftCheckVerdict(verdict, shortfalls, missingKinds, missingUnits, bad, scanned);
    }

    /// <summary>
    /// 设施在场校验：把"站在铁砧旁才能打铁"做成一次位判（规格 §7.1）。
    /// </summary>
    /// <param name="stationServiceFlags">玩家正在交互的那个 NPC 的服务位图；不在设施旁时传 0。</param>
    /// <param name="requireStation">这张配方是否要求设施（有些配方确实"随处可做"，比如基础结绳）。</param>
    public static bool StationOk(uint stationServiceFlags, bool requireStation)
    {
        if (!requireStation) return true;
        return (stationServiceFlags & (uint)NpcServiceBits.CraftStation) != 0;
    }

    /// <summary>
    /// 设施**身份**对号：配方绑定了设施 NPC 时，交互对象必须就是那一位。
    /// <para>清单只列"绑定到这位设施 NPC"的配方（<c>StationNpcId == 他</c>）；动作若只查"有没有设施位"，
    /// 站在别的设施前也能做不属于它的东西——两条入口不同数，面板上的清单成了摆设。</para>
    /// <para>配方没绑设施（≤0）时随处可做，是设计而不是漏洞（基础结绳那一类）。</para>
    /// </summary>
    public static bool StationIdentityOk(int recipeStationNpcId, int interactingNpcId)
        => recipeStationNpcId <= 0 || interactingNpcId == recipeStationNpcId;

    /// <summary>
    /// 找出"这位训练师教这张配方"的那一行（<see cref="NpcTrainerService.Recipe"/> 且 id 命中）；没有回 null。
    /// <para>收费与教学材料也从这一行读——判据与清单同源（清单就是从这份行里出的）。</para>
    /// </summary>
    public static NpcTrainerRow? FindTaughtRecipeRow(IReadOnlyList<NpcTrainerRow>? rows, int recipeId)
    {
        if (rows == null || recipeId <= 0) return null;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Service != NpcTrainerService.Recipe) continue;
            if (rows[i].SkillId == recipeId) return rows[i];
        }

        return null;
    }

    /// <summary>
    /// "这位训练师教这张配方吗"：<see cref="FindTaughtRecipeRow"/> 的判空形式。
    /// <para>不查这一步时，站在任意训练师面前能学全服配方，而面板上的"师承"只列他真正教的几张。</para>
    /// </summary>
    public static bool TaughtByTrainer(IReadOnlyList<NpcTrainerRow>? rows, int recipeId)
        => FindTaughtRecipeRow(rows, recipeId).HasValue;

    /// <summary>
    /// 一次完整的"能不能做"判定（顺序即玩家看到的失败原因优先级）。
    /// </summary>
    /// <param name="learned">玩家是否已学过这一配方。<b>没学过就先拒</b>：观测里没学过的配方根本不进制作列表，
    /// 若走到材料校验才被拦，等于泄露了"这张图纸长什么样"。</param>
    public static NpcCraftCheckVerdict TryCraft(
        bool learned,
        bool requireStation,
        uint stationServiceFlags,
        IReadOnlyList<NpcMaterialNeed>? needs,
        IReadOnlyList<NpcHeldItem>? held)
    {
        if (!learned)
        {
            var empty = new NpcCraftCheckVerdict(NpcCraftReject.RecipeNotLearned, Array.Empty<NpcMaterialShortfall>(), 0, 0, 0, 0);
            return empty;
        }

        if (!StationOk(stationServiceFlags, requireStation))
        {
            var scanned = needs?.Count ?? 0;
            return new NpcCraftCheckVerdict(NpcCraftReject.NotAtStation, Array.Empty<NpcMaterialShortfall>(), 0, 0, 0, scanned);
        }

        return CheckMaterials(needs, held);
    }
}
