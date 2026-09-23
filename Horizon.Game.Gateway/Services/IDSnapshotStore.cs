using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// DSRegistry 持久化快照存储抽象（阶段 2.5+）：保存/恢复注册表快照，
/// 消除网关重启/新实例冷启动后的「DS 心跳续约重建空窗」。
/// <para>
/// 实现选择：Redis 单 blob 快照（<see cref="DSRegistrySnapshotStore"/>）。
/// 快照由 <see cref="DSRegistryMaintenanceHostedService"/> 在既有 15s 维护周期内驱动：
/// 启动时恢复（重启后路由表立即可用），每周期保存（崩溃丢失 ≤15s 的注册增量）。
/// 保存/恢复失败一律降级为空注册表（DS 心跳照常重建），不影响路由可用性。
/// </para>
/// </summary>
public interface IDSnapshotStore
{
    /// <summary>保存当前注册表快照（best-effort：失败由调用方记录并下轮重试）。</summary>
    Task SaveSnapshotAsync(IReadOnlyList<DsEndpointInfo> entries);

    /// <summary>恢复快照；无快照/损坏/存储不可用时返回 null（调用方回退为空注册表）。</summary>
    Task<IReadOnlyList<DsEndpointInfo>?> RestoreSnapshotAsync();
}
