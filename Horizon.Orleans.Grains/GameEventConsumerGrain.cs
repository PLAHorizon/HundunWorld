using Horizon.Core.Abstract;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using MemoryPack;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 游戏事件消费者Grain — 订阅Orleans Stream异步处理游戏事件
    /// 解耦战斗结果通知、日志统计等非关键路径的异步处理
    /// Key格式: "{namespace}:{characterId}" 或 "{namespace}:global"
    /// </summary>
    public class GameEventConsumerGrain : Grain<EventConsumerState>, IGameEventConsumerGrain
    {
        private readonly ILogger<GameEventConsumerGrain> _logger;
        private readonly IPersistentState<EventConsumerState> _consumerState;
        private StreamSubscriptionHandle<GameEvent>? _subscription;

        private const int MaxRecentEvents = 100;
        private const int StatePersistBatchSize = 10;
        private int _pendingWriteCount;

        public GameEventConsumerGrain(
            ILogger<GameEventConsumerGrain> logger,
            [PersistentState("eventConsumer", "GameStore")] IPersistentState<EventConsumerState> consumerState)
        {
            _logger = logger;
            _consumerState = consumerState;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            if (_consumerState.State.Stats == null)
                _consumerState.State.Stats = new EventProcessingStats();

            if (_consumerState.State.RecentEvents == null)
                _consumerState.State.RecentEvents = new List<ProcessedEventSummary>();

            await InitializeAsync();
            await base.OnActivateAsync(cancellationToken);
        }

        /// <summary>
        /// 初始化事件订阅 — 解析GrainKey确定订阅的命名空间和流ID
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var grainKey = this.GetPrimaryKeyString();
                var parts = grainKey.Split(':', 2);

                if (parts.Length < 2)
                {
                    _logger.LogWarning("无效的事件消费者GrainKey格式: {GrainKey}，预期格式为 namespace:streamKey", grainKey);
                    return;
                }

                var streamNamespace = parts[0];
                var streamKey = parts[1];

                _consumerState.State.Stats.Namespace = streamNamespace;

                if (_consumerState.State.Stats.StatsStartTimestamp == 0)
                    _consumerState.State.Stats.StatsStartTimestamp = DateTime.UtcNow.Ticks;

                var streamProvider = this.GetStreamProvider(OrleansConst.CommonMessageStreamProvider);
                var streamId = StreamId.Create(streamNamespace, streamKey);
                var stream = streamProvider.GetStream<GameEvent>(streamId);

                _subscription = await stream.SubscribeAsync(OnEventReceivedAsync);

                _logger.LogInformation("事件消费者已订阅流: Namespace={Namespace}, StreamKey={StreamKey}",
                    streamNamespace, streamKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化事件消费者订阅失败: {GrainKey}", this.GetPrimaryKeyString());
            }
        }

        /// <summary>
        /// 事件处理回调 — 接收并异步处理游戏事件
        /// </summary>
        private async Task OnEventReceivedAsync(GameEvent gameEvent, StreamSequenceToken? token)
        {
            try
            {
                _logger.LogDebug("收到游戏事件: {EventType}, EventId={EventId}, CharacterId={CharacterId}",
                    gameEvent.EventType, gameEvent.EventId, gameEvent.CharacterId);

                // 根据事件类型执行不同的处理逻辑
                ProcessEvent(gameEvent);

                // 更新统计
                UpdateStats(gameEvent, success: true);

                _logger.LogDebug("事件处理完成: {EventType}, EventId={EventId}", gameEvent.EventType, gameEvent.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理游戏事件失败: {EventType}, EventId={EventId}",
                    gameEvent.EventType, gameEvent.EventId);

                UpdateStats(gameEvent, success: false);
            }

            // 批量持久化：每处理StatePersistBatchSize个事件写入一次状态，减少I/O
            _pendingWriteCount++;
            if (_pendingWriteCount >= StatePersistBatchSize)
            {
                await _consumerState.WriteStateAsync();
                _pendingWriteCount = 0;
            }
        }

        /// <summary>
        /// 处理不同类型的事件 — 非关键路径异步处理（日志、统计）
        /// </summary>
        private void ProcessEvent(GameEvent gameEvent)
        {
            switch (gameEvent.EventType)
            {
                // 战斗事件 — 异步统计和日志
                case GameEventType.CombatDamageDealt:
                case GameEventType.CombatPlayerKill:
                case GameEventType.CombatPlayerDeath:
                case GameEventType.CombatPlayerResurrect:
                case GameEventType.CombatSkillCast:
                    _logger.LogInformation("战斗事件统计: {EventType}, 角色={CharacterId}, 描述={Description}",
                        gameEvent.EventType, gameEvent.CharacterId, gameEvent.Description);
                    break;

                // 角色事件 — 异步日志
                case GameEventType.CharacterLogin:
                case GameEventType.CharacterLogout:
                case GameEventType.CharacterLevelUp:
                case GameEventType.CharacterCreated:
                    _logger.LogInformation("角色事件记录: {EventType}, 角色={CharacterId}, 描述={Description}",
                        gameEvent.EventType, gameEvent.CharacterId, gameEvent.Description);
                    break;

                // 社交事件 — 异步通知
                case GameEventType.GuildCreated:
                case GameEventType.GuildMemberJoined:
                case GameEventType.TeamCreated:
                case GameEventType.FriendAdded:
                case GameEventType.TeamMemberJoined:
                case GameEventType.TeamMemberLeft:
                case GameEventType.TeamDisbanded:
                case GameEventType.TeamDungeonEntered:
                    _logger.LogInformation("社交事件通知: {EventType}, 角色={CharacterId}, 描述={Description}",
                        gameEvent.EventType, gameEvent.CharacterId, gameEvent.Description);
                    break;

                // 系统事件 — 异步监控
                case GameEventType.ServerStatusChanged:
                case GameEventType.ActivityStarted:
                case GameEventType.ActivityEnded:
                case GameEventType.DungeonCompleted:
                case GameEventType.QuestCompleted:
                    _logger.LogInformation("系统事件监控: {EventType}, 角色={CharacterId}, 描述={Description}",
                        gameEvent.EventType, gameEvent.CharacterId, gameEvent.Description);
                    break;

                default:
                    _logger.LogWarning("未知事件类型: {EventType}, EventId={EventId}",
                        gameEvent.EventType, gameEvent.EventId);
                    break;
            }
        }

        /// <summary>
        /// 更新事件处理统计
        /// </summary>
        private void UpdateStats(GameEvent gameEvent, bool success)
        {
            var stats = _consumerState.State.Stats;
            stats.TotalEventsProcessed++;
            stats.LastProcessedTimestamp = DateTime.UtcNow.Ticks;

            var typeKey = (int)gameEvent.EventType;
            if (!stats.EventTypeCounters.ContainsKey(typeKey))
                stats.EventTypeCounters[typeKey] = 0;
            stats.EventTypeCounters[typeKey]++;

            if (!success)
                stats.FailedEvents++;

            // 添加到最近事件列表
            var summary = new ProcessedEventSummary
            {
                EventId = gameEvent.EventId,
                EventType = gameEvent.EventType,
                CharacterId = gameEvent.CharacterId,
                ProcessedTimestamp = DateTime.UtcNow.Ticks,
                Success = success,
                Description = gameEvent.Description
            };

            _consumerState.State.RecentEvents.Add(summary);

            // 保持最近事件列表不超过最大限制
            if (_consumerState.State.RecentEvents.Count > MaxRecentEvents)
            {
                _consumerState.State.RecentEvents.RemoveRange(0,
                    _consumerState.State.RecentEvents.Count - MaxRecentEvents);
            }
        }

        public Task<EventProcessingStats> GetProcessingStatsAsync()
        {
            return Task.FromResult(_consumerState.State.Stats);
        }

        public Task<List<ProcessedEventSummary>> GetRecentEventsAsync(int count = 20)
        {
            var events = _consumerState.State.RecentEvents;
            int skip = Math.Max(0, events.Count - count);
            var result = events.Skip(skip).Take(count).ToList();
            return Task.FromResult(result);
        }

        public async Task ResetStatsAsync()
        {
            _consumerState.State.Stats = new EventProcessingStats
            {
                Namespace = _consumerState.State.Stats.Namespace,
                StatsStartTimestamp = DateTime.UtcNow.Ticks
            };
            _consumerState.State.RecentEvents.Clear();

            await _consumerState.WriteStateAsync();

            _logger.LogInformation("事件消费者统计已重置: {GrainKey}", this.GetPrimaryKeyString());
        }

        public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            // 持久化未写入的挂起状态
            if (_pendingWriteCount > 0)
            {
                await _consumerState.WriteStateAsync();
                _pendingWriteCount = 0;
            }

            if (_subscription != null)
            {
                await _subscription.UnsubscribeAsync();
                _logger.LogInformation("事件消费者已取消订阅: {GrainKey}", this.GetPrimaryKeyString());
            }

            await base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}
