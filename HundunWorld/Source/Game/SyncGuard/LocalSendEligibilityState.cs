using System;
using System.Collections.Generic;
using System.Threading;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;

namespace HundunWorld.Game.SyncGuard;

/// <summary>
/// 本地角色发送资格状态机：维护"连接就绪 + 握手完成 + 身份确立"的资格相位，
/// 并联动派生深度绑定实体的发送资格。
/// <para>
/// 状态相位：Disconnected → ConnectedHandshakePending → Established → EligibilityLost。
/// 仅 Established 具备发送资格；本地资格丧失时批量撤销全部有效绑定实体资格，
/// 恢复时仅恢复仍持有有效绑定关系的实体。
/// </para>
/// </summary>
public sealed class LocalSendEligibilityState : ILocalSendEligibilityState
{
    private LocalEligibilityPhase _phase = LocalEligibilityPhase.Disconnected;
    private readonly object _lock = new();

    /// <summary>绑定关系注册表（查询有效绑定实体列表，供资格联动）。</summary>
    private readonly IBindingRelationshipRegistry _bindingRegistry;

    /// <summary>当前已恢复资格的绑定实体集合（资格联动后的派生表）。</summary>
    private readonly HashSet<ulong> _eligibleBoundEntities = new();

    /// <summary>绑定关系失效联动回调（来自注册表）。</summary>
    private Action<ulong, BindingInvalidateReason>? _onInvalidated;

    /// <inheritdoc />
    public event Action<LocalEligibilitySnapshot>? StateChanged;

    /// <summary>本地角色当前是否具备发送资格。</summary>
    public bool IsLocalEligible => _phase == LocalEligibilityPhase.Established;

    /// <summary>当前资格相位（供观测）。</summary>
    public LocalEligibilityPhase Phase => _phase;

    /// <summary>当前处于发送资格状态的绑定实体数量（供观测）。</summary>
    public int EligibleBoundEntityCount
    {
        get
        {
            lock (_lock)
            {
                return _eligibleBoundEntities.Count;
            }
        }
    }

    /// <summary>
    /// 初始化资格状态机。
    /// </summary>
    /// <param name="bindingRegistry">绑定关系注册表（查询有效绑定实体，供资格联动批量撤销/恢复）。</param>
    public LocalSendEligibilityState(IBindingRelationshipRegistry bindingRegistry)
    {
        _bindingRegistry = bindingRegistry ?? throw new ArgumentNullException(nameof(bindingRegistry));
        _onInvalidated = OnBindingInvalidated;
        _bindingRegistry.SetInvalidationCallback(_onInvalidated);
    }

    /// <inheritdoc />
    public bool IsBoundEntityEligible(ulong boundEntityId)
    {
        // 联动迟滞防护（spec 5.2.3 异常 3）：即便联动事件尚未处理，
        // 仍按"绑定有效 + 本地资格具备"双条件独立校验，不得因联动未完成而放行或误拒绝。
        if (!IsLocalEligible)
        {
            return false;
        }

        return _bindingRegistry.TryGetValidBinding(boundEntityId, out _);
    }

    /// <summary>
    /// 通知连接状态变化（由 NetworkManager 连接事件驱动）。
    /// </summary>
    public void OnConnectionChanged(bool isConnected)
    {
        if (!isConnected)
        {
            Transition(LocalEligibilityPhase.Disconnected, "连接断开");
        }
        else
        {
            Transition(LocalEligibilityPhase.ConnectedHandshakePending, "连接建立");
        }
    }

    /// <summary>
    /// 通知同步握手状态变化（由 NetworkManager 握手事件驱动）。
    /// </summary>
    public void OnHandshakeChanged(bool isComplete)
    {
        if (isComplete)
        {
            if (_phase == LocalEligibilityPhase.ConnectedHandshakePending)
            {
                Transition(LocalEligibilityPhase.Established, "握手完成");
            }
            // 修复（静置断线后输入断流 — "无法移动"根因之一）：
            // 断线时握手重置进入 EligibilityLost；重连后握手再次完成（含 resume 握手确认）时
            // 必须允许恢复到 Established。原实现仅接受 ConnectedHandshakePending → Established，
            // 导致重连成功且握手完成后资格永久停留在 EligibilityLost，
            // GuardSyncSender.Authorize 静默拒绝所有 InputPacket（移动请求不上行）。
            // 进入 Established 时 Transition 会重建 _eligibleBoundEntities（仅恢复有效绑定），
            // 从 EligibilityLost 恢复安全。
            else if (_phase == LocalEligibilityPhase.EligibilityLost)
            {
                Transition(LocalEligibilityPhase.Established, "重连握手完成（资格恢复）");
            }
        }
        else
        {
            // 握手重置（重连场景）→ 资格丧失
            if (_phase == LocalEligibilityPhase.Established)
            {
                Transition(LocalEligibilityPhase.EligibilityLost, "握手重置");
            }
        }
    }

    /// <summary>
    /// 通知本地身份确立/丢失（由 SnapshotApplySystem 身份事件驱动）。
    /// </summary>
    /// <param name="isEstablished">本地玩家身份是否已确立（LocalPlayerOwnerId != 0 且实体已设置）。</param>
    public void OnLocalIdentityChanged(bool isEstablished)
    {
        if (isEstablished)
        {
            if (_phase == LocalEligibilityPhase.ConnectedHandshakePending)
            {
                Transition(LocalEligibilityPhase.Established, "本地身份确立");
            }
            // 修复：同上 — 允许从 EligibilityLost 恢复（重连后身份重新确立）。
            else if (_phase == LocalEligibilityPhase.EligibilityLost)
            {
                Transition(LocalEligibilityPhase.Established, "重连身份确立（资格恢复）");
            }
        }
        else
        {
            if (_phase == LocalEligibilityPhase.Established)
            {
                Transition(LocalEligibilityPhase.EligibilityLost, "本地身份丢失");
            }
        }
    }

    /// <summary>绑定关系失效联动（来自注册表 InvalidateBinding）。</summary>
    private void OnBindingInvalidated(ulong boundEntityId, BindingInvalidateReason reason)
    {
        lock (_lock)
        {
            _eligibleBoundEntities.Remove(boundEntityId);
        }

        System.Diagnostics.Debug.WriteLine($"[LocalSendEligibilityState] 绑定实体资格撤销: Bound={boundEntityId}, Reason={reason}");
    }

    /// <summary>
    /// 重连恢复完成收敛：在既有资格相位为 <see cref="LocalEligibilityPhase.ConnectedHandshakePending"/> /
    /// <see cref="LocalEligibilityPhase.Established"/> 时确认资格并输出收敛日志（spec 5.5.1 规则 7/8、5.5.3 异常 3）。
    /// <para>
    /// 不改变既有资格判定规则（实际判定仍由 <see cref="OutboundSyncAuthorizer"/> 依据身份标志与绑定关系得出）；
    /// 重连后身份标志尚未恢复时资格保持"未确立"。
    /// </para>
    /// </summary>
    public void OnReconnectRecoveryComplete()
    {
        lock (_lock)
        {
            if (_phase == LocalEligibilityPhase.ConnectedHandshakePending || _phase == LocalEligibilityPhase.Established)
            {
                System.Diagnostics.Debug.WriteLine($"[LocalSendEligibilityState] 重连恢复完成，资格收敛确认: Phase={_phase}");
                return;
            }

            // 相位未就绪（Disconnected/EligibilityLost）时仅记录，不误确认（spec 5.5.1 规则 7 验收 b）。
            System.Diagnostics.Debug.WriteLine($"[LocalSendEligibilityState] 重连恢复完成但资格相位未就绪，保持未确立: Phase={_phase}");
        }
    }

    /// <summary>资格状态迁移。</summary>
    private void Transition(LocalEligibilityPhase newPhase, string reason)
    {
        LocalEligibilitySnapshot snapshot;
        lock (_lock)
        {
            if (_phase == newPhase)
            {
                return;
            }

            _phase = newPhase;

            if (newPhase == LocalEligibilityPhase.Established)
            {
                // 本地资格恢复 → 仅恢复仍持有有效绑定关系的实体（spec 5.2.1 规则 3、4.2.3）。
                _eligibleBoundEntities.Clear();
                foreach (var boundId in _bindingRegistry.GetValidBoundEntityIds())
                {
                    _eligibleBoundEntities.Add(boundId);
                }
            }
            else if (_phase != LocalEligibilityPhase.Established)
            {
                // 本地资格丧失 → 立即批量撤销全部有效绑定实体资格。
                _eligibleBoundEntities.Clear();
            }

            snapshot = new LocalEligibilitySnapshot(_phase, IsLocalEligible, DateTimeOffset.UtcNow);
        }

        System.Diagnostics.Debug.WriteLine($"[LocalSendEligibilityState] 资格状态迁移: {newPhase}（{reason}）, Time={snapshot.ChangedAt:O}");

        // 通知订阅者（绑定实体资格联动与日志输出）。
        StateChanged?.Invoke(snapshot);
    }
}