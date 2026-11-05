using System;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Network;
using Horizon.Planet;

namespace HundunWorld.Game.Planet
{
    /// <summary>
    /// 区域快照订阅的引擎侧入口：什么时候问、问回来交给谁。
    /// <para>判定一条都不在这里——<see cref="PlanetRegionWatchRules"/> 是纯 C#，判据在
    /// <c>Horizon.Game.Gateway.Tests</c> 里真跑。这一层只接引擎的东西（会话对象、时钟、日志），
    /// 并把"当前订阅集"这份状态放在自己身上：规则层是无函数的，状态留在一个地方才看得见。</para>
    /// <para>与 <see cref="PlanetRegionTransfers"/> 共用会话与角色号那两条惰性取法，
    /// 也共用同一个钟（<see cref="PlanetRegionTransfers.NowMs"/>）：两条链各一个钟，
    /// 节流与超时就会按两种时间走。</para>
    /// </summary>
    public static class PlanetRegionWatches
    {
        /// <summary>在飞的快照请求。<b>只允许一条</b>：快照是幂等读，重复发只会让 grain 排队。</summary>
        public static bool Pending;

        public static long LastRequestMs = -1;
        public static long LastResponseMs = -1;

        /// <summary>上一次是为哪一块区域问的。换区之后必须与当前区域不同，才会立刻再问一趟。</summary>
        public static long LastRequestedForRegionId;

        /// <summary>当前认为有效的订阅集（回执 Apply 之后才更新）。</summary>
        public static long[] SubscribedRegionIds = Array.Empty<long>();
        public static PlanetCellKey[] SubscribedBoundaryCells = Array.Empty<PlanetCellKey>();

        /// <summary>最近一次差集：要加载/要放下的区域与边界 cell。</summary>
        public static PlanetRegionWatchRules.Diff LastPlan;
        public static PlanetRegionWatchRules.WatchAction LastAction = PlanetRegionWatchRules.WatchAction.Ignore;

        /// <summary>上一次"没发"的原因。实机上"没发"和"发了被拒"一样难查，所以要分开记。</summary>
        public static string LastWhy = "还没问过";

        /// <summary>当前归属区域（由换区那一条链告知，快照回执也会校正它）。</summary>
        public static long OwnerRegionId;

        /// <summary>换区之后调一次：下一趟请求的归因要用它。</summary>
        public static void NoteCrossed(long newOwnerRegionId)
        {
            OwnerRegionId = newOwnerRegionId;
        }

        /// <summary>
        /// 问一次邻区清单。<paramref name="crossedRegionId"/> 非 0 表示"刚跨过区"，
        /// 那条会绕过节流（订阅集必须跟着换）。返回 false 时原因写在 <see cref="LastWhy"/>。
        /// </summary>
        public static async Task<bool> TryRequestAsync(long crossedRegionId, bool withinPrefetchMargin)
        {
            long now = PlanetRegionTransfers.NowMs();
            if (!PlanetRegionWatchRules.ShouldRequest(now, LastRequestMs, crossedRegionId,
                                                     LastRequestedForRegionId, withinPrefetchMargin,
                                                     Pending, out string why))
            {
                LastWhy = why;
                return false;
            }
            if (!PlanetRegionTransfers.TryBuildCaliberBytes(out var caliberBytes, out string caliberWhy))
            {
                LastWhy = caliberWhy;
                return false;
            }
            var session = PlanetRegionTransfers.SessionProvider?.Invoke();
            ulong characterId = PlanetRegionTransfers.CharacterIdProvider();
            if (session == null || !session.CanSendMessage() || characterId == 0UL)
            {
                // 三种"发不出去"合并成一条原因：它们都不该让在飞标记卡在 true
                LastWhy = session == null || !session.CanSendMessage() ? "会话不可用" : "角色号还是 0（没进世界）";
                Pending = false;
                return false;
            }

            Pending = true;
            LastRequestMs = now;
            LastRequestedForRegionId = crossedRegionId != 0 ? crossedRegionId : OwnerRegionId;
            bool sent;
            try
            {
                sent = await session.SendAsync(new PlanetRegionSnapshotRequest
                {
                    CharacterId = characterId,
                    PlanetCaliber = caliberBytes,
                });
            }
            catch (Exception e)
            {
                sent = false;
                LastWhy = "发送抛异常：" + e.GetType().Name;
                Debug.LogWarning($"[planet] 快照请求抛异常（按没发处理）：{e.GetType().Name}");
            }
            if (!sent)
            {
                Pending = false;
                LastWhy = "会话没收下这条请求";
                return false;
            }
            LastWhy = "已发出";
            Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "planet_watch_request ms={0} for_region={1} crossed={2} prefetch={3}",
                now, LastRequestedForRegionId, crossedRegionId, withinPrefetchMargin ? 1 : 0));
            return true;
        }

        /// <summary>回执入口（网络处理器调这里）。</summary>
        public static void OnResponse(PlanetRegionSnapshotResponse response)
        {
            if (response is null)
            {
                Debug.LogWarning("[planet] 收到空的快照回执");
                return;
            }
            long now = PlanetRegionTransfers.NowMs();
            Pending = false;
            LastResponseMs = now;
            LastAction = PlanetRegionWatchRules.Decide(hasResponse: true, response.Accepted,
                                                       (int)response.Code, response.OwnerRegionId);
            switch (LastAction)
            {
                case PlanetRegionWatchRules.WatchAction.Apply:
                    LastPlan = PlanetRegionWatchRules.DiffAgainst(SubscribedRegionIds, response.SubscribedRegionIds,
                                                                  SubscribedBoundaryCells, response.SubscribedBoundaryCells);
                    OwnerRegionId = response.OwnerRegionId;
                    SubscribedRegionIds = response.SubscribedRegionIds ?? Array.Empty<long>();
                    SubscribedBoundaryCells = response.SubscribedBoundaryCells ?? Array.Empty<PlanetCellKey>();
                    if (!LastPlan.IsEmpty)
                        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "planet_watch_plan {0} owner={1}", LastPlan, OwnerRegionId));
                    break;

                // 下面三条都<em>不</em>更新订阅集：拿一份不可信的清单去算差集，
                // 等价于把当前所有邻区块全卸载掉——那是"被拒"最像"没数据"的一种误伤。
                case PlanetRegionWatchRules.WatchAction.RefuseWorld:
                    LastWhy = "口径不符，停手并报警：" + response.Reason;
                    Debug.LogError($"[planet] 快照被拒（不是同一个世界）：{response.Reason}");
                    break;
                case PlanetRegionWatchRules.WatchAction.Rebind:
                    LastWhy = "服务端没有本角色的归属，先落位：" + response.Reason;
                    Debug.LogWarning($"[planet] 快照要求重新落位：{response.Reason}");
                    break;
                default:
                    LastWhy = "没回执";
                    break;
            }
        }

        /// <summary>对账用的一行，进驱动的回执文件。</summary>
        public static string Describe() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "pending={0} subs={1} cells={2} owner={3} plan={4} action={5} why={6} last_req_ms={7} last_resp_ms={8}",
            Pending ? 1 : 0, SubscribedRegionIds.Length, SubscribedBoundaryCells.Length, OwnerRegionId,
            LastPlan.ToString(), LastAction, LastWhy, LastRequestMs, LastResponseMs);

        /// <summary>
        /// 换区/断线/换会话之后清账：<b>订阅集作废，在飞标记必须一起清</b>。
        /// <para>两个时间戳也回到"从没问过"：节流算的是 <c>now - LastRequestMs</c>，留着上一轮的读数
        /// 会让紧接而来的新一轮 PIE 白白等一秒才发出第一趟请求——而"这一轮一个订阅都没加载"
        /// 与"服务端真没人"在屏幕上长得一样。</para>
        /// </summary>
        public static void Reset()
        {
            Pending = false;
            LastRequestMs = -1;
            LastResponseMs = -1;
            LastRequestedForRegionId = 0;
            SubscribedRegionIds = Array.Empty<long>();
            SubscribedBoundaryCells = Array.Empty<PlanetCellKey>();
            OwnerRegionId = 0;
            LastPlan = default;
            LastAction = PlanetRegionWatchRules.WatchAction.Ignore;
            LastWhy = "已清账";
        }
    }
}
