using System;
using System.Collections.Generic;

namespace Horizon.Game.Sim.Npcs;

/// <summary>对话动作的种类（一条 gossip 选项要么只是话，要么打开一个服务窗口）。</summary>
/// <remarks>
/// <b>动作种类与 <see cref="NpcServiceBits"/> 是两套清单，且必须成对校验</b>：
/// 选项写"开商店"而 NPC 没开 <see cref="NpcServiceBits.Vendor"/> 位，玩家点了就是一个空窗——
/// 这条不一致由 <see cref="ActionNeedsService"/> 供 validate 检查（判据只在这里下一遍）。
/// </remarks>
public enum NpcGossipAction : byte
{
    /// <summary>只是文本（闲聊/剧情）。</summary>
    Talk = 0,

    /// <summary>打开商人窗口。</summary>
    OpenVendor = 1,

    /// <summary>打开技能训练窗口。</summary>
    OpenTrainer = 2,

    /// <summary>打开制作窗口（设施）。</summary>
    OpenCraft = 3,

    /// <summary>交付任务。</summary>
    TurnInQuest = 4,

    /// <summary>进入下一层菜单（参数是组号）。</summary>
    SubMenu = 5,
}

/// <summary>一条对话选项。</summary>
/// <param name="Seq">窗口里的顺序（数据决定，UI 不重排）。</param>
/// <param name="GroupId">所属台词组；与模板/作息档的组号配对。</param>
/// <param name="TextId">文本行号。0 = 无文本（纯动作项）。</param>
/// <param name="MinLevel">可见等级下限（含）。<b>0 = 不设下限</b>，不是"只能 0 级看"。</param>
/// <param name="MaxLevel">可见等级上限（含）。0 = 不设上限。</param>
public readonly struct NpcGossipOption(
    int seq,
    int groupId,
    int textId,
    NpcGossipAction action,
    int actionArg,
    int minLevel,
    int maxLevel)
{
    public readonly int Seq = seq;
    public readonly int GroupId = groupId;
    public readonly int TextId = textId;
    public readonly NpcGossipAction Action = action;
    public readonly int ActionArg = actionArg;
    public readonly int MinLevel = minLevel;
    public readonly int MaxLevel = maxLevel;

    public readonly string Describe() =>
        $"gossip_seq={Seq} group={GroupId} text={TextId} action={(byte)Action} arg={ActionArg} " +
        $"lv=[{MinLevel},{MaxLevel}]";
}

/// <summary>
/// 对话菜单的确定性判据（规格 §2.3 的"能力位 ∧ 玩家态"这一跳在对话层的样子）。
/// <para>
/// <b>为什么可见性要服务端算</b>：观测里同一个旅店老板对不同玩家显示不同项（炉石已设就不给"设为炉石"）。
/// 客户端自算等于把"哪些内容存在"泄露给客户端，还会出现"看得见点不动"。
/// </para>
/// </summary>
public static class NpcGossipCore
{
    /// <summary>判据版本。</summary>
    public const int Version = 1;

    /// <summary>
    /// 等级可见性。<b>0 在两侧都表示"不设限"</b>：
    /// 把 <c>MinLevel = 0</c> 读成"必须 0 级"、把 <c>MaxLevel = 0</c> 读成"上限 0 级"，
    /// 会让一整组菜单在正常等级下全部消失而数据看起来完全合法
    /// （结构体默认 0 撞合法值的老坑）。
    /// </summary>
    public static bool LevelVisible(in NpcGossipOption option, int playerLevel)
    {
        if (option.MinLevel > 0 && playerLevel < option.MinLevel) return false;
        if (option.MaxLevel > 0 && playerLevel > option.MaxLevel) return false;
        return true;
    }

    /// <summary>按等级筛出可见项（保持数据顺序，<b>不重排</b>：窗口顺序本身就是内容）。</summary>
    public static List<NpcGossipOption> FilterVisible(IEnumerable<NpcGossipOption>? options, int playerLevel)
    {
        var list = new List<NpcGossipOption>(8);
        if (options is null) return list;

        foreach (var o in options)
        {
            if (LevelVisible(in o, playerLevel)) list.Add(o);
        }

        return list;
    }

    /// <summary>
    /// 某个动作需要的服务位（<see cref="NpcServiceBits.None"/> = 不需要任何位）。
    /// 这是"选项与位图成对校验"的唯一出处——两处各写一份必然漂。
    /// </summary>
    public static NpcServiceBits RequiredService(NpcGossipAction action) => action switch
    {
        NpcGossipAction.OpenVendor => NpcServiceBits.Vendor,
        NpcGossipAction.OpenTrainer => NpcServiceBits.Trainer,
        NpcGossipAction.OpenCraft => NpcServiceBits.CraftStation,
        NpcGossipAction.TurnInQuest => NpcServiceBits.QuestTurnIn,
        _ => NpcServiceBits.None,
    };

    /// <summary>选项引用的服务位是否真的开了（未开 → 这条选项是坏数据，点了会开空窗）。</summary>
    public static bool ActionServiceAvailable(in NpcGossipOption option, uint serviceFlags)
    {
        var need = RequiredService(option.Action);
        if (need == NpcServiceBits.None) return true;
        return (serviceFlags & (uint)need) != 0;
    }

    /// <summary>取某个台词组的可见项（按 <see cref="NpcGossipOption.GroupId"/> 过滤 + 等级筛）。</summary>
    public static List<NpcGossipOption> GroupFor(
        IEnumerable<NpcGossipOption>? options,
        int groupId,
        int playerLevel,
        out int hiddenByLevel)
    {
        var list = new List<NpcGossipOption>(8);
        hiddenByLevel = 0;
        if (options is null) return list;

        foreach (var o in options)
        {
            if (o.GroupId != groupId) continue;
            if (!LevelVisible(in o, playerLevel))
            {
                hiddenByLevel++;
                continue;
            }

            list.Add(o);
        }

        // 条数下限由调用方断言：<b>"这个组一条都没有"与"全被等级挡掉"必须能分辨</b>，
        // 所以被挡掉的条数要如实带出去，而不是静默返回空表。
        return list;
    }
}
