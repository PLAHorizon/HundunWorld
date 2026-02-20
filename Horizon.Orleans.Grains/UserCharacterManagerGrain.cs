using AutoMapper;
using Horizon.Core.Abstract;
using Horizon.Core.Helper;
using Horizon.Entities;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 用户角色管理器Grain
    /// 负责管理单个用户的所有角色，支持多角色创建
    /// </summary>
    public class UserCharacterManagerGrain : Grain, IUserCharacterManagerGrain
    {
        private readonly ILogger<UserCharacterManagerGrain> _logger;
        private readonly IDataContext<GameEntityContext, UserEntity, long> _gameUserContext;
        private readonly IDataContext<GameEntityContext, CharacterEntity, long> _gameCharacterContext;
        private readonly IMapper _mapper;

        /// <summary>
        /// 最大角色数量限制
        /// </summary>
        private const int MAX_CHARACTER_COUNT = 5;

        /// <summary>
        /// 构造函数
        /// </summary>
        public UserCharacterManagerGrain(
            ILogger<UserCharacterManagerGrain> logger,
            IDataContext<GameEntityContext, UserEntity, long> gameUserContext,
            IDataContext<GameEntityContext, CharacterEntity, long> gameCharacterContext,
            IMapper mapper)
        {
            _logger = logger;
            _gameUserContext = gameUserContext;
            _gameCharacterContext = gameCharacterContext;
            _mapper = mapper;
        }

        /// <summary>
        /// 获取当前用户ID
        /// </summary>
        private long UserId => this.GetPrimaryKeyLong();

        /// <summary>
        /// 为用户创建新角色
        /// </summary>
        public async Task<CreateCharacterResponse> CreateCharacterAsync(CreateCharacterRequest request)
        {
            try
            {
                _logger.LogInformation("开始创建角色: 角色名={CharacterName}, 用户ID={UserId}, 游戏ID={GameId}",
                    request.CharacterName, UserId, request.GameId);

                // 1. 验证用户状态
                var gameUser = await _gameUserContext.QueryFirstOrDefaultAsync(
                    u => u.Id == UserId && !u.IsDeleted);

                if (gameUser == null)
                {
                    _logger.LogWarning("用户不存在或已被禁用: UserId={UserId}", UserId);
                    return new CreateCharacterResponse
                    {
                        IsSuccess = false,
                        Message = "用户不存在或已被禁用。"
                    };
                }

                if (gameUser.Status > 0)
                {
                    _logger.LogWarning("用户账号被封禁: UserId={UserId}, Status={Status}",
                        UserId, gameUser.Status);
                    return new CreateCharacterResponse
                    {
                        IsSuccess = false,
                        Message = "账号被封禁，无法创建角色，请联系游戏管理员。"
                    };
                }

                // 2. 验证角色数量限制
                var existingCount = await GetCharacterCountAsync(request.GameId);
                if (existingCount >= MAX_CHARACTER_COUNT)
                {
                    _logger.LogWarning("用户角色数量已达上限: UserId={UserId}, 当前数量={Count}",
                        UserId, existingCount);
                    return new CreateCharacterResponse
                    {
                        IsSuccess = false,
                        Message = $"角色数量已达上限（{MAX_CHARACTER_COUNT}个），请删除不需要的角色后再创建。"
                    };
                }

                // 3. 验证角色名
                var cleanedName = await ValidateAndCleanCharacterName(request.CharacterName);
                if (string.IsNullOrEmpty(cleanedName))
                {
                    return new CreateCharacterResponse
                    {
                        IsSuccess = false,
                        Message = "角色名包含非法字符或敏感词汇，请重新输入。"
                    };
                }

                // 4. 检查角色名是否已存在
                var nameExists = await _gameCharacterContext.QueryFirstOrDefaultAsync(
                    c => c.CharacterName == cleanedName && c.GameId == request.GameId && !c.IsDeleted);

                if (nameExists != null)
                {
                    _logger.LogWarning("角色名已存在: {CharacterName}", cleanedName);
                    return new CreateCharacterResponse
                    {
                        IsSuccess = false,
                        Message = "角色名已存在，请重新创建角色。"
                    };
                }

                // 5. 创建角色实体
                var characterEntity = _mapper.Map<CharacterEntity>(request);
                characterEntity.CharacterName = cleanedName;
                characterEntity.Level = 1;
                characterEntity.Experience = 0;
                characterEntity.CreateTime = DateTime.UtcNow;
                characterEntity.ServerId = request.ServerId;
                characterEntity.AreaId = request.ZoneId;
                characterEntity.GameId = request.GameId;
                characterEntity.UserId = UserId;
                characterEntity.GameUserId = gameUser.Id;

                // 设置默认出生位置
                characterEntity = SetDefaultStartingLocation(characterEntity, request.Profession);

                // 6. 保存到数据库
                characterEntity = await _gameCharacterContext.AddAsync(characterEntity);

                _logger.LogInformation("角色创建成功: 角色名={CharacterName}, 角色ID={CharacterId}, 用户ID={UserId}",
                    cleanedName, characterEntity.Id, UserId);

                // 7. 返回成功响应
                return new CreateCharacterResponse
                {
                    IsSuccess = true,
                    Message = "创建角色成功",
                    Character = _mapper.Map<CharacterInfo>(characterEntity)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建角色时发生异常: 角色名={CharacterName}, 用户ID={UserId}",
                    request.CharacterName, UserId);
                return new CreateCharacterResponse
                {
                    IsSuccess = false,
                    Message = "创建角色失败，请稍后重试。"
                };
            }
        }

        /// <summary>
        /// 获取用户所有角色列表
        /// </summary>
        public async Task<List<CharacterInfo>> GetAllCharactersAsync(int gameId)
        {
            try
            {
                _logger.LogInformation("获取用户角色列表: UserId={UserId}, GameId={GameId}",
                    UserId, gameId);

                var charactersQuery = await _gameCharacterContext.QueryAsync(
                    c => c.UserId == UserId &&
                         c.GameId == gameId &&
                         !c.IsDeleted);

                var characters = charactersQuery
                    .OrderByDescending(c => c.LastLoginTime)
                    .ThenByDescending(c => c.CreateTime)
                    .ToList();

                var characterInfos = _mapper.Map<List<CharacterInfo>>(characters);

                _logger.LogInformation("成功获取到 {Count} 个角色", characterInfos.Count);
                return characterInfos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色列表时发生异常: UserId={UserId}, GameId={GameId}",
                    UserId, gameId);
                return new List<CharacterInfo>();
            }
        }

        /// <summary>
        /// 删除角色
        /// </summary>
        public async Task<DeleteCharacterResponse> DeleteCharacterAsync(ulong characterId, int gameId)
        {
            try
            {
                _logger.LogInformation("删除角色: CharacterId={CharacterId}, UserId={UserId}",
                    characterId, UserId);

                var character = await _gameCharacterContext.QueryFirstOrDefaultAsync(
                    c => c.Id == (long)characterId &&
                         c.UserId == UserId &&
                         c.GameId == gameId &&
                         !c.IsDeleted);

                if (character == null)
                {
                    _logger.LogWarning("角色不存在或无权删除: CharacterId={CharacterId}", characterId);
                    return new DeleteCharacterResponse
                    {
                        Success = false,
                        Message = "角色不存在或无权删除。",
                        CharacterId = characterId
                    };
                }

                // 软删除
                character.IsDeleted = true;
                character.DeleteTime = DateTime.UtcNow;
                await _gameCharacterContext.UpdateAsync(character, character.Id);

                _logger.LogInformation("角色删除成功: CharacterId={CharacterId}", characterId);

                return new DeleteCharacterResponse
                {
                    Success = true,
                    Message = "角色删除成功。",
                    CharacterId = characterId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除角色时发生异常: CharacterId={CharacterId}", characterId);
                return new DeleteCharacterResponse
                {
                    Success = false,
                    Message = "删除角色失败，请稍后重试。",
                    CharacterId = characterId
                };
            }
        }

        /// <summary>
        /// 获取用户角色数量
        /// </summary>
        public async Task<int> GetCharacterCountAsync(int gameId)
        {
            try
            {
                var count = await _gameCharacterContext.CountAsync(
                    c => c.UserId == UserId &&
                         c.GameId == gameId &&
                         !c.IsDeleted);
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取用户角色数量时发生异常: UserId={UserId}, GameId={GameId}",
                    UserId, gameId);
                return 0;
            }
        }

        /// <summary>
        /// 检查用户是否可以创建新角色
        /// </summary>
        public async Task<bool> CanCreateCharacterAsync(int gameId)
        {
            var count = await GetCharacterCountAsync(gameId);
            return count < MAX_CHARACTER_COUNT;
        }

        /// <summary>
        /// 验证角色名是否可用
        /// </summary>
        public async Task<bool> ValidateCharacterNameAsync(string characterName, int gameId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(characterName))
                    return false;

                var cleaned = characterName.Trim();
                if (cleaned.Length < 2 || cleaned.Length > 12)
                    return false;

                var exists = await _gameCharacterContext.QueryFirstOrDefaultAsync(
                    c => c.CharacterName == cleaned && c.GameId == gameId && !c.IsDeleted);

                return exists == null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证角色名时发生异常: {CharacterName}", characterName);
                return false;
            }
        }

        #region 私有辅助方法

        /// <summary>
        /// 验证并清理角色名
        /// </summary>
        private async Task<string> ValidateAndCleanCharacterName(string characterName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(characterName))
                    return null;

                var cleaned = characterName.Trim();

                if (cleaned.Length < 2 || cleaned.Length > 12)
                    return null;

                cleaned = cleaned.FilterSensitiveWords(sensitiveWords: null);

                if (string.IsNullOrWhiteSpace(cleaned))
                    return null;

                return cleaned;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证角色名时发生异常: {CharacterName}", characterName);
                return null;
            }
        }

        /// <summary>
        /// 设置默认出生位置
        /// </summary>
        private CharacterEntity SetDefaultStartingLocation(CharacterEntity character, Profession profession)
        {
            switch (profession)
            {
                case Profession.Shaolin:
                    character.MapId = 1001;
                    character.PositionX = 100.0f;
                    character.PositionY = 50.0f;
                    character.PositionZ = 200.0f;
                    break;
                case Profession.Wudang:
                    character.MapId = 1002;
                    character.PositionX = 150.0f;
                    character.PositionY = 80.0f;
                    character.PositionZ = 250.0f;
                    break;
                case Profession.Emei:
                    character.MapId = 1003;
                    character.PositionX = 120.0f;
                    character.PositionY = 60.0f;
                    character.PositionZ = 220.0f;
                    break;
                default:
                    character.MapId = 1000;
                    character.PositionX = 0.0f;
                    character.PositionY = 0.0f;
                    character.PositionZ = 0.0f;
                    break;
            }

            return character;
        }

        #endregion
    }
}
