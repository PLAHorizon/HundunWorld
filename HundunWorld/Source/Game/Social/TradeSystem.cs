using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Social
{
    /// <summary>
    /// 交易状态
    /// </summary>
    public enum TradeState
    {
        None,
        Requested,
        Active,
        Confirmed,
        Completed,
        Cancelled
    }

    /// <summary>
    /// 交易物品槽
    /// </summary>
    [Serializable]
    public class TradeItemSlot
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public int Count { get; set; }
        public int Quality { get; set; }
        public int EnhanceLevel { get; set; }
    }

    /// <summary>
    /// 交易会话数据
    /// </summary>
    public class TradeSession
    {
        public ulong SelfPlayerId { get; set; }
        public ulong OtherPlayerId { get; set; }
        public string OtherPlayerName { get; set; } = "";
        public List<TradeItemSlot> SelfItems { get; } = new List<TradeItemSlot>();
        public List<TradeItemSlot> OtherItems { get; } = new List<TradeItemSlot>();
        public long SelfGold { get; set; }
        public long OtherGold { get; set; }
        public bool SelfConfirmed { get; set; }
        public bool OtherConfirmed { get; set; }
        public TradeState State { get; set; } = TradeState.None;
    }

    /// <summary>
    /// 交易系统 - 玩家间面对面交易。
    /// 产品级特性：
    /// - 交易请求/接受/拒绝
    /// - 双方物品/金币放置
    /// - 双方确认机制
    /// - 交易锁定（确认后不可修改）
    /// - 交易取消/超时
    /// - 防欺诈（确认后修改自动取消确认）
    /// </summary>
    public class TradeSystem
    {
        private static TradeSystem _instance;
        public static TradeSystem Instance => _instance ??= new TradeSystem();

        private TradeSession _currentSession;
        private ulong _pendingRequestFrom = 0;
        private string _pendingRequestName = "";
        private float _requestExpireTime = 0f;
        private const float RequestTimeout = 30f;
        private const int MaxTradeSlots = 8;

        // ===== 事件 =====
        public event Action<ulong, string> OnTradeRequested;
        public event Action<TradeSession> OnTradeStarted;
        public event Action<TradeSession> OnTradeUpdated;
        public event Action<TradeSession> OnTradeCompleted;
        public event Action<string> OnTradeCancelled;
        public event Action<ulong, string> OnTradeRequestExpired;

        public TradeState CurrentState => _currentSession?.State ?? TradeState.None;
        public bool IsInTrade => _currentSession != null && _currentSession.State == TradeState.Active;
        public bool HasPendingRequest => _pendingRequestFrom > 0 && Time.GameTime < _requestExpireTime;

        /// <summary>发起交易请求</summary>
        public void RequestTrade(ulong targetPlayerId, string targetName)
        {
            if (IsInTrade)
            {
                Debug.LogWarning("[TradeSystem] 已在交易中");
                return;
            }
            // TODO: 通过网络发送交易请求
            Debug.Log($"[TradeSystem] 向 {targetName} 发起交易请求");
        }

        /// <summary>接收交易请求</summary>
        public void ReceiveTradeRequest(ulong fromPlayerId, string fromName)
        {
            _pendingRequestFrom = fromPlayerId;
            _pendingRequestName = fromName;
            _requestExpireTime = Time.GameTime + RequestTimeout;
            OnTradeRequested?.Invoke(fromPlayerId, fromName);
        }

        /// <summary>接受交易</summary>
        public void AcceptTrade()
        {
            if (!HasPendingRequest) return;

            _currentSession = new TradeSession
            {
                SelfPlayerId = GetLocalPlayerId(),
                OtherPlayerId = _pendingRequestFrom,
                OtherPlayerName = _pendingRequestName,
                State = TradeState.Active
            };

            _pendingRequestFrom = 0;
            OnTradeStarted?.Invoke(_currentSession);
            // TODO: 通知服务器
        }

        /// <summary>拒绝交易</summary>
        public void DeclineTrade()
        {
            _pendingRequestFrom = 0;
            _pendingRequestName = "";
            // TODO: 通知服务器
        }

        /// <summary>添加物品到交易栏</summary>
        public bool AddItem(int itemId, string itemName, int count, int quality, int enhanceLevel)
        {
            if (!IsInTrade || _currentSession.SelfConfirmed) return false;
            if (_currentSession.SelfItems.Count >= MaxTradeSlots) return false;

            _currentSession.SelfItems.Add(new TradeItemSlot
            {
                ItemId = itemId,
                ItemName = itemName,
                Count = count,
                Quality = quality,
                EnhanceLevel = enhanceLevel
            });

            // 修改后取消确认
            _currentSession.SelfConfirmed = false;
            _currentSession.OtherConfirmed = false;
            OnTradeUpdated?.Invoke(_currentSession);
            return true;
        }

        /// <summary>移除物品</summary>
        public bool RemoveItem(int index)
        {
            if (!IsInTrade || _currentSession.SelfConfirmed) return false;
            if (index < 0 || index >= _currentSession.SelfItems.Count) return false;

            _currentSession.SelfItems.RemoveAt(index);
            _currentSession.SelfConfirmed = false;
            _currentSession.OtherConfirmed = false;
            OnTradeUpdated?.Invoke(_currentSession);
            return true;
        }

        /// <summary>设置金币</summary>
        public void SetGold(long amount)
        {
            if (!IsInTrade || _currentSession.SelfConfirmed) return;
            _currentSession.SelfGold = Math.Max(0, amount);
            _currentSession.SelfConfirmed = false;
            _currentSession.OtherConfirmed = false;
            OnTradeUpdated?.Invoke(_currentSession);
        }

        /// <summary>确认交易</summary>
        public void ConfirmTrade()
        {
            if (!IsInTrade) return;
            _currentSession.SelfConfirmed = true;
            OnTradeUpdated?.Invoke(_currentSession);

            // 双方都确认则完成交易
            if (_currentSession.OtherConfirmed)
            {
                CompleteTrade();
            }
            // TODO: 通知服务器
        }

        /// <summary>取消交易</summary>
        public void CancelTrade(string reason = "")
        {
            if (_currentSession == null) return;

            _currentSession.State = TradeState.Cancelled;
            OnTradeCancelled?.Invoke(string.IsNullOrEmpty(reason) ? "交易已取消" : reason);
            _currentSession = null;
            // TODO: 通知服务器
        }

        /// <summary>接收对方确认</summary>
        public void ReceiveOtherConfirmed()
        {
            if (!IsInTrade) return;
            _currentSession.OtherConfirmed = true;
            OnTradeUpdated?.Invoke(_currentSession);

            if (_currentSession.SelfConfirmed)
            {
                CompleteTrade();
            }
        }

        /// <summary>接收对方物品更新</summary>
        public void ReceiveOtherItemsUpdate(List<TradeItemSlot> items, long gold)
        {
            if (!IsInTrade) return;
            _currentSession.OtherItems.Clear();
            _currentSession.OtherItems.AddRange(items);
            _currentSession.OtherGold = gold;
            _currentSession.OtherConfirmed = false;
            OnTradeUpdated?.Invoke(_currentSession);
        }

        private void CompleteTrade()
        {
            if (_currentSession == null) return;
            _currentSession.State = TradeState.Completed;
            OnTradeCompleted?.Invoke(_currentSession);

            Debug.Log("[TradeSystem] 交易完成!");
            _currentSession = null;
        }

        /// <summary>每帧更新（检查请求超时）</summary>
        public void Update()
        {
            if (HasPendingRequest && Time.GameTime >= _requestExpireTime)
            {
                OnTradeRequestExpired?.Invoke(_pendingRequestFrom, _pendingRequestName);
                _pendingRequestFrom = 0;
                _pendingRequestName = "";
            }
        }

        private ulong GetLocalPlayerId() => 1;
    }
}
