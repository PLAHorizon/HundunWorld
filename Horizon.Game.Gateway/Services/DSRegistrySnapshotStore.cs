using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Strategy.Storage.Redis;
using Microsoft.Extensions.Logging;

namespace Horizon.Game.Gateway.Services;

/// <summary>
/// 阶段 2.5+ — DSRegistry 持久化快照存储（Redis 单 blob）。
/// <para>
/// 键 <c>cluster:{clusterId}:ds_registry_snapshot</c> 存整个注册表快照（JSON 列表），
/// TTL 24h 兜底清理。单 blob 适用于 DS 舰队规模为数十级（快照 ≤ 数 KB）；
/// 周期性全量覆盖（每 15s 一次）而非逐条目写，写入放大可忽略。
/// </para>
/// <para>
/// 失败语义：保存/恢复带 3 次重试（对齐 <see cref="RedisClusterStorage"/> 的既有风格）；
/// 恢复失败/无快照 → 返回 null（调用方回退为空注册表，DS 心跳照常重建）。
/// <see cref="Serialize"/> / <see cref="Deserialize"/> 为公开静态方法，供无 Redis 环境单测。
/// </para>
/// </summary>
public sealed class DSRegistrySnapshotStore : IDSnapshotStore
{
    /// <summary>快照键模板（clusterId 前缀，与 RedisClusterStorage 的键空间一致）。</summary>
    private const string SnapshotKeyTemplate = "cluster:{0}:ds_registry_snapshot";

    /// <summary>快照 TTL：即使网关长期未清理，过期快照也会被 Redis 自动回收。</summary>
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromHours(24);

    private readonly ILogger<DSRegistrySnapshotStore> _logger;
    private readonly string _snapshotKey;
    private readonly int _maxRetryAttempts = 3;
    private readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(500);
    private readonly Lazy<RedisCache> _redisCacheLazy;

    private RedisCache RedisCache => _redisCacheLazy.Value;

    /// <summary>
    /// 创建快照存储。
    /// </summary>
    /// <param name="logger">日志器。</param>
    /// <param name="connectionString">Redis 连接串（GatewayOptions.RedisConnectionString）。</param>
    /// <param name="clusterId">集群 ID（GatewayOptions.ClusterId），隔离不同集群的路由表。</param>
    /// <param name="db">Redis 数据库编号（默认 -1）。</param>
    public DSRegistrySnapshotStore(ILogger<DSRegistrySnapshotStore> logger, string connectionString, string clusterId, int db = -1)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _snapshotKey = string.Format(SnapshotKeyTemplate, clusterId);
        _redisCacheLazy = new Lazy<RedisCache>(() =>
        {
            try
            {
                var cache = new RedisCache(connectionString, db);
                _logger.LogInformation("DSRegistry 快照存储延迟初始化成功，Key={Key}", _snapshotKey);
                return cache;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DSRegistry 快照存储延迟初始化失败（路由仍可用，仅持久化降级）");
                throw;
            }
        });
    }

    /// <inheritdoc />
    public async Task SaveSnapshotAsync(IReadOnlyList<DsEndpointInfo> entries)
    {
        for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
        {
            try
            {
                var json = Serialize(entries);
                await RedisCache.SetAsync(_snapshotKey, json, SnapshotTtl);
                return;
            }
            catch (Exception ex) when (attempt < _maxRetryAttempts)
            {
                _logger.LogWarning(ex, "保存 DS 注册表快照失败，正在进行第 {Attempt} 次重试", attempt);
                await Task.Delay(_retryDelay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存 DS 注册表快照失败，已达到最大重试次数（下轮维护周期重试）");
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DsEndpointInfo>?> RestoreSnapshotAsync()
    {
        for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
        {
            try
            {
                var json = await RedisCache.GetAsync(_snapshotKey);
                if (string.IsNullOrEmpty(json)) return null;

                var entries = Deserialize(json);
                if (entries is null)
                {
                    // 快照损坏：移除并回退为空（DS 心跳将重建），避免每次启动都读坏数据。
                    _logger.LogWarning("DSRegistry 快照反序列化失败，移除损坏快照。Key={Key}", _snapshotKey);
                    await RedisCache.RemoveAsync(_snapshotKey);
                    return null;
                }

                _logger.LogInformation("DSRegistry 快照恢复成功：{Count} 个 DS 条目。Key={Key}", entries.Count, _snapshotKey);
                return entries;
            }
            catch (Exception ex) when (attempt < _maxRetryAttempts)
            {
                _logger.LogWarning(ex, "恢复 DS 注册表快照失败，正在进行第 {Attempt} 次重试", attempt);
                await Task.Delay(_retryDelay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "恢复 DS 注册表快照失败，已达到最大重试次数（回退为空注册表）");
                throw;
            }
        }

        return null;
    }

    /// <summary>序列化快照（无 Redis 环境可单测的纯函数）。</summary>
    public static string Serialize(IReadOnlyList<DsEndpointInfo> entries)
        => JsonSerializer.Serialize(entries);

    /// <summary>反序列化快照；格式损坏返回 null（不抛异常）。</summary>
    public static IReadOnlyList<DsEndpointInfo>? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<DsEndpointInfo>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
