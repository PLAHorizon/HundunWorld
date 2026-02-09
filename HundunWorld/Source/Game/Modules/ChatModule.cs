using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Modules
{
    /// <summary>
    /// 聊天模块
    /// 管理聊天消息的本地存储、频道状态和消息发送接口
    /// </summary>
    public class ChatModule : BaseModule
    {
        /// <summary>
        /// 模块名称
        /// </summary>
        public override string Name => "ChatModule";

        /// <summary>
        /// 模块版本
        /// </summary>
        public override string Version => "1.0.0";

        /// <summary>
        /// 模块描述
        /// </summary>
        public override string Description => "聊天系统模块，管理聊天消息收发和频道状态";

        /// <summary>
        /// 每个频道最大消息缓存数
        /// </summary>
        public int MaxMessagesPerChannel { get; set; } = 200;

        /// <summary>
        /// 当前活动频道
        /// </summary>
        public ChatChannel ActiveChannel { get; private set; } = ChatChannel.World;

        /// <summary>
        /// 已加入的频道列表
        /// </summary>
        public IReadOnlyCollection<ChatChannel> JoinedChannels => _joinedChannels;

        private readonly Dictionary<ChatChannel, List<ChatMessage>> _messageHistory = new();
        private readonly HashSet<ChatChannel> _joinedChannels = new();

        /// <summary>
        /// 新消息到达事件
        /// </summary>
        public event Action<ChatChannel, ChatMessage> MessageReceived;

        /// <summary>
        /// 频道切换事件
        /// </summary>
        public event Action<ChatChannel> ChannelChanged;

        protected override void OnInitialize()
        {
            // 默认加入世界频道和系统频道
            _joinedChannels.Add(ChatChannel.World);
            _joinedChannels.Add(ChatChannel.System);

            // 初始化每个频道的消息历史
            foreach (ChatChannel channel in Enum.GetValues(typeof(ChatChannel)))
            {
                _messageHistory[channel] = new List<ChatMessage>();
            }

            Debug.Log("[ChatModule] 聊天模块已初始化");
        }

        protected override void OnStart()
        {
            Debug.Log("[ChatModule] 聊天模块已启动");
        }

        protected override void OnStop()
        {
            Debug.Log("[ChatModule] 聊天模块已停止");
        }

        protected override void OnDispose()
        {
            _messageHistory.Clear();
            _joinedChannels.Clear();
            Debug.Log("[ChatModule] 聊天模块已释放");
        }

        /// <summary>
        /// 添加收到的聊天消息到本地缓存
        /// </summary>
        /// <param name="message">聊天消息</param>
        public void AddMessage(ChatMessage message)
        {
            if (message == null) return;

            var channel = message.ChannelType;
            if (!_messageHistory.ContainsKey(channel))
            {
                _messageHistory[channel] = new List<ChatMessage>();
            }

            var messages = _messageHistory[channel];
            messages.Add(message);

            // 限制消息缓存数量
            if (messages.Count > MaxMessagesPerChannel)
            {
                messages.RemoveAt(0);
            }

            MessageReceived?.Invoke(channel, message);
        }

        /// <summary>
        /// 获取指定频道的消息历史
        /// </summary>
        /// <param name="channel">聊天频道</param>
        /// <param name="count">获取数量，0表示全部</param>
        /// <returns>消息列表</returns>
        public IReadOnlyList<ChatMessage> GetMessages(ChatChannel channel, int count = 0)
        {
            if (!_messageHistory.TryGetValue(channel, out var messages))
            {
                return Array.Empty<ChatMessage>();
            }

            if (count <= 0 || count >= messages.Count)
            {
                return messages.AsReadOnly();
            }

            return messages.Skip(messages.Count - count).ToList().AsReadOnly();
        }

        /// <summary>
        /// 切换活动频道
        /// </summary>
        /// <param name="channel">目标频道</param>
        public void SwitchChannel(ChatChannel channel)
        {
            if (ActiveChannel == channel) return;

            ActiveChannel = channel;
            ChannelChanged?.Invoke(channel);
            Debug.Log($"[ChatModule] 切换到频道: {channel}");
        }

        /// <summary>
        /// 加入频道
        /// </summary>
        /// <param name="channel">频道</param>
        /// <returns>是否成功加入</returns>
        public bool JoinChannel(ChatChannel channel)
        {
            if (_joinedChannels.Contains(channel))
            {
                return false;
            }

            _joinedChannels.Add(channel);
            Debug.Log($"[ChatModule] 加入频道: {channel}");
            return true;
        }

        /// <summary>
        /// 离开频道
        /// </summary>
        /// <param name="channel">频道</param>
        /// <returns>是否成功离开</returns>
        public bool LeaveChannel(ChatChannel channel)
        {
            // 不允许离开世界频道和系统频道
            if (channel == ChatChannel.World || channel == ChatChannel.System)
            {
                Debug.LogWarning($"[ChatModule] 不能离开{channel}频道");
                return false;
            }

            if (!_joinedChannels.Contains(channel))
            {
                return false;
            }

            _joinedChannels.Remove(channel);

            // 如果离开的是当前频道，切换到世界频道
            if (ActiveChannel == channel)
            {
                SwitchChannel(ChatChannel.World);
            }

            Debug.Log($"[ChatModule] 离开频道: {channel}");
            return true;
        }

        /// <summary>
        /// 清空指定频道的消息缓存
        /// </summary>
        /// <param name="channel">频道</param>
        public void ClearMessages(ChatChannel channel)
        {
            if (_messageHistory.TryGetValue(channel, out var messages))
            {
                messages.Clear();
            }
        }
    }
}
