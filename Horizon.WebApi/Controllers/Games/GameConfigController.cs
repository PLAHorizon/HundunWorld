using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Core.Options;
using Horizon.Share.Dtos.Games;
using Horizon.Share.Dtos.User;
using Horizon.Share.VMs;
using Horizon.WebApi.Configs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers.Games
{
    [ApiGroup(ApiGroupName.Games)]
    [ApiController]
    [Route("[controller]")]
    public class GameConfigController : OrleansControllerBase
    {
        private readonly ILogger<GameConfigController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;
        public GameConfigController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<GameConfigController> logger,
                                IClusterClient clusterClient,
                                IPassportCurrentUser passportCurrent)
                                : base(options, clusterOptions, logger, clusterClient)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        /// <summary>
        /// 获取指定游戏的服务器列表
        /// </summary>
        /// <param name="gameId"></param>
        /// <returns></returns>
        [Authorize]
        [HttpGet("servers")]
        public async Task<ResultVM<GameServersDto>> GameServers(int gameId)
        {
            try
            {
                    var client = await OrleansConnectClient();
                    {
                        IGameConfigGrain passport = client.GetGrain<IGameConfigGrain>(Guid.NewGuid());
                        var resutl = await passport.GetGameServersAsync(gameId);
                        
                        return new ResultVM<GameServersDto> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                    }
               

            }
            catch (Exception ex)
            {
                return new ResultVM<GameServersDto> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }

        /// <summary>
        /// 添加游戏
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("AddGame")]
        public async Task<ResultVM<bool>> AddGameAsync(GameServersDto dto)
        {
            try
            {
                if (_passportCurrent.PassportType == PassportType.System)
                {
                    var client = await OrleansConnectClient();
                    {
                        IGameConfigGrain passport = client.GetGrain<IGameConfigGrain>(Guid.NewGuid());
                        var resutl = await passport.AddGameAsync(dto);
                        
                        return new ResultVM<bool> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                    }
                }
                else return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = "操作失败，权限不足！" };
            }
            catch (Exception ex)
            {
                return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 添加游戏服
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("AddServer")]
        public async Task<ResultVM<bool>> AddServerAsync(ServerDto dto)
        {
            try
            {
                if (_passportCurrent.PassportType == PassportType.System)
                {
                    var client = await OrleansConnectClient();
                    {
                        IGameConfigGrain passport = client.GetGrain<IGameConfigGrain>(Guid.NewGuid());
                        var resutl = await passport.AddServerAsync(dto);
                        
                        return new ResultVM<bool> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                    }
                }
                else return new ResultVM<bool> { Data = true, IsSuccess = false, ErrorMessage = "操作失败，权限不足！" };
            }
            catch (Exception ex)
            {
                return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 获取游戏列表
        /// </summary>
        /// <returns></returns>
        [Authorize]
        [HttpGet("games")]
        public async Task<ResultVM<List<GameInfoDto>>> GetGamesAsync()
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameConfigGrain grain = client.GetGrain<IGameConfigGrain>(Guid.NewGuid());
                    var result = await grain.GetGamesAsync();
                    return new ResultVM<List<GameInfoDto>> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<List<GameInfoDto>> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 获取游戏详情
        /// </summary>
        /// <param name="gameId"></param>
        /// <returns></returns>
        [Authorize]
        [HttpGet("games/{gameId}")]
        public async Task<ResultVM<GameInfoDto>> GetGameAsync(int gameId)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameConfigGrain grain = client.GetGrain<IGameConfigGrain>(Guid.NewGuid());
                    var result = await grain.GetGameAsync(gameId);
                    return new ResultVM<GameInfoDto> { Data = result, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<GameInfoDto> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 更新游戏
        /// </summary>
        /// <param name="gameId"></param>
        /// <param name="dto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPut("games/{gameId}")]
        public async Task<ResultVM<bool>> UpdateGameAsync(int gameId, GameInfoDto dto)
        {
            try
            {
                if (_passportCurrent.PassportType == PassportType.System)
                {
                    var client = await OrleansConnectClient();
                    {
                        IGameConfigGrain grain = client.GetGrain<IGameConfigGrain>(Guid.NewGuid());
                        var result = await grain.UpdateGameAsync(gameId, dto);
                        return new ResultVM<bool> { Data = result, IsSuccess = true, ErrorMessage = null };
                    }
                }
                else return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = "操作失败，权限不足！" };
            }
            catch (Exception ex)
            {
                return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// 删除游戏
        /// </summary>
        /// <param name="gameId"></param>
        /// <returns></returns>
        [Authorize]
        [HttpDelete("games/{gameId}")]
        public async Task<ResultVM<bool>> DeleteGameAsync(int gameId)
        {
            try
            {
                if (_passportCurrent.PassportType == PassportType.System)
                {
                    var client = await OrleansConnectClient();
                    {
                        IGameConfigGrain grain = client.GetGrain<IGameConfigGrain>(Guid.NewGuid());
                        var result = await grain.DeleteGameAsync(gameId);
                        return new ResultVM<bool> { Data = result, IsSuccess = true, ErrorMessage = null };
                    }
                }
                else return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = "操作失败，权限不足！" };
            }
            catch (Exception ex)
            {
                return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = ex.Message };
            }
        }
    }
}
