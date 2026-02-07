using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Network;
using HundunWorld.Game.UI.States;

namespace HundunWorld.Game.UI.Events
{
    /// <summary>
    /// 事件处理器委托
    /// </summary>
    /// <typeparam name="T">事件类型</typeparam>
    /// <param name="eventData">事件数据</param>
    public delegate void EventHandler<T>(T eventData) where T : UIEvent;

    /// <summary>
    /// 异步事件处理器委托
    /// </summary>
    /// <typeparam name="T">事件类型</typeparam>
    /// <param name="eventData">事件数据</param>
    /// <returns>异步任务</returns>
    public delegate Task AsyncEventHandler<T>(T eventData) where T : UIEvent;

    /// <summary>
    /// 事件过滤器委托
    /// </summary>
    /// <typeparam name="T">事件类型</typeparam>
    /// <param name="eventData">事件数据</param>
    /// <returns>是否允许事件传递</returns>
    public delegate bool EventFilter<T>(T eventData) where T : UIEvent;

    /// <summary>
    /// 事件订阅信息
    /// </summary>
    internal class EventSubscription
    {
        public string SubscriptionId { get; } = Guid.NewGuid().ToString();
        public Type EventType { get; set; }
        public Delegate Handler { get; set; }
        public Delegate Filter { get; set; }
        public int Priority { get; set; }
        public bool IsAsync { get; set; }
        public bool IsOneTime { get; set; }
        public string SubscriberName { get; set; }
        public DateTime CreatedTime { get; } = DateTime.UtcNow;
    }

    /// <summary>
    /// UI事件总线
    /// 提供解耦的事件发布订阅机制，支持同步和异步事件处理
    /// </summary>
    public class UIEventBus
    {
        private static UIEventBus _instance;
        private static readonly object _lock = new object();

        // 事件订阅集合 - 使用并发字典确保线程安全
        private readonly ConcurrentDictionary<Type, List<EventSubscription>> _subscriptions = 
            new ConcurrentDictionary<Type, List<EventSubscription>>();

        // 事件队列 - 用于异步事件处理
        private readonly ConcurrentQueue<UIEvent> _eventQueue = new ConcurrentQueue<UIEvent>();

        // 全局事件过滤器
        private readonly List<Func<UIEvent, bool>> _globalFilters = new List<Func<UIEvent, bool>>();

        // 事件处理统计
        private readonly ConcurrentDictionary<Type, long> _eventCounts = new ConcurrentDictionary<Type, long>();
        private readonly ConcurrentDictionary<string, long> _handlerExecutionCounts = new ConcurrentDictionary<string, long>();

        // 配置参数
        public bool IsEnabled { get; set; } = true;
        public bool LogEvents { get; set; } = true;
        public int MaxQueueSize { get; set; } = 1000;
        public int MaxExecutionTimeMs { get; set; } = 5000;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static UIEventBus Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new UIEventBus();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 私有构造函数
        /// </summary>
        private UIEventBus()
        {
            FlaxEngine.Debug.Log("UIEventBus 已初始化");
        }

        /// <summary>
        /// 订阅事件（同步处理）
        /// </summary>
        /// <typeparam name="T">事件类型</typeparam>
        /// <param name="handler">事件处理器</param>
        /// <param name="priority">优先级（数值越大优先级越高）</param>
        /// <param name="filter">事件过滤器</param>
        /// <param name="subscriberName">订阅者名称</param>
        /// <param name="isOneTime">是否为一次性订阅</param>
        /// <returns>订阅ID</returns>
        public string Subscribe<T>(EventHandler<T> handler, int priority = 0, EventFilter<T> filter = null, 
            string subscriberName = "", bool isOneTime = false) where T : UIEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var subscription = new EventSubscription
            {
                EventType = typeof(T),
                Handler = handler,
                Filter = filter,
                Priority = priority,
                IsAsync = false,
                IsOneTime = isOneTime,
                SubscriberName = subscriberName
            };

            AddSubscription(subscription);

            if (LogEvents)
            {
                FlaxEngine.Debug.Log($"[EventBus] 订阅事件: {typeof(T).Name}, 订阅者: {subscriberName}, ID: {subscription.SubscriptionId}");
            }

            return subscription.SubscriptionId;
        }

        /// <summary>
        /// 订阅事件（异步处理）
        /// </summary>
        /// <typeparam name="T">事件类型</typeparam>
        /// <param name="handler">异步事件处理器</param>
        /// <param name="priority">优先级</param>
        /// <param name="filter">事件过滤器</param>
        /// <param name="subscriberName">订阅者名称</param>
        /// <param name="isOneTime">是否为一次性订阅</param>
        /// <returns>订阅ID</returns>
        public string SubscribeAsync<T>(AsyncEventHandler<T> handler, int priority = 0, EventFilter<T> filter = null,
            string subscriberName = "", bool isOneTime = false) where T : UIEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var subscription = new EventSubscription
            {
                EventType = typeof(T),
                Handler = handler,
                Filter = filter,
                Priority = priority,
                IsAsync = true,
                IsOneTime = isOneTime,
                SubscriberName = subscriberName
            };

            AddSubscription(subscription);

            if (LogEvents)
            {
                FlaxEngine.Debug.Log($"[EventBus] 异步订阅事件: {typeof(T).Name}, 订阅者: {subscriberName}, ID: {subscription.SubscriptionId}");
            }

            return subscription.SubscriptionId;
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        /// <param name="subscriptionId">订阅ID</param>
        /// <returns>是否成功取消</returns>
        public bool Unsubscribe(string subscriptionId)
        {
            if (string.IsNullOrEmpty(subscriptionId)) return false;

            foreach (var subscriptionList in _subscriptions.Values)
            {
                lock (subscriptionList)
                {
                    var subscription = subscriptionList.FirstOrDefault(s => s.SubscriptionId == subscriptionId);
                    if (subscription != null)
                    {
                        subscriptionList.Remove(subscription);
                        
                        if (LogEvents)
                        {
                            FlaxEngine.Debug.Log($"[EventBus] 取消订阅: {subscription.EventType.Name}, ID: {subscriptionId}");
                        }
                        
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 取消指定订阅者的所有订阅
        /// </summary>
        /// <param name="subscriberName">订阅者名称</param>
        /// <returns>取消的订阅数量</returns>
        public int UnsubscribeAll(string subscriberName)
        {
            if (string.IsNullOrEmpty(subscriberName)) return 0;

            int unsubscribedCount = 0;

            foreach (var subscriptionList in _subscriptions.Values)
            {
                lock (subscriptionList)
                {
                    var toRemove = subscriptionList.Where(s => s.SubscriberName == subscriberName).ToList();
                    foreach (var subscription in toRemove)
                    {
                        subscriptionList.Remove(subscription);
                        unsubscribedCount++;
                    }
                }
            }

            if (LogEvents && unsubscribedCount > 0)
            {
                FlaxEngine.Debug.Log($"[EventBus] 取消订阅者 {subscriberName} 的 {unsubscribedCount} 个订阅");
            }

            return unsubscribedCount;
        }

        /// <summary>
        /// 发布事件（同步）
        /// </summary>
        /// <typeparam name="T">事件类型</typeparam>
        /// <param name="eventData">事件数据</param>
        public void Publish<T>(T eventData) where T : UIEvent
        {
            if (!IsEnabled || eventData == null) return;

            // 应用全局过滤器
            if (!ApplyGlobalFilters(eventData)) return;

            // 更新统计
            _eventCounts.AddOrUpdate(typeof(T), 1, (key, value) => value + 1);

            if (LogEvents)
            {
                FlaxEngine.Debug.Log($"[EventBus] 发布事件: {typeof(T).Name}, ID: {eventData.EventId}");
            }

            var eventType = typeof(T);
            if (_subscriptions.TryGetValue(eventType, out var subscriptions))
            {
                ExecuteHandlers(eventData, subscriptions);
            }

            // 处理基类事件
            var baseType = eventType.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                if (_subscriptions.TryGetValue(baseType, out var baseSubscriptions))
                {
                    ExecuteHandlers(eventData, baseSubscriptions);
                }
                baseType = baseType.BaseType;
            }
        }

        /// <summary>
        /// 发布事件（异步）
        /// </summary>
        /// <typeparam name="T">事件类型</typeparam>
        /// <param name="eventData">事件数据</param>
        /// <returns>异步任务</returns>
        public async Task PublishAsync<T>(T eventData) where T : UIEvent
        {
            if (!IsEnabled || eventData == null) return;

            // 异步发布通过队列处理
            if (_eventQueue.Count < MaxQueueSize)
            {
                _eventQueue.Enqueue(eventData);
                await ProcessEventQueueAsync();
            }
            else
            {
                FlaxEngine.Debug.LogWarning($"[EventBus] 事件队列已满，丢弃事件: {typeof(T).Name}");
            }
        }

        /// <summary>
        /// 添加全局事件过滤器
        /// </summary>
        /// <param name="filter">过滤器函数</param>
        public void AddGlobalFilter(Func<UIEvent, bool> filter)
        {
            if (filter != null)
            {
                lock (_globalFilters)
                {
                    _globalFilters.Add(filter);
                }
            }
        }

        /// <summary>
        /// 移除全局事件过滤器
        /// </summary>
        /// <param name="filter">过滤器函数</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveGlobalFilter(Func<UIEvent, bool> filter)
        {
            if (filter != null)
            {
                lock (_globalFilters)
                {
                    return _globalFilters.Remove(filter);
                }
            }
            return false;
        }

        /// <summary>
        /// 获取事件统计信息
        /// </summary>
        /// <returns>事件统计字典</returns>
        public Dictionary<string, long> GetEventStatistics()
        {
            var stats = new Dictionary<string, long>();
            
            foreach (var kvp in _eventCounts)
            {
                stats[kvp.Key.Name] = kvp.Value;
            }

            return stats;
        }

        /// <summary>
        /// 获取订阅信息
        /// </summary>
        /// <returns>订阅信息列表</returns>
        public List<string> GetSubscriptionInfo()
        {
            var info = new List<string>();

            foreach (var kvp in _subscriptions)
            {
                var eventType = kvp.Key.Name;
                var subscriptions = kvp.Value;
                
                lock (subscriptions)
                {
                    info.Add($"{eventType}: {subscriptions.Count} 个订阅者");
                    foreach (var sub in subscriptions.OrderByDescending(s => s.Priority))
                    {
                        info.Add($"  - {sub.SubscriberName} (优先级: {sub.Priority}, 异步: {sub.IsAsync})");
                    }
                }
            }

            return info;
        }

        /// <summary>
        /// 清空所有订阅
        /// </summary>
        public void Clear()
        {
            _subscriptions.Clear();
            _globalFilters.Clear();
            _eventCounts.Clear();
            _handlerExecutionCounts.Clear();
            
            // 清空事件队列
            while (_eventQueue.TryDequeue(out _)) { }

            if (LogEvents)
            {
                FlaxEngine.Debug.Log("[EventBus] 已清空所有订阅和统计信息");
            }
        }

        #region 私有方法

        /// <summary>
        /// 添加订阅
        /// </summary>
        /// <param name="subscription">订阅信息</param>
        private void AddSubscription(EventSubscription subscription)
        {
            var subscriptions = _subscriptions.GetOrAdd(subscription.EventType, _ => new List<EventSubscription>());
            
            lock (subscriptions)
            {
                subscriptions.Add(subscription);
                // 按优先级排序
                subscriptions.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        /// <summary>
        /// 执行事件处理器
        /// </summary>
        /// <param name="eventData">事件数据</param>
        /// <param name="subscriptions">订阅列表</param>
        private void ExecuteHandlers(UIEvent eventData, List<EventSubscription> subscriptions)
        {
            var toRemove = new List<EventSubscription>();

            lock (subscriptions)
            {
                foreach (var subscription in subscriptions)
                {
                    try
                    {
                        // 检查事件是否被取消
                        if (eventData.IsCancelled) break;

                        // 应用事件过滤器
                        if (subscription.Filter != null)
                        {
                            var filterResult = (bool)subscription.Filter.DynamicInvoke(eventData);
                            if (!filterResult) continue;
                        }

                        // 执行处理器
                        if (subscription.IsAsync)
                        {
                            Task.Run(async () =>
                            {
                                try
                                {
                                    await (Task)subscription.Handler.DynamicInvoke(eventData);
                                    UpdateExecutionCount(subscription.SubscriptionId);
                                }
                                catch (Exception ex)
                                {
                                    FlaxEngine.Debug.LogError($"[EventBus] 异步事件处理器异常: {ex.Message}");
                                }
                            });
                        }
                        else
                        {
                            subscription.Handler.DynamicInvoke(eventData);
                            UpdateExecutionCount(subscription.SubscriptionId);
                        }

                        // 标记一次性订阅待移除
                        if (subscription.IsOneTime)
                        {
                            toRemove.Add(subscription);
                        }
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[EventBus] 事件处理器异常: {ex.Message}");
                    }
                }

                // 移除一次性订阅
                foreach (var subscription in toRemove)
                {
                    subscriptions.Remove(subscription);
                }
            }
        }

        /// <summary>
        /// 应用全局过滤器
        /// </summary>
        /// <param name="eventData">事件数据</param>
        /// <returns>是否允许传递</returns>
        private bool ApplyGlobalFilters(UIEvent eventData)
        {
            lock (_globalFilters)
            {
                foreach (var filter in _globalFilters)
                {
                    try
                    {
                        if (!filter(eventData)) return false;
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogError($"[EventBus] 全局过滤器异常: {ex.Message}");
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 处理事件队列（异步）
        /// </summary>
        /// <returns>异步任务</returns>
        private async Task ProcessEventQueueAsync()
        {
            while (_eventQueue.TryDequeue(out var eventData))
            {
                try
                {
                    Publish(eventData);
                    await Task.Delay(1); // 防止阻塞
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogError($"[EventBus] 处理队列事件异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 更新执行计数
        /// </summary>
        /// <param name="subscriptionId">订阅ID</param>
        private void UpdateExecutionCount(string subscriptionId)
        {
            _handlerExecutionCounts.AddOrUpdate(subscriptionId, 1, (key, value) => value + 1);
        }

        #endregion
    }
    
    #region 事件类型定义
    
    
   
    
   
    
    /// <summary>
    /// UI状态改变事件
    /// </summary>
    public class UIStateChangedEvent
    {
        public UIState OldState { get; set; }
        public UIState NewState { get; set; }
        public DateTime Timestamp { get; set; }
    }
    
    
    
    
    
   
    
    #endregion
}