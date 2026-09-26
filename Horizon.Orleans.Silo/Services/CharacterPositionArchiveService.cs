using System;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Core.Sim.Server;
using Horizon.Model.GameModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Horizon.Orleans.Silo.Services
{
    /// <summary>
    /// 角色位置归档服务（Redis 热缓存 → SQL Server 冷存储）。<br/>
    /// 周期性枚举"超过 24 小时没有任何新位置更新"的 Redis 位置缓存，
    /// 落库到 SQL Server（Game_HunduShijie_Character 的 position_x/y/z、rotation），
    /// 落库成功后清除该条 Redis Hash 与更新索引成员。<br/>
    /// 读路径配合：CharacterGrain.GetLastPositionAsync 先命中 Redis，未命中回退读 SQL Server。
    /// <para>
    /// 坐标系：Redis 缓存存 ECS Z-up（X=左右, Y=前后, Z=上下）；
    /// SQL 列沿用建号出生点与 GameProfile 映射的 Flax Y-up 约定，
    /// 落库转换 FlaxX=ecsX, FlaxY=ecsZ, FlaxZ=ecsY（与读回侧互为逆运算）。
    /// </para>
    /// </summary>
    public sealed class CharacterPositionArchiveService : BackgroundService
    {
        /// <summary>清扫周期：位置写入约 1 次/秒，10 分钟粒度对"24h 未更新"判定绰绰有余</summary>
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

        /// <summary>无新位置更新超过该时长即落库并清除缓存</summary>
        private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(24);

        /// <summary>单轮最多归档的角色数，防止一次性拉取过多</summary>
        private const int BatchSize = 200;

        private readonly ICharacterPositionStore _positionStore;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CharacterPositionArchiveService> _logger;

        public CharacterPositionArchiveService(
            ICharacterPositionStore positionStore,
            IServiceScopeFactory scopeFactory,
            ILogger<CharacterPositionArchiveService> logger)
        {
            _positionStore = positionStore ?? throw new ArgumentNullException(nameof(positionStore));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CharacterPositionArchiveService 启动：每 {Interval} 清扫一次，超过 {Threshold} 无位置更新落库 SQL 并清 Redis。",
                SweepInterval, StaleThreshold);

            // 历史遗留位置 Key（索引机制上线前写入）不会被枚举，启动时一次性 SCAN 回填索引（尽力而为）
            try
            {
                var backfilled = await _positionStore.BackfillPositionIndexAsync();
                _logger.LogInformation("CharacterPositionArchiveService：位置索引回填完成，补录 {Added} 个角色。", backfilled);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CharacterPositionArchiveService：位置索引回填失败（仅延迟旧键归档，不影响读写）。");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);
                    await SweepOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CharacterPositionArchiveService：本轮清扫失败（下轮重试）。");
                }
            }

            _logger.LogInformation("CharacterPositionArchiveService 退出。");
        }

        private async Task SweepOnceAsync(CancellationToken cancellationToken)
        {
            var cutoffTicks = DateTime.UtcNow.Subtract(StaleThreshold).Ticks;
            var stalePositions = await _positionStore.GetStalePositionsAsync(cutoffTicks, BatchSize);
            if (stalePositions.Count == 0)
            {
                return;
            }

            int archived = 0;
            foreach (var entry in stalePositions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await ArchiveToDatabaseAsync(entry))
                {
                    // 仅落库成功后清缓存；失败时保留 Redis 条目，下轮自动重试
                    await _positionStore.MarkPositionArchivedAsync(entry.CharacterId);
                    archived++;
                    _logger.LogInformation(
                        "角色 {CharacterId} 位置已归档：ECS({X:F2},{Y:F2},{Z:F2})，最后更新 {UpdatedAt:O}。",
                        entry.CharacterId, entry.X, entry.Y, entry.Z,
                        new DateTime(entry.UpdatedAtUtcTicks, DateTimeKind.Utc));
                }
            }

            _logger.LogInformation("CharacterPositionArchiveService：本轮扫描 {Total} 个超期位置，成功归档 {Archived} 个。",
                stalePositions.Count, archived);
        }

        private async Task<bool> ArchiveToDatabaseAsync(StaleCharacterPosition entry)
        {
            try
            {
                // IDataContext 注册为 Scoped，托管服务（Singleton）须开作用域解析
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider
                    .GetRequiredService<IDataContext<GameEntityContext, CharacterEntity, long>>();

                var character = await context.QueryFirstOrDefaultAsync(
                    c => c.Id == entry.CharacterId, isTracking: true);
                if (character == null)
                {
                    // 角色已被删除：视为归档完成，允许清掉缓存与索引
                    _logger.LogWarning("位置归档：角色 {CharacterId} 在数据库中不存在，直接清除其 Redis 缓存。", entry.CharacterId);
                    return true;
                }

                // ECS Z-up → Flax Y-up（与 EnrichCharacterInfo / EnterWorldAsync 的逆向换算一致）
                character.PositionX = entry.X;
                character.PositionY = entry.Z;
                character.PositionZ = entry.Y;
                character.Rotation = entry.Yaw;

                return await context.UpdateAsync(character, character.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "位置归档失败：角色 {CharacterId} 写入 SQL Server 出错（保留缓存，下轮重试）。", entry.CharacterId);
                return false;
            }
        }
    }
}
