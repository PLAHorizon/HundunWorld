using Orleans;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Horizon.Game.Message.Network;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 用户角色管理器Grain接口
    /// 负责管理单个用户的所有角色，包括创建、删除、查询等操作
    /// 使用用户ID作为Grain的键
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IUserCharacterManagerGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// 为用户创建新角色
        /// </summary>
        /// <param name="request">创建角色请求</param>
        /// <returns>创建角色响应</returns>
        Task<CreateCharacterResponse> CreateCharacterAsync(CreateCharacterRequest request);

        /// <summary>
        /// 获取用户所有角色列表
        /// </summary>
        /// <param name="gameId">游戏ID</param>
        /// <returns>角色列表</returns>
        Task<List<CharacterInfo>> GetAllCharactersAsync(int gameId);

        /// <summary>
        /// 删除角色
        /// </summary>
        /// <param name="characterId">角色ID</param>
        /// <param name="gameId">游戏ID</param>
        /// <returns>删除结果</returns>
        Task<DeleteCharacterResponse> DeleteCharacterAsync(ulong characterId, int gameId);

        /// <summary>
        /// 获取用户角色数量
        /// </summary>
        /// <param name="gameId">游戏ID</param>
        /// <returns>角色数量</returns>
        Task<int> GetCharacterCountAsync(int gameId);

        /// <summary>
        /// 检查用户是否可以创建新角色
        /// </summary>
        /// <param name="gameId">游戏ID</param>
        /// <returns>是否可以创建</returns>
        Task<bool> CanCreateCharacterAsync(int gameId);

        /// <summary>
        /// 验证角色名是否可用
        /// </summary>
        /// <param name="characterName">角色名</param>
        /// <param name="gameId">游戏ID</param>
        /// <returns>是否可用</returns>
        Task<bool> ValidateCharacterNameAsync(string characterName, int gameId);
    }
}
