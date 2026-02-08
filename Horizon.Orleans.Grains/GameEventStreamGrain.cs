using Horizon.Core.Abstract;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 游戏事件流管理Grain — 管理事件流的订阅和分发
    /// Key格式: "{namespace}" (如 "CharacterEvents", "CombatEvents")
    /// </summary>
    public class GameEventStreamGrain : Grain, IGameEventStreamGrain
    {
        private readonly ILogger<GameEventStreamGrain> _logger;

        private int _subscriberCount;
        private long _totalEventsPublished;
        private DateTime _lastEventTime;
        private bool _isActive;

        public GameEventStreamGrain(ILogger<GameEventStreamGrain> logger)
        {
            _logger = logger;
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _isActive = true;
            _logger.LogInformation("事件流管理Grain已激活: {Namespace}", this.GetPrimaryKeyString());
            return base.OnActivateAsync(cancellationToken);
        }

        public Task<int> GetSubscriberCountAsync()
        {
            return Task.FromResult(_subscriberCount);
        }

        public Task<EventStreamStatus> GetStreamStatusAsync()
        {
            var status = new EventStreamStatus
            {
                Namespace = this.GetPrimaryKeyString(),
                IsActive = _isActive,
                TotalEventsPublished = _totalEventsPublished,
                SubscriberCount = _subscriberCount,
                LastEventTime = _lastEventTime
            };
            return Task.FromResult(status);
        }

        public async Task PublishEventAsync(GameEvent gameEvent)
        {
            try
            {
                var streamNamespace = this.GetPrimaryKeyString();
                var streamProvider = this.GetStreamProvider(OrleansConst.CommonMessageStreamProvider);
                var streamId = StreamId.Create(streamNamespace, gameEvent.CharacterId.ToString());
                var stream = streamProvider.GetStream<GameEvent>(streamId);

                await stream.OnNextAsync(gameEvent);

                _totalEventsPublished++;
                _lastEventTime = DateTime.UtcNow;

                _logger.LogDebug("事件流Grain发布事件: {EventType} -> {Namespace}, CharacterId={CharacterId}",
                    gameEvent.EventType, streamNamespace, gameEvent.CharacterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "事件流Grain发布事件失败: {EventType}, Namespace={Namespace}",
                    gameEvent.EventType, this.GetPrimaryKeyString());
            }
        }
    }
}
