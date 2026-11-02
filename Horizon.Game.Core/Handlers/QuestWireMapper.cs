using System.Collections.Generic;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Quests;

namespace Horizon.Game.Core.Handlers;

/// <summary>
/// 任务运行时/模板数据 → 线格式的解码器。
/// <para>
/// <b>为什么解码放在网关这一侧</b>：模板的 <c>objectives</c>/<c>rewards</c> 两列是 JSON 文本，
/// 解码规则（目标类型串、奖励键）住在 <c>Horizon.Game.Sim.Quests</c>。
/// 让 Grain 直接把 JSON 交给网关、由这里一次性解码，规则就只有一份；
/// 若在 Grain 里再拼成字符串，两处各改一半时界面上的奖励会和实际发的东西对不上。
/// </para>
/// </summary>
public static class QuestWireMapper
{
    /// <summary>模板 JSON（<c>objectives</c> 列）→ 线格式目标；未接取的任务 Current 恒为 0。</summary>
    public static List<QuestObjectiveWire> ObjectivesFromTemplateJson(string? json)
    {
        var list = new List<QuestObjectiveWire>();
        foreach (var objective in QuestDraft.ParseObjectives(json))
        {
            if (objective == null) continue;
            list.Add(new QuestObjectiveWire
            {
                Description = string.IsNullOrWhiteSpace(objective.Description)
                    ? DescribeType(objective.Kind)
                    : objective.Description,
                Current = 0,
                Required = objective.Count,
                Completed = false,
            });
        }
        return list;
    }

    /// <summary>角色身上那条任务的运行时目标 → 线格式（进度取服务端状态，客户端不自己算）。</summary>
    public static List<QuestObjectiveWire> ObjectivesFromState(Horizon.Game.Message.Network.QuestData quest)
    {
        var list = new List<QuestObjectiveWire>();
        if (quest?.Objectives == null) return list;

        foreach (var objective in quest.Objectives)
        {
            if (objective == null) continue;
            list.Add(new QuestObjectiveWire
            {
                Description = string.IsNullOrWhiteSpace(objective.Description)
                    ? DescribeType(objective.ObjectiveType)
                    : objective.Description,
                Current = objective.CurrentCount,
                Required = objective.RequiredCount,
                Completed = objective.IsCompleted,
            });
        }
        return list;
    }

    /// <summary>模板 <c>rewards</c> 列 → 线格式奖励。</summary>
    public static QuestRewardWire RewardFromTemplateJson(string? json)
        => ToWire(QuestRewardSpec.FromJson(json));

    /// <summary>运行时奖励字典（接取时从模板展平存下来的那份）→ 线格式奖励。</summary>
    public static QuestRewardWire RewardFromState(IDictionary<string, int>? rewards)
        => ToWire(QuestRewardSpec.FromLegacyDictionary(rewards));

    private static QuestRewardWire ToWire(QuestRewardSpec spec)
    {
        var wire = new QuestRewardWire { Exp = spec.Exp, Gold = spec.Gold };
        foreach (var item in spec.Items)
            wire.Items.Add(new QuestRewardItemWire { TemplateId = item.TemplateId, Count = item.Count });
        foreach (var favor in spec.Favor)
            wire.Favor.Add(new QuestFavorWire { Domain = favor.Domain, Points = favor.Points });
        return wire;
    }

    /// <summary>
    /// 模板没写目标文本时的兜底：只说"要做什么类型"，不谎报对象名——
    /// 具体 id 是谁由客户端的物品/怪物目录去翻译，这里没有那个目录。
    /// </summary>
    private static string DescribeType(string? type)
    {
        if (!QuestObjectiveCodec.TryDecode(type, out var spec)) return "完成目标";
        return spec.Kind switch
        {
            QuestObjectiveKind.KillMonster => "击杀目标",
            QuestObjectiveKind.KillDomain => "击杀该五行域的异兽",
            QuestObjectiveKind.TalkNpc => "与指定人物对话",
            QuestObjectiveKind.CollectItem => "收集物品",
            QuestObjectiveKind.UseItem => "使用道具",
            QuestObjectiveKind.ReachZone => "抵达指定地点",
            QuestObjectiveKind.InteractProp => "查看场景物件",
            _ => "完成目标",
        };
    }
}
