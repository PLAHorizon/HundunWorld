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
        /// <summary>单条更新的最大发送尝试次数；超出后丢弃（曾经会无限重播）。</summary>
        public const int MaxSendAttempts = 3;

        public TalesComponent TalesComponent { get; private set; }

        private bool? _isServerOverride;

        /// <summary>
        /// 本端是否为权威服务端（对应 UE5 ListenServer/DedicatedServer 角色）。
        /// 宿主可通过 setter 显式指定；未指定时按进程形态推断：headless 进程（专用服务器/sim 模式）
        /// 为服务端，普通图形客户端为 false。修复「IsServer 永远无人置 true → 服务端广播分支不可达」。
        /// </summary>
        public bool IsServer
        {
            get => _isServerOverride ?? FlaxEngine.Engine.IsHeadless;
            set => _isServerOverride = value;
        }

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

        /// <summary>
        /// 宿主显式声明的网络化模式（对应 UE5 World 的 NetMode）。
        /// 为 null 时由 <see cref="IsNetworked"/> 按当前角色与连接状态推断。
        /// </summary>
        public bool? NetworkedMode { get; set; }

        /// <summary>
        /// 是否处于网络化模式。对应 UE5 <c>GetNetMode() != NM_Standalone</c>：
        /// 单机时 TalesComponent 会抑制 RPC 与复制队列。
        /// 判定顺序：宿主显式声明 &gt; 服务端角色 &gt; 已接入且连上的网络适配器 &gt; 连接状态回调。
        /// </summary>
        public bool IsNetworked
        {
            get
            {
                if (NetworkedMode.HasValue) return NetworkedMode.Value;
                if (IsServer) return true;
                if (NetworkAdapter != null) return NetworkAdapter.IsConnected;
                return IsConnectedCallback != null && IsConnectedCallback();
            }
        }

        public NarrativeSyncManager(TalesComponent talesComponent)
        {
            TalesComponent = talesComponent;
        }

        /// <summary>
        /// 切换同步管理器服务的叙事组件（激活组件注册表变更时由插件调用）。
        /// 传入 null 表示当前没有激活组件：丢弃待发送队列，避免把更新发给已禁用的组件。
        /// </summary>
        public void AttachTalesComponent(TalesComponent talesComponent)
        {
            TalesComponent = talesComponent;
            if (talesComponent == null)
            {
                _pendingUpdates.Clear();
            }
        }

        public void SetNetworkAdapter(INarrativeNetworkAdapter adapter)
        {
            NetworkAdapter = adapter;
        }

        public void SendNarrativeUpdate(NarrativeUpdate update)
        {
            if (TalesComponent == null || update == null) return;

            _pendingUpdates.Add(update);

            // 立即尝试一次发送；失败留在队列由 FlushPendingUpdates 限次重试。
            DispatchUpdateAsync(update);
        }

        public void ProcessNarrativeUpdate(NarrativeUpdate update)
        {
            if (TalesComponent == null || update == null || update.bAcked) return;

            // 落地远端更新期间抑制上行：否则 EnterState/BeginQuest 这类入口会再发一条同类更新，
            // 两台权威端就能就同一次状态变更互发不止（UE5 靠复制方向天然不会，Flax 侧必须自己挡）。
            TalesComponent.SetApplyingRemoteUpdate(true);
            try
            {
                ApplyNarrativeUpdate(update);
            }
            finally
            {
                TalesComponent.SetApplyingRemoteUpdate(false);
            }
        }

        private void ApplyNarrativeUpdate(NarrativeUpdate update)
        {
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
                case EUpdateType.DialogueExit:
                    {
                        // 客户端的上行请求：在权威端重放退出（对应 UE5 ServerTryExitDialogue 的 RPC 体）
                        var reason = update.IntPayload != null && update.IntPayload.Count > 0
                            ? (EExitDialogueReason)update.IntPayload[0]
                            : EExitDialogueReason.PlayerExited;
                        TalesComponent.TryExitDialogue(reason);
                    }
                    break;
                case EUpdateType.DialogueSkip:
                    // 客户端的上行请求：在权威端跳过当前行（对应 UE5 ServerTrySkipCurrentDialogueLine 的 RPC 体）
                    TalesComponent.TrySkipCurrentDialogueLine();
                    break;
            }

            update.bAcked = true;

            // 收到的更新同时充作本地同内容待发的回执（服务器回声），
            // 让 FlushPendingUpdates 能把对应条目出队，而不是反复重发。
            AckEquivalentPendingUpdate(update);
        }

        /// <summary>把与收到的更新同类型、同任务、同载荷的本地待发条目标记为已确认。</summary>
        private void AckEquivalentPendingUpdate(NarrativeUpdate incoming)
        {
            for (int i = 0; i < _pendingUpdates.Count; i++)
            {
                var u = _pendingUpdates[i];
                if (u == null || u.bAcked || ReferenceEquals(u, incoming)) continue;
                if (u.UpdateType == incoming.UpdateType
                    && u.QuestClassId == incoming.QuestClassId
                    && u.Payload == incoming.Payload)
                {
                    u.bAcked = true;
                }
            }
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
            for (int i = 0; i < _pendingUpdates.Count; i++)
            {
                var update = _pendingUpdates[i];
                if (update == null || update.bAcked || update.SendAttempts >= MaxSendAttempts) continue;
                DispatchUpdateAsync(update);
            }

            // 出队：已确认的，以及重试次数耗尽的（修复：此前 bAcked 只在 ProcessNarrativeUpdate
            // 里赋值，发送结果从不回写，未确认条目会被无限重播）。
            _pendingUpdates.RemoveAll(u =>
            {
                if (u == null) return true;
                if (u.bAcked) return true;
                if (u.SendAttempts >= MaxSendAttempts)
                {
                    NarrativeLog.LogWarning($"叙事更新 {u.UpdateType}（任务 {u.QuestClassId}）发送失败 {u.SendAttempts} 次，已丢弃");
                    return true;
                }
                return false;
            });

            DrainTalesComponentPending();
        }

        /// <summary>
        /// 清理 TalesComponent 的待发送表：其条目与这里共享引用，
        /// 已确认或已放弃的都要移出，否则 _pendingUpdateList 只增不减（修复缺陷 4）。
        /// </summary>
        private void DrainTalesComponentPending()
        {
            var pending = TalesComponent?.PendingUpdateList;
            if (pending == null || pending.Count == 0) return;
            pending.RemoveAll(u => u == null || u.bAcked || u.SendAttempts >= MaxSendAttempts);
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

        /// <summary>
        /// 按角色分发一条更新：服务端走广播、客户端走上行。发送结果回写到更新本身
        /// （成功 → bAcked；失败 → SendAttempts++），FlushPendingUpdates 据此出队。
        /// </summary>
        private async void DispatchUpdateAsync(NarrativeUpdate update)
        {
            try
            {
                await DispatchUpdateInternalAsync(update);
            }
            catch (Exception ex)
            {
                update.SendAttempts++;
                NarrativeLog.LogError($"叙事更新发送异常（{update.UpdateType}）：{ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task DispatchUpdateInternalAsync(NarrativeUpdate update)
        {
            var adapter = NetworkAdapter;
            bool hasAdapter = adapter != null;
            bool canSend = hasAdapter
                ? adapter.IsConnected
                : SendCallback != null && (IsConnectedCallback?.Invoke() ?? false);

            if (!canSend)
            {
                // 无可用传输：计入重试次数，达到上限后由 FlushPendingUpdates 丢弃，
                // 不再像旧实现那样无限空转重播。
                update.SendAttempts++;
                NarrativeLog.LogWarning($"叙事更新 {update.UpdateType} 暂无可用网络传输，第 {update.SendAttempts} 次尝试计败");
                return;
            }

            string json = SerializeUpdate(update);
            bool bSent;
            if (hasAdapter)
            {
                bSent = await adapter.SendNarrativeMessageAsync(json, (int)update.UpdateType);
            }
            else
            {
                bSent = await SendCallback(json, (int)update.UpdateType);
            }

            if (bSent)
            {
                update.bAcked = true;
                NarrativeLog.Log(IsServer
                    ? $"广播叙事更新给客户端：{update.UpdateType}"
                    : $"上行叙事更新给服务器：{update.UpdateType}");
            }
            else
            {
                update.SendAttempts++;
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
