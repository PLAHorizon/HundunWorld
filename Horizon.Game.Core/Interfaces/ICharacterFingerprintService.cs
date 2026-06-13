using System.Threading.Tasks;

namespace Horizon.Game.Core.Interfaces
{
    /// <summary>
    /// 游戏角色指纹服务接口
    /// 用于防止同一游戏用户同时使用同一角色进入游戏。
    /// 角色进入游戏时生成指纹，退出时清除。
    /// 实现端使用 Redis 存储以支持网关集群统一管理。
    /// </summary>
    public interface ICharacterFingerprintService
    {
        /// <summary>
        /// 尝试为角色创建在线指纹。如果角色已在线则返回 false。
        /// </summary>
        /// <param name="userId">游戏用户 ID</param>
        /// <param name="characterId">角色 ID</param>
        /// <param name="gatewayId">当前网关实例 ID</param>
        /// <param name="connectionId">连接 ID</param>
        /// <returns>true 表示成功占用；false 表示角色已被其他会话占用</returns>
        Task<bool> TryAcquireAsync(long userId, long characterId, string gatewayId, string connectionId);

        /// <summary>
        /// 释放指定角色的在线指纹
        /// </summary>
        /// <param name="characterId">角色 ID</param>
        /// <returns>true 表示成功释放</returns>
        Task<bool> ReleaseAsync(long characterId);

        /// <summary>
        /// 释放指定连接 ID 关联的所有角色指纹
        /// （用于客户端断线时清理）
        /// </summary>
        /// <param name="connectionId">连接 ID</param>
        Task ReleaseByConnectionAsync(string connectionId);

        /// <summary>
        /// 检查角色是否已在线
        /// </summary>
        /// <param name="characterId">角色 ID</param>
        /// <returns>true 表示角色当前在线</returns>
        Task<bool> IsOnlineAsync(long characterId);
    }
}
