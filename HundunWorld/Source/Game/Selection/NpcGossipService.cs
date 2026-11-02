using System;
using System.Collections.Generic;

namespace HundunWorld.Game.Selection
{
    /// <summary>
    /// 一次"与 NPC 对话"的待打开菜单：由交互层写入，由 <c>NpcGossipPage</c> 在构造时读走。
    /// <para>
    /// 为什么用一次性请求而不是让页面自己去查：页面在导航工厂里被新建，它只拿到 domId，
    /// 不知道玩家点的是哪一只 NPC；把"点的是谁、能做什么"随请求带过去，比在页面里猜当前选中更可靠
    /// （选中对象随时可能被销毁，而面板明明是在点下去那一刻打开的）。
    /// </para>
    /// </summary>
    public static class NpcGossipService
    {
        /// <summary>一次对话请求。</summary>
        public sealed class Request
        {
            public ulong EntityId;
            public int NpcId;
            public string DisplayName = string.Empty;
            public List<NpcStanceCatalog.GossipOption> Options = new List<NpcStanceCatalog.GossipOption>();

            /// <summary>本地玩家等级（菜单项的等级门槛在客户端只做灰显；服务端仍会各自复核）。</summary>
            public int PlayerLevel;
        }

        private static readonly object Gate = new object();
        private static Request _pending;

        /// <summary>写一份待打开请求（覆盖上一份：后点的那只 NPC 才算数）。</summary>
        public static void Begin(ulong entityId, int npcId, string displayName, int playerLevel,
            IReadOnlyList<NpcStanceCatalog.GossipOption> options)
        {
            var request = new Request
            {
                EntityId = entityId,
                NpcId = npcId,
                DisplayName = displayName ?? string.Empty,
                PlayerLevel = playerLevel,
            };

            if (options != null)
            {
                for (var i = 0; i < options.Count; i++) request.Options.Add(options[i]);
            }

            lock (Gate) _pending = request;
        }

        /// <summary>读走请求（读后即弃：面板关掉后再打开应是新的一次交互）。</summary>
        public static Request Take()
        {
            lock (Gate)
            {
                var request = _pending;
                _pending = null;
                return request;
            }
        }
    }
}
