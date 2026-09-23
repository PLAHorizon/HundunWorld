using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Observability;
using Microsoft.Extensions.Logging;

namespace Horizon.Strategy.Storage.Redis;

/// <summary>
/// 阶段 6.3 P2 — 修正计数跨进程汇聚的 Redis 实现（Silo 写 / 网关读共用）。
/// <para>
/// 总数键 <c>cluster:{clusterId}:sync:corrections:total</c>（Silo 侧 INCRBY 原子累加；网关侧读总数
/// 自算窗口增量——自动回滚门修正率信号）；按 reason 哈希键 <c>cluster:{clusterId}:sync:corrections:reasons</c>
/// （Silo 侧 HINCRBY 按 reason 字段累加；网关侧读哈希自算按 reason 窗口增量——灰度对比面板）。
/// 读失败返回 0/空（fail-open：宁可漏检不误触发）；写失败抛异常由发布器宿主捕获重试。
/// </para>
/// </summary>
public sealed class RedisCorrectionCounterStore : ICorrectionCounterStore
{
    /// <summary>总数键模板（clusterId 前缀，与 DSRegistrySnapshotStore 的键空间风格一致）。</summary>
    private const string KeyTemplate = "cluster:{0}:sync:corrections:total";

    /// <summary>按 reason 哈希键模板（P2⑨ 灰度对比面板）。</summary>
    private const string ReasonsKeyTemplate = "cluster:{0}:sync:corrections:reasons";

    private readonly string _key;
    private readonly string _reasonsKey;
    private readonly ILogger<RedisCorrectionCounterStore>? _logger;
    private readonly Lazy<RedisCache> _cacheLazy;

    private RedisCache RedisCache => _cacheLazy.Value;

    /// <summary>
    /// 创建修正计数存储。
    /// </summary>
    /// <param name="connectionString">Redis 连接串（Silo 侧 <c>Redis:ConnectionString</c>；网关侧 <c>Gateway:RedisConnectionString</c>）。</param>
    /// <param name="clusterId">集群 ID，隔离不同集群（必须与网关 <c>GatewayOptions.ClusterId</c> 一致）。</param>
    /// <param name="db">Redis 数据库编号（默认 -1）。</param>
    /// <param name="logger">日志器。</param>
    public RedisCorrectionCounterStore(string connectionString, string clusterId, int db = -1, ILogger<RedisCorrectionCounterStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("connectionString 不能为空（修正计数汇聚依赖 Redis）", nameof(connectionString));
        }
        _key = GetKey(clusterId);
        _reasonsKey = GetReasonsKey(clusterId);
        _logger = logger;
        _cacheLazy = new Lazy<RedisCache>(() =>
        {
            try
            {
                var cache = new RedisCache(connectionString, db);
                _logger?.LogInformation("修正计数汇聚存储延迟初始化成功，Key={Key} ReasonsKey={ReasonsKey}", _key, _reasonsKey);
                return cache;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "修正计数汇聚存储延迟初始化失败（Key={Key}）", _key);
                throw;
            }
        });
    }

    /// <summary>总数 Redis 键（Silo 写与网关读必须一致；测试可直调断言键格式）。</summary>
    public static string GetKey(string clusterId) => string.Format(KeyTemplate, clusterId);

    /// <summary>按 reason 哈希 Redis 键（Silo 写与网关读必须一致；测试可直调断言键格式）。</summary>
    public static string GetReasonsKey(string clusterId) => string.Format(ReasonsKeyTemplate, clusterId);

    /// <inheritdoc />
    public Task<long> GetCorrectionsTotalAsync()
    {
        // GetAsync 内部吞异常返回默认（0）：Redis 故障时修正信号 fail-open 为 0（不误触发回滚门）。
        return RedisCache.GetAsync<long>(_key);
    }

    /// <inheritdoc />
    public async Task IncrementCorrectionsAsync(long delta)
    {
        if (delta <= 0) return;
        await RedisCache.IncrementValueAsync(_key, delta);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> GetCorrectionsByReasonAsync()
    {
        try
        {
            return await RedisCache.GetHashAllAsync(_reasonsKey);
        }
        catch (Exception ex)
        {
            // 读失败返回空（fail-open：面板修正信号缺失不误报）。
            _logger?.LogWarning(ex, "读取按 reason 修正计数失败（ReasonsKey={ReasonsKey}），本窗口修正信号置空。", _reasonsKey);
            return new Dictionary<string, long>();
        }
    }

    /// <inheritdoc />
    public async Task IncrementCorrectionsByReasonAsync(IReadOnlyDictionary<string, long> deltas)
    {
        foreach (var kv in deltas)
        {
            if (kv.Value <= 0) continue;
            await RedisCache.IncrementHashValueAsync(_reasonsKey, kv.Key, kv.Value);
        }
    }
}
