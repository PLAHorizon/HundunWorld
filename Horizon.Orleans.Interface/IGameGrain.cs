using Horizon.Game.Message;
using Horizon.Game.Message.Network;
using Horizon.Share.Dtos.Games;

namespace Horizon.Orleans.Interface
{
    [global::Orleans.CodeGeneration.Version(1)]
    public interface IGameGrain:IGrainWithGuidKey
    {
        /// <summary>
        /// 获取游戏服务器列表
        /// </summary>
        /// <param name="gameQueryDto">游戏查询数据传输对象</param>
        /// <returns>服务器信息列表</returns>
        Task<List<ServerInfo>> GetServerListAsync(GameQueryDto gameQueryDto);
    }
    
}