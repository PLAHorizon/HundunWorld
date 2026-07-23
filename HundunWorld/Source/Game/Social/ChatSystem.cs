using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Social
{
    /// <summary>
    /// 聊天频道类型
    /// </summary>
    public enum ChatChannel
    {
        /// <summary>世界频道</summary>
        World = 0,
        /// <summary>区域频道</summary>
        Region = 1,
        /// <summary>队伍频道</summary>
        Team = 2,
        /// <summary>门派/公会频道</summary>
        Guild = 3,
        /// <summary>私聊</summary>
        Whisper = 4,
        /// <summary>系统公告</summary>
        System = 5,
        /// <summary>交易频道</summary>
        Trade = 6,
        /// <summary>附近频道</summary>
        Nearby = 7
    }

    /// <summary>
    /// 聊天消息
    /// </summary>
    [Serializable]
    public class ChatMessage
    {
        /// <summary>消息唯一ID</summary>
        public ulong MessageId { get; set; }

        /// <summary>频道</summary>
        public ChatChannel Channel { get; set; }

        /// <summary>发送者ID</summary>
        public ulong SenderId { get; set; }

        /// <summary>发送者名称</summary>
        public string SenderName { get; set; } = "";

        /// <summary>消息内容</summary>
        public string Content { get; set; } = "";

        /// <summary>发送时间戳</summary>
        public long Timestamp { get; set; }

        /// <summary>目标玩家ID（私聊用）</summary>
        public ulong TargetId { get; set; }

        /// <summary>目标玩家名称（私聊用）</summary>
        public string TargetName { get; set; } = "";

        /// <summary>是否为自己发送</summary>
        public bool IsSelf { get; set; }

        /// <summary>物品链接（嵌入物品信息）</summary>
        public List<ItemLink> ItemLinks { get; set; } = new List<ItemLink>();
    }

    /// <summary>
    /// 物品链接（聊天中嵌入的物品信息）
    /// </summary>
    [Serializable]
    public class ItemLink
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public int Quality { get; set; }
        public int EnhanceLevel { get; set; }
    }

    /// <summary>
    /// 聊天系统 - 管理所有聊天频道、消息历史、过滤。
    /// 产品级特性：
    /// - 多频道支持（世界/区域/队伍/门派/私聊/交易/附近）
    /// - 消息历史记录
    /// - 敏感词过滤
    /// - 频道冷却（防刷屏）
    /// - 物品/技能链接嵌入
    /// - 系统公告/跑马灯
    /// </summary>
    public class ChatSystem
    {
        private static ChatSystem _instance;
        public static ChatSystem Instance => _instance ??= new ChatSystem();

        // ===== 消息存储 =====
        private List<ChatMessage> _allMessages = new List<ChatMessage>();
        private Dictionary<ChatChannel, List<ChatMessage>> _channelMessages = new Dictionary<ChatChannel, List<ChatMessage>>();
        private List<ChatMessage> _whisperMessages = new List<ChatMessage>();
        private ulong _nextMessageId = 1;

        // ===== 配置 =====
        private const int MaxHistoryPerChannel = 200;
        private const int MaxTotalMessages = 1000;
        private Dictionary<ChatChannel, float> _channelCooldowns = new Dictionary<ChatChannel, float>();
        private Dictionary<ChatChannel, float> _lastSendTime = new Dictionary<ChatChannel, float>();
        private HashSet<string> _blockedPlayers = new HashSet<string>();
        private List<string> _filterWords = new List<string>();

        // ===== 事件 =====
        /// <summary>新消息事件</summary>
        public event Action<ChatMessage> OnMessageReceived;

        /// <summary>系统公告事件</summary>
        public event Action<string> OnSystemAnnouncement;

        /// <summary>私聊消息事件</summary>
        public event Action<ChatMessage> OnWhisperReceived;

        public ChatSystem()
        {
            foreach (ChatChannel ch in Enum.GetValues(typeof(ChatChannel)))
            {
                _channelMessages[ch] = new List<ChatMessage>();
            }

            // 频道冷却配置
            _channelCooldowns[ChatChannel.World] = 5f;
            _channelCooldowns[ChatChannel.Region] = 3f;
            _channelCooldowns[ChatChannel.Team] = 0.5f;
            _channelCooldowns[ChatChannel.Guild] = 1f;
            _channelCooldowns[ChatChannel.Whisper] = 0.5f;
            _channelCooldowns[ChatChannel.Trade] = 10f;
            _channelCooldowns[ChatChannel.Nearby] = 2f;

            InitializeFilter();
        }

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        public bool SendMessage(ChatChannel channel, string content, ulong targetId = 0, string targetName = "")
        {
            if (string.IsNullOrWhiteSpace(content)) return false;

            // 冷却检查
            if (!CheckCooldown(channel)) return false;

            // 敏感词过滤
            content = FilterContent(content);

            // 构建消息
            var msg = new ChatMessage
            {
                MessageId = _nextMessageId++,
                Channel = channel,
                SenderId = GetLocalPlayerId(),
                SenderName = GetLocalPlayerName(),
                Content = content,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TargetId = targetId,
                TargetName = targetName,
                IsSelf = true
            };

            // 本地存储
            StoreMessage(msg);
            _lastSendTime[channel] = Time.GameTime;

            // TODO: 通过网络发送到服务器
            // NetworkManager.SendChatMessage(msg);

            OnMessageReceived?.Invoke(msg);
            return true;
        }

        /// <summary>
        /// 接收来自服务器的消息
        /// </summary>
        public void ReceiveMessage(ChatMessage msg)
        {
            if (msg == null) return;

            // 屏蔽检查
            if (_blockedPlayers.Contains(msg.SenderName)) return;

            // 敏感词过滤
            msg.Content = FilterContent(msg.Content);

            StoreMessage(msg);

            if (msg.Channel == ChatChannel.Whisper && !msg.IsSelf)
            {
                OnWhisperReceived?.Invoke(msg);
            }
            if (msg.Channel == ChatChannel.System)
            {
                OnSystemAnnouncement?.Invoke(msg.Content);
            }

            OnMessageReceived?.Invoke(msg);
        }

        /// <summary>
        /// 获取指定频道的消息历史
        /// </summary>
        public List<ChatMessage> GetChannelHistory(ChatChannel channel, int count = 50)
        {
            if (!_channelMessages.TryGetValue(channel, out var messages)) return new List<ChatMessage>();
            return messages.TakeLast(count).ToList();
        }

        /// <summary>
        /// 获取私聊历史（与指定玩家）
        /// </summary>
        public List<ChatMessage> GetWhisperHistory(ulong playerId, int count = 50)
        {
            return _whisperMessages
                .Where(m => m.SenderId == playerId || m.TargetId == playerId)
                .TakeLast(count)
                .ToList();
        }

        /// <summary>
        /// 获取所有频道的最近消息（用于综合聊天窗口）
        /// </summary>
        public List<ChatMessage> GetRecentMessages(int count = 100)
        {
            return _allMessages.TakeLast(count).ToList();
        }

        // ===== 屏蔽/过滤 =====

        /// <summary>屏蔽玩家</summary>
        public void BlockPlayer(string playerName)
        {
            _blockedPlayers.Add(playerName);
        }

        /// <summary>取消屏蔽</summary>
        public void UnblockPlayer(string playerName)
        {
            _blockedPlayers.Remove(playerName);
        }

        /// <summary>是否被屏蔽</summary>
        public bool IsBlocked(string playerName) => _blockedPlayers.Contains(playerName);

        // ===== 内部方法 =====

        private void StoreMessage(ChatMessage msg)
        {
            _allMessages.Add(msg);
            if (_allMessages.Count > MaxTotalMessages)
                _allMessages.RemoveAt(0);

            if (_channelMessages.TryGetValue(msg.Channel, out var list))
            {
                list.Add(msg);
                if (list.Count > MaxHistoryPerChannel)
                    list.RemoveAt(0);
            }

            if (msg.Channel == ChatChannel.Whisper)
            {
                _whisperMessages.Add(msg);
                if (_whisperMessages.Count > MaxHistoryPerChannel)
                    _whisperMessages.RemoveAt(0);
            }
        }

        private bool CheckCooldown(ChatChannel channel)
        {
            if (!_channelCooldowns.TryGetValue(channel, out float cooldown)) return true;
            if (!_lastSendTime.TryGetValue(channel, out float lastTime)) return true;
            return Time.GameTime - lastTime >= cooldown;
        }

        private string FilterContent(string content)
        {
            foreach (var word in _filterWords)
            {
                if (content.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    content = content.Replace(word, new string('*', word.Length));
                }
            }
            return content;
        }

        private void InitializeFilter()
        {
            // 基础敏感词列表（实际应从服务器/配置加载）
            _filterWords.AddRange(new[] { "fuck", "shit", "damn" });
        }

        private ulong GetLocalPlayerId()
        {
            // TODO: 从本地玩家数据获取
            return 1;
        }

        private string GetLocalPlayerName()
        {
            // TODO: 从本地玩家数据获取
            return "玩家";
        }

        /// <summary>
        /// 创建物品链接文本
        /// </summary>
        public static string CreateItemLinkText(ItemLink link)
        {
            return $"[{link.ItemName}+{link.EnhanceLevel}]";
        }

        /// <summary>
        /// 获取频道显示名称
        /// </summary>
        public static string GetChannelDisplayName(ChatChannel channel) => channel switch
        {
            ChatChannel.World => "世界",
            ChatChannel.Region => "区域",
            ChatChannel.Team => "队伍",
            ChatChannel.Guild => "门派",
            ChatChannel.Whisper => "私聊",
            ChatChannel.System => "系统",
            ChatChannel.Trade => "交易",
            ChatChannel.Nearby => "附近",
            _ => "未知"
        };

        /// <summary>
        /// 获取频道颜色
        /// </summary>
        public static Color GetChannelColor(ChatChannel channel) => channel switch
        {
            ChatChannel.World => new Color(1f, 0.85f, 0.3f),     // 金色
            ChatChannel.Region => new Color(0.6f, 0.9f, 0.6f),   // 浅绿
            ChatChannel.Team => new Color(0.4f, 0.7f, 1f),       // 蓝色
            ChatChannel.Guild => new Color(0.7f, 0.4f, 1f),      // 紫色
            ChatChannel.Whisper => new Color(1f, 0.6f, 0.8f),    // 粉色
            ChatChannel.System => new Color(1f, 1f, 0.4f),       // 黄色
            ChatChannel.Trade => new Color(1f, 0.6f, 0.2f),      // 橙色
            ChatChannel.Nearby => new Color(0.8f, 0.8f, 0.8f),   // 灰白
            _ => Color.White
        };
    }
}
