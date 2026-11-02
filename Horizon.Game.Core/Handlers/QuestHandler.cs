using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    public class QuestHandler : MessageHandlerBase
    {
        /// <summary>世界实体身份注册表（DS 上行落地点）。null = DS 通道未接线，交互一律不采信。</summary>
        private readonly Horizon.Game.Core.Sim.Server.DsEntityRegistry? _entityRegistry;

        /// <summary>角色 → shard 路由。null 时按 0 号 shard 处理（单 shard 部署的既有口径）。</summary>
        private readonly Horizon.Game.Core.World.IShardRouter? _shardRouter;

        public QuestHandler(
            ILogger<MessageHandlerBase> logger,
            IClusterClient clusterClient,
            HorizonMessageAdapter adapter,
            Horizon.Game.Core.Sim.Server.DsEntityRegistry? entityRegistry = null,
            Horizon.Game.Core.World.IShardRouter? shardRouter = null) : base(logger, clusterClient, adapter)
        {
            _entityRegistry = entityRegistry;
            _shardRouter = shardRouter;
        }


        public override List<MessageType> MessageTypes { get; } = new List<MessageType> {
            MessageType.QuestUpdate,
            MessageType.AcceptQuest,
            MessageType.CompleteQuest,
            MessageType.QuestListRequest,
            MessageType.QuestInteractRequest
        };

        public override ServiceType ServiceType => ServiceType.Quest;



        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            return await base.HandleAsync(client, message);
        }

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {

            switch (message.Header.MessageType)
            {
                default:
                case MessageType.QuestUpdate:
                    return await HandleQuestUpdateAsync(message);
                case MessageType.AcceptQuest:
                    return await HandleAcceptQuestAsync(message);
                case MessageType.CompleteQuest:
                    return await HandleCompleteQuestAsync(message);
                case MessageType.QuestListRequest:
                    return await HandleQuestListAsync(message);
                case MessageType.QuestInteractRequest:
                    return await HandleQuestInteractAsync(message);
            }
        }

        /// <summary>
        /// 回服务端任务清单（1383 → 1384）。
        /// <para>
        /// 协议与客户端接收侧（<c>QuestMessageHandler</c> 等 <c>QuestListResponse</c>）一直就在，
        /// 缺的只是网关侧的处理方：客户端问了没下文，任务页就只能退回本地硬编码列表，
        /// 于是 §14.2 的"任务获得初级技能书"在界面上根本看不见。
        /// </para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleQuestListAsync(HorizonMessagePacket message)
        {
            var response = new QuestListResponseMessage();

            try
            {
                if (message.Body is not QuestListRequestMessage request || request.CharacterId == 0)
                    return (true, CreateHorizonMessage(response));

                // 回带发布者：客户端要按 NPC 各自缓存清单（头顶 !/? 逐只 NPC 判），
                // 并发问多个 NPC 时没这个字段就分不清"这份清单是谁的"。
                response.GiverEntityId = request.GiverEntityId;

                var key = GrainKeys.ForEntity(request.CharacterId);
                var grain = _clusterClient.GetGrain<IQuestGrain>(key);
                var manager = _clusterClient.GetGrain<global::Horizon.Orleans.Interface.World.IQuestManagerGrain>(
                    global::Horizon.Orleans.Interface.World.QuestManagerKeys.Singleton);

                // 撤销队列先落地：运营"关闭并强制取消"时只在 Grain 里记了账，
                // 由网关在这里取走并执行——Grain 之间不互相回 call，避免把调用环拧成超时。
                var revoked = await manager.TakeRevokedQuestsAsync(key);
                foreach (var revokedQuestId in revoked ?? Array.Empty<int>())
                    await grain.AbandonQuestAsync(revokedQuestId);

                var active = new List<QuestData>(await grain.GetActiveQuestsAsync());
                var quests = new List<QuestData>(active);
                if (!request.ActiveOnly)
                    quests.AddRange(await grain.GetCompletedQuestsAsync());

                response.Quests = quests.Select(ToSummary).ToList();

                // 可接列表：客户端不声明任何身份也能拿到（giverTemplateId=0 = 不按发布者过滤），
                // 内容全部由服务端按发布态/等级/前置算。带发布者时只是把这张表再收窄一点。
                //
                // 发布者收窄现在也走**服务端解析**：客户端只报实体 id，(类别, 模板 id) 由 DS 上行的
                // 实体注册表解出并核对距离（QuestInteractionAuthority.TryResolveGiver）。
                // 旧协议这里是客户端直接填 kind+模板 id，服务端无从证伪，只能当显示过滤器用。
                if (!request.ActiveOnly)
                {
                    var level = await ResolveCharacterLevelAsync(request.CharacterId);
                    var completedIds = (await grain.GetCompletedQuestsAsync()).Select(q => q.QuestId).ToArray();
                    var activeIds = active.Select(q => q.QuestId).ToArray();

                    var giverKind = 0;
                    var giverTemplateId = 0;
                    if (request.GiverEntityId != 0UL)
                    {
                        var shardId = _shardRouter?.Resolve((long)request.CharacterId) ?? 0L;
                        var position = await ResolveCharacterFlaxPositionAsync(request.CharacterId);
                        if (!Sim.Server.QuestInteractionAuthority.TryResolveGiver(
                                _entityRegistry, shardId, request.GiverEntityId, position,
                                out giverKind, out giverTemplateId, out var giverReason))
                        {
                            // 认不出玩家指的这个对象时**不给总表**：那等于用一个不存在的对象
                            // 换到整个目录，比"这次没看到可接任务"糟得多。清单本身照发。
                            Logger.LogDebug("任务清单：发布者实体 {EntityId} 未被采信（{Reason}）",
                                request.GiverEntityId, giverReason);
                            response.CatalogVersion = await manager.GetCatalogVersionAsync();
                            return (true, CreateHorizonMessage(response));
                        }
                    }

                    var offers = await manager.GetOfferedQuestsAsync(
                        giverKind, giverTemplateId, level, completedIds, activeIds);

                    response.Offers = offers.Select(offer => new QuestOfferInfo
                    {
                        QuestId = offer.QuestId,
                        QuestName = offer.QuestName,
                        Description = offer.Description,
                        QuestType = offer.QuestType,
                        ContentVersion = offer.ContentVersion,
                        AlreadyAccepted = offer.AlreadyAccepted,
                        AlreadyFinished = offer.Finished,
                        LevelRequire = offer.LevelRequire,
                        GiverLabel = offer.GiverName,
                        Objectives = QuestWireMapper.ObjectivesFromTemplateJson(offer.ObjectivesJson),
                        Reward = QuestWireMapper.RewardFromTemplateJson(offer.RewardsJson),
                    }).ToList();

                    response.CatalogVersion = await manager.GetCatalogVersionAsync();

                    // 头顶符号要补的两个事实（都不改 Grain 接口，只读已有目录快照）：
                    //   ① 条件不足的条数——可接清单已把它们滤掉，客户端原本无从知道"以后会有"；
                    //   ② 这个发布者是不是交付者——黄/灰 ? 只该画在交付者头上。
                    if (giverKind > 0)
                    {
                        var catalog = await manager.GetCatalogAsync();
                        var entries = catalog?.Entries;
                        var locked = Sim.Server.QuestIndicatorOfferCore.CountLocked(
                            entries, giverKind, giverTemplateId, level, completedIds, activeIds);
                        response.LockedOfferCount = locked.Total;

                        var byQuestId = new Dictionary<int, global::Horizon.Orleans.Interface.World.QuestCatalogEntry>();
                        if (entries != null)
                        {
                            for (var i = 0; i < entries.Length; i++)
                            {
                                var entry = entries[i];
                                if (entry == null || entry.PublishState != 1) continue;
                                byQuestId[entry.QuestId] = entry;
                            }
                        }

                        for (var i = 0; i < response.Quests.Count; i++)
                        {
                            var summary = response.Quests[i];
                            if (byQuestId.TryGetValue(summary.QuestId, out var entry))
                                summary.TurnInHere = Sim.Server.QuestIndicatorOfferCore.IsTurnInHere(entry, giverKind, giverTemplateId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理任务清单请求失败");
            }

            return (true, CreateHorizonMessage(response));
        }

        /// <summary>
        /// 处理"与世界中某个对象交互"（1658 → 1659）——<c>talk_npc</c> / <c>interact_prop</c> 条件的权威事件源。
        /// <para>
        /// <b>客户端在这里只提供一个实体 id，不提供任何身份或进度</b>：
        /// 身份由 DS 上行的 <see cref="Sim.Server.DsEntityRegistry"/> 解出，
        /// 邻近由"角色位置缓存（ECS，换轴后）↔ 注册表里的实体坐标"判定，
        /// 三条核对全过才推进目标（<see cref="Sim.Server.QuestInteractionAuthority"/>）。
        /// </para>
        /// <para>
        /// <b>为什么不能沿用可接列表请求里的 giver 字段</b>：那是展示语义，客户端填什么
        /// 服务端只能照单全收；拿它推进进度等于让被服务的一方声明服务结论。
        /// </para>
        /// <para>
        /// 位置读不到时**不采信**（<c>no_position</c>）而不是跳过距离核对：
        /// 缓存缺失是运维态问题，把它降级成"免检通过"就等于给伪造开了一个恒开的口子。
        /// </para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleQuestInteractAsync(HorizonMessagePacket message)
        {
            var response = new QuestInteractResultMessage();

            try
            {
                if (message.Body is not QuestInteractRequestMessage request || request.CharacterId == 0)
                    return (true, CreateHorizonMessage(response));

                response.CharacterId = request.CharacterId;
                response.EntityId = request.EntityId;

                // shard 必须按角色路由解析，不能"全 shard 找一个"：实体 id 由刷怪点算出，
                // 多 shard 共用同一份刷怪表时两个世界里会有同名 id，跨片命中就是给假 id 开门。
                var shardId = _shardRouter?.Resolve((long)request.CharacterId) ?? 0L;

                (float X, float Y, float Z)? flaxPos = await ResolveCharacterFlaxPositionAsync(request.CharacterId);

                var verdict = Sim.Server.QuestInteractionAuthority.Evaluate(
                    _entityRegistry, shardId, request.EntityId, flaxPos);

                response.Ok = verdict.Ok;
                response.Reason = verdict.Reason;
                if (!verdict.Ok) return (true, CreateHorizonMessage(response));

                var grain = _clusterClient.GetGrain<IQuestGrain>(GrainKeys.ForEntity(request.CharacterId));
                var active = await grain.GetActiveQuestsAsync();
                var advanced = 0;

                foreach (var quest in active ?? new List<QuestData>())
                {
                    // Status: 0=进行中。已达标的任务（1=可提交）不该被同一次对话再推一格。
                    if (quest?.Objectives is null || quest.Status != 0) continue;

                    for (var i = 0; i < quest.Objectives.Count; i++)
                    {
                        var objective = quest.Objectives[i];
                        if (objective is null || objective.IsCompleted) continue;
                        if (!Sim.Server.QuestInteractionAuthority.MatchesObjective(
                                objective.ObjectiveType, verdict.Kind, verdict.TemplateId)) continue;

                        try
                        {
                            if (await grain.UpdateQuestProgressAsync(quest.QuestId, i, 1)) advanced++;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "任务交互：推进任务 {QuestId} 目标 {Index} 失败。",
                                quest.QuestId, i);
                        }
                    }
                }

                response.AdvancedCount = advanced;
                await CreditNpcTalkFavorAsync(request.CharacterId, verdict);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理任务交互请求失败");
                response.Ok = false;
                response.Reason = "gateway_error";
            }

            return (true, CreateHorizonMessage(response));
        }

        /// <summary>单次对话累积的好感点数。</summary>
        private const int FavorPerTalk = 1;

        /// <summary>同一位 NPC 的对话好感上限（显式压得比默认来源上限紧）。</summary>
        /// <remarks>
        /// 击杀一条来源按默认 20 计（要杀满 20 只才吃满），对话若也照默认走，站在 NPC 面前连点 20 次
        /// 就白拿一整个 §14 玄阶门槛。压到 5 之后：好感仍然是"在这片地域待过"的凭证（还得靠击杀/任务/信物补齐），
        /// 而不是"和某个人聊过天"的凭证。这条线与"取消门派限制"是同一条设计意图——门槛要能被多种玩法满足，
        /// 但任何一种玩法都不该一个人吃满全场。
        /// </remarks>
        private const int TalkFavorPerTargetCap = 5;

        /// <summary>
        /// §14「与某地区域生物/<b>NPC</b> 好感度触发中级技能学习条件」里 NPC 那一半的入账口。
        /// <para>
        /// 此前这条口根本不存在：交互链路（<c>QuestInteractionAuthority</c>）已经能服务端权威地认定
        /// "这个人真的站在这位 NPC 面前说过话"，却只用来推进任务目标；而
        /// <c>IFavorabilityGrain.AddAsync</c> 的注释里连 <c>npc:10086</c> 这个 targetKey 示例都写好了。
        /// </para>
        /// <para>
        /// 判定失败/未登记 NPC 一律<b>不记账</b>（<c>DomainOf</c> 返回 0）：
        /// 未登记的"路人#N"没有域，写进账本就是给一个不存在的域加好感。
        /// </para>
        /// </summary>
        private async Task CreditNpcTalkFavorAsync(
            ulong characterId, Sim.Server.QuestInteractionAuthority.Verdict verdict)
        {
            // 必须按类别闸：NPC 与怪物共用同一个整数 id 空间（都落在 MonsterTemplateId 字段上），
            // 少了这一行，砍一只"墨麒麟"会被记成"和铸剑师聊过天"。
            if (verdict.Kind != Horizon.Game.Sim.Quests.QuestEntityKind.Npc) return;

            int domain = Horizon.Game.Sim.NpcTemplateCatalog.DomainOf(verdict.TemplateId);
            if (domain <= 0) return;

            try
            {
                var favor = _clusterClient.GetGrain<IFavorabilityGrain>(GrainKeys.ForEntity(characterId));
                await favor.AddAsync(domain, FavorPerTalk, $"npc:{verdict.TemplateId}", TalkFavorPerTargetCap);
            }
            catch (Exception ex)
            {
                // 好感入账失败不该把这次对话判成失败：任务目标已经推进了，回滚它更糟。
                Logger.LogWarning(ex, "NPC 对话好感入账失败。CharacterId={CharacterId}, NpcTemplate={TemplateId}",
                    characterId, verdict.TemplateId);
            }
        }

        /// <summary>
        /// 取角色的**权威位置**并换算到注册表所在的 Flax Y-up 空间；取不到返回 null。
        /// <para>
        /// 调用方（交互裁决、发布者解析）拿到 null 一律按"判不了距离 = 不采信"处理，
        /// 不能降级成免检——位置缓存缺失是运维态问题，绕过距离核对等于给伪造开一个恒开的口子。
        /// </para>
        /// </summary>
        private async Task<(float X, float Y, float Z)?> ResolveCharacterFlaxPositionAsync(ulong characterId)
        {
            try
            {
                var character = _clusterClient.GetGrain<ICharacterGrain>((long)characterId);
                var cached = await character.GetLastPositionAsync();
                if (cached is not { } p) return null;
                return Sim.Server.QuestInteractionAuthority.ToFlax(p.X, p.Y, p.Z);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "任务侧：读取角色 {CharacterId} 位置缓存失败。", characterId);
                return null;
            }
        }

        /// <summary>
        /// 取角色等级（权威值，不用客户端报的数）。取不到时返回 0 —— 这会让所有有等级门槛的任务
        /// 都不出现在可接列表里：宁可少给，也不能把"读不到档案"变成"谁都能接"。
        /// </summary>
        private async Task<int> ResolveCharacterLevelAsync(ulong characterId)
        {
            try
            {
                var character = _clusterClient.GetGrain<ICharacterGrain>((long)characterId);
                var profile = await character.GetSyncProfileAsync();
                return profile?.Level ?? 0;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "读角色等级失败，任务可接列表按等级 0 处理: CharacterId={CharacterId}", characterId);
                return 0;
            }
        }

        /// <summary>
        /// 进度按"已完成目标数 / 目标总数"给，客户端只需要一个可读的比值；
        /// 目标本身的结构（多目标、每种类型）不在这里展开，展开会让摘要与详情两套口径分叉。
        /// </summary>
        private static QuestSummary ToSummary(QuestData quest) => new()
        {
            QuestId = quest.QuestId,
            QuestName = quest.QuestName ?? string.Empty,
            QuestType = quest.QuestType,
            Status = quest.Status,
            CurrentProgress = quest.Objectives?.Count(o => o.IsCompleted) ?? 0,
            TargetProgress = quest.Objectives?.Count ?? 0,
            Description = quest.Description ?? string.Empty,
            Objectives = QuestWireMapper.ObjectivesFromState(quest),
            Reward = QuestWireMapper.RewardFromState(quest.Rewards),
        };

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleQuestUpdateAsync(HorizonMessagePacket message)
        {
            try
            {
                QuestUpdateMessage questUpdateMessage = message.Body as QuestUpdateMessage;
                // 处理任务更新逻辑
                var response = new QuestUpdateMessage
                {
                    CharacterId = questUpdateMessage.CharacterId,
                    QuestId = questUpdateMessage.QuestId,
                    UpdatedQuest = questUpdateMessage.UpdatedQuest
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理任务更新消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理任务更新消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleAcceptQuestAsync(HorizonMessagePacket message)
        {
            try
            {
                var request = message.Body as AcceptQuestRequest;
                if (request is null || request.CharacterId == 0)
                    return (true, CreateHorizonMessage(new AcceptQuestResponse
                    {
                        Success = false,
                        Message = "角色ID无效",
                    }));

                // 这里原来也是空桩（恒回 Success=true、什么都不做）。§14.2 的初级技能书产出全押在
                // "任务"这条路上，所以接任务必须真的落到服务端状态里。
                // 注意走的是 AcceptFromTemplateAsync 而不是 AcceptQuestAsync：后者让调用方自带 rewards，
                // 接上网络就等于让客户端自己声明"这个任务给我三十本秘籍"，整套习得门槛当场作废。
                //
                // 等级也从**服务端**取着传进去（与可接列表同源的那条 GetSyncProfileAsync）：
                // 门槛只在校验列表时生效过一次的话，直连一条 questId 就能把高等级任务接走。
                // 发布者身份：任务指定了发布者就只能在那一处接（与交付同一纪律；可接列表也是按发布者出的）。
                var giverReject = await ValidateGiverAsync(request);
                if (giverReject is { } giverGate)
                {
                    return (true, CreateHorizonMessage(new AcceptQuestResponse
                    {
                        Success = false,
                        Message = giverGate,
                    }));
                }

                var questGrain = _clusterClient.GetGrain<IQuestGrain>(GrainKeys.ForEntity(request.CharacterId));
                var characterLevel = await ResolveCharacterLevelAsync(request.CharacterId);
                var accepted = await questGrain.AcceptFromTemplateAsync(request.QuestId, characterLevel);

                var response = new AcceptQuestResponse
                {
                    Success = accepted != null,
                    Message = accepted != null ? "接受任务成功" : "任务不存在或当前不可接取",
                    AcceptedQuest = accepted == null
                        ? null
                        : new QuestInfo { QuestId = accepted.QuestId, QuestName = accepted.QuestName },
                };
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理接受任务消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理接受任务消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCompleteQuestAsync(HorizonMessagePacket message)
        {
            try
            {
                var request = message.Body as CompleteQuestRequest;
                if (request is null || request.CharacterId == 0)
                    return (true, CreateHorizonMessage(new CompleteQuestResponse
                    {
                        Success = false,
                        Message = "角色ID无效",
                    }));

                // 这里原来是一份恒回 Success=true、Rewards 为空的空桩：任务"完成"了，
                // 进度没动、东西没给，玩家什么也拿不到。五行技能书的产出（§14.2 初级书靠任务）
                // 全押在这条链上，所以先把它接到真 Grain 上。
                var questGrain = _clusterClient.GetGrain<IQuestGrain>(GrainKeys.ForEntity(request.CharacterId));

                // D11：任务指定了交付者（模板 turn_in_* / 0=沿用发布者）时，必须在那一处交付。
                // 校验只认服务端解析出的身份（DS 注册表 + 分片 + 交互距离），客户端报的只是实体 id。
                var turnInReject = await ValidateTurnInAsync(request);
                if (turnInReject is { } gate)
                {
                    return (true, CreateHorizonMessage(new CompleteQuestResponse
                    {
                        Success = false,
                        Message = gate,
                        CompletedQuestId = request.QuestId,
                    }));
                }

                var result = await questGrain.CompleteQuestAsync(request.QuestId);

                var granted = result.Rewards ?? new Dictionary<string, int>();
                if (result.Success)
                    await GrantQuestRewardsAsync(request.CharacterId, request.QuestId, granted);

                var response = new CompleteQuestResponse
                {
                    Success = result.Success,
                    Message = result.Message,
                    Rewards = granted,
                    CompletedQuestId = request.QuestId,
                };
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理完成任务消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理完成任务消息失败" }));
            }
        }

        /// <summary>
        /// 交付对象校验（D11）。通过返回 <c>null</c>；不通过返回一句能念给玩家的话。
        /// <para>
        /// <b>取目录失败按拒绝处理</b>：若失败即放行，一条"本该到指定 NPC 交"的任务就变成随处可交，
        /// 而这类漏洞在成功路径上完全看不出来（玩家只会觉得今天运气好）。
        /// </para>
        /// </summary>
        /// <summary>
        /// 接取前的发布者校验：返回 null = 放行；否则是给玩家看的一句话理由。
        /// <para>目录里没有这条任务时**放行给 Grain 去拒**（"任务不存在"的权威在那边）；
        /// 但目录读失败时**拒绝**——宁可拒绝，也不放走"任意地点接任意任务"。</para>
        /// </summary>
        private async Task<string?> ValidateGiverAsync(AcceptQuestRequest request)
        {
            try
            {
                var manager = _clusterClient.GetGrain<global::Horizon.Orleans.Interface.World.IQuestManagerGrain>(
                    global::Horizon.Orleans.Interface.World.QuestManagerKeys.Singleton);
                var catalog = await manager.GetCatalogAsync();

                global::Horizon.Orleans.Interface.World.QuestCatalogEntry? entry = null;
                var entries = catalog?.Entries ?? Array.Empty<global::Horizon.Orleans.Interface.World.QuestCatalogEntry>();
                for (var i = 0; i < entries.Length; i++)
                {
                    if (entries[i].QuestId != request.QuestId) continue;
                    entry = entries[i];
                    break;
                }

                if (entry is null) return null;

                var (giverKind, giverTemplateId) = Horizon.Game.Sim.Quests.QuestGiverCore.Required(
                    entry.GiverKind, entry.GiverTemplateId);
                if (giverTemplateId <= 0) return null;   // 不限制发布者

                var resolved = false;
                var providedKind = 0;
                var providedTemplateId = 0;
                if (request.EntityId != 0UL)
                {
                    var shardId = _shardRouter?.Resolve((long)request.CharacterId) ?? 0L;
                    var position = await ResolveCharacterFlaxPositionAsync(request.CharacterId);
                    resolved = Sim.Server.QuestInteractionAuthority.TryResolveGiver(
                        _entityRegistry, shardId, request.EntityId, position,
                        out providedKind, out providedTemplateId, out var resolveReason);
                    if (!resolved)
                        Logger.LogDebug("任务发布者未被采信：EntityId={EntityId} 原因={Reason}",
                            request.EntityId, resolveReason);
                }

                var verdict = Horizon.Game.Sim.Quests.QuestGiverCore.Evaluate(
                    giverKind, giverTemplateId, request.EntityId, resolved, providedKind, providedTemplateId);
                return verdict.Ok ? null : Horizon.Game.Sim.Quests.QuestGiverCore.RejectMessage(verdict.Reason);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "接取任务：发布者校验失败，按拒绝处理 QuestId={QuestId}", request.QuestId);
                return "任务发布者校验失败，请稍后再试";
            }
        }

        private async Task<string?> ValidateTurnInAsync(CompleteQuestRequest request)
        {
            try
            {
                var manager = _clusterClient.GetGrain<global::Horizon.Orleans.Interface.World.IQuestManagerGrain>(
                    global::Horizon.Orleans.Interface.World.QuestManagerKeys.Singleton);
                var catalog = await manager.GetCatalogAsync();

                global::Horizon.Orleans.Interface.World.QuestCatalogEntry? entry = null;
                var entries = catalog?.Entries ?? Array.Empty<global::Horizon.Orleans.Interface.World.QuestCatalogEntry>();
                for (var i = 0; i < entries.Length; i++)
                {
                    if (entries[i].QuestId != request.QuestId) continue;
                    entry = entries[i];
                    break;
                }

                // 模板不在目录里：交给 Grain 去拒（那边有权威的"任务不存在/不可交付"）。
                if (entry is null) return null;

                var (requiredKind, requiredTemplateId) = Horizon.Game.Sim.Quests.QuestTurnInCore.ResolveRequired(
                    entry.GiverKind, entry.GiverTemplateId, entry.TurnInKind, entry.TurnInTemplateId);

                var resolved = false;
                var providedKind = 0;
                var providedTemplateId = 0;
                if (request.EntityId != 0UL)
                {
                    var shardId = _shardRouter?.Resolve((long)request.CharacterId) ?? 0L;
                    var position = await ResolveCharacterFlaxPositionAsync(request.CharacterId);
                    resolved = Sim.Server.QuestInteractionAuthority.TryResolveGiver(
                        _entityRegistry, shardId, request.EntityId, position,
                        out providedKind, out providedTemplateId, out var resolveReason);
                    if (!resolved)
                        Logger.LogDebug("任务交付对象未被采信：EntityId={EntityId} 原因={Reason}",
                            request.EntityId, resolveReason);
                }

                var verdict = Horizon.Game.Sim.Quests.QuestTurnInCore.Evaluate(
                    requiredKind, requiredTemplateId, request.EntityId, resolved, providedKind, providedTemplateId);
                if (verdict.Ok) return null;

                Logger.LogInformation("任务交付被拒。quest={QuestId} {Verdict}", request.QuestId, verdict.Describe());
                return verdict.Reason switch
                {
                    Horizon.Game.Sim.Quests.QuestTurnInReject.MissingEntity => "这个任务需要到指定的交付者处交付",
                    Horizon.Game.Sim.Quests.QuestTurnInReject.UnresolvedEntity => "附近没有可交付的对象",
                    Horizon.Game.Sim.Quests.QuestTurnInReject.WrongKind => "这个对象不是该任务的交付者",
                    Horizon.Game.Sim.Quests.QuestTurnInReject.WrongTemplate => "该任务不在这个对象处交付",
                    _ => "无法交付",
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "任务交付对象校验失败（按拒绝处理）");
                return "暂时无法核对交付对象，请稍后再试";
            }
        }

        /// <summary>
        /// 按约定键发放任务奖励。<see cref="QuestData.Rewards"/> 是 <c>Dictionary&lt;string,int&gt;</c>，
        /// 与其为"书"和"好感"各加一列，不如把产出写成数据：
        /// <list type="bullet">
        ///   <item><c>item:&lt;模板id&gt;</c> = 数量 —— 发物品。五行初级书（800000+技能id）与灵材都走这一条。</item>
        ///   <item><c>favor:&lt;域&gt;</c> = 点数 —— 加该五行域好感（1金 2木 3水 4火 5土）。玄阶以上的习得条件由此打开。</item>
        /// </list>
        /// 其余既有键（gold/exp 之类）保持原样不动：本方法不认领自己不认识的东西，只记一条日志，
        /// 免得把还没接线的奖励悄悄吞掉。
        /// <para>
        /// 发放失败绝不影响"任务已完成"这件事本身：任务状态是权威事实，奖励少了人去对账，
        /// 而不是把完成判成失败让玩家反复重交。
        /// </para>
        /// </summary>
        private async Task GrantQuestRewardsAsync(ulong characterId, int questId, Dictionary<string, int> rewards)
        {
            foreach (var (key, value) in rewards)
            {
                if (string.IsNullOrWhiteSpace(key) || value <= 0) continue;

                var parts = key.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    if (parts.Length == 2 && parts[0].Equals("item", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(parts[1], out var templateId) && templateId > 0)
                    {
                        // 背包的权威键是 GrainKeyMapper.ToInventoryKey（大端 Guid）：
                        // 行囊 UI、使用物品、DS 掉落入包全走它。这里若用 ForEntity，
                        // 任务发的书会进另一把键下的背包 grain —— 玩家"交完任务"却在行囊里看不见，
                        // 而两端各自成功、没有任何报错（真集群实测出来的分岔）。
                        var inventory = _clusterClient.GetGrain<IInventoryGrain>(
                            Horizon.Game.Core.Sim.Server.GrainKeyMapper.ToInventoryKey(characterId));
                        if (!await inventory.AddItemAsync(templateId, value))
                            Logger.LogWarning("任务奖励发放失败（背包拒收）: CharacterId={CharacterId} quest={QuestId} item={ItemId} count={Count}",
                                characterId, questId, templateId, value);
                    }
                    else if (parts.Length == 2 && parts[0].Equals("favor", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(parts[1], out var domain))
                    {
                        var favor = _clusterClient.GetGrain<IFavorabilityGrain>(GrainKeys.ForEntity(characterId));
                        // 来源记到任务上：同一个任务反复交不能无限刷好感（上限按任务给）
                        await favor.AddAsync(domain, value, $"quest:{questId}", perTargetCap: value);
                    }
                    else if (parts.Length == 1 && parts[0].Equals("exp", StringComparison.OrdinalIgnoreCase))
                    {
                        // 经验必须走服务端唯一入口：它同时决定等级与升等节奏（§14 的等级门槛靠它才可达）
                        var character = _clusterClient.GetGrain<ICharacterGrain>((long)characterId);
                        var result = await character.AddExpAsync(value);
                        if (!result.Success)
                            Logger.LogWarning("任务经验发放被拒: CharacterId={CharacterId} quest={QuestId} amount={Amount} msg={Message}",
                                characterId, questId, value, result.Message);
                    }
                    else
                    {
                        Logger.LogInformation("任务奖励键 {Key} 不在已接线的发放约定内，未处理。quest={QuestId}", key, questId);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "发放任务奖励异常: CharacterId={CharacterId} quest={QuestId} key={Key}",
                        characterId, questId, key);
                }
            }
        }
    }
}