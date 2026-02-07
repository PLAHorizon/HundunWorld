using Horizon.Game.Message.Network;
using HundunWorld.Game.Network;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Worlds
{
    /// <summary>
    /// 事件广播器，负责游戏事件的广播和处理
    /// </summary>
    public class EventBroadcaster
    {
        private readonly NetworkManager _networkManager;
        private readonly Dictionary<WorldEventType, List<Action<WorldEvent>>> _eventHandlers;
        private readonly object _lockObject = new object();

        public EventBroadcaster(NetworkManager networkManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _eventHandlers = new Dictionary<WorldEventType, List<Action<WorldEvent>>>();
            
          
        }

        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <param name="eventType">事件类型</param>
        /// <param name="handler">事件处理函数</param>
        public void Subscribe(WorldEventType eventType, Action<WorldEvent> handler)
        {
            lock (_lockObject)
            {
                if (!_eventHandlers.ContainsKey(eventType))
                {
                    _eventHandlers[eventType] = new List<Action<WorldEvent>>();
                }
                
                _eventHandlers[eventType].Add(handler);
            }
        }

        /// <summary>
        /// 取消订阅事件
        /// </summary>
        /// <param name="eventType">事件类型</param>
        /// <param name="handler">事件处理函数</param>
        public void Unsubscribe(WorldEventType eventType, Action<WorldEvent> handler)
        {
            lock (_lockObject)
            {
                if (_eventHandlers.ContainsKey(eventType))
                {
                    _eventHandlers[eventType].Remove(handler);
                }
            }
        }

        /// <summary>
        /// 广播事件到本地监听器
        /// </summary>
        /// <param name="worldEvent">世界事件</param>
        public void BroadcastEventLocally(WorldEvent worldEvent)
        {
            List<Action<WorldEvent>> handlers = null;
            
            lock (_lockObject)
            {
                if (_eventHandlers.ContainsKey(worldEvent.EventType))
                {
                    handlers = new List<Action<WorldEvent>>(_eventHandlers[worldEvent.EventType]);
                }
            }
            
            // 调用所有处理函数
            if (handlers != null)
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler(worldEvent);
                    }
                    catch (Exception ex)
                    {
                        // 记录处理异常，但不中断其他处理函数
                        Console.WriteLine($"事件处理异常: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 发送事件到服务器
        /// </summary>
        /// <param name="worldEvent">世界事件</param>
        public async Task BroadcastEventToServerAsync(WorldEvent worldEvent)
        {
            // 构造事件消息并发送到服务器
            // 由于缺少具体的消息定义，这里只是一个示例
            /*
            var message = new HorizonMessagePacket
            {
                ServiceType = ServiceType.World,
                Header = new MessageHeader
                {
                    MessageType = MessageType.WorldEvent,
                },
                Body = new MessageUnion { WorldEvent = worldEvent }
            };
            
            await _networkManager.SendMessageAsync(message);
            */
        }

        /// <summary>
        /// 处理来自服务器的网络消息
        /// </summary>
        private void OnNetworkMessageReceived(HorizonMessagePacket message)
        {
            // 处理来自服务器的事件广播消息
            /*
            if (message.Header.MessageType == MessageType.WorldEvent)
            {
                WorldEvent worldEvent = message.Body.WorldEvent;
                BroadcastEventLocally(worldEvent);
            }
            */
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
                      
            // 清理事件处理函数
            lock (_lockObject)
            {
                _eventHandlers.Clear();
            }
        }
    }
}