using FlaxEngine;
using Game.Character.Attributes;
using Horizon.Game.Message.Network;
using ManagedHundunWorld.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Chat
{
    /// <summary>一条可在 UI 中展示的聊天行（频道使用协议枚举）。</summary>
    public struct ChatLine
    {
        public ChatChannel Channel;
        /// <summary>服务器消息ID（撤回/举报定位用；0 表示无）。</summary>
        public long MessageId;
        public ulong SenderId;
        public string SenderName;
        public int SenderLevel;
        public ulong ReceiverId;
        public string Content;
        public long Timestamp;
        public bool IsSystem;
    }

    /// <summary>
    /// 游戏内聊天客户端：订阅网络层 <see cref="ChatMessageHandler"/> 的聊天事件，按频道缓存消息，
    /// 并提供发送/历史请求。Ink 聊天框（CombatHudTraditionalPage）在其 Update 中读取本服务渲染。
    /// <para>
    /// 独立命名空间 <c>HundunWorld.Game.Chat</c>：避免与 <c>HundunWorld.Game.Social.ChatSystem</c>
    /// 的同名 ChatChannel/ChatMessage 发生命名空间级遮蔽（同命名空间的类型会遮蔽 using 别名）。
    /// </para>
    /// </summary>
    public sealed class ChatClient
    {
        public static ChatClient Instance { get; } = new ChatClient();

        private const int MaxPerChannel = 200;

        private readonly object _gate = new object();
        private readonly Dictionary<ChatChannel, List<ChatLine>> _byChannel = new Dictionary<ChatChannel, List<ChatLine>>();
        private readonly Dictionary<ChatChannel, int> _unread = new Dictionary<ChatChannel, int>();
        private readonly HashSet<ChatChannel> _muted = new HashSet<ChatChannel>();
        private ChatMessageHandler _handler;

        /// <summary>新增一条消息（网络线程触发，订阅者自行切线程）。</summary>
        public event Action<ChatLine> MessageAdded;

        private ChatClient() { }

        /// <summary>惰性挂接网络处理器（首次调用或重连后可再次调用；幂等）。</summary>
        public void EnsureAttached()
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            var handler = nm?.GetHandler<ChatMessageHandler>();
            if (handler == null || ReferenceEquals(handler, _handler))
                return;

            if (_handler != null)
            {
                _handler.ChatMessageReceived -= OnChatMessage;
                _handler.ChatNotifyReceived -= OnChatNotify;
                _handler.ChatHistoryReceived -= OnHistory;
                _handler.ChatRecallReceived -= OnRecall;
            }

            _handler = handler;
            _handler.ChatMessageReceived += OnChatMessage;
            _handler.ChatNotifyReceived += OnChatNotify;
            _handler.ChatHistoryReceived += OnHistory;
            _handler.ChatRecallReceived += OnRecall;
        }

        /// <summary>取指定频道消息快照（UI 线程安全）。</summary>
        public ChatLine[] GetMessages(ChatChannel channel)
        {
            lock (_gate)
            {
                return _byChannel.TryGetValue(channel, out var list)
                    ? list.ToArray()
                    : Array.Empty<ChatLine>();
            }
        }

        /// <summary>
        /// 清空本地消息缓存：<paramref name="channel"/> 为 null 时清空全部频道，否则仅清空该频道。
        /// 仅影响客户端显示，服务端历史不受影响（如需连带删除历史另加协议）。
        /// </summary>
        public void Clear(ChatChannel? channel = null)
        {
            lock (_gate)
            {
                if (channel == null)
                {
                    _byChannel.Clear();
                    _unread.Clear();
                }
                else
                {
                    _byChannel.Remove(channel.Value);
                    _unread[channel.Value] = 0;
                }
            }
        }

        /// <summary>未读数（该频道自上次已读以来的新消息数）。</summary>
        public int GetUnread(ChatChannel channel)
        {
            lock (_gate) return _unread.TryGetValue(channel, out var n) ? n : 0;
        }

        /// <summary>标记频道已读（清未读计数）。</summary>
        public void MarkRead(ChatChannel channel)
        {
            lock (_gate) _unread[channel] = 0;
        }

        /// <summary>是否存在未读（任一频道）。</summary>
        public bool HasAnyUnread
        {
            get { lock (_gate) return _unread.Values.Any(v => v > 0); }
        }

        /// <summary>频道是否被屏蔽（屏蔽后不入缓存、不计未读）。</summary>
        public bool IsMuted(ChatChannel channel)
        {
            lock (_gate) return _muted.Contains(channel);
        }

        /// <summary>切换频道屏蔽。</summary>
        public void ToggleMute(ChatChannel channel)
        {
            lock (_gate)
            {
                if (!_muted.Add(channel))
                    _muted.Remove(channel);
            }
        }

        /// <summary>取与指定玩家之间的密聊会话（收发双向）。</summary>
        public ChatLine[] GetWhisper(ulong peerId)
        {
            lock (_gate)
            {
                if (!_byChannel.TryGetValue(ChatChannel.Private, out var list))
                    return Array.Empty<ChatLine>();
                return list.Where(l => l.SenderId == peerId || l.ReceiverId == peerId).ToArray();
            }
        }

        /// <summary>发送一条聊天消息。</summary>
        public bool Send(ChatChannel channel, string content, ulong targetId = 0)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || string.IsNullOrWhiteSpace(content))
                return false;

            // 以 / 开头的是脚本指令：交给 WorldScriptClient 上行，不能当聊天正文发出去。
            var trimmed = content.Trim();
            if (trimmed.Length > 1 && trimmed[0] == '/')
                return WorldScriptClient.Instance.TryHandleCommand(trimmed);

            var req = new ChatSendMessage
            {
                SenderId = game.PlayerId,
                SenderName = ResolveLocalName(),
                Channel = channel,
                Content = trimmed,
                TargetId = targetId,
                Timestamp = DateTime.UtcNow.Ticks,
                ClientMsgId = Guid.NewGuid().ToString("N"),
            };
            _ = nm.SendAsync(req);
            return true;
        }

        /// <summary>请求某频道历史（密聊需传对端 <paramref name="peerId"/>）。</summary>
        public bool RequestHistory(ChatChannel channel, ulong peerId = 0, long beforeMessageId = 0, int count = 50)
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            if (nm == null) return false;
            _ = nm.SendAsync(new ChatHistoryRequest
            {
                CharacterId = HundunWorldGame.Instance.PlayerId,
                ChannelType = channel,
                PeerId = peerId,
                BeforeMessageId = beforeMessageId,
                Count = count,
            });
            return true;
        }

        private static string ResolveLocalName()
        {
            try
            {
                var actor = HundunWorldGame.Instance?.LocalPlayerActor;
                var attrs = actor?.GetScript<CharacterAttributesComponent>();
                if (attrs != null && !string.IsNullOrEmpty(attrs.Nickname))
                    return attrs.Nickname;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatClient] 解析本地昵称失败: {ex.Message}");
            }
            // 属性组件还没就绪时不要冒用设计稿名字：自己发的消息显示"我"。
            return "我";
        }

        private void OnChatMessage(ChatMessage m) => Add(new ChatLine
        {
            Channel = m.ChannelType,
            MessageId = ParseId(m.MessageId),
            SenderId = m.SenderId,
            SenderName = m.SenderName,
            ReceiverId = m.ReceiverId,
            Content = m.Content,
            Timestamp = m.Timestamp,
            IsSystem = m.IsSystemMessage,
        });

        private void OnChatNotify(ChatNotifyMessage n) => Add(new ChatLine
        {
            Channel = n.Channel,
            MessageId = ParseId(n.ChatMessageId),
            SenderId = n.SenderId,
            SenderName = n.SenderName,
            SenderLevel = n.SenderLevel,
            ReceiverId = n.ReceiverId,
            Content = n.Content,
            Timestamp = n.Timestamp,
            IsSystem = n.IsSystemMessage,
        });

        /// <summary>历史合并完成（页面据此结束"加载更早"状态并刷新）。</summary>
        public event Action HistoryMerged;

        /// <summary>追加一条本地系统提示（仅本机可见，不落库、不上行）。</summary>
        public void AddLocalSystem(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Add(new ChatLine
            {
                Channel = ChatChannel.System,
                SenderName = "系统",
                Content = text,
                Timestamp = DateTime.UtcNow.Ticks,
                IsSystem = true,
            });
        }

        /// <summary>历史响应：把更早的消息并入缓存（按 MessageId 去重，插到对应频道头部）。</summary>
        private void OnHistory(ChatHistoryResponse r)
        {
            if (r?.Messages == null) return;

            lock (_gate)
            {
                foreach (var m in r.Messages)
                {
                    long id = ParseId(m.MessageId);
                    if (!_byChannel.TryGetValue(m.ChannelType, out var list))
                    {
                        list = new List<ChatLine>();
                        _byChannel[m.ChannelType] = list;
                    }
                    if (id > 0 && list.Any(l => l.MessageId == id)) continue;

                    list.Insert(0, new ChatLine
                    {
                        Channel = m.ChannelType,
                        MessageId = id,
                        SenderId = m.SenderId,
                        SenderName = m.SenderName,
                        ReceiverId = m.ReceiverId,
                        Content = m.Content,
                        Timestamp = m.Timestamp,
                        IsSystem = m.IsSystemMessage,
                    });
                }
            }

            try { HistoryMerged?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning($"[ChatClient] HistoryMerged 订阅者异常: {ex.Message}"); }
        }

        /// <summary>撤回通知：从本地缓存移除对应消息。</summary>
        private void OnRecall(ChatRecallNotify n)
        {
            if (n == null || n.MessageId <= 0) return;
            lock (_gate)
            {
                if (_byChannel.TryGetValue(n.Channel, out var list))
                    list.RemoveAll(l => l.MessageId == n.MessageId);
            }
        }

        /// <summary>撤回自己的某条消息（服务端会校验发送者身份）。</summary>
        public bool Recall(ChatChannel channel, long messageId, ulong receiverId = 0)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || messageId <= 0) return false;
            _ = nm.SendAsync(new ChatRecallRequest
            {
                Channel = channel,
                MessageId = messageId,
                SenderId = game.PlayerId,
                ReceiverId = receiverId,
            });
            return true;
        }

        /// <summary>举报某条消息。</summary>
        public bool Report(ChatChannel channel, long messageId, string reason)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || messageId <= 0) return false;
            _ = nm.SendAsync(new ChatReportRequest
            {
                Channel = channel,
                MessageId = messageId,
                ReporterId = game.PlayerId,
                Reason = reason ?? string.Empty,
            });
            return true;
        }

        /// <summary>取某频道最早一条消息ID（上滚加载更早历史的游标；0 表示未知）。</summary>
        public long GetOldestMessageId(ChatChannel channel)
        {
            lock (_gate)
            {
                if (!_byChannel.TryGetValue(channel, out var list) || list.Count == 0) return 0;
                long min = long.MaxValue;
                foreach (var l in list)
                    if (l.MessageId > 0 && l.MessageId < min) min = l.MessageId;
                return min == long.MaxValue ? 0 : min;
            }
        }

        /// <summary>取与某玩家密聊中最早一条消息ID（游标；0 表示未知）。</summary>
        public long GetOldestWhisperId(ulong peerId)
        {
            lock (_gate)
            {
                if (!_byChannel.TryGetValue(ChatChannel.Private, out var list)) return 0;
                long min = long.MaxValue;
                foreach (var l in list)
                    if ((l.SenderId == peerId || l.ReceiverId == peerId) && l.MessageId > 0 && l.MessageId < min)
                        min = l.MessageId;
                return min == long.MaxValue ? 0 : min;
            }
        }

        private static long ParseId(string s)
            => long.TryParse(s, out var v) ? v : 0;

        private void Add(ChatLine line)
        {
            lock (_gate)
            {
                if (_muted.Contains(line.Channel))
                    return;   // 屏蔽频道：不入缓存、不计未读

                if (!_byChannel.TryGetValue(line.Channel, out var list))
                {
                    list = new List<ChatLine>();
                    _byChannel[line.Channel] = list;
                }
                list.Add(line);
                if (list.Count > MaxPerChannel)
                    list.RemoveRange(0, list.Count - MaxPerChannel);

                _unread[line.Channel] = (_unread.TryGetValue(line.Channel, out var n) ? n : 0) + 1;
            }

            try { MessageAdded?.Invoke(line); }
            catch (Exception ex) { Debug.LogWarning($"[ChatClient] MessageAdded 订阅者异常: {ex.Message}"); }
        }
    }
}
