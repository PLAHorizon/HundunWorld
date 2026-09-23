using System.Threading.Tasks;

namespace Horizon.Game.Core.Sim.Server;

/// <summary>
/// 角色位置永久存储接口（Redis 实现）。<br/>
/// 使用独立 Redis Key 永久存储角色最后位置，与 Orleans GrainStorage 分离（双轨制）。<br/>
/// Key 设计：character:position:{characterId} → Hash { x, y, z, yaw, updatedAt }<br/>
/// TTL = 无（永久存储），服务器重启后激活 Grain 时从此处恢复位置。
/// </summary>
/// <remarks>
/// 坐标系约定：存储 ECS Z-up 坐标（X=左右, Y=前后, Z=上下），<br/>
/// 由调用方（ZoneShardGrain）负责 Flax Y-up ↔ ECS Z-up 坐标转换，Store 层不感知坐标系。
/// </remarks>
public interface ICharacterPositionStore
{
    /// <summary>
    /// 保存角色最后位置到 Redis（永久存储，无 TTL）。<br/>
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
    /// 在 CharacterGrain.OnActivateAsync 中调用，从 Redis 恢复位置到内存缓存。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <returns>位置元组；null=无数据或 Redis 不可用</returns>
    Task<(float X, float Y, float Z, float Yaw)?> GetPositionAsync(long characterId);

    /// <summary>
    /// 清除角色位置数据（预留）。<br/>
    /// 用于未来角色删除场景，当前业务流程不调用。
    /// </summary>
    /// <param name="characterId">角色 ID</param>
    /// <returns>true=清除成功或 key 不存在；false=清除失败</returns>
    Task<bool> ClearPositionAsync(long characterId);
}
