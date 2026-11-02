using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// 任务目录版本号 = **按 <see cref="QuestCatalogEntry.QuestId"/> 排序后的内容指纹**（FNV-1a 变体）。
/// <para>
/// 它取代原先的 <c>Σ(ContentVersion + PublishState) + 行数</c>。求和有两个实测过的钝点：
/// ① **互补变化互抵**——A 任务下线（<c>PublishState</c> 1→0，−1）恰好被 B 任务内容版本 +1 抵掉，
/// 号和不变，于是靠版本号做增量的那一路不会重画；
/// ② **直接改库不 bump <c>content_version</c> 就完全不动**——运营手改描述/目标/奖励原文时，
/// 库里内容换了而版本号还是旧值。
/// 指纹把每条任务**玩家可见的字段原文**都吃进去，两处都解掉。
/// </para>
/// <para>
/// 为什么放在 Interface 而不是 <c>Horizon.Game.Sim/Quests/</c>：<see cref="QuestCatalogEntry"/> 定义在本程序集，
/// 而 Sim 与 Interface 是平级工程（两边互不引用），类型能到的地方只有一处。
/// 版本号只是**相等性令牌**，不要求单调，所以 <c>int</c> 溢出与负值都无所谓（全程 <c>unchecked</c>）。
/// </para>
/// </summary>
public static class QuestCatalogFingerprint
{
    /// <summary>
    /// 空目录的固定值。刻意给一个非零、且不可能由"逐条混入"算出来的数：
    /// "库里真的一条任务都没有"必须能自证，不能和某条有内容的目录撞成同一个值——
    /// 那会让"任务系统上线了但一条任务都没发出去"在现场看起来像"没变过"。
    /// </summary>
    public const int EmptyCatalog = unchecked((int)0x51A7E5);

    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    /// <summary>算目录版本号。入参顺序无关（内部按 <c>QuestId</c> 排序）。</summary>
    public static int Compute(IReadOnlyList<QuestCatalogEntry>? entries)
    {
        if (entries == null || entries.Count == 0) return EmptyCatalog;

        var ordered = entries.OrderBy(entry => entry.QuestId).ToArray();
        unchecked
        {
            var hash = OffsetBasis;
            hash = Mix(hash, (uint)ordered.Length);
            foreach (var entry in ordered)
            {
                hash = Mix(hash, (uint)entry.QuestId);
                hash = MixString(hash, entry.QuestName);
                hash = Mix(hash, (uint)entry.QuestType);
                hash = Mix(hash, (uint)entry.LevelRequire);
                hash = Mix(hash, (uint)entry.PublishState);
                hash = Mix(hash, (uint)entry.GiverKind);
                hash = Mix(hash, (uint)entry.GiverTemplateId);
                hash = Mix(hash, (uint)entry.ContentVersion);
                hash = Mix(hash, (uint)entry.RepeatLimit);
                hash = MixInts(hash, entry.PrerequisiteQuestIds);
                hash = MixString(hash, entry.Description);
                hash = MixString(hash, entry.ObjectivesJson);
                hash = MixString(hash, entry.RewardsJson);
                hash = MixString(hash, entry.GiverName);
            }
            return (int)hash;
        }
    }

    private static uint Mix(uint hash, uint value)
    {
        unchecked
        {
            // 高低位各混一次：单靠 (hash ^ value) * Prime，小整数之间的差异太容易被后续乘法吞掉。
            hash = (hash ^ value) * Prime;
            hash = (hash ^ (value >> 16)) * Prime;
            return hash;
        }
    }

    private static uint MixString(uint hash, string? text)
    {
        if (string.IsNullOrEmpty(text)) return Mix(hash, 0);
        unchecked
        {
            hash = Mix(hash, (uint)text.Length);
            foreach (var ch in text) hash = (hash ^ ch) * Prime;
            return hash;
        }
    }

    private static uint MixInts(uint hash, IEnumerable<int>? values)
    {
        var array = values?.ToArray() ?? Array.Empty<int>();
        hash = Mix(hash, (uint)array.Length);
        unchecked
        {
            // 前置任务是一组 id，不能只算和：{1,3} 与 {2,2} 的和相同，顺序也不同，都得区分开。
            foreach (var value in array.OrderBy(v => v)) hash = Mix(hash, (uint)value);
            return hash;
        }
    }
}
