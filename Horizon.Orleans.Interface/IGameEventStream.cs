using MemoryPack;
using Orleans;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 游戏事件流观察者接口 — 接收Orleans Stream中的游戏事件通知
    /// 实现此接口的组件可以订阅并处理特定命名空间的游戏事件
    /// </summary>
    public interface IGameEventObserver
    {
        /// <summary>
        /// 接收游戏事件通知
        /// </summary>
        /// <param name="gameEvent">游戏事件数据</param>
        Task OnGameEventReceivedAsync(GameEvent gameEvent);

        /// <summary>
        /// 事件流发生错误时回调
        /// </summary>
        /// <param name="ex">异常信息</param>
        Task OnErrorAsync(Exception ex);
    }

    /// <summary>
    /// 游戏事件流管理Grain接口 — 管理事件流的订阅和分发
    /// 用于注册/注销事件观察者，查询事件流状态
    /// Key格式: "{namespace}" (如 "CharacterEvents", "CombatEvents" 等)
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IGameEventStreamGrain : IGrainWithStringKey
    {
        /// <summary>
        /// 获取当前命名空间的活跃订阅者数量
        /// </summary>
        Task<int> GetSubscriberCountAsync();

        /// <summary>
        /// 获取事件流状态信息（命名空间、是否活跃、总发布事件数）
        /// </summary>
        Task<EventStreamStatus> GetStreamStatusAsync();

        /// <summary>
        /// 向指定角色发布游戏事件到流
        /// </summary>
        /// <param name="gameEvent">要发布的游戏事件</param>
        Task PublishEventAsync(GameEvent gameEvent);
    }

    /// <summary>
    /// 事件流状态信息
    /// </summary>
    [MemoryPackable(SerializeLayout.Explicit)]
    [GenerateSerializer]
    [Serializable]
    public partial class EventStreamStatus
    {
        /// <summary>事件流命名空间</summary>
        [MemoryPackOrder(0)]
        [Id(0)]
        public string Namespace { get; set; } = "";

        /// <summary>流是否活跃</summary>
        [MemoryPackOrder(1)]
        [Id(1)]
        public bool IsActive { get; set; }

        /// <summary>总发布事件数</summary>
        [MemoryPackOrder(2)]
        [Id(2)]
        public long TotalEventsPublished { get; set; }

        /// <summary>活跃订阅者数量</summary>
        [MemoryPackOrder(3)]
        [Id(3)]
        public int SubscriberCount { get; set; }

        /// <summary>最后事件发布时间</summary>
        [MemoryPackOrder(4)]
        [Id(4)]
        public DateTime LastEventTime { get; set; }
    }
}
