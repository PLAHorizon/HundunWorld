using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Quests;

/// <summary>
/// 发布者显示名的解析规则（单一实现，供任务目录与可接列表共用）。
/// <para>
/// <b>为什么在服务端解析而不是让客户端查</b>：客户端没有 NPC/物品模板目录，也不该相信模板 id。
/// 名字只有一份真相才不会和目录分叉；解析不到时返回空串，由界面回退成"发布者 #id"，
/// <b>不编一个假名</b>——写死版本最常见的翻车就是把 id 显示成"未知"然后没人发现配置漏了。
/// </para>
/// </summary>
public static class QuestGiverNaming
{
    /// <summary>
    /// 解析一个发布者的显示名。
    /// </summary>
    /// <param name="kind">发布者类别（<c>giver_kind</c> 列）。</param>
    /// <param name="templateId">发布者模板 id。</param>
    /// <param name="itemNames">物品 id → 名字（只有 <see cref="QuestEntityKind.Item"/> 会用；null 表示查不到）。</param>
    public static string Resolve(QuestEntityKind kind, int templateId, IReadOnlyDictionary<int, string>? itemNames = null)
    {
        if (templateId <= 0) return string.Empty;

        switch (kind)
        {
            case QuestEntityKind.Npc:
                return NpcTemplateCatalog.Get(templateId).Name;
            case QuestEntityKind.Monster:
                return MonsterTemplateCatalog.Get(templateId).Name;
            case QuestEntityKind.Item:
                return itemNames != null && itemNames.TryGetValue(templateId, out var itemName) ? (itemName ?? string.Empty) : string.Empty;
            default:
                // 物件与区域暂无目录可查：留空，让界面自己回退，而不是猜一个。
                return string.Empty;
        }
    }

    /// <summary>按 <c>giver_kind</c> 整数解析（库里的存储形态）。</summary>
    public static string Resolve(int giverKind, int templateId, IReadOnlyDictionary<int, string>? itemNames = null)
        => Resolve((QuestEntityKind)giverKind, templateId, itemNames);
}
