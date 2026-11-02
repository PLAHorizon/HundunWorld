using System;
using System.Collections.Generic;
using FlaxEngine;
using Horizon.Game.Message.Network;
using ManagedHundunWorld.Network.Handlers;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 任务客户端服务：向服务端要"身上的任务"与"某个 NPC/物件能给的任务"，缓存一份只读快照给 UI。
    /// <para>
    /// <b>为什么 UI 不直接订阅事件</b>：响应在连接线程上回调，直接碰 GUI 会撞
    /// "only allowed on a main thread"。这里把更新攒成 <see cref="Revision"/> 计数，
    /// 页面在自己的 Update（主线程）里发现计数变了再重绘——事件只当"脏标记"用。
    /// </para>
    /// <para>
    /// 判定权全在服务端：这里的 <see cref="Quests"/> 与 <see cref="Offers"/> 都是服务端算好的结果，
    /// 客户端不自己判"目标完成了没""等级够不够接"。
    /// </para>
    /// </summary>
    public sealed class QuestClientService
    {
        /// <summary>全局单例（与 <c>ChatClient</c> 同一条路子）。</summary>
        public static QuestClientService Instance { get; } = new QuestClientService();

        private readonly object _gate = new object();
        private List<QuestSummary> _quests = new List<QuestSummary>();
        private List<QuestOfferInfo> _offers = new List<QuestOfferInfo>();
        private QuestMessageHandler _handler;
        private int _revision;
        private int _catalogVersion;
        private string _lastNotice = string.Empty;
        private bool _interactionOk;
        private string _interactionReason = string.Empty;
        private int _interactionAdvanced;
        private bool _interactionPending;

        /// <summary>当前 <see cref="GetOffers"/> 属于哪个 NPC 实体（0 = 全目录/未知）。头顶符号据此对号入座。</summary>
        private ulong _offersGiverEntityId;

        /// <summary>
        /// 按发布者实体缓存的清单（头顶 <c>!</c>/<c>?</c> 要逐只 NPC 判）。
        /// <para>不共享一个"最后一次"槽位：并发问两个 NPC 时，晚到的回包会把先到的清单覆盖掉，
        /// 符号就会挂到错的 NPC 头上，而且两边都不报错。</para>
        /// </summary>
        private readonly Dictionary<ulong, List<QuestOfferInfo>> _offersByGiver =
            new Dictionary<ulong, List<QuestOfferInfo>>();

        /// <summary>按发布者缓存的"条件不足条数"（服务端裁决，客户端只显示灰 !）。</summary>
        private readonly Dictionary<ulong, int> _lockedByGiver = new Dictionary<ulong, int>();

        /// <summary>按发布者缓存的"交付者就是这位"的任务号集合（服务端裁决）。</summary>
        private readonly Dictionary<ulong, HashSet<int>> _turnInByGiver = new Dictionary<ulong, HashSet<int>>();

        private QuestClientService() { }

        /// <summary>数据版本号：任何一次服务端回包都会让它变，UI 据此决定是否重绘。</summary>
        public int Revision { get { lock (_gate) return _revision; } }

        /// <summary>服务端任务目录版本号（编辑器改配置后它会变，是"我的显示是不是旧的"的对账基准）。</summary>
        public int CatalogVersion { get { lock (_gate) return _catalogVersion; } }

        /// <summary>最近一次接取/提交的服务端回执文本（失败原因要能看见，别静默）。</summary>
        public string LastNotice { get { lock (_gate) return _lastNotice; } }

        /// <summary>惰性挂接网络处理器（幂等；重连后可再调一次）。</summary>
        public void EnsureAttached()
        {
            var nm = HundunWorldGame.Instance?.NetworkManager;
            var handler = nm?.GetHandler<QuestMessageHandler>();
            if (handler == null || ReferenceEquals(handler, _handler))
                return;

            if (_handler != null)
            {
                _handler.QuestListReceived -= OnQuestList;
                _handler.QuestProgressUpdated -= OnProgress;
                _handler.AcceptResponded -= OnAccepted;
                _handler.CompleteResponded -= OnCompleted;
                _handler.InteractResponded -= OnInteracted;
            }

            _handler = handler;
            _handler.QuestListReceived += OnQuestList;
            _handler.QuestProgressUpdated += OnProgress;
            _handler.AcceptResponded += OnAccepted;
            _handler.CompleteResponded += OnCompleted;
            _handler.InteractResponded += OnInteracted;
        }

        /// <summary>
        /// 拉任务清单。<paramref name="giverEntityId"/> 非 0 表示"玩家正对着这个对象"，
        /// 回包里的 <see cref="Offers"/> 就只剩这个对象能提供的任务。
        /// <para>
        /// 客户端这里**只出实体 id**：它是什么类别、模板 id 多少、玩家离得够不够近，
        /// 都由服务端按 DS 上行的实体注册表判（旧协议是让客户端自己填 kind+模板 id，那没法信）。
        /// </para>
        /// </summary>
        public bool RequestList(ulong giverEntityId = 0UL, bool activeOnly = false)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || game.PlayerId == 0)
                return false;

            EnsureAttached();
            lock (_gate) _offersGiverEntityId = giverEntityId;
            return SendListRequest(giverEntityId, activeOnly);
        }

        /// <summary>发一次清单请求，<b>不改"当前发布者"槽位</b>（自动刷新用，不能抢走对话页正在读的那一份）。</summary>
        private bool SendListRequest(ulong giverEntityId, bool activeOnly)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || game.PlayerId == 0)
                return false;

            EnsureAttached();
            _ = nm.SendAsync(new QuestListRequestMessage
            {
                CharacterId = game.PlayerId,
                ActiveOnly = activeOnly,
                GiverEntityId = giverEntityId,
            });
            return true;
        }

        /// <summary>
        /// 接/交之后把<b>每只已知任务 NPC</b> 的清单各刷新一遍。
        /// <para>只刷"曾经问过的那些"：没问过的 NPC 本来就没有符号可画。
        /// 不这么刷的话，接过一个任务后老清单会一直挂在头顶，标着"还能接"。</para>
        /// </summary>
        private void RefreshAllGivers()
        {
            List<ulong> givers;
            lock (_gate)
            {
                givers = new List<ulong>(_offersByGiver.Count);
                foreach (var key in _offersByGiver.Keys) givers.Add(key);
            }

            if (givers.Count == 0)
            {
                RequestList();
                return;
            }

            for (var i = 0; i < givers.Count; i++) SendListRequest(givers[i], activeOnly: false);
        }

        /// <summary>接取任务：报模板 id **与发布者实体**，名称/目标/奖励由服务端读模板表（客户端声明等于凭空造奖励）。</summary>
        public bool Accept(int questId, ulong giverEntityId)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || game.PlayerId == 0 || questId <= 0) return false;

            EnsureAttached();
            _ = nm.SendAsync(new AcceptQuestRequest
            {
                CharacterId = game.PlayerId,
                QuestId = questId,
                EntityId = giverEntityId,
            });
            return true;
        }

        /// <summary>
        /// 可接任务 + 各自的发布者实体。接取时要把它报给服务端（服务端核对"是不是这位发布的"）。
        /// <para>按 (发布者, 任务号) 排序：同一次刷新两次打开的顺序必须可复现。</para>
        /// </summary>
        public (ulong GiverEntityId, QuestOfferInfo Offer)[] GetOffersWithGiver()
        {
            lock (_gate)
            {
                var count = 0;
                foreach (var kv in _offersByGiver) count += kv.Value.Count;

                var result = new (ulong GiverEntityId, QuestOfferInfo Offer)[count];
                var i = 0;
                foreach (var kv in _offersByGiver)
                {
                    for (var k = 0; k < kv.Value.Count; k++) result[i++] = (kv.Key, kv.Value[k]);
                }

                Array.Sort(result, static (a, b) =>
                {
                    var byGiver = a.GiverEntityId.CompareTo(b.GiverEntityId);
                    return byGiver != 0 ? byGiver : a.Offer.QuestId.CompareTo(b.Offer.QuestId);
                });
                return result;
            }
        }

        /// <summary>提交任务（服务端校验目标完成后发奖，奖励表随回执一起回来）。</summary>
        public bool Complete(int questId)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || game.PlayerId == 0 || questId <= 0) return false;

            EnsureAttached();
            // 带上"正在交互的是谁"：任务指定了交付者时，服务端要用它核对位置与身份（D11）。
            // 只传实体 id——类别/模板号由服务端用 DS 注册表解析，客户端声明的一律不采信。
            var selected = HundunWorld.Game.Selection.WorldSelectionService.Instance?.Selected;
            var entityId = selected != null
                           && selected.Kind == HundunWorld.Game.Selection.SelectableKind.Npc
                ? selected.EntityId
                : 0UL;

            _ = nm.SendAsync(new CompleteQuestRequest
            {
                CharacterId = game.PlayerId,
                QuestId = questId,
                EntityId = entityId,
            });
            return true;
        }

        /// <summary>取身上任务快照（含已完成，服务端按请求的 activeOnly 决定范围）。</summary>
        public QuestSummary[] GetQuests()
        {
            lock (_gate) return _quests.ToArray();
        }

        /// <summary>当前 offer 清单属于哪个 NPC 实体（0 = 不是针对某个 NPC 拉的）。</summary>
        public ulong OffersGiverEntityId { get { lock (_gate) return _offersGiverEntityId; } }

        /// <summary>取当前发布者能给的任务快照。</summary>
        public QuestOfferInfo[] GetOffers()
        {
            lock (_gate) return _offers.ToArray();
        }

        /// <summary>
        /// 取某个发布者实体能给的清单（没有回包时返回空数组）。
        /// <para>头顶 <c>!</c>/<c>?</c> 用这一份：清单为空时应当"不画符号"，
        /// 而不是退回别人的清单——那会把符号挂到错的 NPC 头上。</para>
        /// </summary>
        /// <summary>这位发布者名下"条件不足"的任务条数（没有回包时为 0）。</summary>
        public int GetLockedOfferCountFor(ulong giverEntityId)
        {
            if (giverEntityId == 0UL) return 0;
            lock (_gate) return _lockedByGiver.TryGetValue(giverEntityId, out var n) ? n : 0;
        }

        /// <summary>服务端认定"交付者就是这位"的任务号集合（没有回包时为空）。</summary>
        public IReadOnlyCollection<int> GetTurnInQuestIdsFor(ulong giverEntityId)
        {
            if (giverEntityId == 0UL) return Array.Empty<int>();
            lock (_gate)
            {
                return _turnInByGiver.TryGetValue(giverEntityId, out var set)
                    ? (IReadOnlyCollection<int>)set
                    : Array.Empty<int>();
            }
        }

        public QuestOfferInfo[] GetOffersFor(ulong giverEntityId)
        {
            if (giverEntityId == 0UL) return Array.Empty<QuestOfferInfo>();
            lock (_gate)
            {
                return _offersByGiver.TryGetValue(giverEntityId, out var list)
                    ? list.ToArray()
                    : Array.Empty<QuestOfferInfo>();
            }
        }

        /// <summary>
        /// 上报"玩家与世界里某个对象交互"（对话 / 触发 / 拾取入口）。
        /// <para>
        /// <b>只带运行时实体 id，不带任何身份</b>：这个实体存在吗、是不是可交互类别、离玩家够不够近，
        /// 全部由服务端按 DS 上行的实体身份注册表判定（docs/QUEST_SYSTEM_DESIGN.md §11）。
        /// 客户端连"它是什么"都不参与声明——否则"与铁匠对话"就变成客户端想推就能推的一句话。
        /// </para>
        /// <para>成立与否的回执走 <see cref="InteractResolved"/>；进度本身由服务端另发进度通知。</para>
        /// </summary>
        public bool ReportInteract(ulong entityId)
        {
            var game = HundunWorldGame.Instance;
            var nm = game?.NetworkManager;
            if (nm == null || game.PlayerId == 0 || entityId == 0UL) return false;

            EnsureAttached();
            _ = nm.SendAsync(new QuestInteractRequestMessage
            {
                CharacterId = game.PlayerId,
                EntityId = entityId,
            });
            // 同一次按键还要回答"它身上有没有我能接的任务"：清单按实体 id 收窄，
            // 身份与距离仍由服务端判（服务端认不出这个 id 时只会给空可接，不会退化成总表）。
            RequestList(entityId);
            return true;
        }

        /// <summary>最近一次交互的服务端裁决（Ok=false 时 <see cref="InteractionReason"/> 给短码）。</summary>
        public bool LastInteractionOk { get { lock (_gate) return _interactionOk; } }

        /// <summary>不成立的短码：<c>unknown_entity</c>/<c>not_interactable</c>/<c>no_position</c>/<c>too_far</c>…</summary>
        public string InteractionReason { get { lock (_gate) return _interactionReason; } }

        /// <summary>本次交互推进的任务目标条数。</summary>
        public int InteractionAdvanced { get { lock (_gate) return _interactionAdvanced; } }

        private void OnQuestList(QuestListResponseMessage message)
        {
            if (message == null) return;
            lock (_gate)
            {
                _quests = message.Quests != null ? new List<QuestSummary>(message.Quests) : new List<QuestSummary>();

                var offers = message.Offers != null ? new List<QuestOfferInfo>(message.Offers) : new List<QuestOfferInfo>();
                var giver = message.GiverEntityId;
                if (giver != 0UL)
                {
                    _offersByGiver[giver] = offers;
                    _lockedByGiver[giver] = message.LockedOfferCount;

                    var turnIn = new HashSet<int>();
                    if (message.Quests != null)
                    {
                        for (var i = 0; i < message.Quests.Count; i++)
                        {
                            var q = message.Quests[i];
                            if (q != null && q.TurnInHere && q.QuestId > 0) turnIn.Add(q.QuestId);
                        }
                    }

                    _turnInByGiver[giver] = turnIn;
                }

                // "当前清单"只在这两种情况下被替换：
                //   1) 回包就是当前对过的那个发布者；
                //   2) 当前没有指定发布者、回包也是全目录。
                // 其余（并发的别的 NPC）只进缓存，不许覆盖对话页正在读的那一份。
                if (giver == _offersGiverEntityId)
                    _offers = offers;
                else if (giver == 0UL && _offersGiverEntityId == 0UL)
                    _offers = offers;

                _catalogVersion = message.CatalogVersion;
                _revision++;
            }
        }

        private void OnProgress(QuestProgressNotifyMessage message)
        {
            // 通知只带"某条任务的某个目标到几了"（QuestId/ObjectiveIndex/CurrentProgress），
            // 不重算、只照抄：判定权在服务端，客户端这里连目标总数都不该自己猜。
            if (message == null || message.QuestId <= 0) return;
            lock (_gate)
            {
                for (var index = 0; index < _quests.Count; index++)
                {
                    var quest = _quests[index];
                    if (quest == null || quest.QuestId != message.QuestId) continue;

                    if (message.CurrentProgress >= quest.TargetProgress && quest.TargetProgress > 0)
                        quest.Status = 1;             // 可交付
                    quest.CurrentProgress = Math.Max(quest.CurrentProgress, message.CurrentProgress);
                    _quests[index] = quest;
                    _revision++;
                    return;
                }

                // 身上还没有这条任务（刚接取但清单还没回来）：不猜它的内容，等下一次清单回包。
                _revision++;
            }
        }

        private void OnAccepted(AcceptQuestResponse response)
        {
            if (response == null) return;
            Note(response.Success ? "已接取：" + (response.AcceptedQuest?.QuestName ?? "") : "接取失败：" + response.Message);
            // 接完立刻回源要一份权威清单：本地不猜进度，也不猜"这个 NPC 还剩什么能给"。
            RefreshAllGivers();
        }

        private void OnCompleted(CompleteQuestResponse response)
        {
            if (response == null) return;
            var rewardCount = response.Rewards != null ? response.Rewards.Count : 0;
            Note(response.Success ? $"任务完成，发放 {rewardCount} 项产出" : "提交失败：" + response.Message);
            RefreshAllGivers();
        }

        private void OnInteracted(QuestInteractResultMessage result)
        {
            if (result == null) return;
            lock (_gate)
            {
                _interactionOk = result.Ok;
                _interactionReason = result.Reason ?? string.Empty;
                _interactionAdvanced = result.AdvancedCount;
                _interactionPending = true;   // 由主线程取走后才能碰 GUI
            }
            Note(result.Ok
                ? $"交互成立：推进 {result.AdvancedCount} 条任务目标"
                : "交互未被采信：" + result.Reason);
        }

        /// <summary>
        /// 取走一次交互裁决（**每个结果只发一次**）。回包到达在连接线程，
        /// 所以这里只置标志，界面/提示由主线程轮询取走——与任务清单同一套脏标记纪律。
        /// </summary>
        public bool TryTakeInteractionResult(out bool ok, out string reason, out int advanced)
        {
            lock (_gate)
            {
                ok = _interactionOk;
                reason = _interactionReason;
                advanced = _interactionAdvanced;
                if (!_interactionPending) return false;
                _interactionPending = false;
                return true;
            }
        }

        private void Note(string text)
        {
            lock (_gate) _lastNotice = text ?? string.Empty;
            Debug.Log("[QuestClientService] " + text);
        }
    }
}
