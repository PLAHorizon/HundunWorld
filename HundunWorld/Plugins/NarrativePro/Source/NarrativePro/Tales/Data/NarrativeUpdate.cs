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
    }
}
