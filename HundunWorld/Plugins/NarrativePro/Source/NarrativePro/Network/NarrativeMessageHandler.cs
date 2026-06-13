using System;
using System.Collections.Generic;
using System.Text.Json;
using NarrativePro.Core;
using NarrativePro.Tales.Data;

namespace NarrativePro.Network
{
    public class NarrativeMessageHandler
    {
        public NarrativeSyncManager SyncManager { get; private set; }

        private readonly Dictionary<EUpdateType, Action<NarrativeUpdate>> _handlers = new Dictionary<EUpdateType, Action<NarrativeUpdate>>();

        public NarrativeMessageHandler(NarrativeSyncManager syncManager)
        {
            SyncManager = syncManager;
            RegisterDefaultHandlers();
        }

        private void RegisterDefaultHandlers()
        {
            _handlers[EUpdateType.BeginQuest] = HandleBeginQuest;
            _handlers[EUpdateType.ForgetQuest] = HandleForgetQuest;
            _handlers[EUpdateType.RestartQuest] = HandleRestartQuest;
            _handlers[EUpdateType.QuestNewState] = HandleQuestNewState;
            _handlers[EUpdateType.CompleteTask] = HandleCompleteTask;
            _handlers[EUpdateType.TaskProgressMade] = HandleTaskProgressMade;
        }

        public void HandleMessage(string jsonPayload)
        {
            if (string.IsNullOrEmpty(jsonPayload)) return;

            try
            {
                var update = JsonSerializer.Deserialize<NarrativeUpdate>(jsonPayload);
                if (update != null && _handlers.TryGetValue(update.UpdateType, out var handler))
                {
                    handler(update);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to handle narrative message: {ex.Message}");
            }
        }

        private void HandleBeginQuest(NarrativeUpdate update)
        {
            SyncManager.ProcessNarrativeUpdate(update);
        }

        private void HandleForgetQuest(NarrativeUpdate update)
        {
            SyncManager.ProcessNarrativeUpdate(update);
        }

        private void HandleRestartQuest(NarrativeUpdate update)
        {
            SyncManager.ProcessNarrativeUpdate(update);
        }

        private void HandleQuestNewState(NarrativeUpdate update)
        {
            SyncManager.ProcessNarrativeUpdate(update);
        }

        private void HandleCompleteTask(NarrativeUpdate update)
        {
            SyncManager.ProcessNarrativeUpdate(update);
        }

        private void HandleTaskProgressMade(NarrativeUpdate update)
        {
            SyncManager.ProcessNarrativeUpdate(update);
        }
    }
}
