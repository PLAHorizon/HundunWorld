using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NarrativePro.Core;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Events;
using NarrativePro.Tales.Nodes;

namespace NarrativePro.Tales.Dialogue
{
    public static class DialogueFactory
    {
        /// <summary>
        /// 对话资产里既有 camelCase（"dialogueId"）也有与 C# 同名的 PascalCase 写法（对话行的 "Text"），
        /// 因此整份 JSON 用大小写不敏感绑定；这只会让此前静默丢失的行字段（text/duration/soundPath）真正生效。
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static Dialogue LoadDialogue(string jsonFilePath)
        {
            string json = File.ReadAllText(jsonFilePath);
            var dialogueData = JsonSerializer.Deserialize<DialogueData>(json, _jsonOptions);
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
                            IsPlayer = true,
                            DefaultSpeakerShotName = speakerData.DefaultShotName ?? "",
                            // UE5 里 FPlayerSpeakerInfo::SelectingReplyShot 是 Instanced 对象；
                            // 端口资产只写名字，播放时由 Dialogue.NPCFinishedTalking 经注册表解析
                            SelectingReplyShotName = speakerData.SelectingReplyShotName ?? ""
                        };
                    }
                    else
                    {
                        dialogue.Speakers.Add(new SpeakerInfo
                        {
                            SpeakerID = speakerData.SpeakerId,
                            DisplayName = speakerData.DisplayName,
                            Tags = speakerData.Tags ?? new List<string>(),
                            DefaultSpeakerShotName = speakerData.DefaultShotName ?? ""
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
                // 对话默认镜头按名解析（对应 UE5 资产上 DirectDialogueShot 的 Instanced 引用）。
                // 未命中时 Resolve 返回 null 并告警一次，:775/:1151 的回退语义与此前一致。
                dialogue.DefaultDialogueShot = NarrativePro.Cinematics.NarrativeDialogueShotRegistry
                    .Resolve(dialogueData.Config.DefaultShotName);
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
                        Line = BuildLine(replyData.Line),
                        bIsSkippable = replyData.IsSkippable,
                        SelectingReplyShotName = replyData.SelectingReplyShotName ?? "",
                        NodePosX = replyData.NodePosX,
                        NodePosY = replyData.NodePosY
                    };
                    ApplyAlternativeLines(node, replyData.AlternativeLines);
                    BuildLogic(node.Events, replyData.Events);
                    BuildLogic(node.Conditions, replyData.Conditions);
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
                        Line = BuildLine(replyData.Line),
                        OptionText = replyData.OptionText ?? "",
                        HintText = replyData.HintText ?? "",
                        bAutoSelect = replyData.AutoSelect,
                        bAutoSelectIfOnlyReply = replyData.AutoSelectIfOnlyReply,
                        CinematicOption = BuildCineOption(replyData.CinematicOption),
                        NodePosX = replyData.NodePosX,
                        NodePosY = replyData.NodePosY
                    };
                    ApplyAlternativeLines(node, replyData.AlternativeLines);
                    // 选项上的条件（UE5 内联 Instanced UNarrativeCondition）：
                    // DialogueNode.GetPlayerReplies 靠它过滤掉玩家拿不到/不该出现的选项。
                    BuildLogic(node.Events, replyData.Events);
                    BuildLogic(node.Conditions, replyData.Conditions);
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

        /// <summary>
        /// 把 JSON 行视图转成运行时 <see cref="DialogueLine"/>，并经由
        /// <see cref="NarrativeLogicFactory"/> 把 <c>conditions</c> 构造成真实条件实例。
        /// UE5 里 <c>FDialogueLine::Conditions</c>（DialogueSM.h）是 Instanced 对象数组，
        /// 端口必须在加载时按类型名构造，否则 <c>GetRandomLine</c> 的条件过滤永远拿到空列表。
        /// </summary>
        private static DialogueLine BuildLine(DialogueLineData data)
        {
            if (data == null) return new DialogueLine();

            var line = new DialogueLine
            {
                Text = data.Text ?? "",
                Duration = data.Duration,
                DurationSecondsOverride = data.DurationSecondsOverride,
                SoundPath = data.SoundPath ?? "",
                BodyAnimationName = data.BodyAnimationName ?? "",
                FacialAnimationName = data.FacialAnimationName ?? "",
                ShotName = data.ShotName ?? "",
                Shot = data.Shot,
                CutscenePath = data.CutscenePath ?? "",
                WaitForCutscene = data.WaitForCutscene
            };

            if (data.Conditions != null && data.Conditions.Count > 0)
            {
                line.Conditions = NarrativeLogicFactory.CreateConditions(data.Conditions);
            }

            return line;
        }

        /// <summary>构造备选行列表（对应 UE5 <c>UDialogueNode::AlternativeLines</c>）。</summary>
        private static void ApplyAlternativeLines(Nodes.DialogueNode node, List<DialogueLineData> lines)
        {
            if (node == null || lines == null) return;

            foreach (var data in lines)
            {
                if (data == null) continue;
                node.AlternativeLines.Add(BuildLine(data));
            }
        }

        /// <summary>构造节点事件（对应 UE5 <c>UNarrativeNodeBase::Events</c>）。</summary>
        private static void BuildLogic(List<NarrativeEvent> target, List<JsonElement> elements)
        {
            if (target == null || elements == null || elements.Count == 0) return;
            foreach (var evt in NarrativeLogicFactory.CreateEvents(elements))
            {
                target.Add(evt);
            }
        }

        /// <summary>构造节点条件（对应 UE5 <c>UNarrativeNodeBase::Conditions</c>）。</summary>
        private static void BuildLogic(List<NarrativeCondition> target, List<JsonElement> elements)
        {
            if (target == null || elements == null || elements.Count == 0) return;
            foreach (var cond in NarrativeLogicFactory.CreateConditions(elements))
            {
                target.Add(cond);
            }
        }

        /// <summary>
        /// 构造电影对话选项（对应 UE5 <c>FNarrativeCineDialogueLine</c>，NarrativeLevelSequenceActor.h:117）。
        /// 资产里 playerReplies[i].cinematicOption 缺失时为 null，行为与旧资产完全一致；
        /// 条件走 <see cref="NarrativeLogicFactory"/> 的按名构造注册表（与行/节点条件同一口径）。
        /// 播放设置用显式 DTO 属性映射而不是直接反序列化 <c>NarrativeSequencePlaybackSettings</c>：
        /// 该类成员是公有字段，System.Text.Json 默认不绑定字段（本工程已两次踩 camelCase/字段绑定坑）。
        /// </summary>
        private static NarrativePro.Cinematics.NarrativeCineDialogueLine BuildCineOption(CineDialogueLineData data)
        {
            if (data == null) return null;

            var line = new NarrativePro.Cinematics.NarrativeCineDialogueLine
            {
                OptionText = data.OptionText ?? "",
                HintText = data.HintText ?? "",
                DestinationSequencePath = data.DestinationSequencePath ?? "",
                DestinationMarker = data.DestinationMarker ?? "",
                bBlendAnimsBetweenSequences = data.BlendAnimsBetweenSequences,
                bKeepExistingSequenceSettings = data.KeepExistingSequenceSettings,
            };

            if (data.Conditions != null && data.Conditions.Count > 0)
            {
                line.Conditions = NarrativeLogicFactory.CreateConditions(data.Conditions);
            }

            if (data.DestinationPlaybackSettings != null)
            {
                var s = data.DestinationPlaybackSettings;
                line.DestinationPlaybackSettings = new NarrativePro.Cinematics.NarrativeSequencePlaybackSettings
                {
                    bWalkBindingsToStartTransform = s.WalkBindingsToStartTransform,
                    bUpdateControlRotationToPawn = s.UpdateControlRotationToPawn,
                    bShowCinematicBars = s.ShowCinematicBars,
                    bCanSkip = s.CanSkip,
                    bStopDialogue = s.StopDialogue,
                    bHideEvenEssentialHUDElements = s.HideEvenEssentialHUDElements,
                    bAutoPlay = s.AutoPlay,
                    DurationSeconds = s.DurationSeconds,
                    PlayRate = s.PlayRate,
                    BlendOutTime = s.BlendOutTime,
                };
            }

            return line;
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

        /// <summary>说话者默认镜头名（经 Cinematics/NarrativeDialogueShotRegistry 解析）。</summary>
        [JsonPropertyName("defaultShotName")] public string DefaultShotName { get; set; }

        /// <summary>玩家选择回复时播放的镜头名（仅 isPlayer 生效）。</summary>
        [JsonPropertyName("selectingReplyShotName")] public string SelectingReplyShotName { get; set; }
    }

    public class DialogueConfigData
    {
        [JsonPropertyName("endDialogueDist")] public float EndDialogueDist { get; set; }
        [JsonPropertyName("showCinematicBars")] public bool ShowCinematicBars { get; set; }
        [JsonPropertyName("unskippable")] public bool Unskippable { get; set; }
        [JsonPropertyName("freeMovement")] public bool FreeMovement { get; set; } = true;
        [JsonPropertyName("canBeExited")] public bool CanBeExited { get; set; } = true;
        [JsonPropertyName("priority")] public int Priority { get; set; }

        /// <summary>对话默认镜头名（对应 UE5 对话资产上 Instanced 的 DefaultDialogueShot 引用）。</summary>
        [JsonPropertyName("defaultShotName")] public string DefaultShotName { get; set; }
    }

    public class NpcReplyData
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("speakerId")] public string SpeakerId { get; set; }
        [JsonPropertyName("isRoot")] public bool IsRoot { get; set; }
        [JsonPropertyName("isSkippable")] public bool IsSkippable { get; set; } = true;
        [JsonPropertyName("line")] public DialogueLineData Line { get; set; }
        [JsonPropertyName("npcReplies")] public List<string> NpcReplies { get; set; }
        [JsonPropertyName("playerReplies")] public List<string> PlayerReplies { get; set; }

        /// <summary>随机备选行。对应 UE5 <c>AlternativeLines</c>。</summary>
        [JsonPropertyName("alternativeLines")] public List<DialogueLineData> AlternativeLines { get; set; }

        /// <summary>节点事件。对应 UE5 <c>UNarrativeNodeBase::Events</c>。</summary>
        [JsonPropertyName("events")] public List<JsonElement> Events { get; set; }

        /// <summary>节点条件。对应 UE5 <c>UNarrativeNodeBase::Conditions</c>。</summary>
        [JsonPropertyName("conditions")] public List<JsonElement> Conditions { get; set; }

        /// <summary>图上位置。对应 UE5 <c>NodePos</c>，回复链按它排序。</summary>
        [JsonPropertyName("nodePosX")] public float NodePosX { get; set; }
        [JsonPropertyName("nodePosY")] public float NodePosY { get; set; }

        /// <summary>玩家选中回复时播放的镜头名。对应 UE5 <c>SelectingReplyShot</c> 的标识。</summary>
        [JsonPropertyName("selectingReplyShotName")] public string SelectingReplyShotName { get; set; }
    }

    public class PlayerReplyData
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("optionText")] public string OptionText { get; set; }
        [JsonPropertyName("hintText")] public string HintText { get; set; }
        [JsonPropertyName("autoSelect")] public bool AutoSelect { get; set; }
        [JsonPropertyName("autoSelectIfOnlyReply")] public bool AutoSelectIfOnlyReply { get; set; } = true;
        [JsonPropertyName("line")] public DialogueLineData Line { get; set; }
        [JsonPropertyName("npcReplies")] public List<string> NpcReplies { get; set; }
        [JsonPropertyName("playerReplies")] public List<string> PlayerReplies { get; set; }

        [JsonPropertyName("alternativeLines")] public List<DialogueLineData> AlternativeLines { get; set; }
        [JsonPropertyName("events")] public List<JsonElement> Events { get; set; }
        [JsonPropertyName("conditions")] public List<JsonElement> Conditions { get; set; }
        [JsonPropertyName("nodePosX")] public float NodePosX { get; set; }
        [JsonPropertyName("nodePosY")] public float NodePosY { get; set; }

        /// <summary>
        /// 电影对话选项（可选，缺失为 null）。对应 UE5 <c>FNarrativeCineDialogueLine</c>。
        /// </summary>
        [JsonPropertyName("cinematicOption")] public CineDialogueLineData CinematicOption { get; set; }
    }

    /// <summary><c>FNarrativeCineDialogueLine</c> 的 JSON 视图（见 DialogueFactory.BuildCineOption）。</summary>
    public class CineDialogueLineData
    {
        [JsonPropertyName("conditions")] public List<JsonElement> Conditions { get; set; }
        [JsonPropertyName("optionText")] public string OptionText { get; set; }
        [JsonPropertyName("hintText")] public string HintText { get; set; }
        [JsonPropertyName("destinationSequencePath")] public string DestinationSequencePath { get; set; }
        [JsonPropertyName("destinationMarker")] public string DestinationMarker { get; set; }

        /// <summary>UE5 默认 true（构造函数 bBlendAnimsBetweenSequences = true）。</summary>
        [JsonPropertyName("blendAnimsBetweenSequences")] public bool BlendAnimsBetweenSequences { get; set; } = true;

        /// <summary>UE5 默认 true（bKeepExistingSequenceSettings = true）。</summary>
        [JsonPropertyName("keepExistingSequenceSettings")] public bool KeepExistingSequenceSettings { get; set; } = true;

        [JsonPropertyName("destinationPlaybackSettings")] public CinePlaybackSettingsData DestinationPlaybackSettings { get; set; }
    }

    /// <summary>
    /// 目的地播放设置的显式 JSON 视图：成员全为属性（带 setter），
    /// 避免 System.Text.Json 默认不绑定 <c>NarrativeSequencePlaybackSettings</c> 公有字段的静默丢失。
    /// <para>
    /// 三个时长字段（durationSeconds / playRate / blendOutTime）必须在这里显式建模：
    /// 播放侧 <c>NarrativeLevelSequenceActor.GetSequenceDurationSeconds</c>（:417-419）与
    /// <c>OnUpdate</c> 的计时推进（PlayRate）、<c>BlendOutAndStop</c>（BlendOutTime）都读它们，
    /// 少绑一个就会让资产写了也不生效——本工程的 camelCase/字段绑定已经踩过两次。
    /// </para>
    /// </summary>
    public class CinePlaybackSettingsData
    {
        [JsonPropertyName("walkBindingsToStartTransform")] public bool WalkBindingsToStartTransform { get; set; }
        [JsonPropertyName("updateControlRotationToPawn")] public bool UpdateControlRotationToPawn { get; set; }
        [JsonPropertyName("showCinematicBars")] public bool ShowCinematicBars { get; set; }
        [JsonPropertyName("canSkip")] public bool CanSkip { get; set; } = true;
        [JsonPropertyName("stopDialogue")] public bool StopDialogue { get; set; }
        [JsonPropertyName("hideEvenEssentialHUDElements")] public bool HideEvenEssentialHUDElements { get; set; }
        [JsonPropertyName("autoPlay")] public bool AutoPlay { get; set; } = true;

        /// <summary>序列时长（秒）。对应 <c>NarrativeSequencePlaybackSettings.DurationSeconds</c>（缺省 0 → 播放侧退落到 FallbackDurationSeconds）。</summary>
        [JsonPropertyName("durationSeconds")] public float DurationSeconds { get; set; } = 0f;

        /// <summary>播放速率。对应 <c>NarrativeSequencePlaybackSettings.PlayRate</c>（C# 默认 1）。</summary>
        [JsonPropertyName("playRate")] public float PlayRate { get; set; } = 1f;

        /// <summary>混合输出时长（秒）。对应 <c>NarrativeSequencePlaybackSettings.BlendOutTime</c>（C# 默认 0.35）。</summary>
        [JsonPropertyName("blendOutTime")] public float BlendOutTime { get; set; } = 0.35f;
    }

    /// <summary>
    /// 对话行的 JSON 视图。属性名与 <see cref="DialogueLine"/> 一一对应，
    /// 因反序列化开了大小写不敏感，camelCase（"text"）与 PascalCase（"Text"）两种资产写法都能绑定。
    /// 唯一区别是 <c>conditions</c> 保留为原始 JSON 元素：UE5 里它是 Instanced
    /// <c>UNarrativeCondition</c> 数组，<c>System.Text.Json</c> 无法实例化抽象基类，
    /// 必须交给 <see cref="NarrativeLogicFactory"/> 按类型名构造。
    /// </summary>
    public class DialogueLineData
    {
        public string Text { get; set; }
        public ELineDuration Duration { get; set; } = ELineDuration.Default;
        public float DurationSecondsOverride { get; set; }
        public string SoundPath { get; set; }
        public string BodyAnimationName { get; set; }
        public string FacialAnimationName { get; set; }
        public string ShotName { get; set; }
        public NarrativeDialogueSequence Shot { get; set; }

        /// <summary>本行播放的 CG/过场视频（工程内路径，如 Content/Movies/xxx.mp4）。空＝不播。</summary>
        [JsonPropertyName("cutscenePath")] public string CutscenePath { get; set; }

        /// <summary>true＝这一行等 CG 播完再继续（行内计时让步给视频）。</summary>
        [JsonPropertyName("waitForCutscene")] public bool WaitForCutscene { get; set; }
        public List<JsonElement> Conditions { get; set; }
    }
}
