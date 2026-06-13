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
    public class SkillHandler : MessageHandlerBase
    {
        public SkillHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter) : base(logger, clusterClient, adapter)
        {

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
            MessageType.Defense
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

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleLearnSkillAsync(HorizonMessagePacket message)
        {
            try
            {
                LearnSkillRequest learnSkillRequest = message.Body as LearnSkillRequest;
                var skillGrainKey = GuidFromUlong(learnSkillRequest.CharacterId);
                var skillGrain = _clusterClient.GetGrain<ISkillGrain>(skillGrainKey);
                var success = await skillGrain.LearnSkillAsync(learnSkillRequest.SkillId);
                var response = new LearnSkillResponse
                {
                    Success = success,
                    Message = success ? "技能学习成功" : "技能学习失败",
                    LearnedSkill = new SkillInfo { SkillId = learnSkillRequest.SkillId },
                    ConsumedGold = 0,
                    ConsumedItems = new List<long>()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理学习技能消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理学习技能消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSkillCooldownAsync(HorizonMessagePacket message)
        {
            try
            {
                SkillCooldownQueryRequest skillCooldownQueryRequest = message.Body as SkillCooldownQueryRequest;
                var skillGrainKey = GuidFromUlong(skillCooldownQueryRequest.CharacterId);
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
                var skillGrainKey = GuidFromUlong(upgradeSkillRequest.CharacterId);
                var skillGrain = _clusterClient.GetGrain<ISkillGrain>(skillGrainKey);
                var success = await skillGrain.UpgradeSkillAsync(upgradeSkillRequest.SkillId);
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
                var skillGrainKey = GuidFromUlong(skillCastMessage.CasterId);
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
                var combatGrainKey = Guid.NewGuid();
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
                        { "Message", combatResult.Message }
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
                var combatGrainKey = Guid.NewGuid();
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
                var characterGrain = _clusterClient.GetGrain<ICharacterGrain>((long)qingGongMessage.CharacterId);
                var result = await characterGrain.UseQingGongAsync(qingGongMessage);
                var tem = CreateHorizonMessage(result);
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

        private static Guid GuidFromUlong(ulong value)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(value).CopyTo(bytes, 0);
            return new Guid(bytes);
        }
    }
}