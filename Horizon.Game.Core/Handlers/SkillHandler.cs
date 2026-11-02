using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Game.Sim.Economy;
using Horizon.Game.Sim.Npcs;
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
    public class SkillHandler : MessageHandlerBase
    {
        /// <summary>世界实体身份注册表（DS 上行落地点）。null = 通道未接线，训练师交互一律不采信。</summary>
        private readonly Horizon.Game.Core.Sim.Server.DsEntityRegistry? _entityRegistry;

        /// <summary>角色 → shard 路由。</summary>
        private readonly Horizon.Game.Core.World.IShardRouter? _shardRouter;

        public SkillHandler(
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
            MessageType.LearnSkill,
            MessageType.SkillCooldown,
            MessageType.SkillProficiency,
            MessageType.UpgradeSkill,
            MessageType.SkillCast,
            MessageType.Attack,
            MessageType.QingGong,
            MessageType.NeiGong,
            MessageType.ComboAttack,
            MessageType.Defense,
            // 技能模板目录下行。只有本处理器读它，不与其他处理器争抢同一消息类型。
            MessageType.SkillCatalogSync,
            // 镇岳（§5.3）：护体行只存在于 SkillGrain 的状态里，判定也就必须落在那边。
            MessageType.SetGuardElement
        };

        public override ServiceType ServiceType => ServiceType.Combat;



        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            return await base.HandleAsync(client, message);
        }

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {

            switch (message.Header.MessageType)
            {
                default:
                case MessageType.LearnSkill:
                    return await HandleLearnSkillAsync(message);
                case MessageType.SkillCooldown:
                    return await HandleSkillCooldownAsync(message);
                case MessageType.SkillProficiency:
                    return await HandleSkillProficiencyAsync(message);
                case MessageType.UpgradeSkill:
                    return await HandleUpgradeSkillAsync(message);
                case MessageType.SkillCast:
                    return await HandleSkillCastAsync(message);
                case MessageType.SkillCatalogSync:
                    return await HandleSkillCatalogSyncAsync(message);
                case MessageType.SetGuardElement:
                    return await HandleSetGuardElementAsync(message);
                case MessageType.Attack:
                    return await HandleAttackAsync(message);
                case MessageType.QingGong:
                    return await HandleQingGongAsync(message);
                case MessageType.NeiGong:
                    return await HandleNeiGongAsync(message);
                case MessageType.ComboAttack:
                    return await HandleComboAttackAsync(message);
                case MessageType.Defense:
                    return await HandleDefenseAsync(message);
            }
        }

        /// <summary>
        /// 技能目录下行：服务端技能表 + 该角色已学技能，一次回给客户端。
        /// <para>
        /// CharacterId 在这里盖章——grain 只认自己的键，不知道请求方要的是谁。
        /// 失败一律回 <c>Success=false</c> 而不抛：客户端据此保留本地兜底表，技能栏不会整屏清空。
        /// </para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSkillCatalogSyncAsync(HorizonMessagePacket message)
        {
            var request = message.Body as SkillCatalogSyncMessage;
            var characterId = request?.CharacterId ?? 0UL;

            if (request is null || characterId == 0UL)
                return (true, CreateHorizonMessage(new SkillCatalogSyncMessage { Success = false, Message = "角色ID无效" }));

            try
            {
                var skillGrain = _clusterClient.GetGrain<ISkillGrain>(GrainKeys.ForEntity(characterId));
                var response = await skillGrain.GetSkillCatalogAsync();
                response.CharacterId = characterId;
                await FillFavorSnapshotAsync(characterId, response);
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理技能目录同步失败。CharacterId={CharacterId}", characterId);
                return (true, CreateHorizonMessage(new SkillCatalogSyncMessage
                {
                    CharacterId = characterId,
                    Success = false,
                    Message = "获取技能目录失败",
                }));
            }
        }

        /// <summary>
        /// 顺手带一份分域好感快照，让树页能说"火域好感 24/60"而不是只说"需 60"。
        /// <para>
        /// 取不到就让它保持 null：客户端据此退回"只说要什么"。这里<b>不能</b>因为好感读失败
        /// 就把整次目录同步判失败——那会让技能树整屏空掉，而门槛判定本来就在服务端，不影响 correctness。
        /// 也不能退化成发空表：空表在客户端的口径里是"服务端确认这人零好感"。
        /// </para>
        /// <para>
        /// 键用 <see cref="GrainKeys.ForEntity"/>：与写入侧（击杀/任务发好感）同一把键。
        /// 背包那把大端 Guid 键读到的永远是零，这个错配本仓已经踩过一次。
        /// </para>
        /// </summary>
        private async Task FillFavorSnapshotAsync(ulong characterId, SkillCatalogSyncMessage response)
        {
            if (!response.Success) return;
            try
            {
                var favor = _clusterClient.GetGrain<IFavorabilityGrain>(GrainKeys.ForEntity(characterId));
                var byDomain = await favor.GetByDomainAsync();
                if (byDomain != null) response.FavorByDomain = byDomain;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "读取分域好感快照失败，技能目录仍照常下行。CharacterId={CharacterId}", characterId);
            }
        }

        /// <summary>
        /// 镇岳设定：冷却与"这一行练没练过"都在 SkillGrain 判（那边才有状态），这里只寻址、如实回。
        /// 失败一律回 <c>Success=false</c> 加生效行，让客户端能回滚选中态而不是留着假亮。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSetGuardElementAsync(HorizonMessagePacket message)
        {
            var request = message.Body as SetGuardElementMessage;
            var characterId = request?.CharacterId ?? 0UL;

            if (request is null || characterId == 0UL)
                return (true, CreateHorizonMessage(new SetGuardElementMessage { Success = false, Message = "角色ID无效" }));

            try
            {
                var skillGrain = _clusterClient.GetGrain<ISkillGrain>(GrainKeys.ForEntity(characterId));
                var result = await skillGrain.SetGuardElementAsync(request.Element);

                return (true, CreateHorizonMessage(new SetGuardElementMessage
                {
                    CharacterId = characterId,
                    Element = request.Element,
                    Success = result.Success,
                    Message = result.Message,
                    GuardElement = result.GuardElement,
                    NextChangeInSeconds = result.NextChangeInSeconds,
                }));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理镇岳设定失败。CharacterId={CharacterId}", characterId);
                return (true, CreateHorizonMessage(new SetGuardElementMessage
                {
                    CharacterId = characterId,
                    Success = false,
                    Message = "镇岳设定失败",
                }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleLearnSkillAsync(HorizonMessagePacket message)
        {
            try
            {
                LearnSkillRequest learnSkillRequest = message.Body as LearnSkillRequest;
                var skillGrainKey = GrainKeys.ForEntity(learnSkillRequest.CharacterId);
                var skillGrain = _clusterClient.GetGrain<ISkillGrain>(skillGrainKey);

                // §14.5：不再按客户端上报的 LearnMethod 选通道。真研读发生在"背包里使用物品"那条路上
                // （InventoryGrain 会扣书、按成功率掷签、成功后扣材料）；这里收到的请求一律走直接学习，
                // 而登记过秘籍的行会被 SkillGrain 原样拒回"此式需研读《X》"——
                // 否则客户端只要声明一个 LearnMethod=2 就能白读一卷并不存在的书。
                //
                // 判定要用的事实同样由服务端装配（材料/好感/属性都不许客户端上报）。
                var inventory = _clusterClient.GetGrain<IInventoryGrain>(
                    GrainKeys.ForEntity(learnSkillRequest.CharacterId));
                // ── NPC 训练师分支（EntityId != 0）──
                // 学费、教不教、材料代价全由服务端从已装配的训练师行裁决；
                // 门槛仍走 SkillGrain.CheckLearnGates（这里绝不重判一遍）。
                ICurrencyGrain? wallet = null;
                long trainerCost = 0;
                int trainerItemId = 0;
                int trainerItemCount = 0;

                if (learnSkillRequest.EntityId != 0)
                {
                    var trainer = await ResolveTrainerAsync(
                        learnSkillRequest.CharacterId, learnSkillRequest.EntityId, learnSkillRequest.SkillId);

                    if (!trainer.Ok)
                        return (true, CreateHorizonMessage(new LearnSkillResponse { Success = false, Message = trainer.Reason }));

                    trainerCost = trainer.CostGold;
                    trainerItemId = trainer.CostItemId;
                    trainerItemCount = trainer.CostItemCount;

                    if (trainerCost > 0)
                    {
                        wallet = _clusterClient.GetGrain<ICurrencyGrain>((long)learnSkillRequest.CharacterId);
                        var spend = await wallet.SpendAsync(WalletCurrency.Gold, trainerCost,
                            $"npc_trainer:{trainer.NpcId}:{learnSkillRequest.SkillId}");
                        if (!spend.Ok)
                        {
                            var why = spend.Reject == (int)WalletReject.InsufficientFunds ? "金币不足，学不了这一式" : "扣费失败";
                            return (true, CreateHorizonMessage(new LearnSkillResponse { Success = false, Message = why }));
                        }
                    }

                    if (trainerItemId > 0 && trainerItemCount > 0)
                    {
                        var consumed = await inventory.ConsumeByTemplateAsync(
                            learnSkillRequest.CharacterId, trainerItemId, trainerItemCount);
                        if (!consumed)
                        {
                            await RefundLearnAsync(wallet, trainerCost, $"npc_trainer_refund:{learnSkillRequest.SkillId}");
                            return (true, CreateHorizonMessage(new LearnSkillResponse { Success = false, Message = "缺少教学材料" }));
                        }
                    }
                }

                var gate = await inventory.BuildLearnGateContextAsync(learnSkillRequest.CharacterId);

                var result = await skillGrain.LearnSkillAsync(
                    (long)learnSkillRequest.CharacterId, learnSkillRequest.SkillId, gate);

                if (!result.Success && (trainerCost > 0 || (trainerItemId > 0 && trainerItemCount > 0)))
                {
                    // 门槛失败是常规路径（等级/境界/前置没到），不是异常：钱与材料必须原路退回。
                    await RefundLearnAsync(wallet, trainerCost, $"npc_trainer_refund:{learnSkillRequest.SkillId}");
                    if (trainerItemId > 0 && trainerItemCount > 0)
                    {
                        await inventory.AddItemAsync(trainerItemId, trainerItemCount);
                    }
                }

                if (result.Success)
                {
                    // 直接学习通道通常不带材料（带材料的都登记了书），但清单是数据说了算，
                    // 所以这里照回执扣——判定与扣减分处两方，中间没有第二个人能动背包。
                    for (int i = 0; i < result.MaterialItemIds.Count && i < result.MaterialCounts.Count; i++)
                    {
                        int templateId = result.MaterialItemIds[i];
                        int need = result.MaterialCounts[i];
                        if (need <= 0) continue;
                        if (!await inventory.ConsumeByTemplateAsync(learnSkillRequest.CharacterId, templateId, need))
                            Logger.LogWarning(
                                "习得成功但材料扣减不完整: CharacterId={CharacterId} SkillId={SkillId} template={TemplateId} need={Need}",
                                learnSkillRequest.CharacterId, learnSkillRequest.SkillId, templateId, need);
                    }
                }

                if (result.Success) InvalidateCombatProfile(learnSkillRequest.CharacterId);

                result.LearnedSkill ??= new SkillInfo { SkillId = learnSkillRequest.SkillId };
                if (result.LearnedSkill.SkillId <= 0) result.LearnedSkill.SkillId = learnSkillRequest.SkillId;
                // 失败原因（缺哪一卷、差几份灵材、哪一域好感不够）就照 Grain 给的原文下行，
                // 一句"学习失败"只会让人以为技能树坏了。
                var tem = CreateHorizonMessage(result);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理学习技能消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理学习技能消息失败" }));
            }
        }

        /// <summary>训练师裁决结果（失败时 <see cref="Reason"/> 直接下行给玩家）。</summary>
        private readonly record struct TrainerResolution(bool Ok, string Reason, int NpcId, long CostGold, int CostItemId, int CostItemCount);

        /// <summary>
        /// 解析"正在教这一式的 NPC"：实体必须在本 shard 存在、可交互、离玩家够近，且
        /// NPC 位图声明训练服务、名下确有这一式的教学行。
        /// </summary>
        private async Task<TrainerResolution> ResolveTrainerAsync(ulong characterId, ulong entityId, int skillId)
        {
            try
            {
                var shardId = _shardRouter?.Resolve((long)characterId) ?? 0L;

                var character = _clusterClient.GetGrain<ICharacterGrain>((long)characterId);
                var cached = await character.GetLastPositionAsync();
                if (cached is not { } p) return new TrainerResolution(false, "读不到你的位置，暂时无法与训练师结算", 0, 0, 0, 0);

                var flaxPos = Sim.Server.QuestInteractionAuthority.ToFlax(p.X, p.Y, p.Z);
                var verdict = Sim.Server.QuestInteractionAuthority.Evaluate(_entityRegistry, shardId, entityId, flaxPos);
                if (!verdict.Ok) return new TrainerResolution(false, "与训练师的距离或身份校验未通过", 0, 0, 0, 0);

                if (!NpcContent.TryGetConfig(verdict.TemplateId, out var config) || config is null)
                    return new TrainerResolution(false, "找不到这个训练师", 0, 0, 0, 0);

                if ((config.ServiceFlags & (uint)NpcServiceBits.Trainer) == 0)
                    return new TrainerResolution(false, "这位 NPC 不教技能", 0, 0, 0, 0);

                var rows = NpcContent.TrainerRows(verdict.TemplateId);
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row.SkillId != skillId || row.Service != NpcTrainerService.Skill) continue;
                    return new TrainerResolution(true, string.Empty, verdict.TemplateId, row.CostGold, row.CostItemId, row.CostItemCount);
                }

                return new TrainerResolution(false, "这位训练师不教这一式", 0, 0, 0, 0);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "解析训练师失败 CharacterId={CharacterId} EntityId={EntityId}", characterId, entityId);
                return new TrainerResolution(false, "训练师结算失败，请稍后再试", 0, 0, 0, 0);
            }
        }

        /// <summary>退款（补偿用）：失败只记日志，此时调用方已经在返回失败回执。</summary>
        private async Task RefundLearnAsync(ICurrencyGrain? wallet, long amount, string label)
        {
            if (wallet is null || amount <= 0) return;

            try
            {
                var refund = await wallet.AddAsync(WalletCurrency.Gold, amount, label);
                if (!refund.Ok) Logger.LogError("学技退款失败：{Amount} 金币（{Label}）", amount, label);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "学技退款异常：{Amount} 金币（{Label}）", amount, label);
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSkillCooldownAsync(HorizonMessagePacket message)
        {
            try
            {
                SkillCooldownQueryRequest skillCooldownQueryRequest = message.Body as SkillCooldownQueryRequest;
                var skillGrainKey = GrainKeys.ForEntity(skillCooldownQueryRequest.CharacterId);
                var skillGrain = _clusterClient.GetGrain<ISkillGrain>(skillGrainKey);
                var cooldowns = new Dictionary<int, long>();
                foreach (var skillId in skillCooldownQueryRequest.SkillIds)
                {
                    var remaining = await skillGrain.GetSkillCooldownAsync(skillId);
                    cooldowns[skillId] = (long)(remaining * 1000);
                }
                var response = new SkillCooldownQueryResponse
                {
                    CharacterId = skillCooldownQueryRequest.CharacterId,
                    SkillCooldowns = cooldowns
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理技能冷却查询消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理技能冷却查询消息失败" }));
            }
        }

        /// <summary>
        /// 熟练度查询：**这条通道按 D1 已经是遗留物**，这里恒回空表是刻意的，不是没写完。
        /// <para>
        /// 设计早期把"圆满"定义成熟练度满 + <c>skill_realm</c> 破境；D1 改成"三境各为一式"之后，
        /// 圆满的定义换成了 <c>Level == MaxLevel</c>（§6.2），熟练度既没有存储（<c>GrainStateModels</c> 里那个
        /// <c>Proficiency</c> 是**炼丹**熟练度）、没有上限数据（技能表没这一列）、也没有读者（客户端零引用）。
        /// </para>
        /// <para>
        /// 所以要改的不是这个返回值，而是先回答"熟练度与等级轴谁作准"（任务 #34）：
        /// 要么撤销 D1 把熟练度重新立起来，要么把 <c>SkillProficiency*</c> 三个协议消息一起删掉。
        /// 在此之前别在这里"顺手补个数"——补出来的任何数字都是一条没人认领的第二真相源。
        /// </para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSkillProficiencyAsync(HorizonMessagePacket message)
        {
            try
            {
                SkillProficiencyQueryRequest skillProficiencyQueryRequest = message.Body as SkillProficiencyQueryRequest;
                // 处理技能熟练度查询逻辑
                var response = new SkillProficiencyQueryResponse
                {
                    CharacterId = skillProficiencyQueryRequest.CharacterId,
                    SkillProficiencies = new Dictionary<int, int>()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理技能熟练度查询消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理技能熟练度查询消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleUpgradeSkillAsync(HorizonMessagePacket message)
        {
            try
            {
                UpgradeSkillRequest upgradeSkillRequest = message.Body as UpgradeSkillRequest;
                var skillGrainKey = GrainKeys.ForEntity(upgradeSkillRequest.CharacterId);
                var skillGrain = _clusterClient.GetGrain<ISkillGrain>(skillGrainKey);
                var success = await skillGrain.UpgradeSkillAsync(upgradeSkillRequest.SkillId);
                if (success) InvalidateCombatProfile(upgradeSkillRequest.CharacterId);
                var response = new UpgradeSkillResponse
                {
                    Success = success,
                    Message = success ? "技能升级成功" : "技能升级失败",
                    UpgradedSkill = new SkillInfo { SkillId = upgradeSkillRequest.SkillId },
                    ConsumedGold = 0,
                    ConsumedItems = new List<long>(),
                    ConsumedExperience = 0
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理升级技能消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理升级技能消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSkillCastAsync(HorizonMessagePacket message)
        {
            try
            {
                SkillCastMessage skillCastMessage = message.Body as SkillCastMessage;
                var skillGrainKey = GrainKeys.ForEntity(skillCastMessage.CasterId);
                var skillGrain = _clusterClient.GetGrain<ISkillGrain>(skillGrainKey);
                var castResult = await skillGrain.CastSkillAsync(skillCastMessage);
                if (!castResult.Success)
                {
                    var failResponse = new SkillCastMessage
                    {
                        CasterId = skillCastMessage.CasterId,
                        SkillId = skillCastMessage.SkillId,
                        Success = false,
                        Message = castResult.Message
                    };
                    return (true, CreateHorizonMessage(failResponse));
                }
                // CombatGrain 按受击方寻址：并发打同一目标的多名攻方串行落在同一个 Grain，
                // 判死与 HP 回写只发生一次（原先的 Guid.NewGuid() 让每次施法都新激活一个 Grain，
                // CombatParticipants 缓存与战斗日志因此永远命不中）。
                var combatGrainKey = GrainKeys.ForEngagement(skillCastMessage.TargetEntityId, skillCastMessage.CasterId);
                var combatGrain = _clusterClient.GetGrain<ICombatGrain>(combatGrainKey);
                var combatResult = await combatGrain.ProcessSkillCastAsync(castResult);
                var response = new AttributeUpdateMessage
                {
                    CharacterId = skillCastMessage.CasterId,
                    UpdateTime = DateTime.UtcNow.Ticks,
                    AttributeChanges = new Dictionary<string, object>
                    {
                        { "SkillId", combatResult.SkillId },
                        { "Success", combatResult.Success },
                        { "EnergyCost", combatResult.EnergyCost },
                        { "Message", combatResult.Message },
                        // 五行循环节的权威值：HUD 指示器据此校正本地预测，玩家看到的层数不再是猜的
                        { "LoopStack", combatResult.LoopStack },
                        { "LoopMultiplier", combatResult.LoopMultiplier },
                        { "CycleCompleted", combatResult.CycleCompleted },
                        { "LoopCompletionCount", combatResult.LoopCompletionCount }
                    }
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理技能施放消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理技能施放消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleAttackAsync(HorizonMessagePacket message)
        {
            try
            {
                AttackMessage attackMessage = message.Body as AttackMessage;
                // 同上：按受击方寻址 CombatGrain，保证同一目标的伤害结算串行且状态连续。
                var combatGrainKey = GrainKeys.ForEngagement(attackMessage.TargetId, attackMessage.AttackerId);
                var combatGrain = _clusterClient.GetGrain<ICombatGrain>(combatGrainKey);
                var damageResult = await combatGrain.ProcessAttackAsync(attackMessage);
                var tem = CreateHorizonMessage(damageResult);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理攻击消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理攻击消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleQingGongAsync(HorizonMessagePacket message)
        {
            try
            {
                QingGongMessage qingGongMessage = message.Body as QingGongMessage;
                // 轻功消息直接回显（确定性移动由 Sim 层 MovementFormulaStepper 处理，此处仅确认技能使用）。
                var response = new QingGongMessage
                {
                    CharacterId = qingGongMessage.CharacterId,
                    QingGongSkillId = qingGongMessage.QingGongSkillId,
                    StartPosition = qingGongMessage.StartPosition,
                    TargetPosition = qingGongMessage.TargetPosition,
                    PathPoints = qingGongMessage.PathPoints
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理轻功消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理轻功消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleNeiGongAsync(HorizonMessagePacket message)
        {
            try
            {
                NeiGongMessage neiGongMessage = message.Body as NeiGongMessage;
                // 处理内功逻辑
                var response = new NeiGongMessage
                {
                    CharacterId = neiGongMessage.CharacterId,
                    NeiGongSkillId = neiGongMessage.NeiGongSkillId,
                    NeiLiChange = neiGongMessage.NeiLiChange,
                    CurrentNeiLi = neiGongMessage.CurrentNeiLi,
                    Duration = neiGongMessage.Duration
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理内功消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理内功消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleComboAttackAsync(HorizonMessagePacket message)
        {
            try
            {
                ComboAttackMessage comboAttackMessage = message.Body as ComboAttackMessage;
                // 处理招式连击逻辑
                var response = new ComboAttackMessage
                {
                    AttackerId = comboAttackMessage.AttackerId,
                    ComboSequence = comboAttackMessage.ComboSequence,
                    TargetId = comboAttackMessage.TargetId,
                    TotalDamage = comboAttackMessage.TotalDamage,
                    ComboMultiplier = comboAttackMessage.ComboMultiplier
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理招式连击消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理招式连击消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleDefenseAsync(HorizonMessagePacket message)
        {
            try
            {
                DefenseMessage defenseMessage = message.Body as DefenseMessage;
                // 处理防御逻辑
                var response = new DefenseMessage
                {
                    DefenderId = defenseMessage.DefenderId,
                    AttackerId = defenseMessage.AttackerId,
                    DefenseType = defenseMessage.DefenseType,
                    DefenseValue = defenseMessage.DefenseValue,
                    IsSuccess = defenseMessage.IsSuccess
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理防御消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理防御消息失败" }));
            }
        }

    }
}