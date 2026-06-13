using System;
using System.IO;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Editor
{
    public static class NarrativeEditorCommands
    {
        private static NarrativeEditorWindow _editorWindow;
        private static NarrativeEditorUI _editorUI;

        public static void Initialize(NarrativeEditorUI editorUI)
        {
            _editorUI = editorUI;
            _editorWindow = editorUI.Editor;
            NarrativeLog.Log("叙事编辑器命令已初始化");
        }

        public static void ExecuteCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return;

            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string cmd = parts[0].ToLower();

            switch (cmd)
            {
                case "new_quest":
                    _editorWindow.NewQuest();
                    NarrativeLog.Log("新建任务");
                    break;
                case "new_dialogue":
                    _editorWindow.NewDialogue();
                    NarrativeLog.Log("新建对话");
                    break;
                case "load_quest":
                    if (parts.Length > 1) _editorWindow.LoadQuest(parts[1]);
                    else NarrativeLog.LogWarning("用法: load_quest <文件路径>");
                    break;
                case "load_dialogue":
                    if (parts.Length > 1) _editorWindow.LoadDialogue(parts[1]);
                    else NarrativeLog.LogWarning("用法: load_dialogue <文件路径>");
                    break;
                case "save":
                    if (parts.Length > 1) _editorWindow.Save(parts[1]);
                    else _editorWindow.Save();
                    break;
                case "validate":
                    string error;
                    bool valid = _editorWindow.Validate(out error);
                    NarrativeLog.Log(valid ? "验证通过" : $"验证失败: {error}");
                    break;
                case "add_state":
                    if (parts.Length >= 4)
                    {
                        EStateNodeType type = (EStateNodeType)Enum.Parse(typeof(EStateNodeType), parts[2]);
                        _editorWindow.AddQuestState(parts[1], type, parts[3]);
                        NarrativeLog.Log($"添加状态: {parts[1]}");
                    }
                    else NarrativeLog.LogWarning("用法: add_state <id> <type:0/1/2> <description>");
                    break;
                case "add_branch":
                    if (parts.Length >= 5)
                    {
                        _editorWindow.AddQuestBranch(parts[1], parts[2], parts[3], parts[4]);
                        NarrativeLog.Log($"添加分支: {parts[1]}");
                    }
                    else NarrativeLog.LogWarning("用法: add_branch <id> <fromState> <toState> <description>");
                    break;
                case "add_npc_reply":
                    if (parts.Length >= 4)
                    {
                        bool isRoot = parts.Length > 4 && parts[4].ToLower() == "root";
                        _editorWindow.AddNPCReply(parts[1], parts[2], parts[3], isRoot);
                        NarrativeLog.Log($"添加NPC回复: {parts[1]}");
                    }
                    else NarrativeLog.LogWarning("用法: add_npc_reply <id> <speakerId> <text> [root]");
                    break;
                case "add_player_reply":
                    if (parts.Length >= 4)
                    {
                        _editorWindow.AddPlayerReply(parts[1], parts[2], parts[3]);
                        NarrativeLog.Log($"添加玩家回复: {parts[1]}");
                    }
                    else NarrativeLog.LogWarning("用法: add_player_reply <id> <optionText> <lineText>");
                    break;
                case "link_npc_to_npc":
                    if (parts.Length >= 3)
                    {
                        _editorWindow.LinkNPCReplyToNPC(parts[1], parts[2]);
                        NarrativeLog.Log($"链接 NPC->NPC: {parts[1]} -> {parts[2]}");
                    }
                    else NarrativeLog.LogWarning("用法: link_npc_to_npc <fromId> <toId>");
                    break;
                case "link_npc_to_player":
                    if (parts.Length >= 3)
                    {
                        _editorWindow.LinkNPCReplyToPlayer(parts[1], parts[2]);
                        NarrativeLog.Log($"链接 NPC->Player: {parts[1]} -> {parts[2]}");
                    }
                    else NarrativeLog.LogWarning("用法: link_npc_to_player <fromId> <toId>");
                    break;
                case "link_player_to_npc":
                    if (parts.Length >= 3)
                    {
                        _editorWindow.LinkPlayerReplyToNPC(parts[1], parts[2]);
                        NarrativeLog.Log($"链接 Player->NPC: {parts[1]} -> {parts[2]}");
                    }
                    else NarrativeLog.LogWarning("用法: link_player_to_npc <fromId> <toId>");
                    break;
                case "help":
                    NarrativeLog.Log("可用命令: new_quest, new_dialogue, load_quest, load_dialogue, save, validate, add_state, add_branch, add_npc_reply, add_player_reply, link_npc_to_npc, link_npc_to_player, link_player_to_npc, help");
                    break;
                default:
                    NarrativeLog.LogWarning($"未知命令: {cmd}。输入 help 查看可用命令。");
                    break;
            }
        }
    }
}
