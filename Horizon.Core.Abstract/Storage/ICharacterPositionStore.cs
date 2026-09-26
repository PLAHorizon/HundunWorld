using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 归档清扫用的位置缓存条目（Redis Hash 原始读数，坐标系约定同接口注释）。
/// </summary>
/// <param name="CharacterId">角色 ID</param>
/// <param name="X">ECS X（左右）</param>
/// <param name="Y">ECS Y（前后）</param>
/// <param name="Z">ECS Z（上下）</param>
/// <param name="Yaw">朝向（弧度）</param>
/// <param name="UpdatedAtUtcTicks">最后一次位置写入时间（DateTime.UtcNow.Ticks）</param>
public readonly record struct StaleCharacterPosition(long CharacterId, float X, float Y, float Z, float Yaw, long UpdatedAtUtcTicks);

/// <summary>
/// 角色位置缓存存储（Redis 实现，配合 24h SQL Server 归档）。<br/>
/// 双轨制：<br/>
/// - 热缓存：Key <c>character:position:{characterId}</c> → Hash { x, y, z, yaw, updatedAt }（ECS 坐标）<br/>
/// - 更新索引：Sorted Set <c>character:position:index</c>，member=characterId，score=updatedAtTicks，
///   供归档服务按"超过 24h 无新位置更新"枚举。<br/>
/// 角色进入游戏时先命中本缓存，未命中由调用方回退读 SQL Server 归档。
/// </summary>
/// <remarks>
/// 坐标系约定：存储 ECS Z-up 坐标（X=左右, Y=前后, Z=上下），<br/>
/// 由调用方（ZoneShardGrain）负责 Flax Y-up ↔ ECS Z-up 坐标转换，Store 层不感知坐标系。
/// </remarks>
public interface ICharacterPositionStore
{
    /// <summary>
    /// 保存角色最后位置到 Redis 热缓存，并同步更新索引 score（=本次写入时间）。<br/>
    /// 在 ZoneShardGrain.TickAsync 中由 CharacterGrain.UpdateLastPositionAsync 调用。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <param name="x">X 坐标（ECS Z-up：左右）</param>
    /// <param name="y">Y 坐标（ECS Z-up：前后）</param>
    /// <param name="z">Z 坐标（ECS Z-up：上下）</param>
    /// <param name="yaw">朝向（弧度）</param>
    /// <returns>true=保存成功；false=保存失败（Redis 不可用时降级）</returns>
    Task<bool> SavePositionAsync(long characterId, float x, float y, float z, float yaw);

    /// <summary>
    /// 读取角色最后位置。<br/>
    /// 在 CharacterGrain.OnActivateAsync / GetLastPositionAsync 中调用。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <returns>位置元组；null=无数据或 Redis 不可用</returns>
    Task<(float X, float Y, float Z, float Yaw)?> GetPositionAsync(long characterId);

    /// <summary>
    /// 清除角色位置数据（角色删除场景；归档完成请改用 <see cref="MarkPositionArchivedAsync"/>）。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <returns>true=清除成功或 key 不存在；false=清除失败</returns>
    Task<bool> ClearPositionAsync(long characterId);

    /// <summary>
    /// 枚举"最后更新早于 cutoffUtcTicks"的位置条目（按更新时间升序，最多 maxCount 条）。<br/>
    /// 基于索引 Sorted Set；索引有而 Hash 已丢失的孤儿成员会被顺手移除。
    /// </summary>
    Task<IReadOnlyList<StaleCharacterPosition>> GetStalePositionsAsync(long cutoffUtcTicks, int maxCount);

    /// <summary>
    /// 归档完成：删除位置 Hash 并移除索引成员。仅在 SQL Server 落库成功后调用。
    /// </summary>
    Task<bool> MarkPositionArchivedAsync(long characterId);

    /// <summary>
    /// 一次性迁移：SCAN 所有历史 <c>character:position:*</c> Hash，把无索引成员的角色
    /// 按 Hash 内 updatedAt 补进索引（ZADD NX），确保旧数据也能被归档清扫。
    /// </summary>
    /// <returns>补录的索引成员数</returns>
    Task<int> BackfillPositionIndexAsync();
}
