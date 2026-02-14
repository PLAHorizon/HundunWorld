using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 游戏事件发布器接口 — 通过Orleans Memory Stream发布游戏事件
    /// 解耦战斗结果通知、日志统计等非关键路径的异步处理
    /// </summary>
    public interface IGameEventPublisher
    {
        /// <summary>
        /// 发布角色相关事件
        /// </summary>
        /// <param name="gameEvent">游戏事件</param>
        Task PublishCharacterEventAsync(GameEvent gameEvent);

        /// <summary>
        /// 发布战斗相关事件
        /// </summary>
        /// <param name="gameEvent">游戏事件</param>
        Task PublishCombatEventAsync(GameEvent gameEvent);

        /// <summary>
        /// 发布社交相关事件
        /// </summary>
        /// <param name="gameEvent">游戏事件</param>
        Task PublishSocialEventAsync(GameEvent gameEvent);

        /// <summary>
        /// 发布系统相关事件
        /// </summary>
        /// <param name="gameEvent">游戏事件</param>
        Task PublishSystemEventAsync(GameEvent gameEvent);
    }
}
