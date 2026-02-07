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
                // 处理学习技能逻辑
                var response = new LearnSkillResponse
                {
                    Success = true,
                    Message = "技能学习成功",
                    LearnedSkill = new SkillInfo(),
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
                // 处理技能冷却查询逻辑
                var response = new SkillCooldownQueryResponse
                {
                    CharacterId = skillCooldownQueryRequest.CharacterId,
                    SkillCooldowns = new Dictionary<int, long>()
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
                // 处理升级技能逻辑
                var response = new UpgradeSkillResponse
                {
                    Success = true,
                    Message = "技能升级成功",
                    UpgradedSkill = new SkillInfo(),
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
                // 处理技能施放逻辑
                var response = new AttributeUpdateMessage
                {
                    CharacterId = skillCastMessage.CasterId,
                    UpdateTime = DateTime.UtcNow.Ticks,
                    AttributeChanges = new Dictionary<string, object>()
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
                // 处理攻击逻辑
                var response = new DamageMessage
                {
                    VictimId = attackMessage.TargetId,
                    AttackerId = attackMessage.AttackerId,
                    Damage = attackMessage.Damage,
                    IsCritical = attackMessage.IsCritical,
                    RemainingHealth = 100,
                    IsDodged = false,
                    IsBlocked = false
                };
                var tem = CreateHorizonMessage(response);
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
                // 处理轻功逻辑
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