using System;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 6.3 P2 — 回滚 override 状态持久化的 Redis 实现。
/// <para>
/// 键 <c>cluster:{clusterId}:ds_rollback_override:{gatewayId}</c> 存
/// <see cref="RollbackPersistedState"/>（JSON：armed + 冷却截止）。写入带 TTL
/// （协调器按剩余冷却 + 余量传入）——网关崩溃未清除时键自动过期，不留永久残留；
/// 网关重启在 TTL 内 → 恢复 override。
/// </para>
/// <para>
/// 降级语义（与 DSRegistrySnapshotStore 一致，惰性初始化）：读失败 → null（干净启动）；
/// 写失败抛异常（协调器捕获，内存态仍权威）；空连接串 → 存储禁用（Read 恒 null、
/// Write/Delete 空操作，网关照常以纯内存态运行）。同步面：内部 async Redis 调用以
/// <see cref="OpTimeout"/> 超时阻塞——只被低频路径调用（触发/恢复/启动恢复），不碰数据面热路径。
/// </para>
/// </summary>
public sealed class RedisRollbackStateStore : IRollbackStateStore
{
    /// <summary>键模板（clusterId + gatewayId 前缀；gatewayId 相同 = 副本间共享，见协调器文档）。</summary>
    private const string KeyTemplate = "cluster:{0}:ds_rollback_override:{1}";

    /// <summary>单次 Redis 操作超时（阻塞同步面；防 Redis 挂起时拖住启动/门循环）。</summary>
    private static readonly TimeSpan OpTimeout = TimeSpan.FromSeconds(2);

    private readonly string _key;
    private readonly bool _enabled;
    private readonly ILogger<RedisRollbackStateStore>? _logger;
    private readonly Lazy<RedisCache> _cacheLazy;

    private RedisCache RedisCache => _cacheLazy.Value;

    /// <summary>
    /// 创建回滚状态存储。
    /// </summary>
    /// <param name="connectionString">Redis 连接串（GatewayOptions.RedisConnectionString）。</param>
    /// <param name="clusterId">集群 ID（GatewayOptions.ClusterId）。</param>
    /// <param name="gatewayId">网关实例 ID（GatewayOptions.GatewayId；相同 ID 的副本共享 override 键）。</param>
    /// <param name="logger">日志器。</param>
    public RedisRollbackStateStore(string connectionString, string clusterId, string gatewayId, ILogger<RedisRollbackStateStore>? logger = null)
    {
        _key = GetKey(clusterId, gatewayId);
        _logger = logger;
        _enabled = !string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(gatewayId);
        _cacheLazy = new Lazy<RedisCache>(() =>
        {
            try
            {
                var cache = new RedisCache(connectionString!, -1);
                _logger?.LogInformation("回滚 override 状态存储延迟初始化成功，Key={Key}", _key);
                return cache;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "回滚 override 状态存储延迟初始化失败（Key={Key}，回滚降级为纯内存态）", _key);
                throw;
            }
        });
    }

    /// <summary>Redis 键（协调器写/读与测试断言用；格式漂移即跨重启恢复失效）。</summary>
    public static string GetKey(string clusterId, string gatewayId) => string.Format(KeyTemplate, clusterId, gatewayId);

    /// <inheritdoc />
    public RollbackPersistedState? Read()
    {
        if (!_enabled) return null;
        try
        {
            var json = RunSync(() => RedisCache.GetAsync(_key));
            if (string.IsNullOrEmpty(json)) return null;
            var state = JsonSerializer.Deserialize<RollbackPersistedState>(json);
            return state.Armed ? state : null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取持久化回滚 override 失败（Key={Key}，按无 override 启动）", _key);
            return null;
        }
    }

    /// <inheritdoc />
    public void Write(RollbackPersistedState state, TimeSpan ttl)
    {
        if (!_enabled) return;
        // 负/过短 TTL 钳制：至少保留 30s（时钟回拨/异常时钟下防键过早过期）。
        if (ttl < TimeSpan.FromSeconds(30)) ttl = TimeSpan.FromSeconds(30);
        var json = JsonSerializer.Serialize(state);
        RunSync(() => RedisCache.SetAsync(_key, json, ttl));
    }

    /// <inheritdoc />
    public void Delete()
    {
        if (!_enabled) return;
        try
        {
            RunSync(() => RedisCache.RemoveAsync(_key));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "清除持久化回滚 override 失败（Key={Key}，残留键由 TTL 兜底过期）", _key);
        }
    }

    private static T RunSync<T>(Func<Task<T>> op)
    {
        var task = op();
        if (!task.Wait(OpTimeout))
        {
            throw new TimeoutException($"Redis 操作超时（>{OpTimeout.TotalSeconds:F0}s）");
        }
        return task.GetAwaiter().GetResult();
    }
}
