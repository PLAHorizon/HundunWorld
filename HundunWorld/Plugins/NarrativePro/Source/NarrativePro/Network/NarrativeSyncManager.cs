using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using NarrativePro.Core;
using NarrativePro.Tales;
using NarrativePro.Tales.Data;

namespace NarrativePro.Network
{
    public interface INarrativeNetworkAdapter
    {
        Task<bool> SendNarrativeMessageAsync(string jsonPayload, int updateType);
        bool IsConnected { get; }
    }

    public class NarrativeSyncManager
    {
        public TalesComponent TalesComponent { get; private set; }
        public bool IsServer { get; set; } = false;
        public float SyncInterval { get; set; } = 0.5f;
        public INarrativeNetworkAdapter NetworkAdapter { get; private set; }

        private float _syncTimer = 0f;
        private List<NarrativeUpdate> _pendingUpdates = new List<NarrativeUpdate>();

        /// <summary>
        /// 网络发送回调（跨模块桥接用，替代直接依赖 INarrativeNetworkAdapter）
        /// </summary>
        public Func<string, int, System.Threading.Tasks.Task<bool>> SendCallback { get; set; }

        /// <summary>
        /// 网络连接状态检查回调
        /// </summary>
        public Func<bool> IsConnectedCallback { get; set; }

        public NarrativeSyncManager(TalesComponent talesComponent)
        {
            TalesComponent = talesComponent;
        }

        public void SetNetworkAdapter(INarrativeNetworkAdapter adapter)
        {
            NetworkAdapter = adapter;
        }

        public void SendNarrativeUpdate(NarrativeUpdate update)
        {
            if (TalesComponent == null) return;

            _pendingUpdates.Add(update);

            if (IsServer)
            {
                BroadcastUpdateToClients(update);
            }
            else
            {
                SendUpdateToServer(update);
            }
        }

        public void ProcessNarrativeUpdate(NarrativeUpdate update)
        {
            if (TalesComponent == null || update.bAcked) return;

            switch (update.UpdateType)
            {
                case EUpdateType.BeginQuest:
                    TalesComponent.BeginQuest(update.QuestClassId, update.Payload);
                    break;
                case EUpdateType.ForgetQuest:
                    TalesComponent.ForgetQuest(update.QuestClassId);
                    break;
                case EUpdateType.RestartQuest:
                    TalesComponent.RestartQuest(update.QuestClassId, update.Payload);
                    break;
                case EUpdateType.QuestNewState:
                    var quest = TalesComponent.GetQuestInstance(update.QuestClassId);
                    if (quest != null)
                    {
                        var state = quest.GetState(update.Payload);
                        if (state != null) quest.EnterState(state);
                    }
                    break;
                case EUpdateType.CompleteTask:
                    if (update.IntPayload != null && update.IntPayload.Count > 0)
                    {
                        TalesComponent.CompleteNarrativeDataTask(update.Payload, "", update.IntPayload[0]);
                    }
                    break;
                case EUpdateType.TaskProgressMade:
                    quest = TalesComponent.GetQuestInstance(update.QuestClassId);
                    if (quest != null && update.IntPayload != null && update.IntPayload.Count >= 2)
                    {
                        var branch = quest.GetBranch(update.Payload);
                        if (branch != null && update.IntPayload[0] < branch.QuestTasks.Count)
                        {
                            branch.QuestTasks[update.IntPayload[0]].SetProgress(update.IntPayload[1]);
                        }
                    }
                    break;
            }

            update.bAcked = true;
        }

        public void TickSync(float deltaTime)
        {
            _syncTimer += deltaTime;
            if (_syncTimer >= SyncInterval)
            {
                _syncTimer = 0f;
                FlushPendingUpdates();
            }
        }

        public void FlushPendingUpdates()
        {
            foreach (var update in _pendingUpdates)
            {
                if (!update.bAcked)
                {
                    if (IsServer)
                    {
                        BroadcastUpdateToClients(update);
                    }
                    else
                    {
                        SendUpdateToServer(update);
                    }
                }
            }
            _pendingUpdates.RemoveAll(u => u.bAcked);
        }

        public string SerializeUpdate(NarrativeUpdate update)
        {
            return JsonSerializer.Serialize(update);
        }

        public NarrativeUpdate DeserializeUpdate(string json)
        {
            return JsonSerializer.Deserialize<NarrativeUpdate>(json);
        }

        public List<NarrativeUpdate> GetPendingUpdates()
        {
            return new List<NarrativeUpdate>(_pendingUpdates);
        }

        public void ClearPendingUpdates()
        {
            _pendingUpdates.Clear();
        }

        private async void BroadcastUpdateToClients(NarrativeUpdate update)
        {
            var canSend = NetworkAdapter?.IsConnected ?? (IsConnectedCallback?.Invoke() ?? false);
            if (!canSend)
            {
                NarrativeLog.Log($"Broadcasting narrative update: {update.UpdateType} (no network adapter)");
                return;
            }

            try
            {
                var json = SerializeUpdate(update);
                if (NetworkAdapter != null)
                    await NetworkAdapter.SendNarrativeMessageAsync(json, (int)update.UpdateType);
                else if (SendCallback != null)
                    await SendCallback(json, (int)update.UpdateType);
                NarrativeLog.Log($"Broadcasting narrative update to clients: {update.UpdateType}");
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to broadcast narrative update: {ex.Message}");
            }
        }

        private async void SendUpdateToServer(NarrativeUpdate update)
        {
            var canSend = NetworkAdapter?.IsConnected ?? (IsConnectedCallback?.Invoke() ?? false);
            if (!canSend)
            {
                NarrativeLog.Log($"Sending narrative update to server: {update.UpdateType} (no network adapter)");
                return;
            }

            try
            {
                var json = SerializeUpdate(update);
                if (NetworkAdapter != null)
                    await NetworkAdapter.SendNarrativeMessageAsync(json, (int)update.UpdateType);
                else if (SendCallback != null)
                    await SendCallback(json, (int)update.UpdateType);
                NarrativeLog.Log($"Sending narrative update to server: {update.UpdateType}");
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to send narrative update: {ex.Message}");
            }
        }

        public void OnNarrativeMessageReceived(string jsonPayload)
        {
            try
            {
                var update = DeserializeUpdate(jsonPayload);
                if (update != null)
                {
                    ProcessNarrativeUpdate(update);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to process narrative message: {ex.Message}");
            }
        }
    }
}
