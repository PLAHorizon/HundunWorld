using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.Core;
using NarrativePro.Tales;
using NarrativePro.Tales.Nodes;
using NarrativePro.Tales.Quest;
using NarrativePro.Tales.Tasks;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Save
{
    public class NarrativeSaveManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public ISaveStorageProvider StorageProvider { get; private set; }

        public NarrativeSaveManager(ISaveStorageProvider storageProvider = null)
        {
            StorageProvider = storageProvider;
        }

        public void SetStorageProvider(ISaveStorageProvider storageProvider)
        {
            StorageProvider = storageProvider;
        }

        public NarrativeSaveData SaveNarrativeState(TalesComponent talesComponent)
        {
            var saveData = new NarrativeSaveData();

            foreach (var quest in talesComponent.QuestList)
            {
                var savedQuest = new SavedQuest
                {
                    QuestClassId = quest.QuestName,
                    QuestCompletion = quest.QuestCompletion,
                    CurrentStateId = quest.CurrentState?.ID ?? "",
                    bTracked = quest.IsTracked()
                };

                foreach (var state in quest.ReachedStates)
                {
                    savedQuest.ReachedStateIds.Add(state.ID);
                }

                foreach (var branch in quest.Branches)
                {
                    var savedBranch = new SavedQuestBranch
                    {
                        BranchId = branch.ID
                    };

                    foreach (var task in branch.QuestTasks)
                    {
                        savedBranch.TasksProgress.Add(task.CurrentProgress);
                    }

                    savedQuest.Branches.Add(savedBranch);
                }

                saveData.SavedQuests.Add(savedQuest);
            }

            foreach (var kvp in talesComponent.MasterTaskList)
            {
                saveData.MasterTaskList[kvp.Key] = kvp.Value;
            }

            if (talesComponent.CurrentDialogue != null)
            {
                saveData.CurrentDialogueId = talesComponent.CurrentDialogue.DialogueId;
            }

            return saveData;
        }

        public void LoadNarrativeState(TalesComponent talesComponent, NarrativeSaveData saveData)
        {
            if (saveData == null) return;

            talesComponent.bIsLoading = true;

            foreach (var quest in new List<QuestClass>(talesComponent.QuestList))
            {
                quest.Deinitialize();
            }
            talesComponent.QuestList.Clear();

            foreach (var savedQuest in saveData.SavedQuests)
            {
                var quest = talesComponent.BeginQuest(savedQuest.QuestClassId);
                if (quest == null) continue;

                quest.ReachedStates.Clear();
                foreach (var stateId in savedQuest.ReachedStateIds)
                {
                    var state = quest.GetState(stateId);
                    if (state != null) quest.ReachedStates.Add(state);
                }

                foreach (var savedBranch in savedQuest.Branches)
                {
                    var branch = quest.GetBranch(savedBranch.BranchId);
                    if (branch == null) continue;

                    for (int i = 0; i < savedBranch.TasksProgress.Count && i < branch.QuestTasks.Count; i++)
                    {
                        branch.QuestTasks[i].SetProgress(savedBranch.TasksProgress[i]);
                    }
                }

                if (!string.IsNullOrEmpty(savedQuest.CurrentStateId))
                {
                    var currentState = quest.GetState(savedQuest.CurrentStateId);
                    if (currentState != null)
                    {
                        quest.EnterState(currentState);
                    }
                }

                quest.SetTracked(savedQuest.bTracked);
            }

            talesComponent.MasterTaskList.Clear();
            foreach (var kvp in saveData.MasterTaskList)
            {
                talesComponent.MasterTaskList[kvp.Key] = kvp.Value;
            }

            talesComponent.bIsLoading = false;
        }

        public string SerializeToJson(NarrativeSaveData saveData)
        {
            return JsonSerializer.Serialize(saveData, JsonOptions);
        }

        public NarrativeSaveData DeserializeFromJson(string json)
        {
            return JsonSerializer.Deserialize<NarrativeSaveData>(json);
        }

        public bool SaveToFile(NarrativeSaveData saveData, string filePath)
        {
            try
            {
                string json = SerializeToJson(saveData);

                if (StorageProvider != null)
                {
                    StorageProvider.SaveData(filePath, json);
                    return true;
                }

                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to save narrative data to {filePath}: {ex.Message}");
                return false;
            }
        }

        public NarrativeSaveData LoadFromFile(string filePath)
        {
            try
            {
                string json = null;

                if (StorageProvider != null)
                {
                    if (!StorageProvider.HasData(filePath)) return null;
                    json = StorageProvider.LoadData(filePath);
                }
                else
                {
                    if (!File.Exists(filePath)) return null;
                    json = File.ReadAllText(filePath);
                }

                return DeserializeFromJson(json);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to load narrative data from {filePath}: {ex.Message}");
                return null;
            }
        }
    }
}
