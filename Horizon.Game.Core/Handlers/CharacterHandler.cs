using Horizon.Core.Security;
using Horizon.Game.Core.Security;
using Horizon.Game.Core.Interfaces;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using log4net.Repository.Hierarchy;
using MemoryPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TouchSocket.Sockets;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 角色消息处理器
    /// 处理与游戏角色相关的消息，包括角色创建、进入游戏、移动、战斗、技能等
    /// </summary>
    public class CharacterHandler : MessageHandlerBase
    {
        private readonly AuthenticationValidator _validator;
        private readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory;
        private readonly ICharacterFingerprintService _fingerprintService;
        private readonly UserAuthTokenProvider _authTokenProvider;
        /// <summary>
        /// 构造函数
        /// 初始化角色消息处理器
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="clusterClient">Orleans集群客户端</param>
        public CharacterHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, HorizonMessageAdapter adapter,
            Microsoft.Extensions.Logging.ILoggerFactory loggerFactory = null,
            AuthenticationValidator validator = null,
            ICharacterFingerprintService characterFingerprintService = null,
            UserAuthTokenProvider authTokenProvider = null) : base(logger, clusterClient, adapter)
        {
            _loggerFactory = loggerFactory;
            _validator = validator ?? new AuthenticationValidator(
               _loggerFactory?.CreateLogger<AuthenticationValidator>() ??
               logger as ILogger<AuthenticationValidator> ??
               new Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthenticationValidator>());
            _fingerprintService = characterFingerprintService;
            _authTokenProvider = authTokenProvider;
        }


        /// <summary>
        /// 获取此处理器支持的消息类型列表
        /// 包括角色管理、游戏核心玩法、社交、物品、技能、交互和系统消息等
        /// </summary>
        public override List<MessageType> MessageTypes { get; } = new List<MessageType> {
             MessageType.CharacterList,
            MessageType.CreateCharacter,
            MessageType.SelectCharacter,
            MessageType.CharacterDelete,
            MessageType.CharacterNameCheck,
            MessageType.Appearance,

            MessageType.CreateCharacter,
            MessageType.SelectCharacter,
            MessageType.CharacterDelete,
            MessageType.EnterGame,
            MessageType.Movement,
            MessageType.PlayerAnimation,
            MessageType.PlayerSpawn,
            MessageType.AttributeUpdate,
            MessageType.Attack,
            MessageType.SkillCast,
            MessageType.QingGong,
            MessageType.NeiGong,
            MessageType.ComboAttack,
            MessageType.Defense,
            MessageType.Damage,
            MessageType.Death,
            MessageType.Resurrect,
            MessageType.SectInfo,
            MessageType.JoinSect,
            MessageType.SectSkill,
            MessageType.SectQuest,
            MessageType.Reputation,
            MessageType.ChivalryPoint,
            MessageType.Duel,
            MessageType.SwornBrother,
            MessageType.MasterApprentice,
            MessageType.InventoryUpdate,
            MessageType.EquipItem,
            MessageType.WeaponSwitch,
            MessageType.UseItem,
            MessageType.EquipmentInfo,
            MessageType.EquipmentEnhance,
            MessageType.EquipmentRefine,
            MessageType.Crafting,
            MessageType.CraftingResult,
            MessageType.AttributeInheritance,
            MessageType.WuXingCrafting,
            MessageType.LearnSkill,
            MessageType.SkillCooldown,
            MessageType.SkillProficiency,
            MessageType.UpgradeSkill,
            MessageType.Chat,
            MessageType.Friend,
            MessageType.Team,
            MessageType.Guild,
            MessageType.QuestUpdate,
            MessageType.AcceptQuest,
            MessageType.CompleteQuest,
        };

        /// <summary>
        /// 获取此处理器支持的服务类型
        /// </summary>
        public override ServiceType ServiceType => ServiceType.Game;



        /// <summary>
        /// 异步处理消息
        /// </summary>
        /// <param name="client">TCP会话客户端</param>
        /// <param name="message">消息包</param>
        /// <returns>处理结果和响应消息</returns>
        public override async Task<(bool IsSuccess, MessageUnion? Response)> HandleAsync(ITcpSessionClient client, HorizonMessagePacket message)
        {
            return await base.HandleAsync(client, message);
        }

        /// <summary>
        /// 路由消息处理
        /// 根据消息类型将消息路由到相应的处理方法
        /// </summary>
        /// <param name="message">消息包</param>
        /// <returns>处理结果和响应消息包</returns>
        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {

            switch (message.Header.MessageType)
            {
                default:
                case MessageType.CharacterList:
                    return await HandleCharacterListRequestAsync(message);

                case MessageType.CharacterDelete:
                    return await HandleDeleteCharacterAsync(message);
                case MessageType.CharacterNameCheck:
                    return await HandleCharacterNameCheckAsync(message);

                case MessageType.CreateCharacter:
                    return await CreateCharacterAsync(message);
                case MessageType.SelectCharacter:
                    return await SelectCharacterAsync(message);
                case MessageType.EnterGame:
                    return await CharacterEnterGameAsync(message);
                case MessageType.Movement:
                    return await HandleMovementAsync(message);
                case MessageType.Attack:
                    return await HandleAttackAsync(message);
                case MessageType.SkillCast:
                    return await HandleSkillCastAsync(message);
                case MessageType.Chat:
                    return await HandleChatAsync(message);
                case MessageType.UseItem:
                    return await HandleUseItemAsync(message);
                case MessageType.EquipItem:
                    return await HandleEquipItemAsync(message);
                case MessageType.LearnSkill:
                    return await HandleLearnSkillAsync(message);
                case MessageType.UpgradeSkill:
                    return await HandleUpgradeSkillAsync(message);
                // 新增的消息类型处理
                case MessageType.QingGong:
                    return await HandleQingGongAsync(message);
                case MessageType.NeiGong:
                    return await HandleNeiGongAsync(message);
                case MessageType.ComboAttack:
                    return await HandleComboAttackAsync(message);
                case MessageType.Defense:
                    return await HandleDefenseAsync(message);
                case MessageType.Damage:
                    return await HandleDamageAsync(message);
                case MessageType.Death:
                    return await HandleDeathAsync(message);
                case MessageType.Resurrect:
                    return await HandleResurrectAsync(message);
                case MessageType.SectInfo:
                    return await HandleSectInfoAsync(message);
                case MessageType.JoinSect:
                    return await HandleJoinSectAsync(message);
                case MessageType.SectSkill:
                    return await HandleSectSkillAsync(message);
                case MessageType.SectQuest:
                    return await HandleSectQuestAsync(message);
                case MessageType.Reputation:
                    return await HandleReputationAsync(message);
                case MessageType.ChivalryPoint:
                    return await HandleChivalryPointAsync(message);
                case MessageType.Duel:
                    return await HandleDuelAsync(message);
                case MessageType.SwornBrother:
                    return await HandleSwornBrotherAsync(message);
                case MessageType.MasterApprentice:
                    return await HandleMasterApprenticeAsync(message);
                case MessageType.InventoryUpdate:
                    return await HandleInventoryUpdateAsync(message);
                case MessageType.WeaponSwitch:
                    return await HandleWeaponSwitchAsync(message);
                case MessageType.EquipmentInfo:
                    return await HandleEquipmentInfoAsync(message);
                case MessageType.EquipmentEnhance:
                    return await HandleEquipmentEnhanceAsync(message);
                case MessageType.EquipmentRefine:
                    return await HandleEquipmentRefineAsync(message);
                case MessageType.Crafting:
                    return await HandleCraftingAsync(message);
                case MessageType.CraftingResult:
                    return await HandleCraftingResultAsync(message);
                case MessageType.AttributeInheritance:
                    return await HandleAttributeInheritanceAsync(message);
                case MessageType.WuXingCrafting:
                    return await HandleWuXingCraftingAsync(message);
                case MessageType.SkillCooldown:
                    return await HandleSkillCooldownAsync(message);
                case MessageType.SkillProficiency:
                    return await HandleSkillProficiencyAsync(message);
                case MessageType.Friend:
                    return await HandleFriendAsync(message);
                case MessageType.Team:
                    return await HandleTeamAsync(message);
                case MessageType.Guild:
                    return await HandleGuildAsync(message);
                case MessageType.QuestUpdate:
                    return await HandleQuestUpdateAsync(message);
                case MessageType.AcceptQuest:
                    return await HandleAcceptQuestAsync(message);
                case MessageType.CompleteQuest:
                    return await HandleCompleteQuestAsync(message);
            }
        }
        /// <summary>
        /// 处理角色列表请求
        /// 使用用户角色管理器获取用户所有角色
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCharacterListRequestAsync(HorizonMessagePacket message)
        {
            try
            {
                var listRequest = message.Body as CharacterListRequest;
                if (listRequest == null)
                {
                    Logger.LogError("无法反序列化角色列表请求消息");
                    return (false, CreateCharacterListErrorResponse("角色列表请求格式错误"));
                }

                Logger.LogInformation("处理角色列表请求: UserId: {UserId}, ServerId: {ServerId}",
                    listRequest.UserId, listRequest.ServerId);

                // 使用用户角色管理器获取角色列表
                var userCharacterManager = _clusterClient.GetGrain<IUserCharacterManagerGrain>((long)listRequest.UserId);
                var characters = await userCharacterManager.GetAllCharactersAsync((int)message.Header.GameId);

                // 构建响应
                var response = new CharacterListResponse
                {
                    IsSuccess = true,
                    Characters = characters,
                    ErrorMessage = "",
                    MaxCharacterCount = 5
                };

                Logger.LogInformation("成功获取角色列表: UserId: {UserId}, 角色数量: {Count}",
                    listRequest.UserId, characters.Count);

                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理角色列表请求时发生异常");
                return (false, CreateCharacterListErrorResponse("获取角色列表失败，请稍后重试。"));
            }
        }
        /// <summary>
        /// 处理删除角色请求
        /// 使用用户角色管理器删除角色
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleDeleteCharacterAsync(HorizonMessagePacket message)
        {
            try
            {
                var deleteRequest = message.Body as DeleteCharacterRequest;
                if (deleteRequest == null)
                {
                    Logger.LogError("无法反序列化删除角色请求消息");
                    return (false, CreateDeleteCharacterErrorResponse("删除角色请求格式错误"));
                }

                Logger.LogInformation("处理删除角色请求: CharacterId: {CharacterId}, UserId: {UserId}",
                    deleteRequest.CharacterId, deleteRequest.UserId);

                // 使用用户角色管理器删除角色
                var userCharacterManager = _clusterClient.GetGrain<IUserCharacterManagerGrain>((long)deleteRequest.UserId);
                var response = await userCharacterManager.DeleteCharacterAsync(deleteRequest.CharacterId, (int)message.Header.GameId);

                Logger.LogInformation("角色删除结果: CharacterId: {CharacterId}, Success: {Success}",
                    deleteRequest.CharacterId, response.Success);

                return (response.Success, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理删除角色请求时发生异常");
                return (false, CreateDeleteCharacterErrorResponse("删除角色处理异常，请稍后重试。"));
            }
        }

        /// <summary>
        /// 处理角色名检查请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCharacterNameCheckAsync(HorizonMessagePacket message)
        {
            try
            {
                var nameCheckRequest = message.Body as ValidateCharacterNameRequest;
                if (nameCheckRequest == null)
                {
                    Logger.LogError("无法反序列化角色名检查请求消息");
                    return (false, CreateNameCheckErrorResponse("角色名检查请求格式错误"));
                }

                Logger.LogInformation("处理角色名检查请求: CharacterName: {CharacterName}",
                    nameCheckRequest.CharacterName);

                // 这里可以添加角色名验证逻辑
                // - 检查长度
                // - 检查特殊字符
                // - 检查敏感词
                // - 检查数据库中是否已存在

                bool isAvailable = await ValidateCharacterName(nameCheckRequest.CharacterName);

                var response = new ValidateCharacterNameResponse
                {
                    IsAvailable = isAvailable,
                    Message = isAvailable ? "角色名可用" : "角色名已被使用或包含非法字符",
                    SuggestedNames = isAvailable ? new List<string>() : GenerateSuggestedNames(nameCheckRequest.CharacterName)
                };

                Logger.LogInformation("角色名检查结果: CharacterName: {CharacterName}, IsAvailable: {IsAvailable}",
                    nameCheckRequest.CharacterName, isAvailable);

                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理角色名检查请求时发生异常");
                return (false, CreateNameCheckErrorResponse("角色名检查处理异常"));
            }
        }
        /// <summary>
        /// 验证角色名
        /// </summary>
        private async Task<bool> ValidateCharacterName(string characterName)
        {
            try
            {
                // 使用验证器验证
                var validation = _validator.ValidateCharacterName(characterName);
                if (!validation.IsValid)
                {
                    Logger.LogWarning("角色名验证失败: {CharacterName}, 错误: {Error}",
                        characterName, validation.ErrorMessage);
                    return false;
                }

                // 这里可以添加更多验证逻辑：
                // - 数据库查重
                // - 高级敏感词过滤

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "验证角色名时发生异常: {CharacterName}", characterName);
                return false;
            }
        }

        /// <summary>
        /// 生成建议的角色名
        /// </summary>
        private List<string> GenerateSuggestedNames(string originalName)
        {
            var suggestions = new List<string>();

            // 简单的建议名称生成策略
            for (int i = 1; i <= 3; i++)
            {
                suggestions.Add($"{originalName}{i}");
                suggestions.Add($"{originalName}_{i}");
            }

            return suggestions;
        }

        /// <summary>
        /// 创建通用错误响应消息
        /// </summary>
        private HorizonMessagePacket CreateErrorResponse(HorizonMessagePacket originalMessage, string errorMessage)
        {
            var errorResponse = new AuthenticationError
            {
                ErrorCode = 500,
                ErrorMessage = errorMessage,
                ErrorDetails = $"处理消息类型 {originalMessage.Header.MessageType} 时发生错误",
                RetryAfterSeconds = 5,
                RequiresReconnect = false
            };

            return CreateHorizonMessage(errorResponse);
        }

        /// <summary>
        /// 创建角色列表错误响应
        /// </summary>
        private HorizonMessagePacket CreateCharacterListErrorResponse(string errorMessage)
        {
            var response = new CharacterListResponse
            {
                IsSuccess = false,
                Characters = new List<CharacterInfo>(),
                ErrorMessage = errorMessage,
                MaxCharacterCount = 5
            };
            return CreateHorizonMessage(response);
        }

        /// <summary>
        /// 创建创建角色错误响应
        /// </summary>
        private HorizonMessagePacket CreateCreateCharacterErrorResponse(string errorMessage)
        {
            var response = new CreateCharacterResponse
            {
                IsSuccess = false,
                Message = errorMessage,
                Character = new CharacterInfo()
            };
            return CreateHorizonMessage(response);
        }

        /// <summary>
        /// 创建删除角色错误响应
        /// </summary>
        private HorizonMessagePacket CreateDeleteCharacterErrorResponse(string errorMessage)
        {
            var response = new DeleteCharacterResponse
            {
                Success = false,
                Message = errorMessage,
                CharacterId = 0
            };
            return CreateHorizonMessage(response);
        }

        /// <summary>
        /// 创建角色名检查错误响应
        /// </summary>
        private HorizonMessagePacket CreateNameCheckErrorResponse(string errorMessage)
        {
            var response = new ValidateCharacterNameResponse
            {
                IsAvailable = false,
                Message = errorMessage,
                SuggestedNames = new List<string>()
            };
            return CreateHorizonMessage(response);
        }


        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> CharacterEnterGameAsync(HorizonMessagePacket message)
        {
            try
            {
                EnterGameRequest entergame = message.Body as EnterGameRequest;

                // 检查角色指纹：防止同一角色同时在线
                if (_fingerprintService != null)
                {
                    var userId = (long)message.Header.UserId;
                    var characterId = (long)entergame.CharacterId;
                    var connectionId = _gameClient?.Id ?? string.Empty;

                    var acquired = await _fingerprintService.TryAcquireAsync(userId, characterId, string.Empty, connectionId);
                    if (!acquired)
                    {
                        Logger.LogWarning("角色 {CharacterId} 已在其他会话中在线，拒绝进入游戏", characterId);
                        return (false, CreateHorizonMessage(new EnterGameResponse
                        {
                            Success = false,
                            Message = "该角色已在其他设备或会话中在线，请先退出后再尝试"
                        }));
                    }
                }

                var passport = _clusterClient.GetGrain<ICharacterGrain>((long)entergame.CharacterId);
                var passportInfo = await passport.EnterGameAsync(entergame);

                TryAttachCharacterAuthToken(message, entergame, passportInfo);

                var tem = CreateHorizonMessage(passportInfo);
                return (passportInfo.Success, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "角色进入游戏失败");
                return (false, CreateHorizonMessage(new CreateCharacterResponse
                {
                    IsSuccess = false,
                    Message = "角色进入游戏失败"
                }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> SelectCharacterAsync(HorizonMessagePacket message)
        {
            try
            {
                EnterGameRequest entergame = message.Body as EnterGameRequest;

                // 切换角色时，先释放当前连接上的旧角色指纹，再获取新角色指纹
                if (_fingerprintService != null)
                {
                    var userId = (long)message.Header.UserId;
                    var characterId = (long)entergame.CharacterId;
                    var connectionId = _gameClient?.Id ?? string.Empty;

                    // 释放当前连接关联的所有旧角色指纹
                    await _fingerprintService.ReleaseByConnectionAsync(connectionId);

                    var acquired = await _fingerprintService.TryAcquireAsync(userId, characterId, string.Empty, connectionId);
                    if (!acquired)
                    {
                        Logger.LogWarning("角色 {CharacterId} 已在其他会话中在线，拒绝选择", characterId);
                        return (false, CreateHorizonMessage(new EnterGameResponse
                        {
                            Success = false,
                            Message = "该角色已在其他设备或会话中在线，请先退出后再尝试"
                        }));
                    }
                }

                var passport = _clusterClient.GetGrain<ICharacterGrain>((long)entergame.CharacterId);
                var passportInfo = await passport.EnterGameAsync(entergame);

                TryAttachCharacterAuthToken(message, entergame, passportInfo);

                var tem = CreateHorizonMessage(passportInfo);
                return (passportInfo.Success, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "角色进入游戏失败");
                return (false, CreateHorizonMessage(new CreateCharacterResponse
                {
                    IsSuccess = false,
                    Message = "角色进入游戏失败"
                }));
            }
        }

        /// <summary>
        /// 角色进入游戏或选择角色成功后，生成含角色Id的新鉴权令牌并附在响应中
        /// </summary>
        private void TryAttachCharacterAuthToken(HorizonMessagePacket message, EnterGameRequest enterGame, EnterGameResponse response)
        {
            if (!response.Success || _authTokenProvider == null)
                return;

            try
            {
                var currentToken = _authTokenProvider.ParseToken(message.Header.AuthToken);
                var passportId = currentToken?.PassportId ?? "";
                var machineId = currentToken?.MachineId ?? "";
                response.AuthToken = _authTokenProvider.GenerateToken(passportId, machineId, (long)enterGame.CharacterId);
                Logger.LogInformation("已为角色 {CharacterId} 生成含角色Id的新鉴权令牌", enterGame.CharacterId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "生成含角色Id的鉴权令牌失败: CharacterId={CharacterId}", enterGame.CharacterId);
            }
        }

        /// <summary>
        /// 异步创建角色
        /// 使用用户角色管理器支持多角色创建
        /// </summary>
        /// <param name="message">创建角色请求消息包</param>
        /// <returns>处理结果和响应消息包</returns>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> CreateCharacterAsync(HorizonMessagePacket message)
        {
            try
            {
                CreateCharacterRequest createCharacter = message.Body as CreateCharacterRequest;
                createCharacter.GameId = (int)message.Header.GameId;
                createCharacter.ZoneId = (int)message.Header.ZoneId;
                createCharacter.ServerId = (int)message.Header.ServerId;
                createCharacter.UserId = message.Header.UserId;

                // 使用用户角色管理器来创建角色，支持多角色
                var userCharacterManager = _clusterClient.GetGrain<IUserCharacterManagerGrain>((long)createCharacter.UserId);
                var response = await userCharacterManager.CreateCharacterAsync(createCharacter);

                var tem = CreateHorizonMessage(response);
                return (response.IsSuccess, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "创建角色失败");
                return (false, CreateHorizonMessage(new CreateCharacterResponse
                {
                    IsSuccess = false,
                    Message = "创建角色失败，请稍后重试。"
                }));
            }
        }

        /// <summary>
        /// 异步处理移动消息
        /// </summary>
        /// <param name="message">移动请求消息包</param>
        /// <returns>处理结果和响应消息包</returns>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleMovementAsync(HorizonMessagePacket message)
        {
            try
            {
                MoveRequest moveRequest = message.Body as MoveRequest;
                // 处理移动逻辑
                var response = new MoveResponse
                {
                    Success = true,
                    CharacterId = moveRequest.CharacterId,
                    CurrentX = moveRequest.TargetX,
                    CurrentY = moveRequest.TargetY,
                    CurrentZ = moveRequest.TargetZ,
                    AcknowledgedSequence = moveRequest.SequenceNumber
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理移动消息失败");
                return (false, CreateHorizonMessage(new MoveResponse { Success = false }));
            }
        }

        /// <summary>
        /// 异步处理攻击消息
        /// </summary>
        /// <param name="message">攻击消息包</param>
        /// <returns>处理结果和响应消息包</returns>
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
                    IsCritical = attackMessage.IsCritical
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

        /// <summary>
        /// 异步处理技能施放消息
        /// </summary>
        /// <param name="message">技能施放消息包</param>
        /// <returns>处理结果和响应消息包</returns>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSkillCastAsync(HorizonMessagePacket message)
        {
            try
            {
                SkillCastMessage skillCastMessage = message.Body as SkillCastMessage;
                // 处理技能施放逻辑
                var response = new AttributeUpdateMessage
                {
                    CharacterId = skillCastMessage.CasterId,
                    UpdateTime = DateTime.UtcNow.Ticks
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

        /// <summary>
        /// 异步处理聊天消息
        /// </summary>
        /// <param name="message">聊天消息包</param>
        /// <returns>处理结果和响应消息包</returns>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChatAsync(HorizonMessagePacket message)
        {
            try
            {
                ChatMessage chatMessage = message.Body as ChatMessage;
                // 处理聊天消息逻辑
                var response = new ChatMessage
                {
                    SenderId = chatMessage.SenderId,
                    SenderName = chatMessage.SenderName,
                    Content = chatMessage.Content,
                    ChannelType = chatMessage.ChannelType,
                    Timestamp = DateTime.UtcNow.Ticks
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理聊天消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理聊天消息失败" }));
            }
        }

        /// <summary>
        /// 异步处理使用物品消息
        /// </summary>
        /// <param name="message">使用物品请求消息包</param>
        /// <returns>处理结果和响应消息包</returns>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleUseItemAsync(HorizonMessagePacket message)
        {
            try
            {
                UseItemRequest useItemRequest = message.Body as UseItemRequest;
                // 处理使用物品逻辑
                var response = new UseItemResponse
                {
                    Success = true,
                    Message = "物品使用成功"
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理使用物品消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理使用物品消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEquipItemAsync(HorizonMessagePacket message)
        {
            try
            {
                EquipItemMessage equipItemMessage = message.Body as EquipItemMessage;
                // 处理装备物品逻辑
                var response = new EquipItemMessage
                {
                    CharacterId = equipItemMessage.CharacterId,
                    ItemId = equipItemMessage.ItemId,
                    Slot = equipItemMessage.Slot,
                    Success = true,
                    Message = "装备成功"
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理装备物品消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理装备物品消息失败" }));
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
                    Message = "技能学习成功"
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

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleUpgradeSkillAsync(HorizonMessagePacket message)
        {
            try
            {
                UpgradeSkillRequest upgradeSkillRequest = message.Body as UpgradeSkillRequest;
                // 处理升级技能逻辑
                var response = new UpgradeSkillResponse
                {
                    Success = true,
                    Message = "技能升级成功"
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

        // 新增的消息处理方法
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

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleDamageAsync(HorizonMessagePacket message)
        {
            try
            {
                DamageMessage damageMessage = message.Body as DamageMessage;
                // 处理受伤逻辑
                var response = new DamageMessage
                {
                    VictimId = damageMessage.VictimId,
                    AttackerId = damageMessage.AttackerId,
                    Damage = damageMessage.Damage,
                    RemainingHealth = damageMessage.RemainingHealth,
                    IsCritical = damageMessage.IsCritical,
                    IsDodged = damageMessage.IsDodged,
                    IsBlocked = damageMessage.IsBlocked
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理受伤消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理受伤消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleDeathAsync(HorizonMessagePacket message)
        {
            try
            {
                DeathMessage deathMessage = message.Body as DeathMessage;
                // 处理死亡逻辑
                var response = new DeathMessage
                {
                    DeceasedId = deathMessage.DeceasedId,
                    KillerId = deathMessage.KillerId,
                    Cause = deathMessage.Cause,
                    DeathPosition = deathMessage.DeathPosition
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理死亡消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理死亡消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleResurrectAsync(HorizonMessagePacket message)
        {
            try
            {
                ResurrectMessage resurrectMessage = message.Body as ResurrectMessage;
                // 处理复活逻辑
                var response = new ResurrectMessage
                {
                    ResurrectedId = resurrectMessage.ResurrectedId,
                    ResurrectPosition = resurrectMessage.ResurrectPosition,
                    ResurrectType = resurrectMessage.ResurrectType
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理复活消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理复活消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSectInfoAsync(HorizonMessagePacket message)
        {
            try
            {
                SectInfoMessage sectInfoMessage = message.Body as SectInfoMessage;
                // 处理门派信息逻辑
                var response = new SectInfoMessage
                {
                    SectId = sectInfoMessage.SectId,
                    SectName = sectInfoMessage.SectName,
                    SectLeader = sectInfoMessage.SectLeader,
                    MemberCount = sectInfoMessage.MemberCount,
                    SectLevel = sectInfoMessage.SectLevel,
                    Reputation = sectInfoMessage.Reputation,
                    Resources = sectInfoMessage.Resources
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理门派信息消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理门派信息消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleJoinSectAsync(HorizonMessagePacket message)
        {
            try
            {
                JoinSectRequest joinSectRequest = message.Body as JoinSectRequest;
                // 处理加入门派逻辑
                var response = new JoinSectResponse
                {
                    Success = true,
                    Message = "成功加入门派",
                    SectId = joinSectRequest.SectId,
                    Position = "弟子"
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理加入门派消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理加入门派消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSectSkillAsync(HorizonMessagePacket message)
        {
            try
            {
                SectSkillMessage sectSkillMessage = message.Body as SectSkillMessage;
                // 处理门派技能逻辑
                var response = new SectSkillMessage
                {
                    SectId = sectSkillMessage.SectId,
                    SkillId = sectSkillMessage.SkillId,
                    SkillName = sectSkillMessage.SkillName,
                    Description = sectSkillMessage.Description,
                    Level = sectSkillMessage.Level,
                    LearningConditions = sectSkillMessage.LearningConditions
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理门派技能消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理门派技能消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSectQuestAsync(HorizonMessagePacket message)
        {
            try
            {
                SectQuestMessage sectQuestMessage = message.Body as SectQuestMessage;
                // 处理门派任务逻辑
                var response = new SectQuestMessage
                {
                    QuestId = sectQuestMessage.QuestId,
                    QuestName = sectQuestMessage.QuestName,
                    Description = sectQuestMessage.Description,
                    Rewards = sectQuestMessage.Rewards,
                    Requirements = sectQuestMessage.Requirements,
                    SectId = sectQuestMessage.SectId
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理门派任务消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理门派任务消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleReputationAsync(HorizonMessagePacket message)
        {
            try
            {
                ReputationUpdateMessage reputationUpdateMessage = message.Body as ReputationUpdateMessage;
                // 处理声望更新逻辑
                var response = new ReputationUpdateMessage
                {
                    CharacterId = reputationUpdateMessage.CharacterId,
                    ReputationType = reputationUpdateMessage.ReputationType,
                    ChangeValue = reputationUpdateMessage.ChangeValue,
                    CurrentValue = reputationUpdateMessage.CurrentValue,
                    Reason = reputationUpdateMessage.Reason
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理声望更新消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理声望更新消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleChivalryPointAsync(HorizonMessagePacket message)
        {
            try
            {
                ChivalryPointUpdateMessage chivalryPointUpdateMessage = message.Body as ChivalryPointUpdateMessage;
                // 处理侠义值更新逻辑
                var response = new ChivalryPointUpdateMessage
                {
                    CharacterId = chivalryPointUpdateMessage.CharacterId,
                    ChangeValue = chivalryPointUpdateMessage.ChangeValue,
                    CurrentValue = chivalryPointUpdateMessage.CurrentValue,
                    Reason = chivalryPointUpdateMessage.Reason
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理侠义值更新消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理侠义值更新消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleDuelAsync(HorizonMessagePacket message)
        {
            try
            {
                DuelRequest duelRequest = message.Body as DuelRequest;
                // 处理比武切磋逻辑
                var response = new DuelResponse
                {
                    Accepted = true,
                    Message = "接受比武切磋",
                    DuelId = (ulong)new Random().Next(100000, 999999)
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理比武切磋消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理比武切磋消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSwornBrotherAsync(HorizonMessagePacket message)
        {
            try
            {
                SwornBrotherRequest swornBrotherRequest = message.Body as SwornBrotherRequest;
                // 处理结拜逻辑
                var response = new SwornBrotherResponse
                {
                    Agreed = true,
                    Message = "结拜成功",
                    BrotherhoodId = (ulong)new Random().Next(100000, 999999)
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理结拜消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理结拜消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleMasterApprenticeAsync(HorizonMessagePacket message)
        {
            try
            {
                MasterApprenticeRequest masterApprenticeRequest = message.Body as MasterApprenticeRequest;
                // 处理师徒关系逻辑
                var response = new MasterApprenticeResponse
                {
                    Agreed = true,
                    Message = "师徒关系建立成功",
                    RelationshipId = (ulong)new Random().Next(100000, 999999),
                    RelationshipLevel = 1
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理师徒关系消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理师徒关系消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleInventoryUpdateAsync(HorizonMessagePacket message)
        {
            try
            {
                InventoryUpdateMessage inventoryUpdateMessage = message.Body as InventoryUpdateMessage;
                // 处理背包更新逻辑
                var response = new InventoryUpdateMessage
                {
                    CharacterId = inventoryUpdateMessage.CharacterId,
                    ItemChanges = inventoryUpdateMessage.ItemChanges,
                    UpdateTime = DateTime.UtcNow.Ticks
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理背包更新消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理背包更新消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleWeaponSwitchAsync(HorizonMessagePacket message)
        {
            try
            {
                WeaponSwitchMessage weaponSwitchMessage = message.Body as WeaponSwitchMessage;
                // 处理武器切换逻辑
                var response = new WeaponSwitchMessage
                {
                    CharacterId = weaponSwitchMessage.CharacterId,
                    CurrentWeaponSlot = weaponSwitchMessage.CurrentWeaponSlot,
                    TargetWeaponSlot = weaponSwitchMessage.TargetWeaponSlot,
                    SwitchTime = DateTime.UtcNow.Ticks
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理武器切换消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理武器切换消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEquipmentInfoAsync(HorizonMessagePacket message)
        {
            try
            {
                EquipmentInfoMessage equipmentInfoMessage = message.Body as EquipmentInfoMessage;
                // 处理装备信息逻辑
                var response = new EquipmentInfoMessage
                {
                    EquipmentId = equipmentInfoMessage.EquipmentId,
                    TemplateId = equipmentInfoMessage.TemplateId,
                    Name = equipmentInfoMessage.Name,
                    EnhanceLevel = equipmentInfoMessage.EnhanceLevel,
                    RefineLevel = equipmentInfoMessage.RefineLevel,
                    BaseAttributes = equipmentInfoMessage.BaseAttributes,
                    EnhanceAttributes = equipmentInfoMessage.EnhanceAttributes,
                    RefineAttributes = equipmentInfoMessage.RefineAttributes
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理装备信息消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理装备信息消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEquipmentEnhanceAsync(HorizonMessagePacket message)
        {
            try
            {
                EquipmentEnhanceRequest equipmentEnhanceRequest = message.Body as EquipmentEnhanceRequest;
                // 处理装备强化逻辑
                var response = new EquipmentEnhanceResponse
                {
                    Success = true,
                    Message = "装备强化成功",
                    NewEnhanceLevel = 1,
                    ConsumedMaterials = equipmentEnhanceRequest.MaterialIds,
                    ConsumedGold = 1000
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理装备强化消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理装备强化消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEquipmentRefineAsync(HorizonMessagePacket message)
        {
            try
            {
                EquipmentRefineRequest equipmentRefineRequest = message.Body as EquipmentRefineRequest;
                // 处理装备精炼逻辑
                var response = new EquipmentRefineResponse
                {
                    Success = true,
                    Message = "装备精炼成功",
                    NewRefineLevel = 1,
                    ConsumedMaterials = equipmentRefineRequest.MaterialIds,
                    ConsumedRefineStone = equipmentRefineRequest.RefineStoneId,
                    ConsumedGold = 1000
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理装备精炼消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理装备精炼消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCraftingAsync(HorizonMessagePacket message)
        {
            try
            {
                CraftingRequest craftingRequest = message.Body as CraftingRequest;
                // 处理合成逻辑
                var response = new CraftingResponse
                {
                    Success = true,
                    Message = "合成成功",
                    CraftedItems = new List<ItemInfo>(),
                    ConsumedMaterials = craftingRequest.MaterialIds,
                    ConsumedGold = 1000
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理合成消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理合成消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCraftingResultAsync(HorizonMessagePacket message)
        {
            try
            {
                CraftingResponse craftingResponse = message.Body as CraftingResponse;
                // 处理合成结果逻辑
                var response = new CraftingResponse
                {
                    Success = craftingResponse.Success,
                    Message = craftingResponse.Message,
                    CraftedItems = craftingResponse.CraftedItems,
                    ConsumedMaterials = craftingResponse.ConsumedMaterials,
                    ConsumedGold = craftingResponse.ConsumedGold
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理合成结果消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理合成结果消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleAttributeInheritanceAsync(HorizonMessagePacket message)
        {
            try
            {
                AttributeInheritanceRequest attributeInheritanceRequest = message.Body as AttributeInheritanceRequest;
                // 处理属性继承逻辑
                var response = new AttributeInheritanceResponse
                {
                    Success = true,
                    Message = "属性继承成功",
                    InheritedAttributes = new Dictionary<string, object>(),
                    ConsumedGold = 1000,
                    ConsumedMaterials = attributeInheritanceRequest.MaterialIds
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理属性继承消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理属性继承消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleWuXingCraftingAsync(HorizonMessagePacket message)
        {
            try
            {
                WuXingCraftingRequest wuXingCraftingRequest = message.Body as WuXingCraftingRequest;
                // 处理五行合成逻辑
                var response = new WuXingCraftingResponse
                {
                    Success = true,
                    Message = "五行合成成功",
                    CraftedItem = new ItemInfo(),
                    ConsumedMaterials = wuXingCraftingRequest.WuXingMaterials,
                    ConsumedGold = 1000
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理五行合成消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理五行合成消息失败" }));
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

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleFriendAsync(HorizonMessagePacket message)
        {
            try
            {
                AddFriendRequest addFriendRequest = message.Body as AddFriendRequest;
                // 处理好友逻辑
                var response = new AddFriendResponse
                {
                    Success = true,
                    Message = "添加好友成功",
                    FriendInfo = new FriendInfo()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理好友消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理好友消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleTeamAsync(HorizonMessagePacket message)
        {
            try
            {
                CreateTeamRequest createTeamRequest = message.Body as CreateTeamRequest;
                // 处理组队逻辑
                var response = new CreateTeamResponse
                {
                    Success = true,
                    Message = "创建队伍成功",
                    TeamInfo = new TeamInfo()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理组队消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理组队消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleGuildAsync(HorizonMessagePacket message)
        {
            try
            {
                CreateGuildRequest createGuildRequest = message.Body as CreateGuildRequest;
                // 处理帮派逻辑
                var response = new CreateGuildResponse
                {
                    Success = true,
                    Message = "创建帮派成功",
                    GuildInfo = new GuildInfo()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理帮派消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理帮派消息失败" }));
            }
        }

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
                AcceptQuestRequest acceptQuestRequest = message.Body as AcceptQuestRequest;
                // 处理接受任务逻辑
                var response = new AcceptQuestResponse
                {
                    Success = true,
                    Message = "接受任务成功",
                    AcceptedQuest = new QuestInfo()
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
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
                CompleteQuestRequest completeQuestRequest = message.Body as CompleteQuestRequest;
                // 处理完成任务逻辑
                var response = new CompleteQuestResponse
                {
                    Success = true,
                    Message = "完成任务成功",
                    Rewards = new Dictionary<string, int>(),
                    CompletedQuestId = completeQuestRequest.QuestId
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理完成任务消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理完成任务消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleHeartbeatAsync(HorizonMessagePacket message)
        {
            try
            {
                HeartbeatMessage heartbeatMessage = message.Body as HeartbeatMessage;
                // 处理心跳逻辑
                var response = new HeartbeatResponse
                {
                    Timestamp = DateTime.UtcNow.Ticks,
                    ServerTime = DateTime.UtcNow.Ticks,
                    Latency = DateTime.UtcNow.Ticks - heartbeatMessage.ClientTime
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理心跳消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理心跳消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSystemAsync(HorizonMessagePacket message)
        {
            try
            {
                SystemNotificationMessage systemNotificationMessage = message.Body as SystemNotificationMessage;
                // 处理系统消息逻辑
                var response = new SystemNotificationMessage
                {
                    MessageId = systemNotificationMessage.MessageId,
                    SystemMessageType = systemNotificationMessage.SystemMessageType,
                    Title = systemNotificationMessage.Title,
                    Content = systemNotificationMessage.Content,
                    Priority = systemNotificationMessage.Priority,
                    SendTime = DateTime.UtcNow.Ticks,
                    ExpireTime = systemNotificationMessage.ExpireTime,
                    TargetUsers = systemNotificationMessage.TargetUsers,
                    MinLevel = systemNotificationMessage.MinLevel,
                    MaxLevel = systemNotificationMessage.MaxLevel
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理系统消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理系统消息失败" }));
            }
        }

        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleErrorAsync(HorizonMessagePacket message)
        {
            try
            {
                ErrorMessage errorMessage = message.Body as ErrorMessage;
                // 处理错误消息逻辑
                var response = new ErrorMessage
                {
                    ErrorCode = errorMessage.ErrorCode,
                    Message = errorMessage.Message,
                    Details = errorMessage.Details,
                    Timestamp = DateTime.UtcNow.Ticks,
                    RelatedMessageId = errorMessage.RelatedMessageId,
                    ShouldRetry = errorMessage.ShouldRetry,
                    RetryCount = errorMessage.RetryCount
                };
                var tem = CreateHorizonMessage(response);
                return (true, tem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理错误消息失败");
                return (false, CreateHorizonMessage(new ErrorMessage { ErrorCode = 500, Message = "处理错误消息失败" }));
            }
        }
    }
}