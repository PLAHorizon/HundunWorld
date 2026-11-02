using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestEditor
{
    /// <summary>
    /// 任务草稿的 JSON 模型（编辑器与 Horizon.QuestCli / 服务端 Horizon.Game.Sim.Quests.QuestDraft 同一套字段名）。
    /// <para>
    /// 字段名是<b>协议</b>：这里改名而服务端不改，System.Text.Json 会把对不上的属性静默落成默认值——
    /// 表现是"编辑器里填了、库里是空的"，没有任何报错。因此 <see cref="KnownTopLevelKeys"/> 列出了
    /// 服务端认识的键名，窗口保存前会做一次自检（见 QuestEditorWindow.ValidateKeyNames）。
    /// </para>
    /// </summary>
    public class QuestDraftModel
    {
        public int QuestId { get; set; }
        public string QuestName { get; set; } = "";
        public string Description { get; set; } = "";
        public int QuestType { get; set; } = 1;
        public int LevelRequire { get; set; }
        public int WuxingDomain { get; set; }
        public int RepeatLimit { get; set; }
        public int PublishState { get; set; }
        public bool IsValid { get; set; } = true;
        public int ContentVersion { get; set; }
        public EntityRefModel Giver { get; set; } = new EntityRefModel();
        public EntityRefModel TurnIn { get; set; }
        public List<int> PrerequisiteQuests { get; set; } = new List<int>();
        public List<ObjectiveModel> Objectives { get; set; } = new List<ObjectiveModel>();
        public RewardModel Rewards { get; set; } = new RewardModel();

        /// <summary>顶层键名清单（与服务端 QuestDraft 的属性一一对应，用于防静默改名）。</summary>
        public static readonly HashSet<string> KnownTopLevelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "questId", "questName", "description", "questType", "levelRequire", "wuxingDomain",
            "repeatLimit", "publishState", "isValid", "contentVersion", "giver", "turnIn",
            "prerequisiteQuests", "objectives", "rewards",
        };

        public static readonly HashSet<string> KnownObjectiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "kind", "templateId", "count", "description", "mandatory",
        };

        public static readonly HashSet<string> KnownRewardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "exp", "gold", "items", "favor",
        };

        public static readonly HashSet<string> KnownEntityRefKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "kind", "templateId", "zoneId", "spawnPointId",
        };

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            // 刻意不设 Encoder：System.Text.Encodings.Web 不在 Flax 脚本域的引用集里，
            // 中文会被转成 \uXXXX —— 这段 JSON 只交给 CLI 解析，不进人眼，转义无害。
        };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static QuestDraftModel FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new QuestDraftModel();
            return JsonSerializer.Deserialize<QuestDraftModel>(json, Options) ?? new QuestDraftModel();
        }

        /// <summary>新建一条空白任务草稿（默认支线、下线状态、由铁匠发布）。</summary>
        public static QuestDraftModel CreateNew(int suggestedId)
        {
            var draft = new QuestDraftModel
            {
                QuestId = suggestedId,
                QuestName = "新任务",
                Description = "",
                QuestType = 1,
                LevelRequire = 1,
                PublishState = 0,
                IsValid = true,
                Giver = new EntityRefModel { Kind = "npc", TemplateId = 1 },
            };
            draft.Objectives.Add(new ObjectiveModel { Kind = "kill_monster", TemplateId = 4, Count = 3, Description = "" });
            return draft;
        }
    }

    /// <summary>实体引用（发布者/交付者/条件目标）。</summary>
    public class EntityRefModel
    {
        public string Kind { get; set; } = "npc";
        public int TemplateId { get; set; }
        public int ZoneId { get; set; }
        public int SpawnPointId { get; set; }
    }

    /// <summary>一条完成条件。</summary>
    public class ObjectiveModel
    {
        public string Kind { get; set; } = "kill_monster";
        public int TemplateId { get; set; }
        public int Count { get; set; } = 1;
        public string Description { get; set; } = "";
        public bool Mandatory { get; set; } = true;
    }

    /// <summary>奖励集合。</summary>
    public class RewardModel
    {
        public long Exp { get; set; }
        public long Gold { get; set; }
        public List<RewardItemModel> Items { get; set; } = new List<RewardItemModel>();
        public List<RewardFavorModel> Favor { get; set; } = new List<RewardFavorModel>();
    }

    /// <summary>一条物品奖励（可指向任何物品：装备/丹药/材料/宝石/任务物品/秘籍 800000+技能id）。</summary>
    public class RewardItemModel
    {
        public int TemplateId { get; set; }
        public int Count { get; set; } = 1;
    }

    /// <summary>一条五行域好感奖励。</summary>
    public class RewardFavorModel
    {
        public int Domain { get; set; } = 1;
        public int Points { get; set; } = 10;
    }
}
