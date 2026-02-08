using Horizon.Core.Abstract;
using Horizon.Orleans.Interface;
using Horizon.Game.Message.Network;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 游戏事件发布器 — 通过Orleans Memory Stream发布游戏事件
    /// 解耦战斗结果通知、日志统计等非关键路径的异步处理
    /// </summary>
    public class GameEventPublisher : IGameEventPublisher
    {
        private readonly IClusterClient _clusterClient;
        private readonly ILogger<GameEventPublisher> _logger;

        public GameEventPublisher(IClusterClient clusterClient, ILogger<GameEventPublisher> logger)
        {
            _clusterClient = clusterClient;
            _logger = logger;
        }

        public async Task PublishCharacterEventAsync(GameEvent gameEvent)
        {
            await PublishEventAsync(GameStreamNamespaces.CharacterEvents, gameEvent);
        }

        public async Task PublishCombatEventAsync(GameEvent gameEvent)
        {
            await PublishEventAsync(GameStreamNamespaces.CombatEvents, gameEvent);
        }

        public async Task PublishSocialEventAsync(GameEvent gameEvent)
        {
            await PublishEventAsync(GameStreamNamespaces.SocialEvents, gameEvent);
        }

        public async Task PublishSystemEventAsync(GameEvent gameEvent)
        {
            await PublishEventAsync(GameStreamNamespaces.SystemEvents, gameEvent);
        }

        private async Task PublishEventAsync(string streamNamespace, GameEvent gameEvent)
        {
            try
            {
                var streamProvider = _clusterClient.GetStreamProvider(OrleansConst.CommonMessageStreamProvider);
                var streamId = StreamId.Create(streamNamespace, gameEvent.CharacterId.ToString());
                var stream = streamProvider.GetStream<GameEvent>(streamId);

                await stream.OnNextAsync(gameEvent);

                _logger.LogDebug("游戏事件已发布: {EventType} -> {Namespace}, CharacterId={CharacterId}, EventId={EventId}",
                    gameEvent.EventType, streamNamespace, gameEvent.CharacterId, gameEvent.EventId);
            }
            catch (Exception ex)
            {
                // 事件发布失败不应阻断主业务流程（非关键路径），仅记录错误日志
                _logger.LogError(ex, "发布游戏事件失败: {EventType}, Namespace={Namespace}, CharacterId={CharacterId}",
                    gameEvent.EventType, streamNamespace, gameEvent.CharacterId);
            }
        }
    }
}
