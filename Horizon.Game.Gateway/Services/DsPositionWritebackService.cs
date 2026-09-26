using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Horizon.Game.Gateway.Services
{
    /// <summary>
    /// DS 通道玩家位置写回服务。<br/>
    /// <b>背景</b>：<c>UseDS=true</c> 灰度通道下，握手在 <c>TryRouteToDsAsync</c> 即返回，
    /// 玩家实体只存在于 DsHost 的权威 SimWorld，<c>ZoneShardGrain.TickAsync</c> 里
    /// "每 60 tick 写 CharacterGrain 位置缓存"的路径对其完全不生效——
    /// 于是移动后离线再上线，Redis/SQL 里都没有新位置，角色被放回出生点。
    /// <para>
    /// <b>做法</b>：网关是 DS 下行快照的必经点（<c>DsDownlinkIngress</c> 解码后分发）。
    /// 本服务作为旁路观察者，从每个受众自己的快照里提取该玩家的权威 Transform
    /// （协议为 Flax Y-up），每 <see cref="FlushIntervalSeconds"/> 秒换轴成 ECS Z-up
    /// 后 fire-and-forget 写入 <c>ICharacterGrain.UpdateLastPositionAsync</c>
    /// （Redis 热缓存；24h 归档与读回退由 Silo 侧既有链路负责）。
    /// 与 Orleans 通道"每秒一次"的缓存新鲜度同量级；断线 15s 宽限期内必有至少一轮冲刷。
    /// </para>
    /// </summary>
    public sealed class DsPositionWritebackService : BackgroundService
    {
        /// <summary>写回周期（秒）。快照约 10Hz 到达，5s 冲刷一次足够新鲜且把 Grain 压力降两个量级。</summary>
        private const int FlushIntervalSeconds = 5;

        /// <summary>离线超过该时长的跟踪条目被丢弃（其位置已在最后一次冲刷中落缓存）。</summary>
        private static readonly TimeSpan StaleEntryLifetime = TimeSpan.FromMinutes(2);

        private sealed class TrackedPosition
        {
            public float X;   // Flax 左右
            public float Y;   // Flax 上下
            public float Z;   // Flax 前后
            public float Yaw;
            public DateTime LastSeenUtc;
        }

        private readonly ConcurrentDictionary<long, TrackedPosition> _lastSeen = new();
        private readonly IConnectionManager _connectionManager;
        private readonly IClusterClient _clusterClient;
        private readonly ILogger<DsPositionWritebackService> _logger;

        /// <summary>累计观察到的玩家自身位置帧数（监控）。</summary>
        public long ObservedFrameCount { get; private set; }

        /// <summary>累计写回冲刷次数（监控）。</summary>
        public long FlushCount { get; private set; }

        public DsPositionWritebackService(
            IConnectionManager connectionManager,
            IClusterClient clusterClient,
            ILogger<DsPositionWritebackService> logger)
        {
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// <see cref="Horizon.Game.Core.Sim.Server.DsDownlinkIngress"/> 解码成功后的只读观察回调。
        /// 仅处理快照帧中"受众包含实体自身"的 Transform——即该玩家自己的权威位置；
        /// 其他玩家/怪物的条目一律跳过（避免把别人的坐标写进本角色缓存）。
        /// </summary>
        public void Observe(SyncPacket packet, long[] audienceSessionIds)
        {
            if (packet is not SnapshotPacket snapshot) return;
            var deltas = snapshot.Deltas;
            if (deltas is null || deltas.Length == 0) return;
            if (audienceSessionIds is null || audienceSessionIds.Length == 0) return;

            for (int i = 0; i < deltas.Length; i++)
            {
                var delta = deltas[i];
                if (delta.Kind == EntityDeltaKind.Despawn) continue;
                if (delta.Transform is not { } transform) continue;

                // 玩家 EntityId == characterId == sessionId（DS 通道三者同源）
                long characterId = (long)delta.EntityId;

                bool isSelfInAudience = false;
                for (int a = 0; a < audienceSessionIds.Length; a++)
                {
                    if (audienceSessionIds[a] == characterId)
                    {
                        isSelfInAudience = true;
                        break;
                    }
                }
                if (!isSelfInAudience) continue;

                _lastSeen[characterId] = new TrackedPosition
                {
                    X = transform.X,
                    Y = transform.Y,
                    Z = transform.Z,
                    Yaw = transform.Yaw,
                    LastSeenUtc = DateTime.UtcNow,
                };
                ObservedFrameCount++;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DsPositionWritebackService 启动：DS 通道玩家位置每 {Interval}s 写回 CharacterGrain 缓存。", FlushIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(FlushIntervalSeconds), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                FlushPendingPositions();
            }

            // 停机前最后冲刷一次（尽力而为）
            FlushPendingPositions();
            _logger.LogInformation("DsPositionWritebackService 退出。Observed={Observed} Flush={Flush}", ObservedFrameCount, FlushCount);
        }

        private void FlushPendingPositions()
        {
            if (_lastSeen.IsEmpty) return;

            HashSet<long> online;
            try
            {
                online = new HashSet<long>(_connectionManager.GetAllCharacterIds());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DsPositionWritebackService：读取在线角色集合失败，本轮冲刷跳过。");
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var kv in _lastSeen)
            {
                if (!online.Contains(kv.Key))
                {
                    // 已离线：位置已随最后一轮冲刷落缓存；过期条目直接清理
                    if (now - kv.Value.LastSeenUtc > StaleEntryLifetime)
                        _lastSeen.TryRemove(kv.Key, out _);
                    continue;
                }

                var pos = kv.Value;
                // 协议 Flax Y-up → 缓存契约 ECS Z-up（与 ZoneShardGrain 存储、
                // EnterWorldAsync 的 ECS→Flax 换轴互为逆运算）：ecsX=flaxX, ecsY=flaxZ, ecsZ=flaxY
                try
                {
                    _clusterClient.GetGrain<ICharacterGrain>(kv.Key)
                        .UpdateLastPositionAsync(pos.X, pos.Z, pos.Y, pos.Yaw)
                        .Ignore();
                    FlushCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "DsPositionWritebackService：角色 {CharacterId} 位置写回失败（下轮重试）。", kv.Key);
                }
            }
        }
    }
}
