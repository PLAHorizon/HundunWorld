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
        /// <summary>出站推送：外观落库后把权威副本推给同 Gateway 上的其他在线角色（可空，缺失时只跳过推送）。</summary>
        private readonly IPlayerPushService _pushService;
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
            UserAuthTokenProvider authTokenProvider = null,
            IPlayerPushService pushService = null) : base(logger, clusterClient, adapter)
        {
            _loggerFactory = loggerFactory;
            _validator = validator ?? new AuthenticationValidator(
               _loggerFactory?.CreateLogger<AuthenticationValidator>() ??
               logger as ILogger<AuthenticationValidator> ??
               new Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthenticationValidator>());
            _fingerprintService = characterFingerprintService;
            _authTokenProvider = authTokenProvider;
            _pushService = pushService;
        }


        /// <summary>
        /// 获取此处理器支持的消息类型列表
        /// 包括角色管理、游戏核心玩法、社交、物品、技能、交互和系统消息等
        /// </summary>
        public override List<MessageType> MessageTypes { get; } = new List<MessageType> {
            // 任务域消息（接取/完成/更新/门派任务）不在这里认领：本处理器那几支是恒回
            // Success=true 的空桩，真现在 QuestHandler；两处同时声明会让"完成任务"变成一句谎话，
            // 而 §14.2 的初级技能书正押在这条发放链上。
            // 技能域消息（习得/升级/冷却/熟练/连击/防御/内功/轻功）不在这里认领：
            // 本处理器那几支是恒回 Success=true 的空桩，真实现在 SkillHandler，
            // 两处同时声明会让"习得成功"变成一句谎话（与 UseItem 当年的重复声明同一个病）。
             MessageType.CharacterList,
            MessageType.CreateCharacter,
            MessageType.SelectCharacter,
            MessageType.CharacterDelete,
            MessageType.CharacterNameCheck,
            MessageType.Appearance,
            // 数字人外观提交：服务端只存 blob + 三道闸（已发布拒改、模板一致、校验和自算），
            // 不解释参数含义，所以加滑杆不需要动这里。
            MessageType.UpdateCharacterAppearance,
            // 远端玩家外观：进场后按角色 Id 拉一次已落库的 blob（玩家实体 Id 就是角色 Id）。
            MessageType.GetCharacterAppearance,

            MessageType.CreateCharacter,
            MessageType.SelectCharacter,
            MessageType.CharacterDelete,
            MessageType.EnterGame,
            MessageType.Movement,
            MessageType.PlayerAnimation,
            MessageType.PlayerSpawn,
            MessageType.AttributeUpdate,
            MessageType.AttributeAllocate,
            // 权威战斗画像下行（五行三行 + 通用攻防基值）。MessageType 与消息类早已定义，
            // 但全仓既没有服务端实现也没有客户端订阅者，本处理器是它的第一个读者，
            // 因此不存在与其他处理器争抢同一消息类型的问题。
            MessageType.CharacterAttributeRefresh,
            // Attack / SkillCast 不在这里：本处理器曾把它们原样回显（客户端报多少伤害就回多少，
            // 既不改 HP 也不校验），与 SkillHandler 的 CombatGrain 权威路径争抢同一消息类型，
            // 命中哪个取决于 DI 注册顺序。权威结算唯一出口是 SkillHandler。
            MessageType.Damage,
            MessageType.Death,
            MessageType.Resurrect,
            MessageType.SectInfo,
            MessageType.JoinSect,
            MessageType.SectSkill,
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
            MessageType.Chat,
            MessageType.Friend,
            MessageType.Team,
            MessageType.Guild,
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
                case MessageType.AttributeAllocate:
                    return await HandleAttributeAllocateAsync(message);
                case MessageType.UpdateCharacterAppearance:
                    return await HandleUpdateAppearanceAsync(message);
                case MessageType.GetCharacterAppearance:
                    return await HandleGetAppearanceAsync(message);
                case MessageType.CharacterAttributeRefresh:
                    return await HandleCombatProfileRefreshAsync(message);
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
                // 共享令牌不绑定机器ID（MachineId 为空），角色令牌兜底取消息头 MachineId，
                // 保持"开启 ValidateTokenMachineId 的部署"下角色令牌仍可校验
                var machineId = !string.IsNullOrWhiteSpace(currentToken?.MachineId)
                    ? currentToken.MachineId
                    : message.Header.MachineId ?? "";
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
                // 协议级幂等键（请求头携带，客户端在途断线重试时保持不变）：
                // 服务端角色管理器按键去重，重试不会重复创建角色。
                createCharacter.IdempotencyKey = message.Header.IdempotencyKey ?? string.Empty;

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
        /// 属性加点：转发到权威 <see cref="ICharacterGrain.AllocateAttributeAsync"/>（校验 + Redis 缓存 + 落库）。
        /// 响应始终回服务端当前权威分配与点池，客户端据此收敛显示，失败即回滚预览。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleAttributeAllocateAsync(HorizonMessagePacket message)
        {
            var request = message.Body as AttributeAllocateRequest;
            var response = new AttributeAllocateResponse
            {
                CharacterId = request?.CharacterId ?? 0UL,
                Allocation = new AttributeAllocationData(),
            };

            try
            {
                if (request is null || request.CharacterId == 0UL || request.Allocation is null)
                {
                    response.Message = "请求参数无效";
                    return (true, CreateHorizonMessage(response));
                }

                var grain = _clusterClient.GetGrain<ICharacterGrain>((long)request.CharacterId);
                var result = await grain.AllocateAttributeAsync(request.CharacterId, request.Allocation);
                return (true, CreateHorizonMessage(result));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理属性加点消息失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Message = "加点失败，请稍后重试";
                return (true, CreateHorizonMessage(response));
            }
        }

        /// <summary>
        /// 数字人外观落库。拒绝必须带原因并回给客户端：静默吞掉就等于对玩家说「已保存」这句谎话。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleUpdateAppearanceAsync(HorizonMessagePacket message)
        {
            var request = message.Body as UpdateCharacterAppearanceRequest;
            var response = new UpdateCharacterAppearanceResponse();

            try
            {
                if (request is null || request.CharacterId == 0UL || request.Look is null)
                {
                    response.Error = "请求参数无效";
                    return (true, CreateHorizonMessage(response));
                }

                var grain = _clusterClient.GetGrain<ICharacterGrain>((long)request.CharacterId);
                var result = await grain.UpdateAppearanceAsync(request.Look);
                if (result != null && result.Success)
                {
                    await PushAppearanceToPeersAsync(grain, request.CharacterId, result.StoredRevision);
                }
                return (true, CreateHorizonMessage(result));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理数字人外观提交失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Error = "外观保存失败，请稍后重试";
                return (true, CreateHorizonMessage(response));
            }
        }

        /// <summary>
        /// 外观落库后把权威副本推给同 Gateway 上的其他在线角色：远端玩家不必重新进场就能看到对方的新外观。
        /// <para>
        /// 推送内容**回读 grain**，不假设"客户端传的那份就是存下的"；客户端按 Revision 丢弃旧副本。
        /// 本人不推：他的界面已经是刚提交的这一份，回推只会覆盖会话内未保存的编辑。
        /// 推送失败只记日志，不能让保存回执变成失败——外观已经落库，下次进场仍能拉到。
        /// </para>
        /// </summary>
        private async Task PushAppearanceToPeersAsync(ICharacterGrain grain, ulong ownerCharacterId, int storedRevision)
        {
            if (_pushService == null)
            {
                return;
            }

            try
            {
                var online = await _pushService.GetOnlineCharacterIdsAsync();
                var targets = online.Where(id => id != ownerCharacterId).ToList();
                if (targets.Count == 0)
                {
                    return;
                }

                var stored = await grain.GetAppearanceAsync();
                if (stored?.Blob == null || stored.Blob.Length == 0)
                {
                    return;
                }

                int hairColor = 0;
                try
                {
                    var info = await grain.GetCharacterInfoAsync(null);
                    hairColor = info?.Appearance?.HairColor ?? 0;
                }
                catch
                {
                    // 发色只是表现附加项：读不到就回落 0，与读取回执同一口径。
                    hairColor = 0;
                }

                await _pushService.PushAsync(new GetCharacterAppearanceResponse
                {
                    CharacterId = ownerCharacterId,
                    Success = true,
                    Look = stored,
                    HairColor = hairColor,
                }, targets);

                Logger.LogInformation("[外观推送] 角色 {CharacterId} rev={Revision} 已推给 {Count} 名在线玩家",
                    ownerCharacterId, storedRevision, targets.Count);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[外观推送] 角色 {CharacterId} 推送失败", ownerCharacterId);
            }
        }

        /// <summary>
        /// 读取角色外观（远端玩家进场用）。成功但 Look 为空表示该角色从未自定义过，
        /// 客户端应回默认脸；失败必须带原因，别把「读不到」当成「没有」。
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleGetAppearanceAsync(HorizonMessagePacket message)
        {
            var request = message.Body as GetCharacterAppearanceRequest;
            var response = new GetCharacterAppearanceResponse
            {
                CharacterId = request?.CharacterId ?? 0UL,
            };

            try
            {
                if (request is null || request.CharacterId == 0UL)
                {
                    response.Error = "角色ID无效";
                    return (true, CreateHorizonMessage(response));
                }

                var grain = _clusterClient.GetGrain<ICharacterGrain>((long)request.CharacterId);
                response.Look = await grain.GetAppearanceAsync();
                try
                {
                    var info = await grain.GetCharacterInfoAsync(null);
                    response.HairColor = info?.Appearance?.HairColor ?? 0;
                }
                catch
                {
                    // 发色只是表现附加项：读不到就回落 0，不把整次外观读取判失败。
                    response.HairColor = 0;
                }
                response.Success = true;
                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "读取角色外观失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Error = "读取外观失败，请稍后重试";
                return (true, CreateHorizonMessage(response));
            }
        }

        /// <summary>
        /// 下行权威战斗画像（五行三行 + 通用攻防基值 + 五行属性值）。
        /// <para>
        /// 失败必须如实回 <c>Success=false</c>：客户端据此保留本地镜像，
        /// 而不是把"grain 不可用"当成"服务端确认你五行全 0"，那会让面板瞬间归零。
        /// </para>
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCombatProfileRefreshAsync(HorizonMessagePacket message)
        {
            var request = message.Body as CharacterAttributeRefreshRequest;
            var response = new CharacterAttributeRefreshResponse
            {
                CharacterId = request?.CharacterId ?? 0UL,
                Message = "",
            };

            try
            {
                if (request is null || request.CharacterId == 0UL)
                {
                    response.Message = "角色ID无效";
                    return (true, CreateHorizonMessage(response));
                }

                var grain = _clusterClient.GetGrain<ICharacterGrain>((long)request.CharacterId);
                return (true, CreateHorizonMessage(await grain.GetCombatProfileSnapshotAsync()));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理战斗画像同步失败。CharacterId={CharacterId}", request?.CharacterId ?? 0);
                response.Message = "获取战斗画像失败";
                return (true, CreateHorizonMessage(response));
            }
        }

        /// <summary>
        /// 异步处理移动消息（阶段 3.3 后为拒绝路径）。<br/>
        /// 移动权威已迁移至空间权威（ZoneShardGrain + SimWorld），
        /// 旧 <see cref="MessageType.Movement"/> 写路径（CharacterGrain.MoveAsync）已下线，
        /// 消除 HP/位置双权威。保留本分支仅返回拒绝响应，避免旧客户端静默无响应；
        /// 真实移动请走同步协议（SyncPacketKind.Input，经 SyncInputHandler → ZoneShardGrain）。
        /// </summary>
        /// <param name="message">移动请求消息包</param>
        /// <returns>处理结果和响应消息包</returns>
        private Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleMovementAsync(HorizonMessagePacket message)
        {
            if (message.Body is MoveRequest moveRequest)
            {
                Logger.LogWarning(
                    "拒绝旧版移动请求（MessageType.Movement）：移动权威已迁移至空间权威，请改用同步协议。CharacterId={CharacterId}",
                    moveRequest.CharacterId);
            }

            var tem = CreateHorizonMessage(new MoveResponse { Success = false });
            return Task.FromResult<(bool, HorizonMessagePacket)>((true, tem));
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

                // 这条 UseItem 与 ItemHandler 里的同一条**重复注册**（MessageType.UseItem 被两个 handler 都声明），
                // 谁生效取决于注册顺序。原来这里是硬编码 Success=true 的空桩，一旦被选中，
                // 玩家用任何物品（包括秘籍）都会收到"成功"而服务端数量纹丝不动。
                // 现在两条路径共用 InventoryGrain 这一个咽喉，注册顺序不再影响行为。
                var response = new UseItemResponse();
                if (useItemRequest is null || useItemRequest.CharacterId == 0UL || useItemRequest.ItemId <= 0)
                {
                    response.Message = "请求参数无效";
                    return (true, CreateHorizonMessage(response));
                }

                var inventory = _clusterClient.GetGrain<IInventoryGrain>(
                    Horizon.Game.Core.Sim.Server.GrainKeyMapper.ToInventoryKey(useItemRequest.CharacterId));
                var result = await inventory.UseItemWithResultAsync(
                    useItemRequest.CharacterId, useItemRequest.ItemId,
                    useItemRequest.Count > 0 ? useItemRequest.Count : 1);

                response.Success = result.Success;
                response.Message = result.Message;
                response.RemainingCount = result.RemainingCount;
                response.Effects = result.Effects;
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
                // 落到权威背包；失败时如实回传，不再无条件报"装备成功"。
                var (ok, text) = await ApplyEquipCommandAsync(
                    equipItemMessage.CharacterId, equipItemMessage.ItemId, equipItemMessage.TemplateId,
                    equipItemMessage.Slot, equipItemMessage.Unequip);

                var response = new EquipItemMessage
                {
                    CharacterId = equipItemMessage.CharacterId,
                    ItemId = equipItemMessage.ItemId,
                    Slot = equipItemMessage.Slot,
                    Unequip = equipItemMessage.Unequip,
                    TemplateId = equipItemMessage.TemplateId,
                    Success = ok,
                    Message = text
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
                if (!(message.Body is ResurrectMessage resurrectMessage) || resurrectMessage.ResurrectedId == 0)
                    return (true, CreateHorizonMessage(new ErrorMessage { ErrorCode = 400, Message = "复活请求无效" }));

                // 这一手以前是空桩：把请求体抄成响应直接回"成功"，从不触碰任何 grain。
                // 客户端因此以为复活完了，服务端的 CurrentHealth 还是 0 —— 之后每次施放都被
                // SkillGrain 的"施法者已死亡"挡下，玩家看到的是"复活成功后动也动不了"。
                var characterGrain = _clusterClient.GetGrain<ICharacterGrain>((long)resurrectMessage.ResurrectedId);
                var result = await characterGrain.ResurrectAsync(new ResurrectMessage
                {
                    ResurrectedId = resurrectMessage.ResurrectedId,
                    ResurrectPosition = resurrectMessage.ResurrectPosition,
                    ResurrectType = resurrectMessage.ResurrectType,
                });

                // 复活成功的判据是"真的有了血"，不是 grain 调用没抛异常：
                // 生命上限为 0（属性未初始化/装备聚合缺失）时按上限比例恢复还是 0，
                // 那种情况下必须把原因回给客户端，否则 UI 只会显示一个骗人的"已复活"。
                if (result.MaxHealth <= 0f || result.RemainingHealth <= 0f)
                {
                    Logger.LogWarning("复活后血量仍为 0: CharacterId={CharacterId} MaxHealth={MaxHealth} Health={Health}",
                        resurrectMessage.ResurrectedId, result.MaxHealth, result.RemainingHealth);
                    return (true, CreateHorizonMessage(new ErrorMessage
                    {
                        ErrorCode = 500,
                        Message = "复活失败：生命上限为 0，请先同步角色属性"
                    }));
                }

                return (true, CreateHorizonMessage(result));
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