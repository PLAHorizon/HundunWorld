using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Horizon.MonsterCli;

/// <summary>
/// 怪物草稿（编辑器 ↔ CLI 之间的**唯一 JSON 契约**）。
/// <para>
/// 字段名即协议：<see cref="KnownTopLevelKeys"/> 等护栏列表用于"写入前自证字段名没漂"。
/// <see cref="JsonSerializerOptions"/> 默认不绑公开字段，漏 <c>IncludeFields</c> 时反序列化会
/// "成功"但全是初值，两侧同为 0 反而全绿 —— 所以这里一律用**属性**而不是字段，
/// 并由 <c>MonsterEditorContractTests</c> 断言插件侧镜像的键集合与本表逐字相同。
/// </para>
/// </summary>
public sealed class MonsterDraft
{
    /// <summary>顶层已知键（自证用；新增字段必须同时进这张表，否则保存被拒）。</summary>
    public static readonly HashSet<string> KnownTopLevelKeys = new(StringComparer.Ordinal)
    {
        "monsterId", "name", "rank", "species", "wuxingDomain",
        "modelKey", "prefabGuid", "prefabPath", "modelScale", "standState",
        "levelBase", "levelMin", "levelMax", "stats", "xp",
        "respawnBaseSeconds", "respawnPerLevelSeconds", "corpseSeconds",
        "lootWindowSeconds", "pickupDistanceMeters", "aggroSayId",
        "behavior", "skills", "lootBindings", "patrolRoute", "spawnPoints",
        "publishState", "contentVersion", "remark",
    };

    /// <summary>怪物编号。</summary>
    public int MonsterId { get; set; }

    /// <summary>显示名（空名会让客户端回退成 Monster_&lt;id&gt;，校验按必填处理）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>rank：0普通 1精英 2稀有精英 3首领 4世界BOSS。</summary>
    public int Rank { get; set; }

    /// <summary>种族。</summary>
    public int Species { get; set; }

    /// <summary>五行域 0..5。</summary>
    public int WuxingDomain { get; set; }

    public string ModelKey { get; set; } = string.Empty;
    public string? PrefabGuid { get; set; }

    /// <summary>prefab 的内容树路径（客户端按路径解析 GUID；空 = 只登记了意图、还没进包）。</summary>
    public string? PrefabPath { get; set; }

    public double ModelScale { get; set; } = 1.0;

    /// <summary>待机姿态 0站1坐2卧3潜水。</summary>
    public int StandState { get; set; }

    public int LevelBase { get; set; } = 1;
    public int LevelMin { get; set; } = 1;
    public int LevelMax { get; set; } = 1;

    public MonsterStatsDraft Stats { get; set; } = new();
    public MonsterXpDraft Xp { get; set; } = new();

    public double RespawnBaseSeconds { get; set; } = 15.0;
    public double RespawnPerLevelSeconds { get; set; }
    public double CorpseSeconds { get; set; } = 60.0;
    /// <summary>尸体可拾取窗口秒（缺省 AC 60）。</summary>
    public double LootWindowSeconds { get; set; } = 60.0;
    /// <summary>拾取交互距离米（缺省 AC 5.5）。</summary>
    public double PickupDistanceMeters { get; set; } = 5.5;
    public int AggroSayId { get; set; }

    public MonsterBehaviorDraft Behavior { get; set; } = new();
    public List<MonsterSkillDraft> Skills { get; set; } = new();
    public List<MonsterLootBindingDraft> LootBindings { get; set; } = new();
    public MonsterRouteDraft? PatrolRoute { get; set; }
    public List<MonsterSpawnDraft> SpawnPoints { get; set; } = new();

    /// <summary>0草稿 1已发布 2停用。</summary>
    public int PublishState { get; set; }

    /// <summary>乐观锁版本。</summary>
    public int ContentVersion { get; set; }

    public string Remark { get; set; } = string.Empty;

    /// <summary>契约用的序列化选项（与插件侧逐字相同：驼峰 + 忽略 null + 缩进）。</summary>
    public static readonly JsonSerializerOptions Contract = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Contract);

    public static MonsterDraft? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<MonsterDraft>(json, Contract);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>属性公式（截距 + 每级增量 + 基点覆盖）。</summary>
public sealed class MonsterStatsDraft
{
    public static readonly HashSet<string> KnownStatsKeys = new(StringComparer.Ordinal)
    {
        "hpBase", "hpPerLevel", "atkBase", "atkPerLevel", "defBase", "defPerLevel",
        "hpOverrideBps", "atkOverrideBps",
    };

    public int HpBase { get; set; } = 100;
    public int HpPerLevel { get; set; }
    public int AtkBase { get; set; } = 10;
    public int AtkPerLevel { get; set; }
    public int DefBase { get; set; }
    public int DefPerLevel { get; set; }
    public int HpOverrideBps { get; set; }
    public int AtkOverrideBps { get; set; }
}

/// <summary>经验系数（定基口径 = 怪物等级）。</summary>
public sealed class MonsterXpDraft
{
    public static readonly HashSet<string> KnownXpKeys = new(StringComparer.Ordinal)
    { "xpBase", "xpPerLevel", "xpModBps", "healthModBps" };

    public int XpBase { get; set; } = 45;
    public int XpPerLevel { get; set; } = 5;

    /// <summary>逐模板经验倍率基点（10000=×1；魔兽的 CreatureTemplate::ModExperience）。</summary>
    public int XpModBps { get; set; } = 10000;

    /// <summary>"血薄削减"基点（魔兽的 ModHealth）：只有 0..7500 生效，10000=不削减。</summary>
    public int HealthModBps { get; set; } = 10000;
}

/// <summary>行为配置（档位 + 五个能力位覆盖 + 半径/速度/战术阈值）。</summary>
public sealed class MonsterBehaviorDraft
{
    public static readonly HashSet<string> KnownBehaviorKeys = new(StringComparer.Ordinal)
    {
        "aiProfile", "flagUseThreat", "flagUseSkills", "flagUseNavMesh", "flagUseAssist", "flagUseTactics",
        "isAggressive", "aggroRadius", "aggroConeDegrees", "leashRadius", "evadeImmunitySeconds",
        "assistRadius", "detectMask", "wanderRadius", "idleToPatrolSeconds",
        "walkSpeed", "moveSpeed", "followType", "meleeSwingSeconds",
        "rangedMinDistance", "rangedMaxDistance", "fleeHpPercent", "patrolRouteId",
    };

    public int AiProfile { get; set; }

    // 覆盖位：null = 跟档位预设。用 int? 而不是 bool?——库里存 tinyint，两侧同型少一次翻译。
    public int? FlagUseThreat { get; set; }
    public int? FlagUseSkills { get; set; }
    public int? FlagUseNavMesh { get; set; }
    public int? FlagUseAssist { get; set; }
    public int? FlagUseTactics { get; set; }

    public int IsAggressive { get; set; } = 1;
    public double AggroRadius { get; set; } = 10.0;
    public double AggroConeDegrees { get; set; } = 360.0;
    public double LeashRadius { get; set; } = 30.0;
    public double EvadeImmunitySeconds { get; set; } = 3.0;
    public double AssistRadius { get; set; } = 10.0;
    public int DetectMask { get; set; } = 1;
    public double WanderRadius { get; set; } = 6.0;
    public double IdleToPatrolSeconds { get; set; } = 3.0;
    public double WalkSpeed { get; set; } = 2.5;
    public double MoveSpeed { get; set; } = 4.0;
    public int FollowType { get; set; }
    public double MeleeSwingSeconds { get; set; } = 1.5;
    public double RangedMinDistance { get; set; }
    public double RangedMaxDistance { get; set; }
    public int FleeHpPercent { get; set; }
    public int PatrolRouteId { get; set; }
}

/// <summary>技能挂载行。</summary>
public sealed class MonsterSkillDraft
{
    public static readonly HashSet<string> KnownSkillKeys = new(StringComparer.Ordinal)
    {
        "skillId", "slotSeq", "slotType", "chanceBps", "castMs", "cooldownMs",
        "minRange", "maxRange", "targetSelect", "targetCount", "aoeRadius",
        "schoolMask", "interruptible", "allowMovement", "damageScaleBps",
        "statusId", "statusDurationSeconds", "statusMagnitudeBps", "startEmote", "emergencyHpPercent",
    };

    public int SkillId { get; set; }
    public int SlotSeq { get; set; }
    public int SlotType { get; set; }
    public int ChanceBps { get; set; } = 10000;
    public int CastMs { get; set; }
    public int CooldownMs { get; set; } = 5000;
    public double MinRange { get; set; }
    public double MaxRange { get; set; } = 8.0;
    public int TargetSelect { get; set; }
    public int TargetCount { get; set; } = 1;
    public double AoeRadius { get; set; }
    public int SchoolMask { get; set; }
    public int Interruptible { get; set; } = 1;
    public int AllowMovement { get; set; }
    public int DamageScaleBps { get; set; } = 10000;
    public int StatusId { get; set; }
    public double StatusDurationSeconds { get; set; }

    /// <summary>状态强度（基点；今天只有减速用：5000 = 走速打五折；0 = 没填、不生效）。</summary>
    public int StatusMagnitudeBps { get; set; }
    public int StartEmote { get; set; }
    public int EmergencyHpPercent { get; set; } = 30;
}

/// <summary>掉落绑定（一怪 N 表；表内含逐件条目）。</summary>
public sealed class MonsterLootBindingDraft
{
    public static readonly HashSet<string> KnownLootBindingKeys = new(StringComparer.Ordinal)
    {
        "lootTableId", "rollChanceBps", "minCount", "maxCount",
        "conditionKind", "questId", "moneyMin", "moneyMax", "entries",
    };

    public int LootTableId { get; set; }
    public int RollChanceBps { get; set; }
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    public int ConditionKind { get; set; }
    public int QuestId { get; set; }
    public int MoneyMin { get; set; }
    public int MoneyMax { get; set; }
    public List<MonsterDropDraft> Entries { get; set; } = new();
}

/// <summary>逐件掉落条目。</summary>
public sealed class MonsterDropDraft
{
    public static readonly HashSet<string> KnownDropKeys = new(StringComparer.Ordinal)
    {
        "itemId", "chanceBps", "countMin", "countMax", "seq", "groupId", "conditionKind", "questId",
    };

    public int ItemId { get; set; }
    public int ChanceBps { get; set; } = 1000;
    public int CountMin { get; set; } = 1;
    public int CountMax { get; set; } = 1;
    public int Seq { get; set; }

    /// <summary>掉落组号：0=逐件独立掷，&gt;0=同组互斥抽一件（魔兽 creature_loot_template.GroupId）。</summary>
    public int GroupId { get; set; }

    /// <summary>逐件掉落条件：<b>-1=继承所属绑定</b>（魔兽把 QuestRequired 挂在每一行上的那一格）。</summary>
    public int ConditionKind { get; set; } = -1;

    /// <summary>条件=任务持有者时的任务 id；0 = 用绑定上那一份。</summary>
    public int QuestId { get; set; }
}

/// <summary>巡逻路线（点列随路线一起存）。</summary>
public sealed class MonsterRouteDraft
{
    public static readonly HashSet<string> KnownRouteKeys = new(StringComparer.Ordinal)
    {
        "routeId", "name", "mode", "pauseAtPointSeconds", "useNavMesh", "points",
    };

    public int RouteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Mode { get; set; } = 1;
    public double PauseAtPointSeconds { get; set; } = 2.0;
    public int UseNavMesh { get; set; } = 1;
    public List<MonsterPointDraft> Points { get; set; } = new();
}

/// <summary>巡逻点（服务端 Z-up 坐标；换轴在编辑器侧完成）。</summary>
public sealed class MonsterPointDraft
{
    public static readonly HashSet<string> KnownPointKeys = new(StringComparer.Ordinal)
    {
        "seq", "x", "y", "z", "pauseSeconds",
    };

    public int Seq { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double? PauseSeconds { get; set; }
}

/// <summary>刷怪点。</summary>
public sealed class MonsterSpawnDraft
{
    public static readonly HashSet<string> KnownSpawnKeys = new(StringComparer.Ordinal)
    {
        "spawnPointId", "zoneId", "shardId", "x", "y", "z", "yaw",
        "levelOverride", "maxAlive", "slotSpacing", "respawnOverrideSeconds",
        "routeOverrideId", "enabled",
    };

    public int SpawnPointId { get; set; }
    public int ZoneId { get; set; }
    public long ShardId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Yaw { get; set; }
    public int? LevelOverride { get; set; }
    public int MaxAlive { get; set; } = 3;
    public double SlotSpacing { get; set; } = 8.0;
    public double RespawnOverrideSeconds { get; set; }
    public int RouteOverrideId { get; set; }
    public int Enabled { get; set; } = 1;
}
