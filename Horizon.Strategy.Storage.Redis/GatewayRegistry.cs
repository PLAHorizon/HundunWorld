using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Horizon.Strategy.Storage.Redis
{
    /// <summary>
    /// 网关类型枚举。
    /// </summary>
    public enum GatewayType
    {
        /// <summary>未知类型。</summary>
        Unknown = 0,

        /// <summary>游戏网关（Horizon.Game.Gateway）。</summary>
        Game = 1,

        /// <summary>即时通讯网关（Horizon.IM.Gateway）。</summary>
        IM = 2
    }

    /// <summary>
    /// 网关实例注册信息（写入 Redis 的数据结构）。
    /// </summary>
    public class GatewayRegistration
    {
        /// <summary>
        /// 实例 ID（集群内唯一）。
        /// </summary>
        public string InstanceId { get; set; } = string.Empty;

        /// <summary>
        /// 网关类型。
        /// </summary>
        public GatewayType GatewayType { get; set; } = GatewayType.Unknown;

        /// <summary>
        /// 集群 ID（用于区分不同的 Orleans 集群）。
        /// </summary>
        public string ClusterId { get; set; } = string.Empty;

        /// <summary>
        /// 对外可访问的 IP 或主机名。
        /// </summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// 对外可访问的端口。
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// 可选的地域/区域标识。
        /// </summary>
        public string Region { get; set; } = string.Empty;

        /// <summary>
        /// 最后心跳时间（UTC）。
        /// </summary>
        public DateTime LastHeartbeatUtc { get; set; }
    }

    /// <summary>
    /// 网关注册中心。
    /// 将每个网关实例的 (IP, Port, Type) 写入 Redis，方便 WebApi / 客户端发现可用网关。
    /// 所有 Horizon 网关（Game、IM）共享同一个键空间，使用 <see cref="GatewayType"/> 区分。
    /// </summary>
    /// <remarks>
    /// Redis 键布局：
    /// - <c>hundunworld:gateways:instances</c>：存放所有在线实例 ID 的集合。
    /// - <c>hundunworld:gateways:instance:{instanceId}</c>：存放单个实例的 JSON 信息，带 TTL，
    ///   通过定时心跳续期。若实例下线未续期则自动过期，保证集群节点视图的数据一致性。
    /// </remarks>
    public class GatewayRegistry
    {
        /// <summary>在线实例 ID 集合键。</summary>
        public const string InstancesSetKey = "hundunworld:gateways:instances";

        /// <summary>单个实例键前缀。</summary>
        public const string InstanceKeyPrefix = "hundunworld:gateways:instance:";

        private readonly RedisCache _redisCache;
        private readonly ILogger? _logger;
        private readonly TimeSpan _entryTtl;

        /// <summary>
        /// 使用已创建的 <see cref="RedisCache"/> 构造注册中心。
        /// </summary>
        /// <param name="redisCache">共享的 Redis 缓存实例。</param>
        /// <param name="entryTtl">单个实例条目的 TTL；心跳频率应显著低于该值。默认 2 分钟。</param>
        /// <param name="logger">日志记录器。</param>
        public GatewayRegistry(RedisCache redisCache, TimeSpan? entryTtl = null, ILogger? logger = null)
        {
            _redisCache = redisCache ?? throw new ArgumentNullException(nameof(redisCache));
            _entryTtl = entryTtl ?? TimeSpan.FromMinutes(2);
            _logger = logger;
        }

        /// <summary>
        /// 使用连接字符串构造注册中心。
        /// </summary>
        public GatewayRegistry(string redisConnectionString, int db = -1, TimeSpan? entryTtl = null, ILogger? logger = null)
            : this(new RedisCache(redisConnectionString, db), entryTtl, logger)
        {
        }

        /// <summary>
        /// 注册或刷新一个网关实例。
        /// 每次调用都会覆盖写入 JSON 并重置 TTL，因此可以直接用作心跳。
        /// </summary>
        public async Task RegisterAsync(GatewayRegistration registration)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));
            if (string.IsNullOrWhiteSpace(registration.InstanceId))
                throw new ArgumentException("InstanceId 不能为空", nameof(registration));

            registration.LastHeartbeatUtc = DateTime.UtcNow;
            var json = JsonConvert.SerializeObject(registration);

            await _redisCache.SetAsync(GetInstanceKey(registration.InstanceId), json, _entryTtl).ConfigureAwait(false);
            await _redisCache.AddItemToSetAsync(InstancesSetKey, registration.InstanceId).ConfigureAwait(false);

            _logger?.LogDebug(
                "网关实例已注册到 Redis: {InstanceId}, Type={Type}, {Address}:{Port}",
                registration.InstanceId, registration.GatewayType, registration.Address, registration.Port);
        }

        /// <summary>
        /// 下线一个网关实例（同时从集合中移除并删除 JSON 键）。
        /// </summary>
        public async Task UnregisterAsync(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;

            try
            {
                await _redisCache.RemoveAsync(GetInstanceKey(instanceId)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "删除网关实例信息失败: {InstanceId}", instanceId);
            }

            try
            {
                await _redisCache.RemoveItemFromSetAsync(InstancesSetKey, instanceId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "从网关集合中移除实例失败: {InstanceId}", instanceId);
            }
        }

        /// <summary>
        /// 获取所有仍在线的网关实例。过期条目会从集合中剔除以保持一致性。
        /// </summary>
        public async Task<List<GatewayRegistration>> GetAllAsync()
        {
            var instanceIds = await _redisCache.GetAllItemsFromSetAsync(InstancesSetKey).ConfigureAwait(false);
            if (instanceIds == null || instanceIds.Count == 0)
            {
                return new List<GatewayRegistration>();
            }

            var results = new List<GatewayRegistration>(instanceIds.Count);
            foreach (var instanceId in instanceIds)
            {
                if (string.IsNullOrWhiteSpace(instanceId)) continue;

                GatewayRegistration? entry = null;
                try
                {
                    var json = await _redisCache.GetAsync(GetInstanceKey(instanceId)).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(json))
                    {
                        entry = JsonConvert.DeserializeObject<GatewayRegistration>(json);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "反序列化网关实例失败: {InstanceId}", instanceId);
                }

                if (entry != null && !string.IsNullOrWhiteSpace(entry.Address) && entry.Port > 0)
                {
                    results.Add(entry);
                }
                else
                {
                    // 条目已过期或无效：从集合中清理，避免脏数据。
                    try
                    {
                        await _redisCache.RemoveItemFromSetAsync(InstancesSetKey, instanceId).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "清理过期网关实例失败: {InstanceId}", instanceId);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 获取指定类型的所有在线网关。
        /// </summary>
        public async Task<List<GatewayRegistration>> GetByTypeAsync(GatewayType gatewayType)
        {
            var all = await GetAllAsync().ConfigureAwait(false);
            return all.Where(r => r.GatewayType == gatewayType).ToList();
        }

        private static string GetInstanceKey(string instanceId) => InstanceKeyPrefix + instanceId;
    }
}
