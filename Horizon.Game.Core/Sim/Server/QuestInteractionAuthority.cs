using System;
using Horizon.Game.Sim.Quests;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// "玩家与世界中某个对象交互"的服务端裁决（设计文档 §11）。
/// <para>
/// 做成<b>纯函数</b>是有意的：这条链路上最容易出错、也最危险的部分就是判定本身
/// （判错了就是"客户端说什么服务端都认"），把它从 Grain 调用与网络线程里摘出来，
/// 才能在集群不开、DS 不跑的情况下把每条拒绝理由逐一断言。
/// </para>
/// </summary>
public static class QuestInteractionAuthority
{
    /// <summary>
    /// 默认交互半径（世界单位，与 DS 坐标同一量纲）。
    /// <para>取"隔着对话界面站得住"的距离：再近会因客户端投影误差抖动，再远就变成"隔着半张地图对话"。</para>
    /// </summary>
    public const float DefaultRadius = 800f;

    /// <summary>裁决结果。<see cref="Ok"/> 为真时 <see cref="TemplateId"/> 才是服务端认定的身份。</summary>
    /// <param name="Ok">是否采信这次交互。</param>
    /// <param name="Reason">不成立时的短码（供客户端分派表现）；成立时为空串。</param>
    /// <param name="TemplateId">服务端从注册表解出的模板 id（客户端自报的一律不用）。</param>
    /// <param name="Kind">任务语义下的类别。</param>
    public readonly record struct Verdict(bool Ok, string Reason, int TemplateId, QuestEntityKind Kind);

    /// <summary>
    /// 角色位置缓存（ECS Z-up）→ 注册表空间（Flax Y-up）。
    /// <para>
    /// 与 <c>DsPositionWritebackService</c> 写回时的换轴 <c>ecs(x,y,z)=flax(x,z,y)</c> 互为逆运算。
    /// 少做一次换轴的后果不是"偏一点"，而是把水平距离算成高度差：邻近判定全体失效。
    /// </para>
    /// </summary>
    public static (float X, float Y, float Z) ToFlax(float ecsX, float ecsY, float ecsZ)
        => (ecsX, ecsZ, ecsY);

    /// <summary>
    /// 三重核对：实体在这个 shard 的世界里存在吗 → 是不是可交互类别 → 离玩家够近吗。
    /// </summary>
    /// <param name="registry">网关注册表；null = 通路未接线（一律不采信，绝不回退成"相信客户端"）。</param>
    /// <param name="shardId">角色所在 shard。<b>必须按角色路由解析</b>：实体 id 由刷怪点生成，
    /// 多 shard 用同一份刷怪表时会撞号，跨 shard 乱查等于给假 id 开后门。</param>
    /// <param name="entityId">客户端上报的运行时实体 id。</param>
    /// <param name="characterFlaxPos">角色位置（已换到 Flax 空间）；null = 缓存里没有位置，判不了距离。</param>
    /// <param name="radius">允许半径。</param>
    public static Verdict Evaluate(
        DsEntityRegistry? registry,
        long shardId,
        ulong entityId,
        (float X, float Y, float Z)? characterFlaxPos,
        float radius = DefaultRadius)
    {
        if (entityId == 0UL) return new Verdict(false, "unknown_entity", 0, default);
        if (registry is null) return new Verdict(false, "no_registry", 0, default);
        if (!registry.TryResolve(shardId, entityId, out var entry))
            return new Verdict(false, "unknown_entity", 0, default);

        var kind = entry.QuestKind;
        // 怪物不是对话对象；触发区域没有"和它说话"的语义。可交互 = 人 / 物品 / 场景物件。
        if (kind is not (QuestEntityKind.Npc or QuestEntityKind.Item or QuestEntityKind.Prop))
            return new Verdict(false, "not_interactable", entry.TemplateId, kind);

        if (characterFlaxPos is not { } pos)
            return new Verdict(false, "no_position", entry.TemplateId, kind);

        var dx = entry.X - pos.X;
        var dy = entry.Y - pos.Y;
        var dz = entry.Z - pos.Z;
        if (dx * dx + dy * dy + dz * dz > radius * radius)
            return new Verdict(false, "too_far", entry.TemplateId, kind);

        return new Verdict(true, string.Empty, entry.TemplateId, kind);
    }

    /// <summary>
    /// 把"玩家正对着哪个对象"换成**服务端认定的** (类别, 模板 id)：可接列表的发布者收窄过滤器用它，
    /// 于是这个过滤器不再依赖客户端自报身份（旧协议就是让客户端填 kind+模板 id，谁都能填）。
    /// <para>
    /// 判定与交互裁决同一套（存在 + 可交互类别 + 距离），所以"能看到这个 NPC 给的任务"和
    /// "能跟它说上话"在现场是同一个门槛，不会出现"列表里有、点上去说太远"。
    /// </para>
    /// </summary>
    public static bool TryResolveGiver(
        DsEntityRegistry? registry, long shardId, ulong entityId,
        (float X, float Y, float Z)? characterFlaxPos,
        out int giverKind, out int giverTemplateId, out string reason,
        float radius = DefaultRadius)
    {
        var verdict = Evaluate(registry, shardId, entityId, characterFlaxPos, radius);
        giverKind = (int)verdict.Kind;
        giverTemplateId = verdict.TemplateId;
        reason = verdict.Reason;
        return verdict.Ok;
    }

    /// <summary>
    /// 一条目标串是否由这次交互推进。
    /// <para>
    /// 用 <see cref="QuestObjectiveCodec"/> 精确解析而不是关键词包含：
    /// "与 NPC 对话"的目标一旦允许裸匹配，任何一句带"对话"的历史描述都会被任意 NPC 推进。
    /// </para>
    /// </summary>
    public static bool MatchesObjective(string? objectiveType, QuestEntityKind kind, int templateId)
    {
        if (templateId <= 0) return false;
        if (!QuestObjectiveCodec.TryDecode(objectiveType ?? string.Empty, out var spec)) return false;

        return spec.Kind switch
        {
            QuestObjectiveKind.TalkNpc => kind == QuestEntityKind.Npc && spec.TemplateId == templateId,
            QuestObjectiveKind.InteractProp => (kind == QuestEntityKind.Prop || kind == QuestEntityKind.Item)
                                               && spec.TemplateId == templateId,
            // 其余目标各有自己的事件源（击杀走 DS 玩法事件、采集/使用走背包与使用道具链路），
            // 一次"交互"绝不越权推进它们。
            _ => false,
        };
    }
}
