using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using NarrativePro.Core;

namespace NarrativePro.Tales.Data
{
    [Serializable]
    public partial class NarrativeUpdate
    {
        [JsonPropertyName("updateType")]
        public EUpdateType UpdateType { get; set; } = EUpdateType.None;

        [JsonPropertyName("questClassId")]
        public string QuestClassId { get; set; } = "";

        [JsonPropertyName("payload")]
        public string Payload { get; set; } = "";

        [JsonPropertyName("intPayload")]
        public List<byte> IntPayload { get; set; } = new List<byte>();

        [JsonPropertyName("bAcked")]
        public bool bAcked { get; set; } = false;

        /// <summary>
        /// 传输层发送失败的重试次数。超过 NarrativeSyncManager.MaxSendAttempts 后
        /// FlushPendingUpdates 会把它丢弃（而不是永远重播），并在 TalesComponent 的待发送表中被清空。
        /// </summary>
        [JsonPropertyName("sendAttempts")]
        public int SendAttempts { get; set; } = 0;

        [JsonPropertyName("creationTime")]
        public float CreationTime { get; set; } = 0f;

        public static NarrativeUpdate QuestNewState(string questClassId, string newStateId)
        {
            return new NarrativeUpdate
            {
                UpdateType = EUpdateType.QuestNewState,
                QuestClassId = questClassId,
                Payload = newStateId
            };
        }

        public static NarrativeUpdate CompleteTask(string questClassId, string rawTask, int quantity)
        {
            var update = new NarrativeUpdate
            {
                UpdateType = EUpdateType.CompleteTask,
                QuestClassId = questClassId,
                Payload = rawTask
            };
            update.IntPayload.Add((byte)quantity);
            return update;
        }

        public static NarrativeUpdate BeginQuest(string questClassId, string startFromId = "")
        {
            return new NarrativeUpdate
            {
                UpdateType = EUpdateType.BeginQuest,
                QuestClassId = questClassId,
                Payload = startFromId
            };
        }

        public static NarrativeUpdate RestartQuest(string questClassId, string startFromId = "")
        {
            return new NarrativeUpdate
            {
                UpdateType = EUpdateType.RestartQuest,
                QuestClassId = questClassId,
                Payload = startFromId
            };
        }

        public static NarrativeUpdate ForgetQuest(string questClassId)
        {
            return new NarrativeUpdate
            {
                UpdateType = EUpdateType.ForgetQuest,
                QuestClassId = questClassId
            };
        }

        public static NarrativeUpdate TaskProgressMade(string questClassId, byte updatedTaskIdx, byte newProgress, string branchId)
        {
            var update = new NarrativeUpdate
            {
                UpdateType = EUpdateType.TaskProgressMade,
                QuestClassId = questClassId,
                Payload = branchId
            };
            update.IntPayload.Add(updatedTaskIdx);
            update.IntPayload.Add(newProgress);
            return update;
        }

        /// <summary>
        /// 退出对话的复制更新。对应 UE5 <c>ServerTryExitDialogue</c>（TalesComponent.cpp）：
        /// 非权威端把退出原因交给权威端，由权威端真正结束对话并复制状态。
        /// </summary>
        /// <param name="dialogueId">对话标识（端口暂无对话实例 ID，可传空串）。对应 UE5 的 Payload。</param>
        /// <param name="reason">退出原因，取 <see cref="EExitDialogueReason"/> 的整数值，存入 IntPayload[0]。</param>
        public static NarrativeUpdate DialogueExit(string dialogueId, int reason)
        {
            var update = new NarrativeUpdate
            {
                UpdateType = EUpdateType.DialogueExit,
                QuestClassId = "",
                Payload = dialogueId ?? ""
            };
            update.IntPayload.Add((byte)(reason & 0xFF));
            return update;
        }

        /// <summary>
        /// 跳过当前对话行的复制更新。对应 UE5 <c>ServerTrySkipCurrentDialogueLine</c>（TalesComponent.cpp:625-629）。
        /// </summary>
        /// <param name="dialogueId">对话标识（端口暂无对话实例 ID，可传空串）。</param>
        public static NarrativeUpdate DialogueSkip(string dialogueId)
        {
            return new NarrativeUpdate
            {
                UpdateType = EUpdateType.DialogueSkip,
                QuestClassId = "",
                Payload = dialogueId ?? ""
            };
        }
    }
}
