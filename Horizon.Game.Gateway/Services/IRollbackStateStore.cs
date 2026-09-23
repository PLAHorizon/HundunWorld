using System;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 6.3 P2 — 自动回滚门 override 状态持久化存储（跨网关重启保持回切语义）。
/// <para>
/// 现状：override 是进程内内存态——网关重启后丢失，UseDS 配置值原样生效，DS 若仍故障会
/// 重新进入「放量→退化→再触发」循环（重检测窗口期用户受损）。持久化后：重启即恢复上次的
/// 回切状态（冷却期未过 → 继续强制回切；已过 → 进入健康评估），回滚语义跨重启连续。
/// </para>
/// <para>
/// 同步接口：<see cref="Arm"/>/<see cref="Clear"/> 由判定宿主在低频路径调用（每窗口一次，
/// 触发/恢复是罕见事件），同步阻塞（实现带超时）可接受且避免把协调器改异步后波及数据面
/// 热路径与全部测试。读失败 → null（协调器干净启动）；写失败抛异常（协调器捕获后保持
/// 内存态，回滚仍是本进程内权威，仅跨重启不保证）。
/// </para>
/// </summary>
public interface IRollbackStateStore
{
    /// <summary>读取持久化 override（无/失效/读失败返回 null）。</summary>
    RollbackPersistedState? Read();

    /// <summary>写入 override（实现须带 TTL 自清理，防网关崩溃后残留）。</summary>
    void Write(RollbackPersistedState state, TimeSpan ttl);

    /// <summary>清除 override（自愈恢复/手动处置时）。</summary>
    void Delete();
}

/// <summary>持久化的回滚 override 状态快照（System.Text.Json 序列化）。</summary>
public readonly record struct RollbackPersistedState(bool Armed, DateTimeOffset CooldownUntilUtc);
