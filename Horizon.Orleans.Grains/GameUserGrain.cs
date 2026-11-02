using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model;
using Horizon.Model.Basic;
using Horizon.Model.GameModel;
using Horizon.Orleans.Interface;
using Horizon.Share.Dtos.Games;
using Horizon.Share.Dtos.User;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 游戏用户 Grain：通行证 ↔ 游戏用户（Game_HunduShijie_User）↔ 角色（Game_HunduShijie_Character）
    /// 三层数据模型的读写实现，供 WebApi GameUserRoleController（登录链路）调用。
    /// <para>
    /// 映射关系（与 PassportGrain.HandleGameUserCreation / CharacterGrain 一致）：
    /// Basic_Sys_User.Id（Guid）→ Game_HunduShijie_User.GameUserId；
    /// Game_HunduShijie_User.Id（long，user_id）即客户端使用的 GameUserId；
    /// Game_HunduShijie_Character.user_id = Game_HunduShijie_User.Id。
    /// </para>
    /// </summary>
    public class GameUserGrain : Grain, IGameUserGrain
    {
        private readonly IDataContext<GameEntityContext, UserEntity, long> _gameUserContext;
        private readonly IDataContext<GameEntityContext, CharacterEntity, long> _gameCharacterContext;
        private readonly IDataContext<BasicEntityContext, User, Guid> _userdataContext;
        private readonly ILogger<GameUserGrain> _logger;

        public GameUserGrain(
            IDataContext<GameEntityContext, UserEntity, long> gameUserContext,
            IDataContext<GameEntityContext, CharacterEntity, long> gameCharacterContext,
            IDataContext<BasicEntityContext, User, Guid> userdataContext,
            ILogger<GameUserGrain> logger)
        {
            _gameUserContext = gameUserContext;
            _gameCharacterContext = gameCharacterContext;
            _userdataContext = userdataContext;
            _logger = logger;
        }

        // ============================================================
        // 查询 / 注册游戏用户
        // ============================================================

        public async Task<GameUserInfoDto> GetGameUserAsync(GameUserQeuryDto queryDto)
        {
            if (queryDto == null || string.IsNullOrWhiteSpace(queryDto.PassportId))
            {
                return null;
            }

            try
            {
                var user = await _userdataContext.QueryFirstOrDefaultAsync(
                    m => m.PassportId == queryDto.PassportId && m.IsValid);
                if (user == null)
                {
                    _logger.LogWarning("GameUser 查询：通行证不存在。PassportId={PassportId}", queryDto.PassportId);
                    return null;
                }

                var gameUser = await _gameUserContext.QueryFirstOrDefaultAsync(
                    m => m.GameUserId == user.Id &&
                         m.GameId == queryDto.GameId &&
                         m.IsValid && !m.IsDeleted);
                return gameUser == null ? null : ToDto(gameUser, queryDto.PassportId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GameUser 查询异常。PassportId={PassportId}", queryDto.PassportId);
                return null;
            }
        }

        public async Task<GameUserInfoDto> RegisterPassportAndGameUserAsync(GameRegisterDto registerDto, string passportId)
        {
            if (registerDto == null || string.IsNullOrWhiteSpace(passportId))
            {
                return null;
            }

            try
            {
                var user = await _userdataContext.QueryFirstOrDefaultAsync(
                    m => m.PassportId == passportId && m.IsValid);
                if (user == null)
                {
                    _logger.LogWarning("GameUser 注册：通行证不存在。PassportId={PassportId}", passportId);
                    return null;
                }

                // 已存在则复用（幂等），不重复创建
                var existing = await _gameUserContext.QueryFirstOrDefaultAsync(
                    m => m.GameUserId == user.Id &&
                         m.GameId == registerDto.GameId &&
                         m.IsValid && !m.IsDeleted);
                if (existing != null)
                {
                    return ToDto(existing, passportId);
                }

                var entity = new UserEntity
                {
                    GameUserId = user.Id,
                    GameId = registerDto.GameId,
                    ServerId = registerDto.ServerId,
                    AreaId = registerDto.AreaId,
                    AccountName = string.IsNullOrWhiteSpace(user.NickName) ? passportId : user.NickName,
                    Status = 0,
                    CreateTime = DateTime.Now,
                    LastLoginTime = DateTime.Now,
                    LastLoginIp = registerDto.Ip ?? string.Empty,
                    PlatformId = registerDto.PlatformId ?? string.Empty,
                    DeviceId = registerDto.PlatformId ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    Phone = user.Phone ?? string.Empty,
                    IsValid = true,
                    IsDeleted = false,
                };

                var created = await _gameUserContext.AddAsync(entity);
                _logger.LogInformation("GameUser 注册成功。PassportId={PassportId}, GameUserId={GameUserId}", passportId, created.Id);
                return ToDto(created, passportId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GameUser 注册异常。PassportId={PassportId}", passportId);
                return null;
            }
        }

        // ============================================================
        // 角色列表 / 角色信息
        // ============================================================

        public async Task<GameUserRoleInfoDto[]> GetGameUserRolesAsync(GameUserRoleQueryDto queryDto)
        {
            if (queryDto == null || queryDto.GameUserId <= 0)
            {
                return Array.Empty<GameUserRoleInfoDto>();
            }

            try
            {
                var characters = await _gameCharacterContext.QueryAsync(m =>
                    m.UserId == queryDto.GameUserId &&
                    m.GameId == queryDto.GameId &&
                    (queryDto.ServerId <= 0 || m.ServerId == queryDto.ServerId) &&
                    m.IsValid && !m.IsDeleted);

                var list = characters
                    .OrderByDescending(c => c.LastLoginTime)
                    .ThenByDescending(c => c.CreateTime)
                    .Select(c => new GameUserRoleInfoDto
                    {
                        UserRoleId = c.Id,
                        SceneName = c.MapId.ToString(),
                        IsLive = true,
                    })
                    .ToArray();

                _logger.LogInformation("GameUser 角色列表：GameUserId={GameUserId}, Count={Count}", queryDto.GameUserId, list.Length);
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GameUser 角色列表查询异常。GameUserId={GameUserId}", queryDto.GameUserId);
                return Array.Empty<GameUserRoleInfoDto>();
            }
        }

        public async Task<GameRoleNormalDto[]> GetGameRolesAsync(GameRoleQueryDto queryDto)
        {
            // 基础角色模板目录：登录闭环不依赖，返回空目录（由角色系统迭代补全）。
            await Task.CompletedTask;
            return Array.Empty<GameRoleNormalDto>();
        }

        public async Task<GameRoleWorldInfoDto> GetGameRoleWorldInfoAsync(GameUserRoleQueryDto queryDto)
        {
            if (queryDto == null || !queryDto.UserRoleId.HasValue || queryDto.UserRoleId.Value <= 0)
            {
                return null;
            }

            try
            {
                var character = await _gameCharacterContext.QueryFirstOrDefaultAsync(m =>
                    m.Id == queryDto.UserRoleId.Value &&
                    m.UserId == queryDto.GameUserId &&
                    m.IsValid && !m.IsDeleted);
                if (character == null)
                {
                    return null;
                }

                return new GameRoleWorldInfoDto
                {
                    UserRoleId = character.Id,
                    GameId = character.GameId,
                    AreaId = character.AreaId,
                    ServerId = character.ServerId,
                    GameUserId = character.UserId,
                    RoleNickName = character.CharacterName,
                    X = (decimal)character.PositionX,
                    Y = (decimal)character.PositionY,
                    Z = (decimal)character.PositionZ,
                    W = (decimal)character.Rotation,
                    IsLive = true,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GameUser 角色世界信息查询异常。UserRoleId={UserRoleId}", queryDto.UserRoleId);
                return null;
            }
        }

        public async Task<bool> SetGameRoleWorldInfoAsync(GameRoleWorldInfoDto roleDto)
        {
            if (roleDto == null || roleDto.UserRoleId <= 0)
            {
                return false;
            }

            try
            {
                var character = await _gameCharacterContext.QueryFirstOrDefaultAsync(
                    m => m.Id == roleDto.UserRoleId && m.IsValid && !m.IsDeleted, isTracking: true);
                if (character == null)
                {
                    return false;
                }

                character.PositionX = (float)roleDto.X;
                character.PositionY = (float)roleDto.Y;
                character.PositionZ = (float)roleDto.Z;
                character.Rotation = (float)roleDto.W;
                return await _gameCharacterContext.UpdateAsync(character, character.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GameUser 角色世界信息保存异常。UserRoleId={UserRoleId}", roleDto.UserRoleId);
                return false;
            }
        }

        public async Task<bool> CreateGameRoleAsync(GameRoleDto roleDto)
        {
            if (roleDto == null || roleDto.GameUserId <= 0 || string.IsNullOrWhiteSpace(roleDto.RoleNickName))
            {
                return false;
            }

            try
            {
                // 角色名唯一性（同游戏内全局唯一，与 CharacterGrain 约定一致）
                var nameExists = await _gameCharacterContext.QueryFirstOrDefaultAsync(m =>
                    m.CharacterName == roleDto.RoleNickName &&
                    m.GameId == roleDto.GameId &&
                    m.IsValid && !m.IsDeleted);
                if (nameExists != null)
                {
                    _logger.LogWarning("GameUser 创建角色：角色名已存在。Name={Name}", roleDto.RoleNickName);
                    return false;
                }

                // 角色数量上限（与 CharacterGrain.CreateCharacterAsync 一致：5 个）
                var characters = await _gameCharacterContext.QueryAsync(m =>
                    m.UserId == roleDto.GameUserId &&
                    m.GameId == roleDto.GameId &&
                    m.IsValid && !m.IsDeleted);
                if (characters.Count() >= 5)
                {
                    _logger.LogWarning("GameUser 创建角色：角色数量已达上限。GameUserId={GameUserId}", roleDto.GameUserId);
                    return false;
                }

                var gameUser = await _gameUserContext.QueryFirstOrDefaultAsync(
                    m => m.Id == roleDto.GameUserId && m.IsValid && !m.IsDeleted);
                if (gameUser == null)
                {
                    _logger.LogWarning("GameUser 创建角色：游戏用户不存在。GameUserId={GameUserId}", roleDto.GameUserId);
                    return false;
                }

                var entity = new CharacterEntity
                {
                    UserId = gameUser.Id,
                    GameUserId = gameUser.Id,
                    GameId = roleDto.GameId,
                    AreaId = roleDto.AreaId,
                    ServerId = roleDto.ServerId,
                    CharacterName = roleDto.RoleNickName,
                    Level = 1,
                    Experience = 0,
                    Profession = 0,
                    Gender = 0,
                    Sect = 0,
                    Faction = 0,
                    RealmLevel = 0,
                    CombatPower = 0,
                    // 建号默认跟着基值走：写 100 会让服务端与客户端（1000 起算）从第一秒就不是同一个血池
                    Health = Horizon.Game.Core.Combat.AttributeDerivation.HealthBaseValue,
                    MaxHealth = Horizon.Game.Core.Combat.AttributeDerivation.HealthBaseValue,
                    AttackPower = 10f,
                    Defense = 5f,
                    WuxingElement = 0,
                    MapId = 1,
                    PositionX = 0f,
                    PositionY = 0f,
                    PositionZ = 0f,
                    Rotation = 0f,
                    CreateTime = DateTime.Now,
                    LastLoginTime = DateTime.Now,
                    IsValid = true,
                    IsDeleted = false,
                };

                var created = await _gameCharacterContext.AddAsync(entity);
                _logger.LogInformation("GameUser 创建角色成功。Name={Name}, UserRoleId={UserRoleId}", created.CharacterName, created.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GameUser 创建角色异常。Name={Name}", roleDto.RoleNickName);
                return false;
            }
        }

        // ============================================================
        // 辅助
        // ============================================================

        private static GameUserInfoDto ToDto(UserEntity entity, string passportId)
        {
            return new GameUserInfoDto
            {
                PassportId = passportId,
                GameUserId = entity.Id,
                GameId = entity.GameId,
            };
        }
    }
}
