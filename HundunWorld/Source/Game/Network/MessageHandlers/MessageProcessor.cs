using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 消息处理器接口
    /// </summary>
    public interface IMessageHandler
    {
         List<MessageType> MessageTypes { get; }

         ServiceType ServiceType { get; }
        /// <summary>
        /// 验证消息和路由确认
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        bool ValidateMessage(HorizonMessagePacket message);
        /// <summary>
        /// 是否能处理指定类型的消息
        /// </summary>
        /// <param name="messageType">消息类型</param>
        /// <returns>是否能处理</returns>
        bool CanHandle(MessageType messageType);

        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message">消息包</param>
        /// <returns>处理任务</returns>
        Task HandleAsync(HorizonMessagePacket message);
    }

    /// <summary>
    /// 消息处理器
    /// 负责根据消息类型分发到相应的处理器
    /// </summary>
    public class MessageProcessor
    {
        private readonly Dictionary<MessageType, List<IMessageHandler>> _handlers = new();

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        /// <param name="messageType">消息类型</param>
        /// <param name="handler">消息处理器</param>
        public void RegisterHandler(MessageType messageType, IMessageHandler handler)
        {
            if (!_handlers.ContainsKey(messageType))
            {
                _handlers[messageType] = new List<IMessageHandler>();
            }

            _handlers[messageType].Add(handler);
        }

        /// <summary>
        /// 注册消息处理器（泛型版本）
        /// </summary>
        /// <typeparam name="T">消息处理器类型</typeparam>
        /// <param name="messageType">消息类型</param>
        /// <param name="handler">消息处理器实例</param>
        public void RegisterHandler<T>(MessageType messageType, T handler) where T : IMessageHandler
        {
            RegisterHandler(messageType, (IMessageHandler)handler);
        }

        /// <summary>
        /// 处理消息
        /// </summary>
        /// <param name="message">消息包</param>
        /// <returns>处理任务</returns>
        public async Task ProcessMessageAsync(HorizonMessagePacket message)
        {
            if (message?.Header == null)
                return;

            var messageType = message.Header.MessageType;

            if (_handlers.ContainsKey(messageType))
            {
                foreach (var handler in _handlers[messageType])
                {
                    try
                    {
                        await handler.HandleAsync(message);
                    }
                    catch (Exception ex)
                    {
                        // 记录处理器错误，但不中断其他处理器的执行
                       FlaxEngine.Debug.LogError($"消息处理器执行错误: {ex.Message}");
                    }
                }
            }
            else
            {
                // 未找到对应的消息处理器
                FlaxEngine.Debug.Log($"未找到处理消息类型 {messageType} 的处理器");
            }
        }
    }
}