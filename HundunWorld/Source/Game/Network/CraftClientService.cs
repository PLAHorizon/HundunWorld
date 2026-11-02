using System;
using FlaxEngine;
using Horizon.Game.Message.Network;
using ManagedHundunWorld.Network.Handlers;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 制作客户端服务：清单 / 学配方 / 制作。
    /// <para>与商店、任务同一套纪律：回包在连接线程写缓存，页面靠 <see cref="Revision"/> 脏标记重绘；
    /// 价格、材料与成败全由服务端裁决，本地不预扣材料。</para>
    /// </summary>
    public sealed class CraftClientService
    {
        /// <summary>全局单例。</summary>
        public static CraftClientService Instance { get; } = new CraftClientService();

        private readonly object _gate = new object();
        private CraftMessageHandler _handler;
        private CraftingRecipe[] _recipes = Array.Empty<CraftingRecipe>();
        private int _revision;
        private string _lastNotice = string.Empty;
        private bool _pending;

        /// <summary>下一页制作页应绑定的 NPC/设施实体 id（交互入口写入，页面取走后清零）。</summary>
        private ulong _requestedVendorEntity;

        private CraftClientService() { }

        public int Revision { get { lock (_gate) return _revision; } }

        /// <summary>当前 NPC/设施提供的配方（服务端算好的清单）。</summary>
        public CraftingRecipe[] Recipes { get { lock (_gate) return (CraftingRecipe[])_recipes.Clone(); } }

        public string LastNotice { get { lock (_gate) return _lastNotice; } }

        public bool Pending { get { lock (_gate) return _pending; } }

        public void EnsureAttached()
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            var handler = nm?.GetHandler<CraftMessageHandler>();
            if (handler == null || ReferenceEquals(handler, _handler)) return;

            if (_handler != null) _handler.Responded -= OnResponded;

            _handler = handler;
            _handler.Responded += OnResponded;
        }

        /// <summary>
        /// 拉取"这位 NPC 教什么 / 这个设施能做什么"，并记下"下一页制作页要绑定谁"。
        /// <para>分开记是因为路由会复用页面实例：页面在自己的 Update 里取走这个值再绑，
        /// 首次打开与复用旧页面走同一条路（与商店页同一条纪律）。</para>
        /// </summary>
        public bool RequestList(ulong entityId)
        {
            lock (_gate) _requestedVendorEntity = entityId;
            return Send(entityId, recipeId: 0, action: 2);
        }

        /// <summary>取走待绑定实体（取一次即清；0 = 没有待绑定）。</summary>
        public ulong TakeRequestedVendor()
        {
            lock (_gate)
            {
                var value = _requestedVendorEntity;
                _requestedVendorEntity = 0;
                return value;
            }
        }

        /// <summary>向训练师学配方。</summary>
        public bool Learn(ulong entityId, int recipeId) => Send(entityId, recipeId, action: 1);

        /// <summary>在设施旁制作。</summary>
        public bool Craft(ulong entityId, int recipeId) => Send(entityId, recipeId, action: 0);

        private bool Send(ulong entityId, int recipeId, int action)
        {
            EnsureAttached();
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || entityId == 0) return false;

            lock (_gate) _pending = true;
            _ = nm.SendAsync(new CraftingRequest
            {
                CharacterId = game.PlayerId,
                RecipeId = recipeId,
                EntityId = entityId,
                Action = action,
                Count = 1,
            });
            return true;
        }

        private void OnResponded(CraftingResponse response)
        {
            if (response == null) return;

            lock (_gate)
            {
                _pending = false;

                // 清单回执带 Recipes；学/做回执带 Success + Message。两类共用同一个缓存。
                if (response.Recipes != null && response.Recipes.Count > 0) _recipes = response.Recipes.ToArray();
                _lastNotice = response.Success
                    ? response.Message
                    : "失败：" + response.Message;
                _revision++;
            }
        }
    }
}
