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
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.WebApi.Controllers.Games
{
    /// <summary>
    /// 游戏用户角色
    /// </summary>
    [ApiGroup(ApiGroupName.Games)]
    [ApiController]
    [Route("[controller]")]
    public class GameUserRoleController : OrleansControllerBase
    {
        private readonly ILogger<GameUserRoleController> _logger;
        private readonly IPassportCurrentUser _passportCurrent;
        public GameUserRoleController(IOptions<AdoNetOptions> options,
                                IOptions<ClusterOptions> clusterOptions,
                                ILogger<GameUserRoleController> logger,
                                IPassportCurrentUser passportCurrent)
                                : base(options, clusterOptions, logger)
        {
            _logger = logger;
            _passportCurrent = passportCurrent;
        }

        /// <summary>
        /// 获取游戏用户信息
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<ResultVM<GameUserInfoDto>> GameRolesAsync(GameRegisterDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var resutl = await passport.RegisterPassportAndGameUserAsync(queryDto);
                    
                    return new ResultVM<GameUserInfoDto> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                }


            }
            catch (Exception ex)
            {
                return new ResultVM<GameUserInfoDto> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }
        /// <summary>
        /// 获取游戏用户信息
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("gameuser")]
        public async Task<ResultVM<GameUserInfoDto>> GameRolesAsync(GameUserQeuryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var resutl = await passport.GetGameUserAsync(queryDto);
                    
                    return new ResultVM<GameUserInfoDto> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                }


            }
            catch (Exception ex)
            {
                return new ResultVM<GameUserInfoDto> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }
        /// <summary>
        /// 获取游戏内的角色
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("gameroles")]
        public async Task<ResultVM<GameRoleNormalDto[]>> GameRolesAsync(GameRoleQueryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var resutl = await passport.GetGameUserRolesAsync(queryDto);
                    
                    return new ResultVM<GameRoleNormalDto[]> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                }


            }
            catch (Exception ex)
            {
                return new ResultVM<GameRoleNormalDto[]> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }
        /// <summary>
        /// 获取游戏用户的角色列表
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("gameuserroles")]
        public async Task<ResultVM<GameUserRoleInfoDto[]>> GameUserRolesAsync(GameUserRoleQueryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var resutl = await passport.GetGameUserRolesAsync(queryDto);
                    
                    return new ResultVM<GameUserRoleInfoDto[]> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<GameUserRoleInfoDto[]> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }

        /// <summary>
        /// 获取游戏用户的角色
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("getgamerole")]
        public async Task<ResultVM<GameRoleWorldInfoDto>> GameUserRoleAsync(GameUserRoleQueryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var resutl = await passport.GetGameRoleWorldInfoAsync(queryDto);
                    
                    return new ResultVM<GameRoleWorldInfoDto> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<GameRoleWorldInfoDto> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }
        /// <summary>
        /// 设置游戏用户的角色存档信息
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("setgamerole")]
        public async Task<ResultVM<bool>> SetGameUserRoleAsync(GameRoleWorldInfoDto roleDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var resutl = await passport.SetGameRoleWorldInfoAsync(roleDto);
                    
                    return new ResultVM<bool> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }

        /// <summary>
        /// 游戏用户创建角色
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("createroles")]
        public async Task<ResultVM<bool>> CreateGameUserRolesAsync(GameRoleDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameUserGrain passport = client.GetGrain<IGameUserGrain>(Guid.NewGuid());
                    var resutl = await passport.CreateGameRoleAsync(queryDto);
                    
                    return new ResultVM<bool> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<bool> { Data = false, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }
        /// <summary>
        /// 获取游戏角色指定穿戴的装备信息
        /// </summary>
        /// <param name="queryDto"></param>
        /// <returns></returns>
        [Authorize]
        [HttpPost("roleequipments")]
        public async Task<ResultVM<List<EquipmentInfoDto>>> GetRoleEquipmentInfoAsync(RoleEquipmentInfoQueryDto queryDto)
        {
            try
            {
                var client = await OrleansConnectClient();
                {
                    IGameEquipmentGrain passport = client.GetGrain<IGameEquipmentGrain>(Guid.NewGuid());
                    var resutl = await passport.GetRoleEquipmentInfo(queryDto);
                    
                    return new ResultVM<List<EquipmentInfoDto>> { Data = resutl, IsSuccess = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                return new ResultVM<List<EquipmentInfoDto>> { Data = null, IsSuccess = false, ErrorMessage = ex.Message };
            }

        }
    }
}
