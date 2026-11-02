using System;
using System.Collections.Generic;
using FlaxEngine;
using Horizon.Game.Message.Network;
using ManagedHundunWorld.Network.Handlers;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 商人客户端服务：发开窗/买/卖请求，缓存服务端给的货架快照。
    /// <para>
    /// <b>与 <c>QuestClientService</c> 同一套纪律</b>：回包在连接线程回调，这里只写缓存与
    /// <see cref="Revision"/> 计数；页面在主线程发现计数变了再重绘，不直接碰 GUI。
    /// </para>
    /// <para>
    /// <b>价格与成败全由服务端决定</b>：本类不预扣钱、不自行判断"买得起吗"。
    /// 本地只做展示（把 <see cref="Items"/> 画成卡片），成交结果以回执为准。
    /// </para>
    /// </summary>
    public sealed class ShopClientService
    {
        /// <summary>全局单例。</summary>
        public static ShopClientService Instance { get; } = new ShopClientService();

        private readonly object _gate = new object();
        private ShopMessageHandler _handler;
        private ShopItemDto[] _items = Array.Empty<ShopItemDto>();
        private long _gold;
        private int _npcId;
        private int _revision;
        private string _lastNotice = string.Empty;
        private string _lastReason = string.Empty;
        private bool _pending;

        /// <summary>下一页商店应绑定的商人实体 id（交互入口写入，页面取走后清零）。</summary>
        private ulong _requestedVendorEntity;

        private ShopClientService() { }

        /// <summary>数据版本号（回执到达时 +1，页面据此重绘）。</summary>
        public int Revision { get { lock (_gate) return _revision; } }

        /// <summary>服务端认定的商人 NPC 编号（0 = 还没开过窗）。</summary>
        public int NpcId { get { lock (_gate) return _npcId; } }

        /// <summary>服务端给的当前金币。</summary>
        public long Gold { get { lock (_gate) return _gold; } }

        /// <summary>货架快照（只读副本）。</summary>
        public ShopItemDto[] Items { get { lock (_gate) return (ShopItemDto[])_items.Clone(); } }

        /// <summary>最近一次回执的可读文本（失败原因要能看见）。</summary>
        public string LastNotice { get { lock (_gate) return _lastNotice; } }

        /// <summary>最近一次失败短码（成功时为空串）。</summary>
        public string LastReason { get { lock (_gate) return _lastReason; } }

        /// <summary>是否有请求在途（防连点重复下单）。</summary>
        public bool Pending { get { lock (_gate) return _pending; } }

        /// <summary>惰性挂接网络处理器（幂等；重连后可再调一次）。</summary>
        public void EnsureAttached()
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            var handler = nm?.GetHandler<ShopMessageHandler>();
            if (handler == null || ReferenceEquals(handler, _handler)) return;

            if (_handler != null)
            {
                _handler.ShopOpened -= OnOpened;
                _handler.BuyResponded -= OnBought;
                _handler.SellResponded -= OnSold;
            }

            _handler = handler;
            _handler.ShopOpened += OnOpened;
            _handler.BuyResponded += OnBought;
            _handler.SellResponded += OnSold;
        }

        /// <summary>打开某个商人的窗口（entityId = 世界里那个 NPC 的运行时实体 id）。</summary>
        public bool Open(ulong entityId)
        {
            EnsureAttached();
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || entityId == 0) return false;

            lock (_gate) _pending = true;
            _ = nm.SendAsync(new ShopOpenRequestMessage { CharacterId = game.PlayerId, EntityId = entityId });
            return true;
        }

        /// <summary>
        /// 请求打开某个商人的窗口并记下"下一页要绑定谁"。
        /// <para>分开记是因为页面可能已经被路由缓存：页面在自己的 Update 里取走这个值再绑，
        /// 这样"第一次打开"和"复用一个旧页面"走同一条路。</para>
        /// </summary>
        public void RequestOpen(ulong entityId)
        {
            lock (_gate) _requestedVendorEntity = entityId;
            Open(entityId);
        }

        /// <summary>取走待绑定的商人实体（取一次即清；0 = 没有待绑定）。</summary>
        public ulong TakeRequestedVendor()
        {
            lock (_gate)
            {
                var value = _requestedVendorEntity;
                _requestedVendorEntity = 0;
                return value;
            }
        }

        /// <summary>买（按格子）。<paramref name="entityId"/> 必须是当前开着的那个商人。</summary>
        public bool Buy(ulong entityId, int slot, int count)
        {
            EnsureAttached();
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || entityId == 0 || count <= 0) return false;

            lock (_gate) _pending = true;
            _ = nm.SendAsync(new ShopBuyRequestMessage { CharacterId = game.PlayerId, EntityId = entityId, Slot = slot, Count = count });
            return true;
        }

        /// <summary>卖（按物品模板 id）。</summary>
        public bool Sell(ulong entityId, int itemId, int count)
        {
            EnsureAttached();
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || entityId == 0 || itemId <= 0 || count <= 0) return false;

            lock (_gate) _pending = true;
            _ = nm.SendAsync(new ShopSellRequestMessage { CharacterId = game.PlayerId, EntityId = entityId, ItemId = itemId, Count = count });
            return true;
        }

        // ────────────────────────── 回包（连接线程） ──────────────────────────

        private void OnOpened(ShopOpenResultMessage message)
        {
            if (message == null) return;

            lock (_gate)
            {
                _pending = false;
                _npcId = message.NpcId;
                _gold = message.Gold;
                _items = message.Items ?? Array.Empty<ShopItemDto>();
                _lastNotice = message.Ok
                    ? $"商人 #{message.NpcId}：{_items.Length} 件商品"
                    : "打开商人失败：" + message.Reason;
                _lastReason = message.Ok ? string.Empty : message.Reason ?? string.Empty;
                _revision++;
            }
        }

        private void OnBought(ShopBuyResultMessage message)
        {
            if (message == null) return;

            lock (_gate)
            {
                _pending = false;
                _gold = message.GoldAfter;
                _lastNotice = message.Ok
                    ? $"已购入 {message.Count} 件，花费 {message.TotalCost}"
                    : "购买失败：" + message.Reason;
                _lastReason = message.Ok ? string.Empty : message.Reason ?? string.Empty;
                _revision++;
            }
        }

        private void OnSold(ShopSellResultMessage message)
        {
            if (message == null) return;

            lock (_gate)
            {
                _pending = false;
                _gold = message.GoldAfter;
                _lastNotice = message.Ok
                    ? $"已卖出 {message.Count} 件，单价 {message.UnitPrice}"
                    : "卖出失败：" + message.Reason;
                _lastReason = message.Ok ? string.Empty : message.Reason ?? string.Empty;
                _revision++;
            }
        }
    }
}
