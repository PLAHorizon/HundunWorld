using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.ECS.Arch.SyncGuard.Contracts;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// ECS 侧授权系统：在 <see cref="SystemGroup.NetworkSend"/> 阶段、打包之前对发送队列做统一授权过滤。
/// <para>
/// 防御性组件守卫（spec 5.3.1 规则 2）：扫描 <see cref="InputSendQueue"/> 出队包，按发起实体 ID 授权，
/// 无资格包丢弃并告警；同时执行"本地角色唯一性"检查（spec 6.1 规则 3）。
/// </para>
/// </summary>
[ArchSystem(SystemGroup.NetworkSend, order: -1)]
public sealed class SendAuthorizationSystem : ArchSystemBase
{
    /// <summary>当前实例（供外部静态访问）。</summary>
    public static SendAuthorizationSystem? Instance { get; private set; }

    /// <summary>注入的集中式资格判定组件。</summary>
    private IOutboundSyncAuthorizer? _authorizer;

    /// <summary>违规告警上报（与授权器共用同一实例）。</summary>
    private ISendViolationReporter? _violationReporter;

    /// <summary>当前本地玩家实体 ID 提供者（0 表示身份未确立，由装配方注入）。</summary>
    private Func<ulong>? _localPlayerIdProvider;

    /// <summary>诊断：被丢弃的违规包数量。</summary>
    public long DroppedPackets { get; private set; }

    /// <summary>注入集中式资格判定组件（由 HundunWorldGame 在装配时调用）。</summary>
    public void SetAuthorizer(IOutboundSyncAuthorizer authorizer)
    {
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _violationReporter = authorizer as ISendViolationReporter;
    }

    /// <summary>注入本地玩家实体 ID 提供者（由 HundunWorldGame 在装配时调用）。</summary>
    public void SetLocalPlayerIdProvider(Func<ulong> provider)
    {
        _localPlayerIdProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc />
    public override void Initialize(World world)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        if (_authorizer == null)
        {
            return;
        }

        // 本地角色唯一性检查：统计当前 ECS 世界中 IsLocalPlayer=true 的实体数。
        // 超过 1 个时，重复实体被标记为"重复本地玩家"（由授权器在判定时拒绝）。
        int localPlayerCount = 0;
        var query = new QueryDescription().WithAll<NetworkIdentityComponent>();
        world.Query(in query, (ref NetworkIdentityComponent netId) =>
        {
            if (netId.IsLocalPlayer)
            {
                localPlayerCount++;
            }
        });

        if (localPlayerCount > 1)
        {
            OnLocalPlayerDuplicated(world);
        }
    }

    /// <summary>
    /// 本地玩家重复时，对除真实身份以外的重复本地实体输出告警。
    /// 真实身份的判定：与 <see cref="LocalPlayerOwnerId"/> 一致的实体视为真实，其余视为重复冒名。
    /// </summary>
    private void OnLocalPlayerDuplicated(World world)
    {
        var realLocalId = _localPlayerIdProvider?.Invoke() ?? 0UL;

        var query = new QueryDescription().WithAll<NetworkIdentityComponent>();
        world.Query(in query, (Entity entity, ref NetworkIdentityComponent netId) =>
        {
            if (netId.IsLocalPlayer && netId.EntityId != realLocalId)
            {
                DroppedPackets++;
                _violationReporter?.ReportViolation(new SendViolationInfo(
                    netId.EntityId,
                    EntitySendCategory.Unqualified,
                    SendRejectReason.LocalPlayerDuplicated,
                    DateTimeOffset.UtcNow));
            }
        });
    }

    /// <inheritdoc />
    public override void Dispose(World world)
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}