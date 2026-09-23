using System;
using Horizon.Game.Gateway.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 6.3 P2 — 自动回滚门 override 状态机（线程安全）。
/// <para>
/// 语义：<see cref="Arm"/> 触发后进入冷却期，冷却期内 <see cref="IsRollbackActive"/> 恒 true
/// （数据面强制回切）；冷却到期后返回 false，判定宿主开始评估健康窗口，连续达标后调用
/// <see cref="Clear"/> 自愈恢复。期间若再次 <see cref="Arm"/>（根因复发）则刷新冷却。
/// </para>
/// <para>
/// 跨重启持久化（注入 <see cref="IRollbackStateStore"/> 时启用）：<see cref="Arm"/> 把
/// {armed, 冷却截止} 写入存储（TTL = 剩余冷却 + 余量，崩溃残留自动过期）；<see cref="Clear"/>
/// 删除。构造时**最佳努力恢复**——读回持久化 override：冷却未过 → 直接续切（重启不丢回滚语义）；
/// 已过 → armed 但放行评估（判定宿主自然接管恢复流程）。读写失败均降级：内存态仍是权威
/// （回滚在本进程必然生效），仅跨重启不保证。**注意**：键含 <c>GatewayId</c>，相同 ID 的
/// 副本共享 override 键——一台网关触发的回切在另一台重启时被恢复（DS 故障通常是舰队级，
/// 该共享是有意的保守语义；需严格隔离时给副本配唯一 GatewayId）。
/// </para>
/// </summary>
public sealed class RollbackCoordinator : IRollbackCoordinator
{
    /// <summary>持久化 TTL 的固定余量（防时钟漂移导致冷却未过键先过期）。</summary>
    private static readonly TimeSpan TtlMargin = TimeSpan.FromSeconds(60);

    private readonly IOptionsMonitor<GatewayOptions> _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IRollbackStateStore? _stateStore;
    private readonly ILogger<RollbackCoordinator>? _logger;
    private readonly object _lock = new();

    private bool _armed;
    private DateTimeOffset _cooldownUntilUtc = DateTimeOffset.MinValue;

    /// <param name="options">配置监视器（UseDS 热读 + 冷却时长）。</param>
    /// <param name="clock">时钟（测试注入）。</param>
    /// <param name="stateStore">持久化存储（null = 纯内存态，向后兼容）。</param>
    /// <param name="logger">日志器。</param>
    public RollbackCoordinator(
        IOptionsMonitor<GatewayOptions> options,
        Func<DateTimeOffset>? clock = null,
        IRollbackStateStore? stateStore = null,
        ILogger<RollbackCoordinator>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _stateStore = stateStore;
        _logger = logger;
        if (_stateStore is not null)
        {
            TryRestoreFromStore();
        }
    }

    /// <summary>构造时恢复持久化 override（最佳努力：读失败/无状态 → 干净启动）。</summary>
    private void TryRestoreFromStore()
    {
        RollbackPersistedState? persisted;
        try
        {
            persisted = _stateStore!.Read();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "恢复持久化回滚 override 失败，按无 override 启动（门将重新评估）。");
            return;
        }
        if (persisted is null) return;

        lock (_lock)
        {
            _armed = persisted.Value.Armed;
            _cooldownUntilUtc = persisted.Value.CooldownUntilUtc;
        }

        var remaining = _cooldownUntilUtc - _clock();
        if (remaining > TimeSpan.Zero)
        {
            _logger?.LogWarning("恢复跨重启回滚 override：继续强制回切（剩余冷却 {Remaining:g}）。", remaining);
        }
        else
        {
            _logger?.LogInformation("恢复跨重启回滚 override：冷却已过，进入健康评估（armed 保持，等待判定宿主自愈/复发）。");
        }
    }

    public bool IsOverrideArmed
    {
        get { lock (_lock) return _armed; }
    }

    public bool IsRollbackActive
    {
        get
        {
            lock (_lock)
            {
                // 开关本已关闭 → 无需 override（且避免「关→开」时被过期 override 卡死）。
                if (!_options.CurrentValue.UseDS) return false;
                if (!_armed) return false;
                // 冷却期内强制回切；到期后放行评估恢复。
                return _clock() < _cooldownUntilUtc;
            }
        }
    }

    public void Arm()
    {
        DateTimeOffset cooldownUntil;
        lock (_lock)
        {
            // 仅当 DS 通道本处于开启状态时才需要 override；关闭时触发无意义。
            if (!_options.CurrentValue.UseDS) return;
            _armed = true;
            cooldownUntil = _clock() + TimeSpan.FromSeconds(_options.CurrentValue.DsRollback?.OverrideCooldownSeconds ?? 60);
            _cooldownUntilUtc = cooldownUntil;
        }
        Persist(cooldownUntil);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _armed = false;
            _cooldownUntilUtc = DateTimeOffset.MinValue;
        }
        if (_stateStore is null) return;
        try
        {
            _stateStore.Delete();
            _logger?.LogInformation("自动回滚门 override 已清除（自愈恢复，持久化键已删）。");
        }
        catch (Exception ex)
        {
            // 内存态已清除；残留键由 TTL 兜底过期（下轮自愈仍会重试删除）。
            _logger?.LogWarning(ex, "清除持久化回滚 override 失败（残留键由 TTL 兜底过期）。");
        }
    }

    private void Persist(DateTimeOffset cooldownUntil)
    {
        if (_stateStore is null) return;
        try
        {
            var remaining = cooldownUntil - _clock();
            // TTL = 剩余冷却 + 固定余量（至少保留到冷却期结束，崩溃残留由此自动过期）。
            var ttl = remaining + TtlMargin;
            _stateStore.Write(new RollbackPersistedState(true, cooldownUntil), ttl);
            _logger?.LogWarning("自动回滚门 override 已持久化（冷却至 {CooldownUntil:O}，TTL {Ttl:g}）。", cooldownUntil, ttl);
        }
        catch (Exception ex)
        {
            // 持久化失败不阻断回滚：内存态仍权威（本进程生效），仅跨重启不保证。
            _logger?.LogWarning(ex, "持久化回滚 override 失败（回滚仍在本进程生效，跨重启不保证）。");
        }
    }
}
