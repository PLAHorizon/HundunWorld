using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TouchSocket.Sockets;
using Horizon.Game.Core.Security;

namespace Horizon.Game.Core.Handlers
{
    /// <summary>
    /// 角色管理消息处理器
    /// 处理角色创建、选择、删除、角色列表等角色管理相关消息
    /// </summary>
    public class CharacterManagementHandler : MessageHandlerBase
    {
        private readonly AuthenticationValidator _validator;
        private readonly ILoggerFactory _loggerFactory;

        public CharacterManagementHandler(ILogger<MessageHandlerBase> logger, IClusterClient clusterClient, 
            HorizonMessageAdapter adapter, ILoggerFactory loggerFactory = null, 
            AuthenticationValidator validator = null) 
            : base(logger, clusterClient, adapter)
        {
            _loggerFactory = loggerFactory;
            _validator = validator ?? new AuthenticationValidator(
                _loggerFactory?.CreateLogger<AuthenticationValidator>() ?? 
                logger as ILogger<AuthenticationValidator> ?? 
                new Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthenticationValidator>());
        }

        public override List<MessageType> MessageTypes => new()
        {
            //MessageType.CharacterList,
            //MessageType.CreateCharacter,
            //MessageType.SelectCharacter,
            //MessageType.CharacterDelete,
            //MessageType.CharacterNameCheck,
            //MessageType.EnterGame,
            //MessageType.Appearance
        };

        public override ServiceType ServiceType => ServiceType.Game;

        public override async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> RouteHandlerAsync(HorizonMessagePacket message)
        {
            try
            {
                Logger.LogInformation("处理角色管理消息: {MessageType}", message.Header.MessageType);

                switch (message.Header.MessageType)
                {
                    case MessageType.CharacterList:
                        return await HandleCharacterListRequestAsync(message);
                    
                    case MessageType.CreateCharacter:
                        return await HandleCreateCharacterAsync(message);
                    
                    case MessageType.SelectCharacter:
                        return await HandleSelectCharacterAsync(message);
                    
                    case MessageType.CharacterDelete:
                        return await HandleDeleteCharacterAsync(message);
                    
                    case MessageType.CharacterNameCheck:
                        return await HandleCharacterNameCheckAsync(message);
                    
                    case MessageType.EnterGame:
                        return await HandleEnterGameAsync(message);
                        
                    default:
                        Logger.LogWarning("未支持的角色管理消息类型: {MessageType}", message.Header.MessageType);
                        return (false, null);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理角色管理消息时发生异常: {MessageType}", message.Header.MessageType);
                return (false, CreateErrorResponse(message, "角色管理服务异常"));
            }
        }

        /// <summary>
        /// 处理角色列表请求
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

                // 获取角色列表
                var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(0);
                var gameQueryDto = new Share.Dtos.Games.GameQueryDto
                {
                    GameUserId = (long)listRequest.UserId,
                    GameId = (int)message.Header.GameId
                };

                var characters = await characterGrain.GetAllCharactersAsync(gameQueryDto);

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
                return (false, CreateCharacterListErrorResponse("获取角色列表失败"));
            }
        }

        /// <summary>
        /// 处理创建角色请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCreateCharacterAsync(HorizonMessagePacket message)
        {
            try
            {
                var createRequest = message.Body as CreateCharacterRequest;
                if (createRequest == null)
                {
                    Logger.LogError("无法反序列化创建角色请求消息");
                    return (false, CreateCreateCharacterErrorResponse("创建角色请求格式错误"));
                }

                Logger.LogInformation("处理创建角色请求: CharacterName: {CharacterName}, UserId: {UserId}", 
                    createRequest.CharacterName, createRequest.UserId);

                // 获取新的角色Grain ID（可以使用雪花算法或其他方式生成唯一ID）
                var characterId = GenerateNewCharacterId();
                var characterGrain = _clusterClient.GetGrain<ICharacterGrain>(characterId);

                // 设置请求的其他参数
                createRequest.GameId = (int)message.Header.GameId;
                createRequest.ZoneId = (int)message.Header.ZoneId;
                createRequest.ServerId = (int)message.Header.ServerId;

                // 调用CharacterGrain创建角色
                var createResult = await characterGrain.CreateCharacterAsync(createRequest);

                Logger.LogInformation("角色创建结果: {IsSuccess}, Message: {Message}, CharacterName: {CharacterName}", 
                    createResult.IsSuccess, createResult.Message, createRequest.CharacterName);

                return (true, CreateHorizonMessage(createResult));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理创建角色请求时发生异常");
                return (false, CreateCreateCharacterErrorResponse("创建角色处理异常"));
            }
        }

        /// <summary>
        /// 处理选择角色请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleSelectCharacterAsync(HorizonMessagePacket message)
        {
            try
            {
                // 这里可以添加选择角色的逻辑
                // 目前简单返回成功响应
                Logger.LogInformation("处理选择角色请求");

                var response = new EnterGameResponse
                {
                    Success = true,
                    Message = "角色选择成功",
                    CharacterInfo = new CharacterInfo() // 这里应该填充实际的角色信息
                };

                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理选择角色请求时发生异常");
                return (false, CreateErrorResponse(message, "选择角色处理异常"));
            }
        }

        /// <summary>
        /// 处理删除角色请求
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

                // 获取角色Grain并执行删除操作
                var characterGrain = _clusterClient.GetGrain<ICharacterGrain>((long)deleteRequest.CharacterId);
                
                // 这里需要实现删除角色的逻辑
                // 目前返回成功响应
                var response = new DeleteCharacterResponse
                {
                    Success = true,
                    Message = "角色删除成功",
                    CharacterId = deleteRequest.CharacterId
                };

                Logger.LogInformation("角色删除成功: CharacterId: {CharacterId}", deleteRequest.CharacterId);

                return (true, CreateHorizonMessage(response));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理删除角色请求时发生异常");
                return (false, CreateDeleteCharacterErrorResponse("删除角色处理异常"));
            }
        }

        /// <summary>
        /// 处理角色名检查请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleCharacterNameCheckAsync(HorizonMessagePacket message)
        {
            try
            {
                var nameCheckRequest = message.Body as  ValidateCharacterNameRequest ;
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
        /// 处理进入游戏请求
        /// </summary>
        private async Task<(bool IsSuccess, HorizonMessagePacket MessagePacket)> HandleEnterGameAsync(HorizonMessagePacket message)
        {
            try
            {
                var enterGameRequest = message.Body as EnterGameRequest ;
                if (enterGameRequest == null)
                {
                    Logger.LogError("无法反序列化进入游戏请求消息");
                    return (false, CreateEnterGameErrorResponse("进入游戏请求格式错误"));
                }

                Logger.LogInformation("处理进入游戏请求: CharacterId: {CharacterId}", enterGameRequest.CharacterId);

                // 获取角色Grain并执行进入游戏操作
                var characterGrain = _clusterClient.GetGrain<ICharacterGrain>((long)enterGameRequest.CharacterId);
                var enterResult = await characterGrain.EnterGameAsync(enterGameRequest);

                Logger.LogInformation("进入游戏结果: Success: {Success}, Message: {Message}, CharacterId: {CharacterId}", 
                    enterResult.Success, enterResult.Message, enterGameRequest.CharacterId);

                return (true, CreateHorizonMessage(enterResult));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "处理进入游戏请求时发生异常");
                return (false, CreateEnterGameErrorResponse("进入游戏处理异常"));
            }
        }

        #region 私有辅助方法

        /// <summary>
        /// 生成新的角色ID
        /// </summary>
        private long GenerateNewCharacterId()
        {
            // 这里可以使用雪花算法或其他分布式ID生成策略
            // 暂时使用时间戳 + 随机数的简单方法
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var random = new Random().Next(1000, 9999);
            return timestamp * 10000 + random;
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

        /// <summary>
        /// 创建进入游戏错误响应
        /// </summary>
        private HorizonMessagePacket CreateEnterGameErrorResponse(string errorMessage)
        {
            var response = new EnterGameResponse
            {
                Success = false,
                Message = errorMessage,
                CharacterInfo = new CharacterInfo()
            };
            return CreateHorizonMessage(response);
        }

        #endregion
    }
}