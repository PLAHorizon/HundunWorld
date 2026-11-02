namespace Horizon.Game.Sim.Quests;

/// <summary>任务可引用的实体类别。发布者是"人/物/物件"，完成条件还可以是怪物、地点。</summary>
public enum QuestEntityKind : byte
{
    /// <summary>人形 NPC，模板号见 <see cref="NpcTemplateCatalog"/>。</summary>
    Npc = 1,
    /// <summary>怪物，模板号见 <see cref="MonsterTemplateCatalog"/>。</summary>
    Monster = 2,
    /// <summary>物品（作为触发物或交付物，模板号即物品模板 id）。</summary>
    Item = 3,
    /// <summary>场景物件/采集物（脚本生成点承载）。</summary>
    Prop = 4,
    /// <summary>地点（按区域 id 判定）。</summary>
    Zone = 5,
}

/// <summary>
/// 任务里对"某个 NPC / 怪物 / 物品 / 物件 / 地点"的引用。
/// <para>
/// 用模板 id 而不是 prefab guid：本工程 NPC 没有 prefab/场景名注册表，运行时身份 =
/// 模板 id + 生成点 id（<c>SpawnDirectorCore</c> 的 <c>(spawnPointId &lt;&lt; 32) | slot</c>）。
/// </para>
/// </summary>
/// <param name="Kind">类别。</param>
/// <param name="TemplateId">模板 id；<see cref="QuestEntityKind.Zone"/> 时为区域 id。</param>
/// <param name="ZoneId">限定区域，0 = 不限。</param>
/// <param name="SpawnPointId">限定生成点，0 = 不限。</param>
public readonly record struct QuestEntityRef(QuestEntityKind Kind, int TemplateId, int ZoneId = 0, int SpawnPointId = 0)
{
    /// <summary>类别的规范串名（与草稿 JSON 里的 <c>kind</c> 字段一致）。</summary>
    public static string KindToToken(QuestEntityKind kind) => kind switch
    {
        QuestEntityKind.Npc => "npc",
        QuestEntityKind.Monster => "monster",
        QuestEntityKind.Item => "item",
        QuestEntityKind.Prop => "prop",
        QuestEntityKind.Zone => "zone",
        _ => "unknown",
    };

    /// <summary>解析串名；未知返回 false（草稿里写错类别要在入库前就被拒）。</summary>
    public static bool TryParseKind(string? token, out QuestEntityKind kind)
    {
        switch ((token ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "npc": kind = QuestEntityKind.Npc; return true;
            case "monster": kind = QuestEntityKind.Monster; return true;
            case "item": kind = QuestEntityKind.Item; return true;
            case "prop": kind = QuestEntityKind.Prop; return true;
            case "zone": kind = QuestEntityKind.Zone; return true;
            default: kind = QuestEntityKind.Npc; return false;
        }
    }

    /// <summary>该类别能否做任务发布者/交付者：怪物不做发布者，地点不做交付者。</summary>
    public static bool CanBeGiver(QuestEntityKind kind)
        => kind is QuestEntityKind.Npc or QuestEntityKind.Item or QuestEntityKind.Prop;
}
