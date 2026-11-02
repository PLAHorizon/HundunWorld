using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Game.Sim.Quests;

/// <summary>可被名字反查的一条目录条目（物品 / 怪物 / NPC 共用同一个形状）。</summary>
public readonly struct QuestCatalogCandidate
{
    public QuestCatalogCandidate(int templateId, string name, string category = "")
    {
        TemplateId = templateId;
        Name = name ?? string.Empty;
        Category = category ?? string.Empty;
    }

    /// <summary>模板 id（与线上/库里那一份同号）。</summary>
    public int TemplateId { get; }

    /// <summary>显示名。</summary>
    public string Name { get; }

    /// <summary>档位名（"材料"/"武器"/"秘籍"…），只用于把歧义说清楚。</summary>
    public string Category { get; }

    public override string ToString()
        => string.IsNullOrEmpty(Category) ? Name + "(" + TemplateId + ")" : Name + "(" + TemplateId + "," + Category + ")";
}

/// <summary>
/// 任务编辑器里的"按名字填 id"：作者不必记 700001 是哪块矿，直接写"铁矿石"也能存对。
/// <para>
/// <b>为什么住在 Sim 而不是插件里</b>：这条规则一旦有两份，就会出现"编辑器认的名字 CLI 不认"
/// 或反过来，而 CLI 才是唯一写库通道。所以规则只此一份，插件、CLI、测试都调它。
/// </para>
/// <para>
/// 判定顺序是刻意设计的：<b>纯数字优先</b>（作者明明知道 id 时不许被同名物品劫持），
/// 再整名相等，最后才是子串匹配；<b>歧义一律不猜</b>——返回 0 并把候选交回调用方去提示。
/// 宁可让作者多打一个字，也不要悄悄把奖励发成另一件东西。
/// </para>
/// <para>
/// 编辑器侧（<c>HundunWorld/Plugins/QuestEditor/Source/QuestEditorEditor/QuestEditorWindow.cs</c> 的
/// <c>ResolveTemplateId</c>）有一份**同规则**的实现——Flax 插件刻意不把服务端程序集拖进脚本域，
/// 所以那份只能复制。两边的三步顺序由
/// <c>QuestCatalogLookupTests.名称解析规则的三步顺序两边不得漂移</c> 以源码结构钉住；
/// 改这里的规则时记得同步改插件那一份。
/// </para>
/// </summary>
public static class QuestCatalogLookup
{
    /// <summary>解析不出唯一条目时的返回值（0 与"未填模板 id"同形，由校验器接着判）。</summary>
    public const int Unresolved = 0;

    /// <summary>
    /// 把一段输入解析成模板 id。
    /// </summary>
    /// <param name="catalog">该类别的目录（物品类别传物品表，NPC 类别传 NPC 目录…）。</param>
    /// <param name="text">输入：数字 id、整名、或名字片段。</param>
    /// <param name="matches">歧义或未命中时给作者看的候选（命中唯一时是空表）。</param>
    /// <returns>唯一 id；否则 <see cref="Unresolved"/>。</returns>
    public static int Resolve(IReadOnlyList<QuestCatalogCandidate>? catalog, string? text, out IReadOnlyList<QuestCatalogCandidate> matches)
    {
        matches = Array.Empty<QuestCatalogCandidate>();

        var needle = (text ?? string.Empty).Trim();
        if (needle.Length == 0) return Unresolved;

        // 1) 纯数字 = 作者已经知道 id，直接放行（哪怕目录里没有，也交给校验器去报"模板不存在"，
        //     而不是在这里悄悄改成别的条目）。
        if (IsAllDigits(needle, out var numericId)) return numericId;

        if (catalog is null || catalog.Count == 0) return Unresolved;

        // 2) 整名相等优先于子串：库里"新手短剑"与"新手长剑"都在时，"新手短剑"必须命中前者。
        var exact = catalog.Where(entry => string.Equals(entry.Name, needle, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 1) return exact[0].TemplateId;
        if (exact.Length > 1)
        {
            matches = exact;
            return Unresolved;
        }

        // 3) 子串匹配：只接受唯一解。
        var partial = catalog
            .Where(entry => entry.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(entry => entry.Name.Length)   // 短名优先，方便提示里最贴近的那条排在前面
            .ThenBy(entry => entry.TemplateId)
            .ToArray();
        if (partial.Length == 1) return partial[0].TemplateId;

        matches = partial;
        return Unresolved;
    }

    /// <summary>歧义候选的一行说明，供提示文案与日志共用同一份措辞。</summary>
    public static string Describe(IReadOnlyList<QuestCatalogCandidate> matches, int max = 6)
    {
        if (matches is null || matches.Count == 0) return "目录里没有匹配项";
        var head = matches.Take(max).Select(entry => entry.ToString());
        var extra = matches.Count > max ? "…共 " + matches.Count + " 项" : string.Empty;
        return matches.Count > 1 ? "匹配到多个：" + string.Join("、", head) + extra : string.Join("、", head) + extra;
    }

    private static bool IsAllDigits(string text, out int value)
    {
        value = 0;
        if (text.Length == 0 || text.Length > 10) return false;   // 上限 10 位，挡掉溢出与"看着像数字"的长串
        var sum = 0;
        foreach (var ch in text)
        {
            if (ch < '0' || ch > '9') return false;
            sum = sum * 10 + (ch - '0');
        }
        value = sum;
        return true;
    }
}
