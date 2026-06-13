using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.Core;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Quest;

namespace NarrativePro.Data
{
    public static class NarrativeDataLoader
    {
        public static Quest LoadQuest(string questFilePath)
        {
            return QuestFactory.LoadQuest(questFilePath);
        }

        public static Dialogue LoadDialogue(string dialogueFilePath)
        {
            return DialogueFactory.LoadDialogue(dialogueFilePath);
        }

        public static NarrativeDataTask LoadDataTask(string taskFilePath)
        {
            try
            {
                string json = File.ReadAllText(taskFilePath);
                return JsonSerializer.Deserialize<NarrativeDataTask>(json);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to load data task from {taskFilePath}: {ex.Message}");
                return null;
            }
        }

        public static Dictionary<string, Quest> LoadAllQuests(string directoryPath)
        {
            return QuestFactory.LoadAllQuests(directoryPath);
        }

        public static Dictionary<string, Dialogue> LoadAllDialogues(string directoryPath)
        {
            return DialogueFactory.LoadAllDialogues(directoryPath);
        }

        public static Dictionary<string, NarrativeDataTask> LoadAllDataTasks(string directoryPath)
        {
            var tasks = new Dictionary<string, NarrativeDataTask>();
            if (!Directory.Exists(directoryPath)) return tasks;

            foreach (var file in Directory.GetFiles(directoryPath, "*.json"))
            {
                try
                {
                    var task = LoadDataTask(file);
                    if (task != null)
                    {
                        string key = Path.GetFileNameWithoutExtension(file);
                        tasks[key] = task;
                    }
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"Failed to load data task from {file}: {ex.Message}");
                }
            }
            return tasks;
        }

        public static NarrativeSettings LoadSettings(string settingsFilePath)
        {
            try
            {
                if (!File.Exists(settingsFilePath)) return new NarrativeSettings();
                string json = File.ReadAllText(settingsFilePath);
                return JsonSerializer.Deserialize<NarrativeSettings>(json) ?? new NarrativeSettings();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to load settings from {settingsFilePath}: {ex.Message}");
                return new NarrativeSettings();
            }
        }

        public static void SaveSettings(NarrativeSettings settings, string settingsFilePath)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                string directory = Path.GetDirectoryName(settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to save settings to {settingsFilePath}: {ex.Message}");
            }
        }
    }
}
