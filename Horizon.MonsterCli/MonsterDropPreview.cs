using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Sim.Monsters;

namespace Horizon.MonsterCli;

/// <summary>
/// 掉落试算：把绑定行转成**运行期快照**，再让内核自己报期望。
/// <para>
/// <b>填的哪个洞</b>：这里以前自己抄了一遍"总闸 × 每件概率 × 平均数量，逐件相加"。
/// 那个式子对 <c>group_id=0</c> 是对的，对互斥组会系统性偏高——同一次击杀整组最多出一件，
/// 而五件各 100% 的组按抄件算就是"每次 5 件"。表现是编辑器点"试算掉落"报一个
/// 服务端永远达不到的数字，而库里、编辑器、快照三处的概率全都显示得对，
/// 没人会怀疑到试算那一侧。
/// </para>
/// <para>
/// 现在期望件数与逐件中签率都由 <see cref="MonsterLootCore"/> 给出：预览与掷骰共用同一个主人，
/// 改一处两边一起变。本类只做搬运与汇总，不写任何掉率算术。
/// </para>
/// </summary>
public static class MonsterDropPreview
{
    /// <summary>一条逐件条目的试算结果。</summary>
    public sealed class EntryRow
    {
        public int ItemId;

        /// <summary>组号（0=逐件独立掷，&gt;0=同组互斥抽一件）。</summary>
        public int GroupId;

        /// <summary>这一件**实际生效**的掉落条件（条目继承绑定的结果，与内核出账用的是同一个值）。</summary>
        public int ConditionKind;

        /// <summary>这一件实际生效的任务 id（0 = 没有）。</summary>
        public int QuestId;

        /// <summary>该件的中签概率（基点，已乘总闸；组内成员拿的是"轮到它时还剩的余额"）。</summary>
        public double WinChanceBps;

        /// <summary>该件对每次击杀期望件数的贡献（含数量区间；条件件为 0，因为业务层还没判）。</summary>
        public double ExpectedCount;
    }

    /// <summary>一张绑定表的试算结果。</summary>
    public sealed class BindingRow
    {
        public int LootTableId;
        public double GateChance;
        public int EntryCount;

        /// <summary>互斥组个数与挂在组里的件数（策划要能看见"这一堆数字其实是最多出一件"）。</summary>
        public int GroupCount;
        public int GroupedEntryCount;

        public double ExpectedItems;
        public int MoneyMin;
        public int MoneyMax;
        public int ConditionKind;

        /// <summary>表级"抽几件"= 这张表互斥组的重掷轮数区间（内核按同一区间算期望）。</summary>
        public int MinCount;
        public int MaxCount;

        /// <summary>该表第一组的期望有效轮数（预览里说明"为什么期望比单轮概率之和大"）。</summary>
        public double ExpectedRounds;
        public readonly List<EntryRow> Entries = new List<EntryRow>();
    }

    /// <summary>整只怪的试算结果。</summary>
    public sealed class Summary
    {
        public readonly List<BindingRow> Bindings = new List<BindingRow>();
        public double ExpectedItemsPerKill;

        /// <summary>原始（未截断）期望：与逐条求和做交叉核对用的就是它。</summary>
        public double ExpectedItemsUncapped;

        /// <summary>候选期望被 18 件普通件上限压住了（回执里必须念出来，否则策划按原值读结果会以为丢了东西）。</summary>
        public bool CappedByLimit;

        /// <summary>按物品 id 汇总的期望件数（同一件可能挂在多张表上）。</summary>
        public readonly Dictionary<int, double> PerItem = new Dictionary<int, double>();

        /// <summary>按物品 id 汇总的"这件会掉"概率（基点，已乘各表总闸；不是件数）。</summary>
        public readonly Dictionary<int, double> PerItemChanceBps = new Dictionary<int, double>();

        /// <summary>条件件（要业务层按任务判的）条数：不计入期望，但必须在回执里看得见。</summary>
        public int ConditionalEntryCount;

        /// <summary>
        /// 逐条求和与内核整表期望不一致的标记。
        /// <para>正常情况下两者恒等（同一份数据的两条入口）。它为真就只有两种可能：
        /// 某条目被求和跳过，或者内核的两个入口各算各的——两者都必须大声报出来，
        /// 而不是让预览端悄悄拿着其中一份数字。</para>
        /// </summary>
        public bool CrossCheckFailed;

        /// <summary>交叉核对读到的两个数（回执里要印出来，光有布尔值说不出差在哪）。</summary>
        public double EntrySum;
    }

    /// <summary>
    /// 试算一批绑定 DTO（与传入顺序无关：转换层按 (seq, itemId) 排好，与运行时装配逐字相同）。
    /// </summary>
    public static Summary Summarize(IEnumerable<MonsterLootBindingSnapshotDto> rows)
    {
        var summary = new Summary();
        if (rows == null) return summary;

        var snapshots = new List<MonsterLootBindingSnapshot>();
        double entrySum = 0.0;

        foreach (var dto in rows)
        {
            if (dto == null) continue;
            var binding = dto.ToSnapshot();
            snapshots.Add(binding);

            var gate = MonsterLootCore.GateChanceBps(binding.RollChanceBps) / (double)MonsterLootCore.BasisPointsMax;
            var row = new BindingRow
            {
                LootTableId = binding.LootTableId,
                GateChance = gate,
                EntryCount = binding.Entries.Length,
                MoneyMin = binding.MoneyMin,
                MoneyMax = binding.MoneyMax,
                ConditionKind = binding.ConditionKind,
                MinCount = binding.MinCount,
                MaxCount = binding.MaxCount,
            };

            var groupIds = new List<int>();
            for (var i = 0; i < binding.Entries.Length; i++)
            {
                var entry = binding.Entries[i];
                var groupId = MonsterLootCore.GroupIdOf(entry);
                if (groupId > 0)
                {
                    row.GroupedEntryCount++;
                    if (!groupIds.Contains(groupId)) groupIds.Add(groupId);
                }
                // 条件按**这一件**解析（条目 -1 才退回绑定），与内核出账同一套规则。
                var resolvedKind = MonsterLootCore.ResolveConditionKind(entry.ConditionKind, binding.ConditionKind);
                if (resolvedKind != MonsterLootCore.ConditionNone) summary.ConditionalEntryCount++;

                // 期望与中签率全部问内核——本层一个算术都不写。
                var expected = MonsterLootCore.EntryExpectedItems(in binding, i);
                var winChanceBps = MonsterLootCore.EntryWinChanceBps(in binding, i) * gate;
                row.Entries.Add(new EntryRow
                {
                    ItemId = entry.ItemId,
                    GroupId = groupId,
                    ConditionKind = resolvedKind,
                    QuestId = MonsterLootCore.ResolveQuestId(entry.QuestId, binding.QuestId),
                    WinChanceBps = winChanceBps,
                    ExpectedCount = expected,
                });
                row.ExpectedItems += expected;
                entrySum += expected;
                if (entry.ItemId > 0)
                {
                    summary.PerItem.TryGetValue(entry.ItemId, out var acc);
                    summary.PerItem[entry.ItemId] = acc + expected;
                    summary.PerItemChanceBps.TryGetValue(entry.ItemId, out var accChance);
                    summary.PerItemChanceBps[entry.ItemId] = accChance + winChanceBps;
                }
            }
            row.GroupCount = groupIds.Count;
            // 第一个组的期望轮数（多组表逐组不同，这里给的是"为什么期望比单轮大"的量级线索）。
            row.ExpectedRounds = groupIds.Count > 0
                ? MonsterLootCore.ExpectedRoundsForGroup(in binding, MonsterLootCore.GroupStopChanceBps(in binding, groupIds[0]))
                : 1.0;
            summary.Bindings.Add(row);
        }

        // 整表期望以内核的全局入口为准；逐条求和是它的交叉核对，两者必须相等。
        // 期望值会按 18 件普通件上限截断（运行期 EmitEntry 就是这么拦的），所以核对拿**未截断**那一份比：
        // 拿截断值去比"逐条求和"，超上限的配法会被误报成"两个入口各算各的"。
        summary.ExpectedItemsUncapped = MonsterLootCore.ExpectedNormalItemsUncapped(snapshots);
        summary.ExpectedItemsPerKill = MonsterLootCore.ExpectedItemsPerKill(snapshots);
        summary.CappedByLimit = summary.ExpectedItemsPerKill + 1e-9 < summary.ExpectedItemsUncapped;
        summary.EntrySum = entrySum;
        summary.CrossCheckFailed = Math.Abs(summary.ExpectedItemsUncapped - entrySum) > 1e-9;
        return summary;
    }
}
