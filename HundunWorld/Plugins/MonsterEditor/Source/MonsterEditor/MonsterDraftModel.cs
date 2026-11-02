using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonsterEditor
{
    /// <summary>
    /// 怪物草稿的插件侧镜像模型（与 <c>Horizon.MonsterCli.MonsterDraft</c> 是同一份协议的两份副本）。
    /// <para>
    /// <b>为什么必须各写一份</b>：Flax 脚本域里没有 EF/SqlClient/Orleans，插件不能引用仓库工程。
    /// 副本的风险是"改了来源忘了改这里"，所以两侧都有 <c>Known*Keys</c> 清单，
    /// 并由 <c>Horizon.MonsterContractTests</c> 断言两份键集合逐字相同（字段名是协议，
    /// <c>System.Text.Json</c> 对不认识的键**静默丢弃**，两侧同为初值时自校验反而全绿）。
    /// </para>
    /// </summary>
    public sealed class MonsterDraftModel
    {
        public static readonly HashSet<string> KnownTopLevelKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "monsterId", "name", "rank", "species", "wuxingDomain",
            "modelKey", "prefabGuid", "prefabPath", "modelScale", "standState",
            "levelBase", "levelMin", "levelMax", "stats", "xp",
            "respawnBaseSeconds", "respawnPerLevelSeconds", "corpseSeconds",
            "lootWindowSeconds", "pickupDistanceMeters", "aggroSayId",
            "behavior", "skills", "lootBindings", "patrolRoute", "spawnPoints",
            "publishState", "contentVersion", "remark",
        };

        public static readonly HashSet<string> KnownStatsKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "hpBase", "hpPerLevel", "atkBase", "atkPerLevel", "defBase", "defPerLevel",
            "hpOverrideBps", "atkOverrideBps",
        };

        public static readonly HashSet<string> KnownXpKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "xpBase", "xpPerLevel", "xpModBps", "healthModBps",
        };

        public static readonly HashSet<string> KnownBehaviorKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "aiProfile", "flagUseThreat", "flagUseSkills", "flagUseNavMesh", "flagUseAssist", "flagUseTactics",
            "isAggressive", "aggroRadius", "aggroConeDegrees", "leashRadius", "evadeImmunitySeconds",
            "assistRadius", "detectMask", "wanderRadius", "idleToPatrolSeconds",
            "walkSpeed", "moveSpeed", "followType", "meleeSwingSeconds",
            "rangedMinDistance", "rangedMaxDistance", "fleeHpPercent", "patrolRouteId",
        };

        public static readonly HashSet<string> KnownSkillKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "skillId", "slotSeq", "slotType", "chanceBps", "castMs", "cooldownMs",
            "minRange", "maxRange", "targetSelect", "targetCount", "aoeRadius",
            "schoolMask", "interruptible", "allowMovement", "damageScaleBps",
            "statusId", "statusDurationSeconds", "statusMagnitudeBps", "startEmote", "emergencyHpPercent",
        };

        public static readonly HashSet<string> KnownLootBindingKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "lootTableId", "rollChanceBps", "minCount", "maxCount",
            "conditionKind", "questId", "moneyMin", "moneyMax", "entries",
        };

        public static readonly HashSet<string> KnownDropKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "itemId", "chanceBps", "countMin", "countMax", "seq", "groupId", "conditionKind", "questId",
        };

        public static readonly HashSet<string> KnownRouteKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "routeId", "name", "mode", "pauseAtPointSeconds", "useNavMesh", "points",
        };

        public static readonly HashSet<string> KnownPointKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "seq", "x", "y", "z", "pauseSeconds",
        };

        public static readonly HashSet<string> KnownSpawnKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "spawnPointId", "zoneId", "shardId", "x", "y", "z", "yaw",
            "levelOverride", "maxAlive", "slotSpacing", "respawnOverrideSeconds",
            "routeOverrideId", "enabled",
        };

        public int MonsterId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Rank { get; set; }
        public int Species { get; set; }
        public int WuxingDomain { get; set; }
        public string ModelKey { get; set; } = string.Empty;
        public string PrefabGuid { get; set; }

        /// <summary>prefab 的内容树路径：客户端外观表唯一认的加载入口（GUID 跨打包不稳定）。</summary>
        public string PrefabPath { get; set; }

        public double ModelScale { get; set; } = 1.0;
        public int StandState { get; set; }
        public int LevelBase { get; set; } = 1;
        public int LevelMin { get; set; } = 1;
        public int LevelMax { get; set; } = 1;

        public MonsterStatsModel Stats { get; set; } = new MonsterStatsModel();
        public MonsterXpModel Xp { get; set; } = new MonsterXpModel();

        public double RespawnBaseSeconds { get; set; } = 15.0;
        public double RespawnPerLevelSeconds { get; set; }
        public double CorpseSeconds { get; set; } = 60.0;

        /// <summary>尸体可拾取窗口秒（缺省 60，可逐怪配置；对应 <c>MonsterCorpseCore.DefaultLootWindowSeconds</c>）。</summary>
        public double LootWindowSeconds { get; set; } = 60.0;

        /// <summary>拾取交互距离米（缺省 5.5，可逐怪配置；对应 <c>MonsterCorpseCore.DefaultPickupDistanceMeters</c>）。</summary>
        public double PickupDistanceMeters { get; set; } = 5.5;

        public int AggroSayId { get; set; }

        public MonsterBehaviorModel Behavior { get; set; } = new MonsterBehaviorModel();
        public List<MonsterSkillModel> Skills { get; set; } = new List<MonsterSkillModel>();
        public List<MonsterLootBindingModel> LootBindings { get; set; } = new List<MonsterLootBindingModel>();
        public MonsterRouteModel PatrolRoute { get; set; }
        public List<MonsterSpawnModel> SpawnPoints { get; set; } = new List<MonsterSpawnModel>();

        public int PublishState { get; set; }
        public int ContentVersion { get; set; }
        public string Remark { get; set; } = string.Empty;

        /// <summary>
        /// 序列化选项：驼峰 + 忽略 null + 缩进。
        /// <para>刻意**不设 Encoder**：脚本域里没有 <c>System.Text.Encodings.Web</c>，
        /// 于是中文会被转成 <c>\uXXXX</c> —— 无害，因为这段 JSON 只被子进程解析。</para>
        /// </summary>
        public static readonly JsonSerializerOptions Contract = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Contract);

        public static MonsterDraftModel FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<MonsterDraftModel>(json, Contract);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>新建一份可编辑的草稿（编号落在号段内、其余取保守默认）。</summary>
        public static MonsterDraftModel CreateNew(int suggestedId)
        {
            var draft = new MonsterDraftModel
            {
                MonsterId = suggestedId,
                Name = "新异兽",
                ModelKey = "monster_" + suggestedId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ContentVersion = 0,
            };
            return draft;
        }
    }

    /// <summary>属性公式（截距 + 每级增量 + 基点覆盖）。</summary>
    public sealed class MonsterStatsModel
    {
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
    public sealed class MonsterXpModel
    {
        public int XpBase { get; set; } = 45;
        public int XpPerLevel { get; set; } = 5;

        /// <summary>逐模板经验倍率基点（10000 = ×1；魔兽 CreatureTemplate::ModExperience）。</summary>
        public int XpModBps { get; set; } = 10000;

        /// <summary>血薄削减基点（魔兽 ModHealth）：只有 0..7500 生效，10000 = 不削减。</summary>
        public int HealthModBps { get; set; } = 10000;
    }

    /// <summary>行为配置。</summary>
    public sealed class MonsterBehaviorModel
    {
        public int AiProfile { get; set; }
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
    public sealed class MonsterSkillModel
    {
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

    /// <summary>掉落绑定。</summary>
    public sealed class MonsterLootBindingModel
    {
        public int LootTableId { get; set; }
        public int RollChanceBps { get; set; }
        public int MinCount { get; set; } = 1;
        public int MaxCount { get; set; } = 1;
        public int ConditionKind { get; set; }
        public int QuestId { get; set; }
        public int MoneyMin { get; set; }
        public int MoneyMax { get; set; }
        public List<MonsterDropModel> Entries { get; set; } = new List<MonsterDropModel>();
    }

    /// <summary>逐件掉落。</summary>
    public sealed class MonsterDropModel
    {
        public int ItemId { get; set; }
        public int ChanceBps { get; set; } = 1000;
        public int CountMin { get; set; } = 1;
        public int CountMax { get; set; } = 1;
        public int Seq { get; set; }

        /// <summary>掉落组号：0=逐件独立掷，&gt;0=同组互斥抽一件。与 CLI 侧、库列 group_id 同名同义。</summary>
        public int GroupId { get; set; }

        /// <summary>逐件掉落条件：<b>-1=继承所属绑定</b>，0=无条件，1=任务持有者，2=组队，3=稀有度门槛。</summary>
        public int ConditionKind { get; set; } = -1;

        /// <summary>条件=任务持有者时的任务 id；0 = 用绑定上那一份。</summary>
        public int QuestId { get; set; }
    }

    /// <summary>巡逻路线。</summary>
    public sealed class MonsterRouteModel
    {
        public int RouteId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Mode { get; set; } = 1;
        public double PauseAtPointSeconds { get; set; } = 2.0;
        public int UseNavMesh { get; set; } = 1;
        public List<MonsterPointModel> Points { get; set; } = new List<MonsterPointModel>();
    }

    /// <summary>巡逻点（服务端 Z-up 口径；编辑器从场景拾取时已完成换轴）。</summary>
    public sealed class MonsterPointModel
    {
        public int Seq { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double? PauseSeconds { get; set; }
    }

    /// <summary>刷怪点。</summary>
    public sealed class MonsterSpawnModel
    {
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
}
