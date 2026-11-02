using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;
using NarrativePro.Tales.Events;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Tasks;

namespace NarrativePro.Tales.Quest
{
    public static class QuestFactory
    {
        /// <summary>大小写不敏感绑定：任务/事件/条件元素的字段名要能和 C# 属性名互相兼容（同 ItemFactory）。</summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static Quest LoadQuest(string jsonFilePath)
        {
            string json = File.ReadAllText(jsonFilePath);
            var questData = JsonSerializer.Deserialize<QuestData>(json, _jsonOptions);
            if (questData == null) throw new Exception($"Failed to deserialize quest from {jsonFilePath}");

            var quest = new Quest();
            quest.QuestName = questData.QuestName;
            quest.QuestDescription = questData.QuestDescription;
            quest.QuestDialogueClassId = questData.QuestDialogueClass ?? "";

            var stateMap = new Dictionary<string, QuestState>();
            foreach (var stateData in questData.States)
            {
                var state = new QuestState
                {
                    ID = stateData.Id,
                    Description = stateData.Description,
                    StateNodeType = stateData.StateType
                };

                // 图位置：UE5 存在 UNarrativeNodeBase::NodePos，对话回复与分支都按它排序。
                if (stateData.Position != null)
                {
                    state.NodePosX = stateData.Position.X;
                    state.NodePosY = stateData.Position.Y;
                }

                // 节点级事件/条件：UE5 由资产内联实例直接给出，此处经 NarrativeLogicFactory 按类型名构造。
                BuildLogic(state.Events, stateData.Events);
                BuildLogic(state.Conditions, stateData.Conditions);

                stateMap[stateData.Id] = state;
                quest.States.Add(state);
            }

            foreach (var branchData in questData.Branches)
            {
                var branch = new QuestBranch
                {
                    ID = branchData.Id,
                    Description = branchData.Description,
                    bHidden = branchData.Hidden
                };

                if (branchData.Position != null)
                {
                    branch.NodePosX = branchData.Position.X;
                    branch.NodePosY = branchData.Position.Y;
                }

                BuildLogic(branch.Events, branchData.Events);
                BuildLogic(branch.Conditions, branchData.Conditions);

                if (stateMap.TryGetValue(branchData.ToStateId, out var destState))
                {
                    branch.DestinationState = destState;
                }

                if (branchData.Tasks != null)
                {
                    foreach (var taskData in branchData.Tasks)
                    {
                        var task = CreateTask(taskData);
                        if (task != null)
                        {
                            branch.QuestTasks.Add(task);
                        }
                    }
                }

                quest.Branches.Add(branch);

                if (stateMap.TryGetValue(branchData.FromStateId, out var fromState))
                {
                    fromState.Branches.Add(branch);
                }
            }

            if (!string.IsNullOrEmpty(questData.StartStateId) && stateMap.TryGetValue(questData.StartStateId, out var startState))
            {
                quest.QuestStartState = startState;
            }

            // 任务要求：UE5 是资产内联的 Instanced UQuestRequirement 数组，这里改用
            // "questRequirements": [ { "type": "StayNearActorRequirement", ... } ] 走按名构造。
            if (questData.Requirements != null)
            {
                foreach (var node in questData.Requirements)
                {
                    if (node.ValueKind != JsonValueKind.Object) continue;
                    string typeId = "";
                    if (node.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                    {
                        typeId = typeProp.GetString();
                    }
                    var requirement = QuestRequirementFactory.Create(typeId, node);
                    if (requirement != null)
                    {
                        quest.QuestRequirements.Add(requirement);
                    }
                }
            }

            string error;
            if (!ValidateQuest(quest, out error))
            {
                throw new Exception($"Quest validation failed for {jsonFilePath}: {error}");
            }

            return quest;
        }

        /// <summary>
        /// 构造分支上的任务条目。UE5 里 <c>UQuestBranch::QuestTasks</c>（QuestSM.h）是资产内联的
        /// Instanced 对象，运行时直接拿到实例；Flax 侧资产是 JSON，故按任务节点的
        /// <c>"type"</c> 走 <see cref="NarrativeLogicFactory"/> 注册表构造具体任务类，
        /// 并把其余 JSON 字段（waitDuration / itemId / targetLocation / bOptional 等）反射赋到实例上。
        /// 未注册的任务名保持旧行为：回退 <see cref="GenericTask"/> 并保留 type/targetId 作为数据，
        /// 这样 "type": "KillNPC" 这类纯数据任务不会因未注册而丢失。
        /// </summary>
        private static NarrativeTask CreateTask(TaskData taskData)
        {
            if (taskData == null) return null;

            var task = NarrativeLogicFactory.CreateTask(taskData.Type, taskData.ExtraFields);
            if (task == null) return null;

            // 已知字段以 DTO 为准；JSON 未写该键时不去覆盖任务类自身的默认值。
            if (taskData.RequiredQuantity.HasValue)
            {
                task.RequiredQuantity = taskData.RequiredQuantity.Value;
            }
            if (!string.IsNullOrEmpty(taskData.Description))
            {
                task.DescriptionOverride = taskData.Description;
            }

            if (task is GenericTask generic)
            {
                generic.TaskTypeId = taskData.Type ?? "";
                generic.TargetId = taskData.TargetId ?? "";
            }

            return task;
        }

        /// <summary>把状态/分支 JSON 上的 <c>"events"</c> 构造成事件实例（对应 UE5 节点内联的 UNarrativeEvent）。</summary>
        private static void BuildLogic(List<NarrativeEvent> target, List<JsonElement> elements)
        {
            if (target == null || elements == null || elements.Count == 0) return;
            foreach (var evt in NarrativeLogicFactory.CreateEvents(elements))
            {
                target.Add(evt);
            }
        }

        /// <summary>把状态/分支 JSON 上的 <c>"conditions"</c> 构造成条件实例（对应 UE5 节点内联的 UNarrativeCondition）。</summary>
        private static void BuildLogic(List<NarrativeCondition> target, List<JsonElement> elements)
        {
            if (target == null || elements == null || elements.Count == 0) return;
            foreach (var cond in NarrativeLogicFactory.CreateConditions(elements))
            {
                target.Add(cond);
            }
        }

        public static bool ValidateQuest(Quest quest, out string error)
        {
            error = "";

            if (quest.QuestStartState == null)
            {
                error = "Quest has no start state";
                return false;
            }

            bool hasTerminalState = false;
            foreach (var state in quest.States)
            {
                if (state.StateNodeType == EStateNodeType.Success || state.StateNodeType == EStateNodeType.Failure)
                {
                    hasTerminalState = true;
                    break;
                }
            }

            if (!hasTerminalState)
            {
                error = "Quest has no Success or Failure terminal state";
                return false;
            }

            foreach (var branch in quest.Branches)
            {
                if (branch.DestinationState == null)
                {
                    error = $"Branch '{branch.ID}' has no destination state";
                    return false;
                }
            }

            return true;
        }

        public static Dictionary<string, Quest> LoadAllQuests(string directoryPath)
        {
            var quests = new Dictionary<string, Quest>();
            if (!Directory.Exists(directoryPath)) return quests;

            foreach (var file in Directory.GetFiles(directoryPath, "*.json"))
            {
                try
                {
                    var quest = LoadQuest(file);
                    string key = Path.GetFileNameWithoutExtension(file);
                    quests[key] = quest;
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"Failed to load quest from {file}: {ex.Message}");
                }
            }
            return quests;
        }
    }

    public class QuestData
    {
        [JsonPropertyName("questId")] public string QuestId { get; set; }
        [JsonPropertyName("questName")] public string QuestName { get; set; }
        [JsonPropertyName("questDescription")] public string QuestDescription { get; set; }
        [JsonPropertyName("questDialogueClass")] public string QuestDialogueClass { get; set; }
        [JsonPropertyName("questRequirements")] public List<JsonElement> Requirements { get; set; } = new List<JsonElement>();
        [JsonPropertyName("states")] public List<QuestStateData> States { get; set; } = new List<QuestStateData>();
        [JsonPropertyName("branches")] public List<QuestBranchData> Branches { get; set; } = new List<QuestBranchData>();
        [JsonPropertyName("startStateId")] public string StartStateId { get; set; }

        /// <summary>
        /// 这条叙事图实现的是"任务编辑器"里的哪条任务模板（SQL Server QuestTemplate 的 id）。
        /// 两个编辑器各自的作者面：任务编辑器写模板内容/完成条件/奖励，叙事编辑写流程与台词；
        /// 这个字段是两边唯一的显式挂钩，运行时不消费它，留空表示"纯叙事任务、不对应模板"。
        /// </summary>
        [JsonPropertyName("questTemplateId")] public int? QuestTemplateId { get; set; }
    }

    public class QuestStateData
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("stateType")] public EStateNodeType StateType { get; set; }
        [JsonPropertyName("position")] public NodePositionData Position { get; set; }

        /// <summary>状态进入/退出时执行的事件。对应 UE5 <c>UNarrativeNodeBase::Events</c>。</summary>
        [JsonPropertyName("events")] public List<JsonElement> Events { get; set; }

        /// <summary>状态推进所需条件。对应 UE5 <c>UNarrativeNodeBase::Conditions</c>。</summary>
        [JsonPropertyName("conditions")] public List<JsonElement> Conditions { get; set; }
    }

    public class QuestBranchData
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("fromStateId")] public string FromStateId { get; set; }
        [JsonPropertyName("toStateId")] public string ToStateId { get; set; }
        [JsonPropertyName("tasks")] public List<TaskData> Tasks { get; set; } = new List<TaskData>();
        [JsonPropertyName("hidden")] public bool Hidden { get; set; }
        [JsonPropertyName("position")] public NodePositionData Position { get; set; }

        /// <summary>分支被取走时执行的事件。对应 UE5 <c>UQuestBranch</c> 继承自节点基类的 <c>Events</c>。</summary>
        [JsonPropertyName("events")] public List<JsonElement> Events { get; set; }

        /// <summary>分支可选条件。对应 UE5 <c>Conditions</c>。</summary>
        [JsonPropertyName("conditions")] public List<JsonElement> Conditions { get; set; }
    }

    public class TaskData
    {
        /// <summary>任务类型标识。UE5 是 <c>TSubclassOf&lt;UNarrativeTask&gt;</c>，此处为注册表键（"CollectItem" / "WaitTask"…）。</summary>
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("targetId")] public string TargetId { get; set; }

        /// <summary>可空：JSON 未写该键时保持 null，不去覆盖任务类自身的默认需求数量。</summary>
        [JsonPropertyName("requiredQuantity")] public int? RequiredQuantity { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }

        /// <summary>
        /// 任务类自己的字段（itemId / waitDuration / targetLocation / bOptional / tickInterval …）。
        /// <see cref="NarrativeLogicFactory"/> 按构造出的具体类型逐一反射赋值。
        /// </summary>
        [JsonExtensionData] public Dictionary<string, JsonElement> ExtraFields { get; set; }
            = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    }

    public class NodePositionData
    {
        [JsonPropertyName("x")] public float X { get; set; }
        [JsonPropertyName("y")] public float Y { get; set; }
    }
}
