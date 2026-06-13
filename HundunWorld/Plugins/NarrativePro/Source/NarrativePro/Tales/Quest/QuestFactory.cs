using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Tasks;

namespace NarrativePro.Tales.Quest
{
    public static class QuestFactory
    {
        public static Quest LoadQuest(string jsonFilePath)
        {
            string json = File.ReadAllText(jsonFilePath);
            var questData = JsonSerializer.Deserialize<QuestData>(json);
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

            string error;
            if (!ValidateQuest(quest, out error))
            {
                throw new Exception($"Quest validation failed for {jsonFilePath}: {error}");
            }

            return quest;
        }

        private static NarrativeTask CreateTask(TaskData taskData)
        {
            var task = new GenericTask
            {
                RequiredQuantity = taskData.RequiredQuantity,
                DescriptionOverride = taskData.Description ?? "",
                TaskTypeId = taskData.Type ?? "",
                TargetId = taskData.TargetId ?? ""
            };
            return task;
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
        [JsonPropertyName("states")] public List<QuestStateData> States { get; set; } = new List<QuestStateData>();
        [JsonPropertyName("branches")] public List<QuestBranchData> Branches { get; set; } = new List<QuestBranchData>();
        [JsonPropertyName("startStateId")] public string StartStateId { get; set; }
    }

    public class QuestStateData
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("stateType")] public EStateNodeType StateType { get; set; }
        [JsonPropertyName("position")] public NodePositionData Position { get; set; }
    }

    public class QuestBranchData
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("fromStateId")] public string FromStateId { get; set; }
        [JsonPropertyName("toStateId")] public string ToStateId { get; set; }
        [JsonPropertyName("tasks")] public List<TaskData> Tasks { get; set; } = new List<TaskData>();
        [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    }

    public class TaskData
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("targetId")] public string TargetId { get; set; }
        [JsonPropertyName("requiredQuantity")] public int RequiredQuantity { get; set; } = 1;
        [JsonPropertyName("description")] public string Description { get; set; }
    }

    public class NodePositionData
    {
        [JsonPropertyName("x")] public float X { get; set; }
        [JsonPropertyName("y")] public float Y { get; set; }
    }
}
