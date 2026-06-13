using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;

namespace NarrativePro.Tales.Dialogue
{
    public static class DialogueFactory
    {
        public static Dialogue LoadDialogue(string jsonFilePath)
        {
            string json = File.ReadAllText(jsonFilePath);
            var dialogueData = JsonSerializer.Deserialize<DialogueData>(json);
            if (dialogueData == null) throw new Exception($"Failed to deserialize dialogue from {jsonFilePath}");

            var dialogue = new Dialogue();
            dialogue.DialogueId = dialogueData.DialogueId;

            if (dialogueData.Speakers != null)
            {
                foreach (var speakerData in dialogueData.Speakers)
                {
                    if (speakerData.IsPlayer)
                    {
                        dialogue.PlayerSpeakerInfo = new PlayerSpeakerInfo
                        {
                            SpeakerID = speakerData.SpeakerId,
                            DisplayName = speakerData.DisplayName,
                            Tags = speakerData.Tags ?? new List<string>(),
                            IsPlayer = true
                        };
                    }
                    else
                    {
                        dialogue.Speakers.Add(new SpeakerInfo
                        {
                            SpeakerID = speakerData.SpeakerId,
                            DisplayName = speakerData.DisplayName,
                            Tags = speakerData.Tags ?? new List<string>()
                        });
                    }
                }
            }

            if (dialogueData.Config != null)
            {
                dialogue.EndDialogueDist = dialogueData.Config.EndDialogueDist;
                dialogue.bShowCinematicBars = dialogueData.Config.ShowCinematicBars;
                dialogue.bUnskippable = dialogueData.Config.Unskippable;
                dialogue.bFreeMovement = dialogueData.Config.FreeMovement;
                dialogue.bCanBeExited = dialogueData.Config.CanBeExited;
                dialogue.Priority = dialogueData.Config.Priority;
            }

            var npcNodeMap = new Dictionary<string, DialogueNode_NPC>();
            if (dialogueData.NpcReplies != null)
            {
                foreach (var replyData in dialogueData.NpcReplies)
                {
                    var node = new DialogueNode_NPC
                    {
                        ID = replyData.Id,
                        SpeakerID = replyData.SpeakerId,
                        Line = replyData.Line ?? new DialogueLine(),
                        bIsSkippable = replyData.IsSkippable
                    };
                    npcNodeMap[replyData.Id] = node;
                    dialogue.NPCReplies.Add(node);

                    if (replyData.IsRoot)
                    {
                        dialogue.RootDialogue = node;
                    }
                }
            }

            var playerNodeMap = new Dictionary<string, DialogueNode_Player>();
            if (dialogueData.PlayerReplies != null)
            {
                foreach (var replyData in dialogueData.PlayerReplies)
                {
                    var node = new DialogueNode_Player
                    {
                        ID = replyData.Id,
                        Line = replyData.Line ?? new DialogueLine(),
                        OptionText = replyData.OptionText ?? "",
                        HintText = replyData.HintText ?? "",
                        bAutoSelect = replyData.AutoSelect,
                        bAutoSelectIfOnlyReply = replyData.AutoSelectIfOnlyReply
                    };
                    playerNodeMap[replyData.Id] = node;
                    dialogue.PlayerReplies.Add(node);
                }
            }

            if (dialogueData.NpcReplies != null)
            {
                foreach (var replyData in dialogueData.NpcReplies)
                {
                    if (!npcNodeMap.TryGetValue(replyData.Id, out var node)) continue;

                    if (replyData.NpcReplies != null)
                    {
                        foreach (var refId in replyData.NpcReplies)
                        {
                            if (npcNodeMap.TryGetValue(refId, out var refNode))
                                node.NPCReplies.Add(refNode);
                        }
                    }
                    if (replyData.PlayerReplies != null)
                    {
                        foreach (var refId in replyData.PlayerReplies)
                        {
                            if (playerNodeMap.TryGetValue(refId, out var refNode))
                                node.PlayerReplies.Add(refNode);
                        }
                    }
                }
            }

            if (dialogueData.PlayerReplies != null)
            {
                foreach (var replyData in dialogueData.PlayerReplies)
                {
                    if (!playerNodeMap.TryGetValue(replyData.Id, out var node)) continue;

                    if (replyData.NpcReplies != null)
                    {
                        foreach (var refId in replyData.NpcReplies)
                        {
                            if (npcNodeMap.TryGetValue(refId, out var refNode))
                                node.NPCReplies.Add(refNode);
                        }
                    }
                    if (replyData.PlayerReplies != null)
                    {
                        foreach (var refId in replyData.PlayerReplies)
                        {
                            if (playerNodeMap.TryGetValue(refId, out var refNode))
                                node.PlayerReplies.Add(refNode);
                        }
                    }
                }
            }

            string error;
            if (!ValidateDialogue(dialogue, out error))
            {
                throw new Exception($"Dialogue validation failed for {jsonFilePath}: {error}");
            }

            return dialogue;
        }

        public static bool ValidateDialogue(Dialogue dialogue, out string error)
        {
            error = "";
            if (dialogue.RootDialogue == null)
            {
                error = "Dialogue has no root NPC reply node";
                return false;
            }
            return true;
        }

        public static Dictionary<string, Dialogue> LoadAllDialogues(string directoryPath)
        {
            var dialogues = new Dictionary<string, Dialogue>();
            if (!Directory.Exists(directoryPath)) return dialogues;

            foreach (var file in Directory.GetFiles(directoryPath, "*.json"))
            {
                try
                {
                    var dialogue = LoadDialogue(file);
                    string key = Path.GetFileNameWithoutExtension(file);
                    dialogues[key] = dialogue;
                }
                catch (Exception ex)
                {
                    NarrativeLog.LogError($"Failed to load dialogue from {file}: {ex.Message}");
                }
            }
            return dialogues;
        }
    }

    public class DialogueData
    {
        [JsonPropertyName("dialogueId")] public string DialogueId { get; set; }
        [JsonPropertyName("speakers")] public List<SpeakerData> Speakers { get; set; }
        [JsonPropertyName("config")] public DialogueConfigData Config { get; set; }
        [JsonPropertyName("npcReplies")] public List<NpcReplyData> NpcReplies { get; set; }
        [JsonPropertyName("playerReplies")] public List<PlayerReplyData> PlayerReplies { get; set; }
    }

    public class SpeakerData
    {
        [JsonPropertyName("speakerId")] public string SpeakerId { get; set; }
        [JsonPropertyName("displayName")] public string DisplayName { get; set; }
        [JsonPropertyName("tags")] public List<string> Tags { get; set; }
        [JsonPropertyName("isPlayer")] public bool IsPlayer { get; set; }
    }

    public class DialogueConfigData
    {
        [JsonPropertyName("endDialogueDist")] public float EndDialogueDist { get; set; }
        [JsonPropertyName("showCinematicBars")] public bool ShowCinematicBars { get; set; }
        [JsonPropertyName("unskippable")] public bool Unskippable { get; set; }
        [JsonPropertyName("freeMovement")] public bool FreeMovement { get; set; } = true;
        [JsonPropertyName("canBeExited")] public bool CanBeExited { get; set; } = true;
        [JsonPropertyName("priority")] public int Priority { get; set; }
    }

    public class NpcReplyData
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("speakerId")] public string SpeakerId { get; set; }
        [JsonPropertyName("isRoot")] public bool IsRoot { get; set; }
        [JsonPropertyName("isSkippable")] public bool IsSkippable { get; set; } = true;
        [JsonPropertyName("line")] public DialogueLine Line { get; set; }
        [JsonPropertyName("npcReplies")] public List<string> NpcReplies { get; set; }
        [JsonPropertyName("playerReplies")] public List<string> PlayerReplies { get; set; }
    }

    public class PlayerReplyData
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("optionText")] public string OptionText { get; set; }
        [JsonPropertyName("hintText")] public string HintText { get; set; }
        [JsonPropertyName("autoSelect")] public bool AutoSelect { get; set; }
        [JsonPropertyName("autoSelectIfOnlyReply")] public bool AutoSelectIfOnlyReply { get; set; } = true;
        [JsonPropertyName("line")] public DialogueLine Line { get; set; }
        [JsonPropertyName("npcReplies")] public List<string> NpcReplies { get; set; }
        [JsonPropertyName("playerReplies")] public List<string> PlayerReplies { get; set; }
    }
}
