namespace Horizon.Game.Sim;

/// <summary>特殊事件的形态。</summary>
public enum WorldEventMode : byte
{
    /// <summary>玩法型：生成一个带 TTL 的怪群（走脚本生成点通路）。</summary>
    MonsterGroup = 0,

    /// <summary>表现型：只下发一个视觉/播报事件，不在世界里建实体。</summary>
    Presentation = 1,
}

/// <summary>世界事件模板。</summary>
/// <param name="TemplateId">事件 ID（<c>/event</c> 解析出的白名单 ID）。</param>
/// <param name="Name">事件显示名，同时作为表现型事件的屏幕文字。</param>
/// <param name="Mode">事件形态。</param>
/// <param name="MonsterTemplateId">玩法型事件要召唤的怪物模板；表现型事件忽略。</param>
/// <param name="SpawnCount">玩法型事件的怪物数。</param>
public readonly record struct WorldEventInfo(
    int TemplateId, string Name, WorldEventMode Mode, int MonsterTemplateId, int SpawnCount);

/// <summary>
/// 特殊事件目录（设计方案 §3.2）。
/// <para>
/// <b>事件形态由目录决定，不由客户端决定</b>：客户端只给事件标识，"这次是刷怪还是放个光柱"
/// 是服务端配置。否则一条指令就能让客户端指定生成参数，白名单与预算都失去意义。
/// </para>
/// <para>
/// 目录同时被授权侧（白名单校验）与 DS（按 ID 取形态）读取，因此两侧对"这是什么事件"
/// 只有一个事实源。生产环境应替换为配置表下发，调用点不变。
/// </para>
/// </summary>
public static class WorldEventCatalog
{
    /// <summary>开发用默认事件表。</summary>
    private static readonly WorldEventInfo[] Events =
    {
        new(1, "墨麒麟降世", WorldEventMode.MonsterGroup, 1, 3),
        new(2, "石甲狰袭营", WorldEventMode.MonsterGroup, 2, 5),
        new(3, "天空异象", WorldEventMode.Presentation, 0, 0),
        new(4, "地脉震动", WorldEventMode.Presentation, 0, 0),
    };

    /// <summary>取事件信息；未登记返回 false（不做兜底，避免未登记事件被放行）。</summary>
    public static bool TryGetRegistered(int templateId, out WorldEventInfo info)
    {
        for (var i = 0; i < Events.Length; i++)
        {
            if (Events[i].TemplateId == templateId)
            {
                info = Events[i];
                return true;
            }
        }

        info = default;
        return false;
    }

    /// <summary>按事件名查已登记事件。</summary>
    public static bool TryFindByName(string key, out WorldEventInfo info)
    {
        for (var i = 0; i < Events.Length; i++)
        {
            if (System.String.Equals(Events[i].Name, key, System.StringComparison.OrdinalIgnoreCase))
            {
                info = Events[i];
                return true;
            }
        }

        info = default;
        return false;
    }
}
