using System;
using System.Collections.Generic;
using FlaxEngine;
using Horizon.Game.Message.Network;
using ManagedHundunWorld.Network.Handlers;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 好友客户端服务：向服务端要"我的好友名单"，缓存一份只读快照给 UI，并代发起删除。
    /// <para>
    /// 与 <see cref="QuestClientService"/> 同一套纪律：回包在连接线程回调，这里只写缓存与
    /// <see cref="Revision"/> 计数，页面在自己的 Update（主线程）发现计数变了才重绘——事件只当脏标记用。
    /// </para>
    /// <para>
    /// 名单成员、在线与否一律照抄服务端：<see cref="FriendInfo"/> 没带的字段（门派、战力）由界面显示占位，
    /// 这里不补任何猜测值。
    /// </para>
    /// </summary>
    public sealed class FriendClientService
    {
        /// <summary>全局单例（与 <c>ChatClient</c> / <c>QuestClientService</c> 同一条路子）。</summary>
        public static FriendClientService Instance { get; } = new FriendClientService();

        /// <summary>两次列表请求的最小间隔（秒）。打开页面只发一次，避免每次重绘都回源。</summary>
        private const float RequestThrottleSeconds = 2f;

        private readonly object _gate = new object();
        private List<FriendInfo> _friends = new List<FriendInfo>();
        private FriendMessageHandler _handler;
        private int _revision;
        private string _lastNotice = string.Empty;
        private double _lastRequestAt = -1d;

        private FriendClientService() { }

        /// <summary>数据版本号：任何一次服务端回包都会让它变，UI 据此决定是否重绘。</summary>
        public int Revision { get { lock (_gate) return _revision; } }

        /// <summary>最近一次好友操作的服务端回执文本（失败原因要能看见，别静默）。</summary>
        public string LastNotice { get { lock (_gate) return _lastNotice; } }

        /// <summary>已经拿到过服务端名单（false = 还没回包，界面应显示"同步中/空态"而不是假数据）。</summary>
        public bool HasServerData { get { lock (_gate) return _receivedOnce; } }

        private bool _receivedOnce;

        /// <summary>惰性挂接网络处理器（幂等；重连后可再调一次）。</summary>
        public void EnsureAttached()
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            var handler = nm?.GetHandler<FriendMessageHandler>();
            if (handler == null || ReferenceEquals(handler, _handler))
                return;

            if (_handler != null)
            {
                _handler.FriendListUpdated -= OnList;
                _handler.FriendStatusChanged -= OnStatus;
                _handler.FriendOperationResolved -= OnOperation;
            }

            _handler = handler;
            _handler.FriendListUpdated += OnList;
            _handler.FriendStatusChanged += OnStatus;
            _handler.FriendOperationResolved += OnOperation;
        }

        /// <summary>
        /// 拉好友名单。未登录（PlayerId==0）直接拒绝——没有身份就没有名单，不能拿假数据填界面。
        /// </summary>
        public bool RequestList(bool force = false)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || game.PlayerId == 0)
                return false;

            var now = Time.TimeSinceStartup;
            if (!force && _lastRequestAt >= 0 && now - _lastRequestAt < RequestThrottleSeconds)
                return true;        // 刚发过：算成功，避免调用方把它当失败去重试刷屏

            EnsureAttached();
            _lastRequestAt = now;
            _ = nm.SendAsync(new FriendListRequestMessage { CharacterId = game.PlayerId });
            return true;
        }

        /// <summary>删除好友（服务端落库；成功后回包会带新名单，不本地假删）。</summary>
        public bool RemoveFriend(ulong targetId)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || game.PlayerId == 0 || targetId == 0) return false;

            EnsureAttached();
            _ = nm.SendAsync(new RemoveFriendRequest { CharacterId = game.PlayerId, TargetId = targetId });
            return true;
        }

        /// <summary>取好友名单快照（UI 线程安全）。</summary>
        public FriendInfo[] GetFriends()
        {
            lock (_gate) return _friends.ToArray();
        }

        /// <summary>名单里是否一条都没有（含尚未收到回包的情况）。</summary>
        public bool IsEmpty
        {
            get { lock (_gate) return _friends.Count == 0; }
        }

        private void OnList(FriendListUpdateMessage message)
        {
            if (message == null) return;
            lock (_gate)
            {
                _friends = message.Friends != null
                    ? new List<FriendInfo>(message.Friends)
                    : new List<FriendInfo>();
                _receivedOnce = true;
                _revision++;
            }
        }

        private void OnStatus(FriendStatusUpdateMessage message)
        {
            if (message == null || message.FriendId == 0) return;
            lock (_gate)
            {
                foreach (var f in _friends)
                {
                    if (f == null || f.FriendId != message.FriendId) continue;

                    // 只照抄服务端给的在线位；LastLoginTime 没带就不自己编
                    f.IsOnline = message.IsOnline;
                    _revision++;
                    return;
                }
            }
        }

        private void OnOperation(FriendOperationResponse response)
        {
            if (response == null) return;
            Note(response.Success
                ? (response.Op == 2 ? "已删除好友" : "好友操作已受理")
                : "好友操作失败：" + response.Message);
            // 操作完回源要一份权威名单：本地不猜删除结果
            RequestList(force: true);
        }

        private void Note(string text)
        {
            lock (_gate) _lastNotice = text ?? string.Empty;
            Debug.Log("[FriendClientService] " + text);
        }
    }
}
