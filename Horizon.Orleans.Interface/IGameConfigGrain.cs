using Horizon.Share.Dtos.Games;
using Orleans;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Orleans.Interface
{
    /// <summary>
    /// 游戏配置Grain接口
    /// </summary>
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IGameConfigGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 获取指定游戏的服务器列表
        /// </summary>
        Task<GameServersDto> GetGameServersAsync(int gameId);

        /// <summary>
        /// 添加游戏
        /// </summary>
        Task<bool> AddGameAsync(GameServersDto dto);

        /// <summary>
        /// 添加游戏服
        /// </summary>
        Task<bool> AddServerAsync(ServerDto dto);

        /// <summary>
        /// 获取游戏列表
        /// </summary>
        Task<List<GameInfoDto>> GetGamesAsync();

        /// <summary>
        /// 获取游戏详情
        /// </summary>
        Task<GameInfoDto> GetGameAsync(int gameId);

        /// <summary>
        /// 更新游戏
        /// </summary>
        Task<bool> UpdateGameAsync(int gameId, GameInfoDto dto);

        /// <summary>
        /// 删除游戏
        /// </summary>
        Task<bool> DeleteGameAsync(int gameId);
    }
}
