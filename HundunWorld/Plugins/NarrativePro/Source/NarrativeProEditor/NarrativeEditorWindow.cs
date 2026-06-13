using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NarrativePro.Core;
using NarrativePro.Tales.Quest;
using NarrativePro.Tales.Dialogue;

namespace NarrativePro.Editor
{
    public class NarrativeEditorWindow
    {
        public enum EditorMode
        {
            Quest,
            Dialogue
        }

        public EditorMode CurrentMode { get; set; } = EditorMode.Quest;
        public string CurrentFilePath { get; private set; } = "";
        public bool bIsDirty { get; private set; } = false;
        public string StatusMessage { get; set; } = "";

        private QuestData _questData;
        private DialogueData _dialogueData;

        public QuestData QuestData => _questData;
        public DialogueData DialogueData => _dialogueData;

        public void NewQuest()
        {
            CurrentMode = EditorMode.Quest;
            _questData = new QuestData
            {
                QuestId = "NewQuest",
                QuestName = "新任务",
                QuestDescription = "",
                StartStateId = ""
            };
            _questData.States.Add(new QuestStateData
            {
                Id = "Start",
                Description = "开始",
                StateType = EStateNodeType.Regular
            });
            _questData.States.Add(new QuestStateData
            {
                Id = "Success",
                Description = "完成",
                StateType = EStateNodeType.Success
            });
            CurrentFilePath = "";
            bIsDirty = true;
            StatusMessage = "新建任务";
        }

        public void NewDialogue()
        {
            CurrentMode = EditorMode.Dialogue;
            _dialogueData = new DialogueData
            {
                DialogueId = "NewDialogue"
            };
            _dialogueData.Speakers = new List<SpeakerData>
            {
                new SpeakerData { SpeakerId = "NPC", DisplayName = "NPC", IsPlayer = false },
                new SpeakerData { SpeakerId = "Player", DisplayName = "玩家", IsPlayer = true }
            };
            _dialogueData.NpcReplies = new List<NpcReplyData>
            {
                new NpcReplyData { Id = "Root", SpeakerId = "NPC", IsRoot = true, IsSkippable = true, Line = new Tales.Data.DialogueLine { Text = "..." } }
            };
            _dialogueData.PlayerReplies = new List<PlayerReplyData>();
            CurrentFilePath = "";
            bIsDirty = true;
            StatusMessage = "新建对话";
        }

        public bool LoadQuest(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                _questData = JsonSerializer.Deserialize<QuestData>(json);
                if (_questData == null) return false;
                CurrentMode = EditorMode.Quest;
                CurrentFilePath = filePath;
                bIsDirty = false;
                StatusMessage = $"已加载任务: {Path.GetFileName(filePath)}";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
                return false;
            }
        }

        public bool LoadDialogue(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);
                _dialogueData = JsonSerializer.Deserialize<DialogueData>(json);
                if (_dialogueData == null) return false;
                CurrentMode = EditorMode.Dialogue;
                CurrentFilePath = filePath;
                bIsDirty = false;
                StatusMessage = $"已加载对话: {Path.GetFileName(filePath)}";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
                return false;
            }
        }

        public bool Save(string filePath = null)
        {
            filePath = filePath ?? CurrentFilePath;
            if (string.IsNullOrEmpty(filePath)) return false;

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json;

                if (CurrentMode == EditorMode.Quest && _questData != null)
                {
                    json = JsonSerializer.Serialize(_questData, options);
                }
                else if (CurrentMode == EditorMode.Dialogue && _dialogueData != null)
                {
                    json = JsonSerializer.Serialize(_dialogueData, options);
                }
                else
                {
                    return false;
                }

                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, json);
                CurrentFilePath = filePath;
                bIsDirty = false;
                StatusMessage = $"已保存: {Path.GetFileName(filePath)}";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
                return false;
            }
        }

        public void AddQuestState(string stateId, EStateNodeType stateType, string description)
        {
            if (_questData == null) return;
            _questData.States.Add(new QuestStateData
            {
                Id = stateId,
                StateType = stateType,
                Description = description
            });
            bIsDirty = true;
        }

        public void RemoveQuestState(string stateId)
        {
            if (_questData == null) return;
            _questData.States.RemoveAll(s => s.Id == stateId);
            if (_questData.StartStateId == stateId) _questData.StartStateId = "";
            bIsDirty = true;
        }

        public void AddQuestBranch(string branchId, string fromStateId, string toStateId, string description)
        {
            if (_questData == null) return;
            _questData.Branches.Add(new QuestBranchData
            {
                Id = branchId,
                FromStateId = fromStateId,
                ToStateId = toStateId,
                Description = description
            });
            bIsDirty = true;
        }

        public void RemoveQuestBranch(string branchId)
        {
            if (_questData == null) return;
            _questData.Branches.RemoveAll(b => b.Id == branchId);
            bIsDirty = true;
        }

        public void AddTaskToBranch(string branchId, string taskType, string targetId, int requiredQuantity, string description)
        {
            if (_questData == null) return;
            var branch = _questData.Branches.Find(b => b.Id == branchId);
            if (branch == null) return;
            branch.Tasks.Add(new TaskData
            {
                Type = taskType,
                TargetId = targetId,
                RequiredQuantity = requiredQuantity,
                Description = description
            });
            bIsDirty = true;
        }

        public void RemoveTaskFromBranch(string branchId, int taskIndex)
        {
            if (_questData == null) return;
            var branch = _questData.Branches.Find(b => b.Id == branchId);
            if (branch == null || taskIndex < 0 || taskIndex >= branch.Tasks.Count) return;
            branch.Tasks.RemoveAt(taskIndex);
            bIsDirty = true;
        }

        public void SetStartState(string stateId)
        {
            if (_questData == null) return;
            _questData.StartStateId = stateId;
            bIsDirty = true;
        }

        public void AddSpeaker(string speakerId, string displayName, bool isPlayer)
        {
            if (_dialogueData == null) return;
            if (_dialogueData.Speakers == null) _dialogueData.Speakers = new List<SpeakerData>();
            _dialogueData.Speakers.Add(new SpeakerData
            {
                SpeakerId = speakerId,
                DisplayName = displayName,
                IsPlayer = isPlayer
            });
            bIsDirty = true;
        }

        public void RemoveSpeaker(string speakerId)
        {
            if (_dialogueData?.Speakers == null) return;
            _dialogueData.Speakers.RemoveAll(s => s.SpeakerId == speakerId);
            bIsDirty = true;
        }

        public void AddNPCReply(string replyId, string speakerId, string text, bool isRoot)
        {
            if (_dialogueData?.NpcReplies == null) return;
            _dialogueData.NpcReplies.Add(new NpcReplyData
            {
                Id = replyId,
                SpeakerId = speakerId,
                IsRoot = isRoot,
                IsSkippable = true,
                Line = new Tales.Data.DialogueLine { Text = text }
            });
            bIsDirty = true;
        }

        public void RemoveNPCReply(string replyId)
        {
            if (_dialogueData?.NpcReplies == null) return;
            _dialogueData.NpcReplies.RemoveAll(r => r.Id == replyId);
            bIsDirty = true;
        }

        public void AddPlayerReply(string replyId, string optionText, string lineText)
        {
            if (_dialogueData?.PlayerReplies == null) return;
            _dialogueData.PlayerReplies.Add(new PlayerReplyData
            {
                Id = replyId,
                OptionText = optionText,
                AutoSelectIfOnlyReply = true,
                Line = new Tales.Data.DialogueLine { Text = lineText }
            });
            bIsDirty = true;
        }

        public void RemovePlayerReply(string replyId)
        {
            if (_dialogueData?.PlayerReplies == null) return;
            _dialogueData.PlayerReplies.RemoveAll(r => r.Id == replyId);
            bIsDirty = true;
        }

        public void LinkNPCReplyToNPC(string fromId, string toId)
        {
            if (_dialogueData?.NpcReplies == null) return;
            var reply = _dialogueData.NpcReplies.Find(r => r.Id == fromId);
            if (reply == null) return;
            if (reply.NpcReplies == null) reply.NpcReplies = new List<string>();
            if (!reply.NpcReplies.Contains(toId)) reply.NpcReplies.Add(toId);
            bIsDirty = true;
        }

        public void LinkNPCReplyToPlayer(string fromId, string toId)
        {
            if (_dialogueData?.NpcReplies == null) return;
            var reply = _dialogueData.NpcReplies.Find(r => r.Id == fromId);
            if (reply == null) return;
            if (reply.PlayerReplies == null) reply.PlayerReplies = new List<string>();
            if (!reply.PlayerReplies.Contains(toId)) reply.PlayerReplies.Add(toId);
            bIsDirty = true;
        }

        public void LinkPlayerReplyToNPC(string fromId, string toId)
        {
            if (_dialogueData?.PlayerReplies == null) return;
            var reply = _dialogueData.PlayerReplies.Find(r => r.Id == fromId);
            if (reply == null) return;
            if (reply.NpcReplies == null) reply.NpcReplies = new List<string>();
            if (!reply.NpcReplies.Contains(toId)) reply.NpcReplies.Add(toId);
            bIsDirty = true;
        }

        public void UnlinkNode(string fromId, string toId)
        {
            if (_dialogueData == null) return;
            foreach (var npc in _dialogueData.NpcReplies)
            {
                npc.NpcReplies?.Remove(toId);
                npc.PlayerReplies?.Remove(toId);
            }
            foreach (var player in _dialogueData.PlayerReplies)
            {
                player.NpcReplies?.Remove(toId);
                player.PlayerReplies?.Remove(toId);
            }
            bIsDirty = true;
        }

        public bool Validate(out string error)
        {
            error = "";
            if (CurrentMode == EditorMode.Quest && _questData != null)
            {
                if (string.IsNullOrEmpty(_questData.StartStateId))
                {
                    error = "未设置起始状态";
                    return false;
                }
                bool hasTerminal = false;
                foreach (var state in _questData.States)
                {
                    if (state.StateType == EStateNodeType.Success || state.StateType == EStateNodeType.Failure)
                    {
                        hasTerminal = true;
                        break;
                    }
                }
                if (!hasTerminal)
                {
                    error = "任务缺少成功或失败终止状态";
                    return false;
                }
                return true;
            }
            if (CurrentMode == EditorMode.Dialogue && _dialogueData != null)
            {
                bool hasRoot = false;
                foreach (var reply in _dialogueData.NpcReplies)
                {
                    if (reply.IsRoot) { hasRoot = true; break; }
                }
                if (!hasRoot)
                {
                    error = "对话缺少根节点";
                    return false;
                }
                return true;
            }
            error = "无数据";
            return false;
        }
    }
}
